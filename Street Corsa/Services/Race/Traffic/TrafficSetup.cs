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
    /// A road offered for a drive in traffic that has none after all: no plan of its own or a mod's, and none could be
    /// built from its AI line (not the version read, no widths). The drive does not go: an empty road would be scored,
    /// tabled and banked as a run in traffic.
    /// </summary>
    public sealed class NoTrafficPlanException(string trackId, string? layout)
        : Exception($"No traffic plan for {trackId}/{layout ?? "(none)"}: the road has none and none could be built from its AI line");

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
        /// the rules' <see cref="Rules.RaceRules.TrafficLeastModels"/> can be had; the heaviest-weighted first, as many as the detail allows.
        /// Which need making (none there, or out of date) and which are left out and why. A model whose car would not
        /// make it before (and has not changed since), or that is in <paramref name="skip"/> (found wanting on this
        /// drive), takes none of the detail's places: the next of the fleet, or the base fleet, has it.
        /// </summary>
        /// <param name="modelsOf">A car's model files (<see cref="TrafficCarConverter.ModelsOf"/>); a drive that draws the
        /// cast several times gives one that remembers them</param>
        public static Cast CastFor(string trackId, string? layout, TrafficFleets fleets, TrafficCarConverter converter, TrafficModelStore store,
            string toolFolder, TrafficDetail detail, IReadOnlySet<string>? skip = null, Func<string, TrafficCarConverter.Models>? modelsOf = null)
        {
            modelsOf ??= car => TrafficCarConverter.ModelsOf(car);
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
                    // Left out on this drive already, and said so then
                    if (skip?.Contains(model.Id) == true) continue;
                    if (chosen.Count >= detail.MostModels)
                    {
                        left.Add($"{model.Id}: past the Traffic detail's {detail.MostModels} models");
                        continue;
                    }
                    if (fleets.Cars.TryGetValue(model.Id, out var recipe))
                    {
                        if (converter.SourceOf(recipe) is not { } car)
                        {
                            // Made before from a car since taken out: the model is still good. Else one of the
                            // player's own in the traffic tool's folder, taken as it is
                            if (store.Has(model.Id))
                            {
                                chosen.Add(model);
                            }
                            else if (File.Exists(Path.Combine(toolFolder, model.Id + ".json")))
                            {
                                chosen.Add(model);
                                adopt.Add(model.Id);
                            }
                            else
                            {
                                left.Add($"{model.Id}: none of {string.Join(", ", recipe.Cars)} is installed");
                            }
                            continue;
                        }
                        var files = modelsOf(car);
                        var side = recipe.MaxTexture ?? detail.TextureSide;
                        if (store.IsFresh(model.Id, car, files, side, recipe))
                        {
                            chosen.Add(model);
                        }
                        else if (store.WhyFailed(model.Id, car, files, side, recipe) is { } why)
                        {
                            left.Add($"{model.Id}: {Path.GetFileName(car)} would not make it before ({why})");
                        }
                        else
                        {
                            chosen.Add(model);
                            toMake.Add((model, recipe, car));
                        }
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
            if (chosen.Count < Rules.RuleBook.Current.Race.TrafficLeastModels) Take(fleets.BaseModels);
            return new Cast(chosen, toMake, adopt, left);
        }

        /// <summary>
        /// The traffic of a run on a road, made ready: the plan found or built, the count, the models converted where
        /// they are missing (the progress told, the free space checked first: what does not fit is left out, the
        /// heaviest-weighted kept). A model that will not convert is left out and the rest go. Cancelling stops after
        /// the model being made and throws; every model made so far is kept for next time.
        /// </summary>
        /// <exception cref="NoTrafficPlanException">The road has no plan and none could be built: there is no traffic to drive in</exception>
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
            else throw new NoTrafficPlanException(trackId, layout);

            var toolFolder = ModelsFolder(acRoot);
            var looker = new TrafficCarConverter(acRoot, NoTexels.Instance);
            // What this drive found wanting (a model that would not convert, one not whole, one there is no room for):
            // the cast is drawn again without it, so the next of the fleet, or the base fleet, takes its place
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var adopted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // A car's model files are looked up once a drive: the cast is drawn again after every model left out
            var known = new Dictionary<string, TrafficCarConverter.Models>(StringComparer.OrdinalIgnoreCase);
            TrafficCarConverter.Models ModelsOf(string car) => known.TryGetValue(car, out var files) ? files : known[car] = TrafficCarConverter.ModelsOf(car);
            Cast cast;
            GpuTexels? texels = null;
            try
            {
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    cast = CastFor(trackId, layout, fleets, looker, store, toolFolder, detail, skip, ModelsOf);
                    var wanting = skip.Count;

                    foreach (var id in cast.Adopt.Where(id => !adopted.Contains(id)))
                    {
                        try
                        {
                            if (store.Adopt(id, toolFolder))
                            {
                                adopted.Add(id);
                            }
                            else
                            {
                                skip.Add(id);
                                log.Warning("Traffic: {Id} is in the traffic tool's folder but not whole, or names a file outside it; left out", id);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // A full disk, a file held by another program: that model is left out and the rest go
                            skip.Add(id);
                            log.Warning("Traffic: {Id} could not be brought in from the traffic tool's folder: {Error}; left out", id, ex.Message);
                        }
                    }

                    var toMake = cast.ToMake.ToList();
                    var room = store.RoomFor();
                    if (toMake.Count > room)
                    {
                        // The heaviest-weighted fit; the rest wait for room
                        foreach (var (model, _, _) in toMake.Skip(room)) skip.Add(model.Id);
                        log.Warning("Traffic: room on the drive for {Room} of the {Count} model(s) to make ({Free} MB free); the rest left out",
                            room, toMake.Count, store.FreeSpace() / (1 << 20));
                        toMake = toMake.Take(room).ToList();
                    }

                    if (toMake.Count > 0)
                    {
                        texels ??= new GpuTexels();
                        var converter = new TrafficCarConverter(acRoot, texels);
                        for (var i = 0; i < toMake.Count; i++)
                        {
                            var (model, recipe, car) = toMake[i];
                            cancel.ThrowIfCancellationRequested();
                            progress?.Report(new TrafficProgress(i, toMake.Count, recipe.Name ?? model.Id));
                            var started = DateTime.UtcNow;
                            var side = recipe.MaxTexture ?? detail.TextureSide;
                            try
                            {
                                var result = converter.Convert(recipe, store.Root, side, cancel);
                                if (result.Definition == null)
                                {
                                    // The car's own lack (no LODs, encrypted): remembered, not tried drive after drive
                                    skip.Add(model.Id);
                                    store.MarkFailed(model.Id, car, ModelsOf(car), side, recipe, result.Problem);
                                    log.Warning("Traffic: {Id} not made from {Car}: {Problem}", model.Id, Path.GetFileName(car), result.Problem);
                                }
                                else
                                {
                                    store.ForgetFailed(model.Id);
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
                                // A car the converter cannot read, a disk that filled up: that model is left out of this
                                // drive and the rest go; not remembered, as the next drive may fare better
                                skip.Add(model.Id);
                                log.Warning("Traffic: {Id} could not be made from {Car}: {Error}", model.Id, Path.GetFileName(car), ex.Message);
                            }
                        }
                        progress?.Report(new TrafficProgress(toMake.Count, toMake.Count, null));
                    }

                    // Nothing more found wanting: the cast stands
                    if (skip.Count == wanting) break;
                }
            }
            finally
            {
                texels?.Dispose();
            }
            foreach (var why in cast.Left) log.Information("Traffic: left out {Why}", why);

            var models = cast.Models.Where(m => store.Has(m.Id)).ToList();
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
