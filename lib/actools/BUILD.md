# The AcTools binaries

Content Manager's libraries (gro-ove/actools, Ms-PL) and SlimDX (MIT), as the game, the tests and the tools reference
them through `$(AcToolsLib)` in `Directory.Build.props`. Release x64 builds of one commit, unmodified; the checksums
below say whether what is here is still what that build made. See also `docs/architecture/overview.md`, "Build
Dependencies".

## Source

- Repository: https://github.com/gro-ove/actools
- Commit: `effa0e131a44ced7bbce24d8953cfc7db09faef7` (2025-10-02, "Update SimuCube.cs")

## Build

MSBuild from Visual Studio, with the .NET Framework 4.5.2 targeting pack:

```
git clone https://github.com/gro-ove/actools ..\actools
git -C ..\actools checkout effa0e131a44ced7bbce24d8953cfc7db09faef7
MSBuild ..\actools\AcTools.Render\AcTools.Render.csproj -p:Configuration=Release -p:Platform=x64
```

Then copy `AcTools.dll` and `AcTools.dll.config` from `AcTools\bin\x64\Release`; `AcTools.Render.dll`,
`AcTools.ExtraKn5Utils.dll`, `K4os.Compression.LZ4.dll`, `NBidi.dll`, `Nvidia.TextureTools.Compress.dll`,
`StringBasedFilter.dll` and `xxHashSharp.dll` from `AcTools.Render\bin\x64\Release`; and `SlimDX.dll` from
`Libraries\SlimDX-x64`, all into this folder. Release, not Debug: the Debug `CarSlot.SetCarAsync` shows a raw exception
MessageBox for any car that fails to load, and it is unoptimized.

SlimDX stays only as long as AcTools.Render needs it (the game carries Vortice.Direct3D9 for its own Direct3D 9 work).

## Checksums (SHA-256)

Checked with `Get-FileHash -Algorithm SHA256 lib\actools\*.dll` (or `sha256sum`); a rebuild from the same commit with the
same toolchain gives the same bytes for the managed assemblies only if the build is deterministic, so a rebuilt set is
listed here again.

| File                                | SHA-256                                                            |
| ----------------------------------- | ------------------------------------------------------------------ |
| `AcTools.dll`                       | `c24f78f081dace818621a71316caa70ecc037afe706577a792989a23de8e3bc7` |
| `AcTools.dll.config`                | `331a0a2d54a61bc34ee8e127cd2b2f162e69e0b020b7649c7dfa1fc78db503c4` |
| `AcTools.ExtraKn5Utils.dll`         | `9caa996308914aab4840d5d02dfdeaf787713135d7c765873de2a9fe0e3fff1e` |
| `AcTools.Render.dll`                | `00aa0dcd6ebd8d857d944e6f0bb5294cd0d599394d7f3abd9e6513df589d3969` |
| `K4os.Compression.LZ4.dll`          | `781eed9f4ea0d61d336a6e771ddf0979193e304f894da9999bfb7316ebff5c00` |
| `NBidi.dll`                         | `cb8c6923ff00a9ec32ecff01e67af2e9c9620051de01cc9d9eda09ff08c4fb25` |
| `Nvidia.TextureTools.Compress.dll`  | `f8d840e03f754949037bbdef88f323b3a9e0d02ae2288c0b3f7defc9cadd74a9` |
| `SlimDX.dll`                        | `7b6185ce1dc247f66feeef73abfd2c8ceb94eb6ec8f33f3b6758aefe83a05ac9` |
| `StringBasedFilter.dll`             | `56d6e90c739ea18f0dc86c551d2b5452439c70dca86ad69fa891a9ee91adf4d4` |
| `xxHashSharp.dll`                   | `cbc0d23b9647bf7f7da1ee73c0c83c4d587f84fb6f786a69feb0414cb0d94e43` |
