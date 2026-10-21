# Parts System

Every car carries a tree of parts: models, mounting slots, figures and behaviour. Each part is data (a `.json` file
of its own and its KN5 model beside it); what the parts do together is the game's own C#. The engine is worked out from the parts'
specs (`EngineAssembly`), run on the game's dyno (`EngineDyno`) and handed to Assetto Corsa as physics data
(`Parts/Export`). The running gear is read from its parts' specs the same way. The format, part by part, is in
[modding.md](../modding.md) ("parts"); this page is how it works inside.

Scope: mechanical parts only: engine, transmission and running gear (tyres, rims, brakes, springs, shocks, the rear
axle). Car bodies stay Assetto Corsa's.

```
Content\<mod>\parts\                          what the parts are
   <any folders>\<part>.json + *.kn5          one file per part + its model (the base game: <kind>\<group>\)
   engine_builds.json  build_aliases.json     complete engines as part lists, renamed builds
   part_aliases.json   models.json            renamed parts, models a mod gives other parts
   part_classes.json   part_groups.json       what each class is a kind of, the shop shelves
   work_order.json     rebuilds.json          what comes off before what, the work done on parts
   fuels.json                                 what a fuel system can run on

PartsCatalog ──▶ PartTreeBuilder ──▶ EngineAssembly ──▶ EngineDyno ──▶ EngineReport ──▶ AcEngineData
 definitions      build → tree        specs → figures    torque curve    verdict+figures   AC data files

Car.Parts ──RunningGear.Mounted──▶ AcRunningGearData ─┐
                                                       ├──▶ AcCarBuild ──▶ RaceCarDataService ──▶ CarDataOverlay ──▶ acs.exe
Car.Engine ──EngineEvaluator──▶ AcEngineData ──────────┘     all files      per car of a race     apply, race, restore
```

