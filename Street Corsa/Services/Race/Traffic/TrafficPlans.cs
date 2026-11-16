using System.IO;
using System.Text.Json;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>Where a road's traffic plan comes from</summary>
    public enum PlanSource
    {
        /// <summary>The track's own <c>data\traffic.json</c>, read where it is</summary>
        Track,

        /// <summary>A mod's <c>traffic\plans\&lt;track&gt;[\&lt;layout&gt;]\traffic.json</c></summary>
        Mod,

        /// <summary>Made by the game from the road's AI line (<see cref="TrafficPlanBuilder"/>) and kept in its own folder</summary>
        Built
    }

    /// <summary>A road's traffic plan: the file, and where it came from</summary>
    public sealed record RoadPlan(string File, PlanSource Source, string? Why = null)
    {
        /// <summary>The file race.ini names for the mode: none for the track's own, which the mode finds itself</summary>
        public string? ForRace => Source == PlanSource.Track ? null : File;
    }

    /// <summary>
    /// A road's traffic plan, looked for in this order: the track's own (untouched); one a mod ships, the last mod's
    /// first; one the game built from the road's AI line before (and the line has not changed since); else one it builds
    /// now, the first time the road is driven, kept under <see cref="BuiltRoot"/>. Nothing goes into the track: the mode
    /// reads the plan from wherever race.ini's <c>TRAFFIC_PLAN</c> says. A road with no plan and no AI line has no traffic.
    /// </summary>
    public static class TrafficPlans
    {
        /// <summary>The plans folder in a mod's <c>traffic</c> folder</summary>
        public const string PlansFolder = "plans";

        // What a built plan was made from, beside it: a changed AI line or builder means a new plan
        private const string MadeFromFile = "made_from.json";

        // Bumped when the builder makes different plans from the same line, so the old ones are made again
        private const int BuilderVersion = 2;

        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("Traffic"));

        /// <summary>Where the game keeps the plans it builds: its own folder, never the track's</summary>
        public static string BuiltRoot => Path.Combine(AppSettings.AppDataPath, "Traffic", PlansFolder);

        /// <summary>
        /// Whether a road can be driven in traffic: it has a plan, or an AI line to build one from. Only files are looked
        /// for (a list asks it of every track).
        /// </summary>
        public static bool CanHave(string trackFolder, string trackId, string? layout, ModSet mods) =>
            TrafficRoad.PlanOf(trackFolder, layout) != null || Shipped(trackId, layout, mods) != null || AiLine.FileOf(trackFolder, layout) != null;

        /// <summary>A plan a mod ships for the road: the layout's, else the track's, the last mod's first; null when none does</summary>
        public static string? Shipped(string trackId, string? layout, ModSet mods)
        {
            foreach (var (_, dir) in mods.Folders(ContentTypes.Traffic).Reverse())
            {
                var plans = Path.Combine(dir, PlansFolder, trackId);
                if (!string.IsNullOrEmpty(layout) && File.Exists(Path.Combine(plans, layout, TrafficRoad.PlanFileName)))
                    return Path.Combine(plans, layout, TrafficRoad.PlanFileName);
                if (File.Exists(Path.Combine(plans, TrafficRoad.PlanFileName))) return Path.Combine(plans, TrafficRoad.PlanFileName);
            }
            return null;
        }

        /// <summary>Where the game keeps the plan it builds for a road</summary>
        public static string BuiltFolder(string trackId, string? layout, string? root = null) =>
            string.IsNullOrEmpty(layout) ? Path.Combine(root ?? BuiltRoot, trackId) : Path.Combine(root ?? BuiltRoot, trackId, layout);

        /// <summary>
        /// The road's plan, built from its AI line when it has none (<paramref name="keepLeft"/>: traffic keeps left);
        /// null when it has neither, or the line does not read
        /// </summary>
        public static RoadPlan? For(string trackFolder, string trackId, string? layout, bool keepLeft, ModSet mods, string? root = null, PlanLanes lanes = PlanLanes.Auto)
        {
            if (TrafficRoad.PlanOf(trackFolder, layout) is { } own) return new RoadPlan(own, PlanSource.Track);
            if (Shipped(trackId, layout, mods) is { } shipped) return new RoadPlan(shipped, PlanSource.Mod);

            var ai = AiLine.FileOf(trackFolder, layout);
            if (ai == null) return null;

            var folder = BuiltFolder(trackId, layout, root);
            var file = Path.Combine(folder, TrafficRoad.PlanFileName);
            var madeFrom = MadeFrom(ai, keepLeft, lanes);
            if (File.Exists(file) && ReadMadeFrom(folder) == madeFrom) return new RoadPlan(file, PlanSource.Built);

            try
            {
                var line = AiLine.Read(ai);
                var edges = TrafficPlanBuilder.Edges.Of(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ai)!)!, "data"));
                var plan = TrafficPlanBuilder.Build(line, keepLeft, edges, lanes);
                Directory.CreateDirectory(folder);
                WriteAtomically(file, TrafficPlanBuilder.ToJson(plan));
                WriteAtomically(Path.Combine(folder, MadeFromFile), madeFrom);
                LazyLogger.Value.Information("Traffic plan built for {Track}/{Layout}: {Lanes} lane(s), {Why}", trackId, layout ?? "(none)", plan.Lanes.Count, plan.Why);
                return new RoadPlan(file, PlanSource.Built, plan.Why);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
                or ArgumentException or ArithmeticException or IndexOutOfRangeException)
            {
                // A line that does not read, or one the builder cannot make a road of (no points, figures that are none)
                LazyLogger.Value.Warning("No traffic plan could be built for {Track}/{Layout} from {Ai}: {Error}", trackId, layout ?? "(none)", ai, ex.Message);
                return null;
            }
        }

        /// <summary>What a built plan is made from: the AI line (its path, size and time), the driving side, the player's lanes, the builder</summary>
        private static string MadeFrom(string ai, bool keepLeft, PlanLanes lanes)
        {
            var info = new FileInfo(ai);
            return JsonSerializer.Serialize(new { ai = info.FullName, bytes = info.Length, written = info.LastWriteTimeUtc.Ticks, keepLeft, lanes = lanes.ToString(), builder = BuilderVersion });
        }

        private static string? ReadMadeFrom(string folder)
        {
            try
            {
                return File.ReadAllText(Path.Combine(folder, MadeFromFile));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void WriteAtomically(string file, string text)
        {
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, text);
            File.Move(temporary, file, overwrite: true);
        }
    }
}
