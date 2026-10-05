# Diner Screen

Roadmap step 28, idea 6. The player drives to the diner, seen from the driver's seat as at the gas station: down Main
Street, into the diner's lot, nose first into a bay facing it, the racers' cars parked along its front. The street is
the screen; the diner (who is in, the word on the street, a racer's options and a word with them) opens in one window
over it. Leaving, the car backs out of its bay and drives off down the street.

```
Garage ──[racing hub: Hit the streets]──▶ Diner: down Main Street, into the bay ── parked: the diner's window on a click
   ▲                                                                                      │
   └──────────── back out, off down the street, the curtain ◀──[garage button]────────────┤
                 (Cruise: the same, then the curb on Main Street) ◀──[CRUISE THE STREETS]─┘
                 (a challenge: straight to the race; back from it, the car stands in its bay)
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
curb, the rivals' lane, the lights, the glow, the credit) and `diner.json` (the diner's: the ways in and out, the bays;
what it leaves out is the street's). About 39 MB, 252k triangles.

**What it is built of** (research 2026-10-03; no free realistic 1960s Main Street exists, so the block is built):
- Quaternius **Downtown City MegaKit**, the free "Standard" set (CC0): the road (four lanes with sidewalks, 6 m pieces),
  the asphalt of the lot, and three ready buildings (5 to 8 storeys of brick, 12 to 20 m wide, fronts on one side,
  plain brick sides and backs, so they stand in rows), repeated along both sides, round the end of the street and in a
  back row behind the diner: downtown, x −62 to 76. Their fake rooms behind the windows are flat pictures (their engine's parallax shader is
  not in the free set); the windows' glass is left out so the rooms show.
- WaajaaH's **Diner Exterior Asset Reworked** (CC BY 4.0, credited in `street.json`): a streamline moderne corner
  building, a pink band of coloured glass over a recessed ground floor, at 13.5 times its file's size (its mannequin,
  1.8 m tall, stands 0.133 high).
- Our own signs (`tools/DinerScene/prepare_pictures.py`): DINER on the roof, EAT / OPEN 24 HOURS on a pole at the lot's
  way in, and the bay lines.
- Our own **houses** beyond downtown (the owner, after a first look: "too many buildings in the town, I'd like more
  houses"), modelled by `compose_street.py` (`House`, `ranch`, `two_storey`): a ranch long side to the street with a
  picture window, or a two-storey with its gable to the street and a porch; clapboard in four paints (our own picture),
  shingle roofs (Poly Haven's `roof_slates_03`, darkened), framed windows, a door, a chimney, on a 16 m lot with a lawn
  (Poly Haven's `leafy_grass`, turned green), a drive, a walk and a hedge. Both sides from downtown out to the street's
  ends, and a row across the west end, about 25 houses.
- The skies are the gas station's (Poly Haven, CC0).

The "corner auto parts store" download was left out: an untextured generated kiosk that fit nothing.

**The layout** (game metres; x along Main Street, z across it, the diner's side towards −z; the origin is the middle of
the street in front of the lot): the road is 12 m kerb to kerb (a parking lane and a lane each way), sidewalks to ±9 m;
the street runs from x = −120 (a cross street and a row of buildings across it) to x = 144. The lot is x −18…18, z −9…−39;
the diner's front (its overhang) at z = −39, its roof sign over it. Two rows of bays, lines every 3 m from x = −15 to 15,
5 m deep: the racers' along the diner's front (z −34…−39), the player's from z = −15 to −20 with a 6 m aisle before it.
The neighbours' side walls flank the lot, built out from it.

**Building it** (`tools/DinerScene/build_street.py <folder with the two zips> [work folder]`, which runs the rest):
1. `prepare_pictures.py` draws the signs and the siding, and fetches the roofs' and lawns' pictures.
2. `compose_street.py` (Blender, `-b`) lays the street out and saves `street.blend` (open it to look). Every kit piece is
   imported once and copied; the copies of the kit's materials and pictures each import brings (`.001`) are made one; a
   building's flat faces are merged (Decimate, planar, 1°, by material and UV: half its triangles, nothing seen changes)
   and one more than 45 m from the diner loses more (collapse to 45%). The kit's road sits 0.15 under its sidewalks:
   everything is lifted so the road is at 0. A ground plane under it all keeps the void out of the gaps.
3. `tools/Props/prepare_prop.py <blend> street <out> 2048 --jpg --keep-names` makes the OBJ and AC's pictures (see
   below).
4. `DinerSceneBuilder` writes the KN5 (the shared `ObjModel` reading the OBJ in place, `--sky` the domes), and
   `ground.bin` from the street's upward faces (the shared `Ground`, as the gas station's). The street's
   materials are lit more by the sun than a prop's (`ksAmbient` 0.2, `ksDiffuse` 0.75): with a prop's balance the ambient
   light drowned the cars' shadows on the asphalt.

**Hard-won facts:**
- The kit's glTF materials multiply their base picture by the vertex colours (a wear mask for their own shader, mostly
  white): `prepare_prop.py` now follows a picture through a multiply (and a channel split: roughness is the green of the
  packed occlusion-roughness-metal picture, metal the blue). The kit's normal maps are OpenGL's: compose renames them
  `*_gl` so prepare turns them over.
- A blended material with no picture to cut it by (the kit's glass) is left out as glass; a picture with alpha that cuts
  its shape (the road markings) becomes an alpha-tested material (`alphaTest` in materials.json, `AlphaTested` in the
  KN5, which the renderer honours).
- `--keep-names` names the KN5's materials as Blender does (`sign_roof`, `Glass1`, `MI_FakeInterior_1`) so a scene's
  file can name them: `street.json`'s `glow` makes them glow by light of the day (`SceneDressing.Glow`).
- Parenting: deleting an FBX's empties before joining its meshes threw away their scale (the diner came out 150 m wide);
  every object is put where its parents put it first.
- Glob reads `[Standard]` in the kit's folder name as a pattern: the tools walk folders instead.
- The harness's machine reads `1.5` as 15 in its own culture: parse numbers with the invariant culture.
- The first layout had the player 5 m from the diner, looking at a grey wall: the lot is 30 m deep so the diner, the
  racers' cars and the back row fill the windscreen; the driver looks up 12° in the bay (`lookUpWhileParked`), the sign
  above the windscreen's top otherwise.

## The viewport (`Controls/DinerViewport3D.cs`)

The gas station's viewport became `DriveInViewport3D`, the base of both: the place (`IDriveInScene`: model, ground,
sky domes, lights, glow, marks, where the car stands and where the driver looks), the player's car driven by its
scripts, its body, wheels, gauges and steering wheel, the driver's eyes, a drag to look round. The screen and the
viewport share a `DriveInStage` (the gas station's `StationStage`, renamed): Waiting, Arriving, Standing, Leaving, Gone.
`StationViewport3D` and `DinerViewport3D` only name their scripts (the station rings its bell when an arrival has none).

- **Scripts** (`scripts/scripts.json`): `diner.arrive` (down the street with `road`, into the bay, lights off),
  `diner.parked` (none in the base game), `diner.leave` (out of the bay backwards along `back_out` with the new
  `back_out` feel, a walking pace in first, then `leave` with `road`, the screen going 1.2 s before it ends).
- **The racers' cars** (`ParkedCars`): one a bay in `diner.json`'s order, loaded after the player's car (the arrival does
  not wait on them), 900 MB of car files at most (a bigger one is left out and logged). Their lights are not loaded: at
  night the renderer read and guessed every car's lights, which held the UI thread for over a minute, and the arrival's
  30 s limit fired and skipped the drive.
- The driver looks ahead and 12° up in the bay; while driving, as the station's (into a bend).
- **Shadows**: the renderer centres its sun shadows on the main car slot's box (sharp to about 10 m, gone past about
  23 m) and keeps the box from where the car loaded. `DriveInViewport3D.FocusShadows` tells it again where the car is
  as it moves; the diner's widens it to take in the bays while the car is near the lot, so the racers' cars, 20 m and
  more from the player's bay, have shadows. The box is a private field of actools' `CarSlot`, set by reflection
  (`ShadowsPosition` is not virtual); without the field the shadows are only the player's.

## The screen (`Screens/Diner/`)

- **On the way in**: as before, a car that could not have got here goes home (`StreetReady`), then the racers and tracks
  load, then the street: the screen waits for the scene (25 s at most, not at all when 3D cannot show), the curtain
  lifts and the car comes down the street with its engine running; in the bay the engine stops and the diner opens for
  business: its name and how many racers are in (top), the garage button and CRUISE THE STREETS (top left), **Into the
  diner** (bottom). Then the half hour of the visit is spent (late in the evening, the next morning: the racers change).
- **The diner's window** (`IsDinerOpen`): the racers on the left, the word on the street beside them; a racer clicked, the
  same window shows their card, record, car, the races and the stakes, and Talk; the back arrow returns to everyone, the
  cross to the street. Nobody is picked when the player comes in (the window opens on everyone).
- **Leaving**: the garage button or Cruise starts the engine (`DrivingHome.StartUpAsync`, shared with the station), the
  car backs out and drives off, the curtain comes down and the player is home (the car drives into the garage, its engine
  handed over) or at the curb. A way out that goes nowhere leaves the car back in its bay.
- **Back from a race** the car stands in its bay already (`DriveInStage.StandThere`), engine off.
- No 3D (or no street model): the diner's old picture behind the same buttons, the car simply there.
- The ambience is the street's now (outside, in the car).

## Harnesses (session scratchpad)

- `dinershot`: the street alone in the renderer, from given eyes (`top` looks straight down); `drawpath.py` draws
  `diner.json`'s ways over the top view.
- `dinerscreen`: the real game (App, a copy of the save) to the diner at an hour, shots on the way in, the window, a
  racer, the way out; `cruise` the Cruise screen. It reads the scripted car and the script run by reflection.
