# Trust-Surface Inventory

> Overwritten by Phase 3 of each security/all run. Generated 2026-10-18 @ cd77f51, diff mode (window b1e3c1d..cd77f51, 81 commits). Only **new or changed** surfaces are listed; everything else is as in the previous snapshot: `git show b1e3c1d:.claude/skills/tech-audit/references/attack-surface.md`. Paths are relative to `Street Corsa/` unless they start with `tools/`, `apps/` or `installer/`. Findings against these surfaces: `findings/2026-10-18-all.md`.

## 1. Writes outside the app's own folders (changed)

| Target | Site | Restore |
| ------ | ---- | ------- |
| AC car `data\*` | `Services/Assetto Corsa/CarDataOverlay.cs:272` (`KeptFile.Replace`, originals kept at :237 first; manifest written :478) | `Restore` :363-443, `RestoreSound` :486-499, start-up and `finally` |
| AC car sound GUIDs | `CarDataOverlay.cs:294` raw `File.WriteAllText` after `Keep` :287-288 | manifest `sfx` entry |
| Clone folder (`__sr_opponent`) | `CarDataOverlay.cs:562-605` (marker first :563, then `File.Copy`, raw writes) | `RemoveClones` :641-649 deletes only marked folders; `ClonesPath` list :727-754 |
| Unpacked ACD files | `CarDataOverlay.cs:818-832` raw `File.WriteAllBytes` / `File.Copy` | manifest |
| `Directory.Delete(recursive)` sites | `CarDataOverlay.cs:214` (keep folder), `:391` (car `data` dir when the original had none), `:443`, `:647-649` (clone) | each must be proven inside `_carsPath` / `_keepPath` |
| CSP modes `extension\lua\new-modes\sc_race`, `sc_free` | `Services/Assetto Corsa/ScRaceMode.cs:192-219` (SafeFile writes, stale-file delete under the marked target) | reinstalled each launch |
| **The whole install put back from the MSI uninstall** | **`Services/Assetto Corsa/AcUninstall.cs` (new): the game's `--uninstall` waits for AC, restores cars and cfg; run by the MSI's deferred `RestoreAc` action (`installer/Package.wxs:89`, `ExeCommand="--uninstall"`, `Return="ignore"`); the C# custom actions only read/set properties and launch the game at the exit dialog** | n/a (it *is* the restore) |
| AC `race.ini` keys (car part lists, paint tints, wear, cooling, format, free-run mode) | `Services/Configuration/IniModificationService.cs` (values partly from mod content via `RaceStartState.cs:78-162`, which leaves unsafe names out) | `AcConfigBackup` |
| Paint textures, wear layers, `.shell` caches | `Looks/CarPaint.cs:471-524`, `Looks/WearLayer.cs:273-274`, `Looks/ShellFile.cs:109-149` (SafeFile, app cache folders) | app-owned |
| Part price book `part_power.json` | `Services/Parts/CarPartsService.cs:109-119` (SafeFile, `%AppData%`) | app-owned, remeasured when the engine code changes (`PartPriceBook.cs:70` fingerprint) |
| Radio state/cache, AI portraits | `Audio/Radio/RadioLibrary.cs:57,246`, `Services/Ai/AiWriter.cs:315-316` | app-owned |
| **Installer**: settings.json read, game launched, registry `InstallerChoice` | `installer/Actions/CustomActions.cs:32, 130-162`, `Services/Settings/AcInstallFinder.cs:28-46` (registry reads) | n/a |

## 2. Untrusted-input parsers (changed)

