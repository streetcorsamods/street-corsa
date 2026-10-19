# The land round the gas station (2026-10-19)

The owner found the station itself fine and its surroundings flat: bushes, trees, electric poles, some terrain, the
animated grass AC tracks have, and parked cars and trucks if there are models we may give away. What was found, what
was built and what waits for a download.

## How AC tracks do grass and trees, and what our renderer can do

- **Grass in AC.** Vanilla tracks carry grass as meshes of the `ksGrass` shader (blades on cards, swaying in the vertex
  shader). Custom Shaders Patch adds **GrassFX**: no mesh at all, the patch grows grass procedurally round the camera
  on the surfaces a track's `ext_config.ini` names (`[GRASS_FX]`, `GRASS_MATERIALS` or `GRASS_MESHES`, `OCCLUDING_*`
  for the road, `ORIGINAL_GRASS_MATERIALS` to hide the track's own, `MASK_*` thresholds for which colours of ground get
  it). Trees are billboards of the `ksTree` shader, turned to the camera.
- **The station is not drawn by AC.** It is drawn by the game's own window through AcTools' renderer (Content
  Manager's), so neither GrassFX nor `ksGrass` is available there: AcTools knows `ksTree` only as a plain textured
  material (no turning, no wind), has no `ksGrass`, and its shadow pass draws every triangle whole (no cut by alpha).
  Two more findings cost a build each: AcTools' multi-map shader (`ksPerPixelMultiMap`, which every prop and scene of
  ours used) takes a cut-out's alpha from the **normal map**, not the picture, so every alpha-cut material we ever made
  (the fence's bushes, a road marking) was drawn as its whole square; the plain `ksPerPixelAT` shader cuts by the
  picture. `tools/Shared/ObjModel.cs` now writes cut-outs and blended materials with the plain shaders.
- **What was built instead.** Grass clumps, bushes and trees as pictures on crossed cards (how a track draws the trees it
  never drives up to), baked in Blender from Poly Haven's scans (`tools/StationScene/bake_cards.py`: a scanned tree of
  a million triangles becomes two 1024-pixel pictures); the wind as the game leaning the cards' nodes (a shear about
  the foot, `GarageRenderer.Wind`, `WindSway`: a breeze with gusts rolling along the road and a flutter), 12 m cells
  of grass to a node near the forecourt and every tree its own, 700-odd nodes moved a frame. The cards cast no shadow
  (their square would); each tree gets a soft blended shade on the land instead.

## Models we may give away

Searched: Sketchfab's search API (downloadable, the licence read off each model's record; everything below is CC BY
4.0 unless CC0 is said), Poly Haven's model list (all CC0), Kenney and Quaternius (CC0, but blocky next to the cars).

**Used, from Poly Haven (CC0, fetched by the build, no download for you):** `island_tree_02` (the tree on the cards,
scaled up to 9–13 m), `searsia_lucida` a and b (bushes on cards), `shrub_02` a (a shrub on cards; b and c stay real at
the fence), `grass_medium_02` c, d, e (the grass clumps), `shrub_03` a–d (weeds), `wild_rooibos_bush` a, b (small
bushes), `boulder_01`, `dead_tree_trunk_02` (a fallen trunk), `tree_stump_01`; the poles are still
`modular_electricity_poles`, with wires of our own. Poly Haven's other trees (`jacaranda_tree` 215 MB,
`island_tree_01` 66 MB, `tree_small_02` 101 MB, the pines of millions of triangles) were left: too big to fetch for a
card. There are no cars or trucks on Poly Haven (`covered_car` is in the scene already).

