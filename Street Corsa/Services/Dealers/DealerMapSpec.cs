namespace StreetCorsa.Services.Dealers
{
    /// <summary>
    /// The picture the dealers' pins stand on (a <c>maps</c> list in a mod's <c>dealers</c> folder). The dealers'
    /// mapX/mapY are fractions of this picture, so a mod that brings its own map re-reads every pin off it, the base
    /// game's too (<c>"replaces"</c> on each dealer). The last mod's map is the one shown.
    /// </summary>
    public class DealerMapSpec
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>The picture, next to the mod's dealer files</summary>
        public string Image { get; set; } = string.Empty;

        /// <summary>
        /// The part of the picture that must be on screen, as fractions of the whole: where the dealers are. The screen
        /// grows out of it to the window's shape, so a wider window sees more country.
        /// </summary>
        public double AnchorX { get; set; }
        public double AnchorY { get; set; }
        public double AnchorWidth { get; set; } = 1;
        public double AnchorHeight { get; set; } = 1;

        /// <summary>The picture's full path, worked out when it was read; null when it is not there</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? ImagePath { get; set; }

        /// <summary>The anchor kept inside the picture</summary>
        public System.Windows.Rect Anchor
        {
            get
            {
                var x = Clamp01(AnchorX);
                var y = Clamp01(AnchorY);
                var width = Math.Clamp(Finite(AnchorWidth, 1), 0.05, 1 - x);
                var height = Math.Clamp(Finite(AnchorHeight, 1), 0.05, 1 - y);
                return new System.Windows.Rect(x, y, Math.Max(0.05, width), Math.Max(0.05, height));
            }
        }

        private static double Finite(double value, double otherwise) => double.IsFinite(value) ? value : otherwise;

        private static double Clamp01(double value) => Math.Clamp(Finite(value, 0), 0, 0.95);
    }
}
