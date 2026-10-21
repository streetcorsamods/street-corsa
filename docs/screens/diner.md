# Diner Screen

Roadmap step 28, idea 6, reworked in step 37 as late-1960s Los Angeles. The player drives to the diner, seen from the
driver's seat as at the gas station: down Main Street, through the crossing and into the parking lane across the street
from the diner, at the curb, looking over at it out of the side window as in Street Rod; the racers' cars stand in its
lot along its front and its side. The street is the screen; the diner (who is in, the word on the street, a racer's
options and a word with them) opens in one window over it. Leaving, the car backs up a length along the curb, pulls out
and drives off down the street.

```
Garage ──[racing hub: Hit the streets]──▶ Diners' map ──[a diner]──▶ Diner: down Main Street, to the curb ── parked: the diner's window on a click
   ▲                                                                                      │
   └──────────── back out, off down the street, the curtain ◀──[garage button]────────────┤
                 (Cruise: the same, then the curb on Main Street) ◀──[CRUISE THE STREETS]─┘
                 (a challenge: straight to the race; back from it, the car stands at the curb)
```

**The owner's brief (docs/ideas.txt, batch 8, idea 6):** "The street dome looks bad. I want a full model ... some
american city of the 60s or 70s ... If you can't find anything, build one basic model. Build a cinematic for arriving and
leaving the diner, just like the gas station does. The streets scene is the main screen. Don't show any data by default.
Add a toggle button for opening a modal window with the rivals list and the word on the streets. Switch to the rival
options and chat when clicking them inside the same modal window."

So the street replaces the Cruise screen's Pretville dome as well: **Main Street** (`streets/mainstreet`) is the street
the Cruise screen is set in, and the diner is on it. Pretville left the base game (the tools that made it,
`tools/StreetScene`, stay for a mod's panorama street).

## The street

`Content/streetcorsa/streets/mainstreet/`: `street.kn5` (one model for every light of the day, a sky dome each:
`STREET_SKY_DAY`, `_DUSK`, `_NIGHT`), `ground.bin`, `street.json` (the Cruise screen's: where the player parks at the
curb, the rivals' lane, the lights, the glow, the credit), `diner.json` (the diner's: the ways in and out, the bays;
what it leaves out is the street's) and `CREDITS.txt` (every model, its page and what was changed). About 87 MB, 424k
triangles in 40 m tiles, the four diners' looks' 75k among them (below).

**The owner's brief (step 37, `docs/ideas.txt`):** "Let's make it look like the real Los Angeles from the seventies. Add
trees, parked cars, electric poles, trash cans, fire hydrants ... The diner should be at the corner, with the parked cars
at the front ... Maybe in both fronts. The player parks across the street and looks at the diner, like in the original
Street Rod." The era settled on was the late 1960s to early 1970s, so the street sits with the game's cars.

**The layout** (game metres; x along Main Street, z across it, the diner's side towards -z; the origin is the middle of
Main Street in front of the diner): Main Street is 12 m kerb to kerb (a parking lane and a lane each way), sidewalks to
±9 m, from x = -165 to 165; a cross street of the same crosses it at x = -36 (the kit's four-way crossing, its crosswalks
painted). The diner stands back on the north-east corner, turned so its long glass side faces Main Street (its front at
z = -15) and its door the cross street (its west side at x = -15); its lot wraps round both, from the cross street's
sidewalk (x = -27) to x = 20 and back to z = -44. The racers' bays: nine along its front, just behind the sidewalk so
their cars are seen close from the player's curb (5 m deep, z -9.5 to -14.5, lines every 3 m from x = -15 to 12), and
four along its west side (x -15.5 to -20.5), all facing it. (The first build had the front at z = -21: the owner found
the diner too far from the street.) The player's car
stands at the curb across Main Street at (-2, 4.5), facing east: the diner is out of the driver's window
(`lookWhileParked` 82, `lookUpWhileParked` 12). The Cruise screen's curb is where it was (35, -4.5, facing west).

**What it is built of** (the search: `reports/Diner street models.md`; every Sketchfab model is optional to the build,
which leaves out one not downloaded and says so):
- The road, the sidewalks, the crossing and the lot: the Quaternius **Downtown City MegaKit** (CC0). Its brick blocks
  are gone (not LA).
- The diner: WaajaaH's **Diner Exterior Asset Reworked** (CC BY 4.0), 13.5 times its file's size, turned a quarter.
  DINER on the roof is our own board; A9908244's Googie **Diner Sign** pylon stands on the lot's corner, its neon glowing
  at dusk and night. No Googie coffee shop of the Johnie's or Norms kind exists under a licence we can ship: a building of
  our own is the next step if the owner wants one.
- The shops downtown, both sides of both streets: Daniel Zhabotinsky's **Midcity Buildings Set** (low stucco and brick,
  their fronts to the street; the rounded corner block across the cross street), Wardster's **Rentable Stores** across
  from the diner, Raphael Escamilla's **Harbor Breakfast Building** (its mural faces the diner's lot; its sign, ground
  and furniture left out) and Orzechu's brick block east of the lot.
