# The garage's room

The garage screen stands the player's car in a room of the game's own, `Content/streetcorsa/garages/workshop`
(roadmap step 34). The screen itself (the parts view, the stand, the lift, the bench) is described where each was
built; this page is the room.

## The room

A two-bay block garage of the early sixties, 16 m across and 19 m deep, under a pitched roof on open timber trusses.
The car stands in the right-hand bay on the origin, its nose to +z; its sectional door, behind it, is rolled up under the
roof, and a car taken out of the lot comes in through it from the driveway (`scene.json`'s `door` mark, 13 m behind).
The left bay is the workshop: its door is shut, the engine stand stands in it, the workbench is against the front wall
under a pegboard. The walls are concrete block, cream above and a garage green below; windows down the right side, a
side door on the left, fluorescent fittings hanging off the tie beams.

- `workshop.kn5` and `scene.json` are written by `tools/GarageScene/GarageSceneBuilder` out of the pictures
  `prepare_garage.py` gathers (Poly Haven CC0 textures and what it draws: the door, the windows,
  the signs, the pegboard). Change the builder, not the files.
- Every surface is its own material (`gr_floor`, `gr_wall`, `gr_dado`, ...), for a mod to dress the room differently.
- Outside the doors a concrete apron the garage's width runs two car lengths out; past it everything is black (a black
  dome, `GR_OUTSIDE`: the renderer's own background is white), the owner's call until the garage has a street of its
  own. A mod's model may add sky domes for the hours (`GARAGE_SKY_DAY`...): the one for the hour is shown.
- `scene.json`'s `room` keeps the garage camera inside the walls and under the tie beams (`CarViewport3D.RoomLimit`, as
  the car lot's); its lights are picked by the game's clock (`GarageScreenViewModel.GarageLight`).

## The camera

`CarViewport3D.Camera.cs`. The camera orbits what it looks at, eased there frame by frame (a share of what is left a
second, so it is the same at any frame rate).

- On the car it looks at the middle of the car's box, worked out every frame (the lift takes it up; a script's car is
  left to the script, which moves the target itself), from as far back as takes the car in at its widest as the camera
  goes round it: `PreviewFraming.GarageDistance`, the diagonal of its footprint at `CarFill` of the picture's width.
  AcTools' own `AutoAdjustTarget` is never used: it centres the mean of the box's corners on the screen, which leans to
  the car's near end, and chases it a third of the way a frame.
- The picture it fits the car to is what the screen's panels leave: a panel marked `D3DViewportBase.Covers` (the
  workbench's shelf at the left and engine sheet at the right, through `PartsWorkbenchView.Covers`) counts toward
  `CoveredLeft` or `CoveredRight` by which half of the view it lies over. The camera looks to its left by
  `PreviewFraming.SideShift` so the car stands in the middle of what is left, and the fit is made again as a panel
  comes or goes or the window is resized; the player's wheel ends the fitting until the next framing.
- The hand (`OrbitHand`): a drag turns the goals, the orbit follows them at a quick ease (`HandSettleRate`) so the
  picture never jumps a mouse event at a time; let go on the move, the camera is thrown on a little and dies down; held
  still first, it stays. The wheel sets the distance goal, eased at `ZoomSettleRate`.
- The pointer is asked what it is on 30 ms apart at most (`PollThrottle`); the old throttle seeded its clock with
  `long.MinValue`, and `now - long.MinValue` overflows negative, so nothing was ever due and no part glowed under
  the pointer.

## The dressing

Nothing loose is in the model: every prop is a folder of `Content/streetcorsa/props` with its place in `workshop`
(docs/modding.md, props). Most models are Poly Haven's (CC0): `fetch_props.py` downloads their glTFs. Nine are Sketchfab
models (CC BY 4.0) the owner downloaded (a login is needed) as glTF zips into `D:/JUEGOS/3D Models/garage`: the engine
stand, the hoist, jack stands, a creeper, a tool cart, an oil drainer, a battery, an old V8, a clock.
`build_props.py <Poly Haven folder> <zips folder> <work folder>` readies every one with `compose_props.py` in Blender
(what is no part of it taken out, a pose held, scaled and turned, the moving parts split off, brought down to its
triangle budget), then `tools/Props/prepare_prop.py` and `PropBuilder` (metres, JPEG maps to keep them small; a model
with moving parts keeps its origin and has them as nodes). A prop's `credit` names its makers; the CC BY ones are in
Settings' credits.

- **The lift**: the game's own in-ground post (`LiftLayout.CreateModel`, the prop names no model), as a 1960s garage
  had: a steel tread plate in the floor with hazard-striped edging, a collar and bolts, and the saddle (a beam along the
  car, an arm across at each end, a rubber pad at each arm's end, `LiftLayout.PadSide`/`PadAlong`) lying on it, rising on
  its piston. The owner tried the Sketchfab two-post lift first and dropped it: its posts framed every view and its arms
  needed swinging and squashing to fit under a car. Going up, the saddle rises to the body before the car moves:
  `GarageRenderer.BodyUnderside` finds the lowest of the car's body over the pads (its wheels, hubs and suspension left
  out; 24 cm on the Nova). `LiftLayout.SaddleGoal`/`CarHeight` drive the saddle, the car following it.
- **The engine stand**: the head (plate, standoffs, bolts, arms, spindle) is `engine_stand_head`; scaled so the spindle
  is at 0.78 m, the face the block bolts to at Z 0. `EngineStandLayout` puts both parts into the stand's model where its
  boxes would have gone.

The layout, by wall: the rack, the tyre stack and the wheels, then the desk with the radio down the right wall; shelves,
a jerrycan, the broom and a bucket along the front wall on the car's side; the tool cart and chest, the table and drill
press, the welder, a gas bottle and the compressor down the left wall; the ladder, tyre pump, hand truck, jerrycan and
oil drum round the doors; the vise and a toolbox on the bench, the hand tools on the pegboard. Nothing stands in the car's
bay or at the engine stand (`OwnGarageTests` checks it).

## Checking it without the game

A console harness drawing the room through `GarageRenderer` (`WpfMode` off), the dressing, the bench, the radio and the
stand set in as the viewport sets them, a car from the AC install, a light of the day and named camera views, `Shot` to
PNGs: how the layout and the lights were set. Its assembly is named `StreetCorsa.Tests` for the game's
internals.

## Hard-won facts

- Tried and taken out at the owner's word: Poly Haven's `pretville_street` panorama on the dome past the door. A sky
  dome's u = 0 is straight out of the door (-z), `SkyDome` mapping azimuth pi to the picture's left edge; a flat driveway
  out to the horizon meets a panorama at eye height and its street seems to float, so a short apron and a dome reaching
  well under the horizon are what make one look right.
- The room was first 12 x 12.5 m; the owner asked for it bigger (16 x 16), then for more room before the car's nose (16 x 19); the props that stood by a wall moved with it.
- Poly Haven's `steel_frame_shelves_01` comes out ten times too big at scale 1 (left out), and some of its models stand
  up what lies down (a tyre is 0.6 high and 0.17 thick): `tilt` lays them flat.
- Blender's glTF import gives a rigged model (the vise) an icosphere to draw its bones with: a mesh, not hidden
  in the scene; `compose_props.py` leaves out every pose bone's custom shape, or it stands in the garage as a big ball.
- Sketchfab scales are anything: the hoist came in 7 cm long, the jack stand 2 m high, the battery in units of a tenth of
  a millimetre with a backdrop sphere 1.6 km across. Each has its own scale in `SKETCHFAB`.
