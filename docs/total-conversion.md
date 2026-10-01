# Total conversion: making the whole game moddable

The goal (the owner, 2026-09-30): the entire game moddable, so the community can add new game modes and whole new
games from `Content\<mod>\` without touching C#. The 1960s American street-racing setting is the base game's taste,
not the program's: someone should be able to make a Tokyo Drift game (modern JDM cars, yen, km/h, turbos, drift and
touge races, a different look) on the same program.

This file is the inventory of what is still baked into C#, XAML, the exe or the race mode's Lua (taken 2026-09-30,
after PR #50), and the plan for the epic. Step 16 (`docs/modding.md`) already made the content data: parts, sounds,
sfx, props, dealers, part shops, racers, events, milestones, victories, streets, stations, warehouses, showcase,
garages and text pools. What is left is the program around them.

Effort: **easy** = a constant or a literal moved into data with today's value as the default; **medium** = many call
sites or a new loader; **hard** = logic or structure, not numbers.

## 1. Words

| What | Where | Now | Effort |
|---|---|---|---|
| Window title, intro "STREET CORSA / POWERED BY ASSETTO CORSA" | `MainWindow.xaml:11`, `Screens/MainMenu/IntroView.xaml:32-53` | XAML literals | easy |
| Menu, hub and button labels, tooltips, headers | about 320 literals in XAML (Diner 38, Garage 39, RadioPanel 28, CareerDialog 25, NewGame 18, workbench 17, Newspaper 13...) | XAML literals; **no XAML uses `GameText`** | medium (volume) |
| Messages, statuses, dialog text | about 400 player-facing literals in C# (GarageVM 53, App.xaml.cs, workbench, Cruise, RaceLoading, Diner, Station, Services/Race, Services/Market...) | C# literals, many `$""` | medium |
| Difficulty summaries, reputation tier words | `GameRulesEditor.cs:36-42`, `RacerStats.cs:69-129` | C# | easy |
| In-race messages ("You won a few bucks", "Busted!") | `apps/new-modes/sc_race/mode.lua:195-211` | Lua literals | easy |

Shape: a `{t:Text key}` markup extension and `GameText.One(key, args)` over `text/ui.json` (one line per key,
`{placeholders}` for values); the race mode gets its lines through `[STREET_CORSA]` in race.ini.

## 2. Look

| What | Where | Now | Effort |
|---|---|---|---|
| Colours | `Styles/Colors.xaml` (palette), `Styles/Brushes.xaml` (105 keys); 19 hex + about 31 named colours in views, 13 in C# | compiled XAML, all `StaticResource` (1,866 uses) | medium |
| Fonts | no font keys; Georgia, Consolas, Impact, Courier New inline in about 15 places; body text is Segoe UI | XAML literals | medium |
| Sizes | `Styles/Typography.xaml` (19 keys) | compiled XAML | easy |
| Control styles | `Styles/Buttons.xaml` (469 lines), `Inputs.xaml` (1033), `Radio.xaml` (the AM radio) | compiled XAML | colours/sizes easy, full restyle hard |
| Backgrounds, menu button art | `Brushes.xaml:124-216` (main, init, newspaper, diner, settings; new_game, load_game...) | `pack://` resources in the exe | easy-medium |
| Hub icons | `Styles/Icons.xaml:7-17` | `pack://` | easy |
| Calendar pictures, driving licence | `GarageScreenViewModel.cs:250`, `NewGameScreenView.xaml:16` | `pack://` | easy |
| Window/taskbar icon | `street_corsa.ico` | compiled in; the window can take one at run time (the exe's own icon cannot change) | medium |
| Menu music | `Assets/Sound/sr2_cover_intro.mp3`, `MainMenuScreenView.xaml.cs:103` | compiled in | easy (make it the cue `music.intro`) |

Shape: a `theme/` folder per mod: `theme.json` (colour, font and size keys) merged into `Application.Resources` at
start-up, before the window loads (the style dictionaries resolve their colours as they load, so the mod's go in
first); `theme/fonts/*.ttf`; `theme/images/<name>.png` found by one resolver in mod order, falling back to the exe's.
Stray colours and fonts in views move onto keys first. We publish the key list, since mods will depend on it.

## 3. Locale: money, units, dates

| What | Where | Now | Effort |
|---|---|---|---|
| Currency | `Helpers/Money.cs:7`, about 56 `$"...${x}"` in C#, about 19 `Text="$"` in XAML | `$`, whole dollars | medium (sweep) |
| Units | mph (`CarHistory.cs:65`, timeslip), quarts (`GasStation.cs:78`), °C/litres/km/h (`CarCondition.cs`), kg and HP/ton (`MatchupCalculator.cs`), quarter mile and feet (timeslip, Lua) | mixed | medium |
| Culture, 12-hour clock | `en-US` in `App.xaml.cs:152`, `MainWindow.xaml.cs:35`, `GameTimeService.cs:17` | fixed | easy |

Shape: `locale` in the game file: currency symbol, position, decimals, rounding step; unit system; culture. One
money formatter and one unit formatter; every `$` goes through them. Amounts are in the game's own currency (a yen game
writes yen in its rules), there is no conversion.

