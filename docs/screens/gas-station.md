# Gas Station Screen

Roadmap step 19. As in Street Rod, the player drives to the gas station: from the garage, seen from the driver's seat,
the car comes up the road, turns in and stops at the pump. The attendant fills the tank, tops up the oil and washes the
car, and while he's at it says what he noticed about it. Then the player drives off, and is home.

```
Garage ──[GAS STATION]──▶ GasStation: the drive in ── at the pump: jobs, the attendant ──[DRIVE OFF]── the drive out ──▶ Garage
                              └─ the tank is dry: the tow truck brings the car in, it is at the pump from the start
```

**The user decided (2026-09-26/27):**
- filling up only at the station (the garage's fill-up went); the washes moved there too;
- a car too dry to get there is towed in; the garage sells a can of gas (`RepairShop.CanOfGas`, 4 l) to get it there
  instead (review fix, 2026-09-27: without it a player who couldn't pay the tow could not refuel anywhere);
- the station sells oil too, and gives plausible hints about the car's condition: "not something fancy, but 'your
  radiator would need a replacement shortly' will be great";
- the scene is a gas station out of an installed AC track, not shipped ("we won't publish it"): Bannochbrae's, by the
  start line (the user's pointer).

## The scene

`Content/streetcorsa/stations/tomwixon/`: `station.json` (in the repository), `station.kn5` and `ground.bin` (not: they
are cut out of the player's own AC install, gitignored). The station is Bannochbrae's "Tomwixon Garages" (the track is
Rainmaker's Project Cars conversion): a canopy over two pump islands, a shop, a workshop, on the main road through the
village, the hills behind. A modern British station, not a 1960s American one; the best there was.

**Building it** (`tools/StationScene/StationSceneBuilder`, a console tool like the street's):

```
StationSceneBuilder "<AC>/content/tracks/rt_bannochbrae" "<game>/Content/streetcorsa/stations/tomwixon/station.kn5"
    --at AC_AUDIO_GASSTATION --radius 250 --models rt_bannochbrae.kn5,rt_bannochbrae_veg.kn5
    --exclude nz_plastic_barrier,metalfence08b_pcar,metalfence08b_wmd,flag_,ban_pitsign
    --ground-half 150 --sky day=../skies/sky_day.jpg,dusk=../skies/sky_dusk.jpg,night=../skies/sky_night.jpg
    --sky-radius 700
```

The skies are in the repository (`tools/StationScene/skies`, CC0); `tools/StationScene/prepare_skies.py <folder>` makes
them again from Poly Haven.

- It takes every mesh of the models within 250 m of the track's gas-station sound emitter (`AC_AUDIO_GASSTATION`, by the
  shop), the big ones (road, terrain) cut down to the triangles within reach, moved so that point on the ground is the
  scene's middle, with the materials and textures they use (about 320 MB, 115k triangles). The track's race-day dressing
  goes (`--exclude`: plastic barriers, the Project Cars advert fences, flags, pit signs).
- **The ground** (`ground.bin`, "SCG1", a 0.5 m grid of heights over ±150 m, read by `Services/Station/StationGround.cs`)
  comes from the scene's upward faces: per cell the lowest face, or one up to 0.6 m above it (a kerb, the road over the
  terrain, the markings), so a roof or the canopy is not ground. The track's physics mesh would be the right thing but
  covers only the race's roads, not the forecourt. Faces more than 40 m from the middle's height are left out: the track
  has a mesh 467 m under it.
- **The sky**: a dome for each light of the day (`STATION_SKY_DAY`, `_DUSK`, `_NIGHT`), the viewport showing the one for
  the hour. The panoramas are Poly Haven "pure skies" (CC0): Kloofendal 48d Partly Cloudy by day, Qwantani Dusk 2 at dusk,
  Qwantani Night darkened and tinted blue at night, 4096 × 2048, the part below the horizon filled with a hazy ground
  colour (where the cut terrain ends, the dome shows). The dome is 700 m: the camera's far plane is about 976 m at this
  lens, and a car 115 m out along the road saw through a 900 m one.

**The layout** (scene metres, +z towards the road): the road runs along x at z ≈ 28 (the lane nearer the station at
28.4); the canopy spans x −4.6…4.2, z −2.3…12.5, over two islands running along x at z ≈ 2 and z ≈ 10, a pump at each
end (x ≈ ±1.5); the shop and workshops start at z ≈ −2.3; the forecourt opens onto the road at x ≈ −4…−27 (the way in,
with its arrows) and x ≈ 37…54. The car comes up the road from x = −115, turns in, and stops in the lane between the
islands at (0.6, 4.8) facing +x, its driver's door to the island at z = 2; it pulls straight through, across the front of
the workshops and out onto the road at the far opening. A top-down render with the path drawn over it
(`drawpath.py` in the session's scratchpad) checked it clears the islands, the canopy's posts and the grass island.

## The viewport (`Controls/StationViewport3D.cs`)

A `D3DViewportBase` like the street's. The scene is the showroom slot; the player's car the main slot, driven along the
station's paths by **`Controls/Street/PathDrive.cs`**: a centripetal Catmull-Rom curve through the points (the uniform
one overshot into a loop where a short stretch met a long one, and the car spun on the spot before the road), resampled
every 0.25 m,
the speed the most each point's bend allows (1.5 m/s² sideways, 13.5 m/s on the straight), speeding up at 2.2 m/s² and
braking at 3.2 m/s² in time for what comes; an arrival rolls in at the start and stops at the last point, a departure
leaves from a standstill. Worked out once, a function of time. The engine follows the speed through a four-speed, the
clutch in below 3 m/s on the way to a stop (it idles on its own then).

The car sits on the ground under its wheels (the heights at ±1.4 m along it and ±0.75 m across tilt it), its wheels turn,
its body dives and squats (`ChassisPitch`) and rocks with the engine (`CarBodyRock`, placement and lean together), its
brake lights come on, headlights at night. The camera is at the driver's eyes: ahead while driving, turning a little into
a bend; at the pump the head turns to it (`lookAtPump`) over about a second. Dragging looks round; a double click looks
back where the driver would. The station's own listener on the engine rocks the car (the base's `RunningEngine` is not
used: the car moves).

**The dashboard and the steering wheel** (`Controls/Street/CarInstruments.cs`) move as in AC, from the car's own data:
the needles of `analog_instruments.ini` (speed, revs, fuel, water and oil temperature: each a node turned about its own z
by `ZERO + value × STEP` degrees, no lower than `MIN_VALUE`, or off its `LUT`), the tach from the engine sound's revs, the
speedo from the drive, the fuel gauge from the tank (it climbs while the pump runs); the steering wheel (`STEER_HR`,
`STEER_LR`) turned by the front wheels' angle for the bend a third of a second ahead (`atan(wheelbase × curvature)`,
`PathState.Curvature`) times car.ini's `STEER_RATIO`, up to its `STEER_LOCK`. The nodes' own z points forward, away from
the driver, in every model looked at: a positive turn is clockwise as the driver sees it (the dials read that way; a left
turn is a negative one). The Cruise screen's dashboard shows the revs too.

`StationStage` (`Controls/Street/StationStage.cs`) is what the screen and the viewport share, like the street's: the screen
sends the car in (`Arrive`, towed or not) and out (`Leave`); the viewport says when it is at the pump and when it has
gone. With no viewport up the car is simply there, and gone, at once; a car towed in is at the pump from the start.

**Hard-won facts:**
- **How a car sits and where its driver looks, as AC has it** (`AcCarFrame`, `Controls/Street/SceneLights.cs`). AC's car
  frame has its origin at the centre of gravity: the wheel centres `BASEY` under it (suspensions.ini), the road a tyre's
  `RADIUS` under those, the model drawn moved by `[BASIC] GRAPHICS_OFFSET`. A model's own origin differs from mod to mod
  (a '65 GTO's at its axles, a '49 Cadillac's on the road), and the renderer puts the car's origin where it is placed:
  the GTO stood with its tyres half in the road. The car is lifted by radius - BASEY + the offset's height (checked:
  the GTO's and the Cadillac's modelled wheel centres plus their offsets give their BASEY). `DRIVEREYES` is in the
  model's own space, as Content Manager's showroom has it: taking the offset out put the Cadillac's, the Daytona's and
  the '63 Vette's eyes in or above their roofs (the raw eyes sit 23-34 cm under the roof, 1.05-1.37 m over the road, in
  every mod that could be read; the Kunos cars' data.acd cannot be). The mods' eyes are AC's, but they seat some drivers well (a
  Riviera, 34 cm over its wheel) and some with their nose on the wheel (a '40 Ford, 25 cm over and 29 cm behind; a
  Super Bee): the user picked one seat for every car (2026-09-27). `SceneLights.DriverEye` puts the eyes 32 cm over the
  steering wheel's node (`STEER_HR`, or `STEER_LR`) and 50 cm behind it, level with its centre, looking 2 degrees down,
  through the player's own AC onboard lens (cfg\camera_onboard.ini `FOV`, 58.2 here); `DRIVEREYES` only for a car with no
  steering wheel. Checked in the Ford, the Riviera, the Super Bee and the GTO: the road over the hood, the pump in the
  side window. The street uses the same frame.
- The renderer's background is white: whatever the reflections see past the sky (a chrome mirror) is white. The station
  sets the background to the light's upper ambient colour. Some cars' mirror glass is drawn white all the same (the GTO's).
- A second station's model loading while the first is on screen fades out first, as the street's.

## The screen (`Screens/Station/GasStationScreenViewModel.cs`)

- **On the way in**: no car, an impounded one, or one that won't run for anything but its tank (blown, wrecked, totaled:
  `CarCondition.WhyCannotRace(…, counting: false)`; no engine in it, its engine on the stand) sends the player home. The
  garage refuses an engine that won't run before the car leaves (its `EngineSpec`), as races do. A car with less than the drive takes
  (`GasStation.TripLitres`, 0.8 l) is towed in: the tow costs the station's price (25 dollars, times the difficulty's
  part prices) and an hour (`GameAction.TowToStation`); without the money the player stays home. Otherwise the drive takes
  its 0.8 l and 20 minutes (`DriveToStation`); saving and going home wait for that time to be spent (the day may roll
  over). The screen waits for the station to be on screen (25 s at most; not at all once the viewport has failed,
  `StationStage.CannotShow`) before the car rolls in. The engine starts only while the car is still on its way in: one
  worked out after the car stopped stays off at the pump.
