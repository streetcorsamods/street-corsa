# Trust-Surface Inventory

> Overwritten by Phase 3 of each security/all run. Generated 2026-10-05 @ b1e3c1d, diff mode (window e5bfd8c..b1e3c1d, 204 commits). Only **new or changed** surfaces are listed; everything else is as in the previous snapshot: `git show e5bfd8c:.claude/skills/tech-audit/references/attack-surface.md`. Paths are relative to `Street Corsa/` unless they start with `tools/` or `apps/`. Findings against these surfaces: `findings/2026-10-05-all.md`.

## 1. Writes outside the app's own folders (changed)

| Target | Site | Restore |
| ------ | ---- | ------- |
| AC car `data\*` | `Services/Assetto Corsa/CarDataOverlay.cs` Apply now goes through `KeptFile.Replace` (:272) — the 2026-09-29 raw write is fixed | as before (manifest-first) |
| AC car sound GUIDs | `CarDataOverlay.cs:294` raw `File.WriteAllText` (after the original is kept) | manifest `sfx` entry, :496-503 |
| Clone data + GUIDs | `CarDataOverlay.cs:565-607` raw writes into the `__sr_opponent` clone (own folder, marker first) | `RemoveClones` |
| `CarDataOverlay.cs:821` | raw `File.WriteAllBytes` batch (unpacked ACD files) | manifest |
| CSP modes `extension\lua\new-modes\sc_race\` **and `sc_free\`** (new: free run with parts the model lacks) | `Services/Assetto Corsa/ScRaceMode.cs:103-177` (SafeFile, stale-file delete, folder delete) | reinstalled each launch |
| AC `race.ini` new keys: car part lists, `CAR_n_PAINT_k` tints, wear, free-run mode id | `Services/Configuration/IniModificationService.cs` (values partly from mod content: part KN5 paths, skin names) | `AcConfigBackup` |
| Tinted paint textures, wear layers, shell files | `Looks/CarPaint.cs:522-525`, `Looks/WearLayer.cs:267`, `Looks/ShellFile.cs:147` (SafeFile, app cache folders) | app-owned; never into the car folder (by design) |
| AI pictures | `Services/Ai/AiWriter.cs:328` (SafeFile) | app-owned |
| Radio/sound caches | `Audio/Radio/RadioLibrary.cs:57, 203`, `Audio/SoundFiles.cs:203` (SafeFile) | app-owned |

## 2. Untrusted-input parsers (new)

- **Mod JSON through `ModJson.Options` (STJ)**: `Mods/ModSet.cs:500` (`mod.json`), `Audio/SoundCues.cs:82`, `Services/Garage/GarageProps.cs:59`, `Services/PartShops/ShopScene.cs:120` (own `Options`), `Rules/*` (data engines, formats, booth colours, market), car scripts (`Services/Scripting`). Paths inside these files (KN5, textures, sounds) must stay under the mod folder.
- **Skin and car textures** decoded and tinted: `Looks/CarPaint.cs`, `Looks/WearLayer.cs` (dimensions from the file → allocations).
- **Radio audio files** from user folders, read into unmanaged memory: `Audio/AudioHub.Radio.cs:83-112`, tags read back with `Marshal.PtrToStringAnsi`/`Marshal.Copy` :154, :185.
- **AI replies** (LLM text and JSON, ComfyUI images): `Services/Ai/AiProviders.cs:154-160` (`JsonNode` of the whole response), `Services/Ai/ComfyImageService.cs:139+`, talk/clerk JSON search parsed in `Services/Talk`. The game must decide prices and stock, never the model.
- Newtonsoft readers unchanged in kind: `Parts/PartsCatalog.cs:93, 205, 231`, `Parts/WorkOrder.cs:195`, `Parts/SlotShifts.cs:45`, `Parts/Sounds/SoundLibrary.cs:155, 218`, `Services/Race/Validation/RaceResultValidator.cs:65`, `Services/Settings/GameSettingsService.cs:124`, `CarDataOverlay.cs:369, 737`, `AcConfigBackup.cs:211`.

## 3. Process launches (new)

- `Audio/Radio/RadioPanelViewModel.cs:293` — `explorer.exe "<radio folder>"`, `UseShellExecute=false`.
- `Screens/SoundBrowser/SoundBrowserScreenViewModel.cs:267` — `explorer.exe /select,"<file>"` (path from the sound list).
- `Services/Assetto Corsa/AssettoCorsaLauncher.cs:548-556` — `acs.exe` (unchanged in kind).

## 4. Native interop (new)

- `Audio/Radio/RaceKeys.cs:44-98` — global low-level keyboard hook (`SetWindowsHookEx(WH_KEYBOARD_LL)`) during races for the radio keys; must unhook on every exit path and keep `_proc` alive.
- `Audio/AudioHub.Radio.cs` — `AllocHGlobal` sound memory handed to FMOD `OPENMEMORY`, freed in a release callback; `unsafe` block :88.
- `Audio/FmodPlugins.cs`, `Audio/FmodStudio.cs` — 53 `LibraryImport`, `NativeLibrary.SetDllImportResolver` :109.

## 5. Network (new — first network surface in the game)

- One shared `HttpClient` (`Services/Ai/AiHttp.cs:11`, `SocketsHttpHandler`). Providers: Ollama (`/api/chat`, `/api/ps`, `/api/create`, `/api/tags`), OpenAI-compatible (`/chat/completions`, `/models`), Anthropic (`/v1/messages`, `/v1/models`, `x-api-key`), ComfyUI (`/prompt`, history, view). Endpoint and model from Settings; **off by default**.
- API key: DPAPI-protected in settings (`Services/Ai/AiKeys.cs`, `AiSettings.ApiKeyProtected`), sent as `Authorization: Bearer` or `x-api-key` to the configured endpoint only.
- What leaves the machine when on: game facts for talk (car, seller, prices, player's name/messages), clerk search prompts, portrait prompts.

## 6. Cross-unit trust boundaries (changed)

- Game → `race.ini` → `sc_race/car_parts.lua`: part KN5 paths, engine offsets, paint tints, wear layers loaded with `loadKN5` / `applySkin` inside AC.
- Game → `sc_free` mode for free runs with parts.
- Ollama `streetcorsa` model created by the game (`/api/create`) from a Modelfile in content.
