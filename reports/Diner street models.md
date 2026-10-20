# Models for a Los Angeles diner street (2026-10-20)

The diner's street (`streets/mainstreet`, docs/screens/diner.md) reworked to look like Los Angeles in the late 1960s and
early 1970s: the diner on a street corner with a cross street, the racers' cars nose-in along its front (and along
the side street, if there is room), and the player parked at the curb across the street looking at it, as in the
original Street Rod. Built as before: a KN5 made in Blender from models we may give away.

Searched: Sketchfab (its public search API, downloadable only, about 130 searches plus the full downloadable lists of
the makers who turned up most: roh3d, Daniel Zhabotinsky, robinmikart/Libau Media, Streetlights & ETC, Signalrenders,
A9908244, Wardster, novusod, jimbogies, exiS7-Gs, Raphael Escamilla, Mark Peters, Caboose3d, TampaJoey,
nickheitzman), and Poly Haven's model list (all CC0). Every Sketchfab licence below was read off the model's own
record (`/v3/models/<uid>`), and the thumbnails were looked at. Everything from Sketchfab is **CC BY 4.0** (no CC0
models of this kind turned up there), to be named in CREDITS.txt and the street's credit as the station's are. Nothing
NC, ND or "standard" is listed; game rips are called out and left out. "Faces" is Sketchfab's count, which is
triangles.

Already on disk and reusable (not suggested again as new): in `D:/JUEGOS/3D Models/station/` roh3d's C10 1963,
DanielZhabotinsky's Rancher '50, robinmikart's 1955 Buick, Packard and Checker-style taxi, Allie2k's Studebaker, the
two tow trucks, `wrecked_cars_2`, lolipop_1707's lilac pack, and the whole gas station set (Wardster's Vintage Gas
Station and old Gas Station, Rescue3D's pump and nozzle, Plaxa's Shell pump, Mark Peters' sign, gorzi's air meter,
Han1993's soda machine): that set is a ready **gas station corner** across the cross street. In `scenes/` the
Quaternius Downtown City MegaKit (road, sidewalks, the asphalt) and WaajaaH's diner. Poly Haven's
`modular_electricity_poles` are in the station already.

## The diner itself

No Googie coffee shop of the Johnie's / Norms / Pann's kind (the upswept roof, the angled glass, the stone wall, the
pylon sign) exists on Sketchfab under CC BY or CC0. What came closest:

- A9908244's [Diner Sign](https://sketchfab.com/3d-models/diner-sign-a628a61b62304aedac765d9eb5fa61d3) (CC BY,
  47k faces, 25 textures at 2048): a Googie pylon, a boomerang arrow and stacked boards. That is the piece that says
  "Googie" from across the street; it goes on the corner. Its lettering is generic ("DINER"); no brand.
- WaajaaH's streamline moderne diner (on disk): 1940s rather than Googie, but period-correct for LA (moderne corner
  buildings were everywhere). Kept as the first diner, with the pylon in front of it.
- **My call for a real Googie coffee shop**: build the building ourselves in Blender (`compose_street.py` already
  models houses): a folded-plate or upswept roof, floor-to-ceiling glass on two sides, a stone or brick base and
  wall, a canopy along the parking front. A few hundred faces of geometry plus Poly Haven / ambientCG textures; nothing
  found to download is better.

For the later diners (rising difficulty):

- **Rough / industrial**: Elbolillo's [DINER](https://sketchfab.com/3d-models/diner-15789955fbe642b19994da3cd9e725a8)
  (CC BY, 55k, 92 textures at 1024): a flat-roofed diner with a red DINER sign over its parking lot, PS1-style
  textures; for an edge-of-town diner after a repaint at higher resolution. Daniel Zhabotinsky's Industrial Buildings
  Set (below), the wrecked cars on disk and a chain-link fence make the rest.
- **Beach / Sunset Strip / fancy**: novusod's [Roadside Motel](https://sketchfab.com/3d-models/roadside-motel-f52cfdfa6711424289ecc9df463acede)
  (CC BY, 15k, 13 textures at 512): a 1950s Googie motel, two storeys of rooms off a walkway, a big "MOTEL / MERMAID"
  sign: a beach-side neighbour (the sign repainted with our own name). rtaing's [The Theme Building](https://sketchfab.com/3d-models/the-theme-building-8d85bb9628cb4e679acfa64e66177f3a)
  (CC BY, 41k, 22 textures at 512): LAX's 1961 Theme Building, a landmark in the distance from an airport-side diner.
  aocrisan's [Mojave Motel Sign](https://sketchfab.com/3d-models/mojave-motel-sign-c64f69ab7a6e4674826de6600f1a61d6)
  (CC BY, 12k, 9 textures at 4096): a tall vertical MOTEL sign with "HEATING / FREE TV", no brand.

