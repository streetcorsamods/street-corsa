using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Sounds;
using StreetCorsa.Parts.Logic;
using System.Collections.Concurrent;
using System.IO;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// Ties the cars of the game to the parts catalog: which engine a car leaves the factory with, the parts a
    /// car instance carries, and what they come to on the dyno
    /// </summary>
    public class CarPartsService : ICarPartsService
    {
        // A car nobody has touched is still not a new car
        private const double UsedCarTuneChance = 0.35;
        private const double MinTuneLevel = 0.3;

        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Parts);
        private readonly Lazy<PartsCatalog> _catalog;
        private readonly Lazy<EngineBuildIndex> _builds;
        private readonly Lazy<SoundLibrary> _sounds;
        private readonly Lazy<EngineCooling> _cooling;
        private readonly ConcurrentDictionary<string, RatedBuild?> _suggestedBuilds = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _assignLock = new();
        private readonly ConcurrentDictionary<string, AcCarSpecs?> _specs = new();

        // Whether a race has the car's data changed right now (CarDataOverlay): what is read then is not the car's own
        private readonly Func<string, bool> _isCarDataApplied;

        // Script faults are told once each per car: the same engine is evaluated again on every screen that shows
        // it, and the same faulting part on another car is another car to name
        private readonly ConcurrentDictionary<(string Car, string Fault), byte> _toldFaults = new();

        /// <param name="isCarDataApplied">
        /// Whether a car's data is changed for a race right now; by default what <see cref="CarDataOverlay.IsApplied"/>
        /// finds on disk
        /// </param>
        public CarPartsService(IContentCatalogRepository catalogRepo, ICarProfileRepository profileRepo, Func<string, bool>? isCarDataApplied = null)
        {
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            // A new overlay per ask: it reads the AC and restore paths from the settings when it is made, and those
            // may have changed since this service was
            _isCarDataApplied = isCarDataApplied ?? (id => new CarDataOverlay().IsApplied(id));
            // Every part price in the game, process-wide (see PartPricing.Source): the rules of the game in play
            PartPricing.Source = () =>
            {
                var economy = Rules.RuleBook.Current.Economy;
                return new PartPricing.Figures(economy.PartsPriceScale, economy.UsedShopFactor, economy.TradeInFactor);
            };
            // And the engines' figures (cooling, factory, tuners, the engine matcher): the same rules
            EngineRules.Source = () => Rules.RuleBook.Current.Engines;
            // But the engine matcher's go into catalog.db, the program's: the installed mods' rules
            EngineRules.ProgramSource = () => Rules.RuleBook.Installed.Engines;
            _catalog = new Lazy<PartsCatalog>(LoadCatalog);
            _builds = new Lazy<EngineBuildIndex>(CreateIndex);
            _sounds = new Lazy<SoundLibrary>(LoadSounds);
            _cooling = new Lazy<EngineCooling>(() => EngineCooling.Create(Catalog, Builds));
        }

        public PartsCatalog Catalog => _catalog.Value;

        public EngineBuildIndex Builds => _builds.Value;

        public bool IsAvailable => Catalog.Parts.Count > 0 && Builds.Runnable.Count > 0;

        public Task WarmUpAsync() => Task.Run(() =>
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.Warning("No converted parts in the mods (base game: {Path}): cars go without part trees", AppSettings.Instance.PartsPath);
                    return;
                }

                SuggestStockEngines();

                // Every installed car's bank is read for the library: here, not on the way to the first race
                _ = Sounds;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Parts warm-up failed");
            }
        });

        public RatedBuild? GetStockBuild(CarDefinition car)
        {
            if (!IsAvailable) return null;

            var profile = _profileRepo.GetProfile(car.Id);
            return Builds.Get(profile?.StockEngineBuildId) ?? Suggested(car);
        }

        /// <summary>The build the matcher picks for a car without a factory engine on its profile: weighed once a session (the matcher weighs every build)</summary>
        private RatedBuild? Suggested(CarDefinition car) => _suggestedBuilds.GetOrAdd(car.Id, _ => Suggest(car));

        public bool EnsureParts(Car car)
        {
            if (car.HasPartsAssigned && car.HasRunningGearAssigned) return false;
            if (!IsAvailable) return false;

            var engine = car.HasPartsAssigned ? null : CreateFactoryEngine(car.DefinitionId, car.EngineHealth);
            var gear = car.HasRunningGearAssigned ? null : CreateFactoryRunningGear(car.DefinitionId, car.TireCondition);
            return Assign(car, engine, gear);
        }

        public async Task<bool> EnsurePartsAsync(Car car)
        {
            if (car.HasPartsAssigned && car.HasRunningGearAssigned) return false;

            // The parts are put together away from the caller's thread; the car is only touched back on it
            var definitionId = car.DefinitionId;
            var (needsEngine, needsGear) = (!car.HasPartsAssigned, !car.HasRunningGearAssigned);
            var (engineCondition, tyreCondition) = (car.EngineHealth, car.TireCondition);
            var (engine, gear) = await Task.Run(() => IsAvailable
                ? (needsEngine ? CreateFactoryEngine(definitionId, engineCondition) : null, needsGear ? CreateFactoryRunningGear(definitionId, tyreCondition) : null)
                : (null, null));
            return Assign(car, engine, gear);
        }

        /// <remarks>
        /// Never throws for what the content does: a part script that faults, or anything else the parts hold, is an
        /// engine that does not run, with the reason as its problem and in the log
        /// </remarks>
        public EngineReport? Evaluate(Car car)
        {
            if (!IsAvailable || car.Engine is not { } engine) return null;

            EngineReport? report;
            try
            {
                report = EngineFactory.Evaluate(Catalog, engine);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "{Car}: its engine {Block} could not be evaluated", car.DefinitionId, engine.DefinitionId);
                return new EngineReport { Problem = GameText.Say("garage.engine.not_evaluated", ("error", ex.Message)) };
            }

            if (report != null) TellFaults(car.DefinitionId, report.ScriptFaults);
            return report;
        }

        private void TellFaults(string carId, IReadOnlyList<string> faults)
        {
            foreach (var fault in faults)
            {
                if (_toldFaults.TryAdd((carId, fault), 0)) _logger.Warning("{Car}: a part script faulted: {Fault}", carId, fault);
            }
        }

        public SoundLibrary Sounds => _sounds.Value;

        public CarSound? ChooseSound(Car car, EngineReport? report)
        {
            if (!IsAvailable || car.Engine is not { } engine || Sounds.All.Count == 0) return null;

            var request = SoundMatcher.RequestFor(Catalog, engine.DefinitionId, report);
            var sound = SoundMatcher.ForCar(Sounds, request, car.DefinitionId, out var choice);
            if (choice == null) return null;

            _logger.Information("{Car}: {Block} ({Cylinders} cyl, {Family}, {Limiter:0} rpm) sounds like {Sound}: {Reason}{Own}",
                car.DefinitionId, request.BlockId, request.Cylinders, request.Family, request.LimiterRpm, choice.Sound.Name, choice.Reason,
                sound == null ? " (its own bank)" : "");
            return sound;
        }

        public Models.Race.EngineCoolingRating? RateCooling(Car car, EngineReport? engine)
        {
            if (!IsAvailable || !car.HasPartsAssigned) return null;
            var rating = _cooling.Value.Rate(car.Parts.SelectMany(p => p.SelfAndDescendants()), engine?.Dyno?.MaxPowerHp ?? 0, FactoryCoolingOf(car));

            // A sump low on oil surges sooner
            return rating with { OilG = Math.Round(rating.OilG * CarCondition.SumpShare(car), 3) };
        }

        public Func<PartInstance, double, double>? EngineCoolingOf(Car car)
        {
            if (!IsAvailable) return null;
            var cooling = _cooling.Value;
            var factory = FactoryCoolingOf(car);
            return (engine, powerHp) => cooling.Rate(engine.SelfAndDescendants(), powerHp, factory).Cooling;
        }

        // What the car's factory engine was cooled with: its factory build (GetStockBuild)
        private FactoryCooling FactoryCoolingOf(Car car)
        {
            if (_catalogRepo.GetCar(car.DefinitionId) is not { } definition) return StreetCorsa.Parts.Cars.FactoryCooling.Unknown;
            return _cooling.Value.Factory(GetStockBuild(definition));
        }

        // The index weighed every build when it put them on the dyno
        public double? FactoryEngineMass(Car car)
        {
            var definition = _catalogRepo.GetCar(car.DefinitionId);
            return definition == null ? null : GetStockBuild(definition)?.MassKg;
        }

        /// <remarks>
        /// Kept for the session, except what is read while a race has the car's data changed: that is the race's
        /// data, not the car's, and kept it would be scaled again by the next race
        /// </remarks>
        public AcCarSpecs? Specs(string carDefinitionId)
        {
            if (_specs.TryGetValue(carDefinitionId, out var cached)) return cached;

            var appliedBefore = IsCarDataApplied(carDefinitionId);
            var specs = ReadSpecs(carDefinitionId);
            if (!appliedBefore && !IsCarDataApplied(carDefinitionId)) return _specs.GetOrAdd(carDefinitionId, specs);

            _logger.Information("{Car}: its data is changed for a race right now; its specs are read, not kept", carDefinitionId);
            return specs;
        }

        public EngineLayout EngineLayoutOf(string carDefinitionId)
        {
            try
            {
                if (_profileRepo.GetProfile(carDefinitionId)?.EngineLayout is { } set) return set;
            }
            catch (Exception ex)
            {
                // A catalog that can't be read now still leaves the guess: better than the last car's layout
                _logger.Warning("Could not read where {Car} was set to carry its engine: {Error}", carDefinitionId, ex.Message);
            }

            return EngineLayouts.Guess(Specs(carDefinitionId));
        }

        private AcCarSpecs? ReadSpecs(string id)
        {
            try
            {
                return AcCarSpecs.Read(AcCarDataReader.ForCar(Path.Combine(AppSettings.Instance.CarsPath, id)));
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not read the data of {Car}: {Error}", id, ex.Message);
                return null;
            }
        }

        private bool IsCarDataApplied(string carId)
        {
            try
            {
                return _isCarDataApplied(carId);
            }
            catch (Exception ex)
            {
                // Not knowing is not keeping: the next ask reads again
                _logger.Warning("Could not tell whether {Car} is changed for a race: {Error}", carId, ex.Message);
                return true;
            }
        }

        public (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear(Car car) =>
            IsAvailable && Specs(car.DefinitionId) is { } specs ? RunningGearFactory.Choose(Catalog, specs) : null;

        public System.Numerics.Vector3 EngineOffsetOf(string carDefinitionId) =>
            CarBook.Current.Car(carDefinitionId)?.EngineShift ?? System.Numerics.Vector3.Zero;

        // The offsets still to go into cars.json, by car: the latest of each, written by one worker at a time
        private readonly Dictionary<string, System.Numerics.Vector3> _offsetsToWrite = new(StringComparer.OrdinalIgnoreCase);
        private bool _writingOffsets;

        /// <remarks>Each arrow press (key repeat too) moves the engine: the file is written behind, with the latest offset</remarks>
        public void SetEngineOffset(string carDefinitionId, System.Numerics.Vector3 offset)
        {
            CarBook.Current.SetEngineOffset(carDefinitionId, offset);
            lock (_offsetsToWrite)
            {
                _offsetsToWrite[carDefinitionId] = offset;
                if (_writingOffsets) return;
                _writingOffsets = true;
            }
            _ = Task.Run(WriteOffsets);
        }

        private void WriteOffsets()
        {
            while (true)
            {
                KeyValuePair<string, System.Numerics.Vector3>[] offsets;
                lock (_offsetsToWrite)
                {
                    if (_offsetsToWrite.Count == 0)
                    {
                        _writingOffsets = false;
                        return;
                    }
                    offsets = _offsetsToWrite.ToArray();
                    _offsetsToWrite.Clear();
                }

                foreach (var (carId, offset) in offsets)
                {
                    try
                    {
                        BaseCarsFile.SaveEngineOffset(carId, offset);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // The car book has it for the session all the same
                        _logger.Warning("The engine of {Car} moved, but cars.json could not be written: {Error}", carId, ex.Message);
                    }
                }
            }
        }

        public ShownSet? Shown(string carDefinitionId, List<PartInstance> parts)
        {
            if (!IsAvailable) return null;

            // The engine's upper parts, any of which may stand out of the hood: whether they do is for whoever has the car's meshes
            var extras = new List<ShownModelAt>();
            ModelFrame? frame = null;
            if (parts.FirstOrDefault(p => p.ParentSlot == PartInstance.CarEngineSlot) is { } engine && PartTrees.ToInstalled(Catalog, engine) is { } tree)
            {
                var directory = Path.Combine(AppSettings.Instance.CarsPath, carDefinitionId);
                var hubs = CarHubs.Of(directory);
                extras = ShownEngineParts.Of(Catalog, tree.Root, hubs == null ? null : hubs with { EngineOffset = EngineOffsetOf(carDefinitionId) },
                    EngineLayoutOf(carDefinitionId), FactoryEngineParts(carDefinitionId, tree.Root.Definition.Id));
                frame = CarHubs.FrameOf(directory);
            }

            var wheels = Specs(carDefinitionId) is { } specs
                ? ShownParts.Wheels(parts, Catalog, RunningGearFactory.Choose(Catalog, specs))
                : new ShownWheel?[RunningGear.Corners];
            return ShownSet.Of(wheels, extras, frame);
        }

        /// <summary>
        /// The parts of the car's factory build, by their catalog ids, when its engine is on the factory's block (the car's
        /// model has those); null for a car with another engine, or no known factory build
        /// </summary>
        private IReadOnlySet<string>? FactoryEngineParts(string carDefinitionId, string blockId)
        {
            if (_catalogRepo.GetCar(carDefinitionId) is not { } definition || GetStockBuild(definition) is not { } stock) return null;
            var parts = stock.Build.Parts.Select(p => p.Part is { } id ? Catalog.Get(id)?.Id : null).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return parts.Contains(blockId) ? parts : null;
        }

        public bool BringUpToDate(GameState game)
        {
            if (!IsAvailable) return false;

            var changed = 0;

            // The engine on the stand as the engines in the cars: one of a mod that is not installed waits on the
            // shelf, one the catalog has lost for good is gone, and what no longer fits on it comes off to the shelf
            if (game.Player.EngineStand is { } stand)
            {
                if (Catalog.Get(stand.DefinitionId) == null)
                {
                    game.Player.EngineStand = null;
                    changed++;
                    if (Mods.ModIds.IsQualified(stand.DefinitionId)) game.Player.Parts.Add(stand);
                    _logger.Information("The {Block} on the engine stand is not in the catalog: {Where}", stand.DefinitionId,
                        Mods.ModIds.IsQualified(stand.DefinitionId) ? "it waits on the shelf for its mod" : "it is gone");
                }
                else
                {
                    var off = new List<PartInstance>();
                    changed += Renew(new[] { stand }, off);
                    game.Player.Parts.AddRange(off);
                }
            }

            var racers = new[] { game.Player }
                .Concat(game.Racers.Inactive.Values).Concat(game.Racers.Retired.Values).Concat(game.Racers.ReadyToRace.Values);
            foreach (var racer in racers)
            {
                // An engine the catalog no longer knows (its parts were left out of the content) is gone from the
                // car, which gets its factory engine the next time it is looked at, worn like the car. One of a mod
                // that is not installed goes on the owner's shelf whole: it is back in the game with the mod.
                var keep = new List<PartInstance>();
                foreach (var car in racer.Cars)
                {
                    if (car.Engine is not { } engine || Catalog.Get(engine.DefinitionId) != null) continue;

                    car.Parts.Remove(engine);
                    car.HasPartsAssigned = false;
                    changed++;
                    if (!Mods.ModIds.IsQualified(engine.DefinitionId))
                    {
                        _logger.Information("{Car} had a {Block} the catalog no longer has: gets its factory engine again", car.DefinitionId, engine.DefinitionId);
                        continue;
                    }

                    engine.ParentSlot = 0;
                    engine.OwnSlot = 0;
                    keep.Add(engine);
                    _logger.Information("{Car} had a {Block} of a mod that is not installed: it waits on the shelf, the car gets its factory engine",
                        car.DefinitionId, engine.DefinitionId);
                }

                // What comes off a car goes on its owner's shelf, once the shelf itself has been gone through. A mod's
                // part the catalog does not know stays there (the workbench does not show it) till the mod is back.
                var shelf = new List<PartInstance>();
                changed += racer.Parts.RemoveAll(part => Catalog.Get(part.DefinitionId) == null && !Mods.ModIds.IsQualified(part.DefinitionId));
                racer.Parts.AddRange(keep);
                changed += Renew(racer.Cars.SelectMany(c => c.Parts).Concat(racer.Parts), shelf);
                racer.Parts.AddRange(shelf);
            }

            // Cars and parts for sale: what no longer fits is not part of the offer
            changed += game.UsedCarMarket.RemoveAll(l => l.Parts.Any(p => Catalog.Get(p.DefinitionId) == null));
            changed += game.NewspaperAds.Parts.RemoveAll(ad => Catalog.Get(ad.Part.DefinitionId) == null);

            var forSale = game.UsedCarMarket.SelectMany(l => l.Parts)
                .Concat(game.NewspaperAds.Parts.Select(ad => ad.Part));
            changed += Renew(forSale, new List<PartInstance>());

            if (changed > 0) _logger.Information("{Count} change(s) to the save's parts to keep up with the parts catalog", changed);
            return changed > 0;
        }

        private int Renew(IEnumerable<PartInstance> roots, List<PartInstance> loose)
        {
            var changed = 0;
            foreach (var root in roots.ToList())
            {
                var before = loose.Count;
                if (!SavedParts.BringUpToDate(Catalog, root, loose)) continue;

                changed++;
                foreach (var part in loose.Skip(before)) _logger.Information("{Part} no longer fits on {Root} and came off", part.DefinitionId, root.DefinitionId);
            }

            return changed;
        }

        public BuiltEngine? CreateUsedEngine(CarDefinition car, double condition)
        {
            var build = GetStockBuild(car);
            if (build == null) return null;

            var tuneLevel = Random.Shared.NextDouble() < UsedCarTuneChance ? MinTuneLevel + (1 - MinTuneLevel) * Random.Shared.NextDouble() : 0;
            return tuneLevel > 0
                ? EngineFactory.CreateTuned(Catalog, Builds, build, condition, tuneLevel, Random.Shared)
                : EngineFactory.CreateStock(Catalog, build, condition, Random.Shared);
        }

        public string? Describe(PartInstance engine, EngineReport? report)
        {
            var block = Catalog.Get(engine.DefinitionId);
            if (block == null) return null;

            var name = ShortBlockName(block.DisplayName ?? block.Name);
            return report is { Runs: true }
                ? GameText.Say("garage.engine.summary", ("engine", name), ("hp", $"{report.Dyno!.MaxPowerHp:0}"))
                : GameText.Say("garage.engine.summary.not_running", ("engine", name));
        }

        /// <summary>
        /// A "Chrysler 426 Hemi V8 block" is a "Chrysler 426 Hemi V8": the part's kind at the end of the name goes.
        /// A "Chevy 350 small block" (a mod's) keeps it, that is what the engine is called.
        /// </summary>
        public static string ShortBlockName(string name)
        {
            foreach (var kind in new[] { " engine block", " block" })
            {
                if (!name.EndsWith(kind, StringComparison.OrdinalIgnoreCase)) continue;

                var shorter = name[..^kind.Length].TrimEnd();
                var lastWord = shorter[(shorter.LastIndexOf(' ') + 1)..];
                var belongsToName = lastWord.ToLowerInvariant() is "small" or "big" or "bare" or "short";
                return belongsToName || !shorter.Contains(' ') ? name : shorter;
            }

            return name;
        }

        private List<PartInstance>? CreateFactoryRunningGear(string carDefinitionId, double condition)
        {
            var specs = Specs(carDefinitionId);
            var gear = specs == null ? null : RunningGearFactory.Create(Catalog, specs, condition);
            if (gear is not { Count: > 0 }) _logger.Warning("No factory running gear for {Car}", carDefinitionId);
            return gear is { Count: > 0 } ? gear : null;
        }

        private BuiltEngine? CreateFactoryEngine(string carDefinitionId, double condition)
        {
            var definition = _catalogRepo.GetCar(carDefinitionId);
            var build = definition == null ? null : GetStockBuild(definition);
            var engine = build == null ? null : EngineFactory.CreateStock(Catalog, build, condition, Random.Shared);
            if (engine == null) _logger.Warning("No factory engine for {Car}", carDefinitionId);
            return engine;
        }

        /// <summary>Two screens may ask for the same car at the same moment: only the first engine goes in</summary>
        private bool Assign(Car car, BuiltEngine? engine, List<PartInstance>? gear)
        {
            var changed = false;
            lock (_assignLock)
            {
                if (engine != null && !car.HasPartsAssigned)
                {
                    car.Parts.Add(engine.Root);
                    car.HasPartsAssigned = true;
                    changed = true;
                    _logger.Information("{Car} given its factory engine: {Block}, {Power:0} hp", car.DefinitionId, engine.Root.DefinitionId,
                        engine.Report.Dyno?.MaxPowerHp ?? 0);
                }

                if (gear != null && !car.HasRunningGearAssigned)
                {
                    car.Parts.AddRange(gear);
                    car.HasRunningGearAssigned = true;
                    changed = true;
                    _logger.Information("{Car} given its factory running gear: {Count} parts", car.DefinitionId, gear.Count);
                }
            }

            return changed;
        }

        // A Lazy keeps the exception of a load that failed and throws it at everybody who asks afterwards.
        // Parts that cannot be read are parts that are not there: the game goes on without them.
        private PartsCatalog LoadCatalog()
        {
            try
            {
                var started = DateTime.Now;
                var catalog = PartsCatalog.Load(Mods.ModSet.Installed.Folders(Mods.ContentTypes.Parts).ToList());
                _logger.Information("Parts catalog: {Parts} parts, {Builds} engine builds in {Ms} ms", catalog.Parts.Count,
                    catalog.EngineBuilds.Count, (int)(DateTime.Now - started).TotalMilliseconds);
                foreach (var problem in catalog.Problems) _logger.Warning("Parts catalog: {Problem}", problem);
                Mods.ModProblems.Add("Parts catalog", catalog.Problems);
                return catalog;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not read the parts under {Path}: going without them", AppSettings.Instance.PartsPath);
                return PartsCatalog.Empty(AppSettings.Instance.PartsPath);
            }
        }

        // The library is the folder of sounds plus every installed car's own bank; a car's bank is tagged with the
        // engine the parts would give the car, so the harvest waits for the build index
        private SoundLibrary LoadSounds()
        {
            var settings = AppSettings.Instance;
            try
            {
                var started = DateTime.Now;
                var library = SoundLibrary.Load(Mods.ModSet.Installed.Folders(Mods.ContentTypes.Sounds).ToList(), id => Catalog.Get(id) != null);
                var cars = library.Harvest(settings.CarsPath, settings.SfxGuidsPath, SoundLibrary.StockFacts(Builds, Catalog));
                _logger.Information("Sound library: {Curated} sound(s) from the mods (base game: {Path}), {Harvested} more off {Cars} installed car(s), in {Ms} ms",
                    library.Curated.Count, settings.SoundsPath, library.Harvested.Count, cars, (int)(DateTime.Now - started).TotalMilliseconds);
                foreach (var problem in library.Problems) _logger.Warning("Sound library: {Problem}", problem);
                Mods.ModProblems.Add("Sound library", library.Problems);
                return library;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not read the sounds: cars race on their own");
                return SoundLibrary.Empty(settings.SoundsPath);
            }
        }

        private EngineBuildIndex CreateIndex()
        {
            try
            {
                var started = DateTime.Now;
                var index = EngineBuildIndex.Create(Catalog);
                _logger.Information("{Count} engine builds run, put on the dyno in {Ms} ms", index.Runnable.Count,
                    (int)(DateTime.Now - started).TotalMilliseconds);
                foreach (var fault in index.ScriptFaults) _logger.Warning("Engine builds: a part script faulted and was left out: {Fault}", fault);
                return index;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put the engine builds on the dyno: going without them");
                return EngineBuildIndex.Empty;
            }
        }

        private RatedBuild? Suggest(CarDefinition car) =>
            StockEngineMatcher.Best(Builds, car.Id, car.Brand, car.Name, StockEngineMatcher.ParsePower(car.Specs?.Bhp));

        /// <summary>
        /// Fills in the factory engine of every profile that has none, or whose build has left the catalog.
        /// An engine picked by hand is left alone even then: the car runs on the best match until somebody picks again.
        /// </summary>
        private void SuggestStockEngines()
        {
            foreach (var profile in _profileRepo.GetAllProfiles())
            {
                if (NeedsNoSuggestion(profile)) continue;

                var car = _catalogRepo.GetCar(profile.CarDefinitionId);
                var build = car == null ? null : Suggested(car);
                if (build == null) continue;

                // Onto the profile as it is in the database now, not as it was when the loop started: the
                // catalog editor may have saved it in the meantime
                var suggested = _profileRepo.UpdateProfile(profile.CarDefinitionId, stored =>
                {
                    if (NeedsNoSuggestion(stored)) return null;

                    stored.StockEngineBuildId = build.Build.Id;
                    stored.LastUpdatedDate = DateTime.Now;
                    return stored;
                });

                if (suggested) _logger.Information("Factory engine of {Car}: {Build} ({Power:0} hp)", profile.CarDefinitionId, build.Build.Name, build.PowerHp);
            }
        }

        private bool NeedsNoSuggestion(CarProfile profile) =>
            profile.StockEngineIsManual || Builds.Get(profile.StockEngineBuildId) != null;
    }
}
