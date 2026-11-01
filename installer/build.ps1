<#
.SYNOPSIS
  Builds the Street Corsa installer: artifacts\installer\StreetCorsa-<version>.msi

.DESCRIPTION
  1. Writes the wizard's licence page, license.rtf, from license.txt.
  2. Publishes the game self-contained for win-x64 (players need no .NET of their own), without its Content folder.
  3. Stages it with only the content the repository has: Content\ on disk also holds what is local-only (the garage's
     model, the station's model, the private mods; see .gitignore), which must never ship.
  4. Builds installer\StreetCorsa.Installer.wixproj over the stage (WiX 6, restored from NuGet).
  5. With -Mods, packs each of those mod folders (a setting, an engine pack) into its own zip beside the MSI
     (installer\pack-mod.ps1): the downloads that go on top of the core.

  -Step splits it for code signing (SignPath): Stage does 1-3 and stops, leaving the game's own files in
  artifacts\installer\stage to be signed in place (Street Corsa.exe and Street Corsa.dll); Pack does 4-5 over that
  stage as it is, and the MSI it writes is signed after. All, the default, does both in one go, unsigned.

  The version is the game's <Version> in Street Corsa.csproj.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    # Mod folders to pack beside the MSI (from wherever they are kept, never the repository), ';' between them
    [string[]]$Mods,
    # All: the whole build. Stage: publish and stage only. Pack: the MSI over the stage a Stage run left
    [ValidateSet('All', 'Stage', 'Pack')]
    [string]$Step = 'All'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts\installer'
$publish = Join-Path $out 'publish'
$stage = Join-Path $out 'stage'
$game = Join-Path $root 'Street Corsa\Street Corsa.csproj'

$version = ([xml](Get-Content -LiteralPath $game -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $game" }
Write-Host "Street Corsa $version"

# license.txt as RTF, the only text the licence page shows: a paragraph per blank-line block, the headings in bold
function Write-LicenseRtf {
    $text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'license.txt'), [Text.Encoding]::UTF8) -replace "`r`n", "`n"
    $headings = @('Street Corsa', 'MIT License', 'Third-party software')
    $body = foreach ($paragraph in ($text.Trim() -split "`n`n")) {
        $line = $paragraph.Replace("`n", ' ').Trim()
        $escaped = -join ($line.ToCharArray() | ForEach-Object {
            if ('\{}'.Contains([string]$_)) { '\' + $_ }
            elseif ([int]$_ -gt 127) { '\u{0}?' -f [int]$_ }
            else { [string]$_ }
        })
        $(if ($headings -ccontains $line) { "\b $escaped\b0" } else { $escaped }) + '\par\par'
    }
    $rtf = "{\rtf1\ansi\deff0{\fonttbl{\f0 Tahoma;}}\viewkind4\f0\fs16`r`n" + ($body -join "`r`n") + "`r`n}`r`n"
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'license.rtf'), $rtf, [Text.Encoding]::ASCII)
}

# What the build sets for itself, put back as the caller had it when it ends: the console's encoding (git's file
# names come through it, and Windows PowerShell reads them in the OEM code page, so a non-ASCII name would come out
# wrong) and the two variables the wixproj reads
$callerEncoding = [Console]::OutputEncoding
$callerStageDir = $env:StageDir
$callerProductVersion = $env:ProductVersion

Push-Location $root
try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    Write-LicenseRtf

    if ($Step -ne 'Pack') {
        $dirty = git status --porcelain
        if ($dirty) { Write-Warning "The working tree has changes: the installer is built from them as they are" }

        # No engine sound bank ships (they are other people's; the game harvests the installed cars' banks): one tracked
        # by mistake stops the build
        $content = @(git -c core.quotepath=off ls-files -z -- 'Street Corsa/Content' | ForEach-Object { $_ -split "`0" } | Where-Object { $_ })
        if ($content.Count -eq 0) { throw "git ls-files found no content under Street Corsa/Content" }
        $banks = @($content | Where-Object { $_ -like '*.bank' })
        if ($banks.Count -gt 0) { throw "An engine sound bank is tracked and would ship: $($banks -join ', ')" }

        foreach ($dir in $publish, $stage) {
            if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        }

        Write-Host "Publishing the game"
        # CopyContent=false: the Content folder on disk is not copied only to be thrown away; the tracked files go in below
        dotnet publish $game -c $Configuration -r win-x64 --self-contained true -p:SatelliteResourceLanguages=en -p:CopyContent=false -o $publish -nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
        if (Test-Path -LiteralPath (Join-Path $publish 'Content')) { throw "The publish has a Content folder: the local-only content would ship" }

        Write-Host "Staging $($content.Count) content files"
        robocopy $publish $stage /E /XF *.pdb /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
        $prefix = 'Street Corsa/'
        foreach ($file in $content) {
            $target = Join-Path $stage ($file.Substring($prefix.Length) -replace '/', '\')
            $folder = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }
            Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
        }
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination $stage
        Copy-Item -LiteralPath (Join-Path $root 'CONTENT-LICENSE.txt') -Destination $stage
        Copy-Item -Path (Join-Path $root 'lib\actools\LICENSE-*.txt') -Destination $stage
    }
    if ($Step -eq 'Stage') { Write-Host "Staged: $stage (sign Street Corsa.exe and Street Corsa.dll there, then -Step Pack)"; return }
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'Street Corsa.exe'))) { throw "No stage to pack: run -Step Stage first" }

    Write-Host "Building the MSI"
    # As environment variables, which MSBuild reads as properties: no quoting of a path with spaces to get wrong
    $env:StageDir = $stage
    $env:ProductVersion = $version
    $wixproj = Join-Path $PSScriptRoot 'StreetCorsa.Installer.wixproj'
    dotnet build $wixproj -c $Configuration -nologo
    if ($LASTEXITCODE -ne 0) { throw "The installer build failed" }

    # Where the project put it, as MSBuild has it: never an older MSI of the same name left in another output folder
    $msi = (dotnet msbuild $wixproj -getProperty:TargetPath -p:Configuration=$Configuration -nologo | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $msi -or -not (Test-Path -LiteralPath $msi)) { throw "The built MSI was not found ($msi)" }
    Copy-Item -LiteralPath $msi -Destination $out -Force
    $size = [math]::Round((Get-Item -LiteralPath (Join-Path $out (Split-Path -Leaf $msi))).Length / 1MB)
    Write-Host "Done: $(Join-Path $out (Split-Path -Leaf $msi)) ($size MB)"

    if ($Mods) { & (Join-Path $PSScriptRoot 'pack-mod.ps1') -Mod $Mods -OutDir $out }
}
finally {
    # Set at the process's scope above: left there, a later dotnet build of the wixproj in this console would read a stale
    # stage, so each goes back to what the caller had (none: it goes)
    if ($null -eq $callerStageDir) { Remove-Item Env:\StageDir -ErrorAction SilentlyContinue } else { $env:StageDir = $callerStageDir }
    if ($null -eq $callerProductVersion) { Remove-Item Env:\ProductVersion -ErrorAction SilentlyContinue } else { $env:ProductVersion = $callerProductVersion }
    [Console]::OutputEncoding = $callerEncoding
    Pop-Location
}
