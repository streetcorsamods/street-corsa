# Modding Street Corsa

Everything the game knows beyond Assetto Corsa's cars and tracks is **content in mods**: parts, engine sounds, dealers,
racers, events, milestones, victory paths, streets, gas stations, the main screen's rooms, garages and the words the game says. The
base game is itself a mod, `Content\streetcorsa`, loaded through the same loader as any other, first.

Cars and tracks stay AC's: install them in AC's `content` folder as usual.

## A mod

Drop a folder into `Content`, next to the game's exe:

```
Content\
  streetcorsa\          the base game (loaded first)
  mymod\
    mod.json
    parts\  sounds\  dealers\  partshops\  racers\  events\  milestones\  victories\  streets\  stations\  showcase\  garages\  text\
```

`mod.json`:

```json
{
  "id": "mymod",
  "name": "My Mod",
  "version": "1.0",
  "author": "you",
  "description": "What it adds",
  "order": 0
}
```

- `id`: lower case letters, digits, `_` and `-`, at most 40; not `streetcorsa`, not `car`. Left out, it is the folder's
  name. Two folders with the same id: the first (by folder name) loads, the second is left out.
- `order`: mods load after the base game, lowest `order` first, then by id. A later mod's entry wins where entries
  merge (a sound pin, a showroom's measurements, a scene's wall radius).
- A folder whose name starts with `_` or `.` is switched off.
- Only the folders a mod has are read; every one is optional.

What could not be read is written to the log (`%AppData%\StreetCorsa\Logs`), one line each, and the rest of the mod
loads. Start-up logs the mods it found: `Content: streetcorsa 0.1.0, mymod 1.0`.

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
  that make stocks them (see `partshops` below). The base game's makes are `mopar`, `ford` and `gm`; a pack of parts
  for any make (the universal-fit carburettors and air cleaners) has none.

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

A part mounts by what its slots name (`attaches_to`, `compatible_with`: a part id and a slot, a reference as above)
or by a standard fitting (`fits`/`takes`, e.g. `carb:4bbl`), which works across mods with no ids at all.
`tools\SlrrPartsConverter` makes packs from SLRR `.rpk` files.

### sounds

One folder per engine sound (a `.bank`, `GUIDs.txt`, an optional `sound.json`), as the base game's `sounds` (see its
README). The sound's id is its folder's name, namespaced. `sounds.json` pins blocks to sounds:
`{ "pins": { "engines/mine/block": "my_v8" } }` (the block and the sound are references: the mod's own first); its
`cars` facts are about installed AC cars, by car id.

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
10% over what it would pay for the part). `groups` narrows a shop to kinds of part as
the shops list them (`"Camshafts"`). `priceFactor`, `buyFactor`, `stockLow`/`stockHigh` and `restockDays` are optional
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

### showcase and garages

`showcase` lists the main screen's rooms: `{ "scenes": [ { "id": "mygarage", "wallRadius": 12 } ] }`. A room is a
folder of `garages` (a mod's own, `garages\mygarage\mygarage.kn5`; a later mod's room of the same name wins) or an AC
showroom. The garage the player works in is the room `garage`.

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
`station.json`, `names.json`) list every key and, in their `_comment`, the placeholders each takes. When to say what stays in the game.
