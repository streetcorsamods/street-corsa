# Modding Street Corsa

Everything the game knows beyond Assetto Corsa's cars and tracks is **content in mods**: parts, engine sounds, dealers,
racers, events, milestones, victory paths, streets, gas stations, warehouses, the main screen's rooms, garages, the
words the game says, and the rules it plays by (what things cost, how long they take, how the police and the rivals
behave). The base game is itself a mod, `Content\streetcorsa`, loaded through the same loader as any other, first.

Cars and tracks stay AC's: install them in AC's `content` folder as usual.

## A mod

Drop a folder into `Content`, next to the game's exe:

```
Content\
  streetcorsa\          the base game (loaded first)
  mymod\
    mod.json
    game.json
    rules\  parts\  sounds\  sfx\  props\  dealers\  partshops\  racers\  events\  milestones\  victories\  streets\  stations\  warehouses\  showcase\  garages\  text\  theme\  ai\
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
  ids: `dealers`, `partshops`, `racers`, `events`, `milestones`, `victories`, `streets`, `stations`, `warehouses`,
  `text` (pools, by key) and `sfx` (cues, by name). What the mod itself or a later mod adds under the same id stays: a
  mod can take out a base game's pool or cue and give its own. An id no mod before it has is told. Parts can't be
  removed (yet): other parts and engine builds hang off them.
- A folder whose name starts with `_` or `.` is switched off.
- Only the folders a mod has are read; every one is optional.

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
  showroom of that id.
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
| `time` | minutes each action takes (`actions`, by the game's action names), each part at the workbench, an engine in or out, the stand, a tick of cruising, the shortest trip |
| `difficulty` | the New Game screen's presets (`easy`, `normal`, `hard`) and how often pink slips come up at each frequency |
| `police` | how likely a street race draws them, fines, the impound, cars per race, the AC cars they like best |
| `rivals` | how many race, their money (`money`, by age band), going broke, trading up, leaving and coming back, selling cars on, the King's money and record |
| `dealers` | dealers opening and closing, closing-down sales, trade |
| `partShops` | the junkyard, haggling, the regulars' discount, trade, and each kind of shop's prices and shelves (`kinds`) where a shop's own file says nothing |
| `economy` | what parts cost against their scripts (`partsPriceScale`), used and trade-in shares, mail order, the paper's part ads, a prize camshaft's cash |
| `market` | the dealers' lots, selling a car, and what a car is worth (condition, history, mileage, missing parts) |
| `cars` | what a car is worth new and how common it is where cars.json gives neither: the price for its power to weight (`referencePrice` at `referenceHpPerTonne`, rising as the ratio to `priceExponent`), a car without specs, the least and most, the rounding, the believable weights, the factor of its year (`priceByYear`: the first step it is in, `{ "from": 1960, "value": 1.2 }`, a step without `from` takes any year) and source (`kunos`, `dlc`, `mod`, `unknown`), and how common it is by year, source and power to weight. Unlike the rest these follow the mods next to the game, not the career: the catalog is the program's, and a change works every price out again |
| `engines` | how an engine is cooled and oiled by its parts (`cooling`), how the cars on the lots were worked on (`factory`: swaps, bolt-ons), how the rivals' tuners work (`tuner`), and the points an AC car's name and power give an engine build when the game picks its factory engine (`matcher`) |
| `repairs` | the garage's repairs: parts, labour, body work |
| `condition` | damage, wear, dirt, fuel and oil, and what a race takes out of a car |
| `street` | who comes by while cruising and how soon, drag or road, pink-slip offers, grudge rematches, the diner's talk |
| `wagers` | the smallest and largest cash wager, night stakes, a fair match |
| `reputation` | how reputation is worked out: base, wins, pink slips, losing and winning streaks, false starts, escapes |
| `events` | how often race invitations come, how many at once |
| `news` | what the paper prints and for how long |
| `simulation` | the rivals racing among themselves: crashes, pink slips |
| `race` | AC's damage, tyre wear and fuel rates in a race, how the police drive |
| `world` | the sunset through the year, the seasons the rivals race in, AC's weather, temperature, wind, latitude and time zone |
| `ai` | the player's AI (Settings, "AI"): how long a rival's line waits for it (`talkWaitSeconds`), a request's and a picture's timeouts, requests at once and the gap between them, the queue, the rest after a failure, lines written ahead, the chance a line of street talk or a piece in the paper is written again, whether new faces are painted, and how many of its words a save keeps |

## Ids

Every entry has an id, written **without** the mod's name. The game puts the mod's id in front: the part
`engines/gm/intake` of `mymod` is `mymod:engines/gm/intake` in the game. Two mods can never take each other's ids by
accident. The base game's ids go without a namespace (`engines/gm/V8_small_block`), which is why saves made before
mods still work.

A **reference** to another entry (the block a part mounts on, the milestone an event asks for, the block a sound is
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

Laid out as the base game's `Content\streetcorsa\parts` (see [parts-system.md](systems/parts-system.md)): packs
anywhere under the folder (a `pack.json` with the parts' `.kn5` models next to it), and next to them:

- `_scripts\` — the compiled SLRR scripts of the mod's parts; `source_script` is found here. A script class the mod
  does not have is looked for in the base game's, then in the other mods'.
- `engine_builds.json` — complete engines; build ids are namespaced like parts.
- `part_aliases.json` — `{ "old id": "new id" }` for parts the mod renamed; the old id is the mod's own unless written
  in full.
- Part models are drawn at the size they were made: ship them at AC's scale (metres).
- `work_order.json` — what comes off an engine before what (see below). Most mods need none: the base game's covers
  every kind of engine part there is.
- `"make"` in a `pack.json` (`"make": "gm"`) says which make the pack's parts are for: every part shop specialising in
  that make stocks them (see `partshops` below). The base game's makes are `mopar`, `ford` and `gm` (the families of
  makes.json); a pack of parts for any make (the universal-fit carburettors and air cleaners) has none.
- `part_groups.json` — the shelves (see below). Most mods need none.
- `stand_ins.json` — script classes the mod's parts extend that the source game never shipped, made from the class
  their author copied: `{ "stand_ins": { "<class>": { "copy": "<class copied>", "extends": "<what it extends
  instead>", "drop": [ "<methods left out>" ] } } }`. A later mod's replaces one of the same name. The base game's makes
  the Ford six's `Block_Inline_OHV` out of `Block_Vee_OHV`. A stand-in that copies itself, or two that copy each
  other, is no class (the parts that extend it do not load). The parts converter reads the file from its output
  folder, else from its `--previous` content's.

**The shelves.** Every part is in a group: the shelf it is on in a shop, what the player reads as its kind, and what the
game does with it. A part is in the first group with a class its script descends from, by the end of the class's name
(`"Block"` takes in `Block_Vee_OHV`), the groups in the order listed; a part no group takes in is an accessory. A mod's
`part_groups.json` adds groups, tried before the ones before it, or changes a group of an id already there, naming only
what it changes (a group keeps its place). `never_stacks`: never mounts on its own kind (a head on a head);
`bolt_on`: what the cars on the lots were worked on with; `tune`: how much a rival's tuner goes for it (10 the most,
none: not at all); `speed_shop`: a speed shop sells it. The player reads `part_group.<id>` in the text, else the
group's `name`, else its id. The ids `engine_blocks`, `cylinder_heads`, `camshafts`, `crankshafts`, `connecting_rods`,
`pistons`, `transmissions`, `air_filters`, `exhaust`, `oil_pans`, `tyres`, `rims`, `brakes`, `springs` and `shocks`
are the game's own: the engine's internals wear and break, the running gear has a slot on each wheel, the attendant
looks at the air filter and the exhaust. A mod may change their classes, never their meaning. The base game's groups are
`Content\streetcorsa\parts\part_groups.json`.

```json
{ "groups": [ { "id": "intercoolers", "classes": [ "Intercooler" ], "tune": 4, "speed_shop": true } ] }
```

**The work order.** Every engine part has a *kind*: `"kind": "timing_cover"` on the part in its `pack.json` if the pack
says so, else the first kind of the work orders listing one of its script classes (`"classes": ["Crankshaft"]`), else
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
intake waits for it; the Ford six's sits on the side of its block (`"kind": "side_distributor"` in its pack) and leaves
the intake alone. A part that only comes off another way than its kind stays of its kind, rules and all, and says so
itself: `"comes_off"` on the part in its pack, as for a kind below (the Ford six's intake manifolds have
`"comes_off": [ "out" ]`, its alternator `[ "up" ]`). `"stand_strips"` is what comes off an engine as it goes on the stand (the
radiator, and what is in the way of its bellhousing face), in that order: the garage shows them coming off one after
another before the hoist lifts the engine out. None of them goes back on while the engine is on the stand. A kind's `"comes_off"` is how the garage shows it
coming off, leg by leg, in the engine's own directions: `up`, `down`, `forward` (towards the crankshaft's nose),
`back`, `out` (away from the engine, on the side the part is on); `[ "back", "down" ]` for a gearbox. What is on the
part goes along. A later mod's `"comes_off"` for a kind replaces the earlier one's, and a part's own beats its kind's.
A kind's `"worked_from"`, `"above"` or `"below"`, is the side of the engine it goes on from: picking one on the stand turns
the engine that side up (an oil pan turns it over, a cylinder head back). Only looks; a later mod's replaces the earlier
one's, and a kind without one leaves the engine as it is.

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

**A part's figures.** A part's stats come from its SLRR script (`source_script`): the game builds the script and runs it
to work the engine out. The rest is read from the pack: `"class_chain"` says what the part is (a crankshaft, a clutch:
without it the work order and the engine can't tell), and `"properties"` holds the script's
fields as it built them, which is where the price (`value`), the running gear's figures (tyres, springs, shocks,
brakes), an oil pan's `capacity` and a block's `cylinders` for its sound are read; a part with neither is a bare
fitting with no script of its own, left out of the shops and the running gear. `tools\SlrrPartsConverter` writes
both; a part made by hand copies them from the part whose script it shares.

A part that shares a stock script but has figures of its own says so in `"overrides"`: script fields set over what the
script built, before the player's tuning, and over `"properties"` too, so the price and the running gear get them as
well. A number (`"stroke": 76.2`), one element of an array field (`"ratio[2]": 1.0`), or a list for an array field from
its first element on (`"ratio": [0, 1.76, 1.0, 0, 0, 0, 0, -1.76]`, `null` keeps an element). A list goes on before
the single elements, whatever order they are written in, so `"ratio[3]"` wins over the list's fourth number. Names are
the script's, case and all, and units its own (mm, cc); a field that holds whole numbers takes the whole part of what
it is given. An override the game can't use is reported in the log and left out: a field the script doesn't have, one
that holds something other than numbers, a number for an array field or a list for a number, an element past the
end of the array (and a list's numbers past it), anything not a finite number, and a field the script works out itself
from others (the part's `"derived"`, such as a camshaft's `time_in_open`). The engine takes the bore from the block,
the stroke from the crankshaft, the combustion space from the heads, the deck and the pistons' `crown_volume`, and
the gearing from the transmission (`gears`, `ratio`: element 0 unused, the last one reverse).

```json
{
  "id": "engines/l79/CV8_crank_283", "source_script": "...V8_small_block_crank.class",
  "class_chain": [ "...", "java.game.parts.Part" ], "properties": { "value": 180.0, "stroke": 82.55, "...": "..." },
  "overrides": { "stroke": 76.2, "value": 150 }
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
decides what a part does as much as its script:

- a part whose script class doesn't tell its kind gets it from its name (the work order's `"names"`, above);
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
`"fan"` has its own electric fan. On a part in its `pack.json`, `"performance": true` (or `false`) says whether it is a
performance radiator, water pump or fan, or an oil pan that holds its oil like a race sump. Left out, the words above
decide. The base game declares neither.

```json
{ "kinds": [ { "id": "aux_cooler", "name": "auxiliary cooler", "names": [ "aux cooler" ], "cooling": "radiator" } ] }
```

The base game's reading of every part is held in `tests/StreetCorsa.Tests/Fixtures/part_roles.txt`: a rename that
changes what a part does fails `PartRolesSnapshotTests`.

A part mounts by what its slots name (`attaches_to`, `compatible_with`: a part id and a slot, a reference as above)
or by a standard fitting (`fits`/`takes`, e.g. `carb:4bbl`), which works across mods with no ids at all.
`tools\SlrrPartsConverter` makes packs from SLRR `.rpk` files.

### cars

The makes and the cars: `.json` files in a mod's `cars` folder (the base game's `makes.json` and `cars.json`), merged
**key by key** over the mods before them like the rules: a mod adds a make or a car, or changes one figure of one,
naming only that; a list (a make's words) is taken whole. What does not read is told and left out.

- `makes`, by id: `words` that tell the make in a car's brand, else in its name (any case; two words allowed,
  `"alfa romeo"`; a word is one make's); `family`, the corporate family its cars and engines belong to (`gm`: a Pontiac
  turns up with a Chevrolet V8, never a Hemi; the engine builds' and sounds' families come from the same words), none
  for a make whose words name no engine; `country`, a country's id or name; `price`, what the badge does to a car's
  price (`2.5`: two and a half times); `common`, added to how common its cars are (0 rare to 1 common).
- `countries`, by id: `names` a `ui_car.json` writes for it (`"Great Britain"` is the `UK`) and `regions` it is in
  (`"Europe"`); a region is a country entry too, with its names (`"European"`).
- `words`: `aliases` (a nickname read as the word it stands for, `"vette": "corvette"`) and `noise` (words that say
  nothing about which car or engine it is), for matching a car's name to an engine build's.
- `cars`, by the AC car's folder name: what is given here is not worked out from the car's data. `engine`, the
  engine build it leaves the factory with (`engine_builds.json`, namespaced); `layout`, `front`, `mid` or `rear`;
  `price`, in perfect condition in the game's money; `common`, how often it turns up for sale, 0 to 1; `make`, a make
  of `makes`. The base game's holds the factory engines and prices set by hand in the catalog editor (a developer's
  screen, which saves into the base game's `cars.json`).

```json
{
  "makes": { "datsun": { "family": "nissan", "country": "Japan", "words": [ "datsun" ], "price": 0.9 } },
  "cars": { "ks_nissan_skyline_r34": { "engine": "mymod:engines/rb26/stock", "price": 42000, "common": 0.3 } }
}
```

### sounds

One folder per engine sound (a `.bank`, `GUIDs.txt`, an optional `sound.json`), as the base game's `sounds` (see its
README). The sound's id is its folder's name, namespaced. `sounds.json` pins blocks to sounds:
`{ "pins": { "engines/mine/block": "my_v8" } }` (the block and the sound are references: the mod's own first); its
`cars` facts are about installed AC cars, by car id.

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
the game shows its credits (Settings), as a CC BY licence asks. The published `bushradio` mod has `radio`, the set on the
welder: clicking it opens the radio. `tools\Props` makes a prop's KN5: `prepare_prop.py` takes any model with PBR
materials (a Sketchfab .glb, a .blend) through Blender, `PropBuilder` writes the KN5.

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

`kind` is `specialist` (new parts of every pack whose `"make"` is one of its `makes`, a mod's packs too), `speed` (new
performance parts for any make, or its `makes`: carburettors, blowers, cams, manifolds, heads, headers, nitrous, none that
a car came with), `wheels` (new tyres, rims, brakes, springs and shocks) or `junkyard` (used parts and whole engines;
buys the player's parts for a little more than a trade-in, and can be haggled with, though never down to less than
10% over what it would pay for the part). `groups` narrows a shop to kinds of part, by
the ids of part_groups.json (`"camshafts"`; see "parts"). `priceFactor`, `buyFactor`, `stockLow`/`stockHigh` and `restockDays` are optional
(the kind's own otherwise; see `PartShopRules`). `"startsOpen": false` keeps a shop out of business when a game starts:
the shops come and go as the dealers do. Keep a pin about 0.055 across or 0.035 down from every other shop's pin, or the
name plates overlap; the dealers' pins are on their own map. `kind` is one of the four names; anything else (a number
too) makes a specialist. A make with no specialist of its own is still sold by the speed shops (its hot
parts), the junkyards and the mail order.

### racers

A list under `opponents`, or one racer per file (its id as `opponentId` or `id`):

```json
{ "opponentId": "vera", "name": "Vera Holt", "nickname": "Hurricane", "age": 29, "gender": "Female",
  "skill": 95, "aggression": 80, "portraitPath": "vera.png", "location": "The docks", "biography": "…" }
```

`portraitPath` is next to the file. `skill` is 90 to 100. New racers join games already under way. `"isKing": true`
makes the King; to replace the base game's King, give yours `"replaces": "king"`.

### events

The paper's invitations:

```json
{ "id": "mopar_night", "name": "Mopar Night", "description": "…", "raceType": "DragRace",
  "entryRequirements": { "type": "any", "filters": [ { "type": "brand", "brand": "Dodge" }, { "type": "brand", "brand": "Plymouth" } ] },
  "minReputation": 20, "requiredMilestones": [ "street_cred" ], "isPinkSlip": false, "isBracket": false,
  "reward": { "cash": 800, "reputation": 8, "specialItem": "part:engines/mine/cam", "specialItemDescription": "A hot cam" },
  "schedule": "Weekly" }
```

- `raceType`: `DragRace`, `Circuit`, `Sprint`. `schedule`: `OneTime`, `Daily`, `Weekly`, `Permanent`.
- Car filters (`type`): `brand`, `origin` (a country, or American, European, Japanese...), `years` (`startYear`,
  `endYear`), `power` (`minHP`, `maxHP`), `value` (`minValue`, `maxValue`: what the car is worth), `all` and `any`
  (of `filters`). Left out: any car.
- `specialItem`: `rare_camshaft` (the best camshaft that fits the winner's engine) or `part:<part id>` (the mod's own
  part first).

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
  "goal": [ { "type": "beatKing", "step": "Beat the King" } ],
  "progress": { "achieved": "Done!", "unlocked": "The King is waiting", "locked": "{DragWins}/5 drag wins" } }
```

Step types: `counter` (a trigger, as in milestones, at `atLeast`), `beatKing`, `beatAll` (every racer beaten once),
`mostWins` (more wins than any racer but the King). `{target}`, `{current}`, `{remaining}` fill a step's text;
counters by name (`{TotalWins}`), `{defeated}` and `{total}` fill both steps and progress lines, with a format if
wanted (`{MoneyEarned:N0}`). A goal step is never done while the path is locked. The progress shown is the share of all
steps done, a counter counting by how far along it is. Path ids are kept in saves.

### streets

One folder per street, as the base game's `streets\pretville`: a `street.json` and a model per light of the day
(`street_day.kn5`, `street_dusk.kn5`, `street_night.kn5`). `"times": ["night"]` says when the street is used (left
out: every light it has a model for). The Cruise screen picks a street at random among those that fit the hour, and
keeps it while it still fits. When no street fits the hour, the one in view stays, in the nearest light it has.

### stations

One folder per gas station, as the base game's `stations\tomwixon`: a `station.json` and its model (`"model"`, a KN5
with a sky dome for each light of the day named `STATION_SKY_DAY`, `_DUSK` and `_NIGHT`) and, next to it, the ground's
height (`ground.bin`). `tools/StationScene` writes both from a scene cut out of an AC track (see
[screens/gas-station.md](screens/gas-station.md)). The file gives the way in (`arrive`, off the road to the pump, the car
stopping at the last point) and out (`leave`), as `[x, z]` points in metres; where the driver looks at the pump
(`lookAtPump`, degrees to the left); the lights of the day as a street's; and what the station charges (`prices`:
`fuelPerLitre`, `oilPerLitre`, `wash`, `tow`; left out, the base game's). The garage's Gas Station button drives to one
of them at random. A station without its model still works, without the picture.

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

### showcase and garages

`showcase` lists the main screen's rooms: `{ "scenes": [ { "id": "mygarage", "wallRadius": 12 } ] }`. A room is a
folder of `garages` (a mod's own, `garages\mygarage\mygarage.kn5`; a later mod's room of the same name wins) or an AC
showroom. The garage the player works in is the room `garage`; its `scene.json` names the marks its car scripts drive by
(see scripts).

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
alongside), `street.rival_leave` (the rival away; it is gone when the script ends). A mod may name its own; nothing
in the game sets them off yet.

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
| `release` | the game goes on, the script plays out |
| `end` | the script is over |
| `{ "if": {...}, "then": [...], "else": [...] }` | the steps of `then` when the condition holds, else of `else` |

