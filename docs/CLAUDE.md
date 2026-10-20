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
- **Navigation**: Single `NavigationService`; screens are gone to by their id in the flow (`flow\*.json`, `FlowBook`) and leave by named exits (`Leave(this, "back", game)`). Screens never instantiate or name other screens. Screens get their services through NavigationService (constructor parameters), never from `(App)Application.Current`.
- **Dialogs**: Modal overlays, never nested. One dialog at a time: one requested while another is open is queued and shows after it, never replacing it. A dialog closes before it runs its callback.
- **INI Files**: Declare intent, apply minimally, always restore after AC exits. Every cfg INI write goes through `IniModificationService` (`IniText`, line- and encoding-preserving, written atomically by `SafeFile`); the original is kept first under `%AppData%\StreetCorsa\AcRestore\~cfg` (`AcConfigBackup`) and put back in the launcher's `finally`, at start-up, on exit and on the fatal path, never while an AC process runs.
- **Saves**: One open save database at a time (`SaveDatabase`), shared by the game state and race session repositories; the Load screen lists saves by header without switching it.
- **Errors**: Global handlers (dispatcher, AppDomain, unobserved tasks) in `App.xaml.cs`. A recoverable UI exception is logged and shown in a dialog; a fatal one runs the fatal path once (no save: the game goes on from its last save; restore the AC install if AC is not running, shut FMOD down, close the save database, flush the log). Screens guard their own `async` paths; `AsyncRelayCommand` logs what escapes.
- **AC Content**: Import to catalog, never modify AC installation. Two exceptions. The game's own race mode (`apps\new-modes\sc_race`) is installed into `extension\lua\new-modes\sc_race` before every race (`ScRaceMode`: written when it differs, left there, the old `apps\lua\sr_race_manager` app removed). And a race: a car's data files and engine sound, as its parts make them, go in through `CarDataOverlay` (originals kept with a manifest, put back in the launcher's `finally` and at start-up, once no AC process runs), and an opponent in the player's model races in a marked copy of the car folder that the same cleanup deletes. Anything that scans `content\cars` skips folders with the copy marker (`AcCarFolder.IsClone`).
- **Logging**: Structured, via `IAppLogger`. Categories: App, Import, Market, Navigation, etc.
- **Content**: never add game content in C# or under `Assets` (the UI's own pictures only). It is a mod's: the base game's goes in `Content\streetcorsa`, read through `ModSet.Installed` like any mod's, so a mod can add to it or replace an entry (`"replaces"`). A save stamps the mods it was played with (`GameState.Mods`); what needs the content of a mod that is not installed is tidied on load (`ModsInSave`). The tests get the base game's JSON (not its models) copied into their output, so the services' defaults work there; their parts catalog is the base game's plus `tests\StreetCorsa.Tests\Fixtures\parts` (the three V8 packs' data without models, read as the base game's own: `RepoPaths.PartsRoots`), since the V8s are mods now and the tests of machining, rebuilds, the stand and cooling want a V8.
- **Rules**: a figure the game plays by (a price, a chance, a time, an hour) is never a C# constant: it goes in a part of `Rules.RuleBook` (one class per part in `Rules/`, its default the base game's figure) and is read as `RuleBook.Current.<part>.<figure>`; the base game's `Content\streetcorsa\rules\*.json` are written from the code by `RuleBookTests` (`SC_WRITE_BASE_RULES=1`), which otherwise checks they match. A career keeps its book in `GameState.RuleBook` from its start (`RuleBook.PlayBy`). The game's own settings (title, garage) are `game.json` (`Rules.GameFile`).
- **Words**: no player-facing word in XAML or C#: `{t:Text key}` in XAML, `GameText.Say(key, ("name", value))` in C#, `PartWords.Say` in the parts code the tools link, `say('KEY', english)` in the race mode (`race.*` pools); the key goes in the base game's `text/ui_*.json` with its placeholders in `_comment`. `WordsTests` checks every key exists and no screen's XAML writes words. Logs, exceptions, the mods' problem lines and the developer's screens stay English.
- **Release**: `installer\build.ps1` builds the per-user MSI (`artifacts\installer\StreetCorsa-<version>.msi`, the version from `Street Corsa.csproj`). It stages only the content Git has, so what `.gitignore` keeps local (the old showroom's model, the Bannochbrae station cut, the intro music, and every mod folder but the base game's and `bushradio`: `.gitignore` names the mods of ours that ship) never ships; the game's own garage and gas station models are tracked and ship; see `installer\README.md`. **The public game is the core only** (decided 2026-10-20): no engine sound bank (`*.bank` is ignored; the game harvests the installed cars' banks) and no mod but ours. The engine mods (`chrysler_v8`, `ford_v8`, `gm_v8`, once the base game's packs; `l79`, `flathead`, `chevy_six`, `sbc350`; each with its builds, aliases and `cars/cars.json` engine picks; the flathead test beside them) and the eight-bank sound pack live outside the repository in `D:\JUEGOS\ASSETTO CORSA\Mods\streetcorsa-engines` and `streetcorsa-sounds`; copy them into `Content` to play with them. The base game's only engine pack is `engines/ford_six` (the owner's own), with `engines/generic` and `engines/stock`. **Licences (decided 2026-10-20):** the code is MIT (`LICENSE.txt`), the game's own content CC BY 4.0 (`CONTENT-LICENSE.txt`, staged with the installer), other people's content under its own terms named beside it (CREDITS.txt, credits.csv, `credit` fields, all shown in Settings), and the name and logo are the owner's, not licensed (README "Trademarks": a modified version takes a name of its own). GPL is impossible: AcTools is Ms-PL.
- **Before a merge or push**: `tools/check.ps1` builds the whole solution (the tools compile game files by link, so a game change can break them) and the installer's custom actions, runs the tests, `EngineBench rated --base-only` and the race mode's harness.

## Data Models

| Model | Location | Purpose |
|-------|----------|---------|
| CarDefinition | catalog.db | Imported AC car identity (immutable) |
| CarProfile | catalog.db | Gameplay properties: BasePrice, DealerPrecedence; read with what the mods' `cars/cars.json` gives the car laid over it (`CarProfileRepository.Given`), never stored |
| UsedCarListing | saves/*.db | Car for sale in market |
| Car | saves/*.db | Player-owned car (`SkinId`, and `PaintColour` over it from the paint booth) |
| PartInstance | saves/*.db | A part somebody owns, with what is mounted on it: `Car.Parts` (engine on car slot 1, the wheels' running gear on 101-314, the rear axle on 401, a live axle carrying the rear brakes, coil springs, shocks and sway bar on its slots 5-11 (leaf springs stay on the car's 313-314), the front sway bar on 402), `Player.Parts` (shelf), `Player.EngineStand` (the engine on the stand), `Player.PartOrders` (mail order on its way), `Player.MachineWork` (left at a machine shop), `UsedCarListing.Parts`, `NewspaperAds.Parts`, a junkyard's `PartShopLocation.Yard`; `Cuts` is what a machine shop has cut on it (empty: as from the factory) |
| PartShopLocation | saves/*.db | A part shop's business (a `DealerLocation`), its shelves or yard, what the player has spent there |
| Opponent | saves/*.db | AI racer with Skill/Aggression traits |
| GameState | saves/*.db | Player money, date, cars, market |

## Current Systems

| System | Service | Purpose |
|--------|---------|---------|
| Catalog | ContentCatalogRepository, CarProfileService | Car import, profiles (priced by the mods' rules `cars` and makes); which installed cars the game uses is game.json's `cars` (`InstalledCars.Only`) |
| Cars and makes | CarBook, MakeFamilies, PartGroups | The mods' `cars/makes.json` (makes: words, family, country, badge price and rarity; countries) and `cars/cars.json` (a car's engine, layout, price, rarity, make; the developer's Car Catalog Editor saves into the base game's), the shelves of `parts/part_groups.json` (`PartKinds.GroupOf` gives an id, `GroupName` its words) |
| Market | UsedCarMarketService, CarValuation, AdBlurbs | Spawn listings, refresh, purchases; one formula for what a car is worth; every ad's few words, written once from the `ads.*` text pools (`AdBlurbs.Writer` is the seam for a better writer) |
| Dealers | DealerCatalog, DealerRoster, DealerLifeService | The dealers of the mods on their map; about 60% open, now and then one closes (closing-down sale the rivals shop) or opens (the `dealer_review` task; see `docs/systems/market-system.md`) |
| Part shops | PartShopCatalog, PartShopRoster, PartShopService, PartShopLifeService, MachineShopService | Make specialists, speed shops, wheel shops, junkyards and machine shops (the player's parts bored, ground, milled, ported; `Machining`, back by the `machine_work` task) on their own map (the garage's Parts dealers button), each with its own stock and prices, a regulars' discount, coming and going as the dealers do (the `partshop_review` task); the rivals tune from their shelves (see `docs/systems/parts-system.md`, "Part shops") |
| Opponents | OpponentInitializationService, OpponentLifeService, EngineTuner, OpponentEvolutionService | Rivals on the street: daily review (buy, repair, tune, go broke, come back), the King (see `docs/systems/opponent-system.md`) |
| Time | GameTimeScheduler, IScheduledTask | Time-based task execution |
| Race | AssettoCorsaLauncher, IniModificationService, RaceResultIngestionService | Launch AC, modify and restore configs, stop a race, read results (`LaunchResult.Outcome`, `PlayerMessages`) |
| Dialogs | DialogService | Modal overlays |
| Parts | PartsCatalog, EngineAssembly, EngineDyno, AcEngineData, AcRunningGearData | Parts as data (pack.json): the engine worked out from their specs, dyno, AC data (see `docs/systems/parts-system.md`) |
| Cars' looks | CarLooks, RustTask, WearLayer, BodyShell, WearPainter, CarPaint | Rust, worn paint, a cracked windscreen, dirt and dents drawn as a wear layer over the car's body in the scenes and in AC (`CAR_n_LOOK`), rolled by who sells a car, rust growing daily, a broken spring's corner sagging (see `docs/roadmap.md` step 28 ideas 3 and 4); a paint job in the paint booth: one of the mod's skins, or a colour of the player's own tinted into the car's paint textures, over the car's own in the scenes and in AC (`CAR_n_PAINT_k`, see `docs/screens/paint-booth.md`) |
| Cars' parts | CarPartsService, PriceBookStore, SavedPartsMigration, PartsWiring, NewspaperPartsService, Workbench, EngineFactory, RunningGearFactory, RunningGearGuess | Factory engine and running gear per car (the rear axle from cars.json's `running_gear`, else the parts' `came_in`; `AcAxleData` writes its final drive and differential), part trees on cars, plausibly tuned used cars, garage workbench (parts picked in 3D), the paper's used part ads (the kinds `for_sale` in part_groups.json), the mail order (a premium and three days' delivery, the part shops' map's button); the price book measured once and kept on disk, never waited for on the UI thread; a save's parts brought in step with the catalog on load; the process-wide price seams wired once at composition |
| Race data | RaceCarDataService, CarDataOverlay | What the parts make of a car goes into its AC data for a race and comes out after (see `docs/systems/parts-system.md`, "Apply and restore") |
| Engine sounds | SoundLibrary, SoundMatcher, AcCarSound | A sound per engine block: every mod's `sounds` folders plus every installed car's bank, swapped in for the race by `CarDataOverlay` |
| Game sound | AudioHub, SoundCues, Sfx, PlayingSound | One FMOD system (AC's DLLs) for the app: buses with the Settings volumes, a fade out during races and behind other windows, cues from the mods' `sfx` folders (clicks, garage work, each screen's air via `IScreen.Ambience`) |
| Radio | RadioPlayer, RadioQueue, RadioLibrary, RadioPanel | The player's own music from any folders on the PC (radio.json in %AppData%): shuffle, repeat, lists, resume; read into memory so no file is held; an AM car radio on screens with `IScreen.InCar`; F2 or the garage's radio opens the panel, R switches it on and off (off keeps the song and place, saved at once and at quit; the radio comes back on or off as it was left, once a game is loaded, never at the main menu), a dash radio on the Cruise screen; in a race it goes off and `RaceKeys` (a listening low-level keyboard hook, set only while AC runs) lets R and the media keys work it over AC, as the car's AM set |
| Diners by level | DinerBook, DinerCardViewModel, RacersOut.AtDiner | The mods' `diners` (the base game's four): each a horsepower class (`powerFrom`), a street with a diner.json (else seen on the first's), stakes and a name; a racer is a regular of the diner their car's power puts them in, a boss of the top one; the regulars of a diner above the player's car won't race it (`DinerBook.Snubs`, refused in `ChallengeLauncher` too); the racing hub's Hit the streets opens the diners' map (`diner_map`, the dealer map screen's third mode: `mapX`/`mapY`, a click drives there; a diner's Other diners button drives off to it), the diner picked travels in `FlowArgs.DinerId` and `LaunchIntent.ReturnDiner` |
| Mods | ModSet, ModIds, ContentRegistry, ModJson | All game content (parts, sounds, dealers, part shops, racers, events, milestones, victory paths, streets, diners, gas stations, paint booths, warehouses, rooms, words) comes from `Content\<mod>`, the base game (`Content\streetcorsa`) first; ids namespaced by mod, the base game's without (see `docs/modding.md`) |
| Rules | RuleBook, GameFile, ModMerge, ModProblems | Every figure from the mods' `rules` (merged key by key, kept in the save from the career's start), the game's own settings from `game.json`, every mod problem shown at start-up (see `docs/total-conversion.md`) |
| Career and words as data | CareerContent, DataVictory, GameText | Events, milestones and victory paths from the mods' `events`/`milestones`/`victories`; everything the game says (screens, messages, the race mode's lines, paper, talk, names) from their `text` pools, in the player's language (`text/<language>/`), the when-to-say-what in code |
| Player's AI | AiHub, AiTextService, ComfyImageService, AiWriter, AiTalkService | Optional, off by default, never waited on: Ollama / OpenAI-compatible / Anthropic text and ComfyUI images; rivals' lines, street talk and paper pieces told again, ad blurbs, new faces' portraits; kept in the save, the game's own words always the fallback (see `docs/ai-integration.md`) |
| Talking to people | TalkDesk, Conversation, TalkPeople, TalkFacts, Haggle | With the AI on: talk to sellers (phone, curb), a dealer's salesman (talks first, on the lot) and rivals (Diner, Cruise); the game gives the facts every turn and decides every price (`ai.talk` rules), people remember for a while (`GameState.People`), rival races recorded (`GameState.RivalRaces`) |

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
NavigationService.Leave(this, "back", gameState, args?) : bool
  → the flow says where the screen's exit leads (none open: home, or the main menu with no game)
  → Go(id, gameState, FlowArgs) : bool, builds the screen by its kind (ScreenKinds) with the services from its fields
  → SafeNavigate(name, create, id): records the flow id, calls NavigateTo(screen) internally
  → Screen.Enter() lifecycle hook (loading happens here, not in the constructor)
  → A constructor or Enter() that throws: logged, dialog shown, previous screen kept, returns false
```
- Also `GoFrom(this, id, ...)` (a hub's tile), `GoHome`, `GoHomeLater` (from an `Enter`), `GoToMenu(card)`,
  `NavigateToStart()` (startup only), `CanLeave(this, exit)` (show a button only while its exit leads somewhere).
- `FlowArgs` carries what a screen is told: the shop, the dealer, the car, the race, skip the animation, the car
  arriving, back from a race, end the day, the menu's card.
- Races: `GoRace(gameState, intent)` sets `intent.ReturnTo` to the screen on show; the race screen goes back there
  (or where its `after_race` exit leads), home when towed.
- Screens with the car on a 3D scene (`IScriptedScreen`) call `ScreenReady(this)` once up; their `screen.<id>.enter`
  and `.leave` scripts play then and on `Leave`/`GoFrom`. See `docs/architecture/navigation.md`.

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

**New Screen** (another screen of a kind there is needs only an entry in `flow\*.json`):
1. Create `Screens/[Name]/[Name]ScreenViewModel.cs` (inherit BaseScreenViewModel)
2. Create `Screens/[Name]/[Name]ScreenView.xaml` + `.xaml.cs`
3. Add DataTemplate in App.xaml
4. Add a `ScreenKinds` constant in `Navigation/FlowBook.cs`, in `ScreenKinds.All` (and `SystemOf`, `NeedsWhat`, `IsOverlay`, `CanEndDay` if they concern it)
5. Add a case for the kind in `NavigationService.Go` through `SafeNavigate(name, ..., id)`, passing services from its fields (add a constructor parameter for a new one) and what it is told from `FlowArgs`
6. Add it to `Content\streetcorsa\flow\flow.json` with its exits, and the exits or tiles that lead to it

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
- [The player's AI](ai-integration.md)
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
