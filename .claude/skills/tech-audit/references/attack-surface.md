# Trust-Surface Inventory

> Overwritten by Phase 3 of each security/all run. Generated 2026-09-29 @ e5bfd8c. The diff window 6aa3d39..e5bfd8c includes the `Street Rod AC/` → `Street Corsa/` rename, so this is a full re-inventory. Paths are relative to `Street Corsa/` unless they start with `tools/` or `apps/`. Previous snapshot: `git show 6aa3d39:.claude/skills/tech-audit/references/attack-surface.md`. Findings against these surfaces: `findings/2026-09-29-all.md`.

## 1. Writes outside the app's own folders

| Target | Site | Restore |
| ------ | ---- | ------- |
| AC car `data\*.ini`/`*.lut` (unpacked from data.acd when packed) | `Services/Assetto Corsa/CarDataOverlay.cs` Apply (keep copy, then **in-place `File.WriteAllText` :276**), manifest via `SafeFile` | `Restore` :353-471 (SafeFile put-back, per file, failures stay in manifest); launcher finally / background wait after AC exits; start-up; exit; fatal path under `AcInstallGate.TryRun` |
| AC car sound bank + GUIDs.txt | `CarDataOverlay.cs` (original moved to keep folder, donor hard-linked or copied) | same restore paths |
| Same-model clones `content\cars\<id>__sr_opponent` | `CarDataOverlay.CreateClone` (recorded in `AcRestore\clones.json` first, marker `streetrod_clone.json`) | `RemoveClones`: suffix + inside `cars` + marker (or empty); current and every recorded install |
| AC `Documents\Assetto Corsa\cfg\race.ini`, `assists.ini`, showroom INI | `Services/Configuration/IniModificationService.cs` via `SafeFile` | `Services/Configuration/AcConfigBackup.cs` (`~cfg` keep + `ini-manifest.json`) — a lost kept copy is retried forever (drift vs CarDataOverlay) |
| CSP mode `extension\lua\new-modes\sc_race\` | `Services/Assetto Corsa/ScRaceMode.cs` (SafeFile); retired `apps\lua\sr_race_manager`, `new-modes\sr_race` deleted | reinstalled each race, not restored (documented) |
| Result archive/quarantine `Documents\Assetto Corsa\out\sc_race\…` | `Services/Race/RaceResultIngestionService.cs` | app-owned |
| Saves `saves\*.db` + backups, `settings.json`, `%AppData%\StreetCorsa\*` caches | `Services/Storage/SaveDatabase.cs`, `GameSettingsService`, `EngineLoudness`, `EncryptedCars` (SafeFile) | n/a |
| Tools: `Content\streetcorsa\parts` (converter), scene KN5/`ground.bin`/`warehouse.json` (scene builders), `tools/slot_shifts.json` + deletes `bin\…\slot_shifts.json` (convert-parts.ps1) | `tools/SlrrPartsConverter/Program.cs` (staging + swap), `tools/*Scene/*Builder/Program.cs` (**direct writes**), `tools/convert-parts.ps1:476-514` | converter: commit marker + recover; builders: none |

**Gap:** the launch pipeline doesn't check for an AC process it didn't start before writing any of the above (`AssettoCorsaLauncher.cs:226-266`), and `CarDataOverlay.RestoreLeftover` restores with no AC check.

## 2. Untrusted-input parsers

| Input (who writes it) | Parser | Bounds today | Gap |
| --- | --- | --- | --- |
| Result JSON (`sc_race` mode) | `RaceResultIngestionService` → `RaceResultValidator` (Newtonsoft default) | GUID name, 1..1 MB, required fields, finite distance, condition clamped | bracket ET unbounded (`BracketRules.cs:42-47`) |
| `data.acd` (car mods) | `Parts/Export/AcdFile.cs` | name/entry lengths vs remaining bytes (64-bit), every name `IsSafeSegment` | — |
| car `data\*.ini`, POWER_CURVE | `IniText`, `AcEngineData` | Latin-1 round trip, `.lut` safe segment | huge finite figures reach AC |
| `ui_car.json`, `ui_skin.json` | `AcCarUi` (MaxDepth 64), `AcSpecs`, `PoliceCars` | culture-free numbers, weight window | no size cap; bhp → decimal overflow (`CarProfileService.cs:231`) |
| `ui_track.json`, `ui/ui_*.json` | STJ in `AssettoCorsaContentService` | caught per track/layout | phantom layouts from `ui_*.json` |
| Car/part KN5 | AcTools `Kn5.FromFile` | caught in `PartModelBounds`, `EncryptedCars` | uncaught in `PartAssembler.cs:89`; Debug AcTools shows a MessageBox on a bad car |
| SLRR `.class` bytecode (part packs) | `Parts/Scripting/ScriptClass.Load` | every length checked, per-file catch | whole-file read |
| Script execution | `ScriptVm`, `ScriptHost`, `PartScriptRuntime` | 1M steps/entry, depth 24, statics 64, text 64K, array stores in range | budget per entry not VM; array length unbounded; natives 1 step; `Float.toString` output uncapped |
| `pack.json`, `part_aliases.json`, `engine_builds.json`, overrides | Newtonsoft (TypeNameHandling None) | nulls normalized, ids namespaced, paths `TryCombineUnder`, alias hops ≤ 8 | `PartsCatalog.cs:87` enumeration unguarded |
| `work_order.json` | `Parts/WorkOrder.cs` | strict enum names | `Loops` DFS exponential |
| `mod.json`, content JSON (racers, dealers, shops, events, victories, text, showcase) | `Mods/ModSet` (Newtonsoft), `ModJson`/`ContentRegistry` (STJ) | id regex, per-entry catch, portrait/map paths contained | lazy enumeration unguarded; special opponents' cars not checked installed |
| Scene JSON (streets, stations, warehouses) | STJ in `StreetScene`, `StationScene`, `WarehouseScene` | nulls defaulted (warehouse), finite points, price window | null light → NRE (street/station); `"model"` path not contained; numeric enum names; texture names unverified (cross-check) |
| `ground.bin` | `Services/Station/StationGround.cs` | magic, grid bounds | 400 MB allocated before the length check |
| sound `sounds.json`/`sound.json` | `Parts/Sounds/SoundLibrary.cs` | bank path `TryCombineUnder` + `.bank` | bare catches |
| LiteDB saves / catalog.db | `SaveDatabase` (custom `ITypeNameBinder`), `CatalogDatabase` | single instance + lock, read-only `Peek`, corrupt catalog set aside | — |
| SLRR SCX/RPK/cfg (converter) | `tools/SlrrPartsConverter/Slrr/*` | counts/offsets range-checked, `TryCombineUnder` | untested |

## 3. Process launches

- `Services/Assetto Corsa/AssettoCorsaLauncher.cs:466-474`: `Process.Start` with `UseShellExecute = true` and `FileName = Path.Combine(AssettoCorsaPath, intent.Executable)`. The executable name is a code constant, and the folder comes from the user's settings. The exit is awaited async, with a relaunch watch and a bounded kill. There is no check for a pre-existing AC process.

## 4. Deserialization

- Newtonsoft 13.0.4 with default settings everywhere in the game (no `TypeNameHandling`; only a test sets `Auto`). System.Text.Json for scenes, mods' content and caches (MaxDepth 64 by default). LiteDB 5.0.21 uses a direct exclusive connection with `SaveTypeNames` mapping short names. No BinaryFormatter or XmlSerializer.

## 5. Native interop

- FMOD Studio/Core 1.08 from the AC install: 25 `LibraryImport` (blittable), 4 `delegate* unmanaged` exports, `[UnmanagedCallersOnly]` DSP callbacks, and process-lifetime `NativeMemory` descriptions. The struct layouts match the header. `unsafe` code lives in `Audio/FmodPlugins.cs` and `Audio/FmodStudio.cs` and is bounded by FMOD buffer lengths.
- 1 `DllImport` (`CarDataOverlay.cs:836`, kernel32 CreateHardLink).
- D3D9Ex via Vortice (`Controls/SharedTextureBridge.cs`): shared texture into D3DImage, Lock/Unlock in try/finally, no `CheckDeviceState`. DX11 via AcTools.Render + SlimDX (Debug build).

## 6. Network

- None. No `HttpClient`/`WebClient`/sockets in any unit.

## 7. Cross-unit trust boundaries

- `sc_race` mode → `Documents\Assetto Corsa\out\sc_race\{uuid}.json` (temp + `io.move`, written before `ac.shutdownAssettoCorsa()`) → `RaceResultIngestionService` (GUID + size + validation + transactional apply).
- Converter → `Content\streetcorsa\parts` → `PartsCatalog`. The content is now hand-maintained, and the script still defaults to overwriting it.
- Game ↔ AC process: `AcProcesses` (unknown = running), `AcInstallGate`, launcher lock held until restore.
- Mods folder → every content loader (`ModSet.SubFolders`/`JsonFiles`), part packs and scenes.
