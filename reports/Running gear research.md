# Running gear research: differentials, drum brakes, leaf springs (2026-10-17)

A deep web search done for roadmap step 31 (`docs/roadmap.md`). Three parts: free 3D models and their licences, how
Assetto Corsa can carry the parts, real 1960s figures, and how other games do it. Items marked **[unverified]** or
**[est.]** need a check before they become game data.

## What the game has today

- `parts/brakes/stock`: 51 variants of SLRR's `disc_brake` (one mesh, `disc_brake.kn5`) and three `Brake` parts. SLRR
  has a drum script class (`java.game.parts.rgearpart.reciprocatingrgearpart.brake.DrumBrake`); one converted part
  uses it, `brakes/stock/Brake_0023`.
- `parts/suspension/stock`: SLRR's generic McPherson struts and trailing arms (four KN5s), coil springs, shocks, sway
  bars, `RGearPart` stubs.
- No differential, no live axle, no leaf spring. `Parts/Export/AcRunningGearData` scales tyres, brakes
  (`MAX_TORQUE`, `FRONT_SHARE`, `HANDBRAKE_TORQUE`) and suspensions (spring rate, dampers, track, hub mass) by
  mounted/factory part; it never touches `[TEMPS_*]`, `[ARB]` or `[AXLE]`. `[GEARS] FINAL` and `[DIFFERENTIAL]`
  come from the transmission's SLRR figures (`final_drive`, `rearend_ratio`, `diff_lock`) through `AcEngineData`.
- The cars' own AC data (108 cars of `cars.json` in `C:\GAMES\Street Rod AC`, 4 ACD-only): 63 of 104 have a rear
  `AXLE` (all 65 `[AXLE]` sections with `TORQUE_REACTION`, 15 with `LEAF_SPRING_LAT_K`); 61 cars' diff at 0.05/0.05,
  26 at 0.45/0.30; `FINAL` 3.00–4.33, median 3.55; brake temperatures on 28; `[ARB]` on all.

## 1. Free 3D models

**Nothing period-correct is redistributable.** No free Ford 9", Dana 60/44, GM 10/12-bolt, Chrysler 8¾, Kelsey-Hayes
or Budd caliper, booster, slapper or ladder bar, lever shock or Toploader/A833 exists under a licence an open-source
game can ship. The real CAD is on GrabCAD (non-commercial, internal use only).

Usable CC-BY stand-ins (licence read from Sketchfab's API; a download needs a login and gives glTF/GLB; textured or not
is unverified unless said). Each needs decimating, re-texturing and a credit line.

