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
| Menu music | `Assets/Sound/sr2_cover_intro.mp3` (untracked; nothing played it) | not in the game | easy (make it the cue `music.intro`) |

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
- **E7. Car scripting.** JSON scripts for every car move (the owner chose JSON over Lua, 2026-09-30). The plan agreed:
  - One car driver replacing the three movers today (`CarPull` for the lot drive-off and garage drive-in, `PathDrive`
    for the station, `RivalDrive` for the street): every drive is a path and a speed profile worked out up front (the
    lot's steer-for-the-exit simulated once with the same bicycle model), reverse added, several cars per viewport.
  - Commands, free of any era: lights and brake lights, engine start/stop/rev, wait (a time, or until the body has
    settled), drive to a point / along a path / a distance / in reverse, stop at a mark, top speed, the drive's feel
    (acceleration, clutch, steer, lean: the constants in `CarPull` today), fade with the engine's volume, camera
    hold/follow, branches (the engine runs, towed, the hour).
  - Named marks and paths in every scene's JSON (`exit`, `pump`...); the garage needs a scene JSON of its own (door,
    the car's spot; its showroom walls were never checked against the drive-in).
  - Triggers named per screen (`lot.take_out`, `garage.arrive`, `garage.leave`, `station.arrive`, `station.at_pump`,
    `station.leave`, `street.rival_arrive`, `street.rival_leave`; `diner.*` once it has a scene); mods may name their
    own. The base game's scripts in `Content/streetcorsa/scripts`, a mod's replace them (`"replaces"`).
  - The runner: the game's logic never waits on the show; a script that fails, or no 3D, leaves the car where it ends;
    a script stops when its screen goes, a click skips it, a time limit; problems to `ModProblems`.
  - The shows of today ported onto base-game scripts, looking the same, then the old movers deleted.
  - The diner has no 3D scene: a car moving there is its own step, after this one.
  - The same trigger and condition format serves later steps (flow, events).
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

### E5: look (2026-10-01, branch `feature/look`)

The owner's decisions: a theme sets **keys and pictures only** (no XAML from mods: it can run code); the **menu music**
plays only as the game starts (intro and main screen, until a game is on or the radio comes on; back at the main screen
from a game the radio wins, off stays off); the music file (a cover of SR2's intro) is **git-ignored** until its
licence is granted.

- `Styles/Theme.cs` builds the application's resources in code at start-up (App's constructor, after
  `InitializeComponent`): Colors.xaml, Typography.xaml and the new Fonts.xaml with every mod's `theme/*.json` over
  them (`ThemeFile`, load order, keys any case), then Brushes, Icons, Buttons, Inputs, Radio, each loaded once the ones
  before are in (they take their colours as they load; Brushes.xaml no longer merges Colors.xaml). App.xaml keeps only
  the converters and templates.
- Every colour key lives in Colors.xaml, every typeface in Fonts.xaml (`BodyFont`, `DisplayFont`, `DialFont`,
  `NumbersFont`, `TypewriterFont`), every font size in Typography.xaml. The strays moved onto keys: the radio's colours
  (out of Radio.xaml and CarRadio), the intro's chrome title, the main screen's band, the shadows (`Shadow`), the black
  behind the 3D scenes and the fades (`Stage`), the licence's paper, the tach's redline, the pointer label in the 3D
  views; the fonts typed in 15 places. `ThemeTests` keeps new ones out.
- The base game's `theme/theme.json` lists every key at its value, written from the dictionaries by a test
  (`SC_WRITE_BASE_THEME=1`): the published key list, not read at run time (the dictionaries already are what it says).
  A mod naming the game's own face is not told of when the PC lacks it; a mod's font file of that name is still used.
- Pictures by name through one resolver (`ThemeImages`, `{theme:Image backgrounds/main}`): a mod's
  `theme/images/<name>.png|jpg|jpeg|bmp|ico`, last mod first, every mod picture's header checked at start-up (one that
  does not read is told with the other mod problems and passed over; one that fails to decode later gives way to the
  one under it), else `Assets/Images/<name>.png` (the icons moved there from `Assets/Icons`). The resource dictionaries' pictures
  stay lazily decoded. New: `intro/logo` (a game's logo over the lettered title) and `window/icon`.
- `music.intro` (`sfx/music.json`, radio bus, `"optional": true`: a cue whose files may be missing without a problem):
  `Audio/StartMusic` starts it with the radio, ends it when a game is loaded, the radio comes on or the sound library
  opens. `AudioHub`'s place loop became a `LoopSlot`, used twice (the place's air, the music).
- Checked by an off-screen harness that renders 24 views with no data, on dev and on the branch: pixel-identical; and
  with a test theme (fonts, colours, background, logo) that the theme shows.

Left for later, on purpose:
- the control templates (button and panel shapes, corner radii, thicknesses, margins) are the game's; only their colours
  and sizes are keys;
- the 3D scenes' lights and the street's colours are their scenes' data, not the theme;
- `DropShadowEffect` depth, blur and opacity, and animation timings, are not keys;
- the menu music in game: not heard yet.

### E4: locale (2026-10-01, branch `feature/locale`)

The owner's decisions: the base game shows **all US 1960s units** (miles, mph, gallons, quarts, °F, lb; before it mixed
km mileage, litres and °C with mph and quarts), and **the game picks the units, the player may pick the others** in
Settings (taken at once). Money is always the game's.

