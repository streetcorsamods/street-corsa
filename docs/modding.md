# Modding Street Corsa

Everything the game knows beyond Assetto Corsa's cars and tracks is **content in mods**: parts, engine sounds, dealers,
racers, events, milestones, victory paths, streets, the diners, gas stations, warehouses, the part shops' room, the main screen's rooms, garages, the
words the game says, and the rules it plays by (what things cost, how long they take, how the police and the rivals
behave). The base game is itself a mod, `Content\streetcorsa`, loaded through the same loader as any other, first.

Cars and tracks stay AC's: install them in AC's `content` folder as usual.

**The example mod**, `examples\mods\small_imports` in the repo, is an add-on made of data alone to copy: four small
European cars with engines of figures, their speed parts, a shop, a lot, a rival, a format and an event. Its README
says how to try it and what each file shows.

## A mod

Drop a folder into `Content`, next to the game's exe:

```
Content\
  streetcorsa\          the base game (loaded first)
  mymod\
    mod.json
    game.json
    rules\  parts\  sounds\  sfx\  props\  dealers\  partshops\  racers\  events\  milestones\  victories\  streets\  diners\  stations\  booths\  warehouses\  shopscenes\  showcase\  garages\  text\  theme\  ai\
```

`mod.json`:

```json
{
  "id": "mymod",
  "name": "My Mod",
  "version": "1.0",
  "author": "you",
  "description": "What it adds",
  "order": 0,
  "type": "addon",
  "requires": { "othermod": "1.2" },
  "gameVersion": "0.1",
  "removes": { "racers": ["king"], "text": ["news.king_beaten.headline"] }
}
```

- `id`: lower case letters, digits, `_` and `-`, at most 40; not `streetcorsa`, not `car`. Left out, it is the folder's
  name. Two folders with the same id: the first (by folder name) loads, the second is left out.
- `order`: mods load after the base game, lowest `order` first, then by id. A later mod's entry wins where entries
  merge (a sound pin, a showroom's measurements, a scene's wall radius). A mod always loads after the mods it requires,
  whatever its `order`.
- `type`: `"addon"` (the default: it adds to the game) or `"game"` (a whole game of its own, a total conversion). Only
  recorded for now; what a `"game"` mod does is the total conversion's later steps (`docs/total-conversion.md`, E9).
- `requires`: the mods it needs, each with the lowest version it works with (`""` for any). One that is missing, left
  out, or older is told, and this mod is left out (and so is a mod that requires it in turn).
