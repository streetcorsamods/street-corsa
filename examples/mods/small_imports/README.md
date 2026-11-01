# Small Imports: the example mod

An add-on made of data alone, to copy when you make your own. It brings four small European cars into Street Corsa
with engines made of figures alone, their speed parts, a shop that sells those parts, a lot that sells
the cars, a rival who drives them, and a race night of their own. Every file is read by the game's own loaders; the
modding guide (`docs/modding.md`) explains each kind of content in full.

## Trying it

1. Copy this folder into the game's `Content` folder, next to `streetcorsa`: `Content\small_imports`.
2. The cars are Kunos cars that come with every Assetto Corsa: `ks_abarth_595ss`, `ks_alfa_romeo_gta`,
   `ks_ford_escort_mk1` and `abarth500`. They have to be in the `content\cars` folder of the Assetto Corsa the game
   races in (copy them over if that is a separate install).
3. Start the game. The mods it found are in the log (`Content: streetcorsa 0.1.0, small_imports 1.0`), and anything
   it could not read is shown at start-up.

A game already under way gets the new rival, the lot and the shop as it goes on; a new game has them from the start.

## What is in it

| File | What it shows |
|---|---|
| `mod.json` | The mod's id (`small_imports`: every id below gets it as its namespace), version, `gameVersion` |
| `cars/cars.json` | Makes merged over the setting's (Abarth in Fiat's family, Ford of Britain apart from the American Ford) and each car's factory engine, layout (`rear`, `front`, `front_transverse`), price and make |
| `parts/*/*.json` | Four engines made of figures alone, a file per part (`"figures"`: bore, stroke, compression, breathing, cam, fuel, gears, a turbo), each part with a `class` so it lands on its shelf, the `needs` the engine can't run without, and the speed parts that go on the same fittings |
| `parts/engine_builds.json` | Each car's factory engine (`origin: factory`, with the power the real one made) and a speed-shop build of each (`aftermarket`) |
| `sounds/sounds.json` | Each engine pinned to the sound of the car it came in (`car:<car id>`) |
| `partshops/partshops.json` | Euro Speed & Sport, a specialist in the parts' makes |
| `dealers/dealers.json` | Continental Imports, a lot that deals in European cars only (`"cars"`: a car filter) |
| `racers/racers.json` | Gino Ferraro, who drives European cars (`"cars"`) |
| `formats/formats.json` | An autocross: the race mode's time attack over two laps, a format of the mod's own made of data |
| `events/events.json` | Import Night, a weekly autocross for European cars worth up to $6,000, a Cosworth cam for the winner |
| `text/imports.json` | The autocross's words, and Gino's own lines (a pool's key with his id after it: `talk.pulled_up.gino`) |

## The engines

| Car | Engine | Factory | Speed-shop build |
|---|---|---|---|
| Abarth 595 SS | 594 cc air-cooled twin, rear | 30 hp | 695 big-bore kit, sport cam, Dell'Orto: 39 hp |
| Alfa Romeo GTA | 1570 twin cam, two Weber 45s | 107 hp | Autodelta twin-plug head, Corsa cams, stacks, header: 143 hp |
| Ford Escort RS1600 | Cosworth BDA 16-valve, two Weber 40s | 117 hp | 1800 big-bore, BD3 cams, Weber 45s, header, ZF 5-speed: 175 hp |
| Abarth 500 esseesse | 1.4 T-Jet across the front, IHI turbo | 157 hp | Garrett turbo, injectors, Record Monza exhaust: 202 hp |

The game's own dyno makes these from the figures, as it does for every engine. To see an engine's curve, or check a
change you made, put the packs on the bench:

```
tools\EngineBench\bin\Debug\net10.0\EngineBench.exe examples\mods\small_imports\parts rated
tools\EngineBench\bin\Debug\net10.0\EngineBench.exe examples\mods\small_imports\parts show kits/tjet_garrett
```

## Making your own from it

- Give it your own `id` in `mod.json`; the folder's name can be anything.
- Part ids are yours to choose; keep them under a folder of your own (`engines/<engine>/<part>`). A fitting
  (`"takes"` on the block's slot, `"fits"` on the part) is how parts find each other: name them for your engine
  (`fiat_tjet:turbo`) so they never go on another mod's engine by mistake.
- A figure a part does not give comes from the parts under it or the engine's base; `{ "times": 1.15 }` makes a
  bolt-on work on whatever engine it goes on.
- Cars that are not installed are left out quietly; a car of yours needs its factory engine in `cars.json`, or the
  game guesses one of its own makes' engines.
- `tests/StreetCorsa.Tests/ExampleModTests.cs` loads this mod over the core the way the game does; a test like it
  is a quick way to see that your own mod reads.
