# The tests' V8 parts

The Chrysler, Ford V8 and GM engine packs left the base game on 2026-10-20: they are mods the player installs
(`chrysler_v8`, `ford_v8`, `gm_v8`, kept outside the repository). The base game's one engine pack is the Ford six,
and a six has one head, no steering pump belt and no electric fan: the tests of machining, rebuilds, the engine stand,
the cooling and the tuner want a V8 to work on, and the golden dyno figures are of the rated V8 builds.

So this folder holds the three packs' **data only**: each `pack.json` as it is in its mod, with every part's `model`
taken out (the models are the mods', never the repository's, and a part without a model loads as one without usable
geometry), plus their `engine_builds.json`. The mods hold all of it in their `mod.json` (`packs`, `builds`) and carry no aliases
since 2026-10-20; their slots mount by fittings (`fits`/`takes`), as here.

`RepoPaths.PartsRoots` (TestSupport.cs) reads it as the base game's own, after the base parts folder, so the ids go
without a namespace as they did when the packs were the base game's (`engines/chrysler/chrysler_340_v8_block`,
`rated/chrysler-la-340-275-hp`) and the tests read unchanged. `PartRolesSnapshotTests` reads the base game alone.

Made by a script over the mods (2026-10-20, written again by the conversion to fittings the same day); when a mod's
pack changes in a way a test should see, copy its packs here again the same way: each of the mod.json's `packs` to
`engines/<pack>/pack.json` with the `model` fields out, its `builds` to `engine_builds.json`.
