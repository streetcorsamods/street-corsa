---
title: Install
description: What you need and how to set up Street Corsa.
weight: 1
---

# Install

## You need

- Windows 10 or 11, 64-bit
- **Assetto Corsa**, with some cars installed (the game uses your cars; none come with it)
- **Custom Shaders Patch 0.2.11 or newer** for the races ([get it here](https://acstuff.club/patch/), or through Content Manager)
- For the 3D garage: [Visual C++ runtime (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe) and
  [DirectX runtime](https://www.microsoft.com/download/details.aspx?id=8109)

The installer tells you which of these it found.

## Setup

1. Run `StreetCorsa-<version>.msi`. No admin rights needed.
2. Point it at your Assetto Corsa folder (the one with `AssettoCorsa.exe`).
3. Done. Start the game from the desktop or the Start menu.

Windows may warn that the installer is unknown. Click **More info → Run anyway**.

## A setting

The installer is the core. The career you play is a setting, a download of its own:

1. Download a setting's zip (`StreetCorsa-<setting>-<version>.zip`).
2. On the main screen, click **Install mods** (top right) and pick the zip. Several at once work too.
3. **New Game** shows it.

Engine packs and add-ons install the same way. A newer zip of an installed mod replaces it (the game asks first).

By hand: **New Game → Open the Content folder** and unzip it there.

## Optional

- **People who talk back.** Install [Ollama](https://ollama.com), run `ollama pull llama3.1:8b`, then in the game's
  **Settings → AI** pick Ollama and press **Make the game's model**. Works best on a graphics card with 12 GB.
- **Police.** Cops only show up if a police car is installed in Assetto Corsa: a setting says which.
- **Mods.** Drop a mod's folder into the `Content` folder of the game's install folder
  (`%LocalAppData%\Programs\Street Corsa\Content`). Settings (the career you play) and engine packs come as mods.

## Uninstall

Remove it from Windows' **Apps** list. Your saves stay in `%AppData%\StreetCorsa`.