- **Part packs as plain data (new format, d22f114)** — Newtonsoft readers with default settings: `Parts/PartsCatalog.cs:92` (`pack.json` → `PartPack`), `:247` (`part_aliases.json`), `:273` (`models.json`), `:305` (`build_aliases.json`), `:325` (builds); `Parts/PartRebuilds.cs:187` (`rebuilds.json`); `Parts/WorkOrder.cs:203`; `Parts/SlotShifts.cs:45`; `Parts/Sounds/SoundLibrary.cs:156, 220`. STJ via `ModJson.DocumentOptions`: `Parts/PartsFiles.cs:49` (`part_groups.json`, `part_classes.json`, `fuels.json`). **The figures (bore, lift, flow, inertia, gears, counts) now drive `Parts/Logic/EngineAssembly` arithmetic and loops directly** — NaN/∞/negative/huge values must be contained before `AcEngineData`/`AcAxleData` write AC data (`Parts/Export/AcEngineData.cs:84`, `AcAxleData.cs:117` take curve file names from the car's own `engine.ini` through `PathNames.IsSafeSegment`).
- **Part model paths from mods**: `PartsCatalog.cs:187, 550-556` (`TryCombineUnder` root / pack folder), `Parts/Cars/ShownParts.cs:91-93` (invalid chars → name + `.kn5` under the mode folder).
- **Binary caches the app wrote itself but a user may copy/corrupt**: `Looks/ShellFile.cs:57-180` (`BinaryReader`: counts via `Count(reader, most)` :98, vertices/indices arrays), `Services/Station/StationGround.cs:41-58` (`columns*rows` heights array from the file header).
- **Textures** decoded and tinted: `Looks/CarPaint.cs:567, 659, 785-816` (`width*height*4` allocations from KN5/DDS headers via AcTools `ReadAheadBinaryReader.ReadBytes(textureSize)`), `Looks/TexelReader.cs:92`, `Looks/PartGrime.cs:84`.
- **Mod JSON (STJ `ModJson.Options`)**: `Mods/ModSet.cs:500` (`mod.json`), `Mods/ModMerge.cs:59, 88` (`JsonNode.Parse` rules merge), `Audio/SoundCues.cs:88`, `Services/Garage/GarageProps.cs:61`, scenes (`Services/{Diner,PaintBooth,Station,Street,CarLot,PartShops}/*Scene.cs` through `SceneFiles.Load` :58-105 with `TryCombineUnder`/`IsSafeSegment`), `Services/Scripting/SceneMarks.cs:103`, `Services/Text/GameText.cs:191, 229`.
- **Radio audio** read into unmanaged memory: `Audio/AudioHub.Radio.cs:96-149` (`AllocHGlobal(length)`, `unsafe` copy :110-125, `CreateSoundExInfo` by hand :122-130), tags :205-237 (`DataLength` capped 64 KiB :234).
- **AI replies**: `Services/Ai/AiProviders.cs:207, 220` (`JsonNode.Parse` whole response), `Services/Ai/ComfyImageService.cs:164-183` (`prompt_id` into a URL via `Uri.EscapeDataString`; picture bytes capped `MostPictureBytes`), `Services/Ai/AiHttp.cs:27-47` (`ReadBytesAsync(most)`), talk JSON in `Services/Talk/*` (game decides prices: `Haggle.cs`).
- **Saves**: `Services/Storage/GameStateRepository.cs:147, 229` (save headers, cars no longer installed), `Services/Race/RaceResultProcessor.cs:247-257` (own `BsonMapper`, `null!` fields), `Services/Race/Validation/RaceResultValidator.cs:72` (result JSON, Newtonsoft default settings).
- **Settings**: `Services/Settings/GameSettingsService.cs:61, 152, 193` (legacy copy, read, SafeFile write), `AiSettings.ApiKeyProtected` via DPAPI (`Services/Ai/AiKeys.cs`).
- **Tools**: `tools/RunningGearParts/RunningGearParts/Program.cs:78` (`pack.json` JsonDocument), `Model.cs:71, 94` (`size*(1+size*3)` byte arrays), `tools/Shared/ObjModel.cs:88` (manifest JSON), `tools/StreetScene/.../Program.cs:49` (`light.json`).

## 3. Process launches (changed)

- **`installer/Actions/CustomActions.cs:160-162`** — launches the installed game with `UseShellExecute = true` at the end of the wizard (path from the MSI property); and the game's `--uninstall` on remove.
- `Services/Assetto Corsa/AssettoCorsaLauncher.cs` (`acs.exe`), `Audio/Radio/RadioPanelViewModel.cs`, `Screens/SoundBrowser/*` (`explorer.exe`) — unchanged in kind.

## 4. Native interop (changed)

- `Audio/Radio/RaceKeys.cs:44-74` — `WH_KEYBOARD_LL` hook (`LibraryImport`, `SetLastError`), `GetForegroundWindow` check added by the 2026-10-05 fix C; `_proc` kept alive; `finally` :185-197.
- `Audio/AudioHub.Radio.cs` — `AllocHGlobal` sound memory for FMOD `OPENMEMORY`, AM DSP chain :288-343 (`IntPtr[] _amChain`).
- `Audio/AudioHub.cs:46` `volatile IntPtr _system`, shutdown :361-373 (release order: groups → system).
- `Services/Assetto Corsa/CarDataOverlay.cs:837` — `DllImport("kernel32")` `CreateHardLink` (the only `DllImport` left; `LibraryImport` elsewhere).

## 5. Network (unchanged in kind)

One shared `HttpClient` (`Services/Ai/AiHttp.cs:12`, `SocketsHttpHandler`). Providers: Ollama, OpenAI-compatible, Anthropic (`x-api-key`), ComfyUI (`/prompt`, `/history/{id}`, `/view`, `/system_stats`). Endpoint and model from Settings; off by default. API key DPAPI-protected (`AiKeys.cs`). `DealerMapScreenViewModel.cs:141`, `PaintBoothScreenViewModel.cs:509` build file `Uri`s for images (local paths).

## 6. Cross-unit trust boundaries (changed)

- Game → `race.ini` → `sc_race/car_parts.lua` (changed: wear stands at the node, identity pose; parts' KN5 paths) and `sc_race/mode.lua`, `format_drag.lua`, `sc_free/mode.lua`.
- **Installer → game**: `CustomActions.cs` reads `%AppData%\StreetCorsa\settings.json` (AC path) and the registry for ACFOLDER; `Package.wxs:89` (`RestoreAc`) runs the game's `--uninstall` (`AcUninstall.cs`) to put the AC install back before removing the program; the game's `AcInstallFinder.InstallerChoice` reads the registry value the MSI wrote.
- **Save → part ids**: `PartShopService.RenameShelves`, `SavedParts` rename a save's part ids through `part_aliases.json` / `build_aliases.json` (a mod's aliases are attacker-controllable strings that become ids).
- EngineBench and the game share `Parts/**` by link: the same parsers and the same `EngineAssembly`.
