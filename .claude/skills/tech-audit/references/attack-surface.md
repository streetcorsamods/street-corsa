# Attack Surface

> Overwritten by each tech-audit run — a snapshot, not history. Snapshot of 2026-11-12 @ 5ebc1fb9, **diff mode**
> (window c51b24a7..HEAD, 39 commits, 161 changed files). Lists only surfaces that are new or changed in the window; the
> previous inventory is `git show 0f193dcb:.claude/skills/tech-audit/references/attack-surface.md` and still holds for
> everything not named here.

## Units

Unchanged set: Street Corsa (WPF), StreetCorsa.Tests, EngineBench, InstallerActions (net472), sc_race / sc_free (CSP
modes), scripts (`tools/check.ps1`, `installer/*.ps1`), Python helpers. No new projects.

## Mod import and Mod Manager (changed)

- `Mods/ModImport.cs`, `ModManager.cs`, `ModSet.cs`, `ModIds.cs`, `ManifestSections.cs` — a setting's download now
  holds its places (one zip, several mods; PRs #55/#58): entry names → Content paths, ids from `mod.json`.
- `Services/Storage/ModsArrival.cs` — mods found at load.

## Writes into the AC install / cfg (changed in window)

- `Services/Assetto Corsa/CarDataOverlay.cs`, `AcInstallStamp.cs`, `AcUninstall.cs`.
- `Services/Race/RaceCarDataService.cs`, `Parts/Export/AcCarBuild.cs`, `AcRunningGearData.cs`, `AcSetupData.cs` — figures
  that end up in the car's data folder (wheel/spring per corner, PR #49).

## Untrusted-input parsers (changed)

- Race result JSON: `Services/Race/RaceResultIngestionService.cs`, `RaceResultProcessor.Field.cs`, `RaceFormats.cs` ← written by
  `apps/new-modes/sc_race/*.lua` (all five changed).
- Saves: `Services/Storage/SaveMapper.cs`; catalog: `Services/Catalog/CatalogDatabase.cs`.
- Audio: `Audio/EngineLoudness.cs`, `EngineSim.cs`, `StartMusic.cs`.

## Process launches / native interop / network (changed)

- `Controls/D3DViewportBase.cs` (render pump, D3D device-lost handling); `Audio/AudioHub*.cs` (FMOD).
- `Services/Ai/AiBackground.cs`, `AiTextService.cs` — still the one HttpClient site (local Ollama / providers).
- `installer/Actions/CustomActions.cs`, `InstallerChecks.cs`, `installer/pack-mod.ps1`.

## Cross-unit trust boundaries

Unchanged in shape: mod zip → `Content\<mod>` → runtime catalog; Lua mode → result JSON → launcher; game ↔ AC process;
installer custom actions → the game's `--uninstall`.
