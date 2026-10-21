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
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Parts);
        private readonly Lazy<PartsCatalog> _catalog;
        private readonly Lazy<EngineBuildIndex> _builds;

        /// <summary>The cars told of once as having no factory engine: they are asked again on every visit, until a mod brings one</summary>
        private readonly ConcurrentDictionary<string, byte> _noEngineTold = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lazy<SoundLibrary> _sounds;
        private readonly Lazy<EngineCooling> _cooling;
        private readonly PriceBookStore _priceBook;
        private readonly ConcurrentDictionary<string, RatedBuild?> _suggestedBuilds = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _assignLock = new();
        // A car's folder is asked for in whatever case the caller has it (Kept lowers its own key)
        private readonly ConcurrentDictionary<string, AcCarSpecs?> _specs = new(StringComparer.OrdinalIgnoreCase);

        // The parts of a car's factory build on its block, for Shown: a lot page shows car after car, and each would read
        // the catalog twice and gather the build again (ForgetFactoryBuilds when the catalog editor changes one)
        private readonly ConcurrentDictionary<(string Car, string Block), IReadOnlySet<string>?> _factoryParts = new();

        // A model's factory running gear and rear axle: deterministic for its data and its book's picks, and asked on
        // every refresh of the workbench and the parts view (each a few scans of the catalog)
        private readonly ConcurrentDictionary<(CarBook Book, string Car), (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)?> _factoryGear = new();
        private readonly ConcurrentDictionary<(CarBook Book, string Car), RearAxleChoice?> _factoryAxles = new();

        // Whether a race has the car's data changed right now (CarDataOverlay): what is read then is not the car's own
        private readonly Func<string, bool> _isCarDataApplied;

        // The faults in an engine's figures are told once each per car: the same engine is evaluated again on every
        // screen that shows it, and the same faulting part on another car is another car to name
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
            _catalog = new Lazy<PartsCatalog>(LoadCatalog);
            _builds = new Lazy<EngineBuildIndex>(CreateIndex);
            _sounds = new Lazy<SoundLibrary>(LoadSounds);
            _cooling = new Lazy<EngineCooling>(() => EngineCooling.Create(Catalog, Builds));
            _priceBook = new PriceBookStore(() => Mods.ModSet.Installed.ContentOf(Mods.ContentTypes.Parts).SelectMany(f => new[] { f.Dir, f.Mod.ManifestPath }),
                kept => IsAvailable ? PartPriceBook.Create(Catalog, Builds, kept) : null, PriceBookPath);
            // The process-wide prices (PartPricing.Book and the rest) are pointed here once, where the app is composed:
            // PartsWiring, not this constructor, so a second service never takes them over
        }

        /// <summary>Every part measured on the dyno, once a session (<see cref="PriceBookStore"/>): several seconds the first time</summary>
        public PartPriceBook PriceBook => _priceBook.Book;

        /// <summary>
        /// The book for a price asked now: <see cref="PriceBook"/> when the caller <paramref name="mayWait"/> for it to be
        /// made, else the book once it is made and null (every part at its kind's base price) until then. The UI thread never
        /// waits: the warm-up makes the book behind it.
        /// </summary>
        internal PartPriceBook? PriceBookFor(bool mayWait) => mayWait ? _priceBook.Book : _priceBook.IfMade;

        /// <summary>The catalog once it is loaded, null before (a machine shop's cuts change no worth till then)</summary>
        internal PartsCatalog? CatalogIfLoaded => _catalog.IsValueCreated ? _catalog.Value : null;

        /// <summary>Where the measuring of the parts on the dyno is kept, with the fingerprint of the parts it was made from</summary>
        private static string PriceBookPath => Path.Combine(AppSettings.AppDataPath, "part_power.json");

        public PartsCatalog Catalog => _catalog.Value;

        public EngineBuildIndex Builds => _builds.Value;

        public bool IsAvailable => Catalog.Parts.Count > 0 && Builds.Runnable.Count > 0;

        public Task WarmUpAsync() => Task.Run(() =>
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.Warning("No parts in the mods (base game: {Path}): cars go without part trees", AppSettings.Instance.PartsPath);
                    return;
                }

                SuggestStockEngines();

                // Every part's price, before the first shop or lot asks for one
                _ = PriceBook;

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

        /// <summary>Whether the car still lacks what it left the factory with, or its running gear is from before <see cref="RunningGearFactory.GearVersion"/></summary>
        private static bool Lacks(Car car) =>
            !car.HasPartsAssigned || !car.HasRunningGearAssigned || car.GearVersion < RunningGearFactory.GearVersion;

        public bool EnsureParts(Car car)
        {
            if (!Lacks(car) || !IsAvailable) return false;

            var engine = car.HasPartsAssigned ? null : CreateFactoryEngine(car.DefinitionId, car.EngineHealth);
            var gear = car.HasRunningGearAssigned ? null : CreateFactoryRunningGear(car.DefinitionId, car.TireCondition);
            var renewal = car.HasRunningGearAssigned && car.GearVersion < RunningGearFactory.GearVersion ? CreateRenewal(car.DefinitionId, car.TireCondition) : null;
            return Assign(car, engine, gear, renewal);
        }

        public async Task<bool> EnsurePartsAsync(Car car)
        {
            if (!Lacks(car)) return false;

            // The parts are put together away from the caller's thread; the car is only touched back on it
            var definitionId = car.DefinitionId;
            var (needsEngine, needsGear) = (!car.HasPartsAssigned, !car.HasRunningGearAssigned);
            var needsRenewal = car.HasRunningGearAssigned && car.GearVersion < RunningGearFactory.GearVersion;
            var (engineCondition, tyreCondition) = (car.EngineHealth, car.TireCondition);
            var (engine, gear, renewal) = await Task.Run(() => IsAvailable
                ? (needsEngine ? CreateFactoryEngine(definitionId, engineCondition) : null, needsGear ? CreateFactoryRunningGear(definitionId, tyreCondition) : null,
                    needsRenewal ? CreateRenewal(definitionId, tyreCondition) : null)
                : (null, null, null));
            return Assign(car, engine, gear, renewal);
        }

        public async Task<int> EnsurePartsAsync(IEnumerable<Car> cars)
        {
            // What each car lacks, read on the caller's thread; the parts of all of them put together on one worker
            var wanted = cars.Where(Lacks)
                .Select(car => (Car: car, NeedsEngine: !car.HasPartsAssigned, NeedsGear: !car.HasRunningGearAssigned,
                    NeedsRenewal: car.HasRunningGearAssigned && car.GearVersion < RunningGearFactory.GearVersion,
                    EngineCondition: car.EngineHealth, TyreCondition: car.TireCondition))
                .ToList();
            if (wanted.Count == 0) return 0;

            var made = await Task.Run(() => wanted.Select(w =>
            {
                try
                {
                    return IsAvailable
                        ? (Engine: w.NeedsEngine ? CreateFactoryEngine(w.Car.DefinitionId, w.EngineCondition) : null,
                            Gear: w.NeedsGear ? CreateFactoryRunningGear(w.Car.DefinitionId, w.TyreCondition) : null,
                            Renewal: w.NeedsRenewal ? CreateRenewal(w.Car.DefinitionId, w.TyreCondition) : null)
                        : (null, null, null);
                }
                catch (Exception ex)
                {
                    // One car whose parts cannot be put together is not every car
                    _logger.Warning("Could not put together the parts of {Car}: {Error}", w.Car.DefinitionId, ex.Message);
                    return (null, null, null);
                }
            }).ToList());

            var changed = 0;
            for (var i = 0; i < wanted.Count; i++)
            {
                if (Assign(wanted[i].Car, made[i].Engine, made[i].Gear, made[i].Renewal)) changed++;
            }

            return changed;
        }

        /// <remarks>
        /// Never throws for what the content does: figures that break the sum, or anything else the parts hold, is an
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

            if (report != null) TellFaults(car.DefinitionId, report.Faults);
            return report;
        }

        private void TellFaults(string carId, IReadOnlyList<string> faults)
        {
            foreach (var fault in faults)
            {
                if (_toldFaults.TryAdd((carId, fault), 0)) _logger.Warning("{Car}: its engine could not be worked out: {Fault}", carId, fault);
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

        public (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear(Car car) => FactoryRunningGear(car.DefinitionId);

        private (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear(string carDefinitionId) =>
            Kept(_factoryGear, carDefinitionId, () => RunningGearFor(carDefinitionId, PicksOf(carDefinitionId)));

        public RearAxleChoice? FactoryAxle(string carDefinitionId) => Kept(_factoryAxles, carDefinitionId, () => AxleFor(carDefinitionId, PicksOf(carDefinitionId)));

        /// <summary>
        /// What is worked out for a model once it is known for the session: with the catalog there, and the car's own
        /// specs kept (<see cref="Specs"/>: not those of a race's data)
        /// </summary>
        private T Kept<T>(ConcurrentDictionary<(CarBook Book, string Car), T> cache, string carDefinitionId, Func<T> work)
        {
            var key = (CarBook.Current, carDefinitionId.ToLowerInvariant());
            if (cache.TryGetValue(key, out var kept)) return kept;

            var value = work();
            if (IsAvailable && _specs.ContainsKey(carDefinitionId)) cache.TryAdd(key, value);
            return value;
        }

        public (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? RunningGearFor(string carDefinitionId, RunningGearPicks? picks) =>
            IsAvailable && Specs(carDefinitionId) is { } specs ? RunningGearFactory.Choose(Catalog, specs, FactsOf(carDefinitionId), picks) : null;

        public RearAxleChoice? AxleFor(string carDefinitionId, RunningGearPicks? picks) =>
            IsAvailable && Specs(carDefinitionId) is { } specs ? RunningGearGuess.ChooseAxle(Catalog, specs, FactsOf(carDefinitionId), picks) : null;

        public CarFacts FactsOf(string carDefinitionId)
        {
            var definition = _catalogRepo.GetCar(carDefinitionId);
            var book = CarBook.Current;
            var make = book.MakeOf(carDefinitionId, definition?.Brand, definition?.Name);
            var country = book.CountryOfMake(make) ?? (definition?.Country is { Length: > 0 } named ? book.Country(named) : null);
            return new CarFacts(make, book.FamilyOfCar(carDefinitionId, definition?.Brand, definition?.Name), definition?.Year, country);
        }

        /// <summary>What the mods' cars.json picks for the car's running gear</summary>
        private static RunningGearPicks? PicksOf(string carDefinitionId) => CarBook.Current.Car(carDefinitionId)?.RunningGear;

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

        public ShownSet? Shown(string carDefinitionId, List<PartInstance> parts, CarLook? look = null, CornerHeights? heights = null, PaintColour? paint = null)
        {
            var directory = Path.Combine(AppSettings.Instance.CarsPath, carDefinitionId);
            if (look is { IsClean: true }) look = null;
            // Without the parts the car still shows its wear, its paint and how it sits (a sagging corner on a clean car too)
            if (!IsAvailable) return ShownSet.Of(null, null, look == null ? null : CarHubs.FrameOf(directory), look, heights, paint);

            // The engine's upper parts, any of which may stand out of the hood: whether they do is for whoever has the car's meshes
            var extras = new List<ShownModelAt>();
            if (parts.FirstOrDefault(p => p.ParentSlot == PartInstance.CarEngineSlot) is { } engine && PartTrees.ToInstalled(Catalog, engine) is { } tree)
            {
                var hubs = CarHubs.Of(directory);
                extras = ShownEngineParts.Of(Catalog, tree.Root, hubs == null ? null : hubs with { EngineOffset = EngineOffsetOf(carDefinitionId) },
                    EngineLayoutOf(carDefinitionId), FactoryEngineParts(carDefinitionId, tree.Root.Definition.Id));
            }

            // A corner with its wheel off shows its brake and suspension in the wheel's place
            var missing = ShownParts.MissingWheels(parts);
            if (missing != 0) extras.AddRange(ShownCornerParts.Of(Catalog, parts, CarHubs.Of(directory), missing));

            // The model's space, for the parts that stand out and the wear layer: worked out once, when either needs it
            var frame = look != null || extras.Count > 0 ? CarHubs.FrameOf(directory) : null;
            var wheels = Specs(carDefinitionId) != null
                ? ShownParts.Wheels(parts, Catalog, FactoryRunningGear(carDefinitionId))
                : new ShownWheel?[RunningGear.Corners];
            return ShownSet.Of(wheels, extras, frame, look, heights, paint, missing);
        }

        /// <summary>
        /// The parts of the car's factory build, by their catalog ids, when its engine is on the factory's block (the car's
        /// model has those); null for a car with another engine, or no known factory build
        /// </summary>
        private IReadOnlySet<string>? FactoryEngineParts(string carDefinitionId, string blockId) =>
            _factoryParts.GetOrAdd((carDefinitionId, blockId), key => ReadFactoryEngineParts(key.Car, key.Block));

        private IReadOnlySet<string>? ReadFactoryEngineParts(string carDefinitionId, string blockId)
        {
            if (_catalogRepo.GetCar(carDefinitionId) is not { } definition || GetStockBuild(definition) is not { } stock) return null;
            var parts = stock.Build.Parts.Select(id => Catalog.Get(id)?.Id).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return parts.Contains(blockId) ? parts : null;
        }

        public void ForgetFactoryBuilds()
        {
            _factoryParts.Clear();
            _factoryGear.Clear();
            _factoryAxles.Clear();
        }

        public bool BringUpToDate(GameState game) => IsAvailable && new SavedPartsMigration(Catalog, _logger).BringUpToDate(game);

        public BuiltEngine? CreateUsedEngine(CarDefinition car, double condition)
        {
            var build = GetStockBuild(car);
            if (build == null) return null;

            // A car nobody has touched is still not a new car (market.usedCarTuneChance, usedCarMinTuneLevel)
            var market = Rules.RuleBook.Current.Market;
            var tuneLevel = Random.Shared.NextDouble() < market.UsedCarTuneChance
                ? market.UsedCarMinTuneLevel + (1 - market.UsedCarMinTuneLevel) * Random.Shared.NextDouble()
                : 0;
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

        public List<PartInstance>? CreateTunedRunningGear(string carDefinitionId, double condition, double powerHp, Random random)
        {
            if (!IsAvailable || CreateFactoryRunningGear(carDefinitionId, condition) is not { } gear) return null;

            // Worn like the axle it goes in: tired, not gone
            var fitted = AxleTuner.TuneUsed(Catalog, gear, powerHp, EngineRules.Now.Tuner.UsedAxleChance, 0.4 + 0.6 * Math.Clamp(condition, 0, 1), random);
            if (fitted.Count > 0) _logger.Information("A worked-on {Car} for sale has {Parts} in its rear axle", carDefinitionId, string.Join(", ", fitted.Select(p => p.Id)));
            return gear;
        }

        private List<PartInstance>? CreateFactoryRunningGear(string carDefinitionId, double condition)
        {
            var specs = Specs(carDefinitionId);
            var gear = specs == null ? null : RunningGearFactory.Create(Catalog, specs, condition, specs.DrivesRear ? FactoryAxle(carDefinitionId) : null,
                FactsOf(carDefinitionId), PicksOf(carDefinitionId));
            if (gear is not { Count: > 0 }) _logger.Warning("No factory running gear for {Car}", carDefinitionId);
            return gear is { Count: > 0 } ? gear : null;
        }

        /// <summary>
        /// What a car of an older save is brought up to <see cref="RunningGearFactory.GearVersion"/> with: its factory rear axle
        /// (none for a car whose rear wheels are not driven: the gearbox has its differential), and what the factory's brakes
        /// and springs are now. Null when it cannot be told yet (no data), so it is asked again; a car whose axle is not in the
        /// catalog goes without one, as a new one does.
        /// </summary>
        private GearRenewal? CreateRenewal(string carDefinitionId, double condition)
        {
            if (Specs(carDefinitionId) is not { } specs) return null;

            PartInstance? axle = null;
            if (specs.DrivesRear)
            {
                if (FactoryAxle(carDefinitionId) is { } choice) axle = RunningGearFactory.CreateAxle(choice, condition);
                else _logger.Warning("No factory rear axle for {Car}", carDefinitionId);
            }

            return new GearRenewal(axle, specs, FactsOf(carDefinitionId), PicksOf(carDefinitionId));
        }

        private sealed record GearRenewal(PartInstance? Axle, AcCarSpecs Specs, CarFacts Facts, RunningGearPicks? Picks);

        private BuiltEngine? CreateFactoryEngine(string carDefinitionId, double condition)
        {
            var definition = _catalogRepo.GetCar(carDefinitionId);
            var build = definition == null ? null : GetStockBuild(definition);
            var engine = build == null ? null : EngineFactory.CreateStock(Catalog, build, condition, Random.Shared);
            if (engine == null && _noEngineTold.TryAdd(carDefinitionId, 0))
                _logger.Warning("No factory engine for {Car}: no build of its family is installed, or its build does not go together; it races on its own data until one does", carDefinitionId);
            return engine;
        }

        /// <summary>Two screens may ask for the same car at the same moment: only the first engine goes in</summary>
        private bool Assign(Car car, BuiltEngine? engine, List<PartInstance>? gear, GearRenewal? renewal)
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
                    car.GearVersion = RunningGearFactory.GearVersion;
                    changed = true;
                    _logger.Information("{Car} given its factory running gear: {Count} parts", car.DefinitionId, gear.Count);
                }

                // An older car's running gear brought up to now, once: the axle, and its factory brakes and springs as they are now
                if (renewal != null && car.HasRunningGearAssigned && car.GearVersion < RunningGearFactory.GearVersion)
                {
                    // The axle first: the rear brakes, springs and shocks go onto it
                    // (only for a car from before it: one the player took off since stays off)
                    if (renewal.Axle is { } axle && !car.HasAxleAssigned && car.Parts.All(p => p.ParentSlot != RearAxle.CarSlot)) car.Parts.Add(axle);
                    var renewed = RunningGearFactory.BringUpToDate(Catalog, renewal.Specs, renewal.Facts, renewal.Picks, car.Parts, car.GearVersion);
                    car.GearVersion = RunningGearFactory.GearVersion;
                    changed = true;
                    _logger.Information("{Car} brought up to running gear {Version}: {Renewed} factory part(s) of today's, rear axle {Housing}", car.DefinitionId,
                        RunningGearFactory.GearVersion, renewed, renewal.Axle?.DefinitionId ?? "none");
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
                var catalog = PartsCatalog.Load(Mods.ModSet.Installed.ContentOf(Mods.ContentTypes.Parts).ToList());
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
                var library = SoundLibrary.Load(Mods.ModSet.Installed.ContentOf(Mods.ContentTypes.Sounds).ToList(), id => Catalog.Get(id) != null, Catalog.CurrentId);
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
                foreach (var fault in index.Faults) _logger.Warning("Engine builds: a build could not be worked out and was left out: {Fault}", fault);
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
                // A build since renamed goes by its new id, picked by hand or not: the editor finds it among its options
                if (profile.StockEngineBuildId is { Length: > 0 } stockId && Catalog.CurrentBuildId(stockId) is var currentId && currentId != stockId)
                {
                    _profileRepo.UpdateProfile(profile.CarDefinitionId, stored =>
                    {
                        if (stored.StockEngineBuildId != stockId) return null;

                        stored.StockEngineBuildId = currentId;
                        return stored;
                    });
                    profile.StockEngineBuildId = currentId;
                }

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
