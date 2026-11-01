using System.IO;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Services
{
    /// <summary>
    /// The Custom Shaders Patch an AC install has, as CSP states it for launchers in
    /// <c>extension\config\data_manifest.ini</c> ([VERSION] SHADERS_PATCH and SHADERS_PATCH_BUILD).
    ///
    /// The game's race modes call CSP's Lua API, which grows with every build: 0.1.80-preview218 (build 2363) has no
    /// <c>ac.onCarCollision</c>, so sc_race failed to load on it and every race came back without a result (2026-11-01,
    /// a clean AC). The modes are played on <see cref="MinimumName"/>, so a race needs at least that build.
    /// </summary>
    public sealed record CspVersion(string Name, int Build)
    {
        /// <summary>The oldest build the races run on: the one they are played on (sc_race needs ac.onCarCollision,
        /// physics.blockTeleportingToPits, physics.setAICarPosition and physics.setAIStopCounter)</summary>
        public const int MinimumBuild = 3465;

        /// <summary>The version <see cref="MinimumBuild"/> is, for the player</summary>
        public const string MinimumName = "0.2.11";

        /// <summary>Older than the races run on</summary>
        public bool TooOld => Build < MinimumBuild;

        /// <summary>The manifest's path in an AC install</summary>
        public static string ManifestPath(string acRoot) => Path.Combine(acRoot, "extension", "config", "data_manifest.ini");

        /// <summary>
        /// The CSP in <paramref name="acRoot"/>; null when its manifest is missing or names no build (then nothing is known,
        /// and nothing is refused on it). Never throws.
        /// </summary>
        public static CspVersion? Read(string acRoot)
        {
            try
            {
                var path = ManifestPath(acRoot);
                if (!File.Exists(path)) return null;
                var ini = new IniText(File.ReadAllText(path));
                if (!int.TryParse(ini.Get("VERSION", "SHADERS_PATCH_BUILD")?.Trim(), out var build)) return null;
                var name = ini.Get("VERSION", "SHADERS_PATCH")?.Trim();
                return new CspVersion(string.IsNullOrEmpty(name) ? $"build {build}" : name, build);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
