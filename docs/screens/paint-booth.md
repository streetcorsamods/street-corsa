# Paint booth

The garage's Paint button (`garage.tile.paint_booth`, bottom right under the gas station, `icons/paint_spray`). The
owner's brief (docs/ideas.txt batch 9, idea 2): the screen fades to a paint booth in 3D, the car drives in, a spray
animation runs while its skin changes, a custom colour as well as the mod's skins, and after painting the car does not
back out: the screen fades back to the garage and the car makes its entrance in its new colour.

Owner's decisions (2026-10-05): a paint job costs money and time; a colour of the player's own is tinted where the car's
paint can take it, and a car or skin whose paint cannot only offers its mod's skins, saying why.

## Flow

Only a sound body is painted (the owner, 2026-10-05): a car with rust (`Car.Rust` from the repair shop's `someRust`) or
dents and scratches (body damage past half a km/h) is not taken: the Paint button says what the repair shop has to do
first and the car stays home (`PaintFirst.WhyNot`, from `RepairShop.BeforePaint`; the booth checks again on the way in).
Worn paint alone is no bar: the paint job is what takes it off.

Garage → (a car that runs drives out, `garage.leave`; one that will not is pushed over) → the booth: the car rolls in
from the body shop through the open door (`booth.arrive`, `roll_in` feel) and stops over the floor grate; a non-runner
stands there from the start → the player picks (the panel on the right) → **Spray it**: paid, the clock moves on half a
day (`GameAction.PaintJob`, 240 min), the painter walks in from the shop and goes round the car with his gun, the new
colour going on coat by coat → he walks out, a moment to look at it → the curtain comes down → the garage, where the
car comes in (`DrivingHome.Arrival`, `garage.arrive`) in its new colour. **Leave** goes home without a job.

## The pick

- **The make's colours**: the mod's skins (`CarDefinition.AvailableSkins`, police liveries left out), each a card with
  its `preview.jpg` and its `ui_skin.json` name (`skinname`; a mod's "Undefined" or none: the folder's name).
- **Your own colour**: sixteen colours of the day to start from (`booth.colour.*`) and hue / depth / lightness sliders,
  over the skin picked. The car shows it 0.35 s after the sliders rest (`PaintJobShow.Wear`).
- A skin whose paint cannot be tinted greys the colours out and says so (`booth.ui.tint.cannot`); a colour picked is
  dropped back to the skin's own.
- Price (`rules/repairs.json`): `colourChangeCost` 350, plus `customColourCost` 75 for a mixed colour, times the
  career's part price multiplier. **As it is** puts the car back as it came; Spray it is off until something changes.
- The job takes the paint's wear off (`Car.PaintWear = 0`) and the dirt; rust stays (the repair shop's rust job is
  bodywork). `Car.SkinId` is the skin, `Car.PaintColour` (#RRGGBB, null: the skin's own colours) the colour over it;
  both go with the car when it is sold (`UsedCarListing.PaintColour`) and come back with it bought.

## How a car is painted another colour (`Looks/CarPaint`)

Nothing is written into the car's folder. A colour is the car's own textures, tinted:

1. **What the paint is**: the wear's look at the car (`BodyShell.Paint`, materials ordered by how much of each is seen
   from outside; `WearLayer.ShellFor`). The first is the body; another is painted with it only when it shows the same
   colour (a chassis seen from below, black, is not).
2. **Its colour as AC draws it**: the diffuse, and on a multi-map shader with `useDetail` the detail multiplied in where
   the diffuse is see-through: `diffuse × lerp(detail, 1, diffuse.a)` (AcTools' `DarkMaterial.Unpack.fx`, AC's own).
   The diffuse's colour is read apart from its alpha (`TextureReader.ToPng(ignoreAlpha)`): WPF loses a texel's colour
   where its alpha is 0, and there it still counts.
3. **Where the colour comes through the detail** (most of the body under a see-through diffuse: AMC Javelin, Dodge
   Monaco, Mercury Cougar, the pb_ cars), the diffuse's lightness is taken out to judge the paint (a grey AO diffuse
   over a blue detail is blue, crease or panel) and samples weighed by it (a gap in black diffuse shows no paint
   whatever the colour). A detail repeated more than twice over the body (metal flake, `detailUVMultiplier` 20-150)
   counts as its average colour.
4. **The paint's colour**: the most common of 16 shades a channel over the body's area, then the mean of those near it;
   the body can be tinted when 70% of it is close to that (`CarPaint.MinShare`; close = within 0.2 in damped hue and
   0.55-1.6 in lightness, fading out to 0.3 and 0.45-1.8).
5. **The tint**: each texel close to the paint's colour is repainted the new colour times its own lightness against the
   paint's (shading, panel lines and AO stay), fading out at the edge of "close"; others (chrome, badges, trim, the
   interior in another colour) are left. A colour through the detail is baked: diffuse × detail, shading out, tinted,
   shading back, into the diffuse (alpha kept), and the detail made plain white (alpha kept), so the diffuse alone gives
   the colour.
