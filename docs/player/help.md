---
title: Help
description: Common problems and how to fix them.
weight: 6
---

# Help

| Problem | Fix |
|---|---|
| The game asks for the **Assetto Corsa folder** | Pick the folder `AssettoCorsa.exe` is in. To change it later: **Settings → Assetto Corsa folder**. |
| **"Runtimes Missing"** or no 3D | Install the [Visual C++](https://aka.ms/vs/17/release/vc_redist.x64.exe) and [DirectX](https://www.microsoft.com/download/details.aspx?id=8109) runtimes, restart. |
| Race won't start: **Custom Shaders Patch is not installed** | Install [CSP](https://acstuff.club/patch/). |
| **"Race Never Started"** | The race mode failed to load in AC. Nothing was lost. Update CSP; `Documents\Assetto Corsa\logs\custom_shaders_patch.log` says why. |
| Race won't start: **races need 0.2.11 or newer** | Update CSP in Content Manager (Settings → Custom Shaders Patch). |
| **"Assetto Corsa is already running"** | Close Assetto Corsa (and Content Manager's game), try again. |
| **"Mod Problems"** | A mod is broken or too old. The rest of the game works; update or remove that mod. |
| Car won't race | Open **Repairs**: it says why (no fuel, broken engine, missing wheel...). |
| No police ever | Install a police car in Assetto Corsa. |

## Where things are

- Saves, settings, log: `%AppData%\StreetCorsa`
- Mods: the `Content` folder in the game's install folder
- Radio music: `Documents\Street Corsa\Radio` (mp3, ogg, flac). Press **F2** for the radio.

Still stuck? Send the log (`%AppData%\StreetCorsa\Logs`) with your report.
