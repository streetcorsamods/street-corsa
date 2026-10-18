# Workspace Map

> Overwritten by Phase 1 of each tech-audit run — a snapshot, not history. Generated 2026-10-05 @ b1e3c1d.

## Units (27)

| # | Unit | Path | Classification | TFM | Flags |
| - | ---- | ---- | -------------- | --- | ----- |
| 1 | Street Corsa | `Street Corsa/Street Corsa.csproj` | WPF desktop app | net10.0-windows | Nullable, WPF + WinForms, x64, AllowUnsafeBlocks |
| 2 | StreetCorsa.Tests | `tests/StreetCorsa.Tests/StreetCorsa.Tests.csproj` | Test project (xunit.v3 4.0.1, MTP, `OutputType=Exe`) | net10.0-windows | Nullable, WPF, x64, ProjectReference → game, 3 linked files |
| 3 | EngineBench | `tools/EngineBench/EngineBench.csproj` | CLI tool | net10.0 | Nullable, 29 linked game files |
| 5 | StationSceneBuilder | `tools/StationScene/StationSceneBuilder/` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 6 | StreetSceneBuilder | `tools/StreetScene/StreetSceneBuilder/` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 7 | WarehouseSceneBuilder | `tools/WarehouseScene/WarehouseSceneBuilder/` | CLI tool (KN5 scene builder) | net10.0 | Nullable, x64 |
| 8 | ShopSceneBuilder | `tools/ShopScene/ShopSceneBuilder/` | CLI tool (KN5 scene builder) — new | net10.0 | Nullable, x64 |
| 9 | DinerSceneBuilder | `tools/DinerScene/DinerSceneBuilder/` | CLI tool (KN5 scene builder) — new | net10.0 | Nullable, x64 |
| 10 | PaintBoothSceneBuilder | `tools/PaintBoothScene/PaintBoothSceneBuilder/` | CLI tool (KN5 scene builder) — new, no packages | net10.0 | Nullable, x64 |
| 11 | PropBuilder | `tools/Props/PropBuilder/` | CLI tool (garage props KN5) — new, no packages | net10.0 | Nullable, x64 |
| 12 | sc_race | `apps/new-modes/sc_race` (`mode.lua`, `car_parts.lua`, `format_{drag,road,time_attack}.lua`) | CSP new mode | — | installed by `ScRaceMode` |
| 13 | sc_free | `apps/new-modes/sc_free/mode.lua` | CSP new mode — new | — | installed by `ScRaceMode` |
| 15 | check.ps1 | `tools/check.ps1` | Build/ops script | — | build + test gate |
| 16–25 | Python asset scripts | `tools/{DinerScene,PaintBoothScene,Props,ShopScene,StationScene,StreetScene,WarehouseScene}/*.py`, `tools/SalesmanPortraits/paint_salesmen.py`, `tools/sfx/build_sfx.py`, `tools/siren/make_siren.py` | Build/ops scripts (Python) | — | generate textures, OBJ, sounds; ComfyUI for portraits |
| 26 | sc_race_harness | `tools/sc_race_harness/test_{chase,heat,parts,strip,time_attack}.py` | Test harness (Python) | — | exercises the Lua offline |
| 27 | tools/Shared | `tools/Shared/` | Shared source for the scene builders (`ObjModel`, `Ground`) | — | compiled by link |

`apps/lua` and `apps/python` do not exist.

## Packages

| Package | Game | Tests | EngineBench | Converter | Scene builders | Latest (2026-10-05) |
| ------- | ---- | ----- | ----------- | --------- | -------------- | ------------------- |
| LiteDB | 5.0.21 | (via game) | — | — | — | 5.0.21 |
| Newtonsoft.Json | 13.0.4 | (via game) | 13.0.4 | 13.0.4 | 13.0.4 (Station, Street, Warehouse, Shop, Diner) | 13.0.4 |
| Serilog | 4.4.0 | (via game) | — | — | — | 4.4.0 |
| Serilog.Sinks.File | 7.0.0 | (via game) | — | — | — | 7.0.0 |
| Vortice.Direct3D9 | 3.8.3 | (via game) | — | — | — | 3.8.3 |
| Microsoft.NET.Test.Sdk | — | 18.10.1 | — | — | — | 18.10.1 |
| xunit.v3 | — | 4.0.1 | — | — | — | 4.0.1 |
| xunit.runner.visualstudio | — | 4.0.0 | — | — | — | 4.0.0 |

