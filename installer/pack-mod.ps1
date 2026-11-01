<#
.SYNOPSIS
  Packs mods (a setting, an engine pack, an add-on) into zips for download: artifacts\installer\StreetCorsa-<id>-<version>.zip

.DESCRIPTION
  Each mod folder (one with a mod.json) goes into its own zip, laid out as the game's Content folder has it: the
  player unzips it into the Content folder the New Game screen opens (%LocalAppData%\Programs\Street Corsa\Content)
  and the mod is there, in its group (settings\<setting>, engines\<engine pack>, addons\<add-on>).

  What it checks, stopping the pack on any of them:
  - the mod.json reads, has an id, and is not the core;
  - its "gameVersion", when given, is no later than this core's <Version> (Street Corsa.csproj); with none, the zip's
    mod.json is stamped with this core's version, so an older game says what is missing instead of failing on it;
  - a licence file (LICENSE*, CONTENT-LICENSE*, COPYING*) at the mod's root, only warned of when missing;
  - an engine sound bank ships only beside a sound.json whose "source" says it is Street Corsa's own (other people's
    banks never ship).

  Work files (Python, Blender, backups, logs, caches) are left out, and so is anything switched off ('_' or '.').
  A mod it requires that is not packed with it is only told: it is the player's to install too.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File installer\pack-mod.ps1 -Mod "D:\mods\settings\mysetting;D:\mods\engines\myengine"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Mod,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts\installer' }
