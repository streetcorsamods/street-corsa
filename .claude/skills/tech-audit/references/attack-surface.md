# Attack Surface

> Overwritten by each tech-audit run — a snapshot, not history. Snapshot of 2026-11-05 @ c51b24a7, **diff mode**
> (window a56d653f..HEAD, 317 changed game files, 41 new). Lists only surfaces that are new or changed in the window; the
> full inventory of the previous audit is `git show 64cd0d6f:.claude/skills/tech-audit/references/attack-surface.md`
> and still holds for everything not named here.

## Units

Unchanged set: Street Corsa (WPF), StreetCorsa.Tests, EngineBench, InstallerActions (net472), sc_race / sc_free (CSP
modes), scripts (`tools/check.ps1`, `installer/*.ps1` incl. `pack-mod.ps1`), Python helpers (new: `tools/IntroArt/make_intro_art.py`).

## New: mod import and Mod Manager (untrusted archives → the game's Content folder)

| Site | What | Guard to check |
| --- | --- | --- |
| `Mods/ModImport.cs:127, :181, :451, :471` (added 88bcda1b) | opens a user-picked `.zip`, reads `mod.json`, extracts entries with `ExtractToFile(overwrite: true)` into a stage, then `Directory.Move` stage → Content target (:357-:369, rollback move on failure) | zip-slip (entry names with `..`, rooted, drive), zip bomb (entry count / uncompressed size), lock file `:423` |
| `Mods/ModManager.cs:81, :100-:104` (79612009) | disable/enable = move a mod folder aside; remove = move to `gone` then `Directory.Delete(recursive)` | delete target proven under the Content/aside root |
| `Screens/MainMenu/ModInstallFlow.cs` (88bcda1b) | UI flow around the import | errors surfaced, no half-installed mod on cancel |
| `Services/Storage/ModsArrival.cs:91` | mods found at load (CS8600 null path) | |

## Writes into the AC install / cfg (changed in window)

- `Services/Assetto Corsa/CarDataOverlay.cs` (18 grep hits), `.Clones.cs` (12), `.Sound.cs` (9) — data.acd moved aside so AC reads the data folder (PR #21, see memory "AC reads data.acd first"); car wheel/tyre/spring changes feed it.
- `Services/Assetto Corsa/AcInstallGate.cs` (new, 4a14b72d) — install re-look per game (stamp skip, PR #44).
- `Services/Assetto Corsa/ScRaceMode.cs`, `Services/Race/ModeStartMark.cs` (new, 69126276; deletes its mark file :81).
- `Services/Configuration/IniModificationService.cs`, `AcConfigBackup.cs`, `KeptFile.cs`, `RaceIniBuilder.cs`, `Models/Modification*`, `Models/LaunchIntent*` — race.ini for multi-rival fields (up to 20) and formats.
- `Services/Catalog/BaseCarsFile.cs`, `CarImportService.cs`, `CatalogDatabase.cs`.

## Untrusted-input parsers (changed / new)

- `Looks/TexelReader.cs` (new, 6f4b4cc6) and `Looks/CarPaint.cs` — texture bytes from car mods.
- `Parts/Export/AcCarDataReader.cs`, `Parts/Cars/ShownParts.cs` (new) — AC car data, wheel/tyre models.
- `Parts/Sounds/SoundLibrary.cs`, `Audio/SoundFiles.cs`, `Audio/EngineLoudness.cs`, `Audio/AudioHub.Radio.cs` (BinaryReader + 13 interop hits).
- `Services/Parts/PriceBookStore.cs` (new, 7d6fbb76) — kept dyno measurements, written via `SafeFile`.
- Race result JSON: `Models/Race/RaceResultJson.cs`, `RaceStartState.cs`, `RaceContext.cs`, `Services/Race/RaceResultIngestionService.cs`.
- Saves: `Services/Storage/SaveMapper.cs` (BsonMapper), `GameStateRepository.cs`.
- Content data: `Content/streetcorsa/**` (rules, text, flow, formats, prompts, parts books) and their readers under `Rules/`, `Mods/`, `Navigation/`.

## Process launches (changed)

`Services/Content/ContentFolder.cs:16` (new: opens a folder with `UseShellExecute`), `Audio/Radio/RadioPanelViewModel.cs`,
`Screens/MainMenu/MainMenuScreenViewModel.cs`, `Screens/SoundBrowser/SoundBrowserScreenViewModel.cs`,
`Services/Assetto Corsa/AcProcessHost.cs`, `AssettoCorsaLauncher.cs`, `installer/Actions/CustomActions.cs` (5).

## Native interop (changed)

`Controls/D3DViewportBase.cs`, `Controls/SharedTextureBridge.cs` (7), `Audio/FmodPlugins.cs`, `Audio/AudioHub.Radio.cs`,
`Mods/ModImport.cs` (2 Marshal/interop hits), `Screens/MainMenu/ModInstallFlow.cs`.

## Network (changed)

`Services/Ai/AiHttp.cs`, `AiTextService.cs`, `AiProviders.cs` (6), `ComfyImageService.cs` (3) — one `new HttpClient` site in the game.

## Cross-unit trust boundaries

Unchanged in shape: mod zip → `Content\<mod>` → runtime catalog (now also through the Mod Manager); Lua mode → result JSON
→ launcher; game ↔ AC process; installer custom actions → the game's `--uninstall`.
