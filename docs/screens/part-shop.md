# Part shop

`Screens/PartShop/` (`PartShopScreenView`, `PartShopScreenViewModel`), reached from the part shops' map. Built in step 28
(idea 7, 2026-10-04): the black screen with a list became the inside of the shop.

## What the player sees

- **The room** (`Controls/ShopViewport3D`): the part shops' scene (`shopscenes`, `Services/PartShops/ShopScenes.Current`),
  dressed for the shop's kind (`ShopScene.LookFor`, `Dress`: the pictures on its surfaces, its sign, the tubes' colour,
  the windows' glow at the hour). The camera stands inside the door and sways slowly until the player drags (looks round,
  held to `MaxSwing` either way) or wheels in; it never leaves the room.
- **The stock in the room** (`ShopStockModel`, `ShopStockLayout`): a part of each line on the shelves, or every part in a
  junkyard's yard with what is bolted on it, merged into one model (`PartAssembler.BuildModel`) and given to the renderer
  as its scenery, off the UI thread. Each part goes in the first free place of its kind that fits it, square or turned a
  quarter: an engine on the stand, tyres and rims in the rack, the biggest parts on the table and the bottom shelves.
  A shop with no engine puts its biggest part on the stand. Bought, the stock is stood again.
- **The counter** ("Up to the counter", back button closes it first): one window over the room, the clerk's talk on the
  left (while the AI is on), the shop's list on the right as before (shelves or yard, sell, filters, haggle at a
  junkyard), with a price limit chip the clerk can set. Without a scene installed the counter is the screen.
- **The clerk** (see `docs/ai-integration.md`, "The part shop's clerk"): greets the player as they walk in (the button's
  dot calls while the counter is shut); asked for something, the list filters and their picks come first, marked. The
  picks and the dyno runs per ask are the rules' `partShops.mostPicks` and `mostDynoRuns`.

## What the two counters share

The part shop and the machine shop (`docs/screens/machine-shop.md`) are one room and one counter in two screens:
- `ShopRoomViewModel` (the screen's `Room`): the scene for the shop's kind dressed for the hour, what stands in it
  (`StockShown`), the parts catalog loaded on a worker (`OpenAsync`, which also syncs the roster), the counter open or
  not over the room (`IsCounterOpen`; without a scene it always is). The views bind `Room.SceneKn5`, `Room.Bounds`,
  `Room.IsCounterOpen` and the rest.
- `ClerkTalk` (the screen's `Clerk`): whoever is behind the counter while the AI is on: started once as the player
  walks in, let go when the AI never answers, told of the stock changing, ended on Exit. The views bind `Clerk.Talk`.
- `PartShopNotice` (`Services/PartShops`): the lines under the shop's name (the sale, the regular's discount) and the
  same as facts for the clerk.
- The rows of both counters are `Styles/Inputs.xaml`'s `CounterRowStyle`/`PickedRowStyle`.

## Building the room

```
python tools/ShopScene/prepare_shop.py <prepared folder>        # Poly Haven CC0 textures, the signs, pegboard, checker floor
dotnet run --project tools/ShopScene/ShopSceneBuilder -c Release -- <prepared folder> "Street Corsa/Content/streetcorsa/shopscenes/shop"
```

12 m across, 12 m deep, 3.6 m high: the counter across the middle with the till, shelving along the back wall under the
sign (four units, three places a level, five levels), the tyre rack and pegboard down the left wall, a gondola down the
right, the engine stand and the display table on the floor, nine fluorescent tubes. 103 places. Four looks: `speed_shop`
(checker floor, painted brick, red sign), `parts_counter` (specialist), `tire_shop` (wheels), `yard_shed` (junkyard).

## Facts worth keeping

- The renderer's own lens (32 degrees high) shows only the counter from inside the door: the scene sets `fov` 50.
- About 70 parts stand in 0.15-0.3 s; two of the base packs' textures are damaged (a slick tyre, a battery) and the
  renderer logs them.
- Harnesses (session scratchpad, not kept): `shopshot` renders the scene with random stock offscreen; `shopview` runs
  the real view with the real services and App resources (the harness assembly is named `StreetCorsa.Tests` for the
  game's internals), a canned AI or the live Ollama model (`SC_LIVE=streetcorsa`). Call `Enter` inside the running
  dispatcher, or its awaits come back off the UI thread.
