# Street Corsa

Street Rod-style career mode manager for Assetto Corsa.

## Overview
External WPF application that manages game logic, economy, and progression. Assetto Corsa serves as the physics/rendering engine only.

This repository is the core alone (`Content/streetcorsa`: the screens and how they hang together, the rules, the
races, the words, the menus' sounds). It has no town of its own: a **setting**, a mod, makes it a career (its cars,
parts, dealers, racers, places and their screens), picked at New Game; settings, engine packs and other add-ons are
mods dropped into the `Content` folder and kept outside this repository (see [docs/modding.md](docs/modding.md)). No
Assetto Corsa car, no engine sound bank and no third-party mod is included; the game runs on the cars installed in
Assetto Corsa and harvests their engine sounds.

## Download
The installer is on the [releases page](https://github.com/streetcorsamods/street-corsa/releases). You need 64-bit
Windows 10 or 11, Assetto Corsa (Steam) with some cars installed, and Custom Shaders Patch 0.2.11 or newer; the
[install guide](docs/player/install.md) has the rest. The installer is the core: the career you play is a setting, a
zip of its own from the same page, installed with the main screen's Install mods. The game installs for your Windows user
only (no admin) and uninstalls from Windows' Installed apps. The releases are to be signed through SignPath
Foundation's free code signing for open source (see the [code signing policy](#code-signing-policy)); until then
they are unsigned, and Windows may warn that the installer is unknown.

## Tech Stack
- .NET 10 / WPF / MVVM
- LiteDB for persistence
- CSP race mode (`apps/new-modes/sc_race`) for in-race judging, telemetry and results

## Documentation
Players: the [player guide](docs/player/README.md).

See [docs/CLAUDE.md](docs/CLAUDE.md) for AI context and architecture overview.

## Code signing policy
Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

- Committers and reviewers: [streetcorsamods](https://github.com/streetcorsamods) (Pablo Camardelli)
- Approvers: [streetcorsamods](https://github.com/streetcorsamods) (Pablo Camardelli)

Every signed release is built by GitHub Actions from this repository's source
([installer.yml](.github/workflows/installer.yml)), and each signing is approved by hand. Only Street Corsa's own
files are signed: the game's `Street Corsa.exe` and `Street Corsa.dll`, and the MSI itself (the installer's custom
actions, `InstallerActions.dll`, run only from inside it). The libraries made by others that ship with it (the .NET
runtime, AcTools, SlimDX and the rest, `lib/actools`) keep their makers' signatures, or none. Signing is being set up:
until it is in the build, the releases are unsigned.

## Privacy
Street Corsa sends nothing over the network unless you ask it to: no telemetry, no update check, no account. The one
exception is the optional AI writer (Settings, off by default). Once you turn it on, the game sends the provider you
pick what it needs to write the newspaper's and the people's lines: everything you type to the people you talk to,
your offers when you haggle, and your career as the talk needs it (the cars, racers and places it is about, your
money, your cars and their parts, prices, and what was said in earlier talks; Settings' talk detail sends less).
That provider is Ollama or ComfyUI on your own computer or network, or a cloud service you sign up to yourself (an
OpenAI-compatible one or Anthropic), under that service's own privacy policy and with your own key: with a cloud one,
what you type leaves your computer. The installer's links open in your browser only when you click them.

## License
The code is MIT (LICENSE.txt). The game's own content, the models, pictures, sounds, text and part data made for it,
is CC BY 4.0 (CONTENT-LICENSE.txt): use it, change it, build on it, and name Street Corsa. Some of the content is
other people's work under its own terms, each named beside the files it is in: the garage's props, the gas station's
and the street's buildings and cars, the running gear's models and the radio come from Sketchfab under CC BY 4.0
(`CREDITS.txt` files and the `credit` fields of their JSON, shown in the game's Settings), the plants and skies are
Poly Haven's and the sound effects are freesound.org's and Kenney's under CC0 (`sfx/credits.csv`), the icons are
from www.freeicons.io, game-icons.net and the Noun Project under CC BY 3.0, a few public domain from freesvg.org,
each credited to its author (`Assets/Images/Icons/credits.csv`, installed with the game), and
AcTools and SlimDX are under Ms-PL and MIT (`lib/actools`). Nothing of Kunos' and no engine sound bank ships.

The main theme, `Street Corsa/Content/streetcorsa/sfx/music/intro.mp3`, is under no open licence: it is Lame Genie
VGM's cover of the Street Rod 2 intro, which the band let Street Corsa use (2026-10-22). It may not be reused apart
from the game without their permission, and a fork that ships under a name of its own must take it out (drop the
file and the `music.intro` cue in `sfx/music.json`, or put music of its own in its place).

## Trademarks
The name **Street Corsa** and the game's logo belong to Pablo Camardelli and are not covered by the licences above.
Fork the game, change it, ship it, under the MIT and CC BY terms (without the main theme, see above); but a modified
version is not Street Corsa: give it a name of its own, and say it is based on Street Corsa, unless we have agreed otherwise. Mods for the game may
say they are for Street Corsa.

Street Corsa is an independent project, not made, endorsed or sponsored by Kunos Simulazioni or Assetto Corsa's
publishers; Assetto Corsa is their trademark, named only to say what the game runs on.

Makes, models and parts makers named in the game (Chevrolet, Ford, Chrysler, Edelbrock, Holley and the rest) are
trademarks of their owners, used only to say what a car or a part is. Street Corsa is not affiliated with, endorsed
or sponsored by any of them.
