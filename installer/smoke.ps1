<#
.SYNOPSIS
  Installs the built MSI quietly, checks what it put down, and uninstalls it again: the clean-machine check CI runs on a
  fresh runner (no Assetto Corsa there, so the AC folder is put off), and one to run by hand on a clean VM.
.DESCRIPTION
  Fails (exit 1) when the install or the uninstall fails, the game, its content or its shortcut is not where the
  installer puts them, or anything of the game is left behind after the uninstall. msiexec's logs go next to the MSI.
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
if (Test-Path -LiteralPath $exe) { throw "Street Corsa is installed here already ($folder): run this on a clean machine" }

$failures = New-Object System.Collections.Generic.List[string]
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "ok    $what" } else { Write-Host "FAIL  $what"; $failures.Add($what) }
}

function Msiexec([string[]]$arguments, [string]$log) {
    $process = Start-Process msiexec.exe -ArgumentList ($arguments + @('/qn', '/l*v', "`"$log`"")) -Wait -PassThru
    return $process.ExitCode
}

$installLog = [IO.Path]::ChangeExtension($Msi, ".install.log")
$code = Msiexec @('/i', "`"$Msi`"", 'AC_LATER=1', 'DESKTOP_SHORTCUT=0') $installLog
Check ($code -eq 0) "the quiet install ends with 0 (got $code, see $installLog)"
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

$uninstallLog = [IO.Path]::ChangeExtension($Msi, ".uninstall.log")
$code = Msiexec @('/x', "`"$Msi`"") $uninstallLog
Check ($code -eq 0) "the quiet uninstall ends with 0 (got $code, see $uninstallLog)"
Check (-not (Test-Path -LiteralPath $exe)) "the game is gone after the uninstall"
Check (-not (Test-Path -LiteralPath $startMenu)) "the Start menu shortcut is gone"
$left = @(Get-ChildItem -LiteralPath $folder -Recurse -File -ErrorAction SilentlyContinue)
Check ($left.Count -eq 0) "nothing of the install is left in $folder ($($left.Count) files)"

if ($failures.Count -gt 0) {
    [Console]::Error.WriteLine("smoke: $($failures.Count) check(s) failed")
    exit 1
}
Write-Host "smoke: the MSI installs and uninstalls cleanly"
exit 0
