# Workspace Map

> Overwritten by Phase 1 of each tech-audit run — a snapshot, not history. Generated 2026-10-18 @ cd77f51 (branch feature/own-part-logic).

## Units (29)

| # | Unit | Path | Classification | TFM | Flags |
| - | ---- | ---- | -------------- | --- | ----- |
| 1 | Street Corsa | `Street Corsa/Street Corsa.csproj` | WPF desktop app | net10.0-windows | Nullable, WPF + WinForms, x64, AllowUnsafeBlocks |
| 2 | StreetCorsa.Tests | `tests/StreetCorsa.Tests/StreetCorsa.Tests.csproj` | Test project (xunit.v3 4.0.1, MTP, `OutputType=Exe`) | net10.0-windows | Nullable, WPF, x64, ProjectReference → game, 4 linked `tools/Shared` files |
| 3 | EngineBench | `tools/EngineBench/EngineBench.csproj` | CLI tool | net10.0 | Nullable, 32 linked game items (`Parts/**`, `Mods/*`, `Helpers/*`) |
| 4 | RunningGearParts | `tools/RunningGearParts/RunningGearParts/` | CLI tool (running gear KN5 models) — **new** | net10.0 | Nullable, x64, 1 linked game file |
| 5 | MachineShopSceneBuilder | `tools/MachineShopScene/MachineShopSceneBuilder/` | CLI tool (KN5 scene builder) — **new** | net10.0 | Nullable, x64 |
| 6 | StationSceneBuilder | `tools/StationScene/StationSceneBuilder/` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 7 | StreetSceneBuilder | `tools/StreetScene/StreetSceneBuilder/` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 8 | WarehouseSceneBuilder | `tools/WarehouseScene/WarehouseSceneBuilder/` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 9 | ShopSceneBuilder | `tools/ShopScene/ShopSceneBuilder/` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 10 | DinerSceneBuilder | `tools/DinerScene/DinerSceneBuilder/` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 11 | PaintBoothSceneBuilder | `tools/PaintBoothScene/PaintBoothSceneBuilder/` | CLI tool (KN5 scene builder), no packages | net10.0 | Nullable, x64 |
| 12 | PropBuilder | `tools/Props/PropBuilder/` | CLI tool (garage props KN5), no packages | net10.0 | Nullable, x64 |
| 13 | StreetCorsa.Installer | `installer/StreetCorsa.Installer.wixproj` + `installer/Actions/InstallerActions.csproj` | **WiX 6 per-user MSI + managed custom actions — new, own solution `installer/Installer.slnx` (not in the main .slnx)** | WiX 6 (WixToolset.UI.wixext 6.0.2) / net | built by `installer/build.ps1` |
| 14 | sc_race | `apps/new-modes/sc_race` (`mode.lua`, `car_parts.lua`, `format_{drag,road,time_attack}.lua`) | CSP new mode | — | installed by `ScRaceMode` |
| 15 | sc_free | `apps/new-modes/sc_free/mode.lua` | CSP new mode | — | installed by `ScRaceMode` |
| 16 | check.ps1 | `tools/check.ps1` | Build/ops script | — | build + test gate |
| 17 | installer/build.ps1 | `installer/build.ps1` | Build/ops script — **new** | — | stages tracked content, builds the MSI |
| 18–27 | Python asset scripts | `tools/{DinerScene,MachineShopScene,PaintBoothScene,Props,RunningGearParts,ShopScene,StationScene,StreetScene,WarehouseScene}/*.py`, `tools/SalesmanPortraits/paint_salesmen.py`, `tools/sfx/build_sfx.py`, `tools/siren/make_siren.py`, `tools/RebuildSounds/make_sounds.py`, `installer/make_art.py` | Build/ops scripts (Python) | — | textures, OBJ, sounds, installer art |
| 28 | sc_race_harness | `tools/sc_race_harness/test_{chase,heat,parts,strip,time_attack}.py` | Test harness (Python) | — | exercises the Lua offline |
| 29 | tools/Shared | `tools/Shared/` | Shared source for the builders (`ObjModel`, `Ground`, `Kn5SceneWriter`, `SceneMesh`, `BuilderArgs`) | — | compiled by link (builders + tests) |

