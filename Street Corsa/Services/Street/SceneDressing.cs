using StreetCorsa.Services.CarLot;

namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// The one way an indoor scene (a warehouse, a part shop) is dressed for an hour of the day: the renderer's light with
    /// the lamps coloured by the look it wears, the picture each surface wears, and how bright its self-lit glass glows
    /// </summary>
    internal static class SceneDressing
    {
        /// <param name="lights">The scene's light at each hour</param>
        /// <param name="lampColor">The look's colour for the lamps; null leaves them as they are</param>
        /// <param name="pictures">Material to picture, as the look and the scene have them; copied</param>
        /// <param name="glowMaterial">The self-lit material, lit to <paramref name="glows"/> at this light (nothing at a light it leaves out)</param>
        internal static WarehouseDressing Of(IReadOnlyDictionary<StreetLight, StreetLighting> lights, StreetLight light, byte[]? lampColor,
            IReadOnlyDictionary<string, string>? pictures, float[] sun, string glowMaterial, IReadOnlyDictionary<StreetLight, float> glows)
        {
            var lighting = SceneLighting.For(lights, light);
            if (lampColor != null)
                lighting = lighting with { Lamps = lighting.Lamps.Select(lamp => lamp with { Color = lampColor }).ToList() };

            var glow = new Dictionary<string, float>(StringComparer.Ordinal);
            if (glows.TryGetValue(light, out var level)) glow[glowMaterial] = level;

            return new WarehouseDressing(lighting, sun, new Dictionary<string, string>(pictures ?? new Dictionary<string, string>(), StringComparer.Ordinal), glow);
        }
    }
}