- `gameVersion`: the lowest version of the game it works with. On an older game it is left out, told.
- `removes`: entries of the mods before it (the base game's included) it takes out of the game, by kind of content, full
  ids: `dealers`, `partshops`, `racers`, `events`, `milestones`, `victories`, `streets`, `stations`, `booths`, `warehouses`, `shopscenes`,
  `text` (pools, by key) and `sfx` (cues, by name). What the mod itself or a later mod adds under the same id stays: a
  mod can take out a base game's pool or cue and give its own. An id no mod before it has is told. Parts can't be
  removed (yet): other parts and engine builds hang off them.
- A folder whose name starts with `_` or `.` is switched off.
- Only the folders a mod has are read; every one is optional.

**All in mod.json.** A mod can hold its data in its `mod.json` instead of files, so a mod is one file beside its parts
and banks (its parts are files of their own, see "parts" below). Each section is what the file of its name would hold,
read after that file:

| mod.json | holds what this file holds |
|---|---|
| `builds` | `parts\engine_builds.json` |
| `part_aliases`, `build_aliases` | `parts\part_aliases.json`, `parts\build_aliases.json` |
| `cars`, `makes`, `countries`, `words` | what `cars\cars.json` holds under `cars`, and `cars\makes.json` under `makes`, `countries` and `words`; a car's `engine` written short is the mod's own build first, then the base game's |
| `sounds` | `sounds\sounds.json` (`pins`, `cars`); the banks stay in `sounds\<name>\` |

A name at the top of a mod.json that is neither a field of the manifest nor one of these sections (`"build"` for `"builds"`) is told in the start-up problems; notes start with `_`.

```json
{
  "id": "mymod", "name": "My Mod", "version": "1.0",
  "builds": [ { "id": "factory/mine", "name": "Mine", "parts": [ "engines/mine/block" ] } ],
  "cars": { "some_ac_car": { "engine": "mymod:factory/mine" } },
  "sounds": { "pins": { "engines/mine/block": "my_v8" } }
}
```

A section that does not read is told, and the rest of the mod loads. The engine mods are made this way: one `mod.json`,
their parts in `parts\` (a `.json` and its `.kn5` each) and `sounds\<bank>\`, nothing of another mod's needed.

What could not be read is written to the log (`%AppData%\StreetCorsa\Logs`), one line each, and the rest of the mod
loads. Start-up logs the mods it found (`Content: streetcorsa 0.1.0, mymod 1.0`), reads every mod's content once, and
shows the player the problems it found, if any: at most a dozen lines, the rest in the log.

## game.json

The game's own settings: what it is called (the window's title) and the garage it is played from. Every mod may have a
`game.json` next to its `mod.json`; it goes over the mods' before it key by key, so a mod names only what it changes:

```json
{ "title": "Wangan Nights", "garage": "tokyo_garage" }
```

- `title`: the window's title.
- `garage`: the garage the game is played from: a mod's `garages\<id>` (see "showcase and garages"), else the AC
  showroom of that id. The base game's is its own, `workshop`.
- `language`: the language the files directly in the mods' `text` folders are written in (`"en"`); see "text".
- `locale`: how the game writes what the player reads:
  - `culture`: a .NET culture name (`"en-US"`, `"ja-JP"`, `"de-DE"`) for numbers, dates, month and day names.
  - `money`: `symbol` (`"$"`, `"¥"`), `after` (the sign after the amount: `"1.234 €"`), `space` (a space between) and
    `decimals` (0 for whole dollars). Amounts are in the game's own currency: a yen game writes yen in its rules and
    prices; nothing is converted.
  - `units`: `"imperial"` (mph, miles, gallons, quarts, °F, lb, lb-ft) or `"metric"` (km/h, km, litres, °C, kg, N·m).
    The player may pick the other in Settings. The game keeps every figure metric inside; only what is shown changes.

```json
{ "locale": { "culture": "ja-JP", "money": { "symbol": "¥", "decimals": 0 }, "units": "metric" } }
```

  The words of each unit (`unit.mph` = `"{n} mph"`, `unit.mph.name`) and the patterns of the dates the screens write
  (`format.time` = `"h:mm tt"`, `format.date`...) are text pools in `text\locale.json`: a mod or a translation writes
  them its own way, and a mod without them shows the unit's id (`104 mph`). A culture .NET does not list (`"qq-QQ"`,
  `"en_US"`), or units that are neither, are told and the base game's used.
  The program itself keeps US English for the files it reads and writes (race.ini, the saves): only what the player
  reads is in the game's culture.
- `cars`: which of the installed AC cars are in the game (the dealers' lots, the rivals' cars, the events, the main
  screen). Every one when it is not given, as in the base game. `from`, `to`: the oldest and newest model year (a car
  whose `ui_car.json` gives no year is out when either is given); `countries`: countries or regions, any of their names
  (`"USA"`, `"American"`, `"Europe"`: see "cars" below), matched against the car's own country, else its make's;
  `makes`: ids of makes in makes.json; `ids`: AC folder names of cars that are in whatever the rest says; `never`: ones
  that never are; `onlyIds`: the game has the cars of `ids` and no other.

```json
{ "cars": { "from": 1985, "to": 2005, "countries": [ "Japan" ], "never": [ "ks_toyota_supra_mkiv_tuned" ] } }
```

Unlike the rules below, these are the program's, not a career's: a change shows in every save. The player's own
choices (the AC folder, the volumes) stay in their `%AppData%\StreetCorsa\settings.json`. With no AC folder there, the
game uses Steam's when it finds one, and writes it into the settings.

## Rules

Every figure the game plays by is in `rules`: what a car or a part costs, how long a job takes, what a race pays and
wears, how the police, the dealers, the shops and the rivals behave, when the sun sets. The base game's are
`Content\streetcorsa\rules\*.json`, one file per part of the rules, every figure written out with its value: read them
to see what there is.

A mod's `rules` folder holds `.json` files whose top level names the part (`police`, `market`...); a file may hold any
parts, and a part may be spread over files. They go over the rules before them **key by key**, so a mod gives only what
it changes:

```json
{
  "police": { "dayChance": 0.15, "baseFine": 30000 },
  "start": { "money": 500000, "date": "1999-04-01", "cars": [ { "car": "ks_toyota_ae86_drift", "condition": 0.6 } ] },
  "time": { "actions": { "BuyCar": 90 } },
  "world": { "midsummer": 355, "summerMonths": [12, 1, 2], "winterMonths": [6, 7, 8] }
}
```

- An object goes into the one before it (a table given in part, as `time.actions` here, keeps the rest of its
  entries); a list or a value takes the place of what was there.
- A name the rules do not have (a typo) is told and left out, in the entries of a list (`start.cars`) and of a table
  (`partShops.kinds`) too; a part that does not read (a word where a number goes) stays the base game's, told. Keys
  that start with `_` are notes.
- `null` is the base game's figure (or table), and no entry in a list. A least and a most given the wrong way round
  (`market.maxMileage` set below the base game's `minMileage`) are turned round, told.
- A kind of part shop the base game does not have (`partShops.kinds.tuner`) takes what it does not give from
  `partShops.otherKind`.
- A file with the same name twice in one object (whatever its case: `"title"` and `"Title"`) is left out whole, told.
  This goes for `game.json` too.
- **A career keeps the rules it was started with.** They are written into the save when it starts: a mod updated or
  taken out later does not change a game under way. A figure the save does not have (one a later version of the game
  added) comes from the mods as they are. A save from before rules were kept plays by the mods' from its next save on.
- Money in the rules is in the game's own money: a game in yen writes yen.

| Part | What it holds |
|---|---|
| `start` | the player's money, the first day, the cars in the garage on that day (`car`: the AC car's folder, `skin`, `condition` 0-1, `mileage` km; a car AC does not have is left out) |
| `day` | the hours the player's day starts and ends (an `endHour` past 24 is midnight) |
| `time` | minutes each action takes (`actions`, by the game's action names), each part at the workbench, an engine in or out, the stand, the lift from one stop to the next, a tick of cruising, the shortest trip |
| `difficulty` | the New Game screen's presets (`easy`, `normal`, `hard`) and how often pink slips come up at each frequency |
| `police` | how likely a street race draws them, fines, the impound, cars per race, the AC cars they like best |
| `rivals` | how many race, their money (`money`, by age band), going broke, trading up, leaving and coming back, selling cars on, how many tune a day and how often (`maxTunersPerDay`, `tuneChance`, `saleTuneChance`), how long the street talks (`streetTalkDays`, `maxStreetTalk`), a boss's money and record (`king`) |
| `dealers` | dealers opening and closing, closing-down sales, trade |
| `partShops` | the junkyard, haggling, the regulars' discount, trade, a delivery's top-ups (`topUpChance`), each kind of shop's prices and shelves (`kinds`) where a shop's own file says nothing, and the counters: how many picks a clerk is told of (`mostPicks`), parts and whole engines tried on the dyno for one ask (`mostDynoRuns`, `machineDynoRuns`), other customers' work round a machine shop (`othersWork`) |
| `economy` | a factor on every part's price (`partsPriceFactor`; the prices themselves are part_groups.json's), used and trade-in shares, what wear leaves of a part's and a body's worth (`wearFloor`), mail order, the paper's part ads (how many, how worn, what the sellers ask: `partAdMinAskShare` to `partAdMaxAskShare`), a prize camshaft's cash |
| `market` | the dealers' lots, selling a car (the paper's buyers: `paperBaseChance`, `paperFairShare`, `paperChanceSlope`, `paperUnpricedChance`), how often a used car's engine was worked on (`usedCarTuneChance`, `usedCarMinTuneLevel`), and what a car's looks and history do to its worth |
| `cars` | what a car's bare body costs new (`bodyPrice` at `bodyKg`, more for a lighter one and less for a heavier one as the ratio to `bodyWeightPower`, within `minBodyPrice` and `maxBodyPrice`), the least and most a whole car costs, the rounding, the believable weights, and how common a car is (where cars.json does not say) by year, source and power to weight. A car costs its body and its factory engine's and running gear's parts (see "parts", the shelves' `price`). Unlike the rest these follow the mods next to the game, not the career: the catalog is the program's, priced again at every start |
| `engines` | how an engine is cooled and oiled by its parts (`cooling`), how the cars on the lots were worked on (`factory`: swaps, bolt-ons), how the rivals' tuners work (`tuner`), and the points an AC car's name and power give an engine build when the game picks its factory engine (`matcher`) |
| `repairs` | the garage's repairs: parts, labour, body work |
| `condition` | damage (and how little of it counts as none, `undamagedTear`), wear, dirt, fuel and oil, and what a race takes out of a car |
| `street` | who comes by while cruising and how soon, drag or road, pink-slip offers, grudge rematches, the diner's talk |
| `wagers` | the smallest and largest cash wager, night stakes, a fair match |
| `reputation` | how reputation is worked out: base, wins, pink slips, losing and winning streaks, false starts, escapes |
| `events` | how often race invitations come, how many at once |
| `news` | what the paper prints and for how long |
| `simulation` | the rivals racing among themselves: crashes, pink slips |
| `race` | AC's damage, tyre wear and fuel rates in a race, how the police drive |
| `world` | the sunset through the year, the seasons the rivals race in, AC's weather, temperature, wind, latitude and time zone |
| `systems` | the systems the game has, each `true` in the base game: `police`, `station` (the gas station), `paper`, `carDealers`, `partShops`, `mailOrder`, `street`, `diner`, `engineStand`, `talk`, `career`, `paintBooth`; and `careerPath`, the victory a game with no career screen is won on (none: whichever comes first). See "flow" for what goes with one switched off |
| `ai` | the player's AI (Settings, "AI"): how long a rival's line waits for it (`talkWaitSeconds`), a request's and a picture's timeouts, requests at once and the gap between them, the queue, the rest after a failure, lines written ahead, the chance a line of street talk or a piece in the paper is written again, whether new faces are painted, and how many of its words a save keeps |

## Ids

Every entry has an id, written **without** the mod's name. The game puts the mod's id in front: the part
`engines/gm/intake` of `mymod` is `mymod:engines/gm/intake` in the game. Two mods can never take each other's ids by
accident. The base game's ids go without a namespace (`engines/gm/V8_small_block`), which is why saves made before
mods still work.

A **reference** to another entry (a part of an engine build, the milestone an event asks for, the block a sound is
pinned to) finds the mod's own entry first, then the base game's. Written in full (`othermod:x`, or
`streetcorsa:engines/gm/V8_small_block`), it means exactly that entry.

To **change** an entry of another mod, or of the base game, give yours `"replaces": "<its full id>"`: it takes that
entry's id and place. An entry that replaces nothing is left out and logged. Every replacement is logged too.

## Saves and mods

A save remembers the mods it was last played with. Loading it without one of them asks first, then plays on without
it:

- its parts wait: on the shelf, where the workbench does not show them, and on engines, where they do nothing. An
  engine whose block is the mod's comes out of its car whole and waits on the shelf; the car gets its factory engine.
  All of it is back with the mod;
- cars of a car mod that is gone are left as they are (`Legacy`);
- racers live whole in the save and stay;
- invitations to its events go; its dealers and the cars on their lots go;
- a victory path picked from it is no path until it is back;
- the paper and the street talk are kept as they were written.

Only a mod that is not installed takes its content with it. An entry an installed mod leaves out (one that does not
read) is skipped until the mod is fixed; the save keeps what it had of it.

Parts that change ids between versions of a mod keep working through the mod's `part_aliases.json`. Saves know a mod
by its id, never by its folder: renaming the folder (`rivals-1.0` to `rivals-1.1`) loses nothing.

## Content types

Every JSON file may have `//` comments and trailing commas. Where a type is a list of entries, a file may hold the
list under its name (`{ "dealers": [ ... ] }`), be the list (`[ ... ]`), or be one entry (`{ "id": ... }`): one file per
entry, or all in one. A `null` where a list or a text goes reads as none written; an entry that does not read (a text
where a number goes) is left out on its own, logged.

### parts

Laid out as the base game's `Content\streetcorsa\parts` (see [parts-system.md](systems/parts-system.md)): **one
`.json` per part** anywhere under the folder, its `.kn5` model next to it, in whatever folders the mod likes (the base
game sorts its own by kind, `engines\generic\`, `rims\mopar\`; an engine mod keeps them all in `parts\`). Every `.json`
under the folder is a part, but these, directly in it, which are the catalog's:

- `engine_builds.json` — complete engines: `[ { "id": "rated/my-327", "name": "Chevy 327 350 hp", "origin": "rated",
  "rated_power": 350, "parts": [ "<block id>", "<every part on it>" ] } ]`, the block first. `origin` is `factory` (what
  a car left the factory with), `rated` (a known output the engine model is held to), `kit` or the mod's own word;
  `aftermarket: true` marks a speed-shop build no car gets from the factory. Build ids are namespaced like parts.
- `build_aliases.json` — `{ "old build id": "new build id" }` for builds the mod renamed, as for parts.
- `part_aliases.json` — `{ "old id": "new id" }` for parts the mod renamed; the old id is the mod's own unless written
  in full.
- Part models are drawn at the size they were made: ship them at AC's scale (metres).
- `models.json` — models of the mod's own for parts of any mod, the base game's too, without replacing the parts (a model
  pack): `{ "models": { "running_gear/springs_leaf/rear_150": "truck/leaf.kn5", "running_gear/sway_bars/front_*": "bars/front.kn5" } }`.
  A key is a part's full id, or the start of one ending in `*`; the model is a KN5 under the mod's parts folder, in the
  part's own space (its origin where it mounts; see parts-system.md, "The running gear's models"). The part's figures,
  slots and price stay its own; of two mods the later one's model wins. A key that names no part, or a model that is not
  there, is a line in the log and left out.
- `work_order.json` — what comes off an engine or a car's running gear before what (see below). Most mods need none:
  the base game's covers every kind of engine and running gear part there is.
- `part_groups.json` — the shelves (see below). Most mods need none.
- `rebuilds.json` — the work a part can have done on it to take its mileage off (see below). A mod's parts of the base
  game's kinds and shelves get the base game's jobs.
- `part_classes.json` — the classes of part and what each is a kind of (see "A part" below). Most mods need none.
- `fuels.json` and `slot_shifts.json` (the garage's placement nudges) are the base game's.

A part's id is the one in its file, wherever the file is: moving or renaming files changes nothing a save holds. A
`pack.json` with a `parts` list (the parts of before, many to a file) is not read, and told. A mod's new version goes
into an empty folder: unpacked over the old one, the files it renamed or dropped stay, and a part in two files is the
newer file's (told at start-up).
- `fuels.json` — the fuels a fuel system can run on (see "A part" below). Most mods need none.

**The shelves.** Every part is in a group: the shelf it is on in a shop, what the player reads as its kind, and what the
game does with it. A part is in the first group with a class its own class counts as (`"brake"` takes in a
`disc_brake`, see `part_classes.json`), the groups in the order listed; a part no group takes in is an accessory. A mod's
`part_groups.json` adds groups, tried before the ones before it, or changes a group of an id already there, naming only
what it changes (a group keeps its place). `never_stacks`: never mounts on its own kind (a head on a head);
`bolt_on`: what the cars on the lots were worked on with; `tune`: how much a rival's tuner goes for it (10 the most,
none: not at all); `speed_shop`: a speed shop sells it; `for_sale`: parts of the kind are for sale at all (in the
paper, by mail order, on the shops' shelves; the running gear is sold by what it is, and a kind without it only as far
as an engine build uses it: the base game's mufflers, which only dress the tailpipe, are not for sale). The player
reads `part_group.<id>` in the text, else the group's `name`, else its id.

`price`: what a part of the kind costs new, in the game's dollars, by what it does. `base` for every part, and `per_hp` for every horsepower it is worth on the game's dyno: an engine block the
power of its own engine (the least of its factory builds), any other part what it makes over the factory part in its
place on the build it does the most for (measured once at every start), the horsepower raised to `hp_power` first
(none: 1). A kind priced `by` a figure of its own costs as much more or less as its figure stands against the middle
one of its kind, to the power `by_power`: `grip` (a tyre's friction by its width), `brake_torque`, `rim_size`,
`spring_rate`, `damping`, `carb_mounts` (the carburettors a manifold takes: a dual-quad is a kit no dyno run of the
manifold alone measures), `strength` (what a rear axle part holds), `diff_lock` (how hard a carrier locks). The `accessories` group takes no classes: it prices the parts no group takes in.
`EngineBench <Content folder> prices [AC cars folder | part id]` lists the prices by kind, every installed car's new
price, or the power and price of the parts whose id holds what is given. The ids `engine_blocks`, `cylinder_heads`, `camshafts`, `crankshafts`, `connecting_rods`,
`pistons`, `transmissions`, `air_filters`, `exhaust`, `oil_pans`, `tyres`, `rims`, `brakes`, `springs`, `shocks`,
`rear_axles`, `differentials`, `ring_pinions` and `axle_shafts` are the game's own: the engine's internals wear and break,
the running gear has a slot on each wheel, the rear axle one under the car, the attendant looks at the air filter and the
exhaust. A mod may change their classes, never their meaning. The base game's groups are
`Content\streetcorsa\parts\part_groups.json`.

**The shows.** The same file's `shows` are the garage parts view's buttons that put kinds of part out of sight: each has an `id`, an `icon` (a theme image name, `"icons/engine"`) and the `groups` it takes; the
first show naming a part's group takes it, `rest_of_engine: true` takes every other part of the engine, and a part no
show takes stays in sight. The tooltip is `part_show.<id>` in the text. A mod adds shows (after the ones before) or
changes one by its id. The base game has `engine` (the rest of the engine), `drivetrain` (the gearbox, the clutch and flywheel being the engine's,
and `differentials` and `transfer_cases` should a mod add those groups), `brakes` and `suspension` (the part dealers' icon).

```json
{ "groups": [ { "id": "intercoolers", "classes": [ "intercooler" ], "tune": 4, "speed_shop": true } ] }
```

**The work order.** Every engine part has a *kind*: `"kind": "timing_cover"` in the part's file if the mod
says so, else the first kind of the work orders listing its class or one above it (`"classes": ["crankshaft"]`), else
the first kind with a word its name holds (`"names": ["timing chain cover", "timing cover"]`, whole words, a plural s
allowed). A mod's kinds are tried before the base game's. A rule says, for a kind, what has to be off the engine first
(`"after"`, the parts on this very part included), whether it comes out only on the engine stand (`"stand_only"`) and
whether it goes by itself with nothing on it (`"bare"`), and what has to be on the engine before it goes on
(`"needs"`: a drive belt needs the water pump and the fan whose pulley it runs on; only where the engine has a place
for one and one may go on there, and what a part needs stays on while the part does). A kind's `"serves"` names kinds
whose job it does for a part that needs them: the base game's radiator with an electric fan (`radiator_fan`) serves as
the fan. It is only for needs: the rules of the kinds it serves are not its own. The rules work both ways: what has to
be off before a part comes off has to be off before it goes back on, and an engine that comes into the game with a part
lacking what it needs (a build short of its water pump, an old save) has that part taken off first. A kind that is in
the way on one engine and not on another is two kinds: a distributor goes down through the intake on a V8, so the
intake waits for it; the Ford six's sits on the side of its block (`"kind": "side_distributor"` in its file) and leaves
the intake alone. A part that only comes off another way than its kind stays of its kind, rules and all, and says so
itself: `"comes_off"` in the part's file, as for a kind below (the Ford six's intake manifolds have
`"comes_off": [ "out" ]`, its alternator `[ "up" ]`). `"stand_strips"` is what comes off an engine as it goes on the stand (the
radiator, and what is in the way of its bellhousing face), in that order: the garage shows them coming off one after
another before the hoist lifts the engine out. None of them goes back on while the engine is on the stand. A kind's `"comes_off"` is how the garage shows it
coming off, leg by leg, in the engine's own directions: `up`, `down`, `forward` (towards the crankshaft's nose),
`back`, `out` (away from the engine, on the side the part is on); `[ "back", "down" ]` for a gearbox. What is on the
part goes along. A later mod's `"comes_off"` for a kind replaces the earlier one's, and a part's own beats its kind's.
A kind's `"worked_from"`, `"above"` or `"below"`, is the side of the engine it goes on from: picking one on the stand turns
the engine that side up (an oil pan turns it over, a cylinder head back). Only looks; a later mod's replaces the earlier
one's, and a kind without one leaves the engine as it is. A kind's `"tool"`, `"impact"`, has it heard coming off with the
air gun (the drivetrain's big bolts, the wheels' lug nuts), else a socket wrench; a later mod's replaces the earlier one's.

The running gear (everything on a car that is not its engine: wheels, brakes, springs, the rear axle) has kinds and rules
in the same file, read the same way with two differences. A rule counts at the part's own corner: the base game's drum
waits for its own wheel to be off (`{ "kind": "drum", "after": [ "rim" ] }`), not the other three; a part across an axle
(the rear axle, a sway bar) counts at both its corners. And what is mounted on a part goes along without being in its way,
so the rear axle drops whole with the brakes and springs it carries; only `"bare"` asks for them off first. `"stand_only"`
means nothing to the running gear. `"hangs_on"` is the running gear's alone: the kinds a part sits on. On a car that has
a part of such a kind, or whose factory running gear has one, the part goes on only once every place the car has for the
kind holds one, and those come off only once the part is off: the base game's rear axle hangs on its leaf springs
(`{ "kind": "rear_axle", "after": [ "rim" ], "hangs_on": [ "leaf_spring" ] }`), so a coil-sprung car's axle goes on freely.
`"lifted": true` is the running gear's too: a part of the kind comes off and goes on only with the car up on the garage's
lift, at either stop (the base game's `{ "kind": "rim", "lifted": true }`); the car comes down only with all four wheels on.
`"comes_off"` legs are the car's own directions for the running gear: `forward` is the car's nose, `out` the side of the
car the part is on.

```json
{
  "kinds": [ { "id": "timing_cover", "name": "timing cover", "names": [ "timing chain cover", "timing cover" ] } ],
  "rules": [ { "kind": "crankshaft", "stand_only": true, "bare": true, "after": [ "oil_pan", "timing_cover" ] } ],
  "stand_strips": [ "radiator", "starter", "transmission", "clutch", "flywheel" ]
}
```

A rule for a kind that already has one adds to it (more to wait for, more it needs; `"stand_only"` and `"bare"` once
set stay set), so a mod's rule still counts when a later base game rules the same kind. To take any of it back, say
`"replace": true`: the rule is then yours alone. Rules that lock (kinds waiting for each other to be off, or needing each
other on, or a rule needing a kind it waits for) are reported. A kind of the same id adds its classes, words and
`"serves"` to the kind; the name the player reads is the first mod's. The base game's kinds and
rules are in `Content\streetcorsa\parts\work_order.json`.

**Rebuilds.** `rebuilds.json` lists the jobs the parts view offers on a part ("Work on it") to take its mileage off:

```json
{
  "jobs": [
    { "id": "carb_kit", "name": "Rebuild with a kit", "kinds": [ "carburettor" ], "restores": 0.95, "cost": 0.12,
      "cost_at_least": 8, "minutes": 90, "off": true },
    { "id": "brake_reline", "groups": [ "brakes" ], "restores": 0.95, "cost": 0.2, "minutes": 60 },
    { "id": "rebuilt_exchange", "way": "exchange", "kinds": [ "water_pump", "starter" ], "restores": 0.92, "cost": 0.45,
      "minutes": 45, "off": true, "fixes_damage": true }
  ]
}
```

A job is for the parts of its `kinds` (the work order's) or its `groups` (the shelves). `way` is `kit` (the player's own
work, the default) or `exchange` (a rebuilt one from the parts counter, the old part back as the core). `restores` is
the condition the part is in after it (0 to 1; a part in better shape gains nothing), `min_wear` the least it can be in
for the job (below it, it wants a machine shop), `cost` a share of the part's new price, never under `cost_at_least`
dollars, `minutes` the time it takes. `off`: done with the part off the car and off any other part (on the shelf, or
the engine on the stand); `bare`: with nothing mounted on it; `fixes_damage`: takes its damage off too. The player reads
`rebuild.<id>` in the text, else `name`. `steps` is what the player sees done while the job runs, in turn, each as long
as the others: `{ "id": "soak", "say": "Soaking it in carb cleaner", "sound": "work.wash", "start": "station.bell" }`,
read as `rebuild.<job>.<step>` in the text (else `say`) over the looping sfx cue `sound` (none: `work.bench`), with the
one-shot cue `start` played as it begins. The base game's work sounds are `work.bench`, `work.wash`, `work.hone`,
`work.lap`, `work.air`, `work.grind`, `work.press`, `work.road` and `work.counter`. A job without steps shows its name
over the bench's sound. A job of the same id as an earlier mod's takes its place, whole;
`"disabled": true` takes it out. The base game's jobs are in `Content\streetcorsa\parts\rebuilds.json`.

A job with `"becomes": true` turns a part into another, with what is on it, its wear and its damage: the part names what
in its file, by the job's id (`"becomes": { "weld_spiders": "running_gear/axles_gm/gm_10bolt_welded_low" }` on the base
game's open carriers, the welded carrier on the same shelf). It is offered only on parts that name one, and may restore
nothing (`"restores": 0`): the part is no better, it is something else. Not for a machine shop. A target that is no part,
left null or blank, or on another shelf, is told as a mod problem and left out. A welded carrier is priced by its own `price_lock` (how hard
it locks, as the differentials' price reads it) rather than as the spool it acts like.

The player's own work (a kit) lands round what the job can do: `roll` (0 to 0.5) is how far under `restores` it may come
out, up to a quarter of it over, never worse than the part was; left out, 0.08 for a kit, none for an exchange unit,
0.02 at a machine shop. The show ends saying how it came out (`rebuild.quality.good`, `.fair`, `.rough`).

**Machine shop jobs** (`"way": "machine"`) are not in the parts view: the player leaves the part at a machine shop
(`partshops`, kind `machine`) and it is back in the garage `days` later.

```json
{ "id": "bore", "way": "machine", "kinds": [ "block" ], "cut": "bore",
  "sizes": [ { "id": "030", "say": "+.030", "figures": { "bore": { "add": 0.762 } }, "worth": 0.9 },
             { "id": "060", "say": "+.060", "figures": { "bore": { "add": 1.524 } }, "worth": 0.65 } ],
  "fits_new": "pistons", "fits_new_cost": 0.9, "restores": 0.97, "cost": 0.003, "cost_at_least": 45, "days": 4 }
```

`cut` names what is cut; each time the job is done the part goes to the next of `sizes`, and past the last it cannot
be cut again (the part keeps how far it has gone, `PartInstance.Cuts`). A size's `figures` change the engine's figures
from then on, as a part's `"figures"` do (see "Engine figures"; they go on after the part's own), and its `worth` is the
part's worth at that size against the factory's (under 1 for a cut gone, over for speed work). A job with no `cut` takes
no metal off (a radiator re-cored) and can be done again once the part wants it. `restores` 0 with a cut leaves the
part's wear as it is (speed work). `fits_new` is a work order kind the machinist fits new to suit the cut: the part's own
of that kind are swapped for new ones of the size, or, on a bare part, a set of what the part's first engine build has
comes back with it, in the price at `fits_new_cost` of their new price; from then on that kind and the cut part only go
together at the same size (the work order says so). `any_size`: made to any size but the part's own, each offered
(new pistons for a block bored before; inside a block, only at its size). `speed`: speed work, listed apart. The player
reads `rebuild.<id>.size.<size>` (else `say`) and `rebuild.<id>.done` with `{size}` ("bored {size} over") on the
part's card. The machine shop's prices are the job's, never under the rules' `partShops.machineValueShare` (0.85) of
what the work adds to the part's worth, at the shop's `priceFactor`.

**A part.** Each part is a `.json` file of its own:

| Field | What it is |
|---|---|
| `id` | the part's id, unique in the mod, in lower case and `_`, words parted by `/` (`engines/mine/block`; the mod's namespace is added). The file's name and folder are free; the id is what saves and builds name |
| `display_name` | what the player reads (see "A part's name" below) |
| `class` | what the part is, a class of `part_classes.json` (below): `engine_block`, `camshaft`, `disc_brake`... It says how the part works, which shelf it is on and which work order kind it is. A part without one is never sold or fitted on its own |
| `model`, `mass` | the KN5 next to the part's file (in the part's own space, its origin where it mounts) and kg |
| `make` | which make the part is for (`"gm"`): every part shop specialising in that make stocks it (see `partshops` below). The base game's makes are `mopar`, `ford` and `gm` (the families of makes.json); a part for any make (the universal-fit carburettors and air cleaners) has none |
| `credit` | whose model it is, when its licence asks for it (`"\"Leaf Spring\" by 3dsl (sketchfab.com/3dsl), CC BY 4.0"`): the game's Settings credits show every one, each once. A model of the mod's own needs none |
| `specs` | the figures its class reads, in plain metric units (the tables below) |
| `slot_roles` | which of its slots takes what its class works with: `{ "crankshaft": 8, "left_head": 45 }` |
| `needs` | slots that must be filled for the part to work, with what the player reads when one is empty: `[ { "slot": 20, "message": "the engine is missing the alternator." } ]`. A block's are asked in their order, before the compression; then every other part's |
| `slots` | where it mounts and what mounts on it: `id`, `name`, `position` (m), `rotation` (yaw, pitch, roll in radians), and the fittings it mounts by (`fits`) and takes (`takes`) |
| `figures` | engine figures over what the specs work out (see "An engine of figures") |
| `kind`, `comes_off`, `performance`, `came_in`, `becomes`, `aftermarket`, `replaces` | see the work order, the running gear and the rebuilds below |
| `name` | optional: a short name of its own, read with the display name for the words the game looks for; the last part of the id when left out |

What a part costs is the game's (the shelves' `price`, by what it does on the dyno), never written in its file.

**How an engine is worked out.** The block is the root: what is on the slots its `slot_roles` name is what it works
with, and each part's class says what it adds (`Parts/Logic/EngineAssembly.cs` is the whole model, a page long).

- **The bottom end.** The block's `bore` and `cylinders`; the crankshaft's `stroke`; with the block's `deck_height`
  (crank centre to deck), the rods' `rod_length`, the pistons' `pin_height` (pin centre to crown) and `dome` (cc taken
  out of the chamber) and the head's `chamber` (cc), the volume left over the piston, and so the compression.
- **The heads.** Each head (each bank of a vee, the banks averaged; a limit, `max_rpm` or a fuel system's flow, is the
  tighter bank's, and a bank without one leaves the other's standing) breathes through its valves (`intake_valve`,
  `exhaust_valve` in mm, `intake_valves`, `exhaust_valves` per cylinder) opened by the camshaft's lift (`intake_lift`,
  `exhaust_lift`, mm) over the cylinder's volume, times the intake manifold's and the header's `flow` (0 to 1) and the
  head's own `intake_port`/`exhaust_port` (1 when left out). Worn valves pass a tenth less. `heat_loss` is what the
  head takes out of the burn.
- **The camshaft** (on the block's `camshaft` slot, or the head's for an overhead cam): `intake_duration`,
  `exhaust_duration` and `lobe_separation` in degrees, `advance` (degrees, held between `advance_min` and `advance_max`
  in `advance_step` steps).
- **The intake.** Every fuel system (`fuel_system`: a carburettor or an injection) on the manifold and what hangs on it
  gives its `mixture_ratio` (air to fuel by mass), the `fuel` it burns (`fuels.json`: `regular` when left out) and the
  most fuel it passes (`fuel_flow`, g/s); the air it takes in comes through its air cleaner (`air_cleaner`: its
  `air_flow` g/s, less as it clogs by `clogging`: 0 never clogs, 1 passes nothing worn out) or, without one, its open horn (its own `air_flow`). With
  several, the least of each counts. The fuel's best ratio, heat and burn time make the burn: off its best ratio the
  mixture burns cooler and slower, and it takes a static compression of 4 to 10 times its heat in MJ (about 6.8:1 to
  17:1 for pump gas at its best ratio).
- **Chargers.** A `supercharger`'s `boost` (bar) at its `normal_drive_ratio`, more or less as its `drive_ratio` is
  changed, best at `boost_rpm` over `boost_rpm_range`; a `turbocharger`'s `boost`, held by its `wastegate`.
- **Everything that turns.** The engine's top speed is the lowest `max_rpm` of its parts (a quarter over the block's
  `rev_limit` when none gives one); their `inertia` (kg m2) adds up; each part's `friction_loss` (the share it takes,
  0.02 for 2%) over the oil pan's `friction` is how hard the engine turns.
- **The block's own.** `idle_rpm`, `rev_limit`, and the spark: `spark_advance` (degrees before top dead centre) up to
  `spark_rpm_low`, moving to `spark_advance_high` by `spark_rpm_high`.
- **The gearbox** (`transmission`): `gears` (forward ratios, first gear first), `reverse`, `final_drive`, `drive`
  (`rear`, `front` or `all`), `diff_lock` (0 to 1). The clutch's `torque` (Nm) is what it holds.

The figures (`"figures"`) go over what the specs come to before the dyno runs, and the game's dyno makes the curve. The
player's tuning is kept by spec name (`"rev_limit"`, one gear as `"gears[1]"`) and goes over the part's own.

```json
{
  "id": "engines/l79/chevy_283_crankshaft", "display_name": "Chevy 283 stock crankshaft", "class": "crankshaft",
  "model": "chevy_283_crankshaft.kn5", "mass": 22,
  "specs": { "stroke": 76.2, "max_rpm": 8500, "inertia": 0.0261 },
  "slot_roles": { "connecting_rods": 40, "flywheel": 6 },
  "slots": [ { "id": 1, "name": "mount", "fits": [ "l79:chevrolet_265_small_block_3_75_in_bore/crankshaft" ] },
             { "id": 40, "name": "rods", "takes": [ "l79:chevy_283_crankshaft/connecting_rods" ] },
             { "id": 6, "name": "flywheel", "takes": [ "l79:chevy_283_crankshaft/flywheel" ] } ]
}
```

**The classes** (`part_classes.json`): each class with the one it is a kind of (`"disc_brake": "brake"`, `"brake":
"running_gear_part"`). A part of a class counts as one of every class above it, so a shelf or a work order kind that
names `brake` takes in disc and drum brakes. The engine reads `engine_block`, `cylinder_head`, `camshaft`, `crankshaft`,
`connecting_rods`, `pistons`, `flywheel`, `clutch`, `transmission`, `oil_pan`, `intake_manifold`, `fuel_system`,
`air_cleaner`, `supercharger`, `turbocharger` and `exhaust_header`; the running gear `tyre`, `rim`, `brake`,
`disc_brake`, `drum_brake`, `brake_shoes`, `wheel_cylinder`, `backing_plate`, `hub`, `spring`, `leaf_spring`,
`torsion_bar`, `traction_bar`, `shock`, `sway_bar`, `rear_axle`, `differential`, `ring_pinion` and `axle_shaft`. A mod
adds classes of its own (an `intercooler` under `engine_part`) or puts one under another parent; a class no file names
is a kind of nothing, and only its own name takes it in.

**The fuels** (`fuels.json`): `{ "fuels": { "methanol": { "heat": 1.4, "best_ratio": 16, "burn_time": 2.25,
"burn_time_off": 0.4 } } }`: what a kg of the mixture gives off burning (MJ), the air to fuel ratio it burns best at,
and how long it takes to burn there and how much longer at the worst ratio (ms). The base game has `regular`,
`premium`, `high_octane`, `methanol` and `diesel`; a mod adds fuels or changes one by its id, giving only the figures
it changes (`"methanol": { "heat": 1.5 }`).

**An engine of figures.** A part needs no specs to work in an engine: `"figures"` gives its engine figures in plain
metric units. An engine whose block has no specs is made of its parts' figures alone, whichever of its parts give them
(the block may give none); on an engine of specs, the figures go over what the specs worked out before the dyno runs, so
a part of figures (a turbo, a five-speed) also goes on any other engine. Either way the game's own dyno makes the curve from them,
so tuning works the same: a bigger cam, a header or a second carburettor changes the figures, and the dyno the power.

A number puts the part's own figure; `{ "add": n }` adds to the engine's and `{ "times": n }` multiplies it. Of the
same figure, the parts' numbers go first (the part furthest out from the block wins, whatever slot its branch is on;
parts as far out as each other by the slot they are on), then what is added, then what multiplies: a head gives
`"exhaust_flow": 0.35` and a header `"exhaust_flow": { "times": 1.15 }`. An engine the dyno cannot run (a rev range
of none, or past 30000 rpm) says why.

| Figure | Unit | What it is |
|---|---|---|
| `cylinders` | count | whole, 1 to 32 |
| `bore`, `stroke` | mm | |
| `compression` | ratio | 9.5 for 9.5:1; worked out last, from the bore and stroke the engine ends with |
| `chamber` | cc | the space left above a piston at top dead centre, instead of `compression` |
| `intake_flow`, `exhaust_flow` | 0 to 1 | how well the cylinders breathe in and out through their valves at full lift (the base game's V8s: 0.5 to 1 in, 0.2 to 0.45 out) |
| `intake_duration`, `exhaust_duration` | degrees | how long the cam holds each valve open |
| `spark_advance`, `spark_advance_high` | degrees before top dead centre | at `spark_rpm_low` and from `spark_rpm_high` on, rpm |
| `burn_time` | ms | how long the mixture takes to burn |
| `heat_loss` | % per second | heat lost to the cylinder walls |
| `mixture_ratio` | air to fuel by mass | 12.5 |
| `mixture_heat` | MJ per kg of mixture | 1.70 at 12.5:1 |
| `fuel_flow`, `air_flow` | g/s | the most the fuel system delivers, the most the air cleaner and intake pass; an engine of figures needs a `fuel_flow` |
| `rev_limit`, `max_rpm` | rpm | where the ignition cuts, and what the weakest rotating part survives (the curve goes that far; a quarter past `rev_limit` when no part gives it); above 0, up to 30000 |
| `idle_rpm` | rpm | |
| `inertia` | kg m2 | the engine spinning, flywheel and all, as Assetto Corsa takes it |
| `friction` | times a stock bottom end's | engine braking |
| `clutch_torque` | Nm | what the clutch holds |
| `gears` | a list of ratios | the forward gears, first gear first, 1 to 10 of them (numbers only) |
| `reverse`, `final_drive` | ratio | |
| `diff_lock` | 0 to 1 | the limited slip's lock; with a gearbox of figures and no part giving it, the car keeps its own differential |
| `turbo` | `{ "boost": bar, "spool_rpm": rpm, "full_boost_rpm": rpm, "lag_up": 0.992, "lag_down": 0.985 }` | a turbo, or a list of them; boost builds from `spool_rpm` (0 when left out) to all of it at `full_boost_rpm`, then holds; the lags are Assetto Corsa's (closer to 1, slower) |
| `blower` | `{ "boost": bar, "spool_rpm": rpm, "full_boost_rpm": rpm }` | a crank-driven charger: no lag, its boost stays in the power curve |
| `lag_up`, `lag_down` | 0 to 1 | the lag of a turbocharger of specs, on any part of its engine |

Chargers add up, as in Assetto Corsa: twin turbos side by side are two turbos of half the boost each, a sequential pair
a small one that spools early and a big one that comes in later. Each turbo of the engine is an Assetto Corsa turbo of
its own, with its own lag and gauge. An engine of figures starts from a small-block V8's breathing, cam, spark and
mixture, and from nothing for its cylinders, bore, stroke, compression and fuel: what it lacks to run is its problem,
in words ("nothing feeds the engine"), and the parts' `needs` hold them to their slots as on any part.
A figure that does not read (a name not in the table, a word for a number, `times` 0, a turbo without its `boost`) is
told in the log and left out.

A part of figures says what it is by its `"class"` (`engine_block`, `camshaft`, `turbocharger`): that puts it on its
shelf and in the work order like any other. On an engine of specs, a part the engine looks for on a slot (a fuel system
on the intake) works by its specs: give such a part the specs of a similar one and its own figures over them.
`tools\EngineBench <parts> show <build>` puts a build on the dyno.

```json
{
  "id": "engines/four/block", "display_name": "2.0 four block", "class": "engine_block", "mass": 120,
  "slots": [ { "id": 10, "takes": [ "four:head" ] } ],
  "figures": { "cylinders": 4, "bore": 86, "stroke": 86, "compression": 9.5, "rev_limit": 6500, "max_rpm": 7000,
               "idle_rpm": 850, "inertia": 0.12 }
},
{
  "id": "engines/four/big_turbo", "display_name": "big turbo", "class": "turbocharger",
  "slots": [ { "id": 1, "fits": [ "four:turbo" ] } ],
  "figures": { "turbo": { "boost": 0.5, "spool_rpm": 3500, "full_boost_rpm": 5500, "lag_up": 0.995 } }
}
```

**A part's name.** `"display_name"` is what the player reads in the shops, the junkyard, the workbench and the paper.
The base game names its parts as a 1960s American speed shop's counter would: maker, line, what it is, what it fits,
e.g. `Edelbrock Performer RPM camshaft (Chevy 327-396)`, `Holley 4-bbl air cleaner`, `Mopar 440 RB big-block left
exhaust header`. Carburettors as `4-bbl`/`2x4-bbl`, left/right and front/rear as plain words, a variant in brackets
(`(chrome)`), inches and lb/in for the running gear. No two parts share a name: parts alike but for their figures say
the figure, alike but for what they fit say that (`Edelbrock 5-blade fan (Ford V8)`), and alike in all the player can
see get a number (`(No. 2)`). The makers are the period's, named to say what a part is like; the names are their
owners' trademarks, and the game is not tied to any of them. A mod may name its own makers.

The game reads words in names (the cooling and the oil pans read the part's `name` and id with them), so a name
decides what a part does as much as its specs:

- a part whose class doesn't tell its kind gets it from its name (the work order's `"names"`, above);
- the cooling: `radiator` makes a radiator, `water` then `pump` (or `pomp`) a water pump, `fan` as a word a fan; but
  `hardware`, `bracket`, `pulley`, `gasket` or `bolt` anywhere makes the part none of them (`fan with pulley` is no
  fan). A radiator with `alumin`, `racing`, `race`, `champion`, `be cool` or `electric fan` carries off a third more
  (with `electric fan` it keeps up standing still); a water pump with `edelbrock`, `weiand`, `summit`, `high-flow`,
  `meziere`, `electric`, `march` or `paxton` moves a tenth more; a fan with `flex`, `7 blade`/`7-blade`,
  `seven blade`, `electric`, `high-performance`, `edelbrock`, `march` or `performance` cools better standing still.
  A part that is not a water pump must not say `water ... pump` (`timing cover with built-in pump`);
- an oil pan with `deep`, `milodon`, `moroso`, `canton`, `race`, `drag`, `hamburger` or `stef` holds its oil like a race
  sump;
- a block's name ends in `block`: the words before it are the engine's name on the car (`Chevy 327-396 V8 block` is a
  `Chevy 327-396 V8`), except after `small`, `big`, `bare` or `short`, which stay with it (`Chevy 350 small block`
  keeps its name). Its first make word (chevy, ford, mopar, hemi...) picks the family of its sound.

A mod can say it outright instead. On a kind in `work_order.json`, `"cooling"` is what every part of that kind does
for the engine's cooling: `"radiator"`, `"water_pump"`, `"fan"`, or `"none"` for a kind whose name only sounds like one
(a later mod's wins; any other value is reported and left out). A radiator kind that `"serves"` a kind whose cooling is
`"fan"` has its own electric fan. In a part's file, `"performance": true` (or `false`) says whether it is a
performance radiator, water pump or fan, or an oil pan that holds its oil like a race sump. Left out, the words above
decide. The base game declares neither.

```json
{ "kinds": [ { "id": "aux_cooler", "name": "auxiliary cooler", "names": [ "aux cooler" ], "cooling": "radiator" } ] }
```

The base game's reading of every part is held in `tests/StreetCorsa.Tests/Fixtures/part_roles.txt`: a rename that
changes what a part does fails `PartRolesSnapshotTests`.

**The running gear.** The rear axle's figures are its parts' `specs`: a housing (`"class": "rear_axle"`, `strength`
in Nm at the axle, `drag`), a carrier (`differential`: `diff_power`, `diff_coast`, `diff_preload` in Nm, `strength`), a
ring and pinion (`ring_pinion`: `ratio`, `strength`) and an axle shaft (`axle_shaft`: `strength`). The housing goes on the car's axle slot by its slot 1 and carries the carrier on its slot 2
and the shafts on 3 and 4; the carrier carries the gears on its slot 2. A live axle's housing also has the rear corners'
slots, by these ids: 5 and 6 the left and right brake, 7 and 8 the springs, 9 and 10 the shocks, 11 the rear sway bar (the
right side's brake and shock slots turned half round, `"rotation": [3.14159265, 0, 0]`; the game draws them at the
wheels whatever their positions). Only a car on a live axle (its data's rear suspension `AXLE`) carries them there, and
never its leaf springs (`leaf_spring`: the axle hangs on them, they stay on the car); a housing without them, or a
car with an independent rear, leaves the rear running gear on the car's own slots. Give them standard fittings of your own family
(`"takes": ["axle:mymod_banjo"]` on the housing, `"fits"` on the carrier) and `needs` for what the axle cannot do
without. `"came_in"` on a part says which cars it left the factory on, for the guess of a car's
factory running gear: `{ "families": ["gm"], "makes": ["pontiac"], "torque": [460, null], "years": [1965, null] }` (Nm of
the car's peak torque, model years; any may be left out, `{}` is any car's). A part without `came_in` (a spool, an alloy
shaft) is never a factory part while there is one with it. For brakes and springs also `"countries"` (makes.json's: the
base game's drums `["USA"]`), `"mass"` (the car's kg, `[1600, 2100]`), `"axle"` (`"front"` or `"rear"`), `"suspension"`
(the axle's suspensions.ini `TYPE`s, `["AXLE"]` for a leaf spring) and `"option": true` (a dealer's option, only for a car
cars.json picks it for). A corner part no `came_in` names a car for leaves that car on the nearest plain part (one with
no `came_in` that is not `aftermarket`).

A **drum brake** (`drum_brake`) has a brake's figures (`force`, `friction`, `radius`, `calipers`) and how it heats: `torque_k` (°C per kJ of braking), `cool_transfer`, `cool_speed_factor`
(Assetto Corsa's brakes.ini `[TEMPS_*]`); its shoes (`brake_shoes`, on its slot 2 by `shoes:drum`) a `friction` that
multiplies the drum's and a `perf_curve`, an inline lut of °C to torque (`"(|0=0.9|230=1.0|380=0.55|)"`), and its wheel
cylinder (`wheel_cylinder`, slot 3, `wcyl:drum`) a `force_factor`. A disc that fades says it all itself.

A **leaf spring** (`leaf_spring`) has its rate in `force` (N/m at the wheel) and a `wrap` (how much it winds
up under the axle's torque: the base game's multi-leaf 1); a **traction bar** (`traction_bar`, on the leaf's slot 2 by
`traction:leaf`) a `wrap_factor`. A **torsion bar** is a `torsion_bar`. A **sway bar** (`sway_bar`, on the car's own
slots 402 front and 403 rear) gives its `arb_rate` (N/m, as suspensions.ini `[ARB]` counts it); one without does
nothing and is not sold. Any running gear part can say the axle it goes on, `"fits_axle": "front"` or `"rear"` (the
workbench offers it only there), and the suspension it needs, `"fits_suspension": "AXLE"` (the axle's suspensions.ini
`TYPE`: a car whose data is otherwise cannot race on it). A spring, shock or bar with `"aftermarket": true` is never a
car's factory one. A tyre gives `tyre_width` (mm), `radius` (m), `wheel_radius` (mm, its rim), `friction`,
`load_capacity`, `rolling_resistance`, `optimal_inflation`, `min_rim_width` and `max_rim_width`; a rim `wheel_radius`
(m), `rim_width`, `offset` and `rim_type`; a coil `spring` its `force` and `design_load` (kg on the wheel); a `shock`
its `damping` and `rebound_factor`.

```json
{ "id": "running_gear/banjo/housing", "display_name": "Banjo rear axle housing", "class": "rear_axle", "mass": 60,
  "came_in": { "makes": [ "mymake" ] }, "specs": { "strength": 3500, "drag": 0.012 },
  "slots": [ { "id": 1, "name": "mount" }, { "id": 2, "name": "carrier", "takes": [ "axle:banjo" ] },
             { "id": 3, "name": "left shaft", "takes": [ "shaft:banjo" ] }, { "id": 4, "name": "right shaft", "takes": [ "shaft:banjo" ] } ],
  "needs": [ { "slot": 2, "message": "The rear axle needs its differential carrier." } ] }
```

**Fittings.** A part mounts by its fittings alone, never by naming another part: a slot `takes` fittings (what goes on
it) and `fits` fittings (what it goes on), and a part goes on another when a slot of its own fits what a slot of the
other takes, whatever mods they are from. One way only: the part that goes on is the one that fits, never the one that
takes. A fitting is any word; two kinds are in use:

- the standard ones of a kind of part, which any make shares: `carb:4bbl`, `carb:2bbl`, `air:single`, `air:2x4`,
  `blower:roots`, `axle:gm_12bolt`, `gears:ford_9`, `hub:front`... (`exhaust:tip` is what the base game's mufflers fit:
  no slot of the base game takes it yet, so they go on a mod's exhaust pipe that does);
- an engine's own, named `<mod>:<a part that takes it>/<what goes there>` (the base game's by its folder instead of the
  mod: `generic:...`, `stock:...`): `gm_v8:chevy_327_396_v8_block/cylinder_head` is taken by the heads' pads of the
  327/396 blocks of the `gm_v8` mod, `ford_six:ford_188_falcon_six_block/camshaft` by the Falcon six's cam bore.

To make a part for an engine of another mod, look up what that engine's slot takes in its part file and fit that: a
camshaft for every small-block Chevy fits the fittings the small-blocks' cam slots take, and is offered on any of them
the player owns, with or without your mod knowing theirs. A slot may fit and take several fittings. Fittings match
whatever their case.

**New parts.** A `.json` for each part anywhere under the mod's `parts` (`{ "id": "engines/mymake/block", "make": "gm",
"class": "engine_block", "model": "block.kn5", ... }`) with its KN5 model next to it. Give each part a `class`, its
`specs`, the `slot_roles` its class works with and its slots' fittings; list complete engines in the mod's
`engine_builds.json` (or its mod.json's `builds`); and check them on the dyno with
`tools\EngineBench <Content folder> show <build id>` (`all` lists every build, `rated` holds the rated ones to their
power). The base game's parts are the examples: a part of the same kind is the one to copy. A single part can be a mod
of its own: a `mod.json`, its `.json` and its `.kn5`.

### cars

The makes and the cars: `.json` files in a mod's `cars` folder (the base game's `makes.json` and `cars.json`), and its
mod.json's `cars` and `makes` after them, merged **key by key** over the mods before them like the rules: a mod adds a make or a car, or changes one figure of one,
naming only that; a list (a make's words) is taken whole. What does not read is told and left out.

- `makes`, by id: `words` that tell the make in a car's brand, else in its name (any case; two words allowed,
  `"alfa romeo"`; a word is one make's); `family`, the corporate family its cars and engines belong to (`gm`: a Pontiac
  turns up with a Chevrolet V8, never a Hemi; the engine builds' and sounds' families come from the same words), none
  for a make whose words name no engine; `country`, a country's id or name; `common`, added to how common its cars
  are (0 rare to 1 common). A badge does nothing to a car's price.
- `countries`, by id: `names` a `ui_car.json` writes for it (`"Great Britain"` is the `UK`) and `regions` it is in
  (`"Europe"`); a region is a country entry too, with its names (`"European"`).
- `words`: `aliases` (a nickname read as the word it stands for, `"vette": "corvette"`) and `noise` (words that say
  nothing about which car or engine it is), for matching a car's name to an engine build's.
- `cars`, by the AC car's folder name: what is given here is not worked out from the car's data. `engine`, the
  engine build it leaves the factory with (`engine_builds.json`, namespaced); `layout`, `front`, `mid`, `rear`, or `front_transverse` and `mid_transverse` for an engine lying across the car (guessed for a front-wheel-drive car; the garage draws it so);
  `common`, how often it turns up for sale, 0 to 1; `make`, a make of `makes`; `running_gear`, the factory rear axle as
  far as it is given: `axle` (a housing's part id), `diff` (a carrier's), `ratio` (the gear set of that ratio that goes on
  the carrier, the nearest there is), `shafts`, `brakes_front`, `brakes_rear`, `springs_front`, `springs_rear` (part ids); what
is left out is guessed from the parts' `"came_in"` (see `parts`). The base game's holds the factory
  engines set by hand in the catalog editor (a developer's screen, which saves into the base game's `cars.json`).
  A car's price is never given: it costs its body by its weight (the rules' `cars`) and its factory parts (the
  shelves' `price`). A `price` a mod still gives is told as no longer read.

```json
{
  "makes": { "datsun": { "family": "nissan", "country": "Japan", "words": [ "datsun" ] } },
  "cars": { "ks_nissan_skyline_r34": { "engine": "mymod:engines/rb26/stock", "common": 0.3 } }
}
```

### sounds

One folder per engine sound (a `.bank`, `GUIDs.txt`, an optional `sound.json`), as the base game's `sounds` (see its
README). The sound's id is its folder's name, namespaced. `sounds.json` (or mod.json's `sounds`) pins blocks to sounds:
`{ "pins": { "engines/mine/block": "my_v8" } }` (the block and the sound are references: the mod's own first); its
`cars` facts are about installed AC cars, by car id. A pin to `car:<AC car id>` takes that installed car's bank, which
only a player with that car has: a mod that ships its blocks' sounds carries the banks in its own `sounds` folder.

### sfx

The game's sound effects, by cue name: `*.json` files of `{ "cues": { "<name>": { ... } } }`, each cue a `bus`
(`interface`, `effects`, `ambience`, `radio`), its `files` (relative to the json; one is picked at random, never the
same twice in a row), and optionally `volume` (0..2), `pitch` (how far each play's pitch may wander, 0.05 = 5%),
`loop` (a place's air) and `cooldown` (seconds before it plays again). A mod's cue of a name the base game has replaces
it; the base game's cues are in `streetcorsa\sfx\sfx.json` (`ui.click`, `part.ratchet`, `ambience.garage`, ...).
A cue marked `"optional": true` may name files that are not there: it is then silent, and no problem. `music.intro`
(`streetcorsa\sfx\music.json`, a loop on the radio's bus) is the music the game starts to: it plays over the intro and
the main screen until the player is in a game, puts the radio on or opens the sound library, once a session.
`.ogg`, `.wav`, `.mp3` and `.flac` play. A button can name a cue of its own (`audio:Sfx.Click="ui.lights"`).

### props

Things set into a garage, one folder each: a model (KN5) and a `prop.json` naming it and, by garage (the showroom's
folder name), where it stands and which way it faces: `{ "name": "Radio", "model": "radio.kn5", "places": { "garage":
{ "position": [2.62, 1.29, -8.6], "heading": 67 } }, "credit": "..." }` (metres, the car at the origin; degrees about the vertical). A
mod's prop of an id the base game has replaces it, so a mod can swap the radio for another set or put it elsewhere in
its own garage; one without a place in a garage takes it out of that garage. `credit` names the model's maker where
the game shows its credits (Settings), as a CC BY licence asks.

**The garage's dressing.** Every prop with a place in the garage but the four the game draws in places of its own
(`radio`, `workbench`, `lift`, `engine_stand`) is part of its dressing, drawn where its places say: a garage is
furnished by its props' places, and a mod adds a can of oil to it with a folder and a line. A place may be a list, for a
prop that is there more than once: `"workshop": [ { "position": [-4.45, 0, -1.9], "tilt": 90 }, { "position": [-4.45,
0.17, -1.9], "heading": 55, "tilt": 90 } ]` (a stack of tyres). `tilt` tips the model forward about its own x and `roll`
over to its side about its own z, in degrees, before the heading turns it: a tyre laid flat is `"tilt": 90`, a ladder
leant back against a wall `"tilt": -12`. The game stands a tipped prop back on what is now its base, the middle of its
footprint over the position, its bottom at the position's height. A prop stands on the floor (y 0) or on what is under
it (a bench top at 0.9: y 0.9); nothing stops two props standing in each other, so a prop is placed by its looks. The
base game's garage (`workshop`) has some fifty: Poly Haven models (CC0) and Sketchfab ones (CC BY 4.0), made by
`tools\GarageScene\build_props.py`. A prop whose `credit` does not say CC0 is named in the game's credits (Settings), as
a CC BY licence asks.

The published `bushradio` mod has `radio`, the set on the desk: clicking it opens the radio. The base game's `workbench` is where a loose part's "Work on it" job is shown; it names
no model and the game draws its own (a mod's `workbench` may name one, its top 0.9 m up, its front +Z); a garage it has no place in
has no bench, and a loose part's job is shown there without the part. The base game's `lift` is the in-ground post the
car in the bay goes up on: it names no model, and the game draws its own from boxes (a tread plate in the floor, the
saddle on its piston rising out of it). A mod's `lift` may name a model: its rising part (a saddle, or a two-post lift's
carriages and arms) is a node named `lift_post`, the top of it (where the car rests) at the node's origin, and the node
where it is with the lift down. Going up, the saddle rises to the car's body first (its underside over the pads,
measured from the car's model), then carries it. Its place gives the floor's height (the lift stands under the middle of whatever car
is in the bay); a garage it has no place in still lifts the car, with no lift drawn. The base game's `engine_stand` is
where an engine is worked on off the car (the parts view's Stand). Its model is laid out in the stand's own space: the
spindle along Z at 0.78 m over the middle of the floor, the face the block bolts to at Z 0, the legs forward (+Z) under
the engine; the head that turns with the engine is its node `engine_stand_head`, the rest at its root. With no model (or
one without that head) the game draws its own from boxes. Its place is the middle of its spindle over the floor,
`[x, y, z]` with y the floor's height; it always faces the way the car does, whatever its heading, and slides along Z to
the engine on it. A garage it has no place in has it 3 m to the car's left, `[3, 0, 0.3]`. `tools\Props` makes a prop's KN5: `prepare_prop.py` takes any model with PBR
materials (a Sketchfab .glb, a .blend) through Blender, `PropBuilder` writes the KN5 (`--keep-origin` for a part's model,
whose origin is where it mounts: see docs/systems/parts-system.md "The running gear's models" for the spaces; `--node
name=folder` for a moving part, a node of its own). `tools\GarageScene\compose_props.py` readies a downloaded model
first: what is no part of it taken out, a pose held, scaled and turned into the game's space, its moving parts split
off, brought down to a triangle budget (a garage prop is seen across a room).
`tools\RunningGearParts` makes the base game's running gear models out of shapes.

**An actor** is a prop that moves: a figure with a skin on bones (AC's skinned meshes, as a driver's model) and an
animation that moves the bones round and round, `{ "name": "Attendant", "model": "attendant.kn5", "animation":
"talk.ksanim", "seconds": 24.8, "credit": "..." }` (`seconds`: one turn of the animation; a model or animation named
outside the folder is not read, one that is missing or asks for 0 seconds stands still). A scene names the actor it
wants by id and says where he stands (a station's `attendant`); an actor has no place in a garage. The base game's
`attendant` is made by `tools\StationScene\build_station.py` from a Sketchfab model: `tools\Props\prepare_actor.py`
exports the skin, the bones and the animations from Blender (a rig of more than the 64 bones AC's shader takes loses
its least-weighted leaf bones to their parents), `prepare_prop.py` the pictures, and `ActorBuilder` writes the KN5
(materials `ksSkinnedMesh`, the bones dummies of the model) and a `.ksanim` an animation.

### dealers

Lists of `dealers`, of `showrooms` and of `maps`:

```json
{
  "dealers": [
    { "id": "valley", "name": "Valley Cars", "region": "The Valley", "blurb": "…", "mapX": 0.41, "mapY": 0.22,
      "showroomId": "industrial", "priceBandLow": 0.2, "priceBandHigh": 0.7, "conditionCenter": 0.6,
      "travelHours": 2, "stockLow": 6, "stockHigh": 9 }
  ],
  "showrooms": [ { "id": "industrial", "floorWidth": 20, "floorDepth": 14, "wallRadius": 10, "capacity": 10, "indoor": true } ]
}
```

Showrooms and maps are only read from a `showrooms` or `maps` list; a file that is one entry, or a bare list, is
dealers. `"startsOpen": false` keeps a dealer out of business when a game starts: it opens some day, when the market has
room for another lot (about 60% of all the dealers are open at a time, and now and then one goes out of business).
`mapX`/`mapY` are fractions of the dealer map picture (see the base game's `dealers.json` for how they are measured).

`"cars"` is the kind of car the lot deals in, a car filter written as an event's entry requirements (an import lot:
`{ "type": "origin", "origin": "European" }`); its price band is then of the cars of that kind, and with none of
that kind installed it deals in any. Left out: any car. A `value` filter weighs each car at what the lot asks for it,
a `power` filter at the power it is quoted at. Trade-ins go only to a lot that deals in their kind. A filter that
does not read is told, and the lot deals in any car.

A dealer's `salesman` is who the player talks to on the lot, when their AI is on: `{ "name": "Cal Whitaker", "age": 45,
"sex": "man", "temperament": "jive_talker", "portrait": "salesmen/downtown_motors.png" }`. The temperament is a key of
`ai/people.json` (how he talks and how far he comes down), the portrait a picture under the mod's `dealers` folder,
shown at the top of the talk. Leave any of it out and the game makes it up (a dealer with no salesman gets one the first
time he is talked to, with no portrait). The base game's are painted ahead with ComfyUI by
`tools/SalesmanPortraits/paint_salesmen.py`, which a mod can run on its own dealers' file too.

The map the pins stand on is a `maps` entry: `{ "maps": [ { "id": "city", "image": "city.jpg", "anchorX": 0.05,
"anchorY": 0.28, "anchorWidth": 0.62, "anchorHeight": 0.55 } ] }`, the picture next to the file. The anchor is the part
that must be on screen, as fractions of the picture; the screen grows out of it to the window's shape. The last mod's
map is the one shown, so a mod with its own map re-reads every pin off it, the base game's too (`"replaces"`).
`showroomId` is a room by its folder name: a mod's `garages` folder, else an AC showroom. A save that never had a
dealer gets its lot stocked the first time the map is opened; the other lots stay as they are.

### partshops

A list of `shops` (or a file that is one shop, or a bare list), pinned on the part shops' own map (the garage's
Parts dealers button). It is drawn on the dealers' map picture, without the car lots:

```json
{
  "shops": [
    { "id": "valley_speed", "name": "Valley Speed", "region": "The Valley", "blurb": "…", "kind": "speed",
      "mapX": 0.41, "mapY": 0.25, "travelHours": 1.5 },
    { "id": "stovebolt", "name": "Stovebolt Supply", "region": "Downtown", "blurb": "…", "kind": "specialist",
      "makes": [ "gm" ], "mapX": 0.3, "mapY": 0.5, "travelHours": 1, "startsOpen": false }
  ]
}
```

`kind` is `specialist` (new parts whose `"make"` is one of its `makes`, a mod's too), `speed` (new
performance parts for any make, or its `makes`: carburettors, blowers, cams, manifolds, heads, headers, nitrous, none that
a car came with), `wheels` (new tyres, rims, brakes, springs and shocks) or `junkyard` (used parts and whole engines;
buys the player's parts for a little more than a trade-in, and can be haggled with, though never down to less than
10% over what it would pay for the part). `groups` narrows a shop to kinds of part, by
the ids of part_groups.json (`"camshafts"`; see "parts"). `priceFactor`, `buyFactor`, `stockLow`/`stockHigh` and `restockDays` are optional
(the kind's own otherwise; see `PartShopRules`). `"startsOpen": false` keeps a shop out of business when a game starts:
the shops come and go as the dealers do. Keep a pin about 0.055 across or 0.035 down from every other shop's pin, or the
name plates overlap; the dealers' pins are on their own map. `kind` is one of the five names; anything else (a number
too) makes a specialist. A make with no specialist of its own is still sold by the speed shops (its hot
parts), the junkyards and the mail order.

`machine` is a machine shop: it sells and buys no parts. The player brings it parts off the garage's shelf or the
engine off its stand, and has the machine jobs of `rebuilds.json` done on them and the parts on them, paid as they are
left; the shop brings them back on the morning they are ready (an engine back on the stand if it is free). Its
`priceFactor` is against the jobs' prices. Its machinist talks while the AI is on (`chat_machinist`, `machinist_search`;
`"clerk"` as above), and it is seen in the shop scene for its kind.

Inside, a shop is the part shops' room (`shopscenes`, below) dressed in the look for its kind, or the one it names in
`"look"` (a look's id). Whoever is behind the counter, talked to while the player's AI is on, is drawn from the
`names.clerks.*` pools with a temperament `for` `clerk` (ai/people.json), unless the shop gives `"clerk"` as a dealer
gives its salesman: `{ "name": "Big Bob Vance", "age": 58, "sex": "man", "temperament": "old_mechanic", "portrait":
"clerks/bob.png" }` (the portrait under the mod's `partshops` folder; each field optional).

### racers

A list under `opponents`, or one racer per file (its id as `opponentId` or `id`):

```json
{ "opponentId": "vera", "name": "Vera Holt", "nickname": "Hurricane", "age": 29, "gender": "Female",
  "skill": 95, "aggression": 80, "portraitPath": "vera.png", "location": "The docks", "biography": "…" }
```

`portraitPath` is next to the file. `skill` is 90 to 100. New racers join games already under way. `"cars"` is the
kind of car the racer drives, a car filter as an event's entry requirements (`{ "type": "origin", "origin":
"European" }`): he starts with one, and buys one, when there is one to be had (with none for sale, one without a car
of his kind that runs takes what he can get). A car for sale is weighed at what it is offered for and the power it
is quoted at. A filter that does not read is told, and he drives any car.

**A racer's own lines.** Any racer says lines of his own where a mod adds a pool's key with his racer id after it
(`talk.pulled_up.gino`, `talk.pulled_up.aggressive.gino`, `replies.accept.gino`, `replies.decline.gino`,
`street.back_on_street.gino`; `.my_mod:gino` from another mod); otherwise he says what every racer says. The keys are
the `talk.pulled_up*`, `replies.*` and `street.*` pools of the base game's `talk.json` and `street.json`.

A **boss** is a racer with a `"boss"` block: he only races for pink slips, stays out of sight until his victory path
opens (`path`, his mod's own path first, then the base game's; none: out from the start; one no installed mod has, or
a boss whose mod is gone: never out), never races where nobody watches and never leaves town. His money and record are
his own where he names them, the rules' `rivals.king` where he does not (`"boss": true` is a boss with neither). A
boss block that does not read leaves the racer out, and the game says so at startup:

```json
{ "opponentId": "big_jim", "name": "Big Jim", "…": "…",
  "boss": { "path": "Mopar", "money": 15000, "carChoices": 3, "races": 40, "wins": 35, "pinkSlipsWon": 8 } }
```

The base game's King is `"boss": { "path": "King" }` (`"isKing": true`, from before bosses, still means that); to
replace him, give yours `"replaces": "king"`. A path's `beatBoss` step is won by beating every boss whose path it is.
A racer a mod update makes a boss becomes one in games under way.
A boss has lines of his own where a mod adds the key with his racer id after it (`talk.pulled_up.king.big_jim`, or
`talk.pulled_up.king.my_mod:big_jim` from another mod); otherwise he says the King's: `talk.pulled_up.king`,
`replies.king.pink_slips`, `replies.king.no_pink_slip`, `street.king_out`, `chat.fact.rival_king`, and the paper's
`news.king_beaten.<id>.headline`/`.body` and `news.king_won.<id>.*`. The base game's lines name the King, so a mod's
boss should bring his own.

### events

The paper's invitations:

```json
{ "id": "mopar_night", "name": "Mopar Night", "description": "…", "format": "drag",
  "entryRequirements": { "type": "any", "filters": [ { "type": "brand", "brand": "Dodge" }, { "type": "brand", "brand": "Plymouth" } ] },
  "minReputation": 20, "requiredMilestones": [ "street_cred" ], "isPinkSlip": false, "isBracket": false,
  "reward": { "cash": 800, "reputation": 8, "specialItem": "part:engines/mine/cam", "specialItemDescription": "A hot cam" },
  "schedule": "Weekly" }
```

- `format`: a race format's id ([formats](#formats): `drag`, `circuit`, `sprint`, `time_attack`, or the mod's own
  first). `raceType` (`DragRace`, `Circuit`, `Sprint`) is read as before formats; neither: a drag race. A format no
  mod has leaves the event out. `laps`: the race's own laps (1 to 50), left out for the format's; a drag race is
  always one run. An event of more than one lap is only raced on a layout that comes back round (see `laps` below).
- `schedule`: `OneTime`, `Daily`, `Weekly`, `Permanent`.
- Car filters (`type`): `brand`, `origin` (a country, or American, European, Japanese...), `years` (`startYear`,
  `endYear`), `power` (`minHP`, `maxHP`), `value` (`minValue`, `maxValue`: what the car is worth), `all` and `any`
  (of `filters`). Left out: any car.
- `specialItem`: `rare_camshaft` (the best camshaft that fits the winner's engine) or `part:<part id>` (the mod's own
  part first).

### formats

The kinds of race (`formats/formats.json`, `{ "formats": [ ... ] }`), merged by id. The base game's:

| id | family | module | laps | won on |
|---|---|---|---|---|
| `drag` | drag | `drag` (the strip: timeslips, lanes, bracket races, test-and-tune) | 1 | the order at the line, or the bracket's rules |
| `circuit` | road | `road` | 1 | the order at the line |
| `sprint` | road | `road` | 1 | the order at the line (not offered at the diner) |
| `time_attack` | road | `time_attack` (ghosts: a warm-up lap, then flying laps) | 3 | the best lap, lower wins |

```json
{ "id": "drift", "family": "road", "module": "drift", "laps": 2, "session": "Drift",
  "win": "score", "better": "higher", "scoreShows": "points", "name": "paper.race_type.drift",
  "diner": true, "street": false, "police": false }
```

- `family`: `drag` (raced on the strips; counts as a drag race for the record, the wagers, `DragWins` and the street's
  drag share) or `road` (every other track). The family is what the career knows of a format; the rest is the race's.
- `module`: the Lua module the race mode runs it with. One of the mode's own (`drag`, `road`, `time_attack`), or the
  mod's `formats/<module>.lua`, which the game puts into the mode's folder in AC as `format_<mod>-<module>.lua` (the
  `-` is in no module or mode name, so no two mods' modules, nor the mode's own, ever share a file).
- `mode`: a whole CSP mode instead, the mod's `formats/<mode>/` folder with its `mode.lua` (and `manifest.ini`), put
  into AC as `extension/lua/new-modes/<mod>-<mode>` and selected for the race. It reads race.ini and writes its result
  as sc_race does (below), with `metadata.source` its folder name in AC. The game puts sc_race's `car_parts.lua` and
  the race's part models (`parts\`) in its folder too: `require('car_parts').update()` in `script.prepare` and
  `script.update`, as sc_race does, shows the cars' rims, tyres and engine parts that stand out (without it the cars
  race as their models are).
- `laps`: what the race is over (1 to 50; a drag race 1); an event may set its own. `sessionLaps`: the laps AC's
  session is given, when the format runs past its laps (a time attack's warm-up lap and its wait for the rival: 99),
  so AC never ends the race before the mode does. A race of more than one lap of AC's is never offered or picked on a
  layout raced from A to B: one whose `ui_track.json` (the layout's, else the track's) says `point to point`, `A to
  B`, `p2p`, `hillclimb` or `touge` in `run` or `tags`.
- `session`: the session's name in AC. `police`: the police may come. `bracket`: it can be a bracket race (drag only).
- `win`: `position` (the order at the line) or `score` (each racer's `score` in the result; `better`: `lower` or
  `higher` wins; `scoreShows`: `time` for milliseconds shown as a lap time, else `points`). A racer without a score
  loses, and a rival that crashes or breaks down keeps the score it set before; the same score both ways goes by the
  places the mode gives. The career hands out a card with both scores, and says `result.score.won.<id>`/
  `result.score.lost.<id>` when the mod has them (else `result.score.won`/`.lost`).
- `name`: its name's text key; left out, `paper.race_type.<id>`. `diner.format.about.<id>`, when there, is what the
  diner says of it. A format's own keys go by its full id (`paper.race_type.jdm:drift`), else by the id as the mod
  wrote it (`paper.race_type.drift`) when no other format has that name, so two mods' `drift` never share words.
- `diner`: offered at the diner on its family's tracks (the first of a family is the one picked). `street`: raced on
  the street; the first street format of each family is the one the cruise's rivals ask for.
- `aliases`: ids it was known by (the base game's are its old race types, `DragRace`, `Circuit`, `Sprint`, which saves
  and events from before formats name).

A race saved with a format no mod has any more is run and counted as a road race.

**A format module.** The race mode (`apps/new-modes/sc_race/mode.lua`) runs what every race has: the start, false
starts, crashes, breakdowns, the engine's heat and oil, the police, the result file and the result card. A module is a
Lua file that returns a table of hooks, every one optional; `format_road.lua` lists them all and is the one to copy.
A hook that fails is told in the log once and the race goes on as if the module had none.
The hooks get the core (`SC`): the cars' data, the race clock, the laps, the race's words (`SC.say(key, english,
values)`: `race.<key>` in the mods' `text/race.json`, as `<KEY>` in capitals), `endSession`, and more. The mode's
harness (`tools/sc_race_harness`) runs the mode and its modules on LuaJIT without AC; `test_time_attack.py` is the
example to copy for a format's own tests.

**What race.ini tells the mode** (`[STREET_CORSA]`): `FORMAT` (the id), `MODULE` (`format_<module>`), `LAPS`,
`SCORE` (`LOWER`/`HIGHER`, a format won on score), `RACE_TYPE` (`DRAG`, `ROAD`, `TUNE`: the family, and test-and-tune),
`CONTEXT_ID`, each car's start (`CAR_<n>_*`), the police (`POLICE*`), a bracket race's `DIAL_IN`, and
`[STREET_CORSA_TEXT]` the words.

**The result** (schema 1.8), written to `Documents\Assetto Corsa\out\sc_race\<session>.json` (as `.tmp`, then
renamed): `metadata` (`schema_version`, `source`), `session` (`context_id` from race.ini, `end_reason`, `race_type`,
`format`), `participants` (the player, `car_index` 0 and `is_player`, and the rival: `performance.final_position`,
`crash`, `breakdown`, `condition`, and a format won on score its `score`). See
`tests/StreetCorsa.Tests/Fixtures/sc_race_time_attack_result.json`.

### milestones

```json
{ "id": "mopar_man", "name": "Mopar Man", "description": "Win 5 drag races", "trigger": "DragWins",
  "targetValue": 5, "category": "Racing", "unlocks": [ "victory:MyPath" ] }
```

`trigger`: `TotalWins`, `DragWins`, `RoadWins`, `PinkSlipWins`, `ReputationReached`, `MoneyEarned`, `CarsOwned`,
`DaysPlayed`, `OpponentsDefeated`. `unlocks` is what the player is told; a path opens by its own steps.

### victories

Ways to win the game. A path opens once every `unlock` step is done (none: open from the start) and is won once every
`goal` step is done too:

```json
{ "id": "MyPath", "name": "Mopar King", "description": "…",
  "unlock": [ { "type": "counter", "counter": "DragWins", "atLeast": 5, "step": "Win {target} drag races",
                "todo": "Win {target} drag races ({current}/{target})" } ],
  "goal": [ { "type": "beatBoss", "step": "Beat the King" } ],
  "progress": { "achieved": "Done!", "unlocked": "The King is waiting", "locked": "{DragWins}/5 drag wins" } }
```

Step types: `counter` (a trigger, as in milestones, at `atLeast`), `beatBoss` (every boss whose path this is beaten,
or with `"boss"`, a racer id, that one: the mod's own racer first, then the base game's; `beatKing` is the same),
`beatAll` (every racer beaten once; a boss still out of sight is not asked for), `mostWins` (more
wins than any racer but the bosses). `{target}`, `{current}`, `{remaining}` fill a step's text;
counters by name (`{TotalWins}`), `{defeated}` and `{total}` fill both steps and progress lines, with a format if
wanted (`{MoneyEarned:N0}`). A goal step is never done while the path is locked. The progress shown is the share of all
steps done, a counter counting by how far along it is. Path ids are kept in saves.

### streets

One folder per street, as the base game's `streets\mainstreet`: a `street.json` and its model, either one for every light
of the day (`"model": "street.kn5"`, with a sky dome for each named `STREET_SKY_DAY`, `_DUSK` and `_NIGHT`; the
ground's height next to it as `ground.bin`) or one per light (`street_day.kn5`, `street_dusk.kn5`, `street_night.kn5`,
as `tools/StreetScene` makes from a panorama). `"times": ["night"]` says when the street is used (left out: every light
it has a model for). `"glow"` makes the street's self-lit materials glow by light of the day (`"night": { "sign_roof":
1.1 }`, 0 to 4). The Cruise screen picks a street at random among those that fit the hour, and keeps it while it still
fits. When no street fits the hour, the one in view stays, in the nearest light it has.

A street may have **a diner** on it: a `diner.json` next to its `street.json`, as the base game's Main Street. It gives
the diner's ways (`arrive`: down the street and into the player's bay, the car stopping at the last point; `backOut`:
out of the bay backwards; `leave`: from there off down the street), as `[x, z]` points in metres, all three needed; where the driver
looks in the bay (`lookWhileParked`, degrees to the left; `lookUpWhileParked`, degrees up, −15 to 15); and the `bays`
the racers' cars are parked in (`x`, `z`, `heading` in degrees, 0 facing +z), in the order they fill. What it leaves out
(`model`, `sun`, `lights`, `glow`, `credit`) is the street's. Which diner stands on which street is the diners' list
(see "diners"); with no list, every such street is a diner of the first level, and a mod's street with a diner on it
that no diner names is where the base game's beginners' diner is seen (in the street's own look). `tools/DinerScene` builds the base
game's (see [screens/diner.md](screens/diner.md)).

A street's model may hold **look groups**: nodes named `LOOK_<GROUP>...` that are shown only when the scene names the
group, and hidden else, so one model wears a different look for each diner on it. `"looks": ["mygroup"]` in street.json
names the groups the street shows on the Cruise screen and at a seller's curb; a diner names its own (see "diners",
`look.show`), and the Cruise screen keeps the look of the diner the player drove out of when it is that diner's street.
A group is the nodes named `LOOK_<GROUP>` or `LOOK_<GROUP>_...`, so `star` is not `starlite`. A model with no such node
is as it was. The base game's street has none: its diners' looks are pieces of the kit laid on it (`look.layout`).

A street may be **laid out from a kit** (step 39): its model is then only the stage (the road, the sidewalks, a diner's
lot and the diner, what the ways the cars drive and the ground depend on), and a `"layout"` in its street.json is a
recipe of slots the pieces of the street kit (see "streetkit") are put in. Each save lays the street out once, the first
time it is seen, from a seed drawn when the career started and the street's id, and keeps it: the same street for the
whole career, nothing moved by a mod's pieces updated later; a save from before a street had a layout gets one from its
name. A street whose recipe has changed since (the `layout` itself, spacing aside: a street rebuilt, a recipe fixed) is
laid out again from the same seed, and so is one laid out while no mod had any of its pieces. A piece no mod has any
more is left out of the street and kept in the save for when its mod is back. A layout no screen lays out any more (a
street or diner gone with its mod, the street's own once each diner seen on it lays its own) is taken out of the save
when it loads: drawn from the same seed, it comes back as it was with its mod. The street stands round the car at a
seller's curb too. The recipe:

- `rows`: a frontage filled side by side, `from` `[x, z]` towards `to`, each piece's front on the line facing `heading`
  (degrees, 0 facing +z), or its middle on it with `"align": "middle"` (a car at the curb); `gap` `[min, max]` metres
  between one and the next, drawn; `first`, what the first slots take in turn (a corner shop), then `take` for the rest.
  A slot left empty leaves its piece's width vacant (a vacant lot, a gap at the curb).
- `runs`: a piece every `every` metres from `from` to `to`, facing `heading` (lamp posts, palms, poles, meters); `nudge`
  metres further on, once, when its spot is taken (a lamp post off a driveway).
- `spots`: one piece `at` `[x, z]`, facing `heading` (a signal, a sign, a hydrant); put where it is said to go.
- `clear`: boxes (`"rect": [x0, z0, x1, z1]`) nothing goes into, or only what has one of the tags in `only` (the curb at
  the player's spot, the ways the car comes in and goes, a crossing). The recipe keeps the drive-in's ways clear itself.
- Every slot (rows', runs' and spots'): `take`, piece ids and tags in one list (a word takes the piece of that id and
  every piece with that tag; a word that is neither is told among the mods' problems); `far` and `near` (with `nearAt`
  `[x, z]`): what it takes from `near` metres off (a palm's card past 45 m of the player's car; a row's slot by where it
  starts);
  `empty` (the chance it stays empty, 0 to 1); `y` (how high its pieces stand: a sidewalk's 0.15); `turn` (degrees a piece
  is turned at random either way, 360 any way); `scale` `[min, max]`; `height` (metres a piece is scaled to, as made when
  left out: a palm and its card the same height); `clear` (keeps out of the clear areas: rows' and runs' do, spots'
  don't) and `room` (keeps off what stands already: rows' do, a lamp's arm or a palm's crown need not).
- `lamp`: the light each lamp post of the layout gives at night (`color`, `brightness`, `range`), at its piece's `lamp`.

- `weights`: how much more often a piece of a tag is picked against the others its slot takes (`{"industrial": 8}`):
  a piece's weight times the factor of every tag it has; 0 leaves the tag's pieces out of the layout.
- `name`, on any slot: what a diner's look calls it by. A look's slot of the same name takes the street's place (the
  Hi-Way's sparser palms, Surfside's palms twice as close), and the look's `drop` (`["trees_across"]`) takes the
  street's slots of those names away; the look's other slots come after the street's.

The rows go first, then the runs, then the spots, each in the file's order, a piece picked by its weight among those its
slot takes that fit. The street.json's own `lamps` are the stage's (under the diner's overhang); a diner's look recolours
the layout's lamps as it does the street's. A diner's look may lay a recipe of its own over the street's (see "diners",
`look.layout`): the street's slots it names in `drop` gone, those it has a slot of the name of replaced by its own, its
other rows, runs, spots and clear areas after the street's, its weights times the street's; the street is
then laid out for that diner apart, from a seed of the diner's own, so each diner's corner is a street of its own (the
base game's four on Main Street's one stage). A street seen with no diner to go by (a seller's curb, the Cruise screen
out of no diner) is laid out and dressed as the first diner on it wears it.

### streetkit

The pieces streets are laid out with (see "streets", `layout`): one folder per piece, its `piece.json` and its model
(`<folder>.kn5`, or `"model"`), stood on its base with the middle of its footprint at its origin, facing +z (a lamp
post's arm reaching out over +z). `footprint` `[width, depth]` and `height` in metres, as made (each 0.05 or more);
`tags`, what a recipe
asks for (the base game's: `shop`, `corner_shop`, `house`, `curb_car`, `truck`, `palm`, `palm_card`, `lamp`, `pole`,
`signal`, `hydrant`, `meter`, `furniture`, `sign`, `roof_sign`, `billboard`, `lot_prop`, `look`, `street_tree`,
`big_tree`, `plant_card`, and the kinds a diner's look weighs up, `industrial`, `beach`, `googie`); `paint` (the colours
it may be painted where it stands: `materials`, patterns of the names of its materials painted, any case; `colours`,
`[r, g, b]` each; `amount`, 0 to 1, how far its pictures go to the colour, 0.8 left out: each place of it is one of the
colours or as made, drawn with the layout and kept in the save; the picture keeps its light and shade, its middle tone
brought to the colour; any picture the game draws, a DDS too, made a DDS with its mips as a car's paint is, or a PNG
when its sides are no powers of two); `weight` (how often it is picked against the others of a

tag, 1 the usual, 0 never); `lamp` `[x, y, z]`, where a lamp post's light hangs in its own space; `credit`. A mod's
piece of an id replaces the base game's; a new id with a base game's tag turns up in everybody's streets (a piece whose
id is a tag, `house`, is one more house). Settings names the makers of every piece whose credit is not all CC0, and not
the game's own (`Street Corsa (our own)`: a diner's drums, fence, pennants and neon). The base game's are made by `tools/DinerScene/build_street.py
--kit` (see [screens/diner.md](screens/diner.md)).

### diners

The diners by level, `diners\diners.json` (`{ "diners": [ ... ] }`, or one file per diner, as the dealers), the base game's
four:

```json
{ "id": "hiway", "street": "hiway", "powerFrom": 220, "stakes": 1.5, "order": 2,
  "name": "diner.name.hiway", "about": "diner.about.hiway" }
```

- `powerFrom`: the diner's class, the cars from this many horsepower (the base game's unit, whatever the player's
  units). The diners are put in the order of their floors; the lowest is the beginners' diner and takes every car (its
  floor is 0, whatever it says), each next one the cars from its floor up to the next diner's. A racer is a **regular**
  of the diner their car's power (its dyno figure, else its spec sheet's) puts them in, a boss of the top one, and is
  found at no other; two diners on one floor share its racers, each racer always at the same one. The regulars of a diner
  above the player's car's class will not race it ("Not your class yet" on the map, "Won't race you" on their cards,
  `replies.snub` at the table), though the place is open to go, look, listen and talk; the King and a rival out for a
  rematch race it all the same, and a car the game has no power figure for is never turned away. A floor that is no
  number, or under 0, leaves the diner out.
- `street`: the street the diner stands on, one with a `diner.json` (the mod's own first, else the base game's, as a car
  filter names things). Left out, or a street with no diner on it (told in the log), the diner is seen on the first
  diner's street until it has one of its own.
- `stakes`: how many times the house limits (`wagers`) the cash bets go for there; 1 left out, 0 to 100.
- `name`, `about`, `region`: text keys; left out, `diner.name.<id>`, `diner.about.<id>` and `diner.region.<id>` (a
  mod's diner by its full id, then as the mod wrote it), the name else the street's title. `order` breaks ties between
  two diners on one floor: the first of them is where a car of that power belongs.
- `mapX`, `mapY`: where the diner is on the diners' map (the `diner_map` screen: the dealers' picture with the diners
  on it as cream diamonds; the racing hub's Hit the streets opens it, and a diner's Other diners button), fractions of
  the whole picture as the dealers' are; left out, or off the picture (a mods problem), it is marked in a row along the
  foot of the map, so every diner can be driven to. The pin says who is in tonight, "You're parked here" for the diner
  the player came out of (a click goes back to its tables at no cost), and whether the regulars race the player's car;
  a click on another drives down there.
- `look`: what the diner wears on its street. `layout`: a recipe laid over the street's (see "streets", `layout`; on a
  street laid out from a kit): the diner's own `spots` (its roof board, its billboard, what stands in its lot, each a
  piece of the street kit) and slots, its own versions of the street's named slots and the names it `drop`s, and
  `weights` (the Hi-Way's `industrial`, Surfside's `beach`, the Starlite's `googie`, each 8): the base game's Hi-Way has
  few palms, big banyans, gaps between brick shops and trucks at the curbs; Surfside no street trees and palms twice as
  close, tall and fan; the Starlite flowering trees close together and fewer palms; so every save lays out a street of
  each diner's own; a word no piece is or is tagged with is told among the mods' problems, and so are a slot's name
  no slot of its kind on the street has (it is added beside the street's), a name two of the look's slots share and a
  `drop` of a name the street has none of. A layout that is no object is told and the street's own laid. `approach`: the diner's own way to it and away, in place of its street's diner.json's
  (`arrive`, `backOut`, `leave`, `lookWhileParked`, `lookUpWhileParked`, as diner.json has them): the base game's
  Hi-Way is driven to down the cross street to the far curb level with its side bays, Surfside up the cross street and right
  onto Main Street to the curb by the crossing, the Starlite down the cross street to the curb across from its side
  bays, each with its curbs kept clear in its `layout`; the way's own stretch, 2 m either side, is kept clear of curb
  cars whatever the layout says (and a way moved lays a save's street out again); a way that goes nowhere, or does
  not read, is told and the street's is driven. A look's `layout` and `approach` are written on the street the diner
  is seen on (its own, else the first diner's): seen on another street (a mod's that took the first diner's place),
  the diner keeps its light and glow there, not its pieces nor its way. `show`: the look groups of the street model
  shown (names without the `LOOK_` prefix, any case; left out, the street's own `looks`): the kit's stage has none, the
  one-model Main Street (`build_street.py` without `--kit`) one a diner. `sun`, `lights` and `glow` as a street.json has them, over the
  street's: a light of the day given keeps the street's lamps at that hour (the street's day light's when it gives none
  for the hour; recoloured by its `lampColor`, when given) and adds its own `lamps`; a light with no `brightness` keeps
  the street's light and only recolours or adds lamps; a glow goes over the street's material by material (a piece's
  materials are its own, named as in the build). So the base game's four diners share Main Street's stage, each with its
  own sign, billboard, props, lamps, neon, shops and cars at the curbs.

- A mod may `replaces` a diner (the base game's `hiway`, say), add one above the top or below, or take one out with
  `removes`. The diners' map lists them all, the beginners' first, each with its class, who is in tonight and the stakes.

### stations

One folder per gas station, as the base game's `stations\crossroads`: a `station.json` and its model (`"model"`, a KN5
with a sky dome for each light of the day named `STATION_SKY_DAY`, `_DUSK` and `_NIGHT`) and, next to it, the ground's
height (`ground.bin`). `tools/StationScene` builds the base game's out of free models (see
[screens/gas-station.md](screens/gas-station.md)). The file gives the way in (`arrive`, off the road to the pump, the car
stopping at the last point) and out (`leave`), as `[x, z]` points in metres; where the driver looks at the pump
(`lookAtPump`, degrees to the left; `lookUpAtPump`, degrees up, −20 to 20); the `attendant` by the pump (`x`, `z` on the
ground, `y` up over the ground's height there, which `ground.bin` already raises on a pump island, so 0 for one standing
on it, `heading` in degrees about the vertical with 0 facing +z, and
`prop`, the actor he is: `attendant` unless named, see props); the lights of the day and the `glow` of the self-lit
materials (the tubes under the canopy, the pumps' globes) as a street's; `credit`, named in the game's credits; and what
the station charges (`prices`: `fuelPerLitre`, `oilPerLitre`, `wash`, `tow`; left out, the base game's). The garage's
Gas Station button drives to one of them at random. A station without its model still works, without the picture; one
without an attendant, or naming one no mod has, has nobody at the pump. A node of the model named `SWAY_<anything>`
leans in the wind (the base game's grass on the flat land and its trees): the game shears it about its own origin
every frame, so put the node's origin at the foot of what it holds, and the model is drawn on while it stands. Best
let nothing under it cast a shadow (the base game's do not, and lay a shade on the ground instead): a mesh under it
that casts has the scene's shadows drawn again ten times a second while it stands there, a pass over the whole scene
each time.

### booths

One folder per paint booth, as the base game's `booths\booth`: a `booth.json`, the room's model (`"model"`, default
`booth.kn5`) and the painter's (`"painter"`, default `painter.kn5`: a node `ARM` at his shoulder, the gun in his hand,
holding `MIST`, the spray, along +z; `tools/PaintBoothScene` builds both). The file gives the way in (`arrive`, through
the door to where the car is painted), how the painter goes round the car (`round`: `gap` off its body, `low` and `high`
the gun's swing in degrees, `strokes` up and down a side, `wait` where he stands while there is nothing to paint), the
camera (`camera`: `distance`, `height`, `min` and `max` the `[x, z]` corners of the box it stays in), the key light
(`sun`), the lights (`lights`, as a street's; `day` serves every hour) and the colours of the day offered to start from
(`colours`: each a `name`, a text pool key or the name itself, and a `hex`, `#RRGGBB`; a booth without them offers none,
the player mixes their own). The garage's Paint button drives to the first booth. See [screens/paint-booth.md](screens/paint-booth.md).

### warehouses

One folder per warehouse the player's cars are kept in, as the base game's `warehouses\warehouse`: a `warehouse.json`
and its model (`"model"`, default `warehouse.kn5`). The file gives the `bays` (`x`, `z`, `heading`: as many cars as it
has bays go in one warehouse), the `camera` the lot opens on (`alpha`, `beta`, `radius`, `targetHeight`), the `room`
the camera stays inside (`halfLength` either side of the middle along x, `halfWidth` along z, and the `height` of the
roof at its lowest), the `exit` a car taken out of its bay drives for (`x`, `z`: a point on the floor, the base game's
at its roller door; without it the car drives straight out of its bay, through whatever is in front), the `sun`,
the `lights` of the day as a street's plus `lampScale`, the `lamps` (always lit, coloured by the look), how bright the
material `wh_skylight` glows at each light (`skylight`), and the `looks`: for each, the picture each material wears
(`"surfaces": { "wh_wall": "wall_red_brick_03.jpg" }`; every picture must be in the model) and its `lampColor`. Warehouse
n wears the looks in turn; `digits` names the two materials that show its number and the pictures they take
(`"picture": "sign_digit_{0}.jpg"`). With several warehouse scenes installed, warehouse n is in the scenes in turn.
`tools/WarehouseScene` builds the base game's (see [screens/car-lot.md](screens/car-lot.md)).

**What a scene file may name.** A station's or warehouse's `"model"` stays inside its folder: one that points out of
it (`..\`, a full path) is not read, so the station goes on without its picture and the warehouse is left out. A
picture a warehouse names (`surfaces`, `digits`) is a plain file name with no folder in it; any other is left out and
logged. The lights of the day in `lights` are named `day`, `dusk` and `night` (any case): a number or another name is
logged and ignored, and a light written as `null` is skipped, as if it were not there.

### shopscenes

One folder per part shop room, as the base game's `shopscenes\shop`: a `shop.json` and its model (`"model"`, default
`shop.kn5`). Every part shop is seen in the last one the mods give, so a mod's room takes the base game's place; a scene
with `"kinds": ["machine"]` is those kinds' alone (the base game's `shopscenes\machine_shop`), and the others are seen in
the last scene with no kinds. The file
gives the `camera` (`alpha`, `beta`, `radius`, `targetX`, `targetHeight`, `targetZ` the point it turns round, and `fov`
the lens, degrees high), the `room` the camera stays inside (`halfWidth` either side of the middle along x, `halfDepth`
along z, the ceiling `height`), the `sun`, the `lights` of the day with `lampScale` and the `lamps` as a warehouse's, how
bright the material `sh_window` glows at each light (`window`), the `places` the shop's stock stands in, and the `looks`.

A place is the middle of its floor (`x`, `y`, `z`), its room (`width` along its own x, `depth` along its own z,
`height`), the way it faces (`heading`, degrees; 0 faces +z) and what it `holds`: `engine` (a whole engine; with none on
any stand, the biggest part goes on one), `wheel` (tyres and rims first), `large` or `shelf` (anything that fits), or a
shelf's group by its id (`crankshafts`: a part of that group goes there before anywhere else, and nothing else). When the
player walks in, the shop's stock is stood in them, one part a place, in the file's order, so put the places the eye
goes to first; a part too big for every place left is not shown (it is still at the counter). A look has an `id`, the
shop `kinds` it is for (`speed`, `specialist`, `wheels`, `junkyard`), the picture each material wears (`surfaces`, the
sign among them) and its `lampColor`; a shop wears the look it names, else the first for its kind, else the first.
`tools/ShopScene` builds the base game's (see [screens/part-shop.md](screens/part-shop.md)). What a scene file may name is
held as a warehouse's is.

### showcase and garages

`showcase` lists the main screen's rooms: `{ "scenes": [ { "id": "mygarage", "wallRadius": 12 } ] }`. A room is a
folder of `garages` (a mod's own, `garages\mygarage\mygarage.kn5`; a later mod's room of the same name wins) or an AC
showroom. The garage the player works in is the room game.json names (`workshop`, the base game's own); its
`scene.json` names the marks its car scripts drive by (see scripts) and, for a garage of a mod's own, its room:

- `room`: `{ "minX": -5, "maxX": 7, "minZ": -7, "maxZ": 5.5, "height": 2.8 }`, the walls either side of the car's spot
  (x across, +x the car's left; z along the car, +z its nose) and how high the camera may go: it stays inside, however
  it is swung or wheeled out. Without one the camera keeps the garage screen's own limits (an AC showroom).
- `sun`, `lights` (`day`, `dusk`, `night`) and `lamps`: as a warehouse's (the lamps lit at every hour, `lampScale` how
  bright they are then); the game's clock picks the light. Without `lights` the garage is lit as the renderer lights an
  AC showroom.
- `glow`: how bright each self-lit material is at each light of the day, `{ "day": { "gr_window": 1.1 }, "night": {
  "gr_window": 0.06 } }` (0 to 4).
- Sky domes named `GARAGE_SKY_DAY`, `GARAGE_SKY_DUSK` and `GARAGE_SKY_NIGHT` in the model are shown at their light and
  hidden at the others (what is seen through an open door).

`tools\GarageScene` builds the base game's (`prepare_garage.py`, then `GarageSceneBuilder`), and its props
(`fetch_props.py`, then `build_props.py`); see [screens/garage.md](screens/garage.md). Its props have places in
`workshop`; a mod's own garage places them, and its own, under its own id.

### flow

How the screens hang together: `flow\*.json`, merged **key by key** like the rules (an object goes into the one
before it; a value or a list takes its place), but not kept in a save: a mod that changes its flow changes every
career. The base game's is `flow\flow.json`; its comment says what each part is.

```json
{
  "screens": {
    "touge": { "kind": "street", "exits": { "back": "garage" } },
    "diner": { "exits": { "back": "touge" } },
    "newspaper": { "off": true }
  },
  "tiles": {
    "garage": {
      "career": { "off": true },
      "touge": { "to": "touge", "text": "mymod.tile.touge", "icon": "icons/touge", "spot": "right", "order": 2 }
    }
  },
  "cards": { "settings": { "order": 0 } }
}
```

- **`screens`**, by id: each is a `kind` the program has (`main_menu`, `garage`, `car_lot`, `racing_hub`, `career`,
  `diner`, `diner_map`, `street`, `gas_station`, `newspaper`, `car_viewing`, `dealer_map`, `dealer_lot`, `part_shops_map`,
  `part_shop`, `mail_order`, `race`, `sound_browser`, `car_catalog_editor`), and its `exits`, its ways out by name,
  each to a screen id. A mod may rewire an exit, add a screen of a kind there is under an id of its own, or take one
  `off`. A new kind of screen needs the program. `racing_hub` and `career` open over the screen they are opened from.
- **The exits the screens take**: `back` (most of them), `bought` (the car viewing and a dealer's lot, after a sale),
  `gone` (the car viewing, a car sold meanwhile), `garage` (the car viewing's own button), `car_ad` (the paper, to the
  car viewing), `lot` and `shop` (the maps, to a dealer's lot or a part shop), `diner` (the diners' map, a diner's pin,
  and the street), `map` (the diner, off to the diners' map: the car drives off, and down another diner's street from
  the map), `mail_order` (a map: its button is shown
  only on a map whose exit leads somewhere), `cruise` (the diner, to the street), `streets` (the racing hub's Hit the
  streets, to the diners' map in the base game; wired straight to a `diner` screen, the first diner),
  `car_lot` and `quit` (the garage), `won` (the career, when picking a path wins),
  `after_race` (where a race set up on that screen comes back to; none: the screen itself). A race comes back to the
  screen it was set up from, the racing hub's to the screen it was opened over.
- **`home`**: a `garage` screen (any other kind is told and the first garage is home): where a game starts, where
  the night is ended and where an exit that leads nowhere goes (a button for such an exit is not shown
  where the screen has one to hide: the diner's Cruise, the street's Diner, a map's Mail order, the hub's Hit the
  streets). A car towed home, and a street race that ran past the night, go home. **`menu`**: the main screen.
- **`system`**: the rules' system a screen belongs to (`rules`, `systems`). Its kind has one already (the diner's is
  `diner`, the gas station's `station`, the paper's `paper`... the garage's none). A screen of a
  system switched off is not there, nor are the tiles and exits to it. What goes with each switch:
  - `police`: no police come to a race; the risk is not shown;
  - `station`: no gas station; the player's cars are filled up, topped up and washed every night for free;
  - `paper`: no paper, nor the player's own ads (the Sell dialog keeps the dealer); the part ads still come while the
    mail order is on, and so do the deliveries;
  - `carDealers`, `partShops`: their maps, lots and shops, and their nights' work, are gone (the Sell dialog's dealer
    stays: it is how a car is sold without a paper). With no part shops, the base game's garage shows a Mail order
    tile (`"without": ["partShops"]`), and the mail order's back goes home;
  - `mailOrder`, `street`, `diner` (the diner and the diners' map): the screens and the ways to them;
  - `engineStand`: no stand in the garage; an engine pulled goes on the shelf;
  - `talk`: nobody can be called or talked to, whatever the player's AI;
  - `career`: no career screen; the game is won on `systems.careerPath`, or on whichever victory comes first;
  - `paintBooth`: no paint booth; a car keeps the colours it came in.
- **`endDay`**: the day can be ended from that screen. The garage has its own End Day button (shown only while its
  screen says so); anywhere else the window shows one under the money, which asks and then goes home for the night.
  Not on the main menu, a race, an overlay or the tools.
- **`tiles`**, by hub screen then by tile id: the round buttons round the garage's car. `to` (a screen id, or one of
  the hub's own things: `quit`, `car_info`, `calendar`, `radio`), `text` (the tooltip's text key), `icon` (a theme
  image name, `icons/...`), `spot` (`top_left`, `left`, `bottom_left`, `top_right`, `right`, `bottom_right`: a column
  from the top; top left stays while the car is being worked on), `order` (in its spot), `size` (`large`), `padding`,
  `requires` (`car`: a car picked; `car_here`: one that is not impounded), `system` (shown only while it is on),
  `without` (shown only while all of these are off), `off`. A tile cannot open a screen that needs to be told what to
  show (`car_viewing`, `dealer_lot`, `part_shop`, `race`). A tile to the gas station or the paint booth drives the car
  out first (`garage.leave`; one that will not run is pushed to the booth).
- **`cards`**: the main menu's buttons, from the left: `card` (`load_game`, `new_game`, `settings`: each has its
  picture), `text` (a text key; none: `menu.<card>`), `order`, `off`.

What does not read (a kind there is not, an exit to no screen, a tile to nowhere, a spot or a requirement there is not)
is told in the mods' problems and left out. An exit or a tile to a screen a mod took `off` goes quietly.

### scripts

How the cars move on the screens: the car taken out of the lot driving off, the one coming into the garage, the
player's car at the gas station, the rival on the street. A **script** is a list of steps played at a moment of the
game (its `trigger`); a **feel** is how a drive is driven. The base game's are in `scripts\scripts.json` and
`scripts\feels.json`; their comments say what each does.

```json
{
  "scripts": [
    {
      "id": "night_arrive",
      "trigger": "station.arrive",
      "when": { "light": "night" },
      "most": 30,
      "steps": [
        { "do": "lights", "head": true },
        { "do": "drive", "path": "arrive", "feel": "road", "start": "rolling" },
        { "do": "wait", "seconds": 1 },
        { "do": "lights", "head": false }
      ]
    }
  ]
}
```

**Which script plays.** Of the scripts on a trigger, the latest mod's whose `when` holds. A script that `replaces`
the base game's (`"replaces": "station_arrive"`) takes its place; one with its own id and a `when` plays only then, the
base game's the rest of the time. No script on a trigger: nothing moves, and the car is where the move would end.

**The triggers.** `lot.take_out` (the car picked in the lot is taken; the screen goes once the script lets it),
`garage.arrive` (a car taken out of the lot under its own power comes in), `garage.leave` (the car leaves for the gas
station; the base game has none), `station.arrive` (in off the road to the pump; the pump's buttons wait for the
script to let go), `station.at_pump` (stopped at the pump, or towed there; the base game has none), `station.leave`
(out onto the road; the screen goes when the script lets it), `street.rival_arrive` (a rival up the lane, stopping
alongside), `street.rival_leave` (the rival away; it is gone when the script ends), `diner.arrive` (down the street and
into the bay at the diner; its buttons wait for the script to let go), `diner.parked` (in the bay, or back from a race;
the base game has none), `diner.leave` (out of the bay and off down the street; the screen goes when the script lets
it). And the flow's own, on the
screens with the player's car on a 3D scene (the garage, the car lot, the gas station, the diner; not the street, where the
player's car is the camera): `screen.<id>.enter` (the screen of that id in the flow is up and its own show is over: the
garage once it is revealed and any car has driven in, the lot once its warehouse is up, the station once the car is at
the pump, in place of a `station.at_pump` script still playing) and `screen.<id>.leave` (the player leaves it by a way
out, a tile, a race set up on it or the racing hub's Hit the streets; the screen goes once the script lets it, and a car
driven off a way out that then goes nowhere is put back; a way out that leads to no screen plays none). The garage's car
leaving for the gas station plays `garage.leave`, not `screen.garage.leave`; a race entered from the racing hub plays
none (the hub is over the screen). The gas station's plays once its car has pulled out (`station.leave`), before the
curtain; a car taken out of the lot plays `lot.take_out` first, then the lot's leave script.

**The game never waits on the show.** It goes on when the script lets it: a `release` step, or the end. A click on
the picture skips the rest of a script: every step from there is done at once, a drive under way is put at its end,
a fade is finished. So is a script that takes longer than its `most` seconds (30 when it says none). A step that
cannot be done (a mark the scene does not have, say) is a line in the mods' problems, and the rest is skipped the
same way. A screen that goes stops its script where it is. In the lot and the garage, a car a script leaves away
from its place goes back to it once the script is over and the car stands still: the screen is the player's again.
A fade's `engine` turns the engine down against the level the screen has it at (the street's rival heard from where
it is, the station's car from inside), never louder; a screen that takes the car back puts it back up.

**The steps.** Each is `{ "do": ... }`; `"car"` names the car a step is about where a screen has more than one (the
street's is `rival`; left out, the script's own car). A key a step does not know leaves the script out, with a line
in the mods' problems that says which, so a mistyped key is never silently ignored.

| Step | Keys |
|---|---|
| `lights` | `head`, `brake`: true or false; one left out stays as it is |
| `engine` | `on`: true starts it (`crank`: false for one running at once, as a car that drives up; `wait`: false to go on with the next steps while it starts; `most`: seconds to wait for it, 4), false stops it; or `rev`: the throttle 0..1 for `seconds` |
| `wait` | `seconds`; or `until`: `settled` (the body stops rocking), `running` (the engine runs), `drive_end` (the drive under way is over, or `early` seconds before), with `seconds` as the most to wait |
| `drive` | one of `path` (a path of the scene, start to end), `to` (a mark, in a straight line, `from` a mark or where the car is), `toward` (a mark a pull steers for; a pull without one goes straight on), `distance` (metres along the car's own length, backwards when negative); `feel`; `reverse`; `start`: `standing` or `rolling`; `end`: `stop` or `keep_going`; `wait`: false to go on with the next steps while it drives |
| `place` | `at`: a mark (`heading` in degrees, else as it faces); or `path` and `end`: `start` or `end`, facing along it |
| `fade` | `to`: the screen's curtain 0..1 (1 black), over `seconds`, `ease`: `none`, `in`, `out`; `engine`: true takes the engine's volume down with the picture. A screen with no curtain (the station, the street) waits the time |
| `camera` | `mode`: `hold` (the camera stays and watches the car go: the default while a script plays) or `follow`; the seat views (station, street) have their own |
| `sound` | `cue`: a sound cue of the mods' `sfx` (the station's arrival rings `station.bell` a second before the car stops); a script skipped past it plays none |
| `release` | the game goes on, the script plays out |
| `end` | the script is over |
| `{ "if": {...}, "then": [...], "else": [...] }` | the steps of `then` when the condition holds, else of `else` |

**Conditions** (`when` and `if`): every key must hold. `engine`: `runs` (the car has an engine that starts), `dead`,
`running` (now), `off`; `towed`: true or false; `light`: `day`, `dusk`, `night`, or a list; `hours`: `[from, to]` on the
game's clock, round midnight when from is the later (`[20, 6]`); `from` and `to`: the flow's screen id the player came
from or is leaving for (a `screen.<id>.leave` script), one or a list; `not`: a condition; `any`: a list of conditions.

**Marks and paths.** A drive goes to marks and along paths the scene names. A mark is a point `[x, z]` in metres, or a
point measured from where the car stands, `{ "behind": 7 }` (`ahead`, `left`, `right`); `home` (where the car stands
when nothing moves it) and `here` (where it is now) are on every scene. A point is within 10000 metres of the
scene's middle, a measured one within 1000 of the car, in a scene's file as in a script. Every scene file may name its own:
`"marks": { "kerb": [3, -4.5] }` and `"paths": { "round_back": [[0, 0], [5, 5], [0, 10]] }`, next to what it has.
Besides these, the lot's warehouses have `exit` (their `exit`; without one, straight ahead), the garage has `door`
(from `garages\<id>\scene.json`; without it, 7 metres behind the car), the gas stations have the paths `arrive` and
`leave` and the mark `pump` (the end of `arrive`), the diner has the paths `arrive`, `back_out` and `leave` and the mark
`bay` (the end of `arrive`), and the streets have the paths `arrive` (up the lane to the stop)
and `leave`, and the marks `alongside` (the end of `arrive`) and `gone` (out of sight up the lane). The car stands at
the pump, the rival alongside, when nothing moves them: a scene that names its own `arrive` moves where they stand
with it.

**Feels.** In a `feels` list, by id; a drive names one (`"feel": "lane"`, or another mod's as `"mymod:creep"`), or has
`road` (`pull_away` for `toward`). A feel is a `kind` and its numbers; a number left out is its kind's, as the base
game's feels have them. Speeds are metres a second, accelerations metres a second squared.

| Kind | What it is | Its numbers |
|---|---|---|
| `road` | along a path, cruising between bends and slow enough in them, braking in time for a stop | `cruiseSpeed`, `acceleration`, `brake`, `cornerAccel` (the most sideways pull in a bend), `clutchSpeed`, `cruiseThrottle`, `gear` (1 on: stays in it; 0 changes up) |
| `launch` | hard off the line through the gears to the end of its way (it never stops) | `acceleration`, `topSpeed`, `shiftSeconds` |
| `pull` | gently away in first gear, straight out of its place, then turning for the mark it goes `toward` (round for one beside or behind it), and on | `acceleration`, `topSpeed`, `clutchSeconds`, `throttle`, `cruiseThrottle`, `rpmPerMetrePerSecond`, `straightOut`, `turningRadius`, `steerGain`, `steerSeconds`, `steerLead`, `seconds` (how long it goes on) |
| `roll_in` | rolling in and braking to a stop at the end of its way, the braking fading as it stops | `cruiseSpeed` (the speed it comes in at), `rpmPerMetrePerSecond` |

Every kind also has `lean` (how much of the body's lean in a bend shows, 0..1), `halfWheelbase`, `ratios` (the
gearbox, first gear first), `finalDrive` and `tyreRadius`. A drive is worked out whole before the car moves, so a
skipped one lands exactly where it would have ended.

### text

The words the game says, as pools of lines under keys. Where the game says something, it picks a line of the pool at
random and fills in its `{placeholders}`:

```json
{
  "lines":   { "talk.pulled_up": [ "That {car} got any guts?" ] },
  "replace": { "news.first_win.headline": [ "Rookie Wins" ] }
}
```

`lines` add to a pool, `replace` rewrites it. The base game's files (`news.json`, `talk.json`, `street.json`,
`station.json`, `names.json`, `ads.json`: the classifieds' few words on each ad) list every key and, in their `_comment`, the placeholders each takes. The answers to a
challenge are the `replies.*` pools in `talk.json`, among them `replies.car_laid_up` and `replies.car_unknown` (the
racer's car can't race today, or the game can't tell), `replies.king.pink_slips` and `replies.king.no_pink_slip` (a
boss takes a pink-slip race, or turns down one for money; see racers for a boss's own), `replies.rematch` (a racer out for a rematch) and
`replies.always_accept` (`{name}`, when the testing switch makes everybody accept). When to say what stays in the game.

Every word on the screens is a pool too: the labels, buttons, messages and titles are in `ui.json` (what every screen
shares, `common.*`) and `ui_<area>.json` (`ui_garage`, `ui_race`, `ui_market`, `ui_shell`, `ui_parts`), said one way
(the pool's first line). A key no mod has shows as the key itself, so a word left out shows where it goes. The race
mode's messages in AC are the `race.*` pools of `race.json`: they reach it through race.ini. The developer's screens
(the car catalog editor, the sound library) stay in English.

**Languages.** The files directly in `text` are in the game's own language (`game.json`'s `language`). A translation
is a folder in `text` named for its language, `text\es\*.json`, written like the rest (`lines`, `replace`), over every
mod in load order; its `language.name` pool is its name in the Settings picker (`"Español"`). With a language picked,
its pools take the place of the game's, and a key it lacks says the game's own words: a translation can be partial, and
a mod that adds words can add their translations in its own `text\<language>`. A mod's `removes` and `replace` hold in
every language: a pool it takes out says nothing, and one it rewrites says the mod's words (or the mod's own
translation of them) rather than an earlier mod's translation of the old ones. The picker shows once a mod has a
language besides the game's own; the game speaks it from its next start.

**Money, units and dates.** Numbers and dates are written in the game's culture (`game.json`'s `locale`). A number the
game fills in can be named for what it is, and is then written in the game's currency or the player's units:
`{price:money}` ("$1,500"), `{peak:temp}` ("248 °F" or "120 °C"), `{kmh:speed}`, `{km:distance}`, `{litres:fuel}`,
`{litres:oil}`, `{kg:mass}`, `{nm:torque}`, `{hp:power}`, `{hp:power_weight}`. A number format may follow
(`{left:fuel:0.0}`), and `.number` writes the figure alone in that unit (`{left:fuel.number} of {tank:fuel}`: "2.0 of
16 gal"). The figures the game hands over are metric. Money the game hands over writes itself in the currency
whichever way it is named: `{price}` and `{price:money}` both write "$1,500", `{price:money.number}` the amount alone
("1,500"), `{price:N2}` with that format. Figures the game writes out itself come already written (`{speed}` in the
`_comment`s says so). A fraction with no format reads to two decimals at most. A date takes a .NET pattern:
`{until:dddd d MMMM}`.

### ai

What the player's own AI is asked, when they connect one in Settings ("AI": Ollama, an OpenAI-compatible API such as
LM Studio or OpenRouter, or Anthropic; ComfyUI for pictures). It is off by default and the game never waits on it: the
game's own words are always made first, the AI tells them again or adds to them in the background, what it writes is
kept in the save (next to the game's own, which stay for another language and for when the AI is off), and anything
that fails leaves the game's own. So a game mod has its own voice.

`ai\prompts.json`: every mod's goes over the ones before it **key by key**, so a mod changes the persona or one prompt
without repeating the rest.

```json
{
  "persona": [ "You write for {title}, a game about street racing in Tokyo in 2003.", "Write in {language}. Speeds in {units}." ],
  "prompts": {
    "talk": { "prompt": "{name} says: \"{line}\". Say it again in their voice.", "maxLength": 180, "maxTokens": 80, "temperature": 0.9 }
  }
}
```

- `persona`: who the AI is, sent with every request as its system prompt (a text, or lines joined with spaces).
- `prompts`: one per use: `prompt` filled like the game's text (`{price:money}`, `{mileage:distance}`), `maxLength`
  (characters kept, cut at a sentence or a word), `maxTokens`, `temperature` (not sent to Anthropic), `lines` (the
  first lines of the answer kept, 1 when not given, 0 for all: a small model likes to add a second try).
- Every prompt may name `{language}` (the player's language, by the name its `language.name` pool gives it), `{units}`
  and `{title}`. The uses, and what each may name, are listed in the base game's file's `_comment`: `talk` (a rival's
  line; `{line}` is the game's own), `street_talk` (the diner's word on the street), `news` (a piece in the paper,
  answered as the headline on the first line and the body after), `ad` (an
  ad's blurb), `portrait_look` (a new rival's looks for the picture, in English) and `test` (Settings' Test button).
  A use with no prompt is not asked for.
- Talking to people: `chat_seller`, `chat_salesman`, `chat_rival`, `chat_buyer` (who answered the player's own ad), `chat_clerk` (behind a part shop's counter) are sent after the persona as the rest of who the AI
  is, with the talk as the messages; they name `{name}`, `{nickname}`, `{age}`, `{sex}`, `{temperament}`, `{place}`,
  `{player}` and `{facts}` (the game's fact sheet, the text pools `chat.fact.*` and `chat.deal.*`, in sections headed by
  `chat.section` and `chat.section.*`; best at the prompt's end, as the base game's are, so a local model reads again
  only what changed; its size is the rules' `ai.talk.factTokens`). `chat_memory`
  writes what a person keeps of a talk (`{name}`, `{player}`, `{talk}`); `chat_summary` sums up the start of a long talk
  (`{name}`, `{player}`, `{earlier}` the summary so far, `{talk}` the turns to add). The fact sheet also tells how the person
  feels about the player (`chat.fact.feeling.*`) and what happened between them (`chat.fact.events`, `chat.fact.event.*`),
  moved by the rules' `ai.talk.feeling*` weights. A clerk's sheet has the shop (`chat.fact.shop*`) and what the game
  found in its stock for the player's last ask (`chat.fact.stock.*`); `clerk_search` reads that ask first, answered as a
  JSON object (`asks`, `kind`, `fits`, `maxPrice`, `words`, `want`; Ollama and OpenAI-compatible providers are held to
  the schema, Anthropic by the prompt), from `{said}`, `{talk}` (the last turns), `{kinds}` (the kinds in stock) and
  `{player}`. The stage lines, the window's words and the
  words that make a number an offer are the text pools `chat.*` (the base game's `text/chat.json`).

`ai\people.json`: the temperaments people are drawn with, merged key by key:

```json
{
  "temperaments": {
    "jive_talker": { "words": "A fast-talking car salesman full of jive...", "give": 0.8, "for": ["salesman"], "weight": 4 },
    "hothead": { "words": "Cocky, quick to take offence...", "give": 0.4, "for": ["rival"], "minAggression": 65 }
  }
}
```

- `words`: who they are, as the AI is told. `give`: how far they come down on a price, times the rules' most for their
  kind (0 never; a buyer's: how far they come up). `for`: `seller`, `salesman`, `rival`, `buyer` (none: every kind). `weight`: how often. `minAggression`,
  `maxAggression`: a rival's aggression range. Every kind should have one for every aggression, or the person has none.

`ai\ollama.json`: the game's own model in Ollama, made from Settings ("Make the game's model"), merged key by key:
`name`, `from` (the base model), `system` (a text or lines), `parameters` (as a Modelfile's), `examples`
(`{ "player", "answer" }` pairs, its first messages).

A chat prompt (`chat_seller`, `chat_salesman`, `chat_rival`, `chat_buyer`) may have `examples` too, the same pairs, filled
into its `{examples}` under the text `chat.examples`: how the person might talk, for every provider. Keep figures out of
them: a model copies them.

`ai\lore.json`: what everybody knows in the game's time and place, told to the people the player talks to as small talk
they may use (the rules' `ai.talk.loreTold` lines, drawn across the topics they know). Merged key by key: a mod adds a
topic, changes one's lines, or takes one out with `"lines": []`.

```json
{
  "lore": {
    "radio_1970": {
      "from": 1970, "to": 1970,
      "lines": ["On the radio: Creedence's 'Up Around the Bend'..."]
    },
    "racing": { "for": ["rival"], "lines": ["Big Daddy Don Garlits is the name everybody knows in Top Fuel."] }
  }
}
```

`lines`: one thing known a line, written to the AI. `for`: the kinds who know it (`seller`, `salesman`, `rival`, `buyer`;
none is everybody). `from`, `to`: the years of the game's calendar it is true in (none, always). Never a car's price.

`ai\comfy_<use>.json`: a ComfyUI workflow, the last mod's whole (a graph is not merged). The base game has
`comfy_portrait.json`, the portrait painted for a new face on the street, shown in place of their own portrait from then
on (kept under `%AppData%\StreetCorsa\AiPortraits\<save>`):

```json
{
  "checkpoint": "juggernautXL_ragnarokBy.safetensors",
  "width": 512, "height": 512,
  "prompt": "1960s American street racer portrait, {sex} aged {age}, {look}, film grain",
  "negative": "modern clothing, cartoon, watermark",
  "fill": {
    "positive": { "node": "10", "input": "text" },   "negative": { "node": "12", "input": "text" },
    "seed": { "node": "11", "input": "seed" },       "width": { "node": "22", "input": "width" },
    "height": { "node": "22", "input": "height" },   "checkpoint": { "node": "8", "input": "ckpt_name" },
    "prefix": { "node": "21", "input": "filename_prefix" }
  },
  "workflow": { "8": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "" } } }
}
```

- `workflow`: the graph in ComfyUI's API format (in ComfyUI, "Save (API Format)"); any graph that ends in a saved
  image works.
- `fill`: which node input gets which value: `positive` and `negative` (the prompts, filled with `{look}`, `{name}`,
  `{nickname}`, `{age}`, `{sex}`, `{biography}`, `{aggression}`), `seed` (a new one each time), `width`, `height`,
  `checkpoint` (left as the graph has it when empty) and `prefix` (the name ComfyUI saves under). A node the graph does
  not have is told at start-up.
- `checkpoint`: a model file ComfyUI has in `models\checkpoints`; set it to one you have.

How long the game waits, how often it asks and what it keeps are the rules' `ai` part.

### theme

The game's look: its colours, font sizes, typefaces and pictures. `theme\*.json` sets keys, naming only what it
changes; a later mod's key goes over an earlier one's:

```json
{
  "colors": { "AccentPrimary": "#2E7D32", "UiOutline": "#80D8FF", "BgDark": "#101418" },
  "sizes":  { "FontSizeBody": 15 },
  "fonts":  { "BodyFont": "Bahnschrift", "DisplayFont": "Bebas Neue" }
}
```

The base game's `streetcorsa\theme\theme.json` lists every key there is, at the game's own value: it is the list to
pick from (the game itself does not read it). A colour is `#RRGGBB`, `#AARRGGBB` (the first byte is how solid it is)
or a colour's name; a size is in pixels; a typeface is a family name, found first among the font files in the mod's own
`theme\fonts` (`.ttf`, `.otf`), even one with the name of the game's own typeface, then among the fonts installed on the
PC. A key the game lacks, or a value that does not read, is told at start-up and
the game's own stands. Keys are matched whatever their case. The look is taken at start-up: a change shows at the next
start. The typefaces: `BodyFont` (everything not named otherwise), `DisplayFont` (the intro's title), `DialFont` (the
radio's lettering), `NumbersFont` (figures that line up: the timeslip, the dial-in board, the engine panel),
`TypewriterFont` (the licence, the main menu badges' labels), `NewsprintFont` (the paper's classifieds: the ads' small print, the
masthead), `NewsHeadFont` (the ads' run-in headlines and the paper's section heads, set condensed and bold).

A theme sets keys only: the shapes of the buttons and panels stay the game's (a mod's XAML is never read, since XAML
can run code).

