# Workspace Map

> Overwritten by each tech-audit run — a snapshot, not history. Snapshot of 2026-10-25 @ a56d653f.

Solution `Street Corsa.slnx` (3 projects) + the installer's own `installer/Installer.slnx`. Repo-level: `Directory.Build.props`
(`AcToolsLib` = `lib\actools\`), `global.json` (SDK 10.0.400, rollForward latestFeature, MTP test runner), `nuget.config`
(cleared, nuget.org only), `.github/workflows/check.yml` + `installer.yml` (CI disabled on purpose 2026-10-23).

| Unit | Path | Classification | Target | Flags | Packages | Size |
| --- | --- | --- | --- | --- | --- | --- |
| Street Corsa | `Street Corsa/Street Corsa.csproj` | WPF desktop app | net10.0-windows | Nullable, unsafe, WPF + WinForms, x64 | LiteDB 5.0.21, Newtonsoft.Json 13.0.4, Serilog 4.4.0, Serilog.Sinks.File 7.0.0, Vortice.Direct3D9 3.8.3 | 671 .cs (~118k lines) + 55 .xaml |
| StreetCorsa.Tests | `tests/StreetCorsa.Tests/StreetCorsa.Tests.csproj` | Test project (xunit.v3 on MTP) | net10.0-windows | Nullable, WPF, x64 | Microsoft.NET.Test.Sdk 18.11.0, xunit.v3 4.0.2, xunit.runner.visualstudio 4.0.1 | 132 .cs; links `tools/Shared/{Kn5SceneWriter,ObjModel,SceneMesh,BuilderArgs}.cs` |
| EngineBench | `tools/EngineBench/EngineBench.csproj` | CLI tool | net10.0 | Nullable | Newtonsoft.Json 13.0.4 | 5 own .cs; links the game's `Parts/**`, `Mods/*`, some `Helpers/*`, `Models/*` (audited under the game) |
| InstallerActions | `installer/Actions/InstallerActions.csproj` | Library (WiX DTF custom actions; not in the main solution) | net472 | Nullable, LangVersion latest | WixToolset.Dtf.CustomAction 6.0.2 (7.0.0 out) | 1 .cs; plus `Package.wxs`, `Dialogs.wxs`, wixproj |
| sc_race | `apps/new-modes/sc_race` | CSP Lua mode | CSP Lua (LuaJIT) | writes the race result JSON | — | mode.lua, car_parts.lua, format_*.lua |
| sc_free | `apps/new-modes/sc_free` | CSP Lua mode | CSP Lua | — | — | mode.lua |
| check.ps1 | `tools/check.ps1` | Build/ops script | PowerShell | — | — | — |
| installer scripts | `installer/build.ps1`, `sandbox.ps1`, `smoke.ps1` | Build/ops script | PowerShell | — | — | — |
| Python helpers | `tools/sfx`, `tools/siren`, `tools/Shared/scene_tools.py`, `tools/sc_race_harness/test_*.py`, `tests/.../Fixtures/*.py`, `installer/make_art.py` | Content generators / Lua harness (Unknown by the classifier table: no rule for Python tools) | Python | — | `tools/requirements.txt` | — |

Local references (`lib/actools`, checked in, built from gro-ove/actools @ effa0e13): AcTools.dll, AcTools.Render.dll,
SlimDX.dll (+ ExtraKn5Utils, K4os LZ4, NBidi, Nvidia.TextureTools.Compress, StringBasedFilter, xxHashSharp) — all present.

Signals (game): writes_ac_install · parses_untrusted_binary (KN5/ACD via AcTools, own ShellFile/StationGround) · native_interop
(FMOD 1.08 LibraryImport, Win32 hooks, D3D9 Vortice + SlimDX) · unsafe_code · network (HttpClient: Ollama/OpenAI/Anthropic/ComfyUI)
· db LiteDB · process_launch (AC, explorer, shell) · lua_results.

Untracked on disk (not audited): `artifacts/` (installer stage/publish), stale `tools/*Scene`, `tools/Props`, `tools/RebuildSounds`,
`tools/RunningGearParts`, `tools/SlrrPartsConverter` folders from before the core-only split.