| Part | Model | Author | Tris | Notes |
|---|---|---|---|---|
| Drum brake | [Low Poly Brakes](https://sketchfab.com/3d-models/08c70a12956f4775bd67de898be343aa) | themodel_finder | 4.5k | Has a drum; textures in a separate zip |
| Brake shoe | [Brake Shoe (3D scan)](https://sketchfab.com/3d-models/c2b2e708f2fd4bd297c353776abb39a9) | adderecreations | 100k | Scanned, textured |
| Master cylinder | [Brake Master Cylinder](https://sketchfab.com/3d-models/1d331c9876c846f0a1c6a4d7ad8e9b47) | Antonio36996 | 50k | Check the shape |
| Calipers, rotor, hub | [caliper](https://sketchfab.com/3d-models/a68ccf22cca946d08d03d9e345b5d8e6), [rotor hub](https://sketchfab.com/3d-models/750888ec63e042a693855e04c1050212), [wheel hub](https://sketchfab.com/3d-models/4234fdc248af45d18d127e9ae5fef7ca) | vr.designer_09 | 14–73k | Modern shapes |
| Leaf spring | [LEAF SPRING](https://sketchfab.com/3d-models/d61f9656d02d4fcaa5f67999415ffcec) | vr.designer_09 | 27k | Multi-leaf |
| Leaf spring | [Heavy-Duty Truck Rear Leaf Spring Suspension](https://sketchfab.com/3d-models/5817fe005ac9424b899ba9458fe869d0) | vinrax | 110k | 8 leaves with axle mounts |
| Leaf spring | [Leaf Spring_ Assem](https://sketchfab.com/3d-models/16dc589fe31f4e68831e9c1c7afed6d2) | WHV-3DP | 93k | Assembly |
| Rear axle | [axle](https://sketchfab.com/3d-models/4699fa66f2484905a152921790e5cf86) | fasteng | 2.8k | "Simple axle with diff", placeholder |
| Rear axle | [rear-axle](https://sketchfab.com/3d-models/537ffc649a5348859428e100d773495c) | fasteng | 48k | |
| Axle shaft + hub | [Axle Damage and Loosing Wheels](https://sketchfab.com/3d-models/272696492bfc417ab7ca301f6c6445d3) | Rysky | 51k | Live axle, a broken shaft shown |
| Diff gears | [Differential Gear box](https://sketchfab.com/3d-models/1e3b1c25370b467887d982c1011f3b1e) | Anoopbg | 19k | Ring, pinion, spiders, carrier |
| Diff pieces | [Differential Part1–5 + Assembly](https://sketchfab.com/3d-models/a3a60602f268430991096e31519c054c) | designwithboi | 3–240k | Separate pieces |
| Diff housing | [Jaguar differential (scan)](https://sketchfab.com/3d-models/0d212a2b3d2345fbb24bc94470b478b5) | peel-3d.com | 394k | Real casting, IRS |
| Coil spring | [Suspension Coil](https://sketchfab.com/3d-models/f9e9d2bd5ab143d3bd64bd2c27e9d9cb) | jolle.kemppainen | 12k | |
| Shocks | [Shock Absorber](https://sketchfab.com/3d-models/b670e95311e64e0e90f9be82a743725a) | fasteng | 10k | |
| Control arms | [Sheet Metal Control Arms & Brackets](https://sketchfab.com/3d-models/bd3678dda84340fabbca9039ecb1c273) | matt.prsns | 610k | Stamped, right for the era, heavy |
| Torsion bar | [TORSION BAR](https://sketchfab.com/3d-models/ec123b7ec46a4dd2b13efec95be8a994) | vr.designer_09 | 38k | Not a Mopar hex-end bar |
| Driveshaft | [Driveshaft](https://sketchfab.com/3d-models/09b38796c78e49d6bbaf6b7fa63b856f) | PolyPhantom | 17k | ZIL-157 shaft with U-joints |
| U-joint | [Working Universal Joint](https://sketchfab.com/3d-models/145bf8667e0e4ca3b9430e89fd1991ba) | fasteng | 8k | |
| Gearbox | [Muncie AS](https://sketchfab.com/3d-models/1a399cc014504c99b2fbe268c9b93548) | ALEE.SAEFE | 49k | Probably a Muncie 4-speed; preview first |
| Quick-change cover | [Printables 680300](https://www.printables.com/model/680300) | PRUESS | STL | CC-BY, period hot-rod hardware, page unseen |

Empty sources: Poly Haven, ambientCG, Kenney, Quaternius, OpenGameArt, BlendSwap, NASA/Smithsonian 3D; Printables'
full-size axles are NonCommercial.

### Licence red flags

- **GrabCAD:** non-commercial, internal use ([terms](https://grabcad.com/terms)). It also taints models made from it,
  e.g. the Sketchfab [Hypoid Gear 35:7](https://sketchfab.com/3d-models/6592b808326541dd8b887bcf3da44619).
- **Sketchfab "Standard"/"Free Standard":** use in a game is fine, shipping the raw files in a public repo is not.
  Exception to ask about: unityfan777's [modular suspension](https://sketchfab.com/3d-models/cad145dce7444fcab5083a8e405df6cb)
  is labelled Standard but its description says CC0.
- **NonCommercial / NoDerivatives:** out (Rysky's stacked differentials, UnforgettableName's control arm and
  transmission, Laserdesign's caliper scan, Autotechie's differential, makerbot's shock spring).
- **TurboSquid, CGTrader, RenderHub, 3DCADBrowser, BlenderKit "royalty free":** no redistribution of source files.
  Only BlenderKit's `cc_zero` assets are safe.
- **CC-BY-SA** (Printables U-bolts, ring and pinion): fine, but keep them in their own folder and share alike.
- **Every CC-BY item needs a credit** (title, author, link, licence).

### Making our own (recommended)

- **Leaf springs:** no add-on exists. A script: N leaves as arcs of falling length (count, length, arch, width 2"/2.5"),
  eyes with bushings, centre bolt, rebound clips, U-bolts, plate, shackle, hanger. Mono-leaf is one leaf.
- **Drums, backing plates, axle tubes, banjo housings:** lathe profiles (CadQuery revolve or Blender Screw) plus a few
  booleans. What sets the families apart: the 9"'s drop-out third member at the front, the 12-bolt's and 10-bolt's
  rear covers (12 or 10 bolts), the Dana 60's big cover, the 8¾'s removable carrier.
- **Gears:** [cq_gears](https://github.com/meadiode/cq_gears) (Apache-2.0, CadQuery: spur, ring, bevel), OpenSCAD
  BOSL2 `bevel_gear()` (BSD-2), FreeCAD Gears workbench. None does true hypoid; a spiral bevel looks right. Generated
  geometry is ours whatever the tool's licence.
- **Coil springs:** a helix with a bevel (Blender), or Blender's Extra Mesh Objects extension.

## 2. Assetto Corsa

Main source: rustyAC, a reverse-engineering of AC's exe checked against 113 cars
([brakes.md](https://github.com/thesupersupersigma/rustyAC/blob/main/docs/map/brakes.md),
[drivetrain.md](https://github.com/thesupersupersigma/rustyAC/blob/main/docs/map/drivetrain.md),
[suspension.md](https://github.com/thesupersupersigma/rustyAC/blob/main/docs/map/suspension.md)). Not official, but
exact. Another reimplementation reads the same keys: [projectd-core](https://github.com/ColleyLi/projectd-core).

### Differential (`drivetrain.ini`)

- `[GEARS] FINAL`: the ring and pinion. Overall ratio = FINAL × gear.
- `[DIFFERENTIAL] POWER`, `COAST`, `PRELOAD` (Nm). Locking torque = |ratio| × POWER (or COAST) × input torque + PRELOAD.
  Open = 0/0/0. **POWER ≥ 1 and COAST ≥ 1 together make a spool** (both wheels at one speed). AC's only two types are
  LSD and spool.
- CSP adds no documented diff type; `script.lua` physics scripts could add forces **[unverified that nothing exists]**.

### Brakes (`brakes.ini`)

- `[DATA] MAX_TORQUE` (Nm): each front wheel gets MAX_TORQUE × FRONT_SHARE, each rear MAX_TORQUE × (1 − FRONT_SHARE).
  `HANDBRAKE_TORQUE` per rear wheel.
- **Temperature and fade are vanilla** (22 of 113 Kunos cars): `[TEMPS_FRONT]`, `[TEMPS_REAR]` with `TORQUE_K` (°C per
  kJ), `COOL_TRANSFER` (1/s), `COOL_SPEED_FACTOR` (per km/h), `PERF_CURVE` (a LUT of °C → torque multiplier). No drum
  model, but a drum is slow cooling, high `TORQUE_K` and a curve that falls early.

### Suspension (`suspensions.ini`)

- Types `DWB`, `STRUT`, `AXLE` (rear only), `ML`, CSP's `COSMIC`.
- Per axle: `SPRING_RATE` (N/m), `PROGRESSIVE_SPRING_RATE`, `ROD_LENGTH` (m, ride height), `BUMPSTOP_UP/DN`,
  `BUMP_STOP_RATE`, `DAMP_BUMP`, `DAMP_REBOUND`, `DAMP_FAST_BUMP`, `DAMP_FAST_REBOUND`, thresholds, `HUB_MASS` (the whole
  axle's mass for AXLE).
- `[ARB] FRONT`, `REAR` (N/m).
- **Live axle:** `[REAR] TYPE=AXLE` and `[AXLE]`: `LINK_COUNT` (4 in every Kunos car), `J<i>_CAR`, `J<i>_AXLE`,
  `TORQUE_REACTION` (drive torque into axle versus body: axle wind-up), `LEAF_SPRING_LAT_K` (N/m, needs `[HEADER]
  VERSION ≥ 3`, Kunos uses 350000), `ATTACH_REL_POS` (VERSION ≥ 4). Kunos examples: `ks_alfa_romeo_gta`,
  `ks_ferrari_250_gto`. Modder's thread: [overtake live axle](https://www.overtake.gg/threads/live-axle.138789/).
- **No leaf element** in AC or CSP (Cosmic has torsion bars, no leaves). A leaf car is AXLE with 4 links standing in
  for the spring eyes, `LEAF_SPRING_LAT_K` and `TORQUE_REACTION`.
- CSP: `DAMPER_LUTS=1` with `dampers.ini`, `[ARB] EXTEND=1`, all needing `car.ini [HEADER] VERSION=extended-…`.
- **Rewriting a car from DWB/STRUT to AXLE geometry is risky.** Leaf parts should change rates and wrap only on cars
  that are AXLE already.

## 3. Real 1960s figures

### Rear axles (weakest to strongest)

| Axle | Ring | Notes |
|---|---|---|
| GM 10-bolt car | 7.5"/8.2" | Weakest common GM unit |
| BOP 10-bolt | 8.2–8.5" | Pontiac Safe-T-Track (Dana, Eaton or G&A carriers), ratios from 2.56 |
| Ford 8" | 8" | Falcon, Fairlane, Mustang, 28-spline; weak behind big power **[unverified]** |
| Chrysler 8¼ | 8.25" | From about 1969, small engines **[unverified]** |
| GM 12-bolt car | 8.875" | 1965–72, 30-spline; Eaton Posi carrier breaks 3.08–3.90 / 3.73 up; ~700 hp; 2–3% less drag than a 9" |
| Chrysler 8¾ | 8.75" | Pinions 741 (weak), 742, 489 (strong, 1969–74); Powr-Lok, then cone Sure-Grip |
| Ford 9" | 9.00" | 1957–86, straddle-mounted pinion, 28 or 31 spline, drop-out third member, most friction |
| Dana 60 | 9.75" | Ratios from 3.54 (3.54/4.10/4.56/4.88 **[unverified]**); 20–40 lb heavier; Hemi/440 four-speeds |
| Dana 44 | 8.5" | Some AMC |
| Big GM 12-bolt | 9.3" | Full-size Olds/Buick/Pontiac 1967–75, 2.41–2.73; not the Chevy 12-bolt |

Sources: [Moser](https://www.moserengineering.com/choosing-a-rear-end-ford-9-inch-vs-chevy-12-bolt-vs-dana-60/),
[OnAllCylinders](https://www.onallcylinders.com/2018/10/05/dana-60-vs-gm-12-bolt-vs-ford-9-pros-cons-popular-rear-end-assemblies/),
[Ford 9-inch](https://en.wikipedia.org/wiki/Ford_9-inch_axle), [8¾ guide](https://www.bigblockdart.com/techpages/875axle.shtml),
[Wallace axle codes](http://www.wallaceracing.com/axledata2.htm).

**Diffs:** clutch-pack posis (Eaton Posi, Dana Powr-Lok/Trac-Lok, Ford Equa-Lok), cone types (Ford Traction-Lok,
later Sure-Grip), Detroit Locker (locked under power, ratchets free on overrun, clanks, pushes in corners;
[offroaders](https://www.offroaders.com/tech/Detroit-Locker-automatic-lockers.htm)), spool (welded or solid).

### Brakes

Car Life tests, deceleration from 80 mph ([Curbside Classic](https://www.curbsideclassic.com/vintage-reviews/1960s-vintage-reviews/gm-brands-1960s-vintage-reviews/vintage-car-life-review-1967-pontiac-gto-ram-air-king-of-the-supercars/),
[Tunnel Ram](https://www.tunnelram.net/news-blog/2021/7/15/car-life-road-test-1965-thunderbird)):

| Car | Brakes | Result |
|---|---|---|
| 1967 GTO | 9.5" drums | Severe fade after one stop |
| 1967 GTO | Front disc | 0.90 g, 8 stops, little fade |
| 1964 Thunderbird | Drums | Almost nothing left after 2 stops |
| 1965 Thunderbird | Kelsey-Hayes disc | 0.81–0.87 g over 10 stops |
| 1965 Bonneville | 11" drums | 0.62 g, heavy fade |
| 1967 Mustang, 1966 Galaxie | Front disc | 0.84–0.90 g, slight fade |

- Drums: 9×2–2.25 (compacts), 9.5×2.5 (GM intermediates), 10×2.5 (Ford/Mopar intermediates), 11×2.75–3 (big, police,
  Hemi) **[unverified sizes]**. Bendix duo-servo: self-energising, fade-prone.
- Linings: organic; sintered metallic (poor cold, good hot) **[unverified detail]**.
- Discs: Ford Kelsey-Hayes 4-piston from 1965 ($58 on V8 Mustangs, standard on the GT); GM Delco-Moraine 4-piston
  from 1967 (Camaro J52; J56 adds metallic rear shoes); Mopar Budd/Kelsey **[unverified]**.

### Suspension

- Rear leaf: 1965 Mustang K-code 101 → 130 lb/in; Corvette F40/F41 550/305 (front/rear). Bands: soft ~90–110 lb/in,
  HD/police ~130–160, Super Stock ~160–200 **[unverified]**. 1 lb/in = 175.1 N/m.
- Mopar torsion bars: A-body .85/.87/.89/.92, B-body .86/.88/.90/.92, aftermarket 1.03" (230 lb/in A, 210 lb/in B;
  [FABO](https://www.forabodiesonly.com/mopar/posts/1970631804/printfriendly)).
- Sway bars: 1967–69 Camaro 13/16" front, 7/8" rear; GM A-body F41 15/16"/7/8"; 1971–73 Mustang HD 1-1/8".
  Stiffness grows with the fourth power of diameter.
- Traction aids: Traction Master, Lakewood bars, slapper bars, ladder bars: they stop axle wrap and wheel hop.

## 4. Other games

- **Car Mechanic Simulator 2021** (1957 and 1969 cars, [parts list](https://www.yekbot.com/car-mechanic-simulator-2021-all-car-parts-list/)):
  brake drum, brake shoe, drum brake cylinder; leaf spring, leaf spring plate, 4 U-bolts; leaf-spring rear drive axle,
  axle shafts, knuckle housings, hubs, shocks, rubber bushings. The rear diff is not its own part. Every part only has
  a condition %. Players complain about the bolt clicking.
- **My Summer Car:** the final drive is a garage service (3.700 to 4.625, stock 4.286).
- **My Winter Car:** open or LSD diffs, final drives 3.44 to 4.44.
- **BeamNG:** diffs open, LSD, viscous, locked (spool or welded); `gearRatio` swapped by part.
- **Automation:** solid axle with coils or with leaves; leaves cheaper and locate the axle less well; drums fade.
- **SLRR mods:** nothing found with leaf springs, solid axles or rear diffs.
- **Open-source projects:** Rigs of Rods, Speed Dreams and VDrift (GPL) have no useful running-gear meshes. Project
  Chrono (BSD-3) has leaf-spring axle parameter files (`data/vehicle/uaz/suspension/UAZBUS_RearSAELeafspringAxle.json`),
  a physics reference only.
