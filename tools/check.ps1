# Builds the whole solution and runs the tests: the check to pass before a merge or a push. The tools compile game
# files by link, so a game change can break them without the game noticing (EngineBench did not build from PR #20 to
# the 2026-09-25 audit). Nothing runs this automatically yet: CI would need the actools DLLs, which are not in the repo.
# Then the rated engine builds go on the dyno (EngineBench rated, which only reads the content), and the race mode's
# harness runs when Python has lupa (pip install -r tools/sc_race_harness/requirements.txt); without it, it says so and
# goes on.
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

dotnet test --project (Join-Path $root "tests\StreetCorsa.Tests\StreetCorsa.Tests.csproj") -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("check: tests failed")
    exit 1
}

# The content's rated builds still run, near their ratings: a part logic change can break them with every test green.
# rated only reads the content (the catalog, the builds, the dyno); it writes nothing
$bench = Join-Path $root "tools\EngineBench\bin\$Configuration\net10.0\EngineBench.dll"
dotnet $bench (Join-Path $root "Street Corsa\Content\streetcorsa\parts") rated
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("check: EngineBench rated failed (a rated build does not run or is far off its rating)")
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
    foreach ($test in "test_chase.py", "test_heat.py", "test_strip.py", "test_time_attack.py", "test_parts.py") {
        & $python.Source (Join-Path $PSScriptRoot "sc_race_harness\$test")
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("check: the sc_race harness failed ($test)")
            exit 1
        }
    }
}

if ($lupa) { Write-Host "check: build, tests, rated builds and the race mode harness pass" }
else { Write-Host "check: build, tests and rated builds pass (the race mode harness did not run)" }
exit 0