$game = Join-Path $root 'Street Corsa\Street Corsa.csproj'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$coreVersionText = ([xml](Get-Content -LiteralPath $game -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $coreVersionText) { throw "No <Version> in $game" }

# A version as ModVersion reads it: up to three numbers with dots, whatever follows ignored, missing parts 0
function ConvertTo-ModVersion([string]$text) {
    if (-not $text) { return $null }
    $m = [regex]::Match($text.Trim().TrimStart('v', 'V'), '^(\d+)(?:\.(\d+))?(?:\.(\d+))?')
    if (-not $m.Success) { return $null }
    $parts = 1..3 | ForEach-Object { if ($m.Groups[$_].Success) { [int]$m.Groups[$_].Value } else { 0 } }
    return [version]::new($parts[0], $parts[1], $parts[2])
}
$coreVersion = ConvertTo-ModVersion $coreVersionText

# mod.json as the game reads it (comments and trailing commas allowed) made plain JSON for ConvertFrom-Json
function ConvertFrom-ModJson([string]$text) {
    $out = [Text.StringBuilder]::new($text.Length)
    $i = 0
    while ($i -lt $text.Length) {
        $c = $text[$i]
        if ($c -eq '"') {
            $start = $i
            $i++
            while ($i -lt $text.Length -and $text[$i] -ne '"') { if ($text[$i] -eq '\') { $i++ }; $i++ }
            [void]$out.Append($text, $start, [Math]::Min($i + 1, $text.Length) - $start)
            $i++
        }
        elseif ($c -eq '/' -and $i + 1 -lt $text.Length -and $text[$i + 1] -eq '/') {
            while ($i -lt $text.Length -and $text[$i] -ne "`n") { $i++ }
        }
        elseif ($c -eq '/' -and $i + 1 -lt $text.Length -and $text[$i + 1] -eq '*') {
            $end = $text.IndexOf('*/', $i + 2)
            $i = $(if ($end -lt 0) { $text.Length } else { $end + 2 })
        }
        else { [void]$out.Append($c); $i++ }
    }
    $plain = [regex]::Replace($out.ToString(), ',(\s*[}\]])', '$1')
    return $plain | ConvertFrom-Json
}

# Left out of every zip: what the tools that made a mod leave beside it
$workFiles = @('*.py', '*.pyc', '*.blend', '*.blend1', '*.old', '*.bak', '*.tmp', '*.log', '*.orig', 'Thumbs.db', 'desktop.ini')
function Test-WorkFile([string]$relative) {
    $segments = $relative -split '[\\/]'
    if ($segments | Where-Object { $_.StartsWith('_') -or $_.StartsWith('.') -or $_ -eq '__pycache__' }) { return $true }
    $leaf = $segments[-1]
    return [bool]($workFiles | Where-Object { $leaf -like $_ })
}

# powershell -File hands a list over as one string: several folders are then given with ';' between them
$folders = @($Mod | ForEach-Object { $_ -split ';' } | Where-Object { $_.Trim() } | ForEach-Object { (Resolve-Path -LiteralPath $_.Trim()).ProviderPath.TrimEnd('\') })
$manifests = @{}
foreach ($folder in $folders) {
    $file = Join-Path $folder 'mod.json'
    if (-not (Test-Path -LiteralPath $file)) { throw "$folder has no mod.json: not a mod" }
    try { $manifests[$folder] = ConvertFrom-ModJson ([IO.File]::ReadAllText($file, [Text.Encoding]::UTF8)) }
    catch { throw "$file does not read: $($_.Exception.Message)" }
}
$packedIds = @($manifests.Values | ForEach-Object { $_.id })

if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

foreach ($folder in $folders) {
    $info = $manifests[$folder]
    $name = Split-Path -Leaf $folder
    $id = [string]$info.id
    if (-not $id) { throw "$name\mod.json has no id" }
    if ($id -eq 'streetcorsa') { throw "$name is the core: it ships in the MSI, not as a mod" }
    if ($name -ne $id) { Write-Warning "$name\mod.json says id `"$id`": the folder keeps its own name in the zip" }

    # Where the game's Content keeps it: a setting under settings, the rest in the group it sits in on disk (engines,
    # addons), else addons
    $group = if ($info.type -eq 'setting') { 'settings' } else {
        $parent = Split-Path -Leaf (Split-Path -Parent $folder)
        if ($parent -in @('engines', 'addons')) { $parent } else { 'addons' }
    }

    $stamp = $null
    if ($info.gameVersion) {
        $wanted = ConvertTo-ModVersion ([string]$info.gameVersion)
        if (-not $wanted) { throw "$name\mod.json: `"gameVersion`": `"$($info.gameVersion)`" is no version" }
        if ($wanted -gt $coreVersion) { throw "$name needs the game at $($info.gameVersion), this core is $coreVersionText" }
    }
    else { $stamp = $coreVersionText }

    if ($info.requires) {
        foreach ($needed in $info.requires.PSObject.Properties.Name) {
            if ($needed -ne 'streetcorsa' -and $packedIds -notcontains $needed) {
                Write-Warning "$name requires the mod `"$needed`", not packed with it: the player installs it too"
            }
        }
    }

    # A mod's terms travel with it: a LICENSE, CONTENT-LICENSE or COPYING file at its root. None is only told, since
    # the terms are the mod author's to give, not this script's
    if (-not (Get-ChildItem -LiteralPath $folder -File -Force | Where-Object { $_.Name -match '^(CONTENT-)?(LICEN[CS]E|COPYING)' })) {
        Write-Warning "$name has no LICENSE or CONTENT-LICENSE file at its root: the zip goes out with no terms"
    }

    $files = @(Get-ChildItem -LiteralPath $folder -Recurse -File -Force | ForEach-Object {
        [pscustomobject]@{ Full = $_.FullName; Relative = $_.FullName.Substring($folder.Length + 1) }
    } | Where-Object { -not (Test-WorkFile $_.Relative) })

    foreach ($bank in @($files | Where-Object { $_.Relative -like '*.bank' })) {
        $soundJson = Join-Path (Split-Path -Parent $bank.Full) 'sound.json'
        $source = if (Test-Path -LiteralPath $soundJson) { [string](ConvertFrom-ModJson ([IO.File]::ReadAllText($soundJson, [Text.Encoding]::UTF8))).source }
        if (-not $source -or -not $source.StartsWith('Street Corsa')) {
            throw "$name\$($bank.Relative): a sound bank whose sound.json does not say it is Street Corsa's own would ship"
        }
    }

    $version = if ($info.version) { [string]$info.version } else { '0' }
    $zipPath = Join-Path $OutDir "StreetCorsa-$id-$version.zip"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            # Forward slashes, as zips are to have them (Windows PowerShell's Compress-Archive writes backslashes)
            $entryName = "$group/$name/" + ($file.Relative -replace '\\', '/')
            $level = if ($file.Relative -match '\.(ogg|mp3|jpg|png|bank|zip)$') { [IO.Compression.CompressionLevel]::NoCompression } else { [IO.Compression.CompressionLevel]::Optimal }
            if ($stamp -and $file.Relative -eq 'mod.json') {
                # The version stamped in after the opening brace: the rest of the file (its comments) as it was
                $text = [IO.File]::ReadAllText($file.Full, [Text.Encoding]::UTF8)
                $brace = $text.IndexOf('{')
                $text = $text.Substring(0, $brace + 1) + "`r`n  `"gameVersion`": `"$stamp`"," + $text.Substring($brace + 1)
                $entry = $zip.CreateEntry($entryName, $level)
                $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
                try { $writer.Write($text) } finally { $writer.Dispose() }
            }
            else {
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.Full, $entryName, $level)
            }
        }
    }
    finally { $zip.Dispose() }

    $size = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB)
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $stamped = if ($stamp) { ", gameVersion $stamp stamped" } else { '' }
    Write-Host "Done: $zipPath ($($files.Count) files, $size MB$stamped)"
    Write-Host "  SHA-256 $hash"
}
