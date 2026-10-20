# Street Corsa

Street Rod-style career mode manager for Assetto Corsa.

## Overview
External WPF application that manages game logic, economy, and progression. Assetto Corsa serves as the physics/rendering engine only.

What ships is the core game: the base game's content and the mods made for it here. No Assetto Corsa car, no engine
sound bank and no third-party mod is included; the game runs on the cars installed in Assetto Corsa and harvests their
engine sounds. The base game's one engine is a Ford six; the V8 engine packs, like everything else, come as mods
dropped into the `Content` folder (see [docs/modding.md](docs/modding.md)).

## Tech Stack
- .NET 10 / WPF / MVVM
- LiteDB for persistence
- CSP race mode (`apps/new-modes/sc_race`) for in-race judging, telemetry and results

## Documentation
See [docs/CLAUDE.md](docs/CLAUDE.md) for AI context and architecture overview.

## License
The code is MIT (LICENSE.txt). The game's own content, the models, pictures, sounds, text and part data made for it,
is CC BY 4.0 (CONTENT-LICENSE.txt): use it, change it, build on it, and name Street Corsa. Some of the content is
other people's work under its own terms, each named beside the files it is in: the garage's props, the gas station's
and the street's buildings and cars, the running gear's models and the radio come from Sketchfab under CC BY 4.0
(`CREDITS.txt` files and the `credit` fields of their JSON, shown in the game's Settings), the plants and skies are
Poly Haven's and the sound effects are freesound.org's and Kenney's under CC0 (`sfx/credits.csv`), the icons are
from www.freeicons.io under its free licence, credited to their authors (`Assets/Images/Icons/CREDITS.txt`), and
AcTools and SlimDX are under Ms-PL and MIT (`lib/actools`). Nothing of Kunos' and no engine sound bank ships.

## Trademarks
The name **Street Corsa** and the game's logo belong to Pablo Camardelli and are not covered by the licences above.
Fork the game, change it, ship it, under the MIT and CC BY terms; but a modified version is not Street Corsa: give
it a name of its own, and say it is based on Street Corsa, unless we have agreed otherwise. Mods for the game may
say they are for Street Corsa.

Makes, models and parts makers named in the game (Chevrolet, Ford, Chrysler, Edelbrock, Holley and the rest) are
trademarks of their owners, used only to say what a car or a part is. Street Corsa is not affiliated with, endorsed
or sponsored by any of them.