Vulnerable: 0 · Deprecated: 0 · Outdated: 0. Sources: `nuget.config` (nuget.org only). `global.json` present.

## Local DLL references (HintPath, unversioned)

| DLL | Referenced by | Path | Exists |
| --- | ------------- | ---- | ------ |
| AcTools.dll | game, tests, 7 scene/prop builders | `actools\AcTools\bin\x64\Release\` (sibling checkout; Release since the 2026-09-29 fixes) | yes |
| AcTools.Render.dll | game | `actools\AcTools.Render\bin\x64\Release\` | yes |
| SlimDX.dll | game | `actools\Libraries\SlimDX-x64\` | yes |

Native at runtime: FMOD Studio/Core (from the AC install, 53 `LibraryImport`), d3d9, user32 (`RaceKeys` hook, 6 `DllImport`), kernel32 (`CreateHardLink`, `GetModuleHandle`).

## Linked source (audited under the game)

- **EngineBench**: 29 game files (`Parts/**`, `Models/Race/RaceStartState.cs`, `Models/GameState/PartInstance.cs`, `Helpers/*`, `Mods/*`, `Rules/*`).
- **Scene/prop builders**: `tools/Shared/*` plus 0–2 game files each.

## Game project layout (≈635 .cs + .xaml files, ≈102k lines of C#)

Services 188 files · Screens 101 · Parts 74 · Dialogs 60 · Models 51 · Controls 40 · Audio 26 · Rules 23 · Helpers 16 · Styles 12 · Mods 9 · Looks 8 · Converters 6 · Navigation 5 · ViewModels 4 · Logging 3. Tests: 90 .cs.

## Signals

| Signal | Game | EngineBench | Converter | Scene builders | Lua |
| ------ | ---- | ----------- | --------- | -------------- | --- |
| writes_ac_install | yes (`CarDataOverlay`, `AcConfigBackup`, `IniModificationService`, `ScRaceMode` — sc_race + sc_free) | output folder | `Content\streetcorsa\parts` | scene KN5/JSON into the repo | `Documents\Assetto Corsa\out` |
| parses_untrusted | AC car folders, ACD, KN5 (AcTools), skins/textures (Looks), mod JSON (STJ `ModJson`), scene/prop JSON, sound banks + radio files, result JSON, AI replies | pack.json, rules | SCX/RPK/cfg, script bytecode | KN5, OBJ | race.ini (part KN5 paths, paint) |
| runs_script_vm | yes | yes | yes | — | — |
| native_interop | FMOD, D3D9/D3D11 (Vortice, SlimDX, AcTools.Render), user32 low-level keyboard hook, kernel32 | — | — | — | CSP |
| unsafe_code | yes (`FmodPlugins`, `FmodStudio`, `AudioHub.Radio`) | — | — | — | — |
| network | **yes, opt-in**: `Services/Ai` (Ollama, OpenAI-compatible, Anthropic, ComfyUI) via one shared `HttpClient` | none | none | none | none |
| db | LiteDB (`SaveDatabase`) | — | — | — | — |
| process_launch | `acs.exe` (`AssettoCorsaLauncher`), `explorer.exe` (radio folder, sound browser) | — | — | — | — |

## Repo metadata

- No `.github/workflows`, no `Directory.Build.props` / `Directory.Packages.props`. `tools/check.ps1` is the local gate.
- Two remotes: `github` and `gitlab`.
- Architecture rules: `docs/CLAUDE.md`, `docs/architecture/*`, `docs/systems/*`, `docs/screens/*`, `docs/ac-integration/*`, `docs/modding.md`, `docs/total-conversion.md`, `docs/ai-integration.md`.
