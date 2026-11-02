<#
.SYNOPSIS
  Installs the built MSI quietly, checks what it put down, and uninstalls it again, twice: a plain quiet uninstall keeps
  the saves, settings and mods, one with REMOVE_DATA=1 takes them. The clean-machine check to run by hand on a clean VM
  (no Assetto Corsa needed: the AC folder is put off).
.DESCRIPTION
  Fails (exit 1) when an install or uninstall fails, the game, its content or its shortcut is not where the installer
  puts them, a plain uninstall takes the player's things, or anything of the game is left behind after the one that
  removes them. msiexec's logs go next to the MSI.
  Not on a PC the game is installed on: the uninstall would take that install away.
#>
param(
    [string]$Msi
)

$ErrorActionPreference = 'Stop'
if (-not $Msi) {
    $Msi = Get-ChildItem (Join-Path $PSScriptRoot "..\artifacts\installer") -Filter "StreetCorsa-*.msi" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Msi -or -not (Test-Path -LiteralPath $Msi)) { throw "No MSI to check (build it with installer\build.ps1)" }
$Msi = (Resolve-Path -LiteralPath $Msi).Path

$folder = Join-Path $env:LOCALAPPDATA "Programs\Street Corsa"
$exe = Join-Path $folder "Street Corsa.exe"
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) "Street Corsa.lnk"
$key = "HKCU:\Software\Street Corsa"
$appsEntry = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Street Corsa"
$data = Join-Path $env:APPDATA "StreetCorsa"
if (Test-Path -LiteralPath $exe) { throw "Street Corsa is installed here already ($folder): run this on a clean machine" }
# The uninstall takes the saves and settings: never anybody's real ones
if (Test-Path -LiteralPath $data) { throw "There are Street Corsa saves and settings here ($data): run this on a clean machine" }

$failures = New-Object System.Collections.Generic.List[string]
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "ok    $what" } else { Write-Host "FAIL  $what"; $failures.Add($what) }
}

function Msiexec([string[]]$arguments, [string]$log) {
    $process = Start-Process msiexec.exe -ArgumentList ($arguments + @('/qn', '/l*v', "`"$log`"")) -Wait -PassThru
    return $process.ExitCode
}

function Install([string]$round) {
    $log = [IO.Path]::ChangeExtension($Msi, ".install$round.log")
    $code = Msiexec @('/i', "`"$Msi`"", 'AC_LATER=1', 'DESKTOP_SHORTCUT=0') $log
    Check ($code -eq 0) "the quiet install ends with 0 (got $code, see $log)"
}

function Uninstall([string]$round, [string[]]$more) {
    $log = [IO.Path]::ChangeExtension($Msi, ".uninstall$round.log")
    $code = Msiexec (@('/x', "`"$Msi`"") + $more) $log
    Check ($code -eq 0) "the quiet uninstall ends with 0 (got $code, see $log)"
    Check (-not (Test-Path -LiteralPath $exe)) "the game is gone after the uninstall"
    Check (-not (Test-Path -LiteralPath $startMenu)) "the Start menu shortcut is gone"
    Check (-not (Test-Path -LiteralPath $appsEntry)) "the installed-apps entry is gone"
}

$mod = Join-Path $folder "Content\settings\smoke\mod.json"
$save = Join-Path $data "Saves\smoke.db"

Install ""

Check (Test-Path -LiteralPath $exe) "the game is in $folder"
Check (Test-Path -LiteralPath (Join-Path $folder "Content\streetcorsa\game.json")) "the core's content is installed"
Check (@(Get-ChildItem -LiteralPath (Join-Path $folder "Content") -Directory).Count -eq 1) "the core is the only content installed"
Check (Test-Path -LiteralPath (Join-Path $folder "CONTENT-LICENSE.txt")) "the content licence is installed"
Check (Test-Path -LiteralPath $startMenu) "the Start menu shortcut is there"
Check (-not (Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) "Street Corsa.lnk"))) "no desktop shortcut when it is ticked off"
$acPath = (Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).AssettoCorsaPath
Check (-not $acPath) "no AC folder is written when it is put off"
$banks = @(Get-ChildItem -LiteralPath $folder -Recurse -Filter *.bank -ErrorAction SilentlyContinue)
Check ($banks.Count -eq 0) "no engine sound bank is installed"
$entry = Get-ItemProperty -LiteralPath $appsEntry -ErrorAction SilentlyContinue
Check ($entry -and $entry.UninstallString -match '/i \{') "the game's own installed-apps entry opens the uninstall wizard"

# What the player adds after the install: a mod in Content, a save in %AppData%
New-Item -ItemType Directory -Force (Split-Path $mod) | Out-Null
Set-Content -LiteralPath $mod -Value "{}"
New-Item -ItemType Directory -Force (Split-Path $save) | Out-Null
Set-Content -LiteralPath $save -Value "smoke"

# A plain quiet uninstall (as a tool picking QuietUninstallString runs it) keeps them
Uninstall "-keep" @()
Check (Test-Path -LiteralPath $mod) "a plain uninstall keeps the mods in Content"
Check (Test-Path -LiteralPath $save) "a plain uninstall keeps the saves in $data"

# Installed again over what was kept, then uninstalled asking for them to go
Install "-again"
Check (Test-Path -LiteralPath $mod) "the kept mod is still there after installing again"
Uninstall "-remove" @('REMOVE_DATA=1')
$left = @(Get-ChildItem -LiteralPath $folder -Recurse -File -ErrorAction SilentlyContinue)
Check ($left.Count -eq 0) "nothing of the install is left in $folder, the mods put there neither ($($left.Count) files)"
Check (-not (Test-Path -LiteralPath $folder)) "the install folder itself is gone"
Check (-not (Test-Path -LiteralPath $data)) "the saves and settings in $data are gone"

if ($failures.Count -gt 0) {
    [Console]::Error.WriteLine("smoke: $($failures.Count) check(s) failed")
    exit 1
}
Write-Host "smoke: the MSI installs and uninstalls cleanly"
exit 0
