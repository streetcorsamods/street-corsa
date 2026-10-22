<#
.SYNOPSIS
  Opens Windows Sandbox, a clean Windows thrown away when it closes, with the built MSI on its desktop: to walk the
  wizard and start the game on a machine that never had it, by hand (CI's smoke.ps1 only installs quietly).
.DESCRIPTION
  Writes artifacts\installer\sandbox.wsb and opens it. Mapped into the sandbox:
  - artifacts\installer as Desktop\installer (the MSI), read-only;
  - this PC's Assetto Corsa folder as C:\AC, when one is found (-AcFolder, else the game's settings.json), so the
    wizard's Assetto Corsa page and the game's garage can be tried. Read-only: a race cannot run (the game writes the
    car's data and its CSP modes into AC), and nothing in the sandbox can change the real AC.
  - artifacts\installer\sandbox-appdata as the game's %AppData%\StreetCorsa, written (its settings, saves and logs
    outlive the sandbox; emptied at each start).
  The GPU is shared (vGPU) so the 3D runs; networking is on for the runtimes the wizard's "Before you race" page links.
  Windows Sandbox is a Windows feature of Pro and up: Turn Windows features on or off > Windows Sandbox.
#>
param(
    [string]$Msi,
    [string]$AcFolder,
    [switch]$NoAc,
    [switch]$WriteOnly
)

$ErrorActionPreference = 'Stop'
$folder = Join-Path $PSScriptRoot "..\artifacts\installer"
if (-not $Msi) {
    $Msi = Get-ChildItem $folder -Filter "StreetCorsa-*.msi" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Msi -or -not (Test-Path -LiteralPath $Msi)) { throw "No MSI to try (build it with installer\build.ps1)" }
$msiFolder = Split-Path -Parent (Resolve-Path -LiteralPath $Msi).Path

if (-not $NoAc -and -not $AcFolder) {
    $settings = Join-Path $env:APPDATA "StreetCorsa\settings.json"
    if (Test-Path -LiteralPath $settings) {
        $AcFolder = (Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).AssettoCorsaPath
    }
}
$mapAc = -not $NoAc -and $AcFolder -and (Test-Path -LiteralPath (Join-Path $AcFolder "content\cars"))
if (-not $NoAc -and -not $mapAc) { Write-Warning "No Assetto Corsa folder found: the sandbox gets none (-AcFolder to name one)" }

function Escape([string]$text) { [Security.SecurityElement]::Escape($text) }

$sandboxUser = 'C:\Users\WDAGUtilityAccount'
# The game's %AppData%\StreetCorsa (settings, saves, logs) lives on this PC, written: what went wrong in the sandbox is
# still there to read once it is closed. Emptied at each start, so it is that run's
$appData = Join-Path $folder "sandbox-appdata"
if (Test-Path -LiteralPath $appData) { Remove-Item -LiteralPath $appData -Recurse -Force }
New-Item -ItemType Directory -Force $appData | Out-Null
$folders = @"
    <MappedFolder>
      <HostFolder>$(Escape $msiFolder)</HostFolder>
      <SandboxFolder>$sandboxUser\Desktop\installer</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$(Escape (Resolve-Path -LiteralPath $appData).Path)</HostFolder>
      <SandboxFolder>$sandboxUser\AppData\Roaming\StreetCorsa</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
"@
if ($mapAc) {
    $folders += @"

    <MappedFolder>
      <HostFolder>$(Escape (Resolve-Path -LiteralPath $AcFolder).Path)</HostFolder>
      <SandboxFolder>C:\AC</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
"@
}

$wsb = @"
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Enable</Networking>
  <ClipboardRedirection>Enable</ClipboardRedirection>
  <MemoryInMB>8192</MemoryInMB>
  <MappedFolders>
$folders
  </MappedFolders>
</Configuration>
"@

New-Item -ItemType Directory -Force $folder | Out-Null
$path = Join-Path $folder "sandbox.wsb"
[IO.File]::WriteAllText($path, $wsb, (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote $path"
Write-Host "In the sandbox: Desktop\installer\$(Split-Path -Leaf $Msi)$(if ($mapAc) { "; Assetto Corsa is C:\AC (read-only)" })"
Write-Host "The game's settings, saves and logs land in $appData"
if (-not $WriteOnly) { Start-Process -FilePath $path }
