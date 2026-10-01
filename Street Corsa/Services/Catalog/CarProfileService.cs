using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// Implements car profile generation with pricing and precedence algorithms
    /// </summary>
    public class CarProfileService : ICarProfileService
    {
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IAppLogger _logger;

        /// <summary>
        /// Goes into a Generated profile's DefinitionHash with the car's own hash. Moved on when the way specs are
        /// read or priced changes (2: lb and kW converted, instead of read as kg and hp; 3: priced from hp per tonne,
        /// where hp per kg had put every car on the $5,000 floor; 4: the figures from the rules and the badge from the
        /// makes), so every generated price is worked out again once. The formula is deterministic: a car whose specs
        /// read the same keeps its price.
        /// </summary>
        private const string SpecsReading = "specs4";

        /// <summary>The pricing figures in play: the mods' rules, not a career's (the catalog is the program's)</summary>
        private static Rules.CarRules Figures => Rules.RuleBook.Installed.Cars;

        /// <summary>
        /// The car's own hash, the way specs are read, and what the price was worked out by: the rules' figures and the
        /// car's make with its badge's figures. A mod that changes any of them has the price worked out again.
        /// </summary>
        private static string DefinitionHashOf(CarDefinition car)
        {
            var make = MakeOf(car);
            var badge = CarBook.Current.Make(make);
            var by = FormattableString.Invariant($"{FiguresDigest}|{make}|{badge?.Price}|{badge?.Common}");
            return $"{car.ContentHash}|{SpecsReading}|{Digest(by)}";
        }

        private static string FiguresDigest => System.Text.Json.JsonSerializer.Serialize(Figures, Rules.RuleBook.Options);

        private static string Digest(string text) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];

        /// <summary>The car's make, as the mods' makes tell it (<see cref="CarBook.MakeOf"/>)</summary>
        private static string? MakeOf(CarDefinition car) => CarBook.Current.MakeOf(car.Id, car.Brand, car.Name);

        public CarProfileService(IContentCatalogRepository catalogRepo, ICarProfileRepository profileRepo)
        {
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);
        }

        public CarProfile GenerateDefaultProfile(CarDefinition carDefinition)
        {
            var basePrice = CalculateBasePrice(carDefinition);
            var precedence = CalculatePrecedence(carDefinition);

            _logger.Debug("Generated profile for {CarId}: BasePrice=${Price}, Precedence={Precedence}",
                carDefinition.Id, basePrice, precedence);

            return new CarProfile
            {
                CarDefinitionId = carDefinition.Id,
                BasePrice = basePrice,
                DealerPrecedence = precedence,
                Source = ProfileDataSource.Generated,
                DefinitionHash = DefinitionHashOf(carDefinition),
                CreatedDate = DateTime.Now,
                LastUpdatedDate = DateTime.Now,
                IsStreetLegal = true // Default assumption
            };
        }

        public decimal CalculateBasePrice(CarDefinition carDefinition)
        {
            // Start with power/weight ratio (primary metric)
            var powerWeightBase = CalculatePowerWeightPrice(carDefinition);

            var figures = Figures;

            // The badge, the year and where the car comes from
            var withBrand = powerWeightBase * (decimal)(CarBook.Current.Make(MakeOf(carDefinition))?.Price ?? 1.0);
            var withYear = withBrand * (decimal)Rules.CarRules.ForYear(figures.PriceByYear, carDefinition.Year, 1.0);
            var withSource = withYear * (decimal)figures.PriceBySource.GetValueOrDefault(carDefinition.Source.ToString(), 1.0);

            var low = Math.Max(0m, figures.MinPrice);
            var clamped = Math.Clamp(withSource, low, Math.Max(low, figures.MaxPrice));
            var step = figures.PriceStep > 0 ? figures.PriceStep : 1m;
            return Math.Round(clamped / step) * step;
        }

        public float CalculatePrecedence(CarDefinition carDefinition)
        {
            var figures = Figures;
            var precedence = figures.Common;

            // Newer cars are more common, the official ones too, the badge may be rarer or commoner
            precedence += (float)Rules.CarRules.ForYear(figures.CommonByYear, carDefinition.Year, 0.0);
            precedence += (float)figures.CommonBySource.GetValueOrDefault(carDefinition.Source.ToString(), 0.0);
            precedence += CarBook.Current.Make(MakeOf(carDefinition))?.Common ?? 0f;

            // Exotics are rarer, economy cars commoner, by power to weight in hp per tonne (0 is a car without specs)
            var powerWeight = CalculatePowerToWeightRatio(carDefinition);
            if (powerWeight > figures.FastHpPerTonne) precedence += (float)figures.FastCommon;
            else if (powerWeight > 0 && powerWeight < figures.SlowHpPerTonne) precedence += (float)figures.SlowCommon;

            // NaN from a figure a mod got wrong is the middle, not a car that never shows
            return float.IsFinite(precedence) ? Math.Clamp(precedence, 0.0f, 1.0f) : 0.5f;
        }

        /// <summary>
        /// Gives every active car a profile, and works a Generated profile out again when the car's definition
        /// changed since (its hash moved on): a price worked out from an older ui_car.json, or read the wrong way,
        /// would otherwise stick for good. Manual and imported profiles are left alone, and so is everything a
        /// generated one holds besides price and precedence (the stock engine picked for it).
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
                var hash = DefinitionHashOf(car);
                if (!profiles.TryGetValue(car.Id, out var profile))
                {
                    fresh.Add(GenerateDefaultProfile(car));
                    created++;
                }
                else
                {
                    // A figure the mods give the car (cars.json) is theirs: what was set by hand for it in the catalog
                    // goes back to what the game works out, so the catalog keeps only that
                    var given = CarBook.Current.Car(car.Id);
                    var priceToMods = given is { } g && (g.Price != null || g.Common != null) && profile.Source == ProfileDataSource.Manual;
                    var engineToMods = given?.Engine is { Length: > 0 };
                    var layoutToMods = given?.EngineLayout != null;
                    var regenerate = priceToMods || profile.Source == ProfileDataSource.Generated && profile.DefinitionHash != hash;
                    if (regenerate || engineToMods || layoutToMods) changes.Add(new(profile.CarDefinitionId, stored => Rework(car, stored, hash, priceToMods, engineToMods, layoutToMods)));
                    if (regenerate) regenerated++;
                    else existing++;
                }
            }
        }

        /// <summary>
        /// The profile as it is stored when the write comes, worked out again where it is a generated one that has not
        /// been worked out by these figures (only the price and how common the car is), and handed over to what the mods
        /// give the car; null when there is nothing to do
        /// </summary>
        private CarProfile? Rework(CarDefinition car, CarProfile stored, string hash, bool priceToMods, bool engineToMods, bool layoutToMods)
        {
            var changed = false;
            if (priceToMods && stored.Source == ProfileDataSource.Manual)
            {
                stored.Source = ProfileDataSource.Generated;
                stored.DefinitionHash = null;
            }

            if (stored.Source == ProfileDataSource.Generated && stored.DefinitionHash != hash)
            {
                stored.BasePrice = CalculateBasePrice(car);
                stored.DealerPrecedence = CalculatePrecedence(car);
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

        // Helper methods

        /// <summary>
        /// What a car's power to weight is worth before brand, year and source: $7,000 at 70 hp per tonne (an
        /// economy car), rising a little faster than the power, as the power to weight to the 1.3: about $18,000
        /// at 150 hp/t (a muscle car) and $57,000 at 350 hp/t (a supercar). Dearer the quicker, always.
        /// </summary>
        private decimal CalculatePowerWeightPrice(CarDefinition carDef)
        {
            var powerToWeight = CalculatePowerToWeightRatio(carDef);

            if (powerToWeight <= 0)
            {
                // No specs available, use conservative default
                return Figures.NoSpecsPrice;
            }

            // A mod's ui_car.json can say anything (a million bhp, a car of one kilo): a price past what a decimal holds
            // would throw in the cast. The base price is clamped to the rules' most in the end anyway.
            var figures = Figures;
            var price = figures.ReferencePrice * Math.Pow(powerToWeight / figures.ReferenceHpPerTonne, figures.PriceExponent);
            return double.IsFinite(price) && price >= 0 ? (decimal)Math.Min(price, MaxPowerWeightPrice) : figures.NoSpecsPrice;
        }

        private const double MaxPowerWeightPrice = 1e9;

        /// <summary>Power to weight in hp per tonne, 0 when the specs don't give it</summary>
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
                _logger.Debug("{CarId}: weight {Weight} kg is not believable; priced as a car without specs", carDef.Id, weightKg);
                return 0f;
            }

            return (float)(bhp * 1000 / weightKg);
        }
    }
}
