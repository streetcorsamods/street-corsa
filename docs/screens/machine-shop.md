# Machine shop

`Screens/PartShop/MachineShopScreenView`, `MachineShopScreenViewModel`: a part shop of kind `machine`, reached from the part
shops' map (the navigation's `part_shop` screen opens it for that kind). Built in roadmap step 30, phases 2 and 4
(2026-10-17).

## What the player sees

- **The room** (`ShopViewport3D`, the scene `ShopScenes.For("machine")`): a boring bar, the crank grinder under the sign,
  the head bench and its mill, the hot tank, two engine stands and a rack. On them, what the player has left at this shop,
  then a few of the day's other jobs (blocks, heads, cranks drawn the same for the shop and the day). The places name
  the groups they take first (`engine_blocks` on the boring bar, `crankshafts` in the grinder, `cylinder_heads` on the
  bench).
- **The counter**: the machinist on the left while the AI is on; *your parts* (at the shop with the day each is back,
  the engine on the stand, the shelf); *the work* on the one picked and the parts on it (`IMachineShopService.Offers`):
  the size it cuts to, the days, what it comes back as, the new parts fitted, the price; speed work marked; a job that
  cannot be done says why. "Have it done" pays and leaves the piece (`Order`); work on a piece already there keeps it till
  the longest job is done. The prices are worked out off the UI thread (a whole engine's go on the dyno): the list says
  "pricing" meanwhile and stops saying it whatever fails (a saved tree the catalog can no longer copy is logged, the
  counter offers nothing for it); a pricing dropped for a newer one, or by leaving, is cancelled and let go of. The
  pieces and the jobs are listed in one go each (`BulkObservableCollection.ReplaceAll`), the jobs once a refresh.
  The machinist's picks per ask, the whole engines tried on the dyno for one and the other customers' work standing
  about are the rules' `partShops.mostPicks`, `machineDynoRuns` and `othersWork`; the other work is drawn by the
  shop and the day's number, the same on every machine. The room, the counter and the machinist are the pieces the
  part shop's screen uses too (see `docs/screens/part-shop.md`, "What the two counters share"); the `IShopCounter`
  half of the view model is `MachineShopScreenViewModel.Counter.cs`. The job just left is shown step by step over its
  sound through `Screens/Shared/JobShowPlayer`, as the garage's workbench shows its own jobs.
- **Back home**: `MachineWorkTask` brings what is ready each morning (`Deliver`): on the stand if it came from there and
  the stand is free, else on the shelf; the garage tells of it with the mail order's deliveries. The parts view's shelf
  says what is away and when it is back, and a part's card what has been cut on it.

## Building the room

```
python tools/MachineShopScene/prepare_machine_shop.py <prepared folder>     # Poly Haven CC0 textures, the sign, the enamel
dotnet run --project tools/MachineShopScene/MachineShopSceneBuilder -c Release -- <prepared folder> "Street Corsa/Content/streetcorsa/shopscenes/machine_shop"
```

## Facts worth keeping

- Off-screen harnesses (session scratchpad, not kept): a console app on `GarageRenderer` + `ShopStockModel.Build` +
  `Shot` renders the room with stock (set `PartGroups.Current` first, or every part is an accessory and the
  group places stay empty); a WPF one on the real `App` renders the counter (set `Application._resourceAssembly`
  before `new App()`, and junction `Content` next to the exe).
- libsndfile crashes writing a long OGG in one call: `tools/RebuildSounds/make_sounds.py` writes in blocks.