## 4. Rules and numbers

Almost all are `const`s or inline numbers; moving each into a record whose defaults are today's values is easy work.
Where a system's rules sit in the UI layer (`MatchupCalculator` in `Screens/Diner`), they move to a service first.

| System | Where | Suggested file |
|---|---|---|
| Start: money (1,000,000 dev value), date (1970-06-01), day hours (in two places), starting cars and parts (none today) | `Racer.cs:37`, `GameState.cs:8-18`, `GameTimeService.cs:26-27`, `GameStateRepository.CreateNew` | `rules/start.json`, `rules/calendar.json` |
| Time each action takes | `Services/Time/GameAction.cs:71-95`, workbench `PartsWorkbenchViewModel.cs:20-24`, cruise tick | `rules/time.json` |
| Difficulty presets | `GameRules.cs:72-109` | `rules/difficulty.json` |
| Car worth, base prices (brand and year multipliers favour the 1960s), rarity | `CarValuation.cs`, `CarProfileService.cs:34-338` (cached in catalog.db: the cache must follow the file) | `rules/car_pricing.json` |
| Used-car market, selling, dealers opening and closing | `UsedCarMarketService.cs`, `CarSaleService.cs`, `DealerRules.cs` | `rules/market.json`, `rules/dealers.json` |
| Part shops, part prices, mail order, parts price scale (0.2, "1970 dollars", a user setting today) | `PartShopRules.cs`, `PartPricing.cs`, `NewspaperPartsService.cs`, `AppSettings.cs:118` | `rules/partshops.json`, `rules/economy.json` |
| Repairs, labour | `RepairShop.cs:49-63` | `rules/repairs.json` |
| Wagers, challenges, reputation | `MatchupCalculator.cs:15-32`, `OpponentChallengeService.cs`, `RacerStats.cs` | `rules/wagers.json`, `rules/reputation.json` |
| Wear, damage, fuel, oil, heat | `RaceResultProcessor.cs:46-61`, `CarCondition.cs`, `EngineCooling.cs` | `rules/condition.json` |
| Rivals: life, generation, money, off-screen races, grudges, street encounters, talk | `OpponentRules.cs`, `OpponentGenerationService.cs`, `OpponentInitializationService.cs`, `RaceSimulatorService.cs`, `StreetEncounters.cs`, `StaticTalkService.cs` | `rules/rivals.json`, `rules/street.json`, `rules/simulation.json` |
| Police: chances, fines, impound, cars per race, preferred car ("monaco"), chase tuning (about 40 Lua locals) | `PoliceRules.cs`, `PoliceCars.cs:113`, `mode.lua:131-192` | `rules/police.json` (the Lua reads its values from race.ini) |
| Event invitations, newspaper | `RaceEventService.cs:73-76`, `NewsWriter.cs` | `rules/events.json`, `rules/news.json` |
| AC session: damage/wear/fuel rates, AI level, weather (always `4_mid_clear`, 12 °C), latitude, time zone | `IniModificationService.cs` | `rules/race.json`, `rules/world.json` |
| Sun model ("the middle of the States"), northern seasons | `StreetScene.cs:23-25`, `RaceSimulatorService.cs:535` | `rules/world.json` |
| Brand countries for event filters | `OriginFilter.cs:23-90` | `rules/origins.json` |

