using System.IO;
using StreetCorsa.Models.Race;

namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>
    /// The traffic a run of a format in traffic (<see cref="RaceFormat.Traffic"/>) gets on its road: how many cars, sized
    /// to the lane length in reach (<see cref="TrafficRoad.ReachKm"/>) at the rules' cars per lane-km and scaled by the
    /// player's density, and which models, the road's fleet (<see cref="TrafficFleets"/>) as far as the traffic tool has them.
    /// </summary>
    public static class TrafficSetup
    {
        /// <summary>The fewest cars the mode takes: a short road still needs enough to read as traffic, not three strays</summary>
        public const int LeastCars = 20;

        /// <summary>The most cars the mode takes</summary>
        public const int MostCars = 2500;

        /// <summary>The player's density at its quietest and at its busiest (<see cref="Settings.GameSettings.TrafficDensity"/>)</summary>
        public const double LeastDensity = 0.3;
        public const double MostDensity = 2;

        /// <summary>Where CSP's traffic tool keeps its models, in the AC folder: one .json (and its folder) each</summary>
        public static string ModelsFolder(string acRoot) => Path.Combine(acRoot, "extension", "lua", "tools", "csp-traffic-tool", "data");

        /// <summary>Whether a road has traffic to drive in: a plan of its own (only looked for, not read: a list asks it of every track)</summary>
        public static bool HasPlan(string trackFolder, string? layout) => TrafficRoad.PlanOf(trackFolder, layout) != null;

        /// <summary>
        /// How many cars a road gets: its lane-km in reach × <paramref name="perLaneKm"/> × <paramref name="density"/>, held to
        /// what the mode takes; the fewest on a road that cannot be measured
        /// </summary>
        public static int Cars(TrafficRoad? road, double perLaneKm, double density)
        {
            var scale = double.IsFinite(density) ? Math.Clamp(density, LeastDensity, MostDensity) : 1;
            if (road == null || !(road.ReachKm > 0) || !double.IsFinite(perLaneKm)) return LeastCars;
            return (int)Math.Clamp(Math.Round(road.ReachKm * perLaneKm * scale), LeastCars, MostCars);
        }

        /// <summary>
        /// The traffic of a run on a road: its own plan measured, the count, and its fleet as far as the traffic tool
        /// has the models (none of them: no cast, and the mode takes every model there is)
        /// </summary>
        /// <param name="trackFolder">The track's folder in AC's content\tracks</param>
        public static RaceTraffic For(string acRoot, string trackFolder, string trackId, string? layout, double perLaneKm, double density, TrafficFleets fleets)
        {
            var plan = TrafficRoad.PlanOf(trackFolder, layout);
            var road = plan == null ? null : TrafficRoad.Read(plan);
            var cast = TrafficFleets.InFolder(fleets.For(trackId, layout), ModelsFolder(acRoot));
            return new RaceTraffic
            {
                Cars = Cars(road, perLaneKm, density),
                Cast = cast.Count == 0 ? null : TrafficFleets.Spec(cast),
                Density = double.IsFinite(density) ? Math.Clamp(density, LeastDensity, MostDensity) : 1
            };
        }
    }
}