## The picks

**Sketchfab, all CC BY 4.0: the owner downloads them (a login), as glTF zips.**

| Role | Model | Maker, licence | Size | Why |
|---|---|---|---|---|
| **The diner's pylon** | [Diner Sign](https://sketchfab.com/3d-models/diner-sign-a628a61b62304aedac765d9eb5fa61d3) | A9908244, CC BY | 47k faces, 25 textures (2048) | The Googie arrow-and-boards sign, the one piece that reads "1960s LA coffee shop". Heavy for a sign: bring it to about 15k. |
| **Low storefronts** | [Midcity Buildings Set](https://sketchfab.com/3d-models/midcity-buildings-set-low-poly-model-d5cbc471d20c4c9da31c3df656648916) | Daniel Zhabotinsky, CC BY | 4.9k faces (a set), 9 textures (2048) | One-storey worn commercial blocks with big shop windows, "inspired by the low-rise commercial development of mid-city US areas": the LA boulevard between the corners. The maker gives them away unconditionally. Some brick repainted as painted stucco. |
| **A strip of shops** | [Rentable Stores](https://sketchfab.com/3d-models/rentable-stores-b62e317e129e456f884ce6be4904aab3) | Wardster, CC BY | 1.4k faces, 28 textures (4096) | Three one-storey shops under one flat roof with painted (made-up) signs. The liquor store and the barber go here: we paint LIQUOR / BEER - WINE on one board. |
| **Two-storey corner** | [Harbor Breakfast Building](https://sketchfab.com/3d-models/harbor-breakfast-building-5a7968c435934fcea47b5c5484ee4e13) | Raphael Escamilla, CC BY | 62k faces, 52 textures (1024) | A real old two-storey corner building from San Diego with a shop below; the far corner of the cross street. Its "Harbor Breakfast" sign gets painted over. Bring to about 25k. |
| **A brick block** | [Low-poly Old American brick building with shop](https://sketchfab.com/3d-models/low-poly-old-american-brick-building-with-shop-3170993b67ee4e58b27d69d5877f71bd) | Orzechu, CC BY | 3k faces, 3 textures (1024) | Two storeys of brick over a storefront: older Hollywood / downtown fill for the far side. |
| **Ranch houses on the side street** | [House: "Fleetwood" (Design 50)](https://sketchfab.com/3d-models/house-fleetwood-design-50-bd4f4d5b7fca476cb7d260a605192bc2) | AleixoAlonso, CC BY | 27k faces, 15 textures (4096) | A real 1950s National Homes ranch plan, low and long: the side street past the shops (our own `House` builder does the rest). |
| **Parked cars, 1950s-60s** | [Chrysler Saratoga 1960](https://sketchfab.com/3d-models/low-poly-car-chrysler-saratoga-1960-3fa89e6bf78b49aab111af4195efb6fe), [Cadillac 75 Sedan 1953](https://sketchfab.com/3d-models/low-poly-car-cadillac-75-sedan-1953-2935f8faa5f64ce4a3bd65af0f925c21) | roh3d, CC BY | 17.8k and 14.7k faces, 8 textures (4096) each | The same maker and quality as the C10 in the station: a finned '60 Chrysler and a black '53 Cadillac along the curbs. Badges and plates in the textures (see trademarks); plates repainted California black-and-yellow. |
| **Parked cars, 1960s-70s, made-up makes** | [Palomino Special '67](https://sketchfab.com/3d-models/palomino-special-67-low-poly-model-8419d645064e4b1699d970375e1c3cc7), [Phoenix 455 '71](https://sketchfab.com/3d-models/phoenix-455-71-low-poly-model-955edc733c6d44fabc0ad7c246a15896), [Chapman '73](https://sketchfab.com/3d-models/chapman-73-low-poly-model-f30b644b59b54e809355009dce463d47), [Sialia SW '68](https://sketchfab.com/3d-models/sialia-sw-68-low-poly-model-eea2291a90f04a4b860c9e74be14f2d6), [Duntov '57](https://sketchfab.com/3d-models/duntov-57-low-poly-model-a6840c4f4b4b45ca805d0f76e809f042), [Bokaroo '67](https://sketchfab.com/3d-models/bokaroo-67-low-poly-model-22015d1863d6455aa31cfd738b972c50) | Daniel Zhabotinsky, CC BY | 13k-28k faces, 12-15 textures (2048) each | The Rancher '50's maker: a Mustang-like pony car, a Firebird-like muscle car, an Impala/Caprice-like sedan, a Datsun 510-like wagon, a '57 Corvette-like coupe, a Scout-like 4x4. Made-up names, no real badges: **nothing to paint over**. His own models ("I unconditionally give you permission to use this model"), not rips, though some of his names echo GTA's. |
| **A 70s sedan / a police car** | [Generic 70s Police Cruiser (+ Civilian Sedan)](https://sketchfab.com/3d-models/generic-70s-police-cruiser-civillian-sedan-b16585738a284cafbb3c209766408b3a) | TapeREC, CC BY | 33k faces, 18 textures (4096) | A Galaxie-based full-size sedan with the police kit as a separate part: a plain sedan at the curb, or a black-and-white by the corner on a later diner. Generic badges. |
| **A muscle car** | [1968 Chevrolet Chevelle SS](https://sketchfab.com/3d-models/1968-chevrolet-chevelle-ss-7558ed69102a4d59be0ab1db25fc5dd3) | robinmikart (Libau Media), CC BY | 54k faces, 8 textures (4096) | Yellow with black stripes, the maker's own model off a scan; a racer-looking car across the street. **Not** the "1968 Chevrolet Chevelle (raw scan)" under the same name, which is CC BY-NC-SA. Chevrolet / SS badges in the texture. |
| **A 50s sedan** | [FREE 1953 American Sedan (Based on Oldsmobile)](https://sketchfab.com/3d-models/free-1953-american-sedan-based-on-oldsmobile-18cc2b1da0164127baf1c8f5491841ae) | robinmikart, CC BY | 46.5k faces, 29 textures (2048) | A two-tone '53 Olds 88 hardtop, the same series as the Buick and Packard on disk. |
| **A pickup** | [1970 Truck](https://sketchfab.com/3d-models/1970-truck-e0953a2cfb7349dab78fa363fe6545e8) | Raphael Escamilla, CC BY | 67k faces, 11 textures (2048) | An orange-and-white Blazer/Suburban-like 70s truck; with the C10 and the Rancher on disk, three pickups. Bring to about 30k. |
| **The bus** (optional) | [FREE 1950s American Bus (Based on GMC)](https://sketchfab.com/3d-models/free-1950s-american-bus-based-on-gmc-1d25256110b3457cb6b611e28e22674b) | robinmikart, CC BY | 64k faces, 34 textures (2048) | A GMC "old look" bus, as LA still ran in the 60s. It wears a real company's livery (West Towns Bus Company, Elgin IL) and ads: repaint as a made-up line. |
| **The LA palm** | [Washingtonia Robusta Palm_02](https://sketchfab.com/3d-models/washingtonia-robusta-palm-02-61df9f8be11a45c0b7048790db2569e8), [_03](https://sketchfab.com/3d-models/washingtonia-robusta-palm-03-d63d13e51e1d4925b9eb6b984431762b) | André Murrugarra, CC BY | 202k and 190k faces, 17-19 textures (4096) | The tall skinny Mexican fan palm with a skirt of dead fronds that lines every LA street. Far too heavy as geometry: bake the head to crossed cards as the station's trees are (`bake_cards.py`), the trunk a tapered cylinder; or decimate to ~15k for the two or three nearest the player. |
| **Palms, game-ready** | [Realistic Palm Tree 4 Free](https://sketchfab.com/3d-models/realistic-palm-tree-4-free-917c18d6b2d04d33950dea6e20bc074f), [Realistic Palm Tree Free](https://sketchfab.com/3d-models/realistic-palm-tree-free-39052ea764c945858449e699318efa53) | Next Spring, CC BY | 8.1k faces, 2 textures (4096); 27k faces, 3 textures (4096) | A tall slim palm light enough to place by the dozen, and a fat Canary Island date palm for the diner's planter. |
| **A shade tree** | [Chinese Banyan (Ficus Microcarpa)](https://sketchfab.com/3d-models/chinese-banyan-ficus-microcarpa-2a0dbcdf8f5d48f5ad79987c7a8170ce) | Valery.Li, CC BY | 111k faces, 17 textures (1024) | Indian laurel fig, the commonest LA street tree after the palm; as cards. |
| **Street lights (cobra-head)** | [Utility pole street light 1](https://sketchfab.com/3d-models/utility-pole-street-light-1-1833d380f9e446c0958645f0e9f61132), [Caltrans streetlight 2](https://sketchfab.com/3d-models/caltrans-streetlight-2-ff9c5c8c75a34e22a41794c304418050) | Streetlights & ETC, CC BY | 21k faces, 14 textures (512); 41k faces, 20 textures (512) | A GE cobra-head on an arm off a wooden pole (the side street) and a Caltrans steel pole with a long arm (the boulevard). GE M250/M400 and ITT 25 were 60s fixtures. Made in SketchUp: tidy up and bring to under 10k each. |
| **Traffic signals** | [1960's Horizontal Econolite Signal set](https://sketchfab.com/3d-models/1960s-horizontal-econolite-signal-set-afa4df9910334f17a3b27a6e0ccebeb0) | Signalrenders, CC BY | 229k faces for 7 posts (~30k each), 7 textures (256) | 1960s Econolite heads on posts: the corner's four signals; take one or two posts. (Or [Vintage San Diego RRY Traffic Light posts](https://sketchfab.com/3d-models/vintage-san-diego-rry-traffic-light-posts-febbe10dc8534eae8d523959f3d3b524), 109k for a set, with WAIT/WALK.) |
| **Telephone poles** | [Telephone Poles](https://sketchfab.com/3d-models/telephone-poles-a76b68b838424a89b7db8a38f4c3d546) | Caboose3d, CC BY | 5.3k faces, 9 textures (2048) | Weathered wooden poles with crossarms and steps, lighter than Poly Haven's: the alley behind the diner, the side street. Wires of our own as at the station. |
| **The mailbox** | [US Mailbox](https://sketchfab.com/3d-models/us-mailbox-3376b03ebf1d45ea99fbb6ff350489da) | Yaroslav Dubovikov, CC BY | 1.9k faces, 3 textures (4096) | Red-white-and-blue "U.S. MAIL / LETTERS": the 1955-71 livery exactly. (Raphael Escamilla's [Large Mailbox](https://sketchfab.com/3d-models/large-mailbox-d3efdb58cce44aa78446f8c620e49738), 2.7k, is the all-blue one from 1971 on.) |
| **Newspaper boxes** | [Newspaper Dispenser](https://sketchfab.com/3d-models/newspaper-dispenser-a523551bcaf84aa8bdf63150a629b6a2) | Caboose3d, CC BY | 2.4k faces, 9 textures (2048) | Two red boxes for a made-up "POST TODAY". With chrisg4919's [Rusty Old Newspaper Vending Machine](https://sketchfab.com/3d-models/rusty-old-newspaper-vending-machine-29cca5a8e99e438c8fb644d1aea3587e) (872 faces) for the rough diner. |
| **The bus bench** | [City Bus Stop Bench (Low-Poly Prop)](https://sketchfab.com/3d-models/city-bus-stop-bench-low-poly-prop-67c94d85ee1844fa81d31a4988c57914) | Derfmode, CC BY | 8.9k faces, 4 textures (2048) | A wooden bench with an ad board on its back, the LA bus bench; its (modern) ad repainted as a 60s one of our own, its square of sidewalk and grass dropped. |
| **Fire hydrant** | [Fire Hydrant](https://sketchfab.com/3d-models/fire-hydrant-0777a03dfb694e7dad44d9cd4f24aa0b) | Raphael Escamilla, CC BY | 14k faces, 3 textures (4096) | A worn yellow hydrant with a blue cap, close to LA's. (roh3d's red [Rusty Fire Hydrant](https://sketchfab.com/3d-models/rusty-fire-hydrant-4e0b0f865e7b4e6ebff58588dc8e769c), 15k, otherwise; Poly Haven's `fire_hydrant` is 86k.) |
| **Trash can** | [Green City Trash Can](https://sketchfab.com/3d-models/green-city-trash-can-c5d23e21daef4fc2a62704149f5f9af6) | Raphael Escamilla, CC BY | 5.1k faces, 3 textures (4096) | A slatted green litter basket at the corner; its sticker painted out. |
| **Signs** | [Road Signs](https://sketchfab.com/3d-models/road-signs-c39bf97110494b5db3de84165211f592) | FrodoUndead, CC BY | 14k faces for 47 signs (~100 each), 4 textures (2048) | STOP, ONE WAY, NO PARKING, speed limits, clean and dirty. Street-name blades are ours (a picture each). |
| **The used-car-lot sign** | [Classic American signboard](https://sketchfab.com/3d-models/classic-american-signboard-c1d6ed77e69442f7ace544a7f0182e1e) | exiS7-Gs, CC BY | 22k faces, 3 textures (4096) | A vertical "SALES" sign with a "Welcome!" diamond and starbursts: Googie, no brand. A used-car lot on a corner is as LA as it gets; pennant strings we make ourselves. |
| **A billboard** | [Billboard (.psd layered texture maps included)](https://sketchfab.com/3d-models/billboard-psd-layered-texture-maps-included-20d1a51fbfa94373900e29de74a0f9ee) | TampaJoey, CC BY | 11k faces, 3 textures (1024), PSD included | A steel billboard on a pole with a catwalk, the ad in a layered PSD: a 60s ad of our own over the shops' roofs. |
| **Odds and ends** | [City Props Collection (volume 2)](https://sketchfab.com/3d-models/city-props-collection-volume-2-2f52ea53c58e455f9b69d11f58763c96) | TampaJoey, CC BY | 52k faces for 30-odd props, 115 textures (2048) | Benches, planters, lamps, a bus shelter, bollards, grates, 60-3,100 triangles each; take what is period. Its "NoAI" note forbids feeding it to AI training; placing it in a scene is fine. |

**Poly Haven, CC0: the build fetches them itself (no download for you).**
`jacaranda_tree` (312k, the purple LA street tree, to cards as at the station), `island_tree_02` (cards, background),
`shrub_01`..`04`, `searsia_lucida` and `wild_rooibos_bush` (planters, the lot's edge), `potted_plant_04` (a succulent)
and `crystalline_iceplant` (ice plant on the banks, very LA), `grass_bermuda_01` (lawns), `street_lamp_01`/`02` (the
alley, a wall light), `security_light` (the diner's back), `metal_trash_can` (behind the diner), `trashbag`,
`water_manhole_cover` and `utility_box_01`/`02` (the sidewalk), `modular_chainlink_fence` (the lot behind, the rough
diner), `concrete_road_barrier` (rough diner), `planter_box_01`..`03`, `modular_street_seating` and
`wooden_picnic_table` (a burger stand's tables), `barrel_03`, `wooden_crate_01`/`02`, `plastic_crate_01` (a liquor
store's back door), `pull_chain_light_socket`. The `modular_electricity_poles` are in the station already. Poly Haven
has no palm, car, traffic light, road sign or building.

## What else was looked at, and why not

- **Diners**: libblekibble's 50's Diner Low Poly (137k), Virginia Vidonis' Diner (232k), Shurt's Bobs' Dirty Diner
  (8.5k), Lanzaman's Diner-counter: interiors or cut-away rooms. tl0615's City Diner: a cyberpunk scene built of
  another kit. Wardster's "dinner Ps1 style" (1.5k): PS1 textures at 256. Ronan Boyle's Call of Duty DINER sign and
  Spinofan10's Fredbear's Family Diner: game fan art, left out.
- **Googie buildings by A9908244**: Starlite Hotel (233k) and Sunset Palms Apartments (157k, a round tower) are tagged
  "fallout" (in the Fallout games' style; his own models as far as can be seen) and far too heavy; Sunset Palms is no
  LA dingbat. No dingbat apartment (the two-storey box on stilts over the parking) was found anywhere: build it like
  our houses, a box over a carport with a name in script on the front.
- **YourLocalJavaCarDealer's LA intersection and lights** (Basic 4 Way Los Angeles Intersection 277k, Vintage Los
  Angeles Street Light 32k, Traffic Light Remade 48k): CC BY, but built partly from other people's models (a Tahoe by
  ImperialBlue, signals from a Signalrenders set) whose licences are not stated, and modern (LED signals). Left out.
- **Signalrenders' 1960's Vintage California Signals** (981k, the full two-guy mast arms): the right period, far too
  heavy. His **Vintage California Econolite Signals Pack** is texture-baked "with TF2 texture materials" (Valve's Team
  Fortress 2 textures): rejected, as are his other "TF2 style" sets. The Horizontal Econolite set picked is not one.
- **Cars**: the DisneyCars account's '57 Bel Air, '59 Eldorado and '69 Charger (no source stated, likely game rips):
  rejected. Mark Peters' '67 Mustang Fastback (505k) and Kristian LaGrange's '65 Mustang convertible (112k): too heavy
  and full of Ford badges. Eques_inferno's 1967 Impala (57k): fine licence, but the "Supernatural" TV car; the Chapman
  does the job without the badge. robinmikart's VW T1-style bus (21.7k, untextured, VW roundel) and
  aleksey.matveenko's 1963 Beetle (11.9k, Herbie-ish paint): optional, the VW logos to go. robinmikart's 1950s police
  car, Corvette, postal van, woody wagon, 1951 coupe and Hudson Hornet (60-110k each): the series on disk, if more 50s
  cars are wanted. roh3d's Abandoned Hudson Hornet (14k, rusted) and Dirty Cadillac 75 (14.7k): **for the rough diner**.
  Wardster's 70 muscle car (10k, PS2 look) and Lexyc16's Classic Muscle car: too crude. The "1968 Chevrolet Chevelle
  (raw scan)": CC BY-NC-SA, rejected.
- **Buildings**: novusod's McDonalds (late-70s mansard, golden arches), Buster Brown Shoe Store and Suburban Mall 1980
  (Sears, Caldor): trademarks all over, the wrong decade. Daniel Zhabotinsky's Downtown Buildings Set (45.6k: New York
  and Chicago towers) and Brownstone set: not LA. His **Industrial Buildings Set** (30.6k, "the industrial areas of the
  Western United States") and **Rural Buildings Set** (10.3k): for the rough / edge-of-town diner. jimbogies' Apartment
  Complex Building (21.7k, East-coast brick) and Duplex House (19.6k): not LA. Lodgelus' Textured Commercial Building
  (71k): built on textures.com scans, whose licence does not let the textures be passed on. alexdelker's Day 187
  Liquor Store (58k): a Detroit LiDAR scan of today's store with LOTTO signs. hnanw's Stripmall (65k): modern.
  kelp.strewn's California bungalow (a 1922 house in Auckland, scanned, 4.6k): usable for a side street but rough.
  rcfranklin's Ranch-Style House: a 2000s craftsman.
- **Palms and trees**: PlantCatalog's California fan palm, Mexican fan palm and Blue jacaranda series (CC BY, 12k-550k):
  the free ones are infants (3-4 m) or winter-bare. AllQuad's Livistona (a potted palm), evolveduk's Coconut Palm
  (7.4k, tropical: fine for a beach diner), ElectroNick's Palm Trees (24k) and Cypress tree (29k, an Italian cypress,
  for a Bel Air side street), Uncanny Bingchilling's JACARANDA_TREE (32k, cartoon green), gilles.schaeck's potted Yucca.
- **Street furniture**: every parking meter found is a modern digital one (Raphael Escamilla's, EFX's scans, Archer
  Sterling's); none of the 60s mechanical kind: a simple one is quick to model. Phone booths: YJ_'s (modern) and
  1Lqs' (a London red box); nothing like a Bell glass booth was found: a glass box of our own. Payphones are Soviet,
  Japanese or Korean. Fayruso's American fire hydrant (34.6k, a New York green), Mark Peters' Firehydrant (59k) and
  Dumpster (33k): heavier than needed. neverfollow81's Bus Stop Bench (four plastic seats, an airport look). Mark
  Peters' Utility Pole and Streetlamp (17k, 13k): wild-west wooden posts. KMB3D's Various Low-Poly Street Lights (11k,
  untextured). Streetlights & ETC's GE M400R2 (34k, the head alone) and City street light 1 (17.5k, modern pole).
- **Motel and neon signs**: exiS7-Gs' Classic American MOTEL sign (195k), nickheitzman's Sunburn Shores Motel Sign (72k,
  stylised), Black vizual's Neon "Motel" (69k), A9908244's Neon Sign - Arrows (193k for a flat panel): too heavy for
  what they show; neon drawn on cards, as the roof sign is today, does it.
- **Quaternius, Kenney, Poly Pizza, ambientCG, OpenGameArt**: not searched model by model this time. Kenney's and
  Quaternius' CC0 city kits are blocky next to the cars; the Downtown City MegaKit is in already. ambientCG (CC0) has
  materials, not models: stucco, terrazzo and asphalt for our own buildings can come from there or Poly Haven.

## Trademarks to paint over

roh3d's Chrysler and Cadillac (badges, script, plates), the Chevelle SS (bowtie, SS badges, a plate with the maker's
site), the 1953 Olds (badges, plate), the GMC bus (a real company's livery and ads), the Harbor Breakfast building's
sign, the trash can's sticker, Derfmode's bench ad, the VW logos if a VW is taken. Daniel Zhabotinsky's cars, TapeREC's
sedan, the Diner Sign, the SALES signboard, the newspaper boxes ("POST TODAY", made up) and the billboard (an editable
PSD) carry no real brands. The U.S. Mail livery is the government's.

## To download (Sketchfab needs a login), into `D:/JUEGOS/3D Models/diner/` as glTF zips

Must have:
1. A9908244, Diner Sign
2. Daniel Zhabotinsky, Midcity Buildings Set
3. Wardster, Rentable Stores
4. Raphael Escamilla, Harbor Breakfast Building
5. André Murrugarra, Washingtonia Robusta Palm_02 and _03
6. Next Spring, Realistic Palm Tree 4 Free and Realistic Palm Tree Free
7. Streetlights & ETC, Utility pole street light 1 and Caltrans streetlight 2
8. Signalrenders, 1960's Horizontal Econolite Signal set
9. Caboose3d, Telephone Poles and Newspaper Dispenser
10. Yaroslav Dubovikov, US Mailbox; Raphael Escamilla, Fire Hydrant and Green City Trash Can
11. Derfmode, City Bus Stop Bench; FrodoUndead, Road Signs
12. roh3d, Chrysler Saratoga 1960 and Cadillac 75 Sedan 1953
13. Daniel Zhabotinsky, Palomino Special '67, Phoenix 455 '71, Chapman '73, Sialia SW '68

Nice to have:
14. exiS7-Gs, Classic American signboard; TampaJoey, Billboard and City Props Collection (volume 2)
15. Valery.Li, Chinese Banyan; AleixoAlonso, House "Fleetwood"; Orzechu, Old American brick building with shop
16. TapeREC, Generic 70s Police Cruiser; robinmikart, 1968 Chevrolet Chevelle SS and 1953 American Sedan; Raphael
    Escamilla, 1970 Truck; Daniel Zhabotinsky, Duntov '57 and Bokaroo '67
17. For later diners: Elbolillo, DINER; novusod, Roadside Motel; rtaing, The Theme Building; aocrisan, Mojave Motel
    Sign; Daniel Zhabotinsky, Industrial Buildings Set and Rural Buildings Set; roh3d, Abandoned Hudson Hornet and
    Dirty Cadillac 75; chrisg4919, Rusty Old Newspaper Vending Machine; robinmikart, FREE 1950s American Bus

## The recommended set and its budget

Today's street is 252k triangles. Aim for about 450k for the whole corner, every prop brought down by the compose
step's budget as the station's are:

| Group | What | Triangles |
|---|---|---|
| Road, sidewalks, curbs, lot, cross street | the MegaKit pieces (in now), our crosswalks | ~40k |
| The diner | WaajaaH's (or our own Googie building) + the Diner Sign at ~15k | ~45k |
| Shops and corners | Midcity set, Rentable Stores, Harbor Breakfast at ~25k, the brick block, the gas station corner from disk (Wardster's building 6k + two pumps) | ~70k |
| Houses on the side street | ours as now + one Fleetwood | ~35k |
| Parked cars (not the racers', which are AC cars) | 8-10 cars at 15-20k each (robinmikart's cut to 25k) | ~160k |
| Trees | palm heads, ficus and jacaranda as cards (a few hundred each), two near palms at ~15k | ~45k |
| Lights, signals, poles, wires | 6 cobra heads, 4 signal posts and 8 telephone poles, each cut to ~5-8k | ~60k |
| Street furniture | hydrant, mailbox, papers, bench, cans, signs, billboard, meters of our own | ~30k |

About 485k; if the frame rate suffers, the parked cars are where to cut (fewer, or the far ones at 8k).
