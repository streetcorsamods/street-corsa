# Architecture Overview

## Project Goal
Street Rod-style career mode for Assetto Corsa. External WPF manager handles all game logic. AC is treated as physics/rendering engine only.

## Three-Layer Architecture

### 1. Meta-Game Layer (C#/WPF)
- Player progression, economy, car ownership
- Upgrade system, wear/damage persistence
- Save/load system
- All gameplay decisions

### 2. AC Integration Layer
- Launch AC via `acs.exe` (race, free run); cars are previewed in the game's own 3D views
- Generate race configurations via INI modification
- Read race results from the race mode's output (`apps/new-modes/sc_race`)
- Never modify AC binaries or core files

### 3. Data/Persistence Layer
- LiteDB databases
- Catalog (static content definitions)
- Saves (dynamic player state)

## Key Principles

| Principle | Description |
|-----------|-------------|
| AC is external | Treat AC as unreliable content provider |
| Intent-based config | Declare what, let service handle how |
| Minimal modification | Change only owned keys, always restore |
| Deterministic imports | Same input = same catalog output |
| Engine-agnostic models | Game data never stores AC-specific values |

## Tech Stack
- Language: C# / .NET 10
- UI: WPF (native, no WebView)
- Pattern: MVVM (strict separation)
- Storage: LiteDB
- Target: Windows x64

## Build Dependencies
The game and the parts converter reference AcTools (`AcTools.dll`, `AcTools.Render.dll`) and SlimDX from a sibling
checkout of [gro-ove/actools](https://github.com/gro-ove/actools), not from a package: `..\actools` next to this repo,
built **Release x64** (`AcTools\bin\x64\Release`, `AcTools.Render\bin\x64\Release`; SlimDX from `Libraries\SlimDX-x64`).
The DLLs are not versioned here, so a build is only reproducible with the same actools commit.
Known good: actools `effa0e131a44ced7bbce24d8953cfc7db09faef7` (2025-10-02, "Update SimuCube.cs"). To build it:

```
git clone https://github.com/gro-ove/actools ..\actools
git -C ..\actools checkout effa0e131a44ced7bbce24d8953cfc7db09faef7
MSBuild ..\actools\AcTools.Render\AcTools.Render.csproj -p:Configuration=Release -p:Platform=x64
```

(MSBuild from Visual Studio, with the .NET Framework 4.5.2 targeting pack.) Not the Debug build: its
`CarSlot.SetCarAsync` shows a raw exception MessageBox for any car that fails to load, and it is unoptimized.
A different commit may change the KN5 reader or the renderer under the game without a line of this repo changing;
check the commit (`git -C ..\actools rev-parse HEAD`) first when a build or a render differs between machines.

## Non-Goals
- No AC binary patching
- No hot-reloading physics
- No open-world AI traffic
- No online multiplayer (initially)

## Files
- `App.xaml.cs` - Service composition, startup
- `MainWindow.xaml` - Single window host
- `Navigation/NavigationService.cs` - Screen management