Shape: `rules/*.json` per mod, merged key by key in mod order, **copied into the save when a career starts** (as
`GameRules` already is), so updating a mod cannot change a career under way.

Logic, not numbers: **the King** is a special case in code (`IsKing`, path `"King"`, fixed stats and money); a game
wants a list of bosses. **Closed enums saved by name** (milestone triggers, victory step kinds, event schedules, race
types) need string ids before mods can add kinds.

## 5. Cars, makes, parts, engines

| What | Where | Now | Effort |
|---|---|---|---|
| Make families (gm/ford/mopar...; no Japanese makes). Drive the stock engine guess, swaps, tuning, sounds | `Parts/Cars/MakeFamilies.cs:13-23` | C# keywords | easy, big payoff |
| Stock engine, layout and price of an AC car | `StockEngineMatcher.cs`, catalog.db (editor only) | guessed by name; never from a mod | easy: `cars.json` |
| Which AC cars are in the game | `CarImportService.cs`, `UsedCarMarketService.cs` | every installed car, no era filter | medium: allow-list or filter (years, countries, ids) |
| Part groups, bolt-on groups, tuner priorities, performance groups | `PartKinds.cs:17-37`, `EngineFactory.cs:32-35`, `EngineTuner.cs:66-86`, `PartShopRules.cs:62-66` | C# English strings | easy: fields on work-order kinds |
| Stand-in script classes | `ScriptClassLoader.cs:26-32` | C# table | easy |
| **Engine figures** | `PartScriptRuntime.cs`, `EngineEvaluator.cs` | only from SLRR bytecode; plain-data parts work for running gear only | hard: a `"figures"` object on a part that fills `DynoInputs` directly |
| Turbo lag, single turbo | `AcEngineData.cs:148-161` | fixed 0.985/0.992 | easy |
| Gears | `EngineEvaluator.cs:65-66` | SLRR array, at most 6 | medium |
| Engine drawing | `EngineLayout.cs` | always longitudinal (transverse FWD shows wrong) | medium |
| Nitrous | `PartKinds.cs:32` | a shelf, no effect in AC | hard |
| Rotary engines | `EngineDyno.cs` | 4-stroke piston model only | hard (fake piston figures meanwhile) |

Already fine: turbos and superchargers, EFI parts, AWD (the AC car keeps its own traction), free-form fittings, the
work order, cooling roles, sound pins, `overrides` on copied scripts.

## 6. Flow and screens

| What | Where | Now | Effort |
|---|---|---|---|
| Screen list | `Navigation/NavigationService.cs:290-515`, `App.xaml:64-175` | 18 typed `NavigateToX(...)` methods, compiled DataTemplates, no ids | hard |
| Hub tiles | `GarageScreenView.xaml:612-705`, `GarageScreenViewModel.cs:340-346` | fixed | medium: `tiles[]` with label, icon, target, `requires` |
| Garage as home, diner as race lobby | about 20 call sites, `RaceLoadingScreenViewModel.cs:234-268` | assumed | medium |
| Exits of each screen | each view model | C# calls | medium |
| Main menu cards | `MainMenuScreenViewModel.cs` | enum | easy |
| End Day only in the garage; nightly tasks in a fixed order | `GarageScreenViewModel.cs:819-875`, `App.xaml.cs:231-269` | code | easy / medium |
| Shows (drive-off, drive-in, station, street rival) | `Controls/CarPull.cs`, `DriveOff.cs`, `PathDrive.cs`, `RivalDrive.cs` | C# choreography | see step E7 |