**Conditions** (`when` and `if`): every key must hold. `engine`: `runs` (the car has an engine that starts), `dead`,
`running` (now), `off`; `towed`: true or false; `light`: `day`, `dusk`, `night`, or a list; `hours`: `[from, to]` on the
game's clock, round midnight when from is the later (`[20, 6]`); `not`: a condition; `any`: a list of conditions.

**Marks and paths.** A drive goes to marks and along paths the scene names. A mark is a point `[x, z]` in metres, or a
point measured from where the car stands, `{ "behind": 7 }` (`ahead`, `left`, `right`); `home` (where the car stands
when nothing moves it) and `here` (where it is now) are on every scene. A point is within 10000 metres of the
scene's middle, a measured one within 1000 of the car, in a scene's file as in a script. Every scene file may name its own:
`"marks": { "kerb": [3, -4.5] }` and `"paths": { "round_back": [[0, 0], [5, 5], [0, 10]] }`, next to what it has.
Besides these, the lot's warehouses have `exit` (their `exit`; without one, straight ahead), the garage has `door`
(from `garages\<id>\scene.json`; without it, 7 metres behind the car), the gas stations have the paths `arrive` and
`leave` and the mark `pump` (the end of `arrive`), and the streets have the paths `arrive` (up the lane to the stop)
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
racer's car can't race today, or the game can't tell), `replies.king.pink_slips` and `replies.king.no_pink_slip` (the
King takes a pink-slip race, or turns down one for money), `replies.rematch` (a racer out for a rematch) and
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
- Talking to people: `chat_seller`, `chat_salesman`, `chat_rival` are sent after the persona as the rest of who the AI
  is, with the talk as the messages; they name `{name}`, `{nickname}`, `{age}`, `{sex}`, `{temperament}`, `{place}`,
  `{player}` and `{facts}` (the game's fact sheet, the text pools `chat.fact.*` and `chat.deal.*`). `chat_memory`
  writes what a person keeps of a talk (`{name}`, `{player}`, `{talk}`). The stage lines, the window's words and the
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
  kind (0 never). `for`: `seller`, `salesman`, `rival` (none: every kind). `weight`: how often. `minAggression`,
  `maxAggression`: a rival's aggression range. Every kind should have one for every aggression, or the person has none.

`ai\ollama.json`: the game's own model in Ollama, made from Settings ("Make the game's model"), merged key by key:
`name`, `from` (the base model), `system` (a text or lines), `parameters` (as a Modelfile's), `examples`
(`{ "player", "answer" }` pairs, its first messages).

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
| `misc/license` | the licence the player signs for a new game (the name box sits where the game's has its line) |
| `misc/calendar/calendar01` .. `calendar12` | the garage calendar's pictures |
| `intro/logo` | the game's logo on the intro, in place of the lettered title (the base game has none) |
| `window/icon` | the window's and the taskbar's picture (the exe's own icon cannot change) |

The window's title is `game.json`'s `title`; the intro's words are the text pools `intro.title` and
`intro.powered_by`; the dealer map's picture is a `maps` entry of `dealers`.
