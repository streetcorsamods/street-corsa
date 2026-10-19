# Models for a gas station of our own (2026-10-18)

Ideas batch 11, idea 2: the station in place of the Bannochbrae cut (Kunos/Rainmaker content, never shipped), built as the
garage was: a scene of our own dressed with models we may give away, an animated attendant by the driver's window. The
Bannochbrae cut stays a layout reference only (the road along the front, the way in, two pump islands under a canopy, the
building behind, the way out).

Searched: Sketchfab (its public search API, downloadable, CC BY or CC0), Poly Haven's model list (all CC0), Quaternius,
Kenney, Poly Pizza. Every Sketchfab licence below was read off the model's own record; the thumbnails were looked at.
CC0 gas station models do not exist on Sketchfab (one NASA rock came up); everything from there is CC BY 4.0, to be named
in the Settings credits as the garage's Sketchfab props are. Nothing CC BY-NC or ND is listed.

## The picks

| Role | Model | Maker, licence | Size | Why |
|---|---|---|---|---|
| **The building** | [Vintage Gas Station](https://sketchfab.com/3d-models/vintage-gas-station-107d8dd7948040e0b8fee2bede0fc2b4) | Wardster, CC BY | 6.1k faces, 42 textures | A Streamline Moderne office with a curved glass corner, "PETROL" in relief over the door, a stone apron: the fanciest 50s/60s station building found, light enough to stand close to. The lettering goes under our own make's name. No canopy or pumps in it. |
| **A canopy, if we want a modelled one** | [old Gas Station](https://sketchfab.com/3d-models/old-gas-station-ad5f6816d35a4944a034b80443a62cfa) | Wardster, CC BY | 14.8k faces, 27 textures | A flat canopy on two round columns with fluorescent fittings underneath, and a plainer office with a "GAS" pylon sign; abandoned textures (rust, grime) we would have to repaint. My call: build the canopy ourselves in code, as the garage room is, with Poly Haven textures, and skip this one. |
| **The pumps** | [Vintage Gas Pump](https://sketchfab.com/3d-models/vintage-gas-pump-0096f206ce1a4381bea58f9f94fc6b1c) | Rescue3D Assets, CC BY | 7.6k faces, 11 textures | A red and cream 1950s pump with a lit globe, clean, game-ready, and branded with a made-up name ("Bexar Oil", "Fire Boss"): no trademark to paint over. Two or four of them. |
| **A second pump, for variety** | [Vintage "Super Shell" Gas Station Pump](https://sketchfab.com/3d-models/vintage-super-shell-gas-station-pump-d1fb8c946c85471cb398d35e6947c25c) | Plaxa, CC BY | 5.6k faces, 4 textures | A clean white and yellow pump; the Shell logos in its texture would be repainted with our own make. Optional. |
| **The pole sign** | [Gasstation Sign](https://sketchfab.com/3d-models/gasstation-sign-11416edc56e941b7b10bcc87a6dc0ee7) | Mark Peters, CC BY | 1.4k faces, 4 textures | "FUEL · SERVICE · REPAIRS / TRAVEL STOP / LAST GAS FOR 50 MILES / GASOLINE" on a pole, the bottom board lit: period, no brand. |
| **The air meter** | [Old Tire Inflator](https://sketchfab.com/3d-models/old-tire-inflator-3ab1bef1829b44a0857f0287a670c5f0) | gorzi, CC BY | 27k faces (two texture sets: new and damaged) | The red 50s "Eco" air meter on its round base, as every station had by the pumps. Heavy: brought down to about 4k by compose_props.py's budget. |
| **A soda machine** | [Vintage Soda Machine](https://sketchfab.com/3d-models/vintage-soda-machien-05b7b54f12a74e659a8561614e63aee2) | Han1993, CC BY | 3.3k faces | A green 7 Up chest machine by the office door; the logo would be repainted. Optional. |
| **The attendant** | [Speaking Man with Talking Gestures](https://sketchfab.com/3d-models/speaking-man-with-talking-gestures-c55bcf894e1b4da1adf0cf258fe94c9f) | LasquetiSpice, CC BY | 15.7k faces, 2 textures, a 25 s looping talk animation of the author's own | A middle-aged man in a sweater and slacks who talks with his hands: by the window, that is a man chatting while the tank fills. His sweater and slacks get repainted as a light work shirt and dark trousers (two textures); a cap is a small model of our own. The animation is the maker's, not Mixamo's. |

Also worth having, all **Poly Haven, CC0**, fetched by script like the garage's (no download for you):
`metal_trash_can`, `painted_wooden_bench` and `modular_street_seating` (a bench by the door), `standing_chalkboard_01`
(today's prices), `steel_frame_shelves_01` (an oil rack by the pumps, with `oil_tin`, `small_oil_can_01`, `can_rusted`,
`cleaner_tin_01`, `lubricant_spray`), `old_tyre` and `rusted_wheel_rim_01`/`02` (a stack by the wall), `metal_jerrycan`,
`Barrel_01`, `wooden_crate_01`, `hand_truck`, `covered_car` (a car under a tarp beside the building), `mounted_fluorescent_lights`
(under the canopy), `street_lamp_01`/`02`, `security_light` (over the side door), `modular_electricity_poles` (along the
road), `modular_chainlink_fence` (the back of the lot), `fire_hydrant`, `utility_box_01`, `wall_clock`, trees and bushes
for the backdrop (`tree_small_02`, `island_tree_01`..`03`, `jacaranda_tree`, `shrub_01`..`04`, `grass_medium_01`).

## What else was looked at, and why not

- Nick Samarin's [Vintage Gas Station](https://sketchfab.com/3d-models/vintage-gas-station-10812390a8ca490881c2ea0d2249de57)
  (CC BY, 22k): a pump, not a station, yellow and red "Full Service"; heavier than Rescue3D's for the same thing.
- Icaro Arrigoni's [50's Abandoned Desert Tank Station](https://sketchfab.com/3d-models/50s-abandoned-desert-tank-station-9aa92c9729c64a8d879160142b8b9a2d)
  (CC BY, 25k): hand-painted, stylised and abandoned; its green canopy is the wrong style next to the cars.
- CGMeller's [Old Gas Station](https://sketchfab.com/3d-models/old-gas-station-e1d067211c8a4226b2393de0db8dad36) (CC BY,
  28k): a whole desert diorama (road, poles, a modern-ish shop) in one piece; hard to cut apart, modern pumps.
- Elbolillo's PSX [Gas station](https://sketchfab.com/3d-models/gas-station-eeb913b90b4344ddbd7852f82a7ef160) and props:
  a modern Pemex-style station.
- Denis Deline's draft station (a pump, untextured), Natalie Crabtree's Gas Station Noir (no UVs, black and white),
  BOXjordiHEAD's 50s Classic Gas Station (voxel, not downloadable).
- Pumps with real brands in their textures, all CC BY: Artem Goyko's Texaco Fire Chief (6.9k, clean and lovely),
  NJCreates3D's Gulf (5k, rusted), J P Cardona's Mobilgas (rusted), Harry Bond's Ethyl (rusted), Mark Peters' Shell
  self-service (modern). Rusted ones do not suit a working station; the branded clean ones need their logos painted
  over, which Plaxa's needs least.
- Soviet pumps (Kozlov, C0d37g), a Soviet soda machine (Silvestr): the wrong side of the world.
- Phone booths: Enivlens' 60s booth is a first model (wonky), ZwiebelGames' payphone is modern.
- Tyre stacks: ZiemniaQ's scan (2.5k, CC BY) is good, but Poly Haven's `old_tyre` is in the game already.
- Attendants: Agor_2012's cartoon men (CC BY, idle animations from Mixamo, whose terms do not allow passing the
  animations on as an asset), Théo Lerbeil's old man (cartoon), Razvan Savescu's low-poly man (a plain base mesh),
  zhuoyi0904's standing man (68k, T-pose). No Sketchfab model of an attendant or a mechanic in uniform.
- **CC0 fallback for the attendant**: Quaternius' [Universal Base Characters](https://quaternius.com/packs/universalbasecharacters.html)
  (CC0, glTF, humanoid rig, untextured) with his [Universal Animation Library](https://quaternius.itch.io/universal-animation-library)
  (CC0; 45 of its animations free, the list not published). A uniform would have to be painted on. Kenney's Animated
  Characters 3 (CC0, idle) and Quaternius' Ultimate Animated Character Pack (CC0, 52 characters) are blocky: wrong next
  to the cars.
- Poly Haven has no gas pump, canopy or sign; its 521 models were scanned by category.

## How it would be built

- **The scene** (`tools/StationScene`, rewritten): the forecourt, the road along the front with its kerbs and a grass
  verge, the two pump islands, the canopy on steel columns (its underside lit at night) and the lot's edges built in
  code from Poly Haven textures as `GarageSceneBuilder` builds the garage; the building, the pumps, the sign and the
  rest placed by data as the garage's props are (a `places` entry for `station`), so a mod re-dresses it. The ground
  grid and the three sky domes as today. The Bannochbrae layout (road along x, the car stopping between the islands
  facing +x, the driver's door to the near island) is kept, so `station.json`'s paths need only small changes.
- **The attendant**: a skinned model with a baked bone animation. AcTools' renderer has skinned meshes and the
  `ksanim` animator (both in the DLL), so the prop pipeline gets a skinned path: Blender exports the mesh with its
  bone weights and the animation's frames, PropBuilder writes a skinned KN5, the viewport moves the bones each frame
  (as the lift post is moved) and loops the talk while the car stands at the pump, head turned to the window.

## To download (Sketchfab needs a login), into `D:/JUEGOS/3D Models/station/` as glTF zips

1. Wardster, Vintage Gas Station
2. Rescue3D Assets, Vintage Gas Pump
3. Mark Peters, Gasstation Sign
4. gorzi, Old Tire Inflator
5. LasquetiSpice, Speaking Man with Talking Gestures
6. Optional: Plaxa, Vintage "Super Shell" Gas Station Pump; Han1993, Vintage Soda Machine; Wardster, old Gas Station (its canopy)
