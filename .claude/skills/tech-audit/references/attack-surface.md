# Attack Surface

> Overwritten by each tech-audit run — a snapshot, not history. Snapshot of 2026-10-25 @ a56d653f (full sweep: the first
> audit of the core-only repository, recreated 2026-10-22/25). The slice reviews of that run used it as their worklist.

## Units

- **Street Corsa** (WPF game, `net10.0-windows`, x64, unsafe on) — every surface below
- **StreetCorsa.Tests** (xunit.v3 / MTP) — writes only into temp folders (`TestSupport.TempDir`)
- **EngineBench** (CLI, `net10.0`) — the game's part logic by link; reads a Content folder or a mod, writes reports/snapshots
- **InstallerActions** (`net472`, WiX DTF custom actions, not in the solution) — runs elevated-or-not inside msiexec; launches the game and its `--uninstall`
- **sc_race / sc_free** (CSP new-modes, Lua) — installed into AC by `ScRaceMode`; write the race result JSON
- Scripts: `tools/check.ps1`, `installer/*.ps1`, Python generators (`tools/sfx`, `tools/siren`, `tools/Shared/scene_tools.py`, `tests/.../Fixtures/*.py`, `installer/make_art.py`), `tools/sc_race_harness` (LuaJIT via lupa)
- CI: `.github/workflows/check.yml`, `installer.yml` (`GH_TOKEN: github.token` only)

## Writes outside the app's own folders (AC install, AC cfg)

| Site | What | Restore |
| --- | --- | --- |
| `Services/Assetto Corsa/CarDataOverlay.cs` (manifest :414, swap :463) | car `data` / `data.acd` swapped for a race | manifest via `SafeFile` before the swap; restore in the launcher and at start-up |
| `CarDataOverlay.Clones.cs` (:56 marker, :88/:98 plain `File.WriteAllText` into a fresh clone, :249 clones list) | same-model clone folders in `content\cars` | marker + clones list; cleanup deletes only marked folders |
| `CarDataOverlay.Sound.cs` (:31 `GUIDs.txt` plain write) | sound bank swap | through the overlay manifest |
| `Services/Configuration/IniModificationService.cs` (:163), `AcConfigBackup.cs` (:259 manifest), `KeptFile.cs`, `RaceIniBuilder.cs` | `Documents\Assetto Corsa\cfg\*.ini` (race.ini, assists, video…) | `AcConfigBackup` manifest + kept copies |
| `Services/Assetto Corsa/ScRaceMode.cs` (:215 marker, :238 mode files) | `apps\lua`/new-modes folder in the AC install | marker-owned folder |
| `Services/Catalog/BaseCarsFile.cs` (:128) | a file next to the AC cars (via `SafeFile`) | — |

## Untrusted-input parsers

- AC car folders: `Parts/Export/AcCarDataReader.cs` (data.acd/ini), `Parts/Export/AcCarUi.cs` (ui_car.json, STJ MaxDepth 64), `Services/Catalog/*` (catalog import, `EncryptedCars`), AcTools KN5 reads (Controls viewports, `Looks/CarPaint.cs` texture loader :819)
- Own binary formats: `Looks/ShellFile.cs` (BinaryReader, counts capped by `Count(reader, most)`), `Services/Station/StationGround.cs` (:47 columns/rows from file)
- Mods: `Mods/*` (`mod.json`, `ModJson`, `ModMerge`, `ContentRegistry`), `Parts/PartsFiles.cs` + `PartsCatalog.cs` (one JSON per part), `Rules/*`, `Navigation/FlowBook.cs` + `Services/Scripting/*` (flow/car scripts as data), text pools, scenes JSON
- Race result JSON from the Lua mode: `Services/Race/RaceResultIngestionService.cs`, `Models/Race/RaceResultJson.cs`
- Saves: LiteDB via `Services/Storage/SaveDatabase.cs` (:108 read-only probe, :231 open) with `SaveMapper` + `SaveTypeNames` type binder; `Services/Race/RaceStateSnapshot.cs`
- User folders: radio music folders (`Audio/Radio/RadioLibrary.cs`), sound browser

## Paths built from content

`Path.Combine` with car ids, part ids, mod ids, scene/texture names, sound files — helpers `Helpers/PathNames.cs` (`IsUnder`, name checks) and `Mods/ModIds.cs`; tests feed `..`, rooted and UNC names (Audit1005C, AuditFixesB, CarDataOverlayTests, HelperTests).

## Process launches

- `Services/Assetto Corsa/AssettoCorsaLauncher.cs:548` — AC exe, `UseShellExecute = true`, path from settings
- `Audio/Radio/RadioPanelViewModel.cs:293`, `Screens/SoundBrowser/SoundBrowserScreenViewModel.cs:267` — explorer.exe with a quoted folder/file path
- `Screens/NewGame/NewGameScreenViewModel.cs:115` — opens the Content folder via shell
- `installer/Actions/CustomActions.cs:167` (launch the game after install, shell) and `:251` (`--uninstall` switch)

## Deserialization

- Newtonsoft (22 game files) and System.Text.Json (50 files): no `TypeNameHandling` in game code (only a test fingerprint uses `Auto`); Json.NET 13 MaxDepth defaults to 64
- LiteDB `BsonMapper`: `SaveMapper.Create()` with `typeNameBinder: SaveTypeNames.Instance` (`_type` names constrained)
- Part trees capped at `PartInstance.MaxDepth = 64` everywhere they are walked (`PartTrees`, `CarWork`, `SavedParts`); `PartAssembler` depth 8; `PartClasses` 32

## Native interop

- FMOD Studio/Core 1.08 (from the AC install) via `LibraryImport` in `Audio/FmodStudio.cs` (58 sites), DSP plugins re-implemented in `Audio/FmodPlugins.cs` (native callbacks)
- Win32: `Audio/Radio/RaceKeys.cs` (19, keyboard hook / hotkeys during races), `Audio/AudioHub.Radio.cs`, `Looks/Png.cs`, `CarDataOverlay.cs` (2 — file APIs)
- D3D9/D3DImage: `Controls/SharedTextureBridge.cs` (Vortice), AcTools.Render + SlimDX (from `lib/actools`, built from gro-ove/actools @ effa0e13)

## Network

- `Services/Ai/AiHttp.cs` — one shared `HttpClient` (SocketsHttpHandler); providers Ollama / OpenAI-compatible / Anthropic (`AiProviders.cs`), ComfyUI images (`ComfyImageService.cs`); endpoint, model and API key from settings. Model output → chat text, ads, pictures written by `AiWriter.cs:351`

## Cross-unit trust boundaries

- sc_race Lua mode → result JSON in `Documents\Assetto Corsa\out` → `RaceResultIngestionService`
- `Content\<mod>\**` (any downloaded mod) → catalogs, rules, flows, scripts, scenes, sounds
- game ↔ AC process (launch, wait, restore)
- installer custom actions ↔ the game exe (`--uninstall`)
