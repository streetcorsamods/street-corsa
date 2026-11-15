namespace StreetCorsa.Screens.Settings
{
    /// <summary>How busy the roads are in a drive in traffic: the factor on each road's own traffic, and its name</summary>
    public sealed record TrafficChoice(double Density, string Name)
    {
        /// <summary>The choices, quietest first, and the words of each: the road's own (1) in the middle</summary>
        public static readonly (double Density, string Key)[] Levels =
        [
            (0.3, "settings.traffic.quiet"),
            (0.6, "settings.traffic.light"),
            (1, "settings.traffic.normal"),
            (1.5, "settings.traffic.busy"),
            (2, "settings.traffic.rush")
        ];

        public override string ToString() => Name;
    }
}
