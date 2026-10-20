# Street Corsa

Street Rod-style career mode manager for Assetto Corsa.

## Overview
External WPF application that manages game logic, economy, and progression. Assetto Corsa serves as the physics/rendering engine only.

What ships is the core game: the base game's content and the mods made for it here. No Assetto Corsa car, no engine
sound bank and no third-party mod is included; the game runs on the cars installed in Assetto Corsa and harvests their
engine sounds. More content comes as mods dropped into the `Content` folder (see [docs/modding.md](docs/modding.md)).

## Tech Stack
- .NET 10 / WPF / MVVM
- LiteDB for persistence
- CSP race mode (`apps/new-modes/sc_race`) for in-race judging, telemetry and results

## Documentation
See [docs/CLAUDE.md](docs/CLAUDE.md) for AI context and architecture overview.

## License
See LICENSE.txt

## Trademarks
Makes, models and parts makers named in the game (Chevrolet, Ford, Chrysler, Edelbrock, Holley and the rest) are
trademarks of their owners, used only to say what a car or a part is. Street Corsa is not affiliated with, endorsed
or sponsored by any of them.
