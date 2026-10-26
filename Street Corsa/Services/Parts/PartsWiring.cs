using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// Points the parts code's process-wide seams at the game: every part price (<see cref="PartPricing"/>), every
    /// car body's (<see cref="CarPricing"/>) and the engines' figures (<see cref="EngineRules"/>) are asked for from
    /// anywhere without a service at hand, and the tools that link those files have no rules or service. Wired once,
    /// where the app is composed, never by a service's constructor: a second instance (a test, a harness) must not
    /// quietly take the prices over.
    /// </summary>
    public static class PartsWiring
    {
        /// <param name="parts">The parts service whose price book and catalog the prices read</param>
        /// <param name="mayWait">
        /// Whether the caller asking for a price now may wait for the price book to be made (several seconds of dyno runs
        /// the first time); false on the UI thread, which prices at the kinds' bases until the warm-up is through
        /// </param>
        public static void Wire(CarPartsService parts, Func<bool> mayWait)
        {
            // Every part price in the game: the rules of the game in play
            PartPricing.Source = () =>
            {
                var economy = Rules.RuleBook.Current.Economy;
                return new PartPricing.Figures(economy.PartsPriceFactor, economy.UsedShopFactor, economy.TradeInFactor, economy.WearFloor);
            };
            // And the engines' figures (cooling, factory, tuners, the engine matcher): the same rules
            EngineRules.Source = () => Rules.RuleBook.Current.Engines;
            // But the engine matcher's go into catalog.db, the program's: the installed mods' rules
            EngineRules.ProgramSource = () => Rules.RuleBook.Installed.Engines;
            // The body's price the same way: the catalog's figures, the mods' rules
            CarPricing.Source = () =>
            {
                var cars = Rules.RuleBook.Installed.Cars;
                return new CarPricing.Figures(cars.BodyPrice, cars.BodyKg, cars.BodyWeightPower, cars.MinBodyPrice, cars.MaxBodyPrice,
                    cars.MinPlausibleWeightKg, cars.MaxPlausibleWeightKg);
            };
            // Every new part's price: this catalog's parts measured on its builds, once they are
            PartPricing.Book = () => parts.PriceBookFor(mayWait());
            // And what a machine shop's cuts do to a part's worth: this catalog's jobs, once it is loaded
            PartPricing.Jobs = () => parts.CatalogIfLoaded;
            // An engine that breaks the dyno is priced by its parts, and the log says which
            var logger = Logging.AppLoggerFactory.CreateLogger(Logging.LogCategory.Parts);
            PartPricing.Fault = fault => logger.Warning("Part prices: {Fault}", fault);
            // How little damage counts as none: the rules of the game in play
            Models.GameState.PartInstance.UndamagedTearSource = () => Rules.RuleBook.Current.Condition.UndamagedTear;
        }
    }
}
