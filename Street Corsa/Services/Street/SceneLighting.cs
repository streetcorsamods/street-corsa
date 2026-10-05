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
        /// <summary>The light a scene falls back on when its file gives none for the hour, nor for the day</summary>
        public static readonly StreetLighting Default = new(1.5f, [200, 180, 180], 2f, [150, 180, 180], [150, 180, 180], 0.5f, []);

        /// <summary>A street light as a scene's file gives it; a warehouse's lamps are white, so their colour is left out</summary>
        internal sealed record LampEntry(float X, float Y, float Z, int[]? Color, float Brightness, float Range);

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
        /// The renderer's light for each light of the day the file names
        /// </summary>
        /// <param name="scene">The scene's id, for the log</param>
        /// <param name="cubemapAmbient">The reflections' fill when a light leaves it out</param>
        /// <param name="sharedLamps">The lamps of every hour (a warehouse's), in place of each light's own</param>
        internal static Dictionary<StreetLight, StreetLighting> Read(Dictionary<string, LightEntry?>? entries, string scene, IAppLogger logger,
            float cubemapAmbient = 0.5f, IReadOnlyList<LampEntry?>? sharedLamps = null)
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
                var lamps = (sharedLamps ?? light.Lamps ?? []).OfType<LampEntry>()
                    .Select(l => new StreetLamp(l.X, l.Y, l.Z, Rgb(l.Color, 255), l.Brightness * scale, l.Range))
                    .ToList();
                lights[which] = new StreetLighting(light.Brightness, Rgb(light.Color, 200), light.Ambient,
                    Rgb(light.AmbientUp, 150), Rgb(light.AmbientDown, 150), light.CubemapAmbient ?? cubemapAmbient, lamps);
            }

            return lights;
        }

        /// <summary>The light of the day as the renderer gets it; the day's when the file leaves one out, else <see cref="Default"/></summary>
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

        public static StreetLighting For(IReadOnlyDictionary<StreetLight, StreetLighting> lights, StreetLight light) =>
            lights.TryGetValue(light, out var lighting) ? lighting
            : lights.TryGetValue(StreetLight.Day, out var day) ? day
            : Default;

        /// <summary>A colour from JSON numbers (not the base64 a byte[] is read from), each held to 0..255</summary>
        internal static byte[] Rgb(int[]? color, byte fallback) =>
            color is { Length: 3 } ? color.Select(c => (byte)Math.Clamp(c, 0, 255)).ToArray() : [fallback, fallback, fallback];
    }
}
