# Diner Screen

The player drives to a setting's diner, seen from the driver's seat as at the gas station: down the diner's street and
into the player's bay (`diner.json`'s `arrive`), looking over at it out of the side window; the racers' cars stand in
its bays. The street is the screen; the diner (who is in, the word on the street, a racer's options and a word with
them) opens in one window over it. Leaving, the car backs out (`backOut`) and drives off down the street (`leave`).

```
Garage ──[racing hub: Hit the streets]──▶ Diners' map ──[a diner]──▶ Diner: down its street, to the bay ── parked: the diner's window on a click
   ▲                                                                                      │
   └──────────── back out, off down the street, the curtain ◀──[garage button]────────────┤
                 (Cruise: the same, then the street's curb) ◀──[CRUISE THE STREETS]───────┘
                 (a challenge: straight to the race; back from it, the car stands in its bay)
```

The diner, its map and the street are a setting's places: its flow adds the screens (`diner`, `diner_map`, `street`)
and wires them (see [modding.md](../modding.md), "flow"). The diners themselves (`diners`), their streets (`streets`,
with a `diner.json`), a street's layout from the street kit (`streetkit`) and each diner's look over it (`look`) are the
setting's content; what each file may say is in [modding.md](../modding.md), "streets", "streetkit" and "diners".

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
- The ambience is the street's (`ambience.street`, outside, in the car): a setting's cue; without one the street is quiet.

## Harnesses (session scratchpad)

- `dinershot`: the street alone in the renderer, from given eyes (`top` looks straight down); `drawpath.py` draws
  `diner.json`'s ways over the top view.
- `dinerscreen`: the real game (App, a copy of the save) to the diner at an hour, shots on the way in, the window, a
  racer, the way out; `cruise` the Cruise screen. It reads the scripted car and the script run by reflection.
