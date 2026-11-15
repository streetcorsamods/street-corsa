namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>
    /// How much of the traffic a machine is asked to carry. Every traffic model sits in video memory for the whole drive
    /// (about 20 MB each before compression), so a weak machine gets fewer of them, smaller textures and fewer cars.
    /// </summary>
    public sealed record TrafficDetail(string Key, int MostModels, int TextureSide, double DensityFactor)
    {
        /// <summary>At most 8 models, 512 px textures (a quarter of the memory), 0.6 of the cars</summary>
        public static readonly TrafficDetail Low = new("low", 8, 512, 0.6);

        /// <summary>At most 15 models, 1024 px textures</summary>
        public static readonly TrafficDetail Medium = new("medium", 15, 1024, 1);

        /// <summary>The whole fleet, 1024 px textures</summary>
        public static readonly TrafficDetail High = new("high", int.MaxValue, 1024, 1);

        public static IReadOnlyList<TrafficDetail> Levels { get; } = [Low, Medium, High];

        /// <summary>The level of a saved key; null for none or one not known (the machine's own is taken then)</summary>
        public static TrafficDetail? Of(string? key) => Levels.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        /// <summary>The level a graphics card's own memory suits: under 3 GB low, under 6 GB medium, else high</summary>
        public static TrafficDetail ForVideoMemory(long bytes) => bytes switch
        {
            <= 0 => Medium,
            < 3L << 30 => Low,
            < 6L << 30 => Medium,
            _ => High
        };

        /// <summary>The video memory of the machine's first graphics card, in bytes; 0 when it cannot be told</summary>
        public static long VideoMemory()
        {
            try
            {
                using var factory = new SlimDX.DXGI.Factory1();
                using var adapter = factory.GetAdapter1(0);
                return adapter.Description1.DedicatedVideoMemory;
            }
            catch (Exception)
            {
                // No DXGI (a test host, a remote session): the middle level
                return 0;
            }
        }

        /// <summary>The player's level, else the machine's own</summary>
        public static TrafficDetail Current(string? saved) => Of(saved) ?? Machine.Value;

        private static readonly Lazy<TrafficDetail> Machine = new(() => ForVideoMemory(VideoMemory()));
    }
}