A truly new kind of screen will always need C#. Data can rename, rewire, hide and reuse the existing screen kinds.

## 7. Platform

| What | Where | Now | Effort |
|---|---|---|---|
| Player settings | `%AppData%\StreetCorsa\settings.json` (track, AC path, audio) | JSON, no game-wide defaults | easy |
| AC folder default | `AppSettings.cs:37` `C:\GAMES\Street Rod AC` | the developer's own install | easy: detect Steam |
| "Street Corsa" folders (AppData, Documents radio, logs, saves, AC out inbox) | about 10 places | literals | medium: one name, a folder per game |
| mod.json | `Mods/ModSet.cs:36-67` | id, name, version, author, description, order | easy: `requires`, `gameVersion`, `type` |
| The base game | `ModSet.cs:153,237`, `ModIds.cs:17` | `streetcorsa` always loaded first, can't be switched off; mods only `replaces`, never remove | hard |
| Turning mods on and off | folder rename (`_` prefix) | no in-game switch | easy |
| Mod problems | log only | player sees nothing | easy: a dialog |
| Race formats | `RaceContext.cs:115` (Drag/Circuit/Sprint), `RaceResultJson.cs:144`, about 17-35 switch sites; `mode.lua` (2478 lines) | closed on both sides | hard |
| race.ini | `IniModificationService.cs` | built in code (one lap, `DRIFT_MODE=0`, fixed tail) | medium: session templates |
| Race mode | `apps/new-modes/sc_race`, copied from next to the exe | one mode, not from Content | hard: mods ship their own `acmodes/<id>/` |
| Results | schema 1.7, win/lose/crash/busted | no score | medium: `score`, `format` |
| Logging | `AppLoggerFactory.cs` | fixed level | easy |

## The plan: steps of the epic

Each step is one PR you can play; nothing waits for the whole epic. Every step keeps the base game looking and
playing exactly as it does now: today's values become the base game's data.

- **E1. Foundations.** `game.json` per mod (title, locale, the game-wide settings; later mods win key by key) and the
  player's `settings.json` on top for player things; the `rules/` loader, copied into the save at career start;
  mod.json `type` (`addon`/`game`), `requires`, `gameVersion`; `"remove"` beside `"replaces"`; a mod-problems dialog;
  AC folder detection. Small, but everything after stands on it.
- **E2. Rules as data.** Everything in section 4 into `rules/*.json` with the start conditions (money, date, starting
  cars). Mostly mechanical, low risk, and the most modding value for the effort.
- **E3. Words.** The markup extension, `text/ui.json`, the sweep of XAML and C# strings, the race mode's lines.
- **E4. Locale.** Currency, units, culture; the `$` and mph sweep.
- **E5. Look.** Theme keys, fonts, the image resolver, icons, backgrounds, window icon and title, intro, menu music.
- **E6. Cars and makes.** `makes.json`, `cars.json` (engine, layout, price per AC car), the car allow-list, pricing
  curve, part groups and priorities onto work-order kinds.
- **E7. Car scripting.** JSON scripts for every car move, as planned: one driver for the lot, garage, station and
  street; commands; named marks in every scene; triggers per screen (mods can add their own); the existing shows
  ported onto base-game scripts. The same trigger and condition format serves later steps.
- **E8. Flow.** A screen registry with string ids and kinds, hub tiles and menu cards as data, exits and `returnTo`,
  the home screen, where the day can end; screens and systems a game switches off (police, gas station, paper).
- **E9. Games.** A `"type": "game"` mod stands in for `streetcorsa` as the base; a game picker; saves stamped with
  their game and mod set; a data folder per game; bosses instead of the King; string ids for the saved enums.
- **E10. Race formats.** Format ids in data, race.ini session templates, mods shipping their own CSP mode, a result
  contract with a score. Drift, time attack and touge become possible.
