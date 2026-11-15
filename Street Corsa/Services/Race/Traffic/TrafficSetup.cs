using System.IO;
using StreetCorsa.Logging;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Services.Race.Traffic.Conversion;

namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>Where getting a road's traffic ready has got to: a line for the player</summary>
    public sealed record TrafficProgress(int Done, int Of, string? Making);

    /// <summary>
    /// The traffic a run of a format in traffic (<see cref="RaceFormat.Traffic"/>) gets on its road: the plan
    /// (<see cref="TrafficPlans"/>: the track's, a mod's, or one built from its AI line), how many cars (sized to the lane
    /// length in reach, <see cref="TrafficRoad.ReachKm"/>, at the rules' cars per lane-km, scaled by the player's density
    /// and the Traffic detail), and which models: the road's fleet (<see cref="TrafficFleets"/>), each a recipe over the
    /// player's own cars, converted the first time it is needed (<see cref="TrafficCarConverter"/>) into the game's own
    /// folder (<see cref="TrafficModelStore"/>). Too little of the fleet to be made, and the base fleet (Kunos' road
    /// cars) fills in. Nothing is put into AC's folders: race.ini names the plan and the models' folder to the mode.
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

        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("Traffic"));

        /// <summary>Where CSP's traffic tool keeps its models, in the AC folder: one .json (and its folder) each</summary>
        public static string ModelsFolder(string acRoot) => Path.Combine(acRoot, "extension", "lua", "tools", "csp-traffic-tool", "data");

        /// <summary>Whether a road can have traffic to drive in: a plan, or an AI line to build one from (only looked for: a list asks it of every track)</summary>
        public static bool HasPlan(string trackFolder, string trackId, string? layout) => TrafficPlans.CanHave(trackFolder, trackId, layout, ModSet.Installed);

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

        /// <summary>What a road's cast is made of before anything is converted</summary>
        public sealed record Cast(IReadOnlyList<FleetModel> Models, IReadOnlyList<(FleetModel Model, TrafficCarRecipe Recipe, string Car)> ToMake,
            IReadOnlyList<string> Adopt, IReadOnlyList<string> Left);

        /// <summary>
        /// The models a road's traffic is cast from: its fleet, each model made from a recipe whose car is installed, or
        /// one the player already has in the traffic tool's folder; the base fleet added when fewer than
        /// <see cref="TrafficFleets.LeastCast"/> can be had; the heaviest-weighted first, as many as the detail allows.
        /// Which need making (none there, or out of date) and which are left out and why.
        /// </summary>
        public static Cast CastFor(string trackId, string? layout, TrafficFleets fleets, TrafficCarConverter converter, TrafficModelStore store,
            string toolFolder, TrafficDetail detail)
        {
            var chosen = new List<FleetModel>();
            var toMake = new List<(FleetModel, TrafficCarRecipe, string)>();
            var adopt = new List<string>();
            var left = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Take(IEnumerable<FleetModel> models)
            {
                foreach (var model in models.OrderByDescending(m => m.Weight))
                {
                    if (!seen.Add(model.Id)) continue;
                    if (chosen.Count >= detail.MostModels)
                    {
                        left.Add($"{model.Id}: past the Traffic detail's {detail.MostModels} models");
                        continue;
                    }
                    if (fleets.Cars.TryGetValue(model.Id, out var recipe))
                    {
                        if (converter.SourceOf(recipe) is not { } car)
                        {
                            // Made before from a car since taken out: the model is still good
                            if (store.Has(model.Id)) chosen.Add(model);
                            else left.Add($"{model.Id}: none of {string.Join(", ", recipe.Cars)} is installed");
                            continue;
                        }
                        chosen.Add(model);
                        if (!store.IsFresh(model.Id, car, TrafficCarConverter.ModelsOf(car), recipe.MaxTexture ?? detail.TextureSide)) toMake.Add((model, recipe, car));
                    }
                    else if (store.Has(model.Id) || File.Exists(Path.Combine(toolFolder, model.Id + ".json")))
                    {
                        // A model of the player's own, made by hand or by another tool: taken as it is
                        chosen.Add(model);
                        if (File.Exists(Path.Combine(toolFolder, model.Id + ".json"))) adopt.Add(model.Id);
                    }
                    else
                    {
                        left.Add($"{model.Id}: no recipe, and not in the traffic tool's folder");
                    }
                }
            }

            Take(fleets.For(trackId, layout));
            if (chosen.Count < TrafficFleets.LeastCast) Take(fleets.BaseModels);
            return new Cast(chosen, toMake, adopt, left);
        }

        /// <summary>
        /// The traffic of a run on a road, made ready: the plan found or built, the count, the models converted where
        /// they are missing (the progress told, the free space checked first: what does not fit is left out, the
        /// heaviest-weighted kept). A model that will not convert is left out and the rest go. Cancelling stops after
        /// the model being made and throws; every model made so far is kept for next time.
        /// </summary>
        /// <param name="trackFolder">The track's folder in AC's content\tracks</param>
        public static RaceTraffic For(string acRoot, string trackFolder, string trackId, string? layout, double perLaneKm, double density, TrafficFleets fleets,
            TrafficDetail detail, ModSet mods, IProgress<TrafficProgress>? progress = null, CancellationToken cancel = default, TrafficModelStore? store = null,
            string? planRoot = null, PlanLanes lanes = PlanLanes.Auto)
        {
            var log = LazyLogger.Value;
            store ??= new TrafficModelStore();
            var plan = TrafficPlans.For(trackFolder, trackId, layout, fleets.KeepsLeft(trackId, layout), mods, planRoot, lanes);
            var road = plan == null ? null : TrafficRoad.Read(plan.File);
            if (plan != null) log.Information("Traffic plan for {Track}/{Layout}: {Source} {File}", trackId, layout ?? "(none)", plan.Source, plan.File);
            else log.Warning("No traffic plan for {Track}/{Layout}: the road has none and no AI line to build one from", trackId, layout ?? "(none)");

            var toolFolder = ModelsFolder(acRoot);
            var cast = CastFor(trackId, layout, fleets, new TrafficCarConverter(acRoot, NoTexels.Instance), store, toolFolder, detail);
            foreach (var why in cast.Left) log.Information("Traffic: left out {Why}", why);

            var ready = cast.Models.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in cast.Adopt)
            {
                if (!store.Adopt(id, toolFolder))
                {
                    ready.Remove(id);
                    log.Warning("Traffic: {Id} is in the traffic tool's folder but not whole; left out", id);
                }
            }

            var toMake = cast.ToMake.ToList();
            var room = store.RoomFor();
            if (toMake.Count > room)
            {
                // The heaviest-weighted fit; the rest wait for room
                foreach (var (model, _, _) in toMake.Skip(room)) ready.Remove(model.Id);
                log.Warning("Traffic: room on the drive for {Room} of the {Count} model(s) to make ({Free} MB free); the rest left out",
                    room, toMake.Count, store.FreeSpace() / (1 << 20));
                toMake = toMake.Take(room).ToList();
            }

            if (toMake.Count > 0)
            {
                using var texels = new GpuTexels();
                var converter = new TrafficCarConverter(acRoot, texels);
                for (var i = 0; i < toMake.Count; i++)
                {
                    var (model, recipe, car) = toMake[i];
                    cancel.ThrowIfCancellationRequested();
                    progress?.Report(new TrafficProgress(i, toMake.Count, recipe.Name ?? model.Id));
                    var started = DateTime.UtcNow;
                    try
                    {
                        var result = converter.Convert(recipe, store.Root, recipe.MaxTexture ?? detail.TextureSide, cancel);
                        if (result.Definition == null)
                        {
                            ready.Remove(model.Id);
                            log.Warning("Traffic: {Id} not made from {Car}: {Problem}", model.Id, Path.GetFileName(car), result.Problem);
                        }
                        else
                        {
                            log.Information("Traffic: {Id} made from {Car} in {Seconds:0.0} s, {MB:0.0} MB; {Notes}", model.Id, result.SourceCar,
                                (DateTime.UtcNow - started).TotalSeconds, result.Bytes / 1e6, string.Join("; ", result.Notes));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // A car the converter cannot read, a disk that filled up: that model is left out, the rest go
                        ready.Remove(model.Id);
                        log.Warning("Traffic: {Id} could not be made from {Car}: {Error}", model.Id, Path.GetFileName(car), ex.Message);
                    }
                }
                progress?.Report(new TrafficProgress(toMake.Count, toMake.Count, null));
            }

            var models = cast.Models.Where(m => ready.Contains(m.Id) && store.Has(m.Id)).ToList();
            return new RaceTraffic
            {
                // The detail's share on top of the player's density, still within what the mode takes
                Cars = Math.Clamp((int)Math.Round(Cars(road, perLaneKm, density) * detail.DensityFactor), LeastCars, MostCars),
                // None made at all: the mode takes whatever the traffic tool has, as it would without the game
                Cast = models.Count == 0 ? null : TrafficFleets.Spec(models),
                Models = models.Count == 0 ? null : store.Root,
                Plan = plan?.ForRace,
                Density = double.IsFinite(density) ? Math.Clamp(density, LeastDensity, MostDensity) : 1
            };
        }

        /// <summary>No decoding: for the cast, which only asks which cars are installed</summary>
        private sealed class NoTexels : ITexels
        {
            public static readonly NoTexels Instance = new();
            public (int Width, int Height, byte[] Rgba)? Read(byte[] bytes, int most) => null;
        }
    }
}
