# The garage's room

The garage screen stands the player's car in the room `game.json` names (`garage` when none does): a mod's
`garages\<id>` (its KN5 and `scene.json`), else AC's showroom of that id, else AC's own showroom (roadmap step 34).
A setting brings its own garage. The screen itself (the parts view, the stand, the lift, the bench) is described where
each was built; this page is the room.

## The room

- The car stands on the origin, its nose to +z; a car taken out of the lot comes in through `scene.json`'s `door` mark.
- Every surface its own material (`gr_floor`, `gr_wall`, ...) lets a mod dress the room differently.
- Past the doors the renderer's background is white: a model gives the outside a dome (black, or a sky dome for the
  hours, `GARAGE_SKY_DAY`...: the one for the hour is shown).
- `scene.json`'s `room` keeps the garage camera inside the walls and under the roof (`CarViewport3D.RoomLimit`, as the
  car lot's); its lights are picked by the game's clock (`GarageScreenViewModel.GarageLight`).

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
  comes or goes or the window is resized; the player's wheel ends the fitting until the next framing. The fit goes no
  further back than `MaxRadius` (12 m, enough for a 5 m car between the workbench's cards on a 16:9 window) nor past
  the room's walls, so on a narrow window a long car's ends may still go under the cards. A panel is measured where it
  is laid out, not through its own `Reveal` slide, which holds a closed panel out at its offset.
- The orbit is held off the floor every frame (`FloorBeta`, the eye just off the floor): the wheel backs it off quicker
  than beta comes up after its goal, which alone would dip the eye under the floor for a moment. A script's car owns the
  target while it plays (the look goes where the script moves it, not eased back toward the car's home); the camera is
  settled on the car before a script only while it is on the car, not snapped to a part it was easing to. A click with a
  pixel of jitter in it picks and throws nothing; the hand's clock is the stopwatch, which tells a mouse's reports apart
  (`TickCount64` ticks 15.6 ms apart and counted one move in it, so a flick threw almost nothing).
- The hand (`OrbitHand`): a drag turns the goals, the orbit follows them at a quick ease (`HandSettleRate`) so the
  picture never jumps a mouse event at a time; let go on the move, the camera is thrown on a little and dies down; held
  still first, it stays. The wheel sets the distance goal, eased at `ZoomSettleRate`.
- The pointer is asked what it is on 30 ms apart at most (`PollThrottle`); the old throttle seeded its clock with
  `long.MinValue`, and `now - long.MinValue` overflows negative, so nothing was ever due and no part glowed under
  the pointer.

## The dressing

Nothing loose need be in the model: every prop is a folder of a mod's `props` with its place under the garage's id
(docs/modding.md, props). A prop's `credit` names its makers; the CC BY ones are in Settings' credits. Two are the
game's own when a prop names no model:

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

Nothing should stand in the car's bay or at the engine stand.

## Checking it without the game

A console harness drawing the room through `GarageRenderer` (`WpfMode` off), the dressing, the bench, the radio and the
stand set in as the viewport sets them, a car from the AC install, a light of the day and named camera views, `Shot` to
PNGs. Its assembly is named `StreetCorsa.Tests` for the game's internals.

## Hard-won facts

- A sky dome's u = 0 is straight out of the door (-z), `SkyDome` mapping azimuth pi to the picture's left edge; a flat
  driveway out to the horizon meets a panorama at eye height and its street seems to float, so a short apron and a dome
  reaching well under the horizon are what make one look right.
- Blender's glTF import gives a rigged model an icosphere to draw its bones with: a mesh, not hidden in the scene; leave
  out every pose bone's custom shape, or it stands in the garage as a big ball.
- Sketchfab scales are anything (a hoist 7 cm long, a battery in tenths of a millimetre): scale each model to metres.