Gone since the last map: `tools/SlrrPartsConverter` (deleted with the source game's VM, d22f114; only `bin/` and `obj/` remain on disk, untracked), `convert-parts.ps1`, `tools/slot_shifts.json`. `apps/lua` and `apps/python` do not exist.

## Packages

| Package | Game | Tests | EngineBench | Scene builders | Latest (2026-10-18) |
| ------- | ---- | ----- | ----------- | -------------- | ------------------- |
| LiteDB | 5.0.21 | (via game) | — | — | 5.0.21 |
| Newtonsoft.Json | 13.0.4 | (via game) | 13.0.4 | 13.0.4 (Station, Street, Warehouse, Shop, Diner, MachineShop) | 13.0.4 |
| Serilog | 4.4.0 | (via game) | — | — | 4.4.0 |
| Serilog.Sinks.File | 7.0.0 | (via game) | — | — | 7.0.0 |
| Vortice.Direct3D9 | 3.8.3 | (via game) | — | — | 3.8.3 |
| Microsoft.NET.Test.Sdk | — | 18.10.1 | — | — | 18.11.0 |
| xunit.v3 | — | 4.0.1 | — | — | 4.0.2 |
| xunit.runner.visualstudio | — | 4.0.0 | — | — | 4.0.1 |
| WixToolset.UI.wixext | installer | | | | 6.0.2 |

Vulnerable: 0 · Deprecated: 0 · Outdated: 3 (test packages, patch versions). Sources: `nuget.config` (cleared, nuget.org only). `global.json` pins SDK 10.0.400 (latestFeature). .NET 10 is LTS, supported to 2028-11-14.

## Local DLL references (now in the repo)

`Directory.Build.props` (new) sets `AcToolsLib = lib\actools\` — AcTools, AcTools.Render, SlimDX, K4os LZ4, NBidi, Nvidia.TextureTools, StringBasedFilter, xxHashSharp built from gro-ove/actools @ effa0e13 and committed, with LICENSE files. The sibling-checkout reproducibility risk of earlier audits is gone; the DLLs are still unsigned binaries in git (9 DLLs).

Native at runtime: FMOD Studio/Core (from the AC install, `LibraryImport`), d3d9, user32 (`RaceKeys` low-level keyboard hook), kernel32 (`CreateHardLink` — the one remaining `DllImport`, `CarDataOverlay.cs:837`).

## Linked source (audited under the game)

- **EngineBench**: `Parts/*.cs` (13 named), `Parts/Cars/*`, `Parts/Logic/*`, `Parts/Export/*`, `Parts/Sounds/*`, `Models/Race/RaceStartState.cs`, `Models/GameState/PartInstance.cs`, `Helpers/{SafeFile,PathNames,Multiplier,Unit,Conversion,StableHash}.cs`, `Mods/*` (8 files).
- **Scene/prop/running gear builders**: `tools/Shared/*` plus 1–2 game files each.
- **Tests**: `tools/Shared/{Kn5SceneWriter,ObjModel,SceneMesh,BuilderArgs}.cs`.

## Game project layout (605 .cs + 55 .xaml)

Changed in this window (b1e3c1d..cd77f51, 81 commits): Parts 64 files · Services 67 · Screens/Dialogs/Navigation 56 · Controls 20 · Rules 18 · Models 11 · Audio 10 · Looks 6 · Mods 4 · App/MainWindow · tests 65 · tools 56 (incl. 16 Python) · installer 7 · Lua 4.

## Signals

| Signal | Game | EngineBench | Builders | Installer | Lua |
| ------ | ---- | ----------- | -------- | --------- | --- |
| writes_ac_install | yes (`CarDataOverlay`, `AcConfigBackup`, `IniModificationService`, `ScRaceMode`; **new: `AcUninstall`** puts the install back from the MSI) | output folder | scene KN5/JSON into the repo | **yes: custom actions read `%AppData%\StreetCorsa\settings.json`, run the game's `--uninstall`, launch the game** | `Documents\Assetto Corsa\out` |
| parses_untrusted | AC car folders, ACD, KN5 (AcTools), skins/textures (Looks), mod JSON (STJ `ModJson`), **part files as plain data (parts\**\*.json classes/specs/needs, part_classes.json, fuels.json, models.json, aliases)**, scene/prop JSON, `.shell` + `ground.bin` binary caches, sound banks + radio files, result JSON, AI replies | part files, mod.json, rules | KN5, OBJ, manifest JSON | registry, settings.json | race.ini (part KN5 paths, paint) |
| runs_script_vm | **no — removed (d22f114); `EngineAssembly` works the engine out from part figures** | no | — | — | — |
| native_interop | FMOD, D3D9/D3D11 (Vortice, SlimDX, AcTools.Render), user32 hook, kernel32 | — | — | — | CSP |
| unsafe_code | yes (`FmodPlugins`, `FmodStudio`, `AudioHub.Radio`) | — | — | — | — |
| network | opt-in: `Services/Ai` (Ollama, OpenAI-compatible, Anthropic, ComfyUI) via one shared `HttpClient` | none | none | none | none |
| db | LiteDB (`SaveDatabase`) | — | — | — | — |
| process_launch | `acs.exe`, `explorer.exe` | — | — | **game exe (`UseShellExecute=true`) at the end of install** | — |

## Repo metadata

- No `.github/workflows`, no `Directory.Packages.props`. `Directory.Build.props` new (AcToolsLib). `tools/check.ps1` is the local gate.
- Two remotes: `github` and `gitlab`.
- Architecture rules: `docs/CLAUDE.md`, `docs/architecture/*`, `docs/systems/*`, `docs/screens/*`, `docs/ac-integration/*`, `docs/modding.md`, `docs/total-conversion.md`, `docs/ai-integration.md`, `installer/README.md`.