**Pictures.** `theme\images\<name>.png` (or `.jpg`, `.jpeg`, `.bmp`, `.ico`) stands in for the game's picture of that
name, the last mod in load order first; one that does not read is told at start-up and the one under it is used. The
names:

| Name | What |
|---|---|
| `backgrounds/main`, `init`, `newspaper_background`, `diner_background`, `settings_background` | the screens' backgrounds (the game's are 2560x1440; `init` is behind the intro) |
| `buttons/new_game`, `load_game`, `settings` | the main menu's badges (square) |
| `icons/calendar`, `car_dealer`, `car_info`, `driving_wheel`, `fuel_pump`, `garage`, `newspaper`, `parts_filled`, `quit`, `radio`, `trophy` | the round buttons' icons: only their shape counts, painted in the button's colour |
| `icons/car_key`, `car_parts`, `engine_stand`, `gears`, `wrench`, `high_beam`, `car_door`, `rotate`, `handshake` | the garage's buttons under the car (the key, parts view, engine stand, turn over, repairs, lights, doors, rotate) and its Sell button; one that is missing shows its word instead |
| `misc/license` | the licence the player signs for a new game (the name box sits where the game's has its line) |
| `misc/calendar/calendar01` .. `calendar12` | the garage calendar's pictures |
| `intro/logo` | the game's logo on the intro, in place of the lettered title (the base game has none) |
| `window/icon` | the window's and the taskbar's picture (the exe's own icon cannot change) |

The window's title is `game.json`'s `title`; the intro's words are the text pools `intro.title` and
`intro.powered_by`; the dealer map's picture is a `maps` entry of `dealers`.
