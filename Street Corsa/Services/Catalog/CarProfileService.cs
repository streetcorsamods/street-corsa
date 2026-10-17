using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// Works out every car's profile: what it costs new (<see cref="CarPricing"/>: its body by its weight, its factory
    /// engine and running gear at their parts' prices) and how common it is
    /// </summary>
    public class CarProfileService : ICarProfileService
    {
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly ICarPartsService? _parts;
        private readonly IAppLogger _logger;

        /// <summary>
        /// Goes into a Generated profile's DefinitionHash with the car's own hash. Moved on when the way specs are
        /// read for how common a car is changes (2: lb and kW converted, instead of read as kg and hp; 3: hp per tonne;
        /// 4: the figures from the rules and the badge from the makes; 5: prices no longer by the hash, every start),
        /// so every generated profile is worked out again once.
        /// </summary>
        private const string SpecsReading = "specs5";

        /// <summary>The pricing figures in play: the mods' rules, not a career's (the catalog is the program's)</summary>
        private static Rules.CarRules Figures => Rules.RuleBook.Installed.Cars;

        /// <summary>
        /// The car's own hash, the way specs are read, and what the price was worked out by: the rules' figures and the
        /// car's make with its badge's figures. A mod that changes any of them has the price worked out again.
        /// </summary>
        private static string DefinitionHashOf(CarDefinition car, string? make)
        {
            var badge = CarBook.Current.Make(make);
            var by = FormattableString.Invariant($"{FiguresDigest}|{make}|{badge?.Common}");
            return $"{car.ContentHash}|{SpecsReading}|{Digest(by)}";
        }

        // One reference, so a thread never reads half of another's write
        private sealed record DigestOf(Rules.CarRules Figures, string Digest);

        private static DigestOf? _figuresDigest;

        /// <summary>The figures as text, worked out once for each set of them (they do not change while the game runs)</summary>
        private static string FiguresDigest
        {
            get
            {
                var figures = Figures;
                if (_figuresDigest is { } known && ReferenceEquals(known.Figures, figures)) return known.Digest;
                var digest = System.Text.Json.JsonSerializer.Serialize(figures, Rules.RuleBook.Options);
                _figuresDigest = new DigestOf(figures, digest);
                return digest;
            }
        }

        private static string Digest(string text) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];

        /// <summary>The car's make, as the mods' makes tell it (<see cref="CarBook.MakeOf"/>)</summary>
        private static string? MakeOf(CarDefinition car) => CarBook.Current.MakeOf(car.Id, car.Brand, car.Name);

        /// <param name="parts">
        /// The parts, to price a car by its factory engine and running gear; null, or no parts: a car is priced by its body
        /// </param>
        public CarProfileService(IContentCatalogRepository catalogRepo, ICarProfileRepository profileRepo, ICarPartsService? parts = null)
        {
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _parts = parts;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);
            // The body's price, process-wide like the parts' (see CarPricing.Source): the catalog's figures, the mods' rules
            CarPricing.Source = () =>
            {
                var cars = Figures;
                return new CarPricing.Figures(cars.BodyPrice, cars.BodyKg, cars.BodyWeightPower, cars.MinBodyPrice, cars.MaxBodyPrice,
                    cars.MinPlausibleWeightKg, cars.MaxPlausibleWeightKg);
            };
        }

        public CarProfile GenerateDefaultProfile(CarDefinition carDefinition)
        {
            var make = MakeOf(carDefinition);
            return Generate(carDefinition, make, DefinitionHashOf(carDefinition, make));
        }

        private CarProfile Generate(CarDefinition carDefinition, string? make, string hash)
        {
            var price = PriceNew(carDefinition);
            var precedence = Precedence(carDefinition, make);

            _logger.Debug("Generated profile for {CarId}: BasePrice=${Price}, Precedence={Precedence}",
                carDefinition.Id, price.BasePrice, precedence);

            var profile = new CarProfile
            {
                CarDefinitionId = carDefinition.Id,
                DealerPrecedence = precedence,
                Source = ProfileDataSource.Generated,
                DefinitionHash = hash,
                CreatedDate = DateTime.Now,
                LastUpdatedDate = DateTime.Now,
                IsStreetLegal = true // Default assumption
            };
            price.Into(profile);
            return profile;
        }

        public decimal CalculateBasePrice(CarDefinition carDefinition) => PriceNew(carDefinition).BasePrice;

        /// <summary>What a car costs new, in the profile's figures: the whole of it rounded and held to the rules' least and most</summary>
        public readonly record struct NewPrice(decimal BasePrice, decimal Body, decimal Engine, decimal RunningGear)
        {
            /// <summary>Onto a profile; true when that changed it</summary>
            public bool Into(CarProfile profile)
            {
                if (profile.BasePrice == BasePrice && profile.BodyPrice == Body && profile.EnginePrice == Engine && profile.RunningGearPrice == RunningGear)
                    return false;

                profile.BasePrice = BasePrice;
                profile.BodyPrice = Body;
                profile.EnginePrice = Engine;
                profile.RunningGearPrice = RunningGear;
                return true;
            }
        }

        /// <summary>
        /// What a car costs new, the source game's way: its body by its weight (AC's own, else its ui_car.json's), and
        /// its factory engine and running gear (the rear axle too) at their parts' new prices. Without the parts, the body alone.
        /// </summary>
        public NewPrice PriceNew(CarDefinition carDefinition)
        {
            var specs = _parts is { IsAvailable: true } parts ? parts.Specs(carDefinition.Id) : null;
            var weight = specs is { TotalMass: > 0 } ? specs.TotalMass : UiWeight(carDefinition);

            var car = new CarPricing.NewCar(CarPricing.BodyPrice(weight), 0, 0);
            if (_parts is { IsAvailable: true } withParts)
            {
                var gear = specs == null
                    ? []
                    : RunningGearFactory.Create(withParts.Catalog, specs, 1, withParts.FactoryAxle(carDefinition.Id)).SelectMany(p => p.SelfAndDescendants())
                        .Select(p => withParts.Catalog.Get(p.DefinitionId)).OfType<StreetCorsa.Parts.PartDefinition>().ToList();
                car = CarPricing.New(withParts.Catalog, weight, withParts.GetStockBuild(carDefinition), gear);
            }

            var figures = Figures;
            var low = Math.Max(0m, figures.MinPrice);
            var high = Math.Max(low, figures.MaxPrice);
            var step = figures.PriceStep > 0 ? figures.PriceStep : 1m;

            // Worked in double and clamped before it is money again: a mod's figure past what a decimal holds is the most
            decimal Money(double value) => double.IsFinite(value) ? (decimal)Math.Clamp(value, 0, (double)high) : 0m;
            var total = double.IsFinite(car.Total) ? (decimal)Math.Clamp(car.Total, (double)low, (double)high) : low;
            return new NewPrice(Math.Round(total / step) * step, Math.Round(Money(car.Body)), Math.Round(Money(car.Engine)),
                Math.Round(Money(car.RunningGear)));
        }

        /// <summary>The weight its ui_car.json gives, kg; null when it gives none that reads</summary>
        private static double? UiWeight(CarDefinition car) =>
            car.Specs != null && AcSpecs.TryParseWeight(car.Specs.Weight, out var kg) ? kg : null;

        public float CalculatePrecedence(CarDefinition carDefinition) => Precedence(carDefinition, MakeOf(carDefinition));

        private float Precedence(CarDefinition carDefinition, string? make)
        {
            var figures = Figures;
            var precedence = figures.Common;

            // Newer cars are more common, the official ones too, the badge may be rarer or commoner
            precedence += (float)Rules.CarRules.ForYear(figures.CommonByYear, carDefinition.Year, 0.0);
            precedence += (float)figures.CommonBySource.GetValueOrDefault(carDefinition.Source.ToString(), 0.0);
            precedence += CarBook.Current.Make(make)?.Common ?? 0f;

            // Exotics are rarer, economy cars commoner, by power to weight in hp per tonne (0 is a car without specs)
            var powerWeight = CalculatePowerToWeightRatio(carDefinition);
            if (powerWeight > figures.FastHpPerTonne) precedence += (float)figures.FastCommon;
            else if (powerWeight > 0 && powerWeight < figures.SlowHpPerTonne) precedence += (float)figures.SlowCommon;

            // NaN from a figure a mod got wrong is the middle, not a car that never shows
            return float.IsFinite(precedence) ? Math.Clamp(precedence, 0.0f, 1.0f) : 0.5f;
        }

        /// <summary>
        /// Gives every active car a profile, prices every car again (its parts' prices follow the parts in the mods, and
        /// the rules), and works out how common a Generated profile's car is again when the car's definition changed
        /// since (its hash moved on). How common a Manual or imported profile's car is is left alone, and so is
        /// everything else a profile holds (the stock engine picked for it).
        ///
        /// Runs on a worker thread, with one read of each collection and one write: start-up waits for it. The write
        /// touches only the fields worked out here, on the profile as stored at that moment: whatever else was saved
        /// since the read (a stock engine suggested, a profile edited by hand) stays.
        /// </summary>
        public Task EnsureProfilesExistAsync() => Task.Run(EnsureProfilesExist);

        private void EnsureProfilesExist()
        {
            _logger.Information("Ensuring car profiles exist for all car definitions");

            var allCars = _catalogRepo.GetAllCars();
            var activeCars = allCars.Where(c => c.Status == ContentStatus.Active).ToList();

            _logger.Information("Found {TotalCars} total cars, {ActiveCars} active",
                allCars.Count, activeCars.Count);

            var profiles = _profileRepo.GetAllProfiles().ToDictionary(p => p.CarDefinitionId, StringComparer.OrdinalIgnoreCase);
            var fresh = new List<CarProfile>();
            var changes = new List<KeyValuePair<string, Func<CarProfile, CarProfile?>>>();
            int created = 0, regenerated = 0, existing = 0;

            foreach (var car in activeCars)
            {
                try
                {
                    ProfileOne(car);
                }
                catch (Exception ex)
                {
                    // One mod car with figures nobody could price is not every car's profile: it goes without one
                    _logger.Error(ex, "Could not work out the profile of {CarId}", car.Id);
                }
            }

            if (fresh.Count > 0 || changes.Count > 0) _profileRepo.MergeProfiles(fresh, changes);

            _logger.Information("Profile generation complete. Created: {Created}, Regenerated: {Regenerated}, Existing: {Existing}",
                created, regenerated, existing);

            void ProfileOne(CarDefinition car)
            {
                var make = MakeOf(car);
                var hash = DefinitionHashOf(car, make);
                if (!profiles.TryGetValue(car.Id, out var profile))
                {
                    fresh.Add(Generate(car, make, hash));
                    created++;
                }
                else
                {
                    // A figure the mods give the car (cars.json) is theirs: what was set by hand for it in the catalog
                    // goes back to what the game works out, so the catalog keeps only that
                    var given = CarBook.Current.Car(car.Id);
                    var commonToMods = given?.Common != null && profile.Source == ProfileDataSource.Manual;
                    var engineToMods = given?.Engine is { Length: > 0 };
                    var layoutToMods = given?.EngineLayout != null;
                    var regenerate = commonToMods || profile.Source == ProfileDataSource.Generated && profile.DefinitionHash != hash;
                    var price = PriceNew(car);
                    var repriced = price.Into(Copy(profile));
                    if (regenerate || repriced || engineToMods || layoutToMods)
                        changes.Add(new(profile.CarDefinitionId, stored => Rework(car, stored, hash, price, commonToMods, engineToMods, layoutToMods)));
                    if (regenerate || repriced) regenerated++;
                    else existing++;
                }
            }
        }

        /// <summary>A profile's price figures alone, to see whether a price would change one without changing it</summary>
        private static CarProfile Copy(CarProfile profile) => new()
        {
            BasePrice = profile.BasePrice,
            BodyPrice = profile.BodyPrice,
            EnginePrice = profile.EnginePrice,
            RunningGearPrice = profile.RunningGearPrice
        };

        /// <summary>
        /// The profile as it is stored when the write comes, priced at <paramref name="price"/>, worked out again where it
        /// is a generated one that has not been worked out by these figures (how common the car is), and handed over to
        /// what the mods give the car; null when there is nothing to do
        /// </summary>
        private CarProfile? Rework(CarDefinition car, CarProfile stored, string hash, NewPrice price, bool commonToMods, bool engineToMods,
            bool layoutToMods)
        {
            var changed = price.Into(stored);
            if (commonToMods && stored.Source == ProfileDataSource.Manual)
            {
                stored.Source = ProfileDataSource.Generated;
                stored.DefinitionHash = null;
            }

            if (stored.Source == ProfileDataSource.Generated && stored.DefinitionHash != hash)
            {
                stored.DealerPrecedence = Precedence(car, MakeOf(car));
                stored.DefinitionHash = hash;
                changed = true;
            }

            if (engineToMods && stored.StockEngineIsManual)
            {
                stored.StockEngineIsManual = false;
                changed = true;
            }

            if (layoutToMods && stored.EngineLayout != null)
            {
                stored.EngineLayout = null;
                changed = true;
            }

            if (!changed) return null;
            stored.LastUpdatedDate = DateTime.Now;
            return stored;
        }

        /// <summary>Power to weight in hp per tonne by its ui_car.json, 0 when the specs don't give it: how common a car is</summary>
        private float CalculatePowerToWeightRatio(CarDefinition carDef)
        {
            if (carDef.Specs == null)
                return 0f;

            // The one parser for ui_car.json figures: culture-free, first number only ("350hp @ 6000rpm")
            if (!AcSpecs.TryParsePower(carDef.Specs.Bhp, out var bhp))
                return 0f;

            if (!AcSpecs.TryParseWeight(carDef.Specs.Weight, out var weightKg))
                return 0f;

            if (weightKg < Figures.MinPlausibleWeightKg || weightKg > Figures.MaxPlausibleWeightKg)
            {
                _logger.Debug("{CarId}: weight {Weight} kg is not believable; as common as a car without specs", carDef.Id, weightKg);
                return 0f;
            }

            return (float)(bhp * 1000 / weightKg);
        }
    }
}