**Wanting a download (Sketchfab, a login; all CC BY 4.0, the maker to be named in CREDITS.txt and the station's
credit).** `build_station.py` takes each when its zip is in the folder (`OPTIONAL`) and places it
(`compose_station.py`'s `VEHICLES` and the tree packs). The owner downloaded the six cars and the lilac pack the same
day; all seven are in the scene (the Buick without its "55 Buick" label, the Studebaker without its display plinth,
the lilac pack's three big bushes beside the trees nearest the lot and behind the fence, since its "single trees"
are saplings), and a car whose nose comes out backwards is turned with `--turn <id>=180`. The maple and oak packs
were not downloaded. The owner then added two tow trucks, both CC BY 4.0: -X-ScornGames' [Wrecker](https://sketchfab.com/3d-models/wrecker-id105-645-73-2b594f46dd0e42faab75420dd7015dad)
(a 500k-triangle scan, brought down to 60k: the station's own, behind the office along the fence) and 3DMODEL's
[Heavy Duty Tow Truck](https://sketchfab.com/3d-models/heavy-duty-tow-truck-3d-model-246fc01e05d6465eadeac4ea0d292e4b)
(a million triangles made by Tripo, an AI model maker, with one baked picture; brought down to 60k, pulled over on the
roadside past the way out).

| Role | Model | Maker | Size | Where it goes |
|---|---|---|---|---|
| The mechanic's pickup | [Low Poly Car - Chevrolet C10 Pickup 1963](https://sketchfab.com/3d-models/low-poly-car-chevrolet-c10-pickup-1963-679354c151984747bb74310ec5af8995) | roh3d | 16.5k faces, 12 textures | beside the office, nose to the road |
| A customer's truck | [Rancher '50 Custom - Low Poly model](https://sketchfab.com/3d-models/rancher-50-custom-low-poly-model-7f75854aba96417f8279bd9f1b24e6b2) | DanielZhabotinsky | 17k faces | at the far island |
| A customer's car | [1955 Buick Special Downloadable](https://sketchfab.com/3d-models/1955-buick-special-downloadable-1a6d42ef69294498937aab039ececb00) | robinmikart | 77k faces, 5 textures | waiting by the way out |
| A parked car | [Studebaker - Car Model](https://sketchfab.com/3d-models/studebaker-car-model-c7578ec0f82043e6b392fcdf9b00c743) (1951 Starlight coupe) | Allie2k | 71k faces | the forecourt's west end |
| A car on the shoulder | [1955 American Sedan (Packard based) FREE](https://sketchfab.com/3d-models/1955-american-sedan-packard-based-free-28d583f8d4bd4ce39dac2869fb77a197) | robinmikart | 69k faces, 28 textures | the far shoulder, 100 m west |
| A taxi | [FREE 1950s American Taxi (based on Checker)](https://sketchfab.com/3d-models/free-1950s-american-taxi-based-on-checker-d1b10defaed94a1094419e84ca9b0bf1) | robinmikart | 69k faces, 30 textures | the grass past the lot's east end |
| Real trees near the lot | [Lilac bush pack](https://sketchfab.com/3d-models/lilac-bush-pack-12-vars-lods-game-ready-10312697ec994fc99355cb94f1963a2e) (12, LODs), [Maple trees pack](https://sketchfab.com/3d-models/maple-trees-pack-lowpoly-game-ready-lods-b5d2833c258f4054a01ee2b4ef85adf0) (12, LODs), [Oak trees pack](https://sketchfab.com/3d-models/oak-trees-pack-17var-lods-seasons-gameready-a5e4e64f9f1d4089bdcc6170a9333393) (17, LODs) | lolipop_1707 | 63k, 152k, 409k faces | the nine spots nearest the lot (`NEAR_TREES`), the second LOD of each; cards of the Poly Haven tree stand there otherwise |

Also seen, not taken: robinmikart's 1955 Packard convertible, 1951 Buick coupe, 1950s police car and postal van (the
same series, 60–110k each); Lexyc16's Mercedes 300 SL (96k); Pixmani's 1958 Plymouth Fury (181k, a draft); freeReef's
AC Cobra 289 (19k, made for Poser); kryik1023's raw scans of farm trucks and a 1952 Chevrolet (2.5M triangles each);
kird3rf's Hot Rod Pickup (532k); caboose3d's Telephone Poles (5.3k, good, but Poly Haven's poles are in already); Yag1z's
three dead English oaks (28k; a dead tree or two would suit the back); fangzhangmnm's Pine Forest (45k, stylised).
Cars with the wrong era or style: whisper.alt's box truck (1990s Japanese), the VW Kombi (cartoon), the Soviet cars.

## What is in the scene now

`stations/crossroads/station.kn5`: the land as a 4 m grid over ±170 m (`land()`: flat round the lot, the road the
whole way and the driveways, then value noise of ±2 m with a rise to 7 m behind the station, 5 m beyond the road and
6 m at the edge of the world, so the sky dome's seam hides), the forecourt and the road laid on it; about 2,400 grass
clumps (the verges, the lot's edges, then thinner out to the edge); 130-odd tree cards in two rows along the rise at
the back, clumps beyond the road and along the sides, nine nearer the lot; bush cards in the grass; weeds, small
bushes, boulders, a fallen trunk and stumps as real geometry near the lot; nine poles along the far side of the road
with three wires a span and a service drop over the lot's corner to the office. `ground.bin` follows the land, so a
car never drives there and the attendant stands where he did.
