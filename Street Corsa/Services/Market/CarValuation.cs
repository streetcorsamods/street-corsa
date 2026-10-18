using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.Market
{
    /// <summary>
    /// What a car is worth: the one formula for it. The market prices its cars with it, an opponent's car is
    /// bought for it, a pink-slipped car goes back on a lot at it, and an opponent weighs a pink-slip challenge
    /// with it.
    ///
    /// Worth is the body in the shape it is in, and every part on the car
    /// in its wear and damage (<see cref="PartPricing.Worth"/>). What has been put into the car shows in full, what has
    /// been taken off it is not there to count, a worn engine is worth what its worn parts are. The whole is moved by how
    /// the car looks (<see cref="LooksFactor"/>) and a little by its past (<see cref="HistoryFactor"/>).
    /// </summary>
    public static class CarValuation
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Market");

        /// <summary>
        /// How much of its new price a thing in this shape is worth (0 to 1): wear takes 0.7 of it, 0.3 at nothing left
        /// of it and all of it new, as a part's (<see cref="PartPricing.Worth"/>)
        /// </summary>
        public static decimal Shape(double condition) => 0.3m + (decimal)Unit.Clamp01(condition, ifNotFinite: 0) * 0.7m;

        /// <summary>One condition for the whole car: the average of engine, gearbox, body and tyres</summary>
        public static double ConditionOf(Car car) =>
            (car.EngineHealth + car.TransmissionHealth + car.BodyCondition + car.TireCondition) / 4.0;

        /// <summary>
        /// A car worth <paramref name="newPrice"/> new in this condition, its past counted (<see cref="HistoryFactor"/>),
        /// to the nearest hundred
        /// </summary>
        public static decimal Value(decimal newPrice, double condition, decimal history = 1m) =>
            RoundToHundred(Math.Max(0m, newPrice) * Shape(condition) * history);

        /// <summary>What wins add to a car's worth: a point each, two for a car won with it, at most <see cref="MaxHistoryShare"/></summary>
        public static decimal WinShare => Rules.RuleBook.Current.Market.WinShare;
        public static decimal PinkSlipWinShare => Rules.RuleBook.Current.Market.PinkSlipWinShare;

        /// <summary>What an owner beyond the first <see cref="OwnersWithoutPenalty"/> takes off, at most <see cref="MaxOwnersPenalty"/></summary>
        public static decimal OwnerPenalty => Rules.RuleBook.Current.Market.OwnerPenalty;
        public static int OwnersWithoutPenalty => Rules.RuleBook.Current.Market.OwnersWithoutPenalty;
        public static decimal MaxOwnersPenalty => Rules.RuleBook.Current.Market.MaxOwnersPenalty;

        /// <summary>A point off for every <see cref="MileageStepKm"/> past <see cref="MileageWithoutPenaltyKm"/>, at most <see cref="MaxMileagePenalty"/></summary>
        public static double MileageWithoutPenaltyKm => Rules.RuleBook.Current.Market.MileageWithoutPenaltyKm;
        public static double MileageStepKm => Rules.RuleBook.Current.Market.MileageStepKm;
        public static decimal MileagePenalty => Rules.RuleBook.Current.Market.MileagePenalty;
        public static decimal MaxMileagePenalty => Rules.RuleBook.Current.Market.MaxMileagePenalty;

        /// <summary>The most a car's history moves its worth, either way</summary>
        public static decimal MaxHistoryShare => Rules.RuleBook.Current.Market.MaxHistoryShare;

        /// <summary>
        /// What a car's past does to its worth, as a factor on it: wins (a car that took pink slips most) put up to 15%
        /// on, a car that has been through many hands or has a lot of miles loses up to 15%. The car's condition still
        /// says far more. A car nobody knows anything about (null, an older save) is 1.
        /// </summary>
        public static decimal HistoryFactor(CarHistory? history, double odometerKm)
        {
            var bonus = 0m;
            var penalty = 0m;

            if (history != null)
            {
                bonus = Math.Min(MaxHistoryShare, Math.Max(0, history.Wins) * WinShare + Math.Max(0, history.PinkSlipsWon) * PinkSlipWinShare);
                penalty += Math.Min(MaxOwnersPenalty, Math.Max(0, history.OwnerCount - OwnersWithoutPenalty) * OwnerPenalty);
            }

            if (double.IsFinite(odometerKm) && odometerKm > MileageWithoutPenaltyKm)
            {
                var steps = Math.Floor((odometerKm - MileageWithoutPenaltyKm) / MileageStepKm);
                penalty += Math.Min(MaxMileagePenalty, (decimal)Math.Min(steps, 1000) * MileagePenalty);
            }

            var most = Math.Max(0m, MaxHistoryShare);
            return 1m + Math.Clamp(bonus - penalty, -most, most);
        }

        /// <summary>The factor of this very car's history on its worth</summary>
        public static decimal HistoryFactor(Car car) => HistoryFactor(car.History, car.OdometerKM);

        public static decimal RoundToHundred(decimal price) => Math.Round(price / 100) * 100;

        /// <summary>
        /// How much of its worth a car keeps for how it looks: rust, worn paint and a cracked windscreen each take a share
        /// (<see cref="Rules.MarketRules.RustLoss"/> and the rest) as far as they go
        /// </summary>
        public static decimal LooksFactor(double rust, double paintWear, double cracks)
        {
            var market = Rules.RuleBook.Current.Market;
            var loss = market.RustLoss * (decimal)Unit.Clamp01(rust, 0) + market.PaintWearLoss * (decimal)Unit.Clamp01(paintWear, 0)
                       + market.CrackedScreenLoss * (decimal)Unit.Clamp01(cracks, 0);
            return Math.Max(0m, 1m - loss);
        }

        /// <summary>
        /// What this very car is worth, the one way every part of the game asks: by its model's profile (see
        /// <see cref="ValueOf"/>), or, for a model the catalog has no price for, what was paid for the car.
        /// </summary>
        /// <param name="profileOf">The profile of a car definition id</param>
        /// <param name="parts">The parts, to weigh the car's own; null or none: the car is weighed as it left the factory</param>
        public static decimal WorthOf(Car car, Func<string, CarProfile?> profileOf, ICarPartsService? parts)
        {
            var profile = profileOf(car.DefinitionId);
            if (profile == null || profile.BasePrice <= 0) return PaidFor(car);

            return ValueOf(car, profile, parts is { IsAvailable: true } ? parts.Catalog : null);
        }

        /// <summary>A car nobody can price is worth what was paid for it: the best guess there is</summary>
        public static decimal PaidFor(Car car) => RoundToHundred(car.PurchasePrice);

        /// <summary>
        /// What this very car is worth: its body in the shape it is in, and each part on it in its own (the parts it was
        /// never given yet, an older save's, as the factory's in the car's shape), how it looks and its past. A profile
        /// from before the body and parts were priced apart is the whole car in the car's shape.
        /// </summary>
        /// <param name="catalog">The parts catalog; null: every part is taken as the factory's, in the car's shape</param>
        public static decimal ValueOf(Car car, CarProfile profile, PartsCatalog? catalog = null)
        {
            var looks = LooksFactor(car.Rust, car.PaintWear, car.GlassCracks);
            if (profile.BodyPrice <= 0 && profile.EnginePrice <= 0 && profile.RunningGearPrice <= 0)
                return Value(profile.BasePrice * looks, ConditionOf(car), HistoryFactor(car));

            var worth = profile.BodyPrice * Shape(car.BodyCondition);
            worth += (car.HasPartsAssigned ? PartsWorth(car, catalog, engine: true) : null) ?? profile.EnginePrice * Shape(car.EngineHealth);
            worth += (car.HasRunningGearAssigned ? PartsWorth(car, catalog, engine: false) : null) ?? profile.RunningGearPrice * Shape(car.TireCondition);
            return RoundToHundred(Math.Max(0m, worth) * looks * HistoryFactor(car));
        }

        /// <summary>
        /// What a car of this model for sale in this condition is worth before its engine is known: the body and the
        /// factory parts all in the car's shape, how it looks and its past. <see cref="EngineWorthOver"/> puts the engine
        /// it turns out to have in its place.
        /// </summary>
        public static decimal ListingValue(CarProfile profile, double condition, decimal looks, decimal history)
        {
            var whole = profile.BodyPrice + profile.EnginePrice + profile.RunningGearPrice;
            return Value((whole > 0 ? whole : profile.BasePrice) * looks, condition, history);
        }

        /// <summary>
        /// What <paramref name="engine"/>, as it is, is worth over the factory engine of <paramref name="profile"/> in the
        /// car's shape: the difference a listing priced by <see cref="ListingValue"/> is out by once its engine is known
        /// (below nothing for a tired one, above for one worked on)
        /// </summary>
        public static decimal EngineWorthOver(PartsCatalog catalog, PartInstance engine, CarProfile profile, double condition)
        {
            var worth = PartPricing.WorthOfAssembly(catalog, engine);
            return double.IsFinite(worth) ? (decimal)Math.Min(worth, 1e9) - profile.EnginePrice * Shape(condition) : 0m;
        }

        /// <remarks>
        /// Parts work can fail on a bad part. Worth is asked for in places that must not fail over it - a pink-slip
        /// challenge in the diner, a race nobody watches - so a failure (or no catalog) is null: the factory's parts count.
        /// </remarks>
        private static decimal? PartsWorth(Car car, PartsCatalog? catalog, bool engine)
        {
            if (catalog == null) return null;

            try
            {
                var roots = engine ? (car.Engine is { } root ? [root] : []) : car.Parts.Where(p => !ReferenceEquals(p, car.Engine));
                var worth = roots.Sum(p => PartPricing.WorthOfAssembly(catalog, p));
                return double.IsFinite(worth) ? (decimal)Math.Clamp(worth, 0, 1e9) : 0m;
            }
            catch (Exception ex)
            {
                Logger.Warning("Could not weigh the parts of a {CarId}; weighed as the factory's: {Error}", car.DefinitionId, ex.Message);
                return null;
            }
        }

        /// <summary>Worked on: the engine is not made of the factory build's parts</summary>
        public static bool IsModified(PartInstance engine, RatedBuild stock)
        {
            var factory = stock.Build.Parts.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return !engine.SelfAndDescendants().All(p => factory.Contains(p.DefinitionId));
        }
    }
}
