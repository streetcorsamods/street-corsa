# Code review: PR #67, cars look used (2026-10-04)

Review of `feature/cars-look-used` into `dev` at **88a6cbb** (60 files, +3130/-84). Run with `/code-review max`. Ten finder angles, independent verifiers and a gap sweep.

**State:** the review is done, and every finding below was fixed in e45fcd8 (merged with PR #67 as 9c0c79f). The working tree was clean when the review ran.

**Checks:** the app, the tests and EngineBench all build with no warnings. The Lua harness suites pass.

**Evidence the review used:**
- the 49 kept shells in `%AppData%\StreetCorsa\Looks\shells`, parsed one by one
- the game log from 2026-10-04
- SlimDX checked by reflection: its texture-view classes have no finalizer

## Findings, worst first

All paths are under `Street Corsa/`.

### 1. A race can fail to launch: one car's Prune deletes the other car's layer (`Looks/WearLayer.cs:241`)
- **Cause:** `WriteFor` returns a layer that is already on disk without touching it, so the layer keeps its old write time. `Prune(cache, written)` keeps the newest `KeptFiles` by `LastWriteTimeUtc` and spares only the caller's own file. `RaceSetupBuilder` runs both cars' `ForRaceAsync` at once.
- **What happens:** the opponent paints a new layer, then its Prune deletes the player's layer, whose name is already in the race intent. `ScRaceMode.Install` → `Mirror` → `File.ReadAllBytes` throws FileNotFoundException, and the result is `launch.mode_not_installed`. A retry works.
- **Fix:** touch a reused layer (`File.SetLastWriteTimeUtc`), or prune once after both layers exist and spare both names.

### 2. GPU textures of the wear layer leak when a car leaves a scene (`Controls/ShownPartsOnCar.cs:75`)
- **Cause:** the layer's textures go into the per-car `on.Textures` providers. Only `Clear()` disposes those, and it runs only on a later `SetAsync` for the same car.
- **Leaking paths:** these dispose the car via AcTools' `CarWrapper.DisposeEverything` with no `SetAsync(car, null)`:
  - the lot's `ClearBayAsync` (`slot.SetCarAsync(null)`)
  - a garage car switch (`MainSlot.SetCarAsync`)
  - each new street rival
  - every scene close
- **Why nothing frees them:** `PartFile.Dispose` reaches only `Lent.Dispose() { }`. SlimDX views have no finalizer and sit in its static ObjectTable.
- **Size:** about 22 MB per garage visit, and about 5.6 MB per used car per lot page.
- **Background:** the Lent pattern predates this PR (rims, cbf8c1a).
- **Fix:** dispose the providers when a car leaves its slot or renderer, for example on the slot's car change or the viewport unload, or with a hook in the car wrapper's dispose.

### 3. Rivals' repairs now include cosmetics, paid all or nothing (`Parts/Cars/RepairShop.cs:138`)
- **Cause:** `RepairShop.Jobs` now returns rust/respray and windscreen jobs. `OpponentLifeService.TryRepair` sums the whole list: `if (jobs.Count == 0 || bill > racer.Money) return false;`.
- **Example:** a rival at condition 0.6 with a blown tyre:
  - the bill is $387 instead of $50, plus $75 for a cracked windscreen
  - a rival with $300 now sells his car or sits out
  - a rival who can pay has rust, paint wear and cracks reset to 0
- **Fix:** leave the cosmetic jobs out of rivals' repairs, or let rivals pay only the jobs that get the car racing.

### 4. The windscreen cracks with no hit (`Parts/Cars/CarLooks.cs:141`)
- **Cause:** `CrackFromHit` counts any rise in the front zone's running total as a hit, with no tolerance, and sizes the crack from the total.
- **Case (a), rounding:** rivals' simulated races store full-precision zones (`5 + random.NextDouble()*35`), e.g. 27.26. AC gets `BODY=27.3` ("0.0"), and the mode reports 27.3. `ApplyBody` keeps the larger value, so the windscreen cracks (~0.24) and the report says "the hit cracked the windscreen". This happens about half the time, on every such rival in existing saves.
- **Case (b), small touch:** a car with 60 km/h of front damage and a new windscreen re-cracks to ~0.82 after a 0.1 km/h touch.
- **Fix:** use the hit this race added, with the hit line's 0.5 km/h tolerance.

### 5. `ShellFile.MostIndices` is too low, so heavy models rebuild their shell every session (`Looks/ShellFile.cs:39`)
- **Cause:** the limit is `3 * 65536`, but `BodyShell.Build` splits a mesh only when `vertices.Count > 65000`, at 4-5 indices per vertex.
- **Affected:** 6 of the 49 kept shells exceed the limit:

  | Model | Indices |
  |---|---|
  | LM_CHEYENNE | 290,046 |
  | fb_dodge_phoenix | 328,446 |
  | ford_thunderbird_1962 | 306,321 |
  | chevrolet_impala_1967 | 202,848 |

- **What happens:** `TryRead` returns false, and the shell is rebuilt (1.6-3.1 s) and rewritten every session.
- **Fix:** derive the limit from the build's real bound, or check against the file length.

### 6. Kept shells are never invalidated, and failed builds are kept (`Looks/ShellFile.cs:14`)
- **Stale shells:** the windscreen pick changed (corner normals, seen glass), but the magic is still `SCSHELL2`, and the key is only the model's path, size and time. Every shell on disk predates 88a6cbb.
- **Measured on those old shells:**
  - ford_fairlane66: the new test rejects all 78 cached windscreen triangles
  - tmw_442_Hurst: it rejects 172 of 344
  - in 23 of 43 shells, over a quarter of the "windscreen" sits behind the car's middle, i.e. on the rear glass (atcc_ford_falcon_xl: 93%)
- **Failed builds:** `if (kept != null) ShellFile.Write(kept, made)` saves `made == null` after any exception in `BodyShell.Of`, so that model never shows wear again.
- **Fix:** version the shell with BodyShell (like `PainterVersion` for race layers), and never write a failed build.

### 7. The cabin, seen through the glass, gets rust and scratches (`Looks/BodyShell.cs:140`)
- **Cause:** in the "seen from outside" pass, glass hides nothing. Interior materials that the `NotPaint` words miss then pass the 20%-of-the-top-material rule.
- **Examples:**
  - ford_mustang66: leater_dark1 is the top material, and also supplies the layer's ksAmbient/ksDiffuse
  - ford_mustang_convertible_66, ford_galaxie_500 (upholstery), chevy_belair_1957 (_cpitmottled)
- **Fix:** let see-through materials hide what is behind them in the visibility pass. Optionally, add words such as leather, alcantara, upholstery and carpet.

### 8. Windscreens with generic material names are never drawn cracked (`Looks/BodyShell.cs:85`)
- **Cause:** the screen is found only by name (glass|window|windscreen|windshield|vetro) or by a ksWindscreen shader.
- **Affected:** 6 of 49 shells have no screen meshes:
  - 72_gran_torino_stock ("Material #53")
  - chevy_c10hotrod ("19 - Default")
  - dodge_char_70
  - ford_mustang_convertible_66 (wind_toning)
  - chevy_belair_nomad_56 (glmap)
  - lm_camaro_cabrio
- **What happens:** the cracks are priced, reported and offered for repair, but never drawn.
- **Fix:** fall back to `SeeThrough()` materials plus the normals/position test.

### 9. Any look change clears the car's wear until it is repainted (`Controls/ShownPartsOnCar.cs:103`)
- **Cause:** the look is part of `ShownSet`, so a changed set `Clear()`s the car at once. Rims, engine parts and wear come back only after several awaits.
- **What happens:**
  - after a windscreen-only repair, the remaining wear vanishes and pops back
  - closing the Parts view re-uploads the 22 MB DDS synchronously in Draw, mid-fade
- **Fix:** keep the wear node when the look and the picture size are unchanged. Swap a changed node only once the new layer is ready.

### 10. Ads and the AI seller never see the looks (`Services/Market/AdBlurbs.cs:161`)
- **Cause:** ad lines are picked from condition alone. `TalkFacts` (line ~138) passes only condition and mileage.
- **Example:** a rival's car at 0.9 with dirt, some rust and a cracked windscreen gets "Clean, straight, shiny." `CarValuation.LooksFactor` already prices those looks in.
- **Fix:** feed rust, paint, cracks and dirt into the ad picks and the fact sheet.

### 11. The shell cache always goes into the real AppData and is never pruned (`Looks/WearLayer.cs:134`)
- **Cause:** `ShellCache` is static, whatever folder `WriteFor` is given. `Prune` lists only top-level `*.kn5` files.
- **Size:** 49 files, 72 MB after one night.
- **Tests:** `CarLooksTests.A_race_writes_its_layer_with_PNG_pictures_once_for_a_look` writes `box_car_<hash>.shell` into the real AppData, and the hash includes the temp path, so each run adds a file.
- **Fix:** put the shell cache under the given folder, or make it injectable, and prune it by size or age.

### 12. A broken spring's sag is missing on the lot and in car viewing (`Controls/LotViewport3D.cs:713`)
- **Cause, lot:** `OnSelectionChanged` calls `StopScript(); ReleaseRock();` and never re-rocks, so the chosen car stands level until its engine starts.
- **Cause, listings:** `DealerLotScreenViewModel` and `CarViewingScreenViewModel` build `Shown(id, parts, CarLooks.Of(listing))` with no `CornerHeights`.
- **Fix:** re-apply the pose after release, and carry the listing's heights.

### 13. A part model that fails to read hides the wear layer (`Controls/ShownPartsOnCar.cs:116`)
- **Cause:** the catch `"A part's model could not be read..."; return true;` returns before `PutLookAsync`.
- **What happens:** in AC the same car still shows its wear, since car_parts.lua loads `CAR_n_LOOK` on its own.
- **Fix:** put the look on regardless of the part models.

### 14. The scratch pattern depends on the picture size (`Looks/WearPainter.cs:418`)
- **Cause:** all scratches share one `Random`. Each draws `steps = (int)(length*2)` values, with the length in pixels, so 1024 px (lot) and 2048 px (elsewhere) use up the stream differently.
- **What happens:** the scratches move between the lot and the garage, viewing and AC.
- **Fix:** give each scratch its own seed (seed + index), or count steps in panel units.

### 15. A listing from an older save changes its scrapes at purchase (`Parts/Cars/CarLooks.cs:86`)
- **Cause:** with `LookSeed == 0`, the lot uses `SeedOf(listing.Id)`, but `CarFrom` uses `SeedOf(car)` (the InstanceId).
- **Fix:** seed the car from `SeedOf(listing.Id)` when the listing has no seed.

## Cut by the 15-finding cap (cleanups, lower priority)

- **Misplaced doc comments:** `Controls/LotViewport3D.cs:517-524` has two stacked `<summary>` blocks on `WarmWear`. The "one at a time on purpose" text belongs to the method that loads the cars. The review saw the same in `Screens/Garage/GarageScreenView.xaml.cs` and `Controls/CarViewport3D.cs`; search there for doc comments left above the wrong member.
- **`Helpers/SafeFile.cs` not reused:** `Looks/WearLayer.cs:197` and `Looks/ShellFile.cs:99` write to a fixed `path + ".tmp"` with no flush. SafeFile uses unique temp names and retries the move.
- **WarmWear not cancelled:** `LotViewport3D.WarmWear` (lines 410, 539) warms every listed car, including cars the lot leaves off, and nothing stops the queue when the player leaves.
- **Memory:** `KeptBytes = 200 MB` of uncompressed layers (`WearLayer.cs:46`). `ShellsByFile` (`WearLayer.cs:58`) is never trimmed.
- **Duplicated code:**
  - the `CarLooks.CopyTo` pair (`CarLooks.cs:70/80`)
  - the warm-up guards repeated in each viewport
  - hand-written hashes (`CarLooks.cs:95`, `WearPainter.cs:361`, `WearLayer.Hash`)

## Suggested order for the fix session

1. **#1, #2:** a broken launch and a leak.
2. **#5, #6:** the cache is wrong or useless. Bump the shell version so stale shells drop.
3. **#4, #3:** game logic.
4. **#9, #13:** scene behaviour.
5. **#7, #8, #14, #15, #12:** visual correctness.
6. **#10, #11:** text and cache housekeeping, then the cleanups.

When the fixes are done:
- build, run the tests and `check.ps1`
- commit on `feature/cars-look-used` and push to `github`
- the user plays the result

**Repo notes:**
- Git Bash `sed -i` rewrites CRLF files to LF. Use the Edit tool or Python with `newline=''`.
- The remotes are named `github` and `gitlab`; there is no `origin`.