- Houses beyond downtown and along the cross street: a third two-storey (ours, `two_storey`), the rest ranch houses,
  every other one of those AleixoAlonso's **Fleetwood** (a real 1950s National Homes plan; its inside left out, its
  glass ours, its siding in four colours, `tinted()`), the rest ours (`ranch`, narrower than before, they crowded the
  next lot), shutters on our front windows. Every lawn has a tree (Poly Haven's jacaranda, island tree or small tree, or
  Valery.Li's **Chinese Banyan**, the LA ficus, all as cards, a shade under each), bushes (Poly Haven's searsia) along
  the front and gazania and periwinkle in front of them; a hedge on half of them. The rows closing the streets' ends
  are ours only (`plain`).
- Palms along the sidewalks downtown and four behind the diner (Next Spring's **Realistic Palm Tree 4**, 17 m, whole:
  the model within 45 m of the player's curb, a card of it beyond), a fan palm in the lot's corner bed. The first build
  cut the palm to 2500 triangles and Murrugarra's Washingtonia from 200k to 6000: both fell apart (torn fronds, holes in
  the trunks), and the Washingtonia is gone.
- Streetlights & ETC's **Caltrans** cobra heads along Main Street and their **utility pole** lights along the cross
  street; Caboose3d's **telephone poles** behind the south sidewalk and along the cross street, our own wires between
  them; Signalrenders' **Econolite** signals on the crossing's corners.
- Hydrants at the kerb (Raphael Escamilla's; the one by the player's curb stood in the middle of the sidewalk) and trash cans (his too), the corner mailbox (Yaroslav Dubovikov), news racks (Caboose3d), a bus
  bench (Derfmode), parking meters (out of the Harbor Breakfast model) along both curbs downtown; exiS7-Gs' Googie star
  sign across the cross street; TampaJoey's billboard on the vacant corner lot with our own ad (`prepare_pictures.py`'s
  "Golden State Motor Oil": the model's was today's real estate).
- Seventeen cars at the curbs, clear of the player's and the Cruise screen's spots and of the ways in and out: Main
  Street's parking lanes and two along the diner's side on the cross street (roh3d's Chrysler Saratoga and Cadillac 75,
  Zhabotinsky's made-up Palomino, Phoenix, Chapman, Sialia, Duntov and Bokaroo, robinmikart's Chevelle and '53 sedan,
  Escamilla's 1970 truck, and the gas station's six from its folder); 8000 triangles each within 45 m of the player's
  curb, 5000 beyond.
