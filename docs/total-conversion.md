# Total conversion: making the whole game moddable

The goal (the owner, 2026-09-30): the entire game moddable, so the community can add new game modes and whole new
games from `Content\<mod>\` without touching C#. The 1960s American street-racing setting is the base game's taste,
not the program's: someone should be able to make a Tokyo Drift game (modern JDM cars, yen, km/h, turbos, drift and
touge races, a different look) on the same program.

**Changed 2026-10-03:** the owner decided Street Corsa stays one game. It will be open source, so a whole new game is
a fork, not a mod: no game picker, no `"type": "game"` base swap, no data folder per game. The epic's aim is now
everything a Street Corsa **add-on** mod could want to change, and E1-E8 make a fork easy anyway. E9-E12 below were
cut down to match.

**Done 2026-10-03:** E1 to E12 are built, the last with the example mod (`examples/mods/small_imports`). What each step
left is under its "Left for later" in As built.

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
wants a list of bosses (**done, E9**: bosses). **Closed enums saved by name** (milestone triggers, victory step kinds,
event schedules, race types) need string ids before mods can add kinds (E9 looked: victory steps are strings already,
the schedule is not saved, a milestone trigger needs code to count it, race types go with E10).

## 5. Cars, makes, parts, engines

| What | Where | Now | Effort |
|---|---|---|---|
| Make families (gm/ford/mopar...; no Japanese makes). Drive the stock engine guess, swaps, tuning, sounds | `cars/makes.json` | **done (E6)** | |
| Stock engine, layout and price of an AC car | `cars/cars.json` | **done (E6)** | |
| Which AC cars are in the game | game.json `cars` | **done (E6)** | |
| Part groups, bolt-on groups, tuner priorities, performance groups | `parts/part_groups.json` | **done (E6)** | |
| Part classes, fuels | `parts/part_classes.json`, `parts/fuels.json` | **done (own part logic, 2026-10-18)** | |
| Engine figures | a part's `"figures"` (`Parts/Logic/PartFigures.cs`) | **done (E11)** | |
| Turbo lag, twin turbos | a turbo's `lag_up`/`lag_down`; one AC turbo per turbo | **done (E11)** | |
| Gears | `"gears"` in figures or a gearbox's specs, up to 10 | **done (E11)** | |
| Engine drawing | `front_transverse`, `mid_transverse` layouts (`EngineLayout.cs`, `CarPartsLayout.cs`) | **done (E11)** | |
| Nitrous | `PartKinds.cs:32` | a shelf, no effect in AC | hard |
| Rotary engines | `EngineDyno.cs` | 4-stroke piston model only | hard (fake piston figures meanwhile) |

Already fine: turbos and superchargers, EFI parts, AWD (the AC car keeps its own traction), free-form fittings, the
work order, cooling roles, sound pins, `overrides` on copied scripts.

## 6. Flow and screens

| What | Where | Now | Effort |
|---|---|---|---|
| Screen list | `flow/flow.json` `screens`, `Navigation/FlowBook.cs` | **done (E8)** | |
| Hub tiles | `flow/flow.json` `tiles` | **done (E8)** | |
| Garage as home, diner as race lobby | `home`; a race returns where it was set up (`after_race`) | **done (E8)** | |
| Exits of each screen | `screens.<id>.exits` | **done (E8)** | |
| Main menu cards | `flow/flow.json` `cards` | **done (E8)** | |
| End Day only in the garage; nightly tasks in a fixed order | `screens.<id>.endDay`; tasks skip a system that is off | **done (E8)**; the order stays the code's | |
| Screens and systems a game switches off | `rules/systems.json` | **done (E8)** | |
| Shows (drive-off, drive-in, station, street rival) | `scripts/scripts.json`, `scripts/feels.json` | **done (E7)** | |

A truly new kind of screen will always need C#. Data can rename, rewire, hide and reuse the existing screen kinds.

## 7. Platform

| What | Where | Now | Effort |
|---|---|---|---|
| Player settings | `%AppData%\StreetCorsa\settings.json` (track, AC path, audio) | JSON, no game-wide defaults | easy |
| AC folder default | `AppSettings.cs:37` `C:\GAMES\Street Rod AC` | the developer's own install | easy: detect Steam |
| "Street Corsa" folders (AppData, Documents radio, logs, saves, AC out inbox) | about 10 places | literals | kept: one game (2026-10-03); a fork renames them |
| mod.json | `Mods/ModSet.cs:36-67` | id, name, version, author, description, order | easy: `requires`, `gameVersion`, `type` |
| The base game | `ModSet.cs:153,237`, `ModIds.cs:17` | `streetcorsa` always loaded first, can't be switched off; mods `replaces` and `remove` (E1) | kept: one game (2026-10-03) |
| Turning mods on and off | folder rename (`_` prefix) | no in-game switch | easy |
| Mod problems | log only | player sees nothing | easy: a dialog |
| Race formats | `formats/formats.json`, `Services/Race/RaceFormats.cs`; the mode's `format_*.lua` modules | **done (E10)** | |
| race.ini | `IniModificationService.cs`: the format's module, laps, session name, police, mode | **done (E10)**; `DRIFT_MODE=0` and the tail stay | |
| Race mode | sc_race's core plus format modules; a mod ships `formats/<module>.lua` or a whole `formats/<mode>/` | **done (E10)** | |
| Results | schema 1.8: `session.format`, `participants[].score` | **done (E10)** | |
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
- **E9. Saves and mods** (cut down 2026-10-03 from "Games": no game picker, no game-type base, no data folder per
  game). What add-on mods need from the save:
  - saves stamped with their mod set (id and version), and a warning on load when a mod the career used is gone or
    older, instead of a broken career;
  - string ids for the closed enums saved by name (milestone triggers, victory step kinds, event schedules...), so a
    mod can add its own values;
  - the King as data: bosses in the racers' data, so a mod can change the final rival or add more.
- **E10. Race formats.** Format ids in data, race.ini session templates, mods shipping their own CSP mode, a result
  contract with a score. Drift, time attack and touge become possible: worth it for Street Corsa itself.
- **E11. Data engines** (done 2026-10-03). The `"figures"` path (an engine of figures alone), turbo lag and twin turbos
  per part, more gears, transverse engines. Since 2026-10-18 every part is data: the engine is worked out from the parts'
  specs in C# (docs/systems/parts-system.md), the figures go over it.
- **E12. The example mod** (done 2026-10-03; was "the proof", changed 2026-10-03). An example add-on, not a separate game: a few cars,
  a parts pack, an event and a race of its own, made only from data, that shows what still breaks and becomes the mod
  modders copy.

Later, not in the epic: a weather system (there is none today), nitrous in AC.

## As built

### E12: the example mod (2026-10-03, branch `feature/example-mod`)

The owner's decisions: small imports (cars with engines of figures, twin carbs, a turbo kit, a five-speed, a transverse
engine), in the repo's `examples\mods\small_imports` (copied into `Content` to try, never loaded by default), with
makes and a speed shop, an event and a format of its own, a rival and a dealer. The cars are Kunos cars every AC has:
the Abarth 595 SS (rear), the Alfa Romeo GTA, the Ford Escort RS1600 (front) and the Abarth 500 esseesse
(`front_transverse`, turbo).

- **The mod**: `mod.json`; `cars/cars.json` (makes `abarth` in a `fiat` family, `fiat` and `alfa_romeo` given families,
  `ford_uk` apart from the American Ford; the four cars' engines, layouts, prices, makes); four packs of figures
  (`fiat_twin`, `alfa_twincam`, `ford_bda`, `fiat_tjet`: 66 parts with simple class chains, `required_slots`, fittings
  per engine, cooling parts by name, an IHI and a Garrett turbo), eight builds (four factory, rated 32/115/120/160 hp,
  the model within 7%; four aftermarket kits, 39 to 202 hp); `sounds/sounds.json` pinning each block to its car's own
  bank (`car:<id>`); a specialist shop, an import lot, a rival, the `autocross` format (time attack over two laps),
  the weekly Import Night event (a Cosworth cam the prize), the words; a README.
- **What it found, now fixed**: a lot could not deal in one kind of car (`DealerDefinition.Cars`, a car filter; the
  price band is of that kind: `UsedCarMarketService.FillDealer`); a racer could not drive one kind (`Opponent.Drives`,
  read from the racers' `"cars"`, put on him from his definition every review like his portrait, used when he gets
  his first car and when he buys or bargains for one to drive); only bosses had lines of their own (`RacerWords.Key`
  for every racer's `talk.pulled_up*`, `replies.*` and `street.*`). The events' car filter reader moved out of the career
  content into `CarFilterJsonConverter` for all three.
- **For tools and tests**: `ModSet.Load(root, alsoFrom: [folder])` reads a mod from outside the Content folder.
  `ExampleModTests` loads the mod over the base game through every loader (parts, cars, sounds, formats, events, racers,
  dealers, shops, text) and runs its engines.

Review fixes (E11 and E12): `RacerWords.OwnSuffixes` (moved from `Bosses`, `Bosses.Key` gone) guards a racer with no
id (an old save); the grudge, bought-your-car and rivals' race lines are a racer's own too (the loser's for the
rivals'). Figures go on level by level out from the block (`PartFigures.Of`), so the part furthest out wins as the
guide says; a block with no script is an engine of figures whatever gives them; an engine the dyno will not run says
why (`EngineDyno.Unrunnable`, which with `CylinderVolume`, `Compression`, `Span`, `MaxRpm` and `MinCompression` the
figures now share); `rev_limit`/`max_rpm` of 0 or past 30000 are told; `EngineReport.DiffLock` is null when a gearbox
of figures says nothing, and the car keeps its own differential. `EngineLayouts.Parse` matches names only ("mid, rear"
is none). Car filters have `MatchesOffer` (price and power of the car on offer; composites pass on every overload):
lots, racers and trade-ins (`TradeInLocation(dealers, car)`, only to a lot of the car's kind) use it, and a racer with
a car of his kind that cannot race buys one that runs. A dealer's `"cars"` is read by the catalog (`CarsJson`), so one
that does not read (a word, an unknown type) is told and the lot kept, dealing in any car. The rocking body takes the
running engine's layout in every viewport (`EngineSpec.Layout`).

Left for later: unknown keys in a dealer's or a racer's entry are passed over without a word (System.Text.Json ignores
them), part models (the mod's parts are drawn as the placeholder boxes), a portrait for Gino, the cars' own sound banks
pinned only when the car is installed. Not seen in game.

### E11: data engines (2026-10-03, branch `feature/data-engines`)

(As built then, when the engines still ran the original packs' compiled scripts; since 2026-10-18 they are worked out
from the parts' specs, and "scripts" below is what the specs are now.)

The owner's decisions: a part's figures go onto the dyno's inputs (the game's dyno still makes the curve, so tuning
works and a data part goes on an SLRR engine too), in plain metric units, transverse engines as a layout the garage
draws, and tests only (the playable data engine ships with E12's example mod).

- **Figures** (`Parts/Logic/PartFigures.cs`, `PartFigureSet.cs`; pack.json `"figures"`, read as the catalog loads into
  `PartDefinition.ReadFigures`, what does not read told): a number sets, `{ "add" }` adds, `{ "times" }` multiplies;
  parts' own numbers first (the part furthest out from the block, siblings by slot), then adds, then multiplies. Dyno
  figures (`cylinders`, `bore`, `stroke`, `compression` or `chamber`, breathing, cam durations, spark, burn, mixture,
  `fuel_flow`/`air_flow` in g/s, `rev_limit`, `max_rpm`) and engine figures (`idle_rpm`, `inertia` kg m2, `friction`,
  `clutch_torque` Nm, `gears`, `reverse`, `final_drive`, `diff_lock`, an SLRR turbo's `lag_up`/`lag_down`).
  `PartScriptRuntime` puts them over `DynoData` at every `calcDyno`; a block with no script is an engine of figures
  (`EngineReport.FromFigures`) on `PartFigures.DataEngineBase` (a small-block's breathing, cam, spark and mixture), whose
  problem is what it lacks (`PartFigures.MissingOf`). The cam's durations are all the dyno reads of the valve events.
- **Chargers**: `turbo`/`blower` (one or a list) become `DynoInputs.Chargers` (`DynoCharger`: boost in bar, building
  from `spool_rpm` to `full_boost_rpm` by a smoothstep, then held); their boost adds to the scripts' charger's.
  `AcEngineData` fits one `[TURBO_n]` per turbo (the SLRR one first) with its own lag: the dyno's gain over the engine
  without its turbos (blowers kept) is shared at each speed by the boost each turbo makes there. One SLRR turbo comes
  out exactly as before.
- **Gears**: a gearbox of figures has up to 10 (AC's limit); an SLRR one stays at 6.
- **Simple classes**: a part of figures may name its classes simply (`"class_chain": ["Camshaft"]`): `PartKinds.Is`
  and `FrameworkClass` take them; `IsScripted` counts a part with figures. Only a turbo with a script is the scripts'
  charger (`Turbocharged`).
- **Transverse**: `EngineLayout.FrontTransverse` and `MidTransverse` (`front_transverse`, `mid_transverse` in cars.json,
  the editor's "Front, across"), guessed for a front-wheel-drive car (none of the installed cars is one). The garage
  turns the block across the car, its crank 8 cm ahead of the front axle (25 cm ahead of the rear one behind the
  driver), its middle 15 cm off the centre line; a running engine across pitches the body where one along it rolls it.
  The stand and the part moves are the engine's own directions, unchanged.
- **Tests**: `PartFiguresTests` (a four of figures alone with a five-speed, a header, twin turbos to two AC turbos, a
  blower in the curve, bad figures, figures on the Chrysler 340), `EngineLayoutTests`. EngineBench takes a pack of
  figures as it is (a 2.0 l four: 135 hp, 256 hp with the two turbos).

Left for later: wear on a part of figures (its figures stay as new), the player's tuning of a part of figures (it has
no script fields to tune), a boost controller (a `times` on every charger's boost), which side the gearbox of an engine
across the car is on (not seen against a front-wheel-drive car in the garage), rotary engines, nitrous. Not seen in game.

### E10: race formats (2026-10-03, branch `feature/race-formats`)

The owner's decisions: the plumbing (today's races as format data, the score in the result, mods' modules and modes)
plus time attack as the one new base format (drift and touge later); sc_race keeps a shared core and each format is a
Lua module it loads; laps per format and per event; each format names a family, `drag` or `road`, which is all the
career counts (the record, wagers, the street's share and milestones stay drag/road, saves untouched).

- **Formats as data** (`Models/Race/RaceFormat.cs`, `Services/Race/RaceFormats.cs`, content type `formats`): `family`,
  `module` or `mode`, `laps`, `sessionLaps`, `session`, `police`, `bracket`, `win` (`position`/`score`), `better`,
  `scoreShows`, `name`, `diner`, `street`, `aliases`. The base game's `drag`, `circuit`, `sprint` (aliases the old
  `DragRace`, `Circuit`, `Sprint`) and `time_attack`. `RaceType` the enum is gone: a race, an event, a street offer
  and a launch carry a format id (`RaceContext.Format`, kept in the save under `RaceType`, whose old values are the
  aliases). An id no mod has is run and counted as a road race.
- **The code goes by family or format**: police (`format.Police`), bracket (`format.Bracket`), wagers, the street's
  pick (`StreetEncounters.PickFormat`: the family's first `street` format), tracks (`PickTrack`), wins counters, the
  newspaper, the AI's facts. The diner offers its formats per track family (`FormatChoices`, a drop-down when there is
  more than one; `diner.format.about.<id>`). Events name `format` (or `raceType` as before) and may set `laps`; the
  paper has `against_the_clock`, a weekly time attack.
- **race.ini**: `[STREET_CORSA] FORMAT`, `MODULE`, `LAPS`, `SCORE`; `RACE_LAPS`/`LAPS` from the format (`sessionLaps`
  for AC), the session's name, `__CM_CUSTOM_MODE` the format's mode.
- **The mode**: `mode.lua` (4.0.0) is the core; `format_drag.lua` (the strip, bracket races, test-and-tune: moved out
  whole), `format_road.lua` (the finish at the line; its header lists every hook and the core `SC`: the template),
  `format_time_attack.lua` (ghosts via `physics.disableCarCollisions`, a warm-up lap then `LAPS` timed laps, the best
  lap the score, a wait of 90 s for the rival, a stopwatch on screen). A module that does not load runs a road race.
  `ScRaceMode.Install` mirrors the mods' modules in as `format_<mod>_<module>.lua` and a format's own mode as
  `new-modes/<mod>_<mode>` (with the game's marker file; a folder of that name already there with files and no
  marker is somebody else's mode and is left alone, and the race is refused with `launch.mode_folder_taken`); the
  validator takes a result from any format's mode.
- **The result**: schema 1.8, `session.format`, `participants[].score` and `lap_times_ms`. `DetermineOutcome` decides a
  `score` format on the scores (`WinCondition.ScoreFinish`) after crashes and breakdowns; a score card comes first.
- **Tests**: `RaceFormatTests`, the contract fixture `sc_race_time_attack_result.json`, `tools/sc_race_harness/test_time_attack.py`
  (in `check.ps1`); the harness loads the modules with `require` and fails a run whose module does not load.

Left for later: drift and touge, `DRIFT_MODE`, a format's own track filter (a family's tracks only), the street racing
a format other than each family's first, a rival's pace in a time attack beyond AC's AI. Not seen in game.

### E9: saves and mods (2026-10-03, branch `feature/bosses`)

The owner's decisions: several bosses (any racer can be one); race types as ids go with E10, milestone triggers stay an
enum (a new one needs code to count it, so a string id gains nothing); a save played with older mods warns only when
one is older, not newer.

- **What was there already:** saves stamp their mod set on every save (`GameState.Mods`), a missing mod is warned of on
  load (`ModsInSave`) and what needs it is tidied away (events, dealers' cars, parts to the shelf, `Legacy` cars).
- **Older mods:** `ModsInSave.Older` compares each saved mod's version with the installed one (`ModVersion`); an older
  one, the base game too, goes in the same dialog with both versions (`loadgame.mods_older*`, `loadgame.mods_load_anyway`).
- **Bosses:** a racer with `"boss": { "path", "money", "carChoices", "races", "wins", "pinkSlipsWon" }` (every figure
  optional, the rules' `rivals.king` for what is left out; `"isKing": true` is `"boss": { "path": "King" }`).
  `Opponent.IsBoss`/`BossPath` replace `IsKing`, which an old save's King is read from once (`Bosses.BringUpToDate`).
  `EnsureBosses` puts every boss a game lacks in it (a mod added later too), once per game state. A boss comes out
  when his path opens (`Bosses.PathOpen`; none, or a path no mod has: from the start). The career keeps its bosses
  (`CareerState.Bosses`, refreshed with the standing) for the new `beatBoss` step (every boss of the path, or
  `"boss": <racer id>` that one; `beatKing` is the same; a share of the bosses beaten counts as progress).
  `KingName` stays for saves that have not looked their bosses up.
- **A boss's own lines:** `Bosses.Key` (since E12 `RacerWords.Key`, for every racer) takes `<key>.<racer id>` when a mod has it, else the King's
  (`talk.pulled_up.king`, `replies.king.*`, `street.king_out`, `chat.fact.rival_king`, `news.king_beaten/won.<id>.*`).
- A mod's path or racer id without a namespace means its own, else the base game's (`Bosses.Qualify`/`Means`).

Left for later: the street's `kingWeight` and the boss's car pick are the same for every boss; the AI talk's `{king}`
placeholder keeps its name (yes for any boss). Not seen in game.

### E8: flow (2026-10-02, branch `feature/flow`)

The owner's decisions: every system can be switched off, each with a fallback; races from the racing hub come back to
the garage (they went to the diner); the flow's own script triggers play on the screens with a car on a 3D scene; the
system switches are rules (kept in the save), the screens, exits, tiles and cards are read at start-up.

- **The flow** (`Navigation/FlowBook.cs`, content type `flow`, merged key by key): screens by id, each of a kind the
  program has (`ScreenKinds`), with named exits; `home` (a garage screen) and `menu`; the hubs' tiles (`TileActions`, `TileSpots`,
  `TileRequirements`); the main menu's cards (`MenuCards`); `endDay` per screen. Checked on load: what does not read is
  a mods' problem and left out; exits and tiles to a screen a mod took `off` go quietly.
- **Navigation** goes by id: `NavigationService.Go(id, game, FlowArgs)` builds a screen by its kind (the 18 typed
  `NavigateToX` are gone), `Leave(screen, exit)` takes a way out (one that leads nowhere goes home), `GoFrom(screen, id)`
  for a hub's tile, `GoHome`, `GoHomeLater`, `GoToMenu`, `GoRace` (the intent's `ReturnTo` is the screen on show; the
  two return flags are gone). `FlowArgs` carries what a screen is told (the shop, the dealer, the car, the race, skip
  the animation, the car arriving, back from a race, end the day). Each screen knows its flow id (`IdOf`) and the one
  before it.
- **Buttons from data**: the garage's tiles (`GarageTile`, six `ItemsControl` spots), the main menu's buttons
  (`MainMenuButton`; each card's kind has its picture), the racing hub's Hit the streets, the diner's Cruise, the
  street's Diner, a map's Mail order, the Sell dialog's ad, the Stand toggle: each shown only while it leads somewhere.
- **Systems** (`Rules/SystemRules.cs`, `rules/systems.json`): `police` (`PoliceCars.Installed` is none), `station`
  (`StationStandInTask` fills, tops up and washes overnight; the garage's hint says so), `paper` (the car ads' nights,
  the Sell dialog's ad; part ads go on for the mail order, deliveries always), `carDealers` and `partShops` (their
  nights; a Mail order tile `without` part shops), `mailOrder`, `street`, `diner`, `engineStand` (no stand drawn or
  clicked, the engine to the shelf), `talk` (`TalkDesk.IsAvailable`), `career` (`VictoryConditionService.ActivePath`:
  `careerPath`, else the first reached). A task names its system (`IScheduledTask.System`); the scheduler skips it.
- **End Day**: the garage's button follows its screen's `endDay`; elsewhere the window has one under the money card
  (`NavigationService.CanEndDayHere`), which asks, then goes home with `FlowArgs.EndDay` and the garage ends the day.
- **Triggers**: `screen.<id>.enter` and `screen.<id>.leave` (`ScriptTriggers.ScreenEnter/ScreenLeave`) through
  `IScriptedScreen` (the garage, the car lot, the gas station): the view hands the screen a way to play a script, the
  screen tells `NavigationService.ScreenReady` when its own show is over (the lot once its cars stand, the garage once
  its car is there), and `Leave`/`GoFrom`/`GoRace`/the hub's streets play the leave script first (`Depart`); a screen
  with more to put back than its car awaits `LeaveAsync` (the station's curtain, the lot's warehouse) and stays
  `IsLeaving` meanwhile. Facts gain `Screen`, `From`, `To`; conditions gain `from` and `to`.
- Text: `garage.tile.mail_order`, `garage.fix.gas.overnight`, `garage.fix.repairs_and_gas.overnight`,
  `shell.end_day.tip`; the screens' names for a failed navigation are by id (`menu.screen.dealer_lot`,
  `menu.screen.sound_browser`), else by kind.
- Tests: `FlowTests` (the base flow as the game was, switches, a mod's flow, problems, from/to, the race's way back,
  End Day) and `SystemSwitchTests` (nights, the station's stand-in, police, the career's path), in their own
  non-parallel collection.

Not done, for later steps:
- the street has no `screen.street.*` triggers: the player's car there is the camera; the diner and the paper have no
  scene;
- the nightly tasks' order stays the code's (two of them must be last); a game switches them off only by system;
- the Sell dialog's dealer stays with no car dealers (a game needs a way to sell);
- the garage's tiles were not compared pixel for pixel with the old buttons (the same styles, sizes and margins);
- not seen in game.

### E7: car scripting (2026-10-02, branch `feature/car-scripting`)

Built as agreed, on the owner's go after step 26 ("We can proceed"). What a modder writes is in
[modding.md](modding.md), "scripts".

- **One car driver.** `Services/Scripting/DrivePlan` works a drive out whole before the car moves, as a table in time:
  four kinds of feel, each the maths of a mover it replaced with that mover's tuned numbers as the base game's feels
  (`road`: the gas station's spline path, which with a fixed gear is also the rival's lane; `launch`: the rival leaving;
  `pull`: the lot's steer-for-the-exit, the bicycle model now stepped once up front; `roll_in`: the garage's). A
  scratch test held the new drives against the old movers before they were deleted: the station's, the lot's and the
  garage's positions to a tenth of a millimetre, the rival's to 10 cm (3 ms of driving); only a few revs differ at a
  change of gear. Reverse is new (the car faces back, its front wheels steer the other way); so are the front wheels
  steering on every drive (they turn with the path's bend), not only in the lot. `Controls/Scripting/ScriptedCar`
  drives it a frame at a time in any viewport (the wheels, the squat, dive and lean, the engine driven by the wheels,
  the brake lights), and each viewport puts the car where it says its own way (the garage's floor, the station's
  ground). `CarPull`, `DriveOff`, `PathDrive` and `RivalDrive` are gone; `ChassisPitch` has a file of its own.
- **Scripts and feels.** `ScriptBook` reads them from every mod's `scripts` folder, strictly: an unknown key, a step
  that is no step, a feel no mod has, and the script is left out with a line in the mods' problems (one replacing the
  base game's leaves that standing). Several scripts may share a trigger: the latest mod's whose `when` holds plays.
  Feels are read only from a `feels` list (a file that is one script is never taken for a feel).
- **The runner.** `ScriptRun`: the game goes on at a `release` step or the end; a click (not a drag) on the viewport
  skips to the end, as does going past `most` (30 s); a failing step is a problem line and the rest is skipped the same
  way, so the car always lands where the script ends; the screen going cancels it. Time is an `IScriptClock`, so the
  tests move it by hand. No script on a trigger: nothing moves and the car is where the move ends (the station's at the
  pump, the rival alongside).
- **Marks and paths.** `SceneMarks`: every scene's JSON may name `marks` (points, or measured from the car's home:
  `{ "behind": 7 }`) and `paths`. Each scene adds its own: the warehouse's `exit` (straight ahead without one), the
  garage's `door` from its new `garages/garage/scene.json` (the folder is git-ignored for the showroom; that one file is
  let through), the station's `arrive`, `leave` and `pump`, the street's `arrive`, `leave`, `alongside` and `gone`.
  The street's leave drives `to: "gone"` from where the rival is, as before: a rival told to go while still coming up
  the lane launches from there.
- **Triggers wired.** The lot (`TakeOut`, the curtain its fade), the garage (`Arrive`; `Leave` before the gas station,
  with a curtain of its own, the car put back if the station will not open; the base game has no `garage.leave`
  script), the station (`station.arrive` lets the pump's buttons in when it lets go, then `station.at_pump`, also after
  a tow; `station.leave`), the street (`street.rival_arrive`, `street.rival_leave`). The scripts' facts: the game's
  clock (the stages carry it now) and the light, and towed at the station.
- The trigger names sit in the text key spaces (`garage.`, `station.`, `street.`): `WordsTests` lists them as no words.

Not done, for later steps:
- the garage's showroom walls were not checked against the drive-in from the door (the same 7 m as before);
- a click does not skip the garage's drive-in: the garage takes no clicks while a car comes in (as before);
- the diner has no 3D scene: `diner.*` triggers come with one (its own step);
- `station.at_pump` and `garage.leave` have no base script; the flow's `screen.<id>.enter/.leave` came with E8, a
  trigger of a mod's own naming still has nothing to set it off;
- the player's car on the street is the camera's seat: a street script moves only the rival.

Review fixes (2026-10-02): a click that skips a script lets go of the mouse; a fade's `engine` multiplies the
screen's own level (`EngineRunner.Fade`) and does nothing once cancelled, and the garage puts it back (and stops an
engine the script started) when the station will not open; the garage goes to the station only while it is still
the screen; a rival sent away while its car loaded never comes; a pull turns round for a mark beside or behind it,
and drives straight on without one (the warehouse's `exit` default is a mark far ahead in `WarehouseScene`); a roll
in takes 60 s at most; the station's and the street's stage reports log what goes wrong; an orbit viewport lets go
of a car once its script is over and it stands still, back in its place; a car's home comes from the same resolved
marks its scripts drive by (`SceneMarks.Stand`); the garage's camera is not reframed when no script plays; one
`SceneStage` for every screen, one mark parser (`SceneMarks.ParseMark`) for scene files and scripts; a scene's file
is read once; the scripts and the garage's marks are read at start-up.

### E6: cars and makes (2026-10-01, branch `feature/cars-makes`)

The owner's decisions: the factory engines and prices set by hand in the developer's local catalog.db (about 100 cars)
go into the base game's **`cars.json`**, which ships, and the **catalog editor saves there** from now on (catalog.db keeps
only what the game works out); **no car filter** in the base game (every installed car, as before), the filter being
there for mods; the **part groups in a file of their own** (`parts/part_groups.json`), not on the work-order kinds,
which cover engine jobs only.

- **Makes** (`Parts/Cars/CarBook`, a new content type `cars`, merged key by key like the rules): `makes.json` holds
  each make's words, family, country, price factor and how common its badge makes a car, the countries with their names
  and regions, and the words that match a car's name to an engine's (aliases, noise). It replaces four C# tables:
  `MakeFamilies`' keywords (still the API, over the book), `CarProfileService`'s brand multipliers and brand rarity, and
  `OriginFilter`'s brand countries and region aliases. A car's make is the first a word of its brand tells, else of
  its name; a make without a family names no engine, so the families came out exactly as before (checked on every car
  name, engine build and part name of the installed game). Linked into EngineBench, which reads its Content folder's.
- **Cars** (`cars.json`, by AC folder name): `engine`, `layout`, `price`, `common`, `make`. Laid over the stored profile
  as it is read (`CarProfileRepository.Given`), never written into catalog.db, so a mod taken out leaves the car to the
  game's guess. A field the mods give takes over from what was set by hand in the catalog at the next start-up (a
  Manual profile goes back to Generated, a picked engine or layout is let go). The editor writes the fields set by hand
  (the price with how common the car is, the engine, the layout; a reset takes them out) into the installed base
  game's `cars.json` and the source tree's when it runs from a build of it (`BaseCarsFile`), then reads the book again.
- **Pricing** (`rules/cars.json`, `RuleBook.Cars`): the price curve, the year and source factors, the clamp and rounding,
  the believable weights, and how common a car is by year, source and power. Read from the mods' rules, not the
  career's (the catalog is the program's); the profile's hash holds the figures and the car's badge, so a change works
  every generated price out again. The 158 installed cars' prices and rarities came out the same to the cent.
- **Engines** (`rules/engines.json`, `Parts/Cars/EngineRules`, pointed at the career's rules by `CarPartsService`, the
  base game's figures in the tools): the cooling and oil figures (`EngineCooling`), the factory's swaps and bolt-ons
  (`EngineFactory`), the tuners' (`EngineTuner`), the engine matcher's points (`StockEngineMatcher`). The constants kept
  their names as properties.
- **Car filter** (game.json `cars`, `Rules.GameCars`): years, countries, makes, ids in, ids never, only the ids;
  applied in `InstalledCars.Only`, which every picker of a car goes through (the lots, the rivals, the events, the main
  screen and the showcase).
- **Part groups** (`Parts/PartGroups`, `parts/part_groups.json`): each group's classes, `never_stacks`, `bolt_on`,
  `tune` (the tuners' priority), `speed_shop`; a mod's new groups go first, one of an id already there changes it in its
  place. Group ids are now snake case (`engine_blocks`; no save held them); the player reads `part_group.<id>` in the
  text (`ui_parts.json`). The ids the code knows (the internals that wear, the running gear's slots, the attendant's
  air filter and exhaust) are the game's vocabulary: a mod changes their classes, not their meaning. Every part of the
  base game landed in the group it was in.
- **Stand-in classes** (`parts/stand_ins.json`, `ScriptClassLoader.UseStandIns`): the Ford six's `Block_Inline_OHV`
  out of `Block_Vee_OHV`; each mod's parts folder may have one; the converter reads the base game's.

Left for later, on purpose:
- the starting cars and the police cars are not held to game.json's `cars` (a game names its own in its rules);
- a make's name is not shown anywhere yet (shops show the pack's make id, `gm`); makes have no `name`;
- the editor writes into the base game's file even a value a later mod gives the car;
- engine figures from data, turbo lag, more gears, transverse engines: data engines (E11);
- the rivals' challenge odds and traits: personality, not setting (as in E2);
- not played in game: the market, the editor's save and the start-up hand-over were checked by tests and a read-only
  look at a copy of the developer's catalog.

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
