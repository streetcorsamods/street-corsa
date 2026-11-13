using StreetCorsa.Logging;

namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// The lights of the day as the scenes' files give them (a street's street.json, a gas station's station.json, a
    /// warehouse's warehouse.json), read the one way: a mod's file is not trusted to be whole, so a light or a lamp left
    /// empty is skipped, and a light named for no time of day is skipped and logged.
    /// </summary>
    public static class SceneLighting
    {
        /// <summary>
        /// The most lamps of a light of the day that cast shadows. Each is a shadow map of six faces (24 MB), drawn again with
        /// the scene's every change, and past five the renderer shades every pixel for all it can take. The game's own rooms
        /// hang nine at most; a file that has more keeps its brightest casting, the rest lighting without a shadow.
        /// </summary>
        internal const int MostShadowLamps = 12;

        /// <summary>The farthest a lamp reaches, metres (the game's own reach 14 at most); one given farther reaches this far</summary>
        internal const float MostLampRange = 50f;

        /// <summary>The light a scene falls back on when its file gives none for the hour, nor for the day</summary>
        public static readonly StreetLighting Default = new(1.5f, [200, 180, 180], 2f, [150, 180, 180], [150, 180, 180], 0.5f, []);

        /// <summary>
        /// A street light as a scene's file gives it; a warehouse's lamps are white, so their colour is left out.
        /// <see cref="Shadows"/>: whether it casts shadows; left out, a room's lamps do and a street's do not.
        /// </summary>
        internal sealed record LampEntry(float X, float Y, float Z, int[]? Color, float Brightness, float Range, bool? Shadows = null);

        /// <summary>
        /// A light of the day as a scene's file gives it. <see cref="Lamps"/> are its own lamps (a street's, a station's);
        /// a warehouse lists its lamps once for every hour and scales them by <see cref="LampScale"/>.
        /// </summary>
        internal sealed record LightEntry(float Brightness, int[]? Color, float Ambient, int[]? AmbientUp, int[]? AmbientDown,
            float? CubemapAmbient, List<LampEntry?>? Lamps, float? LampScale);

        /// <summary>
        /// A light of the day by name ("day", "dusk", "night", any case). Not a number: <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
        /// takes any integer, and "7" would be a light that is not there.
        /// </summary>
        public static bool TryParse(string? name, out StreetLight light) =>
            Enum.TryParse(name, ignoreCase: true, out light) && Enum.IsDefined(light) && !IsNumber(name);

        private static bool IsNumber(string? name) => name != null && name.Trim().TrimStart('-', '+').All(char.IsDigit);

        /// <summary>
        /// The renderer's light for each light of the day the file names. A lamp that is not where a scene can be (a figure
        /// that is no number, or past <see cref="Scripting.SceneMarks.MostPoint"/>) or reaches nowhere is skipped; one that
        /// reaches past <see cref="MostLampRange"/> reaches that far; past <see cref="MostShadowLamps"/> casting shadows, the
        /// dimmest of them light without one. Each is logged.
        /// </summary>
        /// <param name="scene">The scene's id, for the log</param>
        /// <param name="cubemapAmbient">The reflections' fill when a light leaves it out</param>
        /// <param name="sharedLamps">The lamps of every hour (a warehouse's), in place of each light's own</param>
        /// <param name="lampShadows">Whether a lamp that does not say casts shadows: a room's do, a street's do not</param>
        internal static Dictionary<StreetLight, StreetLighting> Read(Dictionary<string, LightEntry?>? entries, string scene, IAppLogger logger,
            float cubemapAmbient = 0.5f, IReadOnlyList<LampEntry?>? sharedLamps = null, bool lampShadows = false)
        {
            var lights = new Dictionary<StreetLight, StreetLighting>();
            foreach (var (name, light) in entries ?? [])
            {
                if (light == null) continue;
                if (!TryParse(name, out var which))
                {
                    logger.Warning("The scene {Id} has a light \"{Light}\", which is no light of the day (day, dusk, night)", scene, name);
                    continue;
                }

                var scale = light.LampScale ?? 1f;
                var given = (sharedLamps ?? light.Lamps ?? []).OfType<LampEntry>().ToList();
                // A lamp reaching too far is held to MostLampRange; one reaching nowhere (none, not a number, an endless
                // range) is skipped, and told only as skipped
                var hung = given
                    .Where(l => ScenePaths.Within(l.X) && ScenePaths.Within(l.Y) && ScenePaths.Within(l.Z) && l.Range > 0f && float.IsFinite(l.Range))
                    .ToList();
                var lamps = hung
                    .Select(l => new StreetLamp(l.X, l.Y, l.Z, Rgb(l.Color, 255), l.Brightness * scale, Math.Min(l.Range, MostLampRange), l.Shadows ?? lampShadows))
                    .ToList();
                if (lamps.Count < given.Count)
                    logger.Warning("The scene {Id} has {Count} lamps at {Light} out of its bounds or reaching nowhere: skipped", scene, given.Count - lamps.Count, which);
                if (hung.Any(l => l.Range > MostLampRange))
                    logger.Warning("The scene {Id} has lamps at {Light} reaching past {Most} m: they reach that far", scene, which, MostLampRange);

                lights[which] = new StreetLighting(light.Brightness, Rgb(light.Color, 200), light.Ambient,
                    Rgb(light.AmbientUp, 150), Rgb(light.AmbientDown, 150), light.CubemapAmbient ?? cubemapAmbient, FewShadows(lamps, scene, which, logger));
            }

            return lights;
        }

        /// <summary>The lamps with <see cref="MostShadowLamps"/> of them casting shadows at most, the brightest (the first of equals)</summary>
        private static List<StreetLamp> FewShadows(List<StreetLamp> lamps, string scene, StreetLight light, IAppLogger logger)
        {
            var casting = lamps.Where(l => l.Shadows).ToList();
            if (casting.Count <= MostShadowLamps) return lamps;

            logger.Warning("The scene {Id} has {Count} lamps casting shadows at {Light}: the {Most} brightest do", scene, casting.Count, light, MostShadowLamps);
            var kept = casting.OrderByDescending(l => l.Brightness).Take(MostShadowLamps).ToHashSet(ReferenceEqualityComparer.Instance);
            return lamps.Select(l => l.Shadows && !kept.Contains(l) ? l with { Shadows = false } : l).ToList();
        }

        /// <summary>
        /// How bright each self-lit material (a sign, the windows) glows at each light of the day a scene's file names; a
        /// light named for no time of day, or a glow out of 0 to 4, is skipped
        /// </summary>
        public static IReadOnlyDictionary<StreetLight, IReadOnlyDictionary<string, float>> MaterialGlows(Dictionary<string, Dictionary<string, float>?>? entries)
        {
            var glows = new Dictionary<StreetLight, IReadOnlyDictionary<string, float>>();
            foreach (var (name, materials) in entries ?? [])
            {
                if (materials != null && TryParse(name, out var light))
                    glows[light] = materials.Where(m => float.IsFinite(m.Value) && m.Value is >= 0 and <= 4).ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);
            }

            return glows;
        }

        /// <summary>The glows of a light of the day out of <see cref="MaterialGlows"/>; none when it names none</summary>
        public static IReadOnlyDictionary<string, float> GlowAt(IReadOnlyDictionary<StreetLight, IReadOnlyDictionary<string, float>> glows, StreetLight light) =>
            glows.TryGetValue(light, out var glow) ? glow : new Dictionary<string, float>();

        /// <summary>The light of the day as the renderer gets it; the day's when the file leaves one out, else <see cref="Default"/></summary>
        public static StreetLighting For(IReadOnlyDictionary<StreetLight, StreetLighting> lights, StreetLight light) =>
            lights.TryGetValue(light, out var lighting) ? lighting
            : lights.TryGetValue(StreetLight.Day, out var day) ? day
            : Default;

        /// <summary>A colour from JSON numbers (not the base64 a byte[] is read from), each held to 0..255</summary>
        internal static byte[] Rgb(int[]? color, byte fallback) =>
            color is { Length: 3 } ? color.Select(c => (byte)Math.Clamp(c, 0, 255)).ToArray() : [fallback, fallback, fallback];
    }
}
