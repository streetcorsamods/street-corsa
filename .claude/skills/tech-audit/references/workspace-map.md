# Workspace Map

> Overwritten by Phase 1 of each tech-audit run — a snapshot, not history. Generated 2026-09-29 @ e5bfd8c.

## Units (12)

| # | Unit | Path | Classification | TFM | Flags |
| - | ---- | ---- | -------------- | --- | ----- |
| 1 | Street Corsa | `Street Corsa/Street Corsa.csproj` | WPF desktop app | net10.0-windows | Nullable, WPF + WinForms, x64, AllowUnsafeBlocks |
| 2 | StreetCorsa.Tests | `tests/StreetCorsa.Tests/StreetCorsa.Tests.csproj` | Test project (xunit.v3 4.0.1, MTP, `OutputType=Exe`) | net10.0-windows | Nullable, WPF, x64, ProjectReference → game |
| 3 | EngineBench | `tools/EngineBench/EngineBench.csproj` | CLI tool | net10.0 | Nullable |
| 4 | SlrrPartsConverter | `tools/SlrrPartsConverter/SlrrPartsConverter.csproj` | CLI tool | net10.0 | Nullable, x64 |
| 5 | StationSceneBuilder | `tools/StationScene/StationSceneBuilder/StationSceneBuilder.csproj` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 6 | StreetSceneBuilder | `tools/StreetScene/StreetSceneBuilder/StreetSceneBuilder.csproj` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 7 | WarehouseSceneBuilder | `tools/WarehouseScene/WarehouseSceneBuilder/WarehouseSceneBuilder.csproj` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 8 | sc_race | `apps/new-modes/sc_race` (`manifest.ini`, `mode.lua`, `siren.wav`) | CSP new mode | — | installed by `ScRaceMode` |
| 9 | convert-parts.ps1 | `tools/convert-parts.ps1` | Build/ops script | — | drives SlrrPartsConverter |
| 10 | check.ps1 | `tools/check.ps1` | Build/ops script | — | build + test gate |
| 11 | make_siren.py | `tools/siren/make_siren.py` | Build/ops script (Python) | — | generates `siren.wav` |
| 12 | sc_race_harness | `tools/sc_race_harness/test_{chase,heat,strip}.py` | Test harness (Python) | — | exercises `mode.lua` logic offline |

`apps/lua` and `apps/python` do not exist.

## Packages

| Package | Game | Tests | EngineBench | Converter | Scene builders | Latest (2026-09-29) |
| ------- | ---- | ----- | ----------- | --------- | -------------- | ------------------- |
| LiteDB | 5.0.21 | (via game) | — | — | — | 5.0.21 |
| Newtonsoft.Json | 13.0.4 | (via game) | 13.0.4 | 13.0.4 | 13.0.4 | 13.0.4 |
| Serilog | 4.4.0 | (via game) | — | — | — | 4.4.0 |
| Serilog.Sinks.File | 7.0.0 | (via game) | — | — | — | 7.0.0 |
| Vortice.Direct3D9 | 3.8.3 | (via game) | — | — | — | 3.8.3 |
| Microsoft.NET.Test.Sdk | — | 18.10.1 | — | — | — | 18.10.1 |
| xunit.v3 | — | 4.0.1 | — | — | — | 4.0.1 |
| xunit.runner.visualstudio | — | 4.0.0 | — | — | — | 4.0.0 |

Vulnerable: 0 · Deprecated: 0 · Outdated: 0. JetBrains.Annotations is gone. Sources: `nuget.config` clears and lists nuget.org only. `global.json` present.

## Local DLL references (HintPath, unversioned)

| DLL | Referenced by | Path | Exists |
| --- | ------------- | ---- | ------ |
| AcTools.dll | game, tests, converter, 3 scene builders | `..\actools\Output\x64\Debug\` (sibling checkout) | yes |
| AcTools.Render.dll | game | `..\actools\Output\x64\Debug\` | yes |
| SlimDX.dll | game | `..\actools\Libraries\SlimDX-x64\` | yes |

Native at runtime: FMOD Studio/Core (from the AC install, `LibraryImport`), d3d9, user32, kernel32.

## Linked source (audited under the game)

- **EngineBench** links `Parts/{PartDefinition,PartOverrides,EngineBuild,PartsCatalog,SlotShifts,PartKinds,WorkOrder,EngineLayout}.cs`, `Parts/{Cars,Scripting,Logic,Export,Sounds}/*.cs`, `Models/Race/RaceStartState.cs`, `Models/GameState/PartInstance.cs`, `Helpers/{SafeFile,PathNames,Multiplier}.cs`, `Mods/{ModIds,ModSet,ContentRegistry}.cs` — builds at e5bfd8c
- **SlrrPartsConverter** links `Parts/{PartDefinition,PartOverrides,EngineBuild,SlotShifts,Kn5Bitmap}.cs`, `Parts/Scripting/*.cs`, `Helpers/{SafeFile,PathNames}.cs`, `Mods/ModIds.cs`

## Game project layout (≈385 .cs + .xaml files, ≈71k lines)

Services 132 files (22.1k lines) · Screens 79 (18.2k) · Parts 61 (10.0k) · Controls 20 (8.9k) · Models 47 (4.3k) · Audio 7 (2.7k) · Dialogs 28 (2.1k) · App.xaml.cs 909 · Styles 4 · Mods 5 · Helpers 7 · Navigation 3 · Logging 3 · Converters 6 · ViewModels 4 · Components 2 · Configuration 1. Tests: 50 .cs.

## Signals

| Signal | Game | EngineBench | Converter | Scene builders | Lua |
| ------ | ---- | ----------- | --------- | -------------- | --- |
| writes_ac_install | yes (`CarDataOverlay`, `AcConfigBackup`, `IniModificationService`, `ScRaceMode`) | output folder | `Content\streetcorsa\parts` | writes scene KN5/JSON into the repo | `Documents\Assetto Corsa\out` |
| parses_untrusted | AC car folders, ACD, KN5 (AcTools), ui_car.json, pack.json, mod JSON, scene JSON, sound banks, result JSON | pack.json | SCX/RPK/cfg, script bytecode | KN5 | — |
| runs_script_vm | yes | yes | yes | — | — |
| native_interop | FMOD (`LibraryImport`), D3D9 (Vortice, SlimDX, AcTools.Render), user32, kernel32 | — | — | — | — |
| unsafe_code | yes (`FmodPlugins`, `FmodStudio`) | — | — | — | — |
| network | none | none | none | none | none |
| db | LiteDB (`SaveDatabase`) | — | — | — | — |
| process_launch | `acs.exe` (`AssettoCorsaLauncher`, `UseShellExecute=true`) | — | — | — | — |

## Repo metadata

- No `.github/workflows`, no `Directory.Build.props` / `Directory.Packages.props`. `tools/check.ps1` is the local gate.
- Two remotes: `github` and `gitlab`.
- Architecture rules: `docs/CLAUDE.md`, `docs/architecture/*`, `docs/systems/*`, `docs/ac-integration/*`, `docs/modding.md`.
- The project folder was renamed `Street Rod AC/` → `Street Corsa/` in step 15, so the 6aa3d39..e5bfd8c window touches almost every file.
