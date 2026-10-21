namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// A picture of a street piece painted another colour (<see cref="PiecePaint"/>): each pixel's lightness against the
    /// picture's middle tone kept, so the stucco's grain, a wall's shade and a window's dark stay as they were, and the
    /// whole brought <c>amount</c> of the way to the colour
    /// </summary>
    public static class StreetPaint
    {
        /// <summary>The pixels (red, green, blue, alpha, a byte each, as the texel reader gives them) painted in place</summary>
        public static void Tint(Span<byte> rgba, byte[] colour, float amount)
        {
            // The middle tone: the mean lightness of what is not see-through
            double sum = 0;
            var count = 0;
            for (var i = 0; i + 3 < rgba.Length; i += 4)
            {
                if (rgba[i + 3] == 0) continue;
                sum += Lightness(rgba[i], rgba[i + 1], rgba[i + 2]);
                count++;
            }

            if (count == 0) return;
            var middle = Math.Max(sum / count, 1.0);
            var (red, green, blue) = (colour[0], colour[1], colour[2]);
            for (var i = 0; i + 3 < rgba.Length; i += 4)
            {
                var light = Lightness(rgba[i], rgba[i + 1], rgba[i + 2]) / middle;
                rgba[i] = Mix(rgba[i], red * light, amount);
                rgba[i + 1] = Mix(rgba[i + 1], green * light, amount);
                rgba[i + 2] = Mix(rgba[i + 2], blue * light, amount);
            }
        }

        /// <summary>The name a picture is kept under painted a colour, beside the one as made (its extension kept last)</summary>
        public static string NameOf(string texture, byte[] colour)
        {
            var dot = texture.LastIndexOf('.');
            var (stem, extension) = dot > 0 ? (texture[..dot], texture[dot..]) : (texture, string.Empty);
            return $"{stem}_paint_{colour[0]:x2}{colour[1]:x2}{colour[2]:x2}{extension}";
        }

        private static double Lightness(byte r, byte g, byte b) => 0.3 * r + 0.59 * g + 0.11 * b;

        private static byte Mix(byte from, double to, float amount) =>
            (byte)Math.Clamp(Math.Round(from + (Math.Min(to, 255.0) - from) * amount), 0, 255);
    }
}
