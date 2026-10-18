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
The game, the tests and the tools reference AcTools (`AcTools.dll`, `AcTools.Render.dll`) and SlimDX, Content Manager's
libraries from [gro-ove/actools](https://github.com/gro-ove/actools), not from a package. Their Release x64 DLLs are
kept in `lib\actools` (with their licences, Ms-PL and MIT), so a clone builds as it is; every project finds them
through `$(AcToolsLib)` in `Directory.Build.props`. They were built from actools
`effa0e131a44ced7bbce24d8953cfc7db09faef7` (2025-10-02, "Update SimuCube.cs"), unmodified; `lib\actools\BUILD.md` has the
commit, the build line and a SHA-256 of each file, to check what is there against. To build them again:

```
git clone https://github.com/gro-ove/actools ..\actools
git -C ..\actools checkout effa0e131a44ced7bbce24d8953cfc7db09faef7
MSBuild ..\actools\AcTools.Render\AcTools.Render.csproj -p:Configuration=Release -p:Platform=x64
```

(MSBuild from Visual Studio, with the .NET Framework 4.5.2 targeting pack.) Then copy `AcTools.dll` and
`AcTools.dll.config` from `AcTools\bin\x64\Release`, `AcTools.Render.dll`, `AcTools.ExtraKn5Utils.dll`,
`K4os.Compression.LZ4.dll`, `NBidi.dll`, `Nvidia.TextureTools.Compress.dll`, `StringBasedFilter.dll` and
`xxHashSharp.dll` from `AcTools.Render\bin\x64\Release`, and `SlimDX.dll` from `Libraries\SlimDX-x64` into `lib\actools`.
Not the Debug build: its `CarSlot.SetCarAsync` shows a raw exception MessageBox for any car that fails to load, and it
is unoptimized.

At run time SlimDX needs the Visual C++ 2015-2022 runtime (x64) and the DirectX End-User Runtime (June 2010, for
`d3dx11_43.dll`); the installer checks for both (`installer\README.md`).

## Non-Goals
- No AC binary patching
- No hot-reloading physics
- No open-world AI traffic
- No online multiplayer (initially)

## Files
- `App.xaml.cs` - Service composition, startup
- `MainWindow.xaml` - Single window host
- `Navigation/NavigationService.cs` - Screen management
