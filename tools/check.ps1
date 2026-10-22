# Builds the whole solution and runs the tests: the check to pass before a merge or a push. The tools compile game
# files by link, so a game change can break them without the game noticing (EngineBench did not build from PR #20 to
# the 2026-09-25 audit). Nothing runs this automatically yet (the actools DLLs are in lib\actools now, so CI could).
# The rated engine builds go on the dyno in the tests (EngineGoldenTests, over the base game and a handful of real rated
# builds in tests\StreetCorsa.Tests\Fixtures\goldens; TestEnginesTests over the tests' own engines: the base game has no
# engine of its own since 2026-10-20, so EngineBench rated over it had nothing to run),
# and the race mode's harness runs when Python has lupa (pip install -r tools/sc_race_harness/requirements.txt); without
# it, it says so and goes on.
#
# Exit code: 0 when everything builds and every test passes, 1 otherwise.
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot ".."

dotnet build (Join-Path $root "Street Corsa.slnx") -c $Configuration -v q -clp:NoSummary
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("check: the solution does not build")
    exit 1
}

# The installer's custom actions live in their own solution (installer\Installer.slnx, with the wixproj, which needs a
# staged game: build.ps1's); the actions project alone compiles with nothing staged, so it is checked here
dotnet build (Join-Path $root "installer\Actions\InstallerActions.csproj") -c $Configuration -v q -clp:NoSummary
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("check: the installer's custom actions do not build")
    exit 1
}

dotnet test --project (Join-Path $root "tests\StreetCorsa.Tests\StreetCorsa.Tests.csproj") -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("check: tests failed")
    exit 1
}

# The race mode runs in AC only; the harness runs its Lua on LuaJIT, as CSP does. The probe writes nothing to stderr
# (Windows PowerShell turns that into an error), and the Store's python stand-in fails it
$python = Get-Command python -ErrorAction SilentlyContinue
$lupa = $false
if ($python) {
    try {
        & $python.Source -c "import importlib.util as u, sys; sys.exit(0 if u.find_spec('lupa') and u.find_spec('lupa.luajit21') else 1)" *> $null
        $lupa = $LASTEXITCODE -eq 0
    }
    catch {
        $lupa = $false
    }
}
if (-not $lupa) {
    Write-Warning "check: the sc_race harness is skipped: no python with lupa's LuaJIT (pip install -r tools\sc_race_harness\requirements.txt)"
}
else {
    # Every suite in the folder, so a new one runs without being listed here
    foreach ($test in Get-ChildItem (Join-Path $PSScriptRoot "sc_race_harness") -Filter "test_*.py" | Sort-Object Name) {
        & $python.Source $test.FullName
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("check: the sc_race harness failed ($($test.Name))")
            exit 1
        }
    }
}

if ($lupa) { Write-Host "check: build, tests and the race mode harness pass" }
else { Write-Host "check: build and tests pass (the race mode harness did not run)" }
exit 0
