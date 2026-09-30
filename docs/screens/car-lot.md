# Car Lot

The player's cars, parked in their warehouses, in 3D. It replaced the car list (`Screens/CarSelection`): the garage's
select-car button comes here.

```
Garage (hub) ◀──▶ CarLot ──▶ take a car ──▶ back to the garage
```

## What is on screen

- **The warehouse**, full-bleed, in the lot viewport (`Controls/LotViewport3D`, shared with the dealer's lot). The
  camera opens looking down the aisle from the roller door; a click on a car goes to it, a click on nothing comes back,
  dragging swings round, the wheel zooms. However it is swung or zoomed, the camera stays inside the walls and under the
  roof (the scene's `room`; `LotViewport3D.Room`), coming in closer where a wall is near.
- **Which warehouse** at the top, with arrows to the one before and the next when there are several, and what is in
  it ("7 cars in here, 23 in all; room for 3 more").
- **Every car**, in a list down the left, by warehouse and bay ("W2 · bay 4"; "Bay 4" with one warehouse). A click goes
  to the car's warehouse and onto it: this is how a car is found among many. The car the player drives heads it, "In
  the garage": it is not in the warehouse. A car in the police impound is listed "At the police impound", and there is
  nothing to go to.
- **Take this car**, one button at the bottom middle while a car is picked: it becomes the current car (15 minutes, back
  to the garage, as the list did). Nothing else is done from here (the user, 2026-09-29: no card, no selling or engine
  start; the garage does those for the car in it). The lot opens on warehouse 1. While a car is being taken the arrows,
  the list and the warehouse do nothing.
- **Driving off and in** (`Controls/DriveOff`): taking a car whose engine runs, its lights come on, its engine starts,
  it idles a moment and pulls out of its bay, and the screen and the engine fade out together as it rolls; then it
  drives into the garage, lights on and engine running, brakes to a stop in its place, idles and is switched off. A
  car whose engine does not run is towed: it is simply there, as before. Going to the lot shows nothing (the player
  may only be looking). The lot loads no light sources, so there the lamps glow and light nothing.

## Warehouses

A warehouse holds as many cars as its scene has bays (ten, the base game's). There is no limit: the eleventh car goes
in warehouse 2, and so on (`Services/CarLot/CarStorage`).

- The car the player drives (`Player.SelectedCarInstanceId`) is in the garage and has no place. **Taking a car out
  swaps them** (`CarStorage.TakeOut`): the one from the garage is parked in the bay the other leaves, so no bay stands
  empty for a car that is out.
- A car keeps its place (`Car.Warehouse`, `Car.Bay`) from one visit to the next, until it is moved: when a car goes
  (sold, lost, taken to the garage) its bay stays empty and nobody moves up (the user, 2026-09-29).
- A car without one (just bought, won, an older save) takes the first free bay, counting from warehouse 1; places
  given when the lot opens are saved at once.
- A car that changes hands gives its place up (`Car.ClearPlace`: sold, lost or won in a pink slip, bought from a
  rival), so a car sold and bought back is a car without one, and a bay it had long ago does not bring back an empty
  warehouse it was in.
- A place is kept only while it is real and nobody else's. Of two cars in one bay the one the player has had longer
  keeps it (the earlier in `Player.Cars`).
- A car in the police impound keeps its bay (or is given one) for when it is collected, but is not standing in it
  (`CarStorage.In`). Taking a car out while the garage's car is at the impound parks that one in the freed bay all the
  same: that is where it goes when it is collected.
- A car whose model is no longer installed keeps its bay too, for when it is back: the bay counts as taken, but the car
  is not in the list, and a warehouse only such cars are in is not one of the warehouses to page through.
- The warehouses run up to the last one with a listed car in it; one emptied between two full ones stays, and is filled
  first.

## The scene

The base game's warehouse is built, not borrowed: `tools/WarehouseScene`.

1. `prepare_warehouse.py <folder>` downloads the surface textures from Poly Haven (CC0; the ids are in the script and
   in `warehouse.json`'s credit), re-encoded at 1024 px, and draws the rest: the enamel sign ("WAREHOUSE") and a plate
   per digit, the bay lines' yellow, the skylights' wired glass, the lamps' flat colours.
2. `WarehouseSceneBuilder <folder> <out>` models the shed in code and writes `warehouse.kn5` and `warehouse.json` to
   `Content/streetcorsa/warehouses/warehouse`. Change the builder, not the two files.

The shed: 28 m down the aisle (x, the door at +x), 24 m across (z); brick to 3.2 m, cladding to the eaves at 5.5 m, a
pitched roof to 8 m on six steel trusses standing between the bays, purlins, columns. Ten bays, five a side, the cars'
noses to the aisle; yellow lines between them. A roller door in a steel frame at the near end, the sign with the
warehouse's number on the far wall, six enamel lamps on cables over the rows, skylights in every other span.

**Every surface is its own material** (`wh_floor`, `wh_wall`, `wh_upper`, `wh_roof`, `wh_door`, `wh_steel`, the sign's
`wh_digit_tens` and `wh_digit_units`, `wh_skylight`, ...), and every picture any look wears is in the model. A look
(`warehouse.json`, `looks`) says which picture each material wears and the lamps' colour; warehouse n wears look n - 1
in turn (red brick, old factory, whitewashed, block shed, yellow brick). The lot's viewport dresses the standing scene:
it points each material's `txDiffuse` at its picture and refreshes it, so going to the next warehouse swaps the cars
and the dressing, never the scene.

**Light** follows the game's clock (`StreetLights.At`: day, dusk, night): the key light comes from overhead through
the skylights, whose glow changes (`ksEmissive` on `wh_skylight`); the six lamps are always lit, stronger at night
(`lampScale`).

## Hard-won facts

- **A material can be pointed at another picture at run time**, with no AcTools change: the showroom's texture loader
  uploads every texture in the KN5 as it reads the file, whether a material uses it or not, so setting
  `Kn5Material.TextureMapping.Texture` to another name that is in the file and calling
  `ForwardKn5ObjectRenderer.RefreshMaterial(renderer.ShowroomNode.OriginalFile, id)` shows it next frame. A name that
  is not in the file shows nothing. `UpdateMaterialPropertyC` does the same for `ksEmissive`.
- **The scene's textures tile**: the Dark renderer samples a material's diffuse map with an anisotropic WRAP sampler,
  so UVs in metres over a picture's size repeat it across a wall.
- **Point lights stop dead at their range.** Lamps of range 9 over the rows left a hard-edged dark ellipse in the
  middle of the aisle, which looked like a shadow of nothing; range 14 at brightness 0.8 lights the floor evenly.
- **Nothing of the building casts a shadow** (`CastShadows = false`): the key light comes in through the roof.
- `SceneLights.Apply` returns the lamps it hung, so a scene lit again (another look's colour) takes them down first
  (`GarageRenderer.RemoveLight`).
- The builder winds every triangle to face the way its normal says (`Mesh.Facing`); no `--flip` was needed.
- Seen from the door, looking down -x, the viewer's right is -z: the sign reads from +z towards -z.

## Checking it without the game

- A console harness drawing the scene through `GarageRenderer` (`WpfMode = false`): a look, a number, a light of the
  day, cars parked in the bays, `Shot` to a PNG. It is how the lamps, the camera and the looks were set.
- A WPF harness hosting `CarLotScreenView` with fake services (a `DispatchProxy` per interface; the catalog answering
  any id) and a `GameState` of 13 real AC cars, in a window off screen, captured with `RenderTargetBitmap`:
  `StreetCorsa.App.InitializeComponent()` loads the styles, `Application._resourceAssembly` set by reflection to the
  game's assembly for its pack URIs, `Dispatcher.Run()` instead of `Application.Run()` so the game does not start.
