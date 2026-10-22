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

1. Writes `license.rtf`, the licence page, from `license.txt` (edit the text, never the RTF).
2. `dotnet publish` the game self-contained for win-x64, English only, without its `Content` folder
   (`-p:CopyContent=false`): the player needs no .NET.
3. Stages it in `artifacts\installer\stage` with **only the content Git has** (`git ls-files "Street Corsa/Content"`),
   the main theme among it (Lame Genie's cover, with the band's permission, 2026-10-22; credited in sfx/credits.csv).
   `Content\` on disk also holds what `.gitignore` keeps local: the old garage showroom (Rob Taylor's, no licence), the
   old gas station cut (from the player's AC track), and every mod folder but the base game's and
   the mods of ours `.gitignore` names (`bushradio`): the developer's engine mods and engine sound pack, `vfsradio`,
   `truckparts`. None of it may ship. It stops if an engine sound bank (`*.bank`) is tracked: none is ours to ship.
4. Builds `StreetCorsa.Installer.wixproj` over the stage, and copies the MSI from where MSBuild says it put it.

## What the player sees

On anything older than Windows 10 (where .NET 10 does not run, nor does today's Steam) the installer stops at once:
"Street Corsa needs 64-bit Windows 10 or 11". It reads `CurrentMajorVersionNumber` from the registry, since Windows
Installer's own `VersionNT` says 8.1 on Windows 10. On 32-bit Windows the x64 MSI does not install at all.

Welcome, licence (`license.txt`, as `license.rtf`), install folder (`%LocalAppData%\Programs\Street Corsa`, so mods
can be dropped into its `Content` folder without admin; one on a share, a removable drive or a drive's root is
refused, as WixUI_InstallDir does), then the game's three pages (`Dialogs.wxs`):

- **Assetto Corsa**: the folder the game already runs on (its `settings.json`), else the one picked last time, else
  Steam's (`Actions\CustomActions.cs`, the same search as the game's `AcInstallFinder`), each only when it holds AC;
  it must hold `content\cars` and `content\tracks`, or the player ticks "I will set it later". It goes to
  `HKCU\Software\Street Corsa\AssettoCorsaPath` (not when put off). The game takes it at its next start when it is
  new to it (`GameSettings.InstallerAcFolder`), over the folder its settings had: picking another folder on a
  reinstall moves the game.
- **Before you race**: found or missing, with a link: Custom Shaders Patch, the DirectX June 2010 runtime and the
  Visual C++ 2015-2022 runtime (SlimDX needs both). The game installs either way.
- **Last touches**: a desktop shortcut, and the optional AI talk: what Ollama is for, how to set it up, and a tick to
  open Ollama's download page at the end (shown when Ollama is not installed).

The last page can start the game. A quiet install: `msiexec /i StreetCorsa-0.1.0.msi /qn ACFOLDER="D:\Steam\steamapps\common\assettocorsa\"`.

## Uninstall

Before the files go, the installer runs `"Street Corsa.exe" --uninstall` (`AcUninstall`, through the `RestoreAc` custom
action, which the progress bar names): what a race left changed in AC goes back (as at every start), and the game's CSP
modes (`sc_race`, `sc_free`, the mods' formats) leave `extension\lua\new-modes`; a mod's format mode carries a
`streetcorsa.mode` file, so it goes even when the mod was removed before. It runs before the registry values go, so the
game still finds the AC folder picked here. The settings are read without being written. A running AC is waited for up
to half a minute; if it is still running with a race's changes in it, nothing is put back and the game exits with code 3
(`AcUninstall.ExitAcRunning`), on which the wizard says so and offers Retry (close AC and run the clean-up again), Ignore
(go on: the originals stay in `%AppData%\StreetCorsa\AcRestore`) and Abort (keep the game installed); a quiet uninstall
goes on as Ignore does. Still running with nothing waiting, the modes go anyway. Any other failure of the clean-up (exit
code 1) is logged and the uninstall goes on. Not on an upgrade. The saves, settings and log stay in `%AppData%\StreetCorsa`.

## Not shipped, and what the game does without it

- Rob Taylor's old garage showroom and the old station cut: neither is needed, the game's own garage
  (`garages/workshop`) and gas station (`stations/crossroads`) ship.
- Engine sound banks: none ships (they are Kunos' and mod authors'). The game harvests the banks of the cars installed
  in AC and matches one to each engine, so every car races with a sound; a sound pack dropped into `Content` adds to
  them (see `Content\streetcorsa\sounds\README.md`).
- The engine mods (`l79`, `flathead`, `chevy_six`, `sbc350`): the base game's own engines serve every car; a car whose
  factory engine a mod's `cars.json` names gets it only with the mod installed.

## Pictures

`banner.bmp` and `dialog.bmp` carry the game's icon and its title lettered as the intro letters it, over the main screen
background darkened (`make_art.py`, Pillow, Windows' Impact font). Run it again when any of them changes; the bitmaps are
committed, so a build needs no Python.

## Checked in CI, and by hand on a clean Windows

`.github/workflows/installer.yml` builds the MSI on every push to main, every PR that touches the installer, a `v*`
tag (which drafts a release with the MSI) and by hand; it fails when the committed `license.rtf` is not the one
`license.txt` makes, then `smoke.ps1` installs and uninstalls the MSI quietly on the fresh runner and checks what it
put down and that nothing is left. Not on a PC the game is set up on: the uninstall puts that PC's AC back.

The wizard and the game starting on a machine that never had them are tried by hand in Windows Sandbox (Windows Pro
and up: Turn Windows features on or off > Windows Sandbox, then a restart): `installer\sandbox.ps1` writes
`artifacts\installer\sandbox.wsb` and opens it, with the MSI on the sandbox's desktop (`Desktop\installer`) and this
PC's Assetto Corsa read-only as `C:\AC` (from the game's settings.json, or `-AcFolder`; `-NoAc` for none, to see the
wizard without AC). Read-only, a race cannot run there and nothing can change the real AC. The GPU is shared, so the
3D runs.

## Still to do

- Signing: unsigned, Windows SmartScreen warns on first run. Azure Trusted Signing or a code-signing certificate, then
  `signtool` over the MSI in `build.ps1`.
- The wizard and the game walked through in the sandbox (above): the runtimes check, and the game on a PC that never
  had the developer's tools.
- The uninstall's Retry / Ignore / Abort box when Assetto Corsa is left running with a race in it: written against the
  MSI rules, not yet seen on a real uninstall.
- Vortice.Direct3D9 3.8.3 pulls SharpGen.Runtime and SharpGen.Runtime.COM 2.4.2-beta into the MSI: a prerelease
  runtime ships with the release. SharpGen.Runtime has no stable 2.x on NuGet (2.3.0-beta through 2.4.2-beta only), so
  there is nothing to pin; it leaves with Vortice, or when Vortice moves to a stable SharpGen.
- The actions project (`Actions\InstallerActions.csproj`) is compiled by `tools\check.ps1`; the wixproj only here.