- `game.json` `locale`: `culture`, `money` (`symbol`, `after`, `space`, `decimals`), `units`; checked at start-up, what
  cannot be used told (`GameLocale.Checked`). `Helpers.Locale` holds what is in play (the culture, the money style, the
  units); the player's pick is settings.json `Units`.
- The program keeps `en-US` as its thread culture, so the files it writes stay one way; what the player reads goes
  through `Locale.Culture`: `GameText.Fill`, the screens' bindings (`FrameworkElement.Language`, set after the log
  opens and before the first element), `Money`, `Measure`, the date patterns.
- `Helpers.Money` (a value that writes itself, `Money.Text`), `Helpers.Measure` (a figure kept metric, written in the
  player's units through the text pools `unit.<id>`), `MoneyConverter` for XAML. About 45 `$"${x:N0}"` and every unit
  in C# went through them; a test keeps new ones out.
- The text names a number's kind: `{price:money}`, `{peak:temp}`, `{left:fuel.number:0.0}` (`GameText.Kinds`); the
  ~30 `${x:N0}` in the pools became `{x:money}`. Date patterns in code are text pools `format.*` (`GameText.Date`).
- The oil the attendant reads is in quarts or litres (`station.oil_short.<unit>.*`); the timeslip's marks are text and
  its trap speeds in the player's units; the race mode gets `SPEED_PER_MPH` and the unit's words through race.ini, and
  AC's own `USE_MPH` follows the units. The radio dial is text (`radio.dial`).

Left for later, on purpose:
- the strip is the quarter mile wherever it is (60', 330', 1/8, 1/4 are its marks): race formats (E10);
- power stays hp in both systems (a mod can call it PS in `unit.hp`, not converted); the workbench's displacement shows
  litres and cubic inches both;
- figures written in content as words (a milestone's "Earn $10,000 from races", an event's description): content, a
  game writes its own;
- the price rounding steps (a yen game's prices round to 10s of dollars' worth): rules (E2's `rules/*.json`);
- `$"{x:0.0}"` numbers built in C# for a screen without a unit (percentages, ratios, the rpm) are still written in US
  English; logs stay US English;
- in-game look at metric and at another culture: not played yet.

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

### E3: words (2026-09-30, branch `feature/words`)

The owner's decisions: a language picker as well as mods rewriting the words; the whole sweep in one PR; the
developer's screens (car catalog editor, sound library) stay in English.

- **Lookup**: `GameText.Say(key, ("name", value)...)` in C#, `{t:Text key}` in XAML (`Services/Text/TextExtension`,
  `xmlns:t="clr-namespace:StreetCorsa.Services.Text"`): a pool's first line, filled in; a key no mod has says itself.
  App.xaml takes no `{t:Text}`: it loads before the language is set.
- **Files**: `text/ui.json` (shared, `common.*`, the language's name), `ui_garage`, `ui_race`, `ui_market`, `ui_shell`,
  `ui_parts`, and `race.json` for the race mode; about 1,300 keys. Choices made in code (plurals, one of two sentences)
  are `.one`/`.other` or separate keys; the choice stays in code.
- **Languages**: `game.json` `language` (`"en"`) is the language of the files directly in `text`; a translation is
  `text/<language>/` in any mod, over the game's pools, the game's words where it has none; `language.name` is its name
  in the Settings picker (shown when a mod has a language), saved in settings.json `Language`, spoken from the next
  start (strings are built once in many places: a live switch would leave old words on screen).
- **Race mode**: `RaceText` writes every `race.*` pool into race.ini `[STREET_CORSA_TEXT]` (`race.result.win` is
  `RESULT_WIN`), percent-encoded UTF-8 so commas, `;`, `$`, quotes, line breaks and accents survive CSP's INI reader;
  `mode.lua`'s `say(key, english, values)` reads `INIConfig.sections`, decodes, fills `{name}` (letters and digits
  only), and says its own English when the key is not there. C# keys must not start with `race.`. The MSG locals
  became one `MSG` table: the new ones took the main chunk past Lua's 200 locals.
- **Linked code**: the parts code is compiled into the tools, which read no text: it says its words through
  `Parts.Cars.PartWords` (the damage report, why a car can't race, the repair shop's and the gas station's jobs, the
  work order's refusals, a part's few words), pointed at `GameText.Say` by a module initializer in the game; the tools
  say the keys. A repair job has an `Id` (the gas station switches on it) beside its name.
- **Tests** (`WordsTests`): every key in `{t:Text}`, `GameText.Say`, `PartWords.Say` and the mode's `say('KEY'` is in
  the base text; no XAML outside the developer's screens writes words; translations and the INI encoding.

Left for later, on purpose:
- currency, units, number and date formats (`$`, mph, km/h, kg, HP, litres, °C, quarts as figures, the timeslip's
  distances, the radio dial's KC): locale (E4). Money is formatted in code and handed to the text as a value;
- part group names ("Engine blocks", "Pistons"): they are ids too, matched in the code and the data: cars and parts
  (E6, onto work-order kinds). The work-order kinds' names come from the packs' data;
- English grammar made in code: possessives (`'s`, his/her/their), "the" before a part, capitals at a sentence's start;
- messages before the mods are read and on the fatal path (already running, could not start, the crash dialogs), the
  mods' problem lines (for modders, like the log), the FMOD credit, the developer's F5 placement read-out;
- saved text stays in the language it was made in (a used car's engine summary, the news, street talk).
