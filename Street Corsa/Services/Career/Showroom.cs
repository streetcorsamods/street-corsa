using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Services.Catalog;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// Every car of the game in its tier, open or not yet (<see cref="ChallengeCareer.OpenTier"/>): nothing is bought, an
    /// open car is taken out to the garage, one of each. A car's tier is its setting's (<see cref="CarEntry.Tier"/>), else
    /// its power to weight's (<see cref="ChallengeCareer.TierOf"/>).
    /// </summary>
    public static class Showroom
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Showroom");

        /// <summary>The cars of the game, installed and played with, each in its tier: by tier, then by name</summary>
        /// <param name="active">The catalog's active cars</param>
        /// <param name="isGameCar">Whether the game plays with a car (it has a profile); none: every car the game takes</param>
        public static List<ShowroomCar> Cars(IEnumerable<CarDefinition> active, CareerState career, Player player, UnlockRules rules,
            Func<string, bool>? isGameCar = null, CarBook? book = null)
        {
            book ??= CarBook.Current;
            var open = ChallengeCareer.OpenTier(ChallengeCareer.CareerScore(career), rules);
            var owned = (player.Cars ?? []).Select(c => c.DefinitionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return InstalledCars.Only(active, out _)
                .Where(car => isGameCar?.Invoke(car.Id) ?? true)
                .Select(car =>
                {
                    var tier = TierOf(car, rules, book);
                    return new ShowroomCar(car, tier, tier <= open, owned.Contains(car.Id), ChallengeCareer.TierScore(tier, rules));
                })
                .OrderBy(c => c.Tier)
                .ThenBy(c => c.Definition.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>A car's tier: its setting's, else its power to weight's</summary>
        public static int TierOf(CarDefinition car, UnlockRules rules, CarBook? book = null)
        {
            var set = (book ?? CarBook.Current).Car(car.Id)?.Tier;
            double? hp = AcSpecs.TryParsePower(car.Specs?.Bhp, out var power) ? power : null;
            double? kg = AcSpecs.TryParseWeight(car.Specs?.Weight, out var weight) ? weight : null;
            return ChallengeCareer.TierOf(set, hp, kg, rules);
        }

        /// <summary>The cars of the tiers after <paramref name="before"/> up to <paramref name="after"/>, not in the garage yet</summary>
        public static List<ShowroomCar> NewlyOpen(IEnumerable<ShowroomCar> cars, int before, int after) =>
            cars.Where(c => c.Tier > before && c.Tier <= after && !c.Owned).ToList();

        /// <summary>
        /// An open car out of the showroom into the garage, as new, and picked to drive (the car that stood there goes
        /// to the car lot, down off the lift, as wherever a car is taken in: <see cref="CarLot.CarStorage.ToGarage"/>);
        /// null when it is shut or in the garage already
        /// </summary>
        /// <param name="capacityOf">How many bays a warehouse has (from 1); none: the installed warehouses'</param>
        public static Car? TakeOut(GameState gameState, ShowroomCar car, Func<int, int>? capacityOf = null)
        {
            if (!car.Open) return null;
            var player = gameState.Player;
            player.Cars ??= [];
            if (player.Cars.Any(c => string.Equals(c.DefinitionId, car.Definition.Id, StringComparison.OrdinalIgnoreCase))) return null;

            var taken = new Car(car.Definition.Id)
            {
                SkinId = car.Definition.AvailableSkins.FirstOrDefault() ?? "default",
                EngineHealth = 1,
                TransmissionHealth = 1,
                BodyCondition = 1,
                TireCondition = 1,
                PurchaseDate = gameState.Date
            };
            CarLooks.Give(taken, CarLooks.Roll(1, LookSource.Dealer, Random.Shared));
            taken.History.ChangeHands(player.Name, gameState.Date, CarAcquisition.Unknown);
            player.Cars.Add(taken);
            player.Stats.CarsOwned++;
            CarLot.CarStorage.ToGarage(player, taken, capacityOf ?? (n => CarLot.CarStorage.CapacityOf(CarLot.WarehouseScenes.Installed, n)));
            gameState.Career.SyncStanding(player);
            Logger.Information("Out of the showroom into the garage: {Car} (tier {Tier})", car.Definition.Id, car.Tier);
            return taken;
        }
    }

    /// <summary>A car in the showroom: its tier, whether it is open, whether it is in the garage, the score its tier opens at</summary>
    public sealed record ShowroomCar(CarDefinition Definition, int Tier, bool Open, bool Owned, long? OpensAt);
}