- Everything casts its shadow now (`DinerSceneBuilder` passes `castShadows`), the ground (the kit's `MI_` road and
  sidewalks, the lot, the dirt and the lawns) excepted; cut-outs (the palms' fronds) never do.

**Building it** (`tools/DinerScene/build_street.py <folder> [<folder> ...] [--work <dir>] [--turn <car>=<degrees>]
[--blend-only] [--bake]`): the folders are searched for every zip (the base game's: `D:/JUEGOS/3D Models/scenes`,
`.../diner`, `.../station`).
1. `prepare_pictures.py` draws the roof sign, the billboard's ad and the siding, and fetches the roofs' and lawns'
   pictures (its own `poly_haven` once shadowed the shared one it calls, and recursed forever: now
   `poly_haven_pictures`). The Poly Haven plants are downloaded and `tools/StationScene/bake_cards.py` takes the cards
   (`CARDS`: the lawns' trees, bushes and flowers, the far palm), kept in the work folder's `cards` (`--bake` takes
   them again).
2. `compose_street.py` (Blender, `-b`, from a `spec.json`) lays the street out and saves `street.blend` (open it to
   look); `--blend-only` stops there. Each Sketchfab model is a `piece()`: its materials named after its id (so two
   models' "Body" never merge, and `prepare_prop.py`'s `--cap` holds its pictures smaller), scaled to a height or a
   length, brought down to a triangle budget, stood on its foot. It writes `street_built.json` (the lamps for
   `street.json`, the cars used, the models left out).
3. `tools/Props/prepare_prop.py <blend> street <out> 2048 --jpg --keep-names --cap <id>_=<px> ...` (each model's cap is
   in `build_street.py`'s `MODELS`).
4. `DinerSceneBuilder` writes the KN5 and `ground.bin`: the scene's triangles in 40 m tiles, a node each
   (`PreparedScene.Tile`, `ObjModel.ByTile`).

**Hard-won facts:**
- The KN5's size is mostly its vertices: 592k triangles came to 96 MB (over GitHub's 100 MB a file on disk); the props'
  budgets brought it to 393k and 81 MB. 55 palms at 5k each were the most of it: palms stand downtown only.
- The renderer culls nodes, never meshes: one mesh a material across the whole street (as `ObjModel` wrote it) was
  drawn whole wherever the camera looked, and into the sun's shadows every frame the car moved. In 40 m tiles the
  frames over 16 ms while the shadows are drawn again every frame came from 22 in 1,800 to 4, the 99th percentile
  from ~15 ms to ~5.5 ms, the median 0.4 ms up (more draw calls). The gas station gets the same on its next build.
- A collapse decimation tears a cut-out's cards (a palm's fronds) and opens holes in what is left: a palm is kept whole
  near the player and made a card further off.
- Sketchfab units vary: the Caltrans light is in inches, the utility pole in millimetres, the Harbor building in
  centimetres, the mailbox and news racks three times too big: everything is scaled to a height or a length.
- The kit's glTF materials multiply their base picture by the vertex colours; its normal maps are OpenGL's (`*_gl`).
- `--keep-names` names the KN5's materials as Blender does (`sign_roof`, `Glass1`, `diner_sign_Pink_neon`) so
  `street.json`'s `glow` can name them.
- Parenting: every object is put where its parents put it before they are deleted (an FBX's empties carry scale).
- Glob reads `[Standard]` in the kit's folder name as a pattern: the tools walk folders, and `build_street.py` escapes
  the folders it globs in.

## The street kit (step 39, 2026-10-21)

Main Street is laid out by each save now: `street.kn5` is only the **stage** (the kit's road and sidewalks, the crossing,
the lot and its bays, the diner, the telephone wires, the dirt under it all, the skies; 13.7 MB, most of it the diner's
and the road's 2048 pictures, the ground and the ways as they were), and everything else stands on it as **pieces** of
`Content/streetcorsa/streetkit` (82: the 13 shops, 16 houses baked with their lots and gardens, 16 curb cars, the palms
and the palm card, the lamp posts, poles, signal, signs, furniture, and since phase 4 the diners' looks: see below).
street.json's `layout` is the recipe (see [modding.md](../modding.md), streets);
`StreetLayouts.Laid` lays the street out for the save the first time it is seen and keeps it in `GameState.Streets`
(with the recipe's fingerprint: another recipe lays it out again from the same seed), then the viewport
(`D3DViewportBase.DressSceneAsync`: the diner, the Cruise screen, a seller's curb) reads each piece's model off the UI
thread (`StreetPieceModels`, which keeps the last scene's models for the next) and sets them into the renderer as its
dressing, which loads a model's materials and textures once for all its places (300 places of
a dozen models: half a second and 350 MB, against 8.7 s and 5.5 GB with a renderable's own each). The layout's lamp posts
are lit at night at their arms' ends (piece.json's `lamp`), recoloured by a diner's look as the street's own lamps; the
pieces' self-lit materials glow as street.json's `glow` says (`GarageRenderer.GlowDressing`); a piece's `SWAY_` nodes lean
in the wind as the scene's do.

**Building the kit** (`build_street.py ... --kit`): compose_street.py in split mode puts each library piece it would have
placed into the layout instead (`street_built.json`'s `layout`, the game's space) and saves the pieces, each once at the
origin as `PIECE_<id>`, in `kit.blend`; houses are baked as 12 variants (house n is variant n % 12: its kind and siding
kept) plus the far rows' plain ranch ones, their gardens in them; the lamp posts are turned so their arms reach out over
+z. prepare_prop.py exports each file's scene only (a mesh kept in a file by nothing in its scene brings no material);
`DinerSceneBuilder <prepared_kit> kit <streetkit> --kit` (`PreparedKit`) writes a KN5 a piece with only its own
materials; build_street.py writes each `piece.json` (footprint, height, tags from `TAGS`, lamp, credit from `MODELS`).
The stage goes to the work folder's `stage\` (copy `street.kn5` and `ground.bin` into the street's folder); the work
folder's `layout.json` is today's street as a layout, the proof the split lost nothing (render it over the stage).
Without `--kit` the build makes the old one-model street as before.

**The diners' looks as pieces (phase 4, 2026-10-21).** In split mode a look's `put()` (a `LOOK_` name) goes into that
diner's layout (`street_built.json`'s `look_layouts`) instead of the stage, and the look's own geometry is joined into
one piece a group where compose built it, its footprint's middle at the origin: `sign_roof` (Main Street's) and
`sign_roof_<diner>`, `hiway_drums`, `hiway_fence` (with its posts), `surfside_pennants` (with their poles),
`surfside_surfboards`, `starlite_neon`; the billboards are `billboard_ad` and `billboard_ad_<diner>`, the Hi-Way's
`wrecker`, `heavy_tow_truck`, `air_meter`, `soda_machine` and `barrier` pieces too. Each keeps its materials' names, so
the looks' glow goes by them as before. build_street.py writes the work folder's `look_layouts.json`, each look as the
spots of a `look.layout`, which went into `diners/diners.json` once (with each upper diner's `weights`: `industrial`,
`beach`, `googie`, tags `TAGS` gives the brick shop and the pickups, the palms and beach cars, the signs and big cars).
`StreetLayouts` lays the street out per diner (`StreetLayoutState.Diner`, the seed of the street and the diner), so the
four corners differ in their shops and curb cars as well as their lots; a curb of Main Street out of no diner wears the
beginners' diner's (`DinerBook.WornBy`). The stage went from 26 MB to 13.7 MB, the kit from 84 MB to 91 MB.

## The diners' looks (step 37, phase 4, 2026-10-20; pieces of the kit since step 39's phase 4)

The four diners share this one corner, each dressed its own way, instead of three more streets: the owner's ask, seeing
the four look alike. A **look group** is every object named `LOOK_<DINER>_...` in `compose_street.py`; `PreparedScene`
keeps each under a node of its own (as it keeps what sways), casting its shadow, no part of the ground; the game shows
the groups the scene names and hides the rest (`SceneDressing.ShowLooks`, `SceneLooks.Prefix`): the diner's own
(`diners.json`'s `look.show`, else the street's) on the diner screen and on the Cruise screen when the player drove out of
it (`DinerBook.DressedStreet`), `street.json`'s `looks` else and at a seller's curb. A look's `sun`, `lights` (the street's lamps kept and recoloured by `lampColor`, its own
lamps added) and `glow` (by material, over the street's) go with it (`DinerBook.Dressed`). What each adds:
- **Main Street** (4k triangles): the DINER roof board and the Golden State Motor Oil billboard, as before.
- **The Hi-Way Grill** (31k): HI-WAY GRILL on the roof, the Roadking Tires billboard, the station's wrecker and heavy
  tow truck in the back of the lot, oil drums behind the diner, the air meter and the soda machine by the side door, a
  chain-link fence (our own, a cut-out) along the lot's back with posts, TampaJoey's barriers at its east edge; by day
  a lower, duller sun, at dusk and night sodium-orange lamps.
- **Surfside Coffee Shop** (14k): SURFSIDE on the roof, the Pacific Surf Wax billboard, a string of pennants (ours, a
  cut-out) over the front bays and down the lot's side on three poles, three surfboards (ours) against the side wall,
  the Duntov and the Chevelle in the back, three more palm cards behind; a high bluer sun, a brighter day, white lamps.
- **The Starlite** (26k): STARLITE on the roof, the lounge's billboard, pink and blue neon tubes (ours) along the roof's
  front and side edges glowing at dusk and night, exiS7-Gs' star sign again on the lot's east corner, the Phoenix, the
  Chapman and the Cadillac in the back; at night a pink and a blue lamp over the lot, every lamp tinted, the pylon's
  neon brighter.
The roof boards are `prepare_pictures.py`'s `ROOF_SIGNS`, the billboards its `billboard_*`, the fence and pennants its
`chainlink.png` and `pennants.png`. Hidden geometry costs nothing to draw; the file grew 6 MB. The lot behind the diner
(y 30 to 44) is free of the racers' bays: that is where a look's vehicles stand, over each other, one look at a time.
Since step 39's phase 4 the base model has no `LOOK_` groups: each look is its pieces in the diner's `look.layout` (see
"The street kit" above); `SceneLooks` and `look.show` stay for a mod's model.

## The viewport (`Controls/DinerViewport3D.cs`)


The gas station's viewport became `DriveInViewport3D`, the base of both: the place (`IDriveInScene`: model, ground,
sky domes, lights, glow, marks, where the car stands and where the driver looks), the player's car driven by its
scripts, its body, wheels, gauges and steering wheel, the driver's eyes, a drag to look round. The screen and the
viewport share a `DriveInStage` (the gas station's `StationStage`, renamed): Waiting, Arriving, Standing, Leaving, Gone.
`StationViewport3D` and `DinerViewport3D` only name their scripts (the station rings its bell when an arrival has none).

- **Scripts** (`scripts/scripts.json`): `diner.arrive` (down the street with `road`, to the curb, lights off),
  `diner.parked` (none in the base game), `diner.leave` (a length back along the curb, `back_out`, with the
  `back_out` feel, a walking pace in first, then `leave` with `road`, the screen going 1.2 s before it ends).
- **The racers' cars** (`ParkedCars`): one a bay in `diner.json`'s order, loaded after the player's car (the arrival does
  not wait on them), 900 MB of car files at most (a bigger one is left out and logged). Their lights are not loaded: at
  night the renderer read and guessed every car's lights, which held the UI thread for over a minute, and the arrival's
  30 s limit fired and skipped the drive.
- The driver looks out of the side window at the diner and 12° up at the curb; while driving, as the station's (into a bend).
- **Shadows**: the renderer centres its sun shadows on the main car slot's box (sharp to about 10 m, gone past about
  23 m) and keeps the box from where the car loaded. `DriveInViewport3D.FocusShadows` tells it again where the car is
  as it moves; while the car is near the lot the diner's centres them between it and the bays with a car in them
  (`GarageRenderer.ShadowsCentre`, through the renderer's virtual `UpdateShadows`), so the racers' cars, 20 m and more
  from the player's bay, have shadows. The main car's box stays the car's: its reflections and its light probe are
  worked out from it. The base renders the shadows again whenever the box's middle is not where they were last
  centred, so the override tells it (actools' private `_previousShadowsTarget`, by reflection) the car's middle after
  drawing them; without the field the shadows are only the player's.
- The racers' cars keep a slot per bay: a new day's racers reuse them, and a car already in its bay stays there.

## The screen (`Screens/Diner/`)

- **On the way in**: as before, a car that could not have got here goes home (`StreetReady`), then the racers and tracks
  load, then the street: the screen waits for the scene (25 s at most, not at all when 3D cannot show), the curtain
  lifts and the car comes down the street with its engine running; at the curb the engine stops and the diner opens for
  business: its name, its class and how many racers are in (top), the garage button and CRUISE THE STREETS (top left), **Into the
  diner** (bottom). Then the half hour of the visit is spent (late in the evening, the next morning: the racers change).
- **Which diner** (step 37, phase 3): the map's pick (`FlowArgs.DinerId`, `DinerBook.GetOrFirst`; the Other diners
  button drives off to the map again, and Back there drives home), its scene the
  street its diners.json entry names, else the first diner's. Who is in are its regulars (`RacersOut.AtDiner`: the
  racers out whose car's power puts them in its class, the bosses at the top one). A player whose car is below the class
  is **snubbed** (`IsSnubbed`): the challenge button is off, every card wears "Won't race you", the note under the
  stakes says the class, and a racer picked answers from `replies.snub` instead of the AI's greeting. The stakes of the
  place multiply the house limits on the cash bets (`MatchupCalculator.WagerLimits`'s `stakes`).
- **The diner's window** (`IsDinerOpen`): the racers on the left, the word on the street beside them; a racer clicked, the
  same window shows their card, record, car, the races and the stakes, and Talk; the back arrow returns to everyone, the
  cross to the street. Nobody is picked when the player comes in (the window opens on everyone).
- **Leaving**: the garage button or Cruise starts the engine (`DrivingHome.StartUpAsync`, shared with the station), the
  car backs out and drives off, the curtain comes down and the player is home (the car drives into the garage, its engine
  handed over) or at the curb. A way out that goes nowhere leaves the car back at the curb.
- **Back from a race** the car stands at the curb already (`DriveInStage.StandThere`), engine off.
- No 3D (or no street model): the diner's old picture behind the same buttons, the car simply there.
- The ambience is the street's now (outside, in the car).

## Harnesses (session scratchpad)

- `dinershot`: the street alone in the renderer, from given eyes (`top` looks straight down); `drawpath.py` draws
  `diner.json`'s ways over the top view.
- `dinerscreen`: the real game (App, a copy of the save) to the diner at an hour, shots on the way in, the window, a
  racer, the way out; `cruise` the Cruise screen. It reads the scripted car and the script run by reflection.