6. **Other materials drawing that detail** (the textures go over the car's own by name, for every material): each one
   that shows something of it (see-through texels where it draws, not near black) has the detail baked into its own
   diffuse too (the interior's paint, a steering wheel in the body's colour), unless a material drawing that diffuse
   without the detail draws see-through texels of it (an interior's AO atlas, each material coloured by a detail of its
   own). Then the detail itself is tinted in place, the diffuses left, when everything drawing it shows the paint's hue
   (`PaintLayer.InDetail`: the Plymouth Belvedere's dash); else the skin keeps its own colours. Survey 2026-10-05 after
   the review fixes: 1810/1912 skins tintable (1830 before), the same 6 cars none.

The tinted textures are PNGs in `%AppData%\StreetCorsa\Looks\paint`, named for the model, skin, colour and texture
(`<car>_<stamp>_<RRGGBB>_<texture hash>.png`, a `.list` of what was written), pruned past about 60 jobs. The scenes put
them over the car's own by name (`Kn5RenderableCar.OverrideTexture`, a procedural override that wins over the skin's
files) from `ShownPartsOnCar.SetAsync` (`ShownSet.Paint`, every scene: garage, lots, diner, street, station, viewing),
as DDS with mips. A race gets them as `CAR_n_PAINT_k` (file, mirrored into the mode's `paint\` folder like the wear
layer) and `CAR_n_PAINT_k_TEXTURE` (the car's texture it replaces); `car_parts.lua` sets them with CSP's
`root:applySkin{ [texture] = file }` on the car's root, every LOD that draws from that texture.

How many of the installed cars take a colour (2026-10-05, 199 cars): see the run in the PR; two-tone skins and liveries
don't (by design), nor a skin whose textures AcTools can't decode.

## The booth (a setting's `booths\<id>`)

`booth.kn5` (the room) and `painter.kn5` (the man): his right arm a node (`ARM`) at the shoulder, the spray a
see-through cone (`MIST`, alpha-blended, no depth write) at the nozzle. The booth is a setting's place: its flow adds
the `paint_booth` screen and its garage tile.

`booth.json`: `arrive` (the way in), `round` (`gap` off the body, `low`/`high` the gun's swing in degrees, `strokes` a
side, `wait` where he stands by his cart, clear of the way in), `camera` (`distance`, `height`, `min`/`max` the box it
never leaves), `colours` (the swatches offered: `name` a text pool key or the name itself, `hex`; none without them),
`sun` and `lights` (indoors: `day` only, every hour falls back to it). A mod can add or replace a booth (`booths`
content type); the game uses the first.

## The view (`Controls/PaintBoothViewport3D`)

A `DriveInViewport3D` (the station's and diner's drive, ground, stage and scripts) with its own camera instead of the
driver's eyes (`AimView` overridden):

- Round where the car stands painted (the `paint` mark), never outside the camera box, looking at the car where it is.
  On the way in it stands ahead of the car's stop to its left, so the car comes at it through the door.
- While the player picks it walks slowly round; a drag turns it by hand (eased back by the base after), the wheel steps
  closer or back, a double click resets.
- While the painter sprays it goes up 0.85 m and across the car from him, a little ahead, looking down over the roof at
  him and his gun.
- The painter (`GarageRenderer.SetFixture`/`MoveFixture`, `Fixture.GetDummyByName("ARM"/"MIST")`): 14% of the job
  walking in from his wait spot, 76% round the car on a superellipse (power 4) `gap` off the car's box, facing the
  nearest point of its middle line, bobbing with his steps, his arm swinging `strokes` × 4 times a lap between `low` and
  `high`, the spray flickering; 10% walking back out.
- `PaintJobShow` carries the screen's word to the view: `Wear(skin, shown)` (preview, the final look), `Spray(time)`,
  `PutCoat(textures)`, `StopSpraying()`.
- The coats: 8 over the lap, each a blend of the old textures to the new (`CarPaint.TexelsOf` both, `Blend`, a texture
  of another size read at the new one's), put on with `ShownPartsOnCar.Coat`. To another skin there is no blend: the car
  turns into it under the spray halfway round.

The car's shadow: the booth's own meshes cast none, the sun (straight down from the light boxes, a little forward) is
the only light that shadows, so it must outweigh the ambient and the lamps (`booth.json`: sun 2.2, ambient 1.15, lamps
0.35) or the floor under the car is lit as brightly as round it; the walls, ceiling and filters carry the room's white in
their own ksAmbient (0.62) instead. The shadows centre on the car where it stands (`GarageRenderer.ShadowsCentre`).

Spray time is `rules/repairs.json` `spraySeconds` (18). Sounds: `booth.spray` (loop while he sprays), the screen's
ambience `ambience.booth`.

## Checked

Harnesses in the session's scratchpad: `paintshot` (one car, a skin, a colour, rendered with AcTools: Chevelle red →
blue, Javelin blue → yellow, Gran Torino black → white, Monaco purple → lime), `boothshot` (the booth KN5s with a car
and the painter), `boothscreen` (the real App: the save copied, the booth screen, shots on the way in, after a pick and
through the spray; needs the game not running), `tintall` (`CarPaint.SurfaceOf` over every installed car and skin).
Not seen in AC: the race's `applySkin` (CSP docs only).
