# Street Corsa - AI Context

## Project Summary
Street Rod-style career mode manager for Assetto Corsa. WPF app manages game logic, economy, progression. AC is physics engine only - never modified directly.

## Tech Stack
- .NET 10, WPF, MVVM pattern
- LiteDB for persistence
- Single main window, screen-based navigation

## Architecture Layers
1. **Meta-Game Layer** - Player progression, economy, car ownership, saves
2. **AC Integration Layer** - Launch via acs.exe, INI modification, result ingestion
3. **Data Layer** - Dual-database: catalog.db (static) + saves/*.db (dynamic), both under `%AppData%\StreetCorsa` (`Catalog\catalog.db`, `Saves\{name}.db`, next to `settings.json`; see `docs/architecture/data-storage.md`)

## Key Architectural Rules
- **Navigation**: Single `NavigationService` with typed factory methods. Screens never instantiate other screens. Screens get their services through NavigationService (constructor parameters), never from `(App)Application.Current`.
- **Dialogs**: Modal overlays, never nested. One dialog at a time: one requested while another is open is queued and shows after it, never replacing it. A dialog closes before it runs its callback.
- **INI Files**: Declare intent, apply minimally, always restore after AC exits. Every cfg INI write goes through `IniModificationService` (`IniText`, line- and encoding-preserving, written atomically by `SafeFile`); the original is kept first under `%AppData%\StreetCorsa\AcRestore\~cfg` (`AcConfigBackup`) and put back in the launcher's `finally`, at start-up, on exit and on the fatal path, never while an AC process runs.
- **Saves**: One open save database at a time (`SaveDatabase`), shared by the game state and race session repositories; the Load screen lists saves by header without switching it.
- **Errors**: Global handlers (dispatcher, AppDomain, unobserved tasks) in `App.xaml.cs`. A recoverable UI exception is logged and shown in a dialog; a fatal one runs the fatal path once (no save: the game goes on from its last save; restore the AC install if AC is not running, shut FMOD down, close the save database, flush the log). Screens guard their own `async` paths; `AsyncRelayCommand` logs what escapes.
- **AC Content**: Import to catalog, never modify AC installation. Two exceptions. The game's own race mode (`apps\new-modes\sc_race`) is installed into `extension\lua\new-modes\sc_race` before every race (`ScRaceMode`: written when it differs, left there, the old `apps\lua\sr_race_manager` app removed). And a race: a car's data files and engine sound, as its parts make them, go in through `CarDataOverlay` (originals kept with a manifest, put back in the launcher's `finally` and at start-up, once no AC process runs), and an opponent in the player's model races in a marked copy of the car folder that the same cleanup deletes. Anything that scans `content\cars` skips folders with the copy marker (`AcCarFolder.IsClone`).
- **Logging**: Structured, via `IAppLogger`. Categories: App, Import, Market, Navigation, etc.
- **Content**: never add game content in C# or under `Assets` (the UI's own pictures only). It is a mod's: the base game's goes in `Content\streetcorsa`, read through `ModSet.Installed` like any mod's, so a mod can add to it or replace an entry (`"replaces"`). A save stamps the mods it was played with (`GameState.Mods`); what needs the content of a mod that is not installed is tidied on load (`ModsInSave`). The tests get the base game's JSON (not its models) copied into their output, so the services' defaults work there.
- **Before a merge or push**: `tools/check.ps1` builds the whole solution (the tools compile game files by link, so a game change can break them) and runs the tests.

## Data Models

| Model | Location | Purpose |
|-------|----------|---------|
| CarDefinition | catalog.db | Imported AC car identity (immutable) |
| CarProfile | catalog.db | Gameplay properties: BasePrice, DealerPrecedence |
| UsedCarListing | saves/*.db | Car for sale in market |
| CarInstance | saves/*.db | Player-owned car |
| PartInstance | saves/*.db | A part somebody owns, with what is mounted on it: `Car.Parts` (engine on car slot 1), `Player.Parts` (shelf), `Player.EngineStand` (the engine on the stand), `Player.PartOrders` (mail order on its way), `UsedCarListing.Parts`, `NewspaperAds.Parts`, a junkyard's `PartShopLocation.Yard` |
| PartShopLocation | saves/*.db | A part shop's business (a `DealerLocation`), its shelves or yard, what the player has spent there |
| Opponent | saves/*.db | AI racer with Skill/Aggression traits |
| GameState | saves/*.db | Player money, date, cars, market |

## Current Systems

| System | Service | Purpose |
|--------|---------|---------|
| Catalog | ContentCatalogRepository, CarProfileService | Car import, profiles |
| Market | UsedCarMarketService, CarValuation | Spawn listings, refresh, purchases; one formula for what a car is worth |
| Dealers | DealerCatalog, DealerRoster, DealerLifeService | The dealers of the mods on their map; about 60% open, now and then one closes (closing-down sale the rivals shop) or opens (the `dealer_review` task; see `docs/systems/market-system.md`) |
| Part shops | PartShopCatalog, PartShopRoster, PartShopService, PartShopLifeService | Make specialists, speed shops, wheel shops and junkyards on their own map (the garage's Parts dealers button), each with its own stock and prices, a regulars' discount, coming and going as the dealers do (the `partshop_review` task); the rivals tune from their shelves (see `docs/systems/parts-system.md`, "Part shops") |
| Opponents | OpponentInitializationService, OpponentLifeService, EngineTuner, OpponentEvolutionService | Rivals on the street: daily review (buy, repair, tune, go broke, come back), the King (see `docs/systems/opponent-system.md`) |
| Time | GameTimeScheduler, IScheduledTask | Time-based task execution |
| Race | AssettoCorsaLauncher, IniModificationService, RaceResultIngestionService | Launch AC, modify and restore configs, stop a race, read results (`LaunchResult.Outcome`, `PlayerMessages`) |
| Dialogs | DialogService | Modal overlays |
| Parts | PartsCatalog, PartScriptRuntime, EngineDyno, AcEngineData, AcRunningGearData | SLRR parts: run their scripts on the player's build, dyno, AC data (see `docs/systems/parts-system.md`) |
| Cars' parts | CarPartsService, NewspaperPartsService, Workbench, EngineFactory, RunningGearFactory | Factory engine and running gear per car, part trees on cars, plausibly tuned used cars, garage workbench (parts picked in 3D), the newspaper's parts pages (used ads, mail order with a premium and three days' delivery) |
| Race data | RaceCarDataService, CarDataOverlay | What the parts make of a car goes into its AC data for a race and comes out after (see `docs/systems/parts-system.md`, "Apply and restore") |
| Engine sounds | SoundLibrary, SoundMatcher, AcCarSound | A sound per engine block: every mod's `sounds` folders plus every installed car's bank, swapped in for the race by `CarDataOverlay` |
| Game sound | AudioHub, SoundCues, Sfx, PlayingSound | One FMOD system (AC's DLLs) for the app: buses with the Settings volumes, a fade out during races and behind other windows, cues from the mods' `sfx` folders (clicks, garage work, each screen's air via `IScreen.Ambience`) |
| Radio | RadioPlayer, RadioQueue, RadioLibrary, RadioPanel | The player's own music from any folders on the PC (radio.json in %AppData%): shuffle, repeat, lists, resume; read into memory so no file is held; an AM car radio on screens with `IScreen.InCar`; F2 or the garage's radio opens the panel, R switches it on and off (off keeps the song and place, saved at once and at quit; the radio comes back on or off as it was left, once a game is loaded, never at the main menu), a dash radio on the Cruise screen; in a race it goes off and `RaceKeys` (a listening low-level keyboard hook, set only while AC runs) lets R and the media keys work it over AC, as the car's AM set |
| Mods | ModSet, ModIds, ContentRegistry, ModJson | All game content (parts, sounds, dealers, part shops, racers, events, milestones, victory paths, streets, gas stations, warehouses, rooms, words) comes from `Content\<mod>`, the base game (`Content\streetcorsa`) first; ids namespaced by mod, the base game's without (see `docs/modding.md`) |
| Career and words as data | CareerContent, DataVictory, GameText | Events, milestones and victory paths from the mods' `events`/`milestones`/`victories`; everything the game says (paper, talk, street talk, names) from their `text` pools, the when-to-say-what in code |

## File Structure
```
Street Corsa/
├── Models/           # Data models (Catalog/, GameState/)
├── Services/         # Business logic (Catalog/, Market/, Scheduler/, etc.)
├── Screens/          # UI screens (ViewModel + View per screen)
├── Navigation/       # NavigationService, BaseScreenViewModel
├── Dialogs/          # Dialog system (Confirmation, Information, etc.)
├── Styles/           # WPF ResourceDictionaries
└── Logging/          # IAppLogger infrastructure
```

## Screen Navigation Pattern
```
NavigationService.NavigateTo[ScreenName](dependencies) : bool
  → SafeNavigate: creates ScreenViewModel with the services from NavigationService's fields
  → Calls NavigateTo(screen) internally
  → Screen.Enter() lifecycle hook (loading happens here, not in the constructor)
  → A constructor or Enter() that throws: logged, dialog shown, previous screen kept, returns false
```

## Opponent System (AC-Agnostic)
- `Opponent` model stores Skill (90-100; the floor of 90 is deliberate, commit 61ca613: below it AC's AI drives too badly to make a race. Never lower it) and Aggression (0-100)
- `OpponentAIAdapter` converts to AC AI parameters at runtime (never persisted)
- Evolution happens after races via `OpponentEvolutionService`
- The rivals live day to day (`OpponentLifeService`, the `opponent_review` task): `Cars[0]` is the car a rival drives,
  `Car.PowerHp` its dyno figure; what they do goes into `GameState.StreetTalk` (the diner's "word on the street")

## Market System
- `CarProfile.DealerPrecedence` controls spawn probability (0.0=rare, 1.0=common)
- Daily refresh: remove old listings, spawn new based on precedence, on the open lots only (`DealerRoster.Restocking`)
- Condition affects price (better condition = higher price)

## AC-Side Components (Outside C# Project)

| Component | Location | Purpose |
|-----------|----------|---------|
| Street Corsa race mode | `apps\new-modes\sc_race\` (installed to `extension\lua\new-modes\sc_race\` by `ScRaceMode`) | Auto-start, crash and false-start judging, race results JSON, auto-quit; physics API via `ALLOW_PHYSICS_ALTERATIONS` |
| Crash Penalty Mode | `C:\GAMES\Street Rod AC\extension\lua\new-modes\crash-penalty-tournament\` | Penalty tracking (unused) |
| FFB Limiter | `C:\GAMES\Street Rod AC\extension\lua\ffb-postprocess\upper-limit\` | Direct drive protection |
| SR Race Manager (Lua app, removed) | was `apps\lua\sr_race_manager\` | Became the race mode 2026-09-24 (an app gets no physics API); `ScRaceMode` deletes it from the install |
| Python Race App (removed) | was `apps\python\StreetRodRaceApp\` | Superseded by SR Race Manager; deleted 2026-09-24, in git history only |

**Key Integration Points**:
- Every race, drag races too, is a one-lap race session (`TYPE=3`, never AC's drag session, whose rules teleport the cars) in the `sc_race` CSP mode; a test-and-tune is the same race session with the player alone (`CARS=1`) and `TuneLaps` (50) laps, so AC never ends it and the mode ends it after its passes (never read `lapsCompleted >= 1` as finished there): `[RACE] __CM_CUSTOM_MODE=sc_race` and `[STREET_CORSA] RACE_TYPE` in race.ini (`IniModificationService`), with AC's damage and tyre wear on in assists.ini for the race (kept and restored like race.ini)
- The mode writes results to `Documents/Assetto Corsa/out/sc_race/*.json` (schema 1.7: `end_reason`, `false_start`, `disqualified`, the car's `condition` (with its fuel, dirt and engine `heat`), breakdowns and timeslips, the police chase's `pursuit` (the police cars are never participants), bracket dial-ins and breakouts, and a test-and-tune's `passes`) and quits via `ac.shutdownAssettoCorsa()`
- Launcher reads JSON results after AC process exits
- Launcher refuses to start while any AC process runs (one it did not start too), and tells the player what stayed changed in AC after a restore that failed (`AssettoCorsaLauncher.RestoreProblemsMessage`)

## Quick Reference - Adding Features

**New Screen:**
1. Create `Screens/[Name]/[Name]ScreenViewModel.cs` (inherit BaseScreenViewModel)
2. Create `Screens/[Name]/[Name]ScreenView.xaml` + `.xaml.cs`
3. Add `bool NavigateTo[Name]()` in NavigationService through `SafeNavigate`, passing services from its fields (add a constructor parameter for a new one)
4. Add DataTemplate in App.xaml

**New Scheduled Task:**
1. Implement `IScheduledTask` (TaskId, IntervalDays, ExecuteAsync)
2. Register in App.xaml.cs via `Scheduler.RegisterTask()`

**New Dialog:**
1. Create ViewModel in `Dialogs/[Name]/`
2. Create View XAML
3. Add DataTemplate in App.xaml
4. Show via `DialogService.ShowDialog(viewModel)` (queued if another dialog is open)

## Documentation Index
- [Modding](modding.md): the Content folder, mod.json, ids and `replaces`, every content type's format
- [Roadmap](roadmap.md): next steps (damage, timeslips, economy, living opponents, police), decisions and research
- [Architecture Overview](architecture/overview.md)
- [Data Storage](architecture/data-storage.md)
- [Navigation System](architecture/navigation.md)
- [Catalog System](systems/catalog-system.md)
- [Market System](systems/market-system.md)
- [Opponent System](systems/opponent-system.md)
- [Parts System](systems/parts-system.md)
- [AC Launcher](ac-integration/launcher.md)
- [CSP Lua Scripts](ac-integration/csp-lua-scripts.md)
- [Python Race App (removed, for reference)](ac-integration/python-app.md)
- [Screens Index](screens/index.md)
