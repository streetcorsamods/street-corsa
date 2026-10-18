<#
.SYNOPSIS
  Builds the Street Corsa installer: artifacts\installer\StreetCorsa-<version>.msi

.DESCRIPTION
  1. Publishes the game self-contained for win-x64 (players need no .NET of their own).
  2. Stages it with only the content the repository has: Content\ on disk also holds what is local-only (the garage's
     model, the station's model, the private mods, the intro music; see .gitignore), which must never ship.
  3. Builds installer\StreetCorsa.Installer.wixproj over the stage (WiX 6, restored from NuGet).

  The version is the game's <Version> in Street Corsa.csproj.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
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

Push-Location $root
try {
    $dirty = git status --porcelain
    if ($dirty) { Write-Warning "The working tree has changes: the installer is built from them as they are" }

    # The engine sounds are in Git LFS: a clone without them pulled has pointer files where the banks should be
    $content = @(git -c core.quotepath=off ls-files -z -- 'Street Corsa/Content' | ForEach-Object { $_ -split "`0" } | Where-Object { $_ })
    if ($content.Count -eq 0) { throw "git ls-files found no content under Street Corsa/Content" }
    foreach ($bank in $content | Where-Object { $_ -like '*.bank' }) {
        $head = Get-Content -LiteralPath (Join-Path $root $bank) -TotalCount 1 -ErrorAction SilentlyContinue
        if ($head -like 'version https://git-lfs*') { throw "$bank is a Git LFS pointer: run 'git lfs pull' first" }
    }

    foreach ($dir in $publish, $stage) {
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    }

    Write-Host "Publishing the game"
    dotnet publish $game -c $Configuration -r win-x64 --self-contained true -p:SatelliteResourceLanguages=en -o $publish -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    Write-Host "Staging $($content.Count) content files"
    robocopy $publish $stage /E /XD (Join-Path $publish 'Content') /XF *.pdb /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
    $prefix = 'Street Corsa/'
    foreach ($file in $content) {
        $target = Join-Path $stage ($file.Substring($prefix.Length) -replace '/', '\')
        $folder = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
    }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination $stage
    Copy-Item -Path (Join-Path $root 'lib\actools\LICENSE-*.txt') -Destination $stage

    Write-Host "Building the MSI"
    # As environment variables, which MSBuild reads as properties: no quoting of a path with spaces to get wrong
    $env:StageDir = $stage
    $env:ProductVersion = $version
    dotnet build (Join-Path $PSScriptRoot 'StreetCorsa.Installer.wixproj') -c $Configuration -nologo
    if ($LASTEXITCODE -ne 0) { throw "The installer build failed" }

    $msi = Join-Path $PSScriptRoot "bin\x64\$Configuration\StreetCorsa-$version.msi"
    if (-not (Test-Path -LiteralPath $msi)) { $msi = Get-ChildItem -Path (Join-Path $PSScriptRoot 'bin') -Recurse -Filter "StreetCorsa-$version.msi" | Select-Object -First 1 -ExpandProperty FullName }
    Copy-Item -LiteralPath $msi -Destination $out -Force
    $size = [math]::Round((Get-Item -LiteralPath (Join-Path $out (Split-Path -Leaf $msi))).Length / 1MB)
    Write-Host "Done: $(Join-Path $out (Split-Path -Leaf $msi)) ($size MB)"
}
finally {
    Pop-Location
}