- **The curtain**: the screen is black until the car rolls in (it lifts over 1.2 s) and goes black again as the car
  drives off (0.9 s, the viewport reporting the car gone 1.2 s before its drive ends, so the curtain comes down on it
  moving), then the garage fades itself in.
- **At the pump**: the engine goes off, and the headlights with it (a car comes in with them on at night); the attendant greets (the tow's own greeting), checks the oil (`station.oil.*`, in quarts) and says what
  he noticed (`AttendantCheck`), or that everything else looks good. The jobs (`GasStation.Jobs`) are up with their price
  and time, a quarter of an hour each: **fill up** (by the litre), **top up the oil** (by the litre, a quart at least),
  **wash**. Each is watched (`GasStation.WatchSeconds`: the pump at about nine litres a second, 3 to 8 s, its dials counting
  the litres and the dollars on a bar; the oil 2.5 s, the wash 4 s), then paid, done, timed and saved; the attendant says
  it's done. Nothing else can be done meanwhile, driving off included.
- **Driving off**: the starter, a moment at idle (up to 4 s for an engine that won't catch), the headlights back on at
  night, then out onto the road and home. A car left dry is towed home instead, with nothing to watch: the tow costs what
  it did on the way in (or all the player has left) and an hour. A garage that won't open leaves the car back at the pump
  (`StationStage.BackToPump`), engine off, to try again.

## The oil (`CarCondition`)

`Car.OilLevel`, 1 full to 0 dry, null for full (every car of an older save is full). Every race burns a little: 0.4% of a
sump at the start and 0.4% per km, times up to 5 for a worn engine (the pistons' and heads' mileage, or every engine
part's). A drag race on a tired engine costs about 2%, a long road race a fifth. The report says so once at 75% ("it's
burning oil, have it checked at the gas station") and again under 40% (the oil light). A low sump surges sooner: the race
mode's sump holds its oil to its g times 0.4 + 0.6 × the level (`CarCondition.SumpShare`, on the cooling rating's
`OilG`), so a car a quart down surges a little sooner and one near dry at the first hard launch, and the race mode's oil
starvation does the rest. Rivals' races leave the oil alone and a car that came to a rival is topped up the next
morning, like its tank.

The sump holds what the engine's oil pan says (`capacity`), or 4.7 litres (a V8 of the day's five quarts).

## The attendant (`Parts/Cars/AttendantCheck.cs`)

What a man with a rag and a good ear notices while the tank fills: the radiator, water pump and fan (by
`EngineCooling.KindOf`), the exhaust, air filter, carburettor and clutch (by shelf group) and the tread of each tyre, the
brakes and shocks of each axle, worn (`Wear`) under 0.3 ("would need replacing shortly") or 0.12 ("now"); and, heard as
the car rolled in, a bottom end under 55% of its life (knocking) and a gearbox under 55% (crunching). The worst three, a
line each, from `Content/streetcorsa/text/station.json` (`station.hint.<thing>.soon` / `.now`, `{where}` the corner or
axle). Not an inspection: nothing he couldn't see or hear.

## Checking it without playing

Throwaway harnesses in the session's scratchpad (2026-09-27): `shot` (offscreen renders of the scene with a car placed,
a camera anywhere), `vp` (a WPF window off screen hosting `StationViewport3D` with a `StationStage`, sending the car in
and out and saving a frame every 1.5 s) and `drawpath.py` (the paths over a top-down render). Not yet seen in the game.
