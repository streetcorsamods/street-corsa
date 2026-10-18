# The installer

`StreetCorsa-<version>.msi`: the usual Windows wizard, for one player, no admin needed. WiX Toolset 6.0.2, restored
from NuGet by the build (no install of WiX needed; to open the project in Visual Studio, the HeatWave extension).

## Build it

```
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

It writes `artifacts\installer\StreetCorsa-<version>.msi` (about 600 MB), the version being `<Version>` in
`Street Corsa\Street Corsa.csproj` (raise it for every release: a newer MSI replaces the older one, the same version
replaces itself). What it does:

1. `dotnet publish` the game self-contained for win-x64, English only: the player needs no .NET.
2. Stages it in `artifacts\installer\stage` with **only the content Git has** (`git ls-files "Street Corsa/Content"`).
   `Content\` on disk also holds what `.gitignore` keeps local: the garage's model (Rob Taylor's, no licence), the gas
   station's model (cut from the player's AC track), the private mods (`vfsradio`, `truckparts`) and the intro music.
   None of it may ship. It stops if an engine sound is still a Git LFS pointer (`git lfs pull`).
3. Builds `StreetCorsa.Installer.wixproj` over the stage.

## What the player sees

Welcome, licence (`license.txt`, turned into `license.rtf` by `make_license_rtf.py`), install folder
(`%LocalAppData%\Programs\Street Corsa`, so mods can be dropped into its `Content` folder without admin), then the
game's three pages (`Dialogs.wxs`):

- **Assetto Corsa**: the folder, found through Steam (`Actions\CustomActions.cs`, the same search as the game's
  `AcInstallFinder`), or the one picked last time; it must hold `content\cars` and `content\tracks`, or the player
  ticks "I will set it later". It goes to `HKCU\Software\Street Corsa\AssettoCorsaPath`, where the game looks when its
  settings name no folder.
- **Before you race**: found or missing, with a link: Custom Shaders Patch, the DirectX June 2010 runtime and the
  Visual C++ 2015-2022 runtime (SlimDX needs both). The game installs either way.
- **Last touches**: a desktop shortcut, and the optional AI talk: what Ollama is for, how to set it up, and a tick to
  open Ollama's download page at the end (shown when Ollama is not installed).

The last page can start the game. A quiet install: `msiexec /i StreetCorsa-0.1.0.msi /qn ACFOLDER="D:\Steam\steamapps\common\assettocorsa\"`.

## Uninstall

Before the files go, the installer runs `"Street Corsa.exe" --uninstall` (`AcUninstall`): what a race left changed in
AC goes back (as at every start), and the game's CSP modes (`sc_race`, `sc_free`, the mods' formats) leave
`extension\lua\new-modes`. Not while AC runs, and not on an upgrade. The saves, settings and log stay in
`%AppData%\StreetCorsa`.

## Not shipped, and what the game does without it

- The garage's model: the garage is AC's own showroom (`AppSettings.FallbackShowroomId`), unless the player puts Rob
  Taylor's garage showroom in AC's `content\showroom\garage`, where the game finds it.
- The gas station's model: the station works without its picture (station.json says how to cut it with
  `tools\StationScene`).
- The intro music: optional (music.json).

## Pictures

`banner.bmp` and `dialog.bmp` come from the main screen background (`make_art.py`, Pillow). Run it again when the
background changes; the bitmaps are committed, so a build needs no Python.

## Still to do

- Signing: unsigned, Windows SmartScreen warns on first run. Azure Trusted Signing or a code-signing certificate, then
  `signtool` over the MSI in `build.ps1`.
- A run on a clean Windows (a VM): the runtimes check, and the game on a PC that never had the developer's tools.
