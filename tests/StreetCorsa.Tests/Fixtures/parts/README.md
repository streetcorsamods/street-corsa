# The tests' engine parts

The Chrysler, Ford V8 and GM engine packs left the base game on 2026-10-20, and the Ford six after them the same day:
they are mods the player installs (`chrysler_v8`, `ford_v8`, `gm_v8`, `ford_six`, kept outside the repository). The
flathead mod's parts are here too, for its speed parts' tests (`FlatheadSpeedPartsTests`, which travelled in the mod
until 2026-10-21); the radiator it copied from `gm_v8` is the `gm` one here. The
base game has no engine of its own, and the tests of machining, rebuilds, the engine stand, the cooling and the tuner
want engines to work on (a V8, and the six with its side distributor), and the golden dyno figures are of rated
builds of both.

So this folder holds the three mods' parts as **data only**: each part's file as it is in its mod, with its `model`
taken out (the models are the mods', never the repository's, and a part without a model loads as one without usable
geometry), plus their `engine_builds.json`. The mods hold their builds in their `mod.json` (`builds`) and carry no aliases
since 2026-10-20; their slots mount by fittings (`fits`/`takes`), as here.

`RepoPaths.PartsRoots` (TestSupport.cs) reads it as the base game's own, after the base parts folder, so the ids go
without a namespace as they did when the packs were the base game's (`engines/chrysler/chrysler_340_v8_block`,
`rated/chrysler-la-340-275-hp`) and the tests read unchanged. `PartRolesSnapshotTests` reads the base game alone.

Made by a script over the mods (2026-10-20, written again by the conversion to fittings and to a file per part the same
day); when a mod's parts change in a way a test should see, copy them here again the same way: each part file to
`engines/<chrysler|ford|gm|ford_six|flathead>/` with the `model` field out, the mod.json's `builds` to `engine_builds.json`.
