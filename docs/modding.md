# Modding Street Corsa

Everything the game knows beyond Assetto Corsa's cars and tracks is **content in mods**: parts, engine sounds, dealers,
racers, events, milestones, victory paths, streets, the main screen's rooms, garages and the words the game says. The
base game is itself a mod, `Content\streetcorsa`, loaded through the same loader as any other, first.

Cars and tracks stay AC's: install them in AC's `content` folder as usual.

## A mod

Drop a folder into `Content`, next to the game's exe:

```
Content\
  streetcorsa\          the base game (loaded first)
  mymod\
    mod.json
    parts\  sounds\  dealers\  racers\  events\  milestones\  victories\  streets\  showcase\  garages\  text\
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
- `model_scales.json` — `{ "id prefix": scale }` for the mod's own parts (by the ids its packs give them). Ship parts
  at AC's scale and you need none.

A part mounts by what its slots name (`attaches_to`, `compatible_with`: a part id and a slot, a reference as above)
or by a standard fitting (`fits`/`takes`, e.g. `carb:4bbl`), which works across mods with no ids at all.
`tools\SlrrPartsConverter` makes packs from SLRR `.rpk` files.

### sounds

One folder per engine sound (a `.bank`, `GUIDs.txt`, an optional `sound.json`), as the base game's `sounds` (see its
README). The sound's id is its folder's name, namespaced. `sounds.json` pins blocks to sounds:
`{ "pins": { "engines/mine/block": "my_v8" } }` (the block and the sound are references: the mod's own first); its
`cars` facts are about installed AC cars, by car id.

### dealers

Lists of `dealers` and of `showrooms`:

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

Showrooms are only read from a `showrooms` list; a file that is one entry, or a bare list, is dealers.
`mapX`/`mapY` are fractions of the dealer map picture (see the base game's `dealers.json` for how they are measured).
`showroomId` is a room by its folder name: a mod's `garages` folder, else an AC showroom. A save that never had a
dealer gets its lot stocked the first time the map is opened; the other lots stay as they are.

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
`names.json`) list every key and, in their `_comment`, the placeholders each takes. When to say what stays in the game.
