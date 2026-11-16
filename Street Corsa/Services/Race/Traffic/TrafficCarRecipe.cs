namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>
    /// A traffic car as a recipe: the installed AC cars it can be made from, in order of preference, and how it drives.
    /// The game converts the first of them the player has (<see cref="Conversion.TrafficCarConverter"/>), the first
    /// time a road of its fleet is driven. A mod's <c>traffic\*.json</c> holds them under <c>cars</c>, by id: the id is
    /// the traffic model's, and what a fleet names.
    /// </summary>
    public sealed record TrafficCarRecipe
    {
        /// <summary>The traffic model's id: its file names, and what fleets cast</summary>
        public required string Id { get; init; }

        /// <summary>What it is, for the log and Settings</summary>
        public string? Name { get; init; }

        public int? Year { get; init; }

        /// <summary>The AC cars (folders in content\cars) it can be made from, the first installed one taken</summary>
        public required IReadOnlyList<string> Cars { get; init; }

        /// <summary>Its cruising speed in km/h, not a top speed (the lane's limit still holds)</summary>
        public double MaxSpeed { get; init; } = 90;

        /// <summary>Its mass in kg; null: the car's own</summary>
        public double? Mass { get; init; }

        /// <summary>Which of CSP's OEM paint lists its colours come from: <c>vintage</c> or <c>modern</c></summary>
        public string Color { get; init; } = "vintage";

        /// <summary>How often it spawns against the others in the traffic tool's own pick (the fleet's weight casts it)</summary>
        public double Chance { get; init; } = 1;

        /// <summary>How eagerly it drives, 0 to 1 (CSP ships 0.4 and 0.8)</summary>
        public double Dynamic { get; init; } = 0.4;

        /// <summary>Its paint materials by hand; empty: none, its livery kept; null: found</summary>
        public IReadOnlyList<string>? Paint { get; init; }

        /// <summary>Its lamps by hand (<c>headlights</c>, <c>brakes</c>, <c>rear</c>, <c>rearCombined</c>); null: found</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>>? Lights { get; init; }

        /// <summary>The largest side its textures keep; null: the Traffic detail's</summary>
        public int? MaxTexture { get; init; }

        /// <summary>Its near model is the full model, not its LOD B (a LOD B that drops the lamp meshes)</summary>
        public bool NearFull { get; init; }

        /// <summary>Its colour variants: null as many as it needs, 0 none, N that many tints</summary>
        public int? Skins { get; init; }

        /// <summary>
        /// What of the recipe goes into the model made from it, in a line: kept with the model, so a recipe that changed
        /// (a setting's update, a modder's tuning) makes it again. Not its name or year, which the model does not carry.
        /// </summary>
        public string Stamp()
        {
            static string Number(double? n) => n?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "-";
            var paint = Paint == null ? "-" : string.Join(",", Paint);
            var lights = Lights == null ? "-"
                : string.Join(";", Lights.OrderBy(l => l.Key, StringComparer.Ordinal).Select(l => l.Key + "=" + string.Join(",", l.Value)));
            return string.Join("|", Number(MaxSpeed), Number(Mass), Color, Number(Chance), Number(Dynamic), paint, lights,
                NearFull ? "full" : "lod", Skins?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-");
        }
    }
}