The base game's parts live in the repo, `Street Corsa\Content\streetcorsa\parts`, and are copied next to the exe at
build; `AppSettings.PartsPath` reads them from there. **Since 2026-10-20 the base game has no engine of its own: it keeps the
generic parts and the stock parts.** The Chrysler, Ford V8 and GM engine packs (whose models came from community
mods of another racing game) and the Ford six (the owner's own models, the last to leave, later the same day) are mods
the player installs, outside the repository (`D:\JUEGOS\STREET CORSA\mods\engines\chrysler_v8`, `ford_v8`, `gm_v8`,
`ford_six`, beside `l79`, `flathead`, `chevy_six`, `sbc350`). Each stands alone (version 2.0, same day): one `mod.json` holding its builds, `cars` picks and
`sounds` pins, its parts in `parts\` (a `.json` and a `.kn5` each since 2026-10-20 later: "Part files" below; the
parts it copied from another mod in `parts\gm\`) and its own copies of the banks its blocks are pinned to under
`sounds\<bank>\` (the eight curated banks and the installed cars' banks they used, a bank in several mods copied into
each); a part of another mod its builds use is copied in under its own id (`l79` carries 17 of `gm_v8`'s, the GM 500
stand-in internals, the radiator and the gearboxes; `sbc350` 2; `flathead` the radiator). Their aliases are gone, so a
save of before the split loses the old V8 ids. Before this, the builds of `l79`, `sbc350` and `flathead` did not run at
all with the mods installed: their radiator, gearboxes and alternator were `gm_v8`'s, named by attach lines that never
reached another mod. With none of
them installed, a car of a known family (GM, Ford, Mopar, AMC, Volvo...) whose family has no build gets **no engine**
rather than the wrong one: it races on its own AC data, starts and sounds where it stands (the garage, the lots, the
streets) on the engine AC runs it on (engine.ini's idle, limiter and inertia, its power.lut, its own bank:
`EngineSpecs.FromAcData`), is worth its profile's engine price, and is asked for its factory engine again on every
visit until a mod brings one (`EngineMatchRules.RefuseOtherFamily`, rules `engines.json`,
off: the best build there is serves as before). A car whose make is of no known family takes the best there is, and
a build named for the car's own make (the Volvo Amazon's, of Ford six parts) is the car's own whatever its block's
family. The rivals count a car for sale without a dyno figure as a runner (its factory engine nobody measured).

## The parts' folders

```
engines/ford_six                       the Falcon's six, as a mod (ford_six)
engines/chrysler, gm, ford             the V8 engine packs, as mods (GM has Pontiac and Cadillac too)
engines/generic                        universal-fit induction parts: aftermarket carburettors, air cleaners, scoops,
                                       roots blowers (see "Parts that fit any engine")
engines/stock                          batteries, nitrous, and the engine-part fittings the others mount against
rims/mopar, falcon, hudson, opala, goodyear_eagle, stock
tyres/mopar, camaro69, goodyear_eagle, stock
exhaust/mufflers, stock
suspension/stock                       coil springs, shocks and sway bars
brakes/stock                           discs
running_gear/*                         the game's own running gear: rear axles and their gears and shafts, period drums,
                                       leaf springs, torsion bars, sway bars, traction bars, shocks (step 31)
```

A part's id is its group and a name made of its display name (`engines/chrysler/chrysler_340_v8_block`), written in
its file: where the file is, or what it is called, is no part of it. An id never
changes without an alias: `part_aliases.json` maps every id a part had to the one it has now (followed up to 8 hops),
and `build_aliases.json` does the same for engine builds, so a save made with any earlier content loads.
`SavedParts.BringUpToDate` also renames a saved part's tuning to its spec names. Ids before 2026-10-18 were the
original packs' (`engines/chrysler/_Engine_block_340`); they are all aliased.

Parts mount by **fittings**, which name no part: a slot `fits` and `takes` words, and a part goes on another when a
slot of its own fits what one of the other's takes (`PartsCatalog.CanMate`, one way only, as `FindMountable` looks).
A part without a class, specs or figures is never sold or fitted on its own (`PartDefinition.IsRealPart`).

The parts are hand-kept: a part that is wrong is fixed in its file, a part missing is a new file (a part a build
lacks today goes on a list to make).

**Part files** (2026-10-20). Every `.json` under a mod's `parts` folder is one part (`PartFiles`, `PartsCatalog.PartFilesUnder`),
but the catalog's own files directly in it (`PartFiles.CatalogFiles`: `engine_builds.json`, the aliases, `models.json`,
`part_classes.json`, `part_groups.json`, `rebuilds.json`, `work_order.json`, `fuels.json`, `slot_shifts.json`). A
part's `"make"` and `"credit"` are its own. The packs before them (`pack.json`, many parts to a file, and mod.json's
`packs`) are not read: a `pack.json` with a `parts` list in it is one problem line per mod (a part's own file of the
name is a part). A part a mod has in two files (one an older version of the mod left behind when its new version was
unpacked over it) is the newer file's, told. A junction or link under a parts folder is not followed. The base game, the tests' V8 fixture, the example
mod and the seven engine mods were converted by a script, and the whole catalog loaded the same before and after (every
part's fields, make and model file, every build, no problems).

## How an engine is worked out (`Parts/Logic`)

- `InstalledPart`: a part in an assembly: definition, wear/tear, tuning by spec name (`rev_limit`, `mixture_ratio`,
  `advance`, `gears[1]`...), parent and children by slot.
- `PartTreeBuilder`: list of parts → tree. Two slots mate when one `takes` a fitting the other `fits`
  (`PartsCatalog.CanMate`); a part never names another (see "Fittings" below).
  Parts that find no place are reported, not lost.
- `EngineAssembly`: the engine model, a page of C#. The block is the root; its `slot_roles` say which slot takes the
  crankshaft, the heads, the camshaft, the intake, the gearbox and the oil pan, and each part's class says what it
  adds (`PartClasses.IsA`, so a mod's class under `camshaft` works as a camshaft):
  - the bore, stroke, deck height, rod length, pin height and dome with the head's chamber make the clearance volume
    and the compression;
  - each head breathes through its valves' area times the cam's lift over the cylinder volume, times the manifold's
    and the header's flow (`BreatherFactor` 0.9); the banks of a vee are averaged (their limits are not: the engine's
    top speed and its most fuel and air are the tighter of the two banks', and a bank without one leaves the other's
    standing), an engine with a head missing has no breathing (and does not run);
  - the cam's durations, lobe separation and advance (held to its range, in its steps) make the valve events;
  - every part and what hangs on it is gone over, the last slot first: the weakest `max_rpm` is the engine's top
    speed, a fuel system gives its mixture, its fuel (`fuels.json`) and the least fuel and air of all of them (the air
    through its air cleaner, else its open horn), a supercharger or a turbocharger its boost;
  - the moving parts' inertia adds up (a crankshaft carries its flywheel and rods, a rod its pistons, a flywheel its
    clutch; the heads count twice and the camshaft four times), and what they lose to friction over the oil pan's
    figure is how hard the engine turns;
  - the gearbox hands over its gears, reverse, final drive, drive and diff lock.
- `DynoSheet` holds the figures as they are filled in; `DynoInputs` is what the dyno takes (SI units, valve and spark
  events as fractions of the 720 degree cycle).
- `EngineDyno`: mean value model per 250 rpm. Energy: mixture flow × heat per kg of mixture × ideal air cycle
  efficiency `1 − CR^−0.4`, no friction taken off (builds limited by their fuel system land within 5% of their rated
  power that way). Breathing is first-order filling/emptying through the valves, fitted to the rated builds. Boost is
  baked into the curve.
- `EngineEvaluator` → `EngineReport`: runs or not and why (the parts' own words), curve, idle, limiter, inertia, gears,
  final, drive type, diff lock, mass. The block's `needs` are asked first, in order, then the compression its fuel
  takes ("too low", "too high"), then every other part's `needs`; an engine with no fuel system says nothing feeds it.
  The reasons that are the game's own, not a pack's (the compression, nothing feeding it, no displacement, no rev
  range, figures past any engine) are the `engine.problem.*` lines of `text/ui_parts.json` through `PartWords`, their
  numbers written the same in every locale; the tools, which read no text, show the keys. The idle, from a block's
  spec or a part's figure alike, is held between 300 rpm and half the limiter.
- `PartFigures`: a part's own engine figures (`"figures"` in its file, plain metric; see the modding guide's "An engine
  of figures") go over what the specs came to before the dyno runs; a block without specs makes an engine of figures
  alone on `PartFigures.DataEngineBase`. Turbos and blowers of figures are `DynoInputs.Chargers`, their boost added.
- Numbers in `specs` come back from JSON as `long` or `double`; read them with `PartDefinition.Number` (or
  `InstalledPart.Spec`, which puts the player's tuning over them).

**Fault containment.** A bad part costs that part, never the car's evaluation or the catalog: `EngineEvaluator.Evaluate`
never throws (a figure that breaks the sum becomes the report's `Faults` and the engine's Problem), report figures are
sanitized (no NaN, no rpm past 30 000, no ratio past 100, no curve past 20 000 hp or 50 000 Nm reaches AC's data),
and `PartsCatalog.Load` reads each part file on its own (a file that does not read, a null part or id, a duplicate id,
an empty file, an alias to nothing, a broken aliases or builds file are each one line in `PartsCatalog.Problems`).

The model was checked against the engines' earlier evaluation when it became the game's own (2026-10-18): of 201 builds
176 came out the same, none changed whether it runs, the Opala blower six makes 3% more (its carburettor's own
mixture). Model check: `EngineBench <Content> rated` (mean error about −0.3%, mean absolute 10% over the 17 rated builds
that run); `EngineBench <Content> snapshot <file>` writes every build's whole report, to compare two versions of the
logic by.

## Fittings

A slot mounts by its fittings alone (`fits`: what it goes on; `takes`: what goes on it); `PartsCatalog.CanMate` mates two
slots when one fits a fitting the other takes, and `FindMountable(parent, slot)` lists what fits what the slot takes.
A part never names another, so a mod's part goes on any mod's engine whose slot takes its fitting. Until 2026-10-20
slots named their mates by part id and slot (`attaches_to`, `compatible_with`, stand-in slots, and bare class-less
"fitting" parts that only existed to be stood in for); a slot that still has those is told as the older format and
mounts by nothing. Every attach line of the base game and the engine mods was turned into fittings that mate exactly
the same pairs of slots (checked pair by pair, and every build's whole EngineBench report the same): a slot that was
named takes a fitting named `<mod>:<the first part of that slot's group>/<what goes there>` (the base game's by pack:
`ford_six:`, `generic:`, `stock:`, `rims_mopar:`...), and what was named on it fits it. Slots taking the same parts
share one fitting. Which side of a joint is the parent came from how the builds assemble (5,268 joints the part that
named was the child; blocks and a cam naming what goes on them the other way). 64 bare parts went (with 150 base aliases
to them).

## Parts that fit any engine (`engines/generic`, standard fittings)

An aftermarket carburettor bolts to a flange pattern, not a brand; an air cleaner sits on the carburettor's air horn;
a roots blower on a blower manifold. Such parts carry a **standard fitting** (`fits` in its file): `carb:2bbl`,
`carb:4bbl`, `carb:2x4`, `carb:3x2` on carburettor bases, `air:single` and `air:inline` on air cleaners and scoops,
`blower:roots` on blowers; the pads that **take** one say so (`takes`). Slot numbering on the
engine: manifold pad 7, carburettor base 10, its air horn 11, blower pad 9, drive belt 15.

Factory carburettors and air cleaners (Carter AVS, GM's stock 2/4-bbl and Quadrajet-style cleaners, the Mopar pie
tins, Six Pack cleaners, the Shaker, the GTO Tri-Power and Corvette air boxes) keep their own engines' fittings and stay
with their brand: a used car tuned by `EngineFactory` gets aftermarket parts of any make, never another make's factory
part. Kits stay too: the Paxton and Edelbrock E-Force superchargers, the Hemi crossram carburettors. Blowers bring
their own pack's drive belt. Carburettors of different packs are **not** one part even when they are the same product:
the carburettor sets the engine's mixture, and every pack's engines were rated with their own. Several GM and Ford
carburettors are drawn with a Chrysler part's model (`models.json`, and their slots at the donor's positions).

## Every carburettor part is one carburettor (the shared air slot)

A dual-quad or a Six Pack is that many carburettor parts, one per pad: a manifold's pads are a row along the engine
axis (pad 7 the middle one, or the rear one of a pair; the others 300, 301...), plus slot **311**
(`PartSlot.SharedAirSlot`) over the row for an air cleaner that spans the set (`air:2x4`, `air:3x2`). A carburettor in
a row whose own horn is empty breathes through the cleaner over the row (`EngineAssembly.AirCleanerOf`). The extra pads
are the manifold's `needs`, so a row runs only with a carburettor on every pad. The flathead mod's Edelbrock 2x2 and
4x2 intakes follow the same layout; their pads take `carb:97`, which only the Stromberg 97 fits, and the Stromberg's
horn takes `air:97`.

## Placement mode in the garage (F5, `SlotShifts`)

A tool for fitting parts by eye, not gameplay. With a part picked in the workbench, **F5** turns
placement mode on: the arrows move the part across and fore-aft, PgUp/PgDn up and down, in the *engine's* axes
(right, up, towards the radiator), whatever the camera does; a step is 5 mm, Ctrl 1 mm, Shift 2 cm. **Tab** switches
between the part's own slot and the pad the part sits on (a carburettor wrong on one manifold is the pad's fault, one
wrong everywhere is its own). A part's own slot is the part's wherever it goes, so nudging it moves every copy: a part
that is one of several of the same on its parent (the carburettors of a dual quad or Six Pack, the filters on them)
starts on its pad instead, which is that place's alone. **R** takes the slot back to where the packs put it, F5 or
Esc ends the mode; a readout in the part card says what is being moved and by how much so far. `CarViewport3D` turns the step into a move of a slot in that slot's
part's space (the part's own mounting slot moves the other way, since the part hangs so that its slot lands on the
pad), `PartsCatalog.ShiftSlot` changes the part in place, and the parts are laid out again without rebuilding the
model (`GarageRenderer.PlacePart`).

Every step is written to `slot_shifts.json` next to the content the game runs on (part id → slot id → offset, metres,
the part's axes, the sum of every nudge), applied by the catalog on every load; a part renamed since keeps its shifts
under its new id (`part_aliases.json`), and a base game id that names no part is a problem line. To make a fit part of the content, add the
offset to the slot's `position` in the part's file and take the line out of slot_shifts.json. `SlotShifts` is written atomically
(`SafeFile`) and never throws on load: a file that does not read gives no shifts and a `Problem`, and then it is never
written over.

## Cars and their parts (`Parts/Cars`, `Services/Parts`)

Every car in the game owns a tree of parts; what is on the tree is what the garage draws, what the dyno measures and
(next) what Assetto Corsa gets to drive.

**Save model.** `PartInstance` (in `Models/GameState`) is a part as a save file holds it: catalog id, wear, tear, tuning,
the slot it is mounted on, the slot it is mounted by, and its children. `Car.Parts` are the parts that sit on the car
itself, by car slot (`PartInstance.CarEngineSlot` = 1; the running gear uses 101+). A loose part
keeps whatever was on it, so the player's shelf (`Player.Parts`) and the ads hold sub-assemblies as readily as single
parts, a complete engine included. `PartTrees` goes between `PartInstance` and the runtime's `InstalledPart`
(`LiveTree` keeps the way back). `Car.HasPartsAssigned` tells a car that never had parts (an older save) from one whose
engine was pulled; `ICarPartsService.EnsureParts` gives the former its factory engine, worn like the car, the first time
the car is looked at (`EnsurePartsAsync` from a screen: the engine is built on a worker thread, the car changed on the
caller's).

**Factory engine.** `CarProfile.StockEngineBuildId` names an engine build of `engine_builds.json`. It is suggested by
`StockEngineMatcher` unless the mods' `cars/cars.json` gives the car one (`engine`), which the Car Catalog Editor
writes into the base game's (options come best match first; an engine given keeps suggestions away, also when the
build leaves the catalog: the car then runs on the best match until somebody picks again). A suggestion is weighed
again every session against the builds installed then and written over the last one when it differs
(`CarPartsService.TakesSuggestion`): until 2026-10-20 it was kept as long as its build was there, so the Fords
suggested the Ford six on a start without the V8 mods kept it after `ford_v8` came in. The cars are priced again after
(`RepriceAsync`); cars already on the lots, in the paper and in the rivals' garages keep the engines they were built
with. Suggestions are written with `ICarProfileRepository.UpdateProfile`, a read-change-write in one visit to the database. The matcher scores the builds
that run (`EngineBuildIndex`: every build on the dyno once, about a second, kept for the session), by the points of the
rules' `engines.matcher`: same corporate family (`MakeFamilies` over `CarBook`: GM, Ford, Mopar... from the words of
the name, as `cars/makes.json` says; a block belongs to the make whose cars it came in),
words the names share (a 427 is a 427), and how close the power comes to the car's own `bhp`. With the parts there are,
some cars get the nearest relative rather than their engine (no slant six: the Valiant gets a 318).
Check: `EngineBench <parts> cars <AC cars folder>`.

**Engine layout.** Where a car carries its engine (`EngineLayout`: front, mid, rear) is guessed from its own data,
since AC's data never says (`EngineLayouts.Guess`): front-wheel drive, or 44% or more of the weight on the front axle,
is a front engine; anything else is taken for a rear one, not a mid one, as the weight alone does not tell them apart
and the rear engine is the one of the game's era. `layout` in `cars/cars.json` overrules the guess, set in the Car
Catalog Editor ("Guessed: ..." keeps the guess; a reset to defaults goes back to it). The layout only moves the engine
in the garage's 3D view (`CarPartsLayout.EnginePlacement`, the mount candidates too): a front engine behind the front
axle facing forward; a mid one facing forward too, its rear face 0.4 m ahead of the rear axle so the gearbox reaches
back to it; a rear one turned around, its rear face at the rear axle and the gearbox reaching forward past it. The
block's model is measured for its rear face (`PartModelBounds`). Of the installed cars only the Corvairs come out as
rear-engined; they keep the V8 the matcher gives them until a flat-six pack exists, and the matcher does not look at
layout or cylinders yet.
Check: `EngineBench <parts> layout <AC cars folder>`.

**Cars that have been around** (`EngineFactory.CreateTuned`). Used car listings carry their actual engine
(`UsedCarListing.Parts`, moved onto the car when it is bought) and about a third of them have been worked on, only the
way somebody plausibly would have: an engine of the same family with 5-50% more power, and bolt-on parts (air filter,
carburettor, manifold, headers, camshaft, flywheel, clutch) picked from what really goes on that very slot
(`PartsCatalog.FindMountable`), whose own children find their place on the new part. Every change has to leave an engine
that runs, makes no less power, stays under 1.6x the factory power, and is worth doing (more power or a dearer part).
A Hemi never ends up in a Chevrolet. Half of what the changes are worth shows in the asking price.
Check: `EngineBench <parts> tune <build id> [count]`.

**Workbench** (`Workbench`). A part comes off with everything that is mounted on it and goes back on the same way.
`FindPlaces` lists the free slots a loose part mates with (`CanMate`), blocks only ever go in the empty engine bay, and
kinds that never stack (head on head, block on block: copy-paste in the mods' configs) are refused. The list
(`PartKinds.NeverStacks`) was checked against every engine build: intake manifolds are *not* on it, 23 builds mount
theirs on a converter plate that is a manifold itself.
Check: `EngineBench <parts> bench <build id>` takes every part of a build off and has it find its way back.

**What fits a slot.** `PartsCatalog.FindMountable(parent, slot)` answers `CanMate` for the whole catalog from an index
built on first use: slots by the fittings they fit.

**Garage.** The Parts toggle opens the workbench (`PartsWorkbenchViewModel` + `PartsWorkbenchView` around the 3D view):
- the engine sheet: block, power, torque, displacement and compression, gearbox, weight, and when it does not run the
  parts' own words for why;
- parts are picked **in the 3D view**: the part under the pointer glows and is named, a click selects it (orange glow,
  card with condition and worth, "Take it off"). Hidden parts are reached the way they are in a workshop: take off what
  is in the way;
- the shelf: picking a loose part shows it, glowing green, in every place it fits; a click on one mounts it.
  "Take apart" splits what is mounted on a shelf part off it (a manifold from its carburettor), piece by piece; not an
  engine block, which comes apart on the stand only ("Put on the stand").
Every change is saved and costs game time (5 minutes a part, 30 at most; an engine in or out of a car 2 hours, onto
the stand or off it 20 minutes).

**The engine stand** (step 17). The stand is always in the garage, 3 m to the car's left (`EngineStandLayout.Spot`;
AC cars face +Z, left towards +X), with `Player.EngineStand` on it as it is: `CarViewport3D.StandEngine`, drawn as
`GarageRenderer.SetScenery`, a model that is never picked. The engine hangs by its bellhousing face, crankshaft 0.78 m
up, front towards +Z (the head sits at the rear of the block model's bounds; the stand is boxes of our own under a
node that is no part's). The Stand toggle, next to Parts, turns the camera to the stand and leaves the car solid: the
engine moves from the scenery into the prop (`GarageRenderer.StandActive`: picked and glowing without a ghost car, no
body lean), the scenery keeps the stand alone. A click on the stand or its engine does what the toggle does (hover:
glow and "Engine stand"; `GarageRenderer.HitsScenery`, the solid car in front of it counts as cover); in the Stand view a click on the car goes
back to the car's Parts view the same way (`HitsCar`: the stand and its engine in front count as cover, the car
glows under the pointer). Back from the
stand the camera glides to the car in one ease: the renderer's `AutoAdjustTarget` (a third of the way every frame, a
jump after 3 m) is off until the target reaches `GarageRenderer.FramedTarget`, where that framing would settle.
Everything else works as in the car: picking, the card, the shelf's places (`Workbench.FindPlacesOnStand`). "Pull the engine" takes a car's engine
onto the stand when it is free (else to the shelf), "Off the stand" puts it on the shelf, "Put it in the car" into the
selected car's empty bay. Going on the stand, `EngineWork.StripForStand` takes off the work order's `stand_strips`
(radiator, starter, transmission, clutch, flywheel: in the way of the stand's head; the radiator stays with the car, the
starter is bolted to the bellhousing) onto the shelf, and none of them goes back on while the engine is on the stand.
The "Turn over" toggle turns the engine on the stand upside down on the stand's spindle (the crankshaft's line), with
whatever is on it and on its way: `PartsWorkbenchViewModel.IsEngineTurnedOver` → `CarViewport3D.EngineTurnedOver` →
`GarageRenderer.EngineTurnedOver`, which turns the stand's prop, its candidates and the stand's head
(`EngineStandLayout.HeadNode`) as whole models, as far as the engine clears the stand's base (a blown engine only goes
onto its side), and never while parts are coming off it. Picking a part whose
kind is worked on from below (`"worked_from"` in the work order: crankshaft, bearing bridge, oil pan) turns it over by
itself, one from above (heads, intake, covers...) back. Only looks: nothing is refused either way. It is upright again
off the stand, turning back before it goes onto the hoist or into the scenery.

**The work order** (`WorkOrder`, `Content\streetcorsa\parts\work_order.json`, one per mod; see
[modding.md](../modding.md)). Every part has a kind: its own `"kind"`, else its class (the big parts), else
words of its name (timing covers, dampers, distributors, belts... are plain `EnginePart`/`Part` in every mod, but named
alike across makes). 1055 of the 1107 engine parts get one (2026-09-26); the rest are the fittings, batteries, scoops and
bottles. `EngineWork.WhyNotOff`/`WhyNotOn` give the reason in the player's words ("Take off the cylinder heads and the
oil pan first.", shown on the card and as the shelf hint): the crankshaft, rods, pistons and camshaft only on the stand,
one at a time with nothing on them, and the peripherals in their order (the belt before the fan, the fan before the
water pump, the alternator before the water pump, the intake before the heads, the starter before the transmission,
the transmission before the flywheel, the distributor before the intake). A drive belt goes on only over the water
pump and the fan (`"needs"`; a radiator with an electric fan serves as the fan), a power steering belt (`steering_belt`)
only over its pump. The Ford six's distributor sits on the side of the block: its file makes it a `side_distributor`,
which waits for nothing but holds the camshaft in. Its intake manifold bolts to the side of the head and comes off
sideways, its alternator sits on top and comes off upwards: they stay an `intake_manifold` and an `alternator`, rules
and all, and only the way they come off is their own (`"comes_off"` on the part, `PartDefinition.ComesOff`, read by
`WorkOrder.WayOf` before the kind's). The timing cover and the water pump come off each on their own.
The rules hold both ways, so an engine taken apart goes back together in the opposite order. A part that lacks what it
needs could come off but never go back on, so it is taken off as the engine comes into the game
(`EngineWork.TakeOffWhatLacksItsNeeds`: in `EngineFactory` for a build, in `SavedParts.BringUpToDate` for a save, onto
the shelf; a part that will not come off ends it). `EngineStandTests` takes every one of the 161 builds, those short of parts too, down to its bare block on
the stand and back, and checks the builds that run lose nothing that way. A loop among the rules (kinds that wait for
each other, or need each other) is told once per set of kinds that lead to each other (Tarjan's strongly connected
components, linear and without recursion): a work order is any mod's, and listing every simple loop of a dense one would
not end.

**The running gear's work order** (step 31 phase 5, `CarWork`, the same `work_order.json`). Everything on a car that is
not its engine has kinds too (rim, tyre, hub, drum, brake, brake shoes, wheel cylinder, backing plate, rear axle,
differential carrier, ring and pinion, axle shaft, leaf spring, traction bar, torsion bar, spring, shock, sway bar: by
class) and rules, asked as the engine's are (`CarWork.WhyNotOff`/`WhyNotOn`, the card's
reason and the shelf hint; the engine's parts stay `EngineWork`'s). A rule counts at the part's own corner: each part on
the car works at the corners of its slot (a wheel's, brake's, spring's or shock's own; what is on the rear axle's housing
at a corner or shaft slot that side's; the axle and a rear sway bar both rear corners, the front bar both front ones;
anything else all four), and what is on a part works where it does. So the left front drum waits for the left front
wheel only. What is mounted on the part goes along and is never in its way (the engine's rules count it): the rear axle
drops whole with its brakes, coil springs, shocks and bar once the rear wheels are off; `"bare"` asks for the part's own
parts off it first. A shelf place is told by place: the hint is the first reason, the places that may be taken stay.
`"hangs_on"` replaced `AxleWork`: on a car that has a leaf spring on it, or whose factory running gear has one
(`AxleParts.All`), the rear axle goes on once every place for a leaf holds one (`Workbench.FindPlaces` for any part of the
kind finds none free), and a leaf comes off once the axle is off. The base game's rules, a drum brake coming apart as a
real one does: a drum or a disc after the wheel, the drum over its shoes and wheel cylinder; the shoes after the drum and
over the cylinder; the cylinder after the shoes; the backing plate bare (and at the rear after the axle shaft); an axle
shaft after its drum, and only into a carrier; the carrier and the ring and pinion after both shafts; the rear axle after
the rear wheels, hanging on its leaves (a leaf is one part, its hanger, shackle and U-bolts in it: the owner found them
noise as parts of their own). The running gear comes off along the car's axes, whatever way the
engine sits (`PartMove.CarAxes`); a kind's `"tool"` says whether it is heard with the air gun (`Sfx.ToolFor`).

Nothing pops. The cards and the navigation tiles they replace slide and fade through `Helpers/Reveal`
(`Reveal.IsShown` in place of a Visibility binding, `OffsetX/Y` for where an element comes from). Parts move too: a tree
is made anew after every change, so `InstalledPart.InstanceId` (the saved part's id) is how the viewport tells what left
and what arrived. What left lifts off the old model while the new one is put together (`GarageRenderer.MoveParts`: the
part nodes' `LocalMatrix` travels 0.45 m away from the middle of what stays, shrinking, 0.55 s), then the models are
swapped and what arrived flies in the same way backwards. A car change swaps without any of it.

`CarViewport3D` draws what it is given: `PartsCatalog`, `Engine` (an `InstalledPart` tree, a new one after every
change), `Candidates` (`MountCandidate`: a loose tree, the part and slot it would go on) and `SelectedPart`; it raises
`PartClicked` and `CandidateClicked`. `PartAssembler.Assemble` places a tree by its slots, `BuildModel` merges it into
one KN5 with a node per part (`NodeName(i)`, `AssemblyModel.Nodes[i]`), which is how a pick finds its part.
A part without a model (a handful of fuel rails and injectors) goes into the model as a small grey box
(`PlaceholderMesh`): parts are mounted and taken off by clicking them, so every part needs something to click. So
does a part whose model file does not read. The part models are cached for the session by path while their file is
unchanged (emptied past 256 MB of model files), and a texture of the same name as another mod's but other content goes
into the merged model under a name of its own (`body~1.dds`), its part's materials pointed at it.
The model on screen lags behind the tree while parts fly off and the next model is built, so whatever comes out of a
click is checked against what counts now: the workbench finds a clicked part again by its `InstanceId` in the current
tree, and a clicked place only counts if it belongs to the `Candidates` of the shelf part that is picked (the viewport
drops the places of the part picked before at once).
`GarageRenderer` holds two part models (mounted, candidates), picks by ray (`Pick`: bounding box first, then triangles,
about 0.2 ms; a place for a loose part wins over a mounted part in front of it, the places glow through everything
and some sit inside another part) and draws the glows: depth of the thing alone, `EffectPpOutline`, then scaled to a tint with a
blend-factor pass, one buffer per glow (car, candidates, selected, hovered).

**Shop** (`NewspaperPartsService`). Used parts from the ads (`NewspaperAds.Parts`, 25-40 ads turned over daily by
`PartsAdsRefreshTask`, now and then a complete engine) are the newspaper's used parts section, bought from the ad. The
mail order (`Screens/UsedParts`, the part shops' map's Mail order button) has two pages: new parts by mail order (the engine packs plus whatever else the builds use: everything there is, at the list price and 20% more,
`NewspaperPartsService.MailOrderPremium`, paid when ordered and on the shelf three days later: `Player.PartOrders`, brought by
`PartsAdsRefreshTask` each morning, told of in the garage from `Player.Delivered`), and selling from the shelf. Filters: kind
(`PartKinds.GroupOf`, from the parts' classes), name, and "only what fits my car" (`FindFittingParts`: everything that
goes on any slot of the selected car's tree, taken slots included). New price is what the part does
(`PartPriceBook`, 2026-10-06: its kind's `price` in part_groups.json, a base and dollars per horsepower the part is
worth on the dyno, measured once a session over every build that runs and kept under `%AppData%\StreetCorsa\part_power.json`
with a fingerprint of the parts, `PriceBookStore`; no pack writes a price). The process-wide price seams (`PartPricing`,
`CarPricing`, `EngineRules`) are pointed at the rules and the parts service once, where the app is composed
(`PartsWiring.Wire`), never by a constructor; the UI thread never waits for the book to be measured (the warm-up does it)
and prices at the kinds' bases until it is. A used part is worth `new x tear x (floor + (1 - floor) x wear)`
(`PartPricing.Shape`, `economy.wearFloor` 0.3, the one rule for a part's wear and a car's body), ads ask around 60%
of that (`economy.partAdMinAskShare` to `partAdMaxAskShare` of a shop's used price), a trade-in pays 40%. What is for
sale at all is the kinds part_groups.json puts up (`for_sale`: the engine's parts; the running gear is sold by what it is,
the mufflers are not). A whole engine that runs is priced by what it makes (`PartPricing.EngineNewPrice`, 2026-10-17): its dyno
power, measured with every part as new, on the blocks' curve, plus the base price of every other part, and never less than
its parts loose. Its worth shares that over the parts by their prices, each share in its part's wear. Loose parts price
their power linearly (a head $4/hp) and a block by the weakest engine it comes in, so an engine carrying its power in its
speed parts (the '72 Camaro's 327 stroker, 364 hp) came to half the price of one carrying it in its block (the SBC 350,
327 hp). A car is priced the same way (`CarPricing`, `CarValuation`): its body by its weight, its engine as above,
and every other part on it in its wear and damage.
Every part model is drawn at the size it was made. A `model_scales.json` that drew the ported packs 1.2x (brakes
1.35x) came in on 2026-09-26 and went out on 2026-09-27: the user checked and the models are well sized. Measured on 2026-09-26, the models are about real size: the GM small blocks 0.74 × 0.54 × 0.34 m, the big
Mopar and Ford blocks 0.75 × 0.63 m, the stock disc 0.265 m, a four-barrel 0.26 × 0.17 m; only the stock coil spring
(0.28 m tall, 0.095 m across) is small.

**Part shops** (step 21: `Services/PartShops`, content in each mod's `partshops` folder). Shops on the dealer map's
picture, reached by their own button in the garage (Parts dealers, beside Car dealers: the flow's `part_shops_map` screen, the
dealer map with `partShopsMap`, gold square pins and no car lots), driven out to like a lot, each with its own stock:
- **Kinds** (`PartShopRules.Deals`): a *specialist* sells the new parts whose `"make"` (in the part's file,
  `PartDefinition.Make`) is one of its `makes`, running gear aside; a *speed shop* the new parts of the kinds that make
  power (carburettors, manifolds, blowers, cams, heads, exhaust, air, nitrous) that no car came with (no part of an
  `origin: car` build), any make; a *wheel shop* the running gear; a *junkyard* anything, used.
- **Stock**: the save carries it (`PartShopLocation`, a `DealerLocation` with `Shelves`, `Yard`, `RestockedOn`,
  `PlayerSpent`). A delivery (`PartShopService.Deliver`, every `RestockDays`: 7 days a specialist, 10 a speed shop, 5 a
  wheel shop) keeps most of what is left, tops each line up half the time (`partShops.topUpChance`), and adds lines up to 40-60 different parts (a speed shop 25-35,
  one or two of each, its dear parts rarer). A junkyard's yard holds 20-35 parts (15% of them whole engines of a runnable
  build), 15-75% worn, gone after 21 days; wrecks come in every three days, taken apart off the UI thread. A shop never
  stocked (a game just begun, a shop just opened) is stocked when the map or the shop is opened, whatever the rules.
- **Prices**: the list price × the shop's factor (specialist 1.0, speed 1.1, wheels 0.9) × the difficulty; a junkyard
  asks 55% of what the used part is worth (the paper's sellers about 60%). Less a closing-down sale (the dealers' 20%,
  35% in the last three days) and, for the player, the regulars' discount: 5% after $500 spent at that shop, 10% after
  $2,000. A junkyard's part can be haggled over once: half the time the owner takes 10-25% off. Nothing takes a yard's
  part under 10% over what the yard would pay for it (`PartShopRules.JunkyardMargin`), so nothing bought there sells
  straight back at a profit. A shop that closes forgets its regulars: new owners start from nothing.
- **Selling**: every shop buys the player's parts at a trade-in (40% of worth); a junkyard pays 42% and puts the part in
  its yard at its own price. 42% is under what any shop's new part costs a good customer in the last days of a
  closing-down sale in the easy game (a wheel shop's: about 45% of list), so no new part sells to a junkyard at a profit
  either.
- **Their business** (`PartShopLifeService`, the daily `partshop_review` task, after the dealers' and before the rivals'):
  the dealers' rules (`DealerRules`): about 60% open (6 of the base game's 10), a change about every two months, a
  closing-down sale of seven days in which the bargain hunters take a fifth of what is left each day. Every day the town's
  other customers buy 3% of what is on the shelves and in the yard, and a delivery drops about a third of its lines for
  others, so what a shop carries changes and scarce parts sell out. A shop goes under by its trade alone: the town's
  custom, the same for every shop (0.2 a day), and half a car's for every part the player or a rival buys there, fading
  as a dealer's does. A new shop is of a kind the town has none of open, when one is waiting. The paper and the diner
  hear of it (`text/partshops.json`). A two-year run of the base game (`ShopSim`, scratch): about six open, closings
  spread over the kinds, a specialist carrying 140-220 parts and a yard about 30.
- **The rivals** tune (`EngineTuner.TuneUp`'s `newPrice`) at the cheapest shop that has a part in stock
  (`RivalPrices`, sales counted, else the mail order), and the parts they bought new come off that shelf
  (`TakeForRival`); they tune twice as often while a part shop's sale is on, and a bargain there is street talk.

`catalog.db` is opened per call and exclusively; with parts work running in the background the two catalog repositories
now take turns through `CatalogDatabase.Open` (a semaphore, given back when the connection is disposed, whatever
happens while closing; a call must not open the database again while it has it open).

**Running gear** (`Parts/Cars/RunningGear`, `RunningGearFactory`). Every car also carries its wheels: on the car's own
slots as the game numbers them (rim 101+i, brake 111+i, shock 301+i, spring 311+i for corner i: 0-1 front,
2-3 rear, even numbers left), the tyre on the rim's slot 2. `Car.HasRunningGearAssigned` works like `HasPartsAssigned`:
a car without it gets its factory set the first time it is looked at (`EnsureParts`), worn like its tyres. The factory
set is matched to what the car's own Assetto Corsa data says (`AcCarSpecs`, read from the car's `tyres.ini`,
`brakes.ini`, `suspensions.ini`, `car.ini`, folder or `data.acd`): the tyre nearest in width (one tyre for the car
unless it is staggered by 3 cm or more, the plainest compound of that width), the plainest rim that takes it, and the
brake, spring and shock nearest to the car's figures, which is the smallest the catalog has more often than not, since
the stock parts are all stiffer and stronger than a road car of 1970 (so what the shop sells is an upgrade).
The choice is deterministic: it is also the baseline the export measures against. A tyre only goes on a rim of the
same diameter whose width it takes (`RunningGear.TyreFitsRim`); rims, brakes, springs and
shocks go on any free corner (`Workbench.FindPlaces`). What each part does to the car is read off its specs
with the framework's formulas: brake torque = clamp force × calipers × pad-on-disc friction × disc radius, less with
wear; spring rate; bump damping and rebound (gas, oil, gas-oil); tyre width, radius, rim radius, grip (less as it
wears), load capacity, rolling resistance, pressure; rim offset and mass. Sway bars and suspension arms are inert in
the stock parts and stay out of the shop. In the garage the running gear is
drawn at the hubs (`CarPartsLayout`, `CarViewport3D.Gear`) and picked, taken off and put back like engine parts.
Check: `EngineBench <parts> gear <AC cars folder>`.

**The rear axle** (step 31, `Parts/Cars/RearAxle`, `RunningGearGuess`). A car whose rear wheels are driven carries a rear
axle on its own car slot 401: the housing, a differential carrier on the housing's slot 2, the ring and pinion on the
carrier's slot 2, an axle shaft on each of the housing's slots 3 and 4. The axle also carries the rear corners (the
owner's "diferencial", the whole rear axle): each side's brake (housing slots 5 and 6), spring (7, 8) and shock (9, 10)
and the rear sway bar (11), with what is on them (a drum's shoes, a leaf's traction bars). `RearAxle.HousingSlotFor` /
`CarSlotFor` pair them with the car's own slots they stand for; `RunningGear.Mounted` and `SwayBars` read them there as
from the car's corners (one on the car's own slot first), so the export, the condition, the repairs and the looks are the
same. Only a live axle carries them (the car's data's rear suspension `AXLE`), and never its leaf springs: the axle hangs
on those, so they stay on the car's own slots, while coil springs go on the axle (`RearAxle.Carries`); an independent
rear (the Corvettes' Dana 44 carrier) keeps all its running gear on the car. The workbench follows the same rule (the
garage tells it whether the car is on a live axle), the factory builds cars that way (`RunningGearFactory.HangOnAxle`,
which also moves parts back off the axle), and `GearVersion` 3 and 4 bring an older car's rear corners where they belong.
In the garage the axle and its leaves go on and come off in their order (the work order's `"hangs_on"`, see "The running
gear's work order"): on a car sprung on leaves (a leaf on a rear spring slot, or the factory's rear spring a leaf) the
axle goes on once both rear leaves are on, and a leaf comes off once the axle is off. The garage
draws them at their wheel as before (`CarPartsLayout`: a part on a corner slot of the housing is placed where it would be
on the car's slot, whatever the track), and taking the axle off takes them with it. They are parts of data (`parts/running_gear/`:
`axles_gm`, `axles_ford`, `axles_mopar`, `axles_dana`, and the aftermarket's `axle_speed`) and mate by standard
fittings, so a mod's axle meets the base game's carriers and the other way round: `axle:<family>` for the carrier,
`gears:<family>` for the gears (the GM 10 and 12-bolts' two carrier series as `_low` and `_high`), `shaft:<family>`. What
each does is in its `specs`: `strength` (Nm at the axle it holds new; a worn part a quarter less at the end of its
mileage, a damaged one down to 30%), `ratio`, `diff_power`/`diff_coast`/`diff_preload` (Assetto Corsa's lock), `drag`
(the share of the engine's torque the housing's gears take: a 9-inch's 3.5%, the others 1 to 1.5%). The housing's
`needs` (its carrier, both shafts) and the carrier's (its gears) are held: an axle short of one keeps the
car home in the pack's words. The garage draws the housing with its slot 1 between the rear hubs, facing the car's way.
- **The factory axle** comes from cars.json's `running_gear` (`axle`, `diff`, `ratio`, `shafts`; the Car Catalog Editor's
  three pickers write them) and, for what they leave out, from the parts' `"came_in"` against the car: a part naming the
  car's make before one naming its family, before one for any car (the Dana 44: any make no other axle names), one made
  in the car's years before one that was not, then the engine torque (the peak of the car's own lut) and the years it
  is off by, then the plainest. The ratio is the nearest to the car's own `FINAL`, the carrier the one that locks most
  like its `[DIFFERENTIAL]`. Of the 104 installed cars (2026-10-17): 74 Chevy 10-bolts, 13 Pontiac/Olds/Buick 10-bolts,
  5 GM 12-bolts, 4 Chrysler 8-3/4s, 3 Dana 44s, 3 Ford 8-inch and 2 9-inch; the 12 Corvettes of 1963 on are picked by
  hand onto a Dana 44 (their independent rear's centre section). AC's curves run under the factory figures, so the bands
  are low: a Chevy past 460 Nm gets the 12-bolt, a Ford past 380 Nm the 9-inch.
- **Older saves** (`Car.HasAxleAssigned`): a car that has its running gear but never had an axle gets its factory one
  once, as it is looked at; an axle the player took off stays off. A car whose rear wheels are not driven is marked
  with no axle (its gearbox has the differential).
- **Assetto Corsa** (`AcAxleData`, after the engine, before the damage): the axle takes `[GEARS] FINAL` and `[DIFFERENTIAL]`
  over from the gearbox. On its factory gears and carrier the car keeps its own figures as written (put back over what
  the engine's gearbox wrote, so a stock car's diff and final drive are its author's again); other gears write their
  ratio, another carrier its `POWER`, `COAST` and `PRELOAD` (a clutch posi's falling off to 55% as its clutches wear; a
  spool's and a Locker's never). The axle's weight against the factory one moves `TOTALMASS`, and on a live axle (`[REAR]
  TYPE=AXLE`, all of it unsprung) the rear `HUB_MASS`; the housing's drag against the factory one's scales the power
  curve. No axle part is no change: a car of an older save before its axle, a mod's front-drive car.
- **Breaking it** (harsh, the owner's call): see docs/ac-integration/csp-lua-scripts.md "Damage", `CAR_n_AXLE`. The
  repair shop's "Rear axle rebuild" takes the damage off every axle part; the rivals' daily repairs take it too.
- **Work**: `gear_setup` (own work, a rougher roll: 0.12), `posi_rebuild` (the carrier off the car), `axle_bearing`; at the
  machine shop `gear_setup_shop` and `housing_true` (a bent housing straightened). `weld_spiders` (phase 5) welds an open
  carrier's spider gears into a poor man's spool: a job that turns a part into another (`RebuildJob.Becomes`, the open
  carrier's pack names its `_welded` twin in `"becomes"`, checked by `PartRebuilds.CheckBecomes`; `Rebuilding.Apply` with
  the catalog swaps the part's id, keeping what is on it, its wear and damage). The welded carrier locks both ways (1/1, no
  preload) at 85% of the open one's strength (the welds crack), and is priced near the open one it was (`price_lock` 0.45).
- Prices: `rear_axles` and `axle_shafts` by `strength`, `differentials` by `diff_lock` (0.3 open, about 1 a posi, 2.3 a
  spool), `ring_pinions` flat. Shops: the make specialists stock their make's packs, speed shops the carriers and gears,
  junkyards whatever comes off a wreck.
Check: `EngineBench <parts> axles <AC cars folder>`.

**Period brakes** (step 31 phase 2, `parts/running_gear/brakes_drum`, `brakes_disc`). A drum brake is built as the real
ones are (phase 5): a **backing plate** (`BackingPlate`, the backing_plates shelf, one per drum size and axle, `fits_axle`)
on the corner's brake slot (the car's, or the rear axle's housing slot 5/6) carries the shoes (slot 2), the wheel
cylinder (slot 3) and the drum (slot 4, `takes` `drum:<size>`; the drum's own mount `fits` it, so a drum only goes on a
plate of its size and never on the corner itself), all three its needs. A front drum or disc takes its **hub** (slot 5,
`hub:front`, the hubs shelf: a drum's need, a hub-and-drum comes off as one). `RunningGear.Mounted` reads the drum
off its plate (`CornerParts.BackingPlate`, `Brake`, `Shoes`, `WheelCylinder`, `Hub`; told by the part's group when a
catalog is given, else by a drum on slot 4). The drum gives the size (`force`, `radius`,
in the stock discs' units, every drum under the smallest disc so a disc is an upgrade) and how it heats and cools, the
shoes their friction (organic 1.0, semi-metallic 0.95, sintered 0.9 cold) and their fade curve, the wheel cylinder its
force (a big bore 1.1). `RunningGear.BrakeTorque(catalog, corner)` puts them together, the lining's wear the shoes'. The
period discs (Kelsey-Hayes, Delco-Moraine and Budd 4-pistons, dealer's options: `"option": true`, only a car cars.json
picks them for; a single-piston caliper standard on American cars from 1969) carry their pads' curve themselves. The
stock discs carry no fade of their own.
- **The factory's** (`RunningGearFactory.Choose` with the car's `CarFacts`): a part whose `came_in` fits the axle, the
  car's country (the drums are American: a Jaguar keeps the stock disc), its make, family or any car, and its
  years, a standard one before an option; then the car's weight (each drum size has its band: a 9-inch under 1250 kg,
  an 11 x 2.75 from 1600 kg) and the torque nearest the car's own. Front drums until 1972, rear drums always: every
  American car of the drum years is on drums (EngineBench `gear` shows each car's with its shoes and fade). No such part:
  the nearest stock part, as before. cars.json's `brakes_front`/`brakes_rear` pick (the Car Catalog Editor's pickers).
- **Fade in Assetto Corsa** (`AcRunningGearData`): brakes.ini `[TEMPS_FRONT]`/`[TEMPS_REAR]` get `TORQUE_K`, `COOL_TRANSFER`,
  `COOL_SPEED_FACTOR` and `PERF_CURVE` from the brake (`torque_k`, `cool_transfer`, `cool_speed_factor`) and its shoes'
  `perf_curve`. On the factory brakes the car keeps its author's section when there is one; a car whose author wrote none
  gets its factory drums' (the one change a stock car gets: its drums fade, about 300 °C after one hard stop from 80 mph
  at 0.9 °C a kJ, the organic curve down to 55% by 380 °C; to calibrate against the Car Life tests in game). Other brakes
  write theirs; a stock disc leaves the section as it was.
- **Older saves** (`Car.GearVersion`, 0 before step 31): brought up once, the rear axle added and every brake or spring
  that is what the factory gave the car then (the nearest stock part) swapped for what it gives now, in its wear and
  damage, a drum on its plate with its shoes and cylinder (`RunningGearFactory.BringUpToDate`). What the player put on
  stays. A drum of a save from before the backing plates (its shoes and cylinder on itself) goes onto the plate of its
  size with them as the save is read, on every car and every car for sale (`RunningGearFactory.PutDrumsOnPlates`, in
  `CarPartsService.BringUpToDate` before the joints are checked against the catalog, which would take them off a drum
  with no slot for them); a loose drum on a shelf has them taken off onto the shelf. `GearVersion` 5 then adds a hub to
  every front drum or disc that takes one, worn like the brake.
- **Work**: `brake_reline` on the shoes (on a disc, the disc itself: its pads are part of it; never a drum, whose lining is
  its shoes'), `wheel_cylinder_kit`, `hub_repack` on a hub, `drums_turned` at the machine shop. Each is shown done where
  the part is (step 30's job show: the grime clearing off it in place, or on the workbench for a part that is off), the
  same for the running gear as for the engine.
- Not done: a master cylinder, a booster and a proportioning valve. Assetto Corsa has no pedal effort: full pedal is the
  brakes' whole torque, so a booster would be a gate that changes nothing, a proportioning valve only `FRONT_SHARE`.

**Springs, traction bars, sway bars, shocks** (step 31 phase 3, `parts/running_gear/springs_leaf`, `springs_torsion`,
`sway_bars`, `shocks_period`). Parts of data like the drums:
- **Leaf springs** (`["LeafSpring", "Spring"]`, the springs shelf, a corner's spring slot): a ladder of stock rates from 90
  to 370 lb/in (`force` in N/m, 1 lb/in = 175.1 N/m), `came_in` American cars' rear on an `AXLE`, so every live-axle car's
  own rate has one near it; aftermarket mono-leaves and Super Stock springs (no `came_in`). Each has a `wrap` (multi-leaf
  1, mono-leaf 1.3, Super Stock 0.8) and a slot 2 a **traction bar** goes on (`["TractionBar"]`, the traction_bars shelf,
  speed shops: Traction Master 0.6, Lakewood 0.55, slapper 0.5, ladder bars 0.25 of the wrap, `wrap_factor`).
  `fits_axle` `"rear"` and `fits_suspension` `"AXLE"`: the workbench offers a leaf only the rear spring slots, and one
  under a car whose rear is not a live axle keeps the car home (a problem). GM's coil-sprung live axles (the A-bodies, the
  full-size cars) get leaves too by the guess: cars.json's `springs_rear` picks a coil for them.
- **Torsion bars** (`["TorsionBar", "Spring"]`, the springs group takes the `TorsionBar` class): Chrysler's front springs,
  .85 to .92 inch by the car's weight (`came_in` family mopar, front, `DWB`), rate by the diameter's fourth power;
  heavy-duty 1.00 and Super Stock 1.03 aftermarket. `fits_axle` `"front"`, `fits_suspension` `"DWB"`: a Mopar whose data
  is a strut front keeps the stock coil.
- **Sway bars** (class `Swaybar`, the sway_bars shelf, now running gear): on the car's own slots 402 (front) and 403 (rear,
  `RunningGear.SwayBarSlot`), drawn across the axle a little ahead of it. `arb_rate` in N/m as `[ARB]` counts it, by the
  diameter's fourth power, front 11/16 to 1 inch stock, rear 9/16 to 7/8; bigger ones aftermarket. The factory's is the
  stock bar nearest to the car's own `[ARB]`, none on an axle whose data has none (`RunningGearFactory.Choose`, the axle's
  `SwayBar`). The 48 stock sway bars have no rate: nobody sells them.
- **Period shocks** (`ShockAbsorber`, no `came_in`): Gabriel heavy-duty, Koni adjustable, a 90/10 front drag shock (stiff
  bump, a sixth of the rebound: the front comes up easily) and a 50/50 rear drag shock. The factory shock is still the
  nearest plain part: `aftermarket` parts never count as the factory's (`Plain`).
- **In Assetto Corsa** (`AcRunningGearData`): the spring rate by mounted over factory as before; the rear springs' wrap
  (each spring's `wrap` times its traction bar's `wrap_factor`, against the factory spring's) scales `[AXLE]
  TORQUE_REACTION`, its sign kept (a ladder bar on a stock leaf: a quarter of it); `[ARB] FRONT`/`REAR` by the mounted
  bar's rate over the factory's, the bar's own rate added where the factory gave none, 0 with the bar taken off. A stock
  car's suspensions.ini comes out untouched. What it does on the strip is to see in game: axle hop on a hard launch on
  soft leaves, less with bars (the timeslip's 60-foot).
- **Older saves** (`GearVersion` 2): `BringUpToDate` swaps the factory coils that are now leaves or torsion bars and adds
  the factory sway bars, worn like the axle's springs.
- **A leaf is one part** with its hanger, shackle and U-bolts (phase 5 made them parts of their own; the owner found them
  noise and they went). Work: `leaf_rearch` at the machine shop (a sagged or damaged leaf bent back to its arch, its eye
  bushings new).
- **Picked per car** (phase 5): the Car Catalog Editor has a brake and a spring picker for each axle under the rear
  axle's (`AxlePicks`, `ICarPartsService.RunningGearFor`), "Guessed: ..." first, then the parts of the shelf made for that
  axle (its `came_in` naming that axle or none, `fits_axle`): a GM car on coils at the rear picks a coil there. They write
  cars.json's `brakes_front`, `brakes_rear`, `springs_front`, `springs_rear`.
- Not done: lowering springs and blocks (ride height).

**The running gear's models** (step 31 phase 4). Every part under `parts/running_gear` has a KN5 beside its file, made by
tools, never by hand:
- `tools/RunningGearParts` (C#, on `tools/Shared` `SceneMesh`, with a lathe, a tube, a swept rod, turned boxes and hex
  heads in `GearMesh`): the nine axle housings (a Salisbury's snout and bolted cover shaped per family: the Danas'
  lopsided, the 12-bolt's twelve bolts; a banjo's third member bolted in front for the Ford 8 and 9-inch and the 8-3/4),
  a ring and pinion per family (sized by its ring gear), the axle shaft, the drum's wheel cylinder, the four period shocks,
  a torsion bar and a sway bar at each size their parts give (`diameter_in`), the four traction bars. Plain ksPerPixel
  materials of small swatches (cast iron, steel, black paint, rubber, chrome, the shocks' colours).
  `RunningGearParts build <parts folder>`; `RunningGearParts obj <kn5> <obj>` dumps one to look at.
  `render_preview.py <spec> <out>` renders OBJs placed as the garage does (Blender's Workbench), to check places and sizes.
- `tools/RunningGearParts/prepare_standins.py`: stand-ins cut out of CC BY 4.0 Sketchfab downloads (Blender, then
  `tools/Props` `prepare_prop.py` and `PropBuilder --keep-origin`): the drum at four sizes (by the drum's `diam_mm`), the
  period disc with its caliper, the shoes (two to a drum), the leaf spring, the carrier's case. Each folder with one has a
  `CREDITS.txt`. They are to be replaced by generated ones in time; the downloads stay out of the repo (the
  script names the zips it takes and where each came from).
- **Spaces**: a part's model has its origin where it mounts. On the car (the housing at the middle of the rear hubs, the
  sway bars, a leaf with its traction bars): +X the car's left, the front -Z. At a wheel (a drum and what is on it, a
  shock): +X outboard, the hub's axis along X; `CarPartsLayout` turns it round for the right side. A spring's slot faces
  the way the car does on both sides (a leaf or a torsion bar is not the same end to end). The right axle shaft's slot
  on every housing is turned half round (`"rotation": [3.14159265, 0, 0]`), so the one shaft model points out both sides.
  The shocks' studs are staggered, behind the axle on the left and ahead on the right, as the side turns a shock round.
- **Fitted to the car** (after the owner's looks in game): the housing reaches the car's own track with its tubes drawn
  longer beyond its middle (`CarPartsLayout.AxleStretch`, a `PartStretch` on the housing's placed part: what is farther
  than 0.25 m from the centre moves out, so its ends, 0.655 m out in the models, go into the drums 0.02 m in from the
  rear hubs, where a drum hides them; the centre section keeps its size), the shafts inside the tubes moved out as far.
  The drums and period discs mount 0.08 m outboard of the brake slot (their own slot 1 at `[-0.08, 0, 0]`), their shoes
  and wheel cylinder with them. The middle of an axle (the housing's centre section, cover and nose, the ring and pinion,
  the carrier's case) is drawn 1.1 times its ring gear's size (`Parts.CentreScale`; the truck housing's 1.05).
- **The stock coil springs** are drawn with a CC BY coil (`running_gear/coil_springs`, "COIL SPRING" by VR DESIGNER,
  credited beside it) through the base game's own `parts/models.json` (every stock coil spring by its id), so the
  pack is left as it is. The stock model was 96 mm across and 277 mm tall, small for the cars of the day; this one is
  135 mm across and 320 mm tall, standing up from the hub's height (its model's space is theirs: their mount slot 0.4 m
  under the origin).
- Phase 5's parts (`RunningGearParts build`): a backing plate per drum size (`plate_<size>`, a pressed dish just inboard of
  the drum's open side, its size and place from the stand-in drum's model), the front hub (boss, studs, dust cap, in the
  drum's space).
- Not modelled: the other stock parts keep their own; a mod's running gear part without a model is drawn as a small box.
- **A private truck mod** (`Content\truckparts`, git-ignored): `tools/RunningGearParts/prepare_truck_parts.py` cuts the
  leaf spring (with its hangers, shackle and clip), the drum (four sizes), the axle housing (for the Dana 44 and 60) and
  a shock (for the Gabriel) out of "Pickup truck chassis" (Sketchfab, CC BY-NC 4.0) and gives them to the base game's
  parts through the mod's `models.json` (docs/modding.md). The owner is asking its author before it can go in the game;
  until then a clone of the repo draws the base game's own models.

**The wheels and the lift** (step 31 phase 6). The parts view draws the rims and tyres as parts: a rim with the middle of
its tyre (its slot 2, the tyre's seat) on the hub, the AC model's WHEEL_xx dummy, turned the other way from the drums: the
The stock rims' face is -X (the owner's look in game; a render had seemed to say +X), the tyre on the rim's slot 2. The car's own wheels (its WHEEL_xx nodes,
with the part wheels the scenes put on them) are left out of the ghost while the parts are on show
(`GarageRenderer.DrawCarWithoutWheels`), so the two never double; out of the parts view the car is solid with its own. The
`wheels` show (`part_groups.json`, icon `icons/wheel`) hides them to see the drums.
A wheel comes off and goes on only with the car up on the garage's lift (the work order's `"lifted": true` on `rim`;
`CarWork` takes whether the car is up), and what is behind it waits for it as before. The lift is always in the garage:
`Car.LiftStop` (saved; `Down`, `WheelsClear` 0.3 m, `Full` 1.4 m, `Parts/LiftLayout`), stepped by the parts row's lift
button (down, the wheels clear, full height, down; the rules' `time.lift` minutes a move). The car comes down only on its
four wheels (`CarWork.WhyNotDown`); only the car in the bay is up, and another taking its place lowers it, unless a wheel
is off it (it stays up, and comes back up with it). A car up does not race, drive or get towed out
(`CarCondition.WhyCannotRace`, the garage's gas station and paint booth exits), and the engine hoist works only with the car
down. In the 3D view (`CarViewport3D` "Lift") the whole car rises, its wheels hanging with it, through the body rock's
placement (`D3DViewportBase.LiftHeight`), and the parts with it (`BodyLean`); the hubs the parts are placed by are taken
with the lift's height off. The lift is a garage prop (`props/lift`, drawn from boxes unless a mod gives a model with a
`lift_post` node), standing under the middle of the car in the bay, its post rising to catch the body by the first stop
(`LiftLayout.SaddleTop`). The wheels stay on the floor for the first 0.1 m and then hang that far below the body
(`LiftLayout.Droop`): the body rock lowers the AC model's WHEEL/HUB/SUSP/DISC groups, and the renderer the parts that
hang with them (`GarageRenderer.SetUnsprung`/`Droop`: on a wheel or brake slot, the rear axle with all it carries, a leaf
spring), parts on their way included. A wheel taken off takes the car's own out of sight at that corner in every scene
(`ShownSet.MissingWheels`). The camera may look up from under the car while it is up. Sounds: `lift.move` (a loop) and
`lift.stop`, synthesised by `tools/RebuildSounds`.

**Threads.** The game state and the save file belong to the UI thread. Whatever takes long (loading the catalog,
putting engines together for ads, market listings and cars without parts) runs on a worker thread on objects nobody
else has yet, and the result goes into the game state back on the calling thread (`RefreshAdsAsync`,
`EnsurePartsAsync`, `UsedCarMarketService.RefreshMarketAsync`). Two scheduled tasks therefore hand the day back before
they are done (`MarketRefreshTask`, `PartsAdsRefreshTask`); they are registered last, so the race simulation and the
event generation are finished first, and whoever saves after spending time awaits `SpendTimeAsync` before saving.
`ICarPartsService.IsAvailable` never throws: parts that cannot be read count as no parts, for the session.

## Assetto Corsa export (`Parts/Export`)

What a car's parts make of it goes into the car's own data files for a race, by changing them as little as possible
(`IniText` keeps comments, order and untouched lines; a section that occurs twice, a second tyre compound, is reached
by occurrence). `AcCarBuild.Generate(catalog, CarBuild, readFile)` puts it together: the engine's files, then the
running gear's over them, and a list of `Problems` that keep the car from being driven (no engine, an engine that does
not run, a corner without a wheel, brake, spring or shock). `AcCarData.Open(carDirectory)` reads a car's data whether
it is a folder or `data.acd` (`AcdFile` reads the packed form; the key is worked out from the folder name the way the
game does it, checked against every packed car of both installs on this machine).

**Engine and transmission** (`AcEngineData`): the curve **replaces** the car's. The file says what the dyno says.

| File | Changes |
|---|---|
| `power.lut` | Flywheel torque, exactly (no drivetrain loss taken off: `DrivetrainEfficiency` = 1) |
| `engine.ini` | `INERTIA`, `LIMITER`, `MINIMUM`; `COAST_REF` = 12 Nm per litre at the limiter × the build's friction over a stock oil pan's (`engine_friction_fwd`, 0.0002 is the median of the builds, clamped ½..2: a racing pan lets the engine spin freer); `DAMAGE/RPM_THRESHOLD` = what the weakest rotating part survives; the car's own `TURBO_n` removed |
| `engine.ini [TURBO_n]` | One per turbo, the specs' first, each with its lag (0.992 up, 0.985 down unless its figures say); with several, the gain at each speed is shared by the boost each makes there. Only with a turbocharger part: the game's own turbo (lag, gauge, boost damage) fitted to how much more the boosted curve makes than the same engine without its charger: `MAX_BOOST` = `WASTEGATE` = the peak gain, `REFERENCE_RPM` where 95% of it is reached, `GAMMA` by least squares on the way up. The lut is divided by the boost the game adds back, so the torque with the throttle open stays the dyno's. A supercharger is crank-driven and has no lag: it stays baked into the curve |
| `drivetrain.ini` | `GEARS` count/ratios/reverse/final, `DIFFERENTIAL` lock; `CLUTCH/MAX_TORQUE` = the clutch part's clamp figure (`maxF`) × 2.6 Nm, set so every factory engine's clutch holds it (the big-block packs put a 300 behind 770 Nm): a built engine outgrows a stock clutch and it slips; `AUTO_SHIFTER` `UP`/`DOWN` when the car has them. `TRACTION` is left alone: the body decides |
| `ai.ini` | Shift points: `UP` near peak power below the limiter; `DOWN` at most 90% of where the widest gear step lands after an upshift, so wide-ratio boxes do not hunt |
| `setup.ini` | Gear ratio selectors removed; they would override the transmission |
| `car.ini` | `TOTALMASS` moves by what the engine weighs more or less than the car's factory build (`FactoryEngineMass`) |

A car without `engine.ini` or `drivetrain.ini` throws (`FileNotFoundException`) instead of producing skeleton files; a
build without a limiter (`RPM_limit` 0) is limited at the end of its curve.

Blowers: the dyno's boost peaks where the blower's specs say it works best. They give its working band
in engine speeds multiplied by the square of the drive ratio, so `EngineDyno.Boost` compares it with the engine speed
times that square (it compared it with the speed times the ratio before, which put a 4:1 roots blower's peak at
10,600 rpm and made it behave like a big turbo: 55% boost at 3,000 rpm and rising). No build of `engine_builds.json`
is blown, so the rated table did not move; a mounted blower now makes its torque low down.

**Running gear** (`AcRunningGearData`): the car's figures are **scaled**, never replaced. Whatever units the source
game and Assetto Corsa think in, the car's author set its numbers for its factory parts; what is mounted moves each
number by its ratio to the factory part, averaged over the two corners of the axle. A car on its factory parts comes
out byte for byte (a factor of one leaves the line alone; a file without an edit is not written), a worn car a little
weaker (brake torque `0.2 + 0.8 √wear`, grip `0.85 + 0.15 wear`).

| File | Scaled by mounted / factory |
|---|---|
| `tyres.ini`, every `FRONT*`/`REAR*` compound | `WIDTH`, `RADIUS`, `RIM_RADIUS` (and `ANGULAR_INERTIA` by radius²), `DX_REF`/`DY_REF`/`DX0`/`DY0` by grip, `FZ0` by load capacity, `ROLLING_RESISTANCE_0/1`, `PRESSURE_STATIC`/`PRESSURE_IDEAL` |
| `brakes.ini` | `MAX_TORQUE` and `FRONT_SHARE` from the front and rear torques scaled separately (a brake's torque with its shoes and wheel cylinder); `HANDBRAKE_TORQUE` by the rear; `[TEMPS_FRONT]`/`[TEMPS_REAR]` written, not scaled: the period brakes' fade (see "Period brakes") |
| `suspensions.ini` | `SPRING_RATE` (or a coil-over's `RATE`), `DAMP_BUMP`/`DAMP_FAST_BUMP`, `DAMP_REBOUND`/`DAMP_FAST_REBOUND`; `TRACK` moves by twice the rim offset difference, `HUB_MASS` by the unsprung mass difference; `[AXLE] TORQUE_REACTION` by the leaves' wrap, `[ARB]` by the sway bars (see "Springs, traction bars, sway bars, shocks") |
| `car.ini` | `TOTALMASS` by the running gear's mass difference |

What has no lever on either side stays a gate: batteries, alternators, water pumps, radiators, distributors and the
like are plain parts (no figures) and Assetto Corsa has no battery or cooling model (CSP
only synthesises gauge channels). They matter through the block's `needs`: without them the engine does not run, and a car
whose engine does not run does not race.

## Apply and restore (`Services/CarDataOverlay`, `Services/Race/RaceCarDataService`)

The rule for every change to the Assetto Corsa install: the originals are kept aside first, the change is the
smallest that does the job, and whatever happens the originals go back. `CarDataOverlay.Apply(carId, files)` keeps the
car's originals under `%APPDATA%\StreetCorsa\AcRestore\<car>\files` with a `manifest.json` (which files, whether
each existed), written before anything in the install changes, then writes the files into the car's `data` folder.
A car that ships packed (`data.acd`, no folder: the GT500) gets the whole of its data unpacked into a `data` folder
for the race, since the game reads the folder when there is one, and the folder removed after. `Restore`/`RestoreAll`
put everything back and delete the kept copies; the launcher calls `RestoreAll` in its `finally`, and `App.OnStartup`
calls it too, for what a crash or a power cut left behind.

**Sound** (`Parts/Export/AcCarSound`, the overlay's sfx section). Assetto Corsa hangs a sound on a car folder: it opens
`sfx\<car>.bank` by file name and finds `event:/cars/<car>/engine_ext` and the rest by GUID through the car's
`sfx\GUIDs.txt` (a Kunos car has no file of its own and is in the install's `content\sfx\GUIDs.txt`). The GUIDs inside
a bank are its author's and never change when the bank is reused, so a sound is a bank plus its GUID lines, and it
plays under any car once the lines are written for that car's id. `CarSound(BankPath, GuidsText, DonorId)` is one;
`AcCarSound.FromCar(carDirectory, masterGuids)` reads a car's own, and `GuidsFor(carId)` writes the lines for another
car: buses, VCAs and snapshots as they are, the `common` bank and the donor's, the donor's events and the ones outside
any car (collisions, surfaces), everything else dropped, since mod authors ship the whole master file with a hundred
other cars in it more often than not. `CarBuildResult.Sound` carries the choice; `CarDataOverlay.Apply(carId, files,
sound)` moves the car's own bank and GUIDs into `sfx\_streetrod_keep` (a rename, whatever their size), hard-links the
new bank in under the car's name (a copy only when the two are not on one volume), writes the GUIDs, and the manifest
says so (`Sfx`: whether the folder, the bank and the GUIDs existed). `Restore` deletes the link and moves the originals
back; every step checks what is there, so a restore cut short finishes the next time. A donor that races on another
sound itself has its own bank in its keep folder, and `AcCarSound.OwnBank` finds the bytes wherever they are, so a
car can take the sound of a car whose sound is swapped in the same race (two cars on each other's banks included). A
file already in the keep folder is the car's own, from a race whose manifest was lost: it is never overwritten, the
file in its place goes. A sound is never worth a race: a bank that is gone leaves the car on its own sound, and a
choice that throws leaves the car on its parts with its own. Every installed car has
`engine_ext` and `engine_int`; four lack `limiter` (`AcCarSound.MissingEngineEvents`).

**Two cars of one model** (`CarDataOverlay.CreateClone`, `Parts/Export/AcCarFolder`). An opponent in the player's
model races in a copy of the car folder under its own id (`<car>__sr_opponent`), so each car has its own data and
sound. The copy is made first, from the untouched originals, before the player's changes go into the car itself
(what an earlier race could not put back goes back first): models, textures, skins and ui are hard links (a
read-only file is copied, since its flag is shared with the link, and so is a read-only bank wherever one is linked);
the data is a real copy (the car's folder, or what its `data.acd` holds; the copy has no `data.acd`, whose key comes
from the folder name) with the opponent's files over it; the sfx folder gets the opponent's sound, or the car's own,
under the copy's name. A marker file (`streetrod_clone.json`) goes in first and comes out last; only a folder with
it is ever deleted, every reader of the install's cars (content service, catalog import, sound harvest, EngineBench)
goes through `AcCarFolder.InstalledCars`, which leaves it out, and `RestoreAll` (after every race
and at start-up) takes every copy away. The diner sets the opponent's `MODEL` in race.ini to the copy's id; results
are matched by car instance, not model, so nothing else changes. An opponent whose car cannot run races in a copy
as its author made it. Checked with a dry-run console on full car folders (the Camaro, the packed GT500): the car's
own folder hashes the same after the copy, after the player's changes next to it, after `RestoreAll`, after a crash.

**Sound library and matcher** (`Parts/Sounds`, `CarPartsService.Sounds`/`ChooseSound`). Two sources, one list
(`SoundLibrary`): a folder under any mod's `sounds` is a sound (a bank, a `GUIDs.txt`, an optional `sound.json` with
name, donor id, cylinders, family, rev ceiling, tags; drop one in and the game has it, see the README in the base
game's, which ships none: the banks are other people's), and
every installed car's own bank is a sound too, harvested in the parts warm-up (`Harvest`): banks that are the same bytes
(size plus the first 64 KB) are one sound with those cars as carriers, its rev ceiling the highest `LIMITER` among
them, its cylinders and family those of the stock engine the parts would give the carriers, when they all agree
(a bank under a Chevrolet, a Ford and a Plymouth says nothing). The install's 165 cars come to 59 sounds in about a
second. `sounds.json` at the library root pins a block to a sound and corrects or excludes a car's bank. A sound
whose GUIDs give its donor no engine (neither `engine_ext` nor `engine_int`) is left out rather than racing silent;
a curated sound without `donor_id` whose bank name is not the donor takes the one car its GUIDs give an engine to. A
folder, a car or a sounds.json that does not read is one line in `SoundLibrary.Problems` (logged) and the rest
stands. A sound keeps only the GUID lines its bank answers; the master file is read once for every Kunos car. The
matcher (`SoundMatcher`) scores every sound for a `SoundRequest` (block id, cylinders, family, limiter, from the
block part and the dyno): cylinders decide (a six never gets a V8 bank), family counts, and the ceiling must reach
the limiter: below it by more than 500 rpm (Content Manager's margin) the bank runs out of samples and the penalty
grows with the shortfall; a little short costs a little; above, the tighter the better, since a bank made for
8,500 rpm idles too low at 700; a curated sound beats a harvested one on equal terms; among equals a hash of the
block id and the sound id decides, so a car sounds the same every race until its engine changes and different
blocks spread over equal sounds. `ForCar` returns null when the choice is the bank the car already ships, so
nothing is swapped. `RaceCarDataService.Prepare` sets `CarBuildResult.Sound` for a car that can drive.

The diner prepares the data before a race (`RaceCarDataService.Prepare(car)`: the engine on the dyno, the running gear
against the factory's, for the player's car and the opponent's, each on its own parts). A player's car with problems
does not race ("Your car is not going anywhere: no brake front right"); an opponent's car with problems races as its
author made it. The files ride on the `LaunchIntent` (`CarData`) and the launcher applies them after the race config
and before `acs.exe`. An opponent in the player's model races in a copy of the folder (see "Two cars of one model");
the launcher still refuses a second `Apply` to a car changed for the same race, in case. A manifest an earlier
race could not restore is put back before the car's data is changed again. A car without parts (no catalog, an older
save) races on the data its author gave it.

Check: `EngineBench <parts> car <AC car folder> <build id> [output folder]` writes every file the car's parts change,
with the factory running gear mounted (so only the engine files differ).

## Damage and repairs (`Parts/Cars/CarCondition`, `RepairShop`, `Parts/Export/AcDamageData`)

A part's `Wear` is mileage: a new part, or a job of `parts/rebuilds.json` (below), undoes it; its `Tear` is damage and a
repair takes it off.
Races set both from what Assetto Corsa reports at the end (`CarCondition.ApplyRace`, called by
`RaceResultProcessor`):

- AC's engine life (1000 new) lands on the rotating parts (groups Crankshafts, Connecting rods, Pistons, Camshafts).
  The part that gave is the weakest one (`CarCondition.WeakestRotatingPart`; when two are as worn, the rods first,
  then the pistons, the crankshaft, the camshafts). Its `Tear` goes down to life/1000, and the rest of the rotating
  assembly loses 35% of that loss (`SharedEngineDamage`). The report names it ("a connecting rod let go"), and so does
  the repair shop's engine rebuild ("Blown: the <part> let go", with the part's catalog name). The next race starts with 1000 x the weakest
  one's `Tear`.
- AC's gearbox damage (from 0 each race) comes off the transmission's `Tear`.
- A bent steering rod (AC's `suspension_damage` / 0.05 m) comes off that corner's spring and shock.
- The tyre's `Wear` loses AC's tyre wear; a blown tyre's `Tear` goes to 0.
- The engine gets 1/40000 of `Wear` per km driven.
- Body damage has no part to go on: it stays on the car, `Car.BodyDamageKmh` (front, rear, left, right), in AC's km/h.
  All zones together at 200 km/h or more is a totaled car.

`CarCondition.RefreshFigures` works the car's own figures out from the parts (engine: its parts' mean `Wear` x its
life; gearbox and tyres: `Wear` x `Tear`; body: from the damage), so prices and the garage follow the parts.
`CarCondition.StartState` gives what the next race starts with; `AcDamageData` puts what AC cannot be told into the
car's data (a worn gearbox shifts slower, a bent axle gets the mean bend of its corners as extra `TOE_OUT`).
`CarCondition.WhyCannotRace` keeps a car home with a blown engine, a gearbox or corner under 10%, a blown tyre, a
totaled body or under 2 litres in the tank (a tank whose size no race has reported yet counts as full).

`RepairShop.Jobs` lists what the garage can fix, each with its cost and time: an engine rebuild (every engine part
with `Tear`), a gearbox rebuild, a corner straightened, a new tyre for a blown one, body work, rust repair and respray
(`RustCost` $900 and `PaintCost` $300 by how far each goes; a respray alone for worn paint without rust), a new
windscreen (`WindscreenCost` $60). A part repair costs
`PartShare` (60%) of the damaged parts' new price times the damage, plus `Labour` ($15); body work `BodyCostPerKmh`
($8) per km/h.

**How used a car looks** (`Parts/Cars/CarLooks`, docs/roadmap.md step 28 ideas 3 and 4): rust, worn paint and a cracked
windscreen on the car (`Car.Rust`, `PaintWear`, `GlassCracks`), rolled when it comes into the game by who sells it,
rust growing every day (`RustTask`), a front hit of 25 km/h cracking the windscreen. A spring under 0.6 sags its corner,
5 cm broken (`CarLooks.RideHeight`): the scenes lean the body by it, AC gets a shorter `ROD_LENGTH` per axle. The game
draws it all as a wear layer over the car's body (`Looks/WearLayer`), in its scenes and in AC. The same list fills the tank (`FuelPerLitre`, $0.11) and washes the car (`Wash`, $3), 15 minutes
each. The garage's Repairs button shows them (`Dialogs/RepairShop`); the parts view shows a part's damage next to its
wear.

**Rebuilds** (`Parts/PartRebuilds`, `Parts/Cars/Rebuilding`, docs/roadmap.md step 30): the parts view's "Work on it"
jobs take a part's mileage off, the 1960s way. Each job of the mods' `parts/rebuilds.json` is for parts of its work order
kinds or shelves: a kit (a carburettor rebuilt, a tune-up, a valve job, a ring job, bearings, a gearbox kit, brakes
relined) or a rebuilt unit from the parts counter, the old part its core (the exchange also takes the damage off). A job
brings `Wear` up to its `restores`, never down; costs its `cost` share of the part's new price (at least
`cost_at_least`) at the save's part prices; takes its `minutes`; may need the part off (on the shelf, or the engine on
the stand) or bare; and a part worn below its `min_wear` is too far gone for it (the machine shop, phase 2). The car's
figures are worked out again after a job on a part in it. The job is then shown done (`Screens/Garage/RebuildShow`,
`PartsWorkbenchViewModel.ShowWork`): its `steps` in turn over their looping sounds, the part on the workbench
(`WorkbenchLayout`, `CarViewport3D.WorkPiece`) or where it is mounted, its grime (`Looks/PartGrime`,
`GarageRenderer.SetWork`/`WorkGrime`) fading from its old condition's to its new one's, the clock and its condition
going up; Skip ends the showing, not the job.

## Cooling and oil (`Parts/Cars/EngineCooling`)

What the race mode's heat and oil work from (race.ini `CAR_n_COOLING`, see `docs/ac-integration/csp-lua-scripts.md`,
"Heat and oil"). The radiators, fans and water pumps are plain parts with no figures. What one is comes from data when a
mod says so: a work order kind's `"cooling"` (`"radiator"`, `"water_pump"`, `"fan"` or `"none"`, `WorkKind.Cooling`,
stamped on the parts as `PartDefinition.CoolingRole` when the catalog loads; a radiator kind that `serves` a fan kind has
its own fan), and a part's `"performance": true|false` in its file (`PartDefinition.Performance`: a performance
radiator, water pump or fan, a deep race oil pan). Parts that say nothing, the stock ones among them, are known by
the words of their name (`KindOf`), whole words only ("race" is not in "brace"). A car's
cooling was sized for its engine as it left the factory (`FactoryCooling`, read once per stock build): the car's
factory power with its own block, or a swapped-in block's least powerful factory build, since the radiator goes with the
engine. A performance radiator carries off 35% more, a performance water pump 10% more, and tuning past the factory's
power is what makes an engine run hot. A radiator, a water pump or a fan the factory build had is missed when it is
gone. A deep or race oil pan holds its oil to more g than a stock one. `ICarPartsService.RateCooling` rates a car;
`RaceCarDataService.Prepare` does it for every race (not a free run). `ICarPartsService.EngineCoolingOf` hands a tuner
the same rating for an engine at a power: `EngineTuner.TuneUp` fits a performance radiator or pump when an upgrade
outgrows the cooling (`CoolEnough`, 1.0), and skips an upgrade it cannot keep above `RunsHotBelow` (0.85).

## Bench (`tools/EngineBench`)

A Content folder as the `<parts folder>` (and as `renew`'s older folder) loads the parts of every mod in it, as the
game does: `EngineBench "Street Corsa/Content" bench mercury-255-race` benches a mod's build.

```
EngineBench <parts folder> list | rated | all | inputs | show <build id>
EngineBench <parts folder> rated --base-only           the same with the base game's makes, groups, classes and fuels alone (--base-only goes with any command)
EngineBench <parts folder> snapshot <file>             every build's whole report, one JSON line each, to diff two versions by (written whole or not at all)
EngineBench <parts folder> export <build id> <car data folder> <output folder>
EngineBench <parts folder> cars <AC cars folder>       factory engine suggested for every car, with the runners-up
EngineBench <parts folder> tune <build id> [count]     engines a used car of that build may turn up with
EngineBench <parts folder> bench <build id>            every part comes off and has to find its way back
EngineBench <parts folder> renew <older parts folder>  engines as a save made with older parts holds them, brought up to date
EngineBench <parts folder> gear <AC cars folder>       factory running gear chosen for every car, against its own data
EngineBench <parts folder> layout <AC cars folder>     where every car carries its engine, guessed from its weight and driven wheels
EngineBench <parts folder> car <AC car folder> <build id> [output folder]   every data file the car's parts change
EngineBench <parts folder> sound <AC car folder> <car id> [output folder]   the car's sound written for another car id, with the engine events it lacks
EngineBench <parts folder> sounds <AC cars folder> [sounds folder]        the sound library (curated, then harvested with carriers), and the sound every build gets, with the reason
```

`car` also prints the sound the car would race with, and whether it is the car's own bank.

The overlay's sfx section was checked with a dry-run console (a mirror of a car's `data` and `sfx`, the folder hashed
before and after): sound plus data, sound only with a crash and `RestoreAll` from a fresh instance, a restore cut
short, a car without `GUIDs.txt`, the packed GT500, and a mirror on another volume for the copy fallback. After the
PR review, a scratch console on made-up cars checked: a car taking the sound of a car already swapped in the race and
two cars on each other's banks, a copy made while an earlier race's changes were still in, a kept bank whose
manifest was lost, a donor bank that is gone, read-only donors, and the library's left-out sounds, bad json and
`"pins": null`. The harvest of the install came out the same line for line.

## Not done yet

- Engine sounds: the base game ships no bank (none is ours; decided 2026-10-20), so for a player every choice is a
  harvested bank. The engine mods carry copies of the banks their blocks are pinned to (eight curated, eight off
  installed cars, none ours): fine for testing, not to be shared until each author agrees; the owner means to record
  banks of our own. The harvest tags a bank with the stock engine the parts would give
  its car, so a slant-six Valiant carrying a V8 bank is tagged 8 cylinders; `sounds.json` corrects that per car.
  Mufflers and headers do not change the sound yet; a garage preview through fmodstudio64.dll is not started.
- Working on an engine outside a car (an engine stand): on the shelf an assembly can be taken apart, but parts only
  go together on a car.
- Tuning UI (tuning is kept by spec name and set directly). Wear from mileage is only the
  engine's small per-km share: brakes, clutch and the rest of the running gear do not wear by the km yet.
- Nitrous: the parts exist and gate on their slots, but neither the dyno nor Assetto Corsa has a model for it.
- The running gear's placement in the garage is an estimate from the hubs (springs and shocks 24 cm inboard); a
  car-level part cannot be nudged with F5, only parts on a parent.
- Camber and toe: the stock suspension arm parts carried them but nothing applies them, so neither does the export.