- **E11. Data engines.** The `"figures"` path that skips the SLRR VM, turbo lag and twin turbos per part, more gears,
  transverse engines.
- **E12. The proof.** A small, deliberately different game mod (a few modern Japanese cars, yen and km/h, its own
  look, a changed flow) that shows what still breaks and becomes the example modders copy.

Later, not in the epic: a weather system (there is none today), nitrous in AC.

## As built

### E1 and E2 (2026-09-30, one PR, branch `feature/total-conversion`)

The owner's decisions: E1 and E2 together; JSON for the general settings (no INI): `game.json` in the mods plus the
player's `settings.json`; rules copied into the save when a career starts.

- **mod.json**: `type` (`addon`/`game`, recorded only), `requires` (id to lowest version; missing or older is left out,
  in a chain; a mod loads after what it requires), `gameVersion` (held against the program's `<Version>`, now 0.1.0 in
  the csproj), `removes` (by kind: dealers, partshops, racers, events, milestones, victories, streets, stations,
  warehouses, text pools, sfx cues; not parts). `Mods/ModVersion.cs`, linked into EngineBench.
- **Mod problems**: `Mods/ModProblems` collects every loader's problems (once each); start-up reads all the content on
  a worker after the parts warm up and shows them in one dialog (`App.ShowModProblemsAsync`).
- **game.json** (`Rules/GameFile`): `title` (the window), `garage` (was `AppSettings.GarageShowroomId`'s literal).
- **AC folder**: settings without one and no AC at the developer's default get Steam's (`AcInstallFinder`: the uninstall
  entry of app 244210, then every library in libraryfolders.vdf), written into settings.json.
- **Rules** (`Rules/RuleBook` + one section class per part): `ModMerge` merges `rules/*.json` key by key; each part is
  read on its own over the code's figures (`JsonObjectCreationHandling.Populate`), so a bad part stays the base game's;
  unknown names are told. `GameState.RuleBook` holds the whole book as JSON from the career's start;
  `RuleBook.PlayBy(game)` (load screen, `GameStateRepository.CreateNew`, `App.CurrentGameState`) makes it `Current`,
  merged over the mods' merged files so figures added later come from the mods. Old saves get the mods' book.
- Constants became properties reading `RuleBook.Current.<part>.<figure>`, keeping their names, so callers did not
  change. The parts code the tools link (`PartPricing`) asks `PartPricing.Source`, which the parts service points at the
  rules (pushing values in made tests depend on each other).
- The base files `Content/streetcorsa/rules/*.json` are written from the code by a test
  (`SC_WRITE_BASE_RULES=1`, `RuleBookTests`), which otherwise checks they still hold exactly the code's figures.
- Moved: start (money, date, starting cars), day hours, time costs, difficulty presets, police (and preferred police
  cars), rivals (life, money by age band, the King's money and record), dealers, part shops (with per-kind tables),
  economy (part price scale, used/trade-in shares, mail order, part ads, prize cash), market (lots, selling,
  valuation), repairs, condition (wear, fuel, oil, race wear), street (encounters, grudges, talk), wagers (limits,
  night stakes), reputation (the whole formula), events, news, simulation (crashes, pink slips, seasons), race (AC
  damage/wear/fuel rates, police AI), world (sunset model, seasons, AC weather, temperature, wind, place).

Left for later steps, on purpose:
- the rivals' challenge acceptance odds (`OpponentChallengeService`, about 30 inline figures), their traits by age
  (`OpponentGenerationService`) and their cars' shape: personality, not setting;
- the police chase's tuning in `mode.lua` (about 40 locals): with the race formats (E10), which change the Lua anyway;
- bracket racing (quarter mile, the Hale factor): race formats (E10);
- car prices, rarity, brand countries (`CarProfileService`, `OriginFilter`) and the cooling figures and brand words
  (`EngineCooling`, linked into EngineBench): cars and makes (E6);
- the reputation tier words, difficulty summaries and other English: words (E3).
