using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.CarLot;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;
using StreetCorsa.Helpers;

namespace StreetCorsa.Services.Market
{
    /// <summary>What came of trying to buy a car</summary>
    public enum PurchaseOutcome
    {
        Bought,
        NotEnoughMoney,
        NoLongerAvailable
    }

    /// <summary>
    /// The result of a purchase, for the screen to put in front of the player. <see cref="Car"/> is the car bought, and
    /// <see cref="CarName"/> its name; <see cref="AskToSwitch"/> is true when another car stands in the garage, so the
    /// new one went to the car lot and the player is asked whether to bring it in instead.
    /// </summary>
    public readonly record struct PurchaseResult(PurchaseOutcome Outcome, string Message, bool SaveFailed = false,
        Car? Car = null, string? CarName = null, bool AskToSwitch = false)
    {
        public bool Succeeded => Outcome == PurchaseOutcome.Bought;
    }

    /// <summary>
    /// Buying a car: the money, the paperwork and the clock. Shared by every screen that sells one, so a car
    /// bought off the lot and a car bought out of the listings end up in exactly the same state.
    /// </summary>
    public class CarPurchaseService(
        IGameStateRepository gameStateRepository,
        ICarPartsService partsService,
        IGameTimeService gameTimeService,
        Func<IReadOnlyList<WarehouseScene>>? warehouses = null) : ICarPurchaseService
    {
        // The warehouses the car lot has, for the bays a car from the garage is parked in
        private readonly Func<IReadOnlyList<WarehouseScene>> _warehouses = warehouses ?? (() => WarehouseScenes.Installed);
        private readonly IGameStateRepository _gameStateRepository = gameStateRepository;
        private readonly ICarPartsService _partsService = partsService;
        private readonly IGameTimeService _gameTimeService = gameTimeService;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarPurchase");

        public async Task<PurchaseResult> PurchaseAsync(
            Models.GameState.GameState gameState, UsedCarListing listing, CarDefinition carDef)
        {
            if (gameState.Player.Money < listing.Price)
            {
                _logger.Warning("Purchase failed: insufficient funds");
                return new PurchaseResult(PurchaseOutcome.NotEnoughMoney,
                    GameText.Say("purchase.no_money"));
            }

            var currentListing = gameState.UsedCarMarket.FirstOrDefault(l => l.Id == listing.Id);
            if (currentListing == null || currentListing.IsSold)
            {
                _logger.Warning("Purchase failed: listing no longer available");
                return new PurchaseResult(PurchaseOutcome.NoLongerAvailable,
                    GameText.Say("purchase.unavailable"));
            }

            // Claimed and paid for before anything is awaited: a second confirm that comes in while the engine is
            // being put together finds it sold, and a second purchase of a different car checks the money that is
            // actually left instead of the same bankroll, so two overlapping purchases can never overdraw
            currentListing.IsSold = true;
            currentListing.SoldDate = gameState.Date;
            gameState.Player.Money -= listing.Price;

            Car carInstance;
            try
            {
                carInstance = await CreateCarAsync(gameState, listing);
            }
            catch
            {
                currentListing.IsSold = false;
                currentListing.SoldDate = null;
                gameState.Player.Money += listing.Price;
                throw;
            }

            gameState.Player.Cars ??= [];
            gameState.Player.Cars.Add(carInstance);
            gameState.Player.Stats.CarsOwned++;
            gameState.Career.SyncStanding(gameState.Player);

            // Somebody with no car in the garage has just bought one: it is the one they mean. Without this
            // the garage falls back to whatever happens to be first in the list.
            var askToSwitch = !TakeIfGarageEmpty(gameState.Player, carInstance);

            // The parts went with the car; the sold listing stays around for a week and need not keep a copy
            currentListing.Parts = [];

            // The seller remembers it: the lot's salesman, or whoever the player called about the car
            Talk.TalkPeople.Happened(gameState, new PersonEvent { Date = gameState.Date, Kind = "bought", CarId = listing.CarDefinitionId, Amount = listing.Price },
                Talk.TalkPeople.DealerKey(listing.DealerLocation), Talk.TalkPeople.CarKey(listing));

            _logger.Information("Purchase completed: {CarName} for ${Price}, new bankroll: ${Bankroll}",
                carDef.Name, listing.Price, gameState.Player.Money);

            // Spend time for buying a car. Waited for before saving: late in the day that is the next morning,
            // with the market and the ads turned over, and all of it belongs in the save.
            try
            {
                await _gameTimeService.SpendTimeAsync(gameState, GameAction.BuyCar);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for buying the car");
            }

            var saveFailed = !GameSaves.TrySave(_gameStateRepository, gameState, _logger, "a purchase");

            return new PurchaseResult(PurchaseOutcome.Bought,
                GameText.Say("purchase.bought", ("car", $"{carDef.Brand} {carDef.Name}"), ("price", new Money(listing.Price)),
                    ("where", WhereItIs(askToSwitch))),
                saveFailed, carInstance, $"{carDef.Brand} {carDef.Name}", askToSwitch);
        }

        /// <summary>
        /// A rival's car out of the paper (<see cref="RivalCarAds"/>): that very car, from the rival's garage, the money to
        /// the rival. Nothing to put together, so nothing is awaited before the car changes hands.
        /// </summary>
        public async Task<PurchaseResult> PurchaseFromRivalAsync(Models.GameState.GameState gameState, RivalCarAd ad, CarDefinition carDef)
        {
            // The ad as it stands now: sold meanwhile, or its price come down since the page was drawn
            if (gameState.NewspaperAds.RivalCars?.Contains(ad) != true)
            {
                _logger.Warning("Purchase failed: the ad is gone");
                return new PurchaseResult(PurchaseOutcome.NoLongerAvailable, GameText.Say("purchase.unavailable"));
            }

            var price = ad.AskingPrice;
            var seller = ad.RivalName;
            if (gameState.Player.Money < price)
            {
                _logger.Warning("Purchase failed: insufficient funds");
                return new PurchaseResult(PurchaseOutcome.NotEnoughMoney, GameText.Say("purchase.no_money"));
            }

            var car = RivalCarAds.HandOver(gameState, ad, gameState.Player.Name, gameState.Date);
            if (car == null)
            {
                _logger.Warning("Purchase failed: the rival no longer has the car");
                return new PurchaseResult(PurchaseOutcome.NoLongerAvailable, GameText.Say("purchase.unavailable"));
            }

            gameState.Player.Money -= price;
            gameState.Player.Cars ??= [];
            car.ClearPlace();
            gameState.Player.Cars.Add(car);
            gameState.Player.Stats.CarsOwned++;
            gameState.Career.SyncStanding(gameState.Player);
            var askToSwitch = !TakeIfGarageEmpty(gameState.Player, car);
            if (gameState.Racers.Find(seller) is Opponent rival)
                Talk.TalkPeople.Happened(gameState, rival, new PersonEvent { Date = gameState.Date, Kind = "bought", CarId = car.DefinitionId, Amount = price });

            _logger.Information("Purchase completed: {CarName} from {Seller} for ${Price}, new bankroll: ${Bankroll}",
                carDef.Name, seller, price, gameState.Player.Money);

            try
            {
                await _gameTimeService.SpendTimeAsync(gameState, GameAction.BuyCar);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for buying the car");
            }

            var saveFailed = !GameSaves.TrySave(_gameStateRepository, gameState, _logger, "a purchase");

            return new PurchaseResult(PurchaseOutcome.Bought,
                GameText.Say("purchase.bought_private", ("seller", seller), ("car", $"{carDef.Brand} {carDef.Name}"), ("price", new Money(price)),
                    ("where", WhereItIs(askToSwitch))),
                saveFailed, car, $"{carDef.Brand} {carDef.Name}", askToSwitch);
        }

        public SwitchOutcome SwitchTo(Models.GameState.GameState gameState, Car car)
        {
            var player = gameState.Player;
            if (player.Cars?.Contains(car) != true)
            {
                _logger.Warning("Not switched to {CarId} (Instance: {InstanceId}): the player no longer has it", car.DefinitionId, car.InstanceId);
                return SwitchOutcome.NotOwned;
            }

            // The car from the garage takes the first free bay: the new one had none to give it
            CarStorage.ToGarage(player, car, n => CarStorage.CapacityOf(_warehouses(), n));
            _logger.Information("Switched to the car just bought: {CarId} (Instance: {InstanceId})", car.DefinitionId, car.InstanceId);
            return GameSaves.TrySave(_gameStateRepository, gameState, _logger, "switching to the car just bought")
                ? SwitchOutcome.Switched
                : SwitchOutcome.SaveFailed;
        }

        /// <summary>
        /// Puts the car just bought in the garage when no car stands there: none picked, or the picked one gone. A car
        /// in the police impound still counts as there: it is collected from the garage. True when the new car went in;
        /// false when another car is there and the new one goes to the car lot.
        /// </summary>
        public static bool TakeIfGarageEmpty(Player player, Car car)
        {
            if (player.Cars.Any(c => c.InstanceId == player.SelectedCarInstanceId && c != car)) return false;

            player.SelectedCarInstanceId = car.InstanceId;
            return true;
        }

        private static string WhereItIs(bool inTheLot) => GameText.Say(inTheLot ? "purchase.where.lot" : "purchase.where.garage");

        /// <summary>
        /// The car that was for sale, as whoever buys it gets it: the parts it came with (the listing's list moves to the
        /// car), paid the listing's price on <paramref name="date"/>. Older listings carry no parts; the car is then
        /// given its factory ones (<see cref="ICarPartsService.EnsureParts"/>), which is the buyer's to do. The car's
        /// history comes with it, <paramref name="buyer"/> its newest owner.
        /// </summary>
        public static Car CarFrom(UsedCarListing listing, DateTime date, string buyer)
        {
            var car = new Car
            {
                // A relisted car is the same car it was
                InstanceId = listing.CarInstanceId ?? Guid.NewGuid(),
                DefinitionId = listing.CarDefinitionId,
                SkinId = listing.SkinId,
                PurchasePrice = listing.Price,
                PurchaseDate = date,
                OdometerKM = listing.Mileage,
                // Map single condition to health metrics
                EngineHealth = listing.Condition,
                TransmissionHealth = listing.Condition,
                BodyCondition = listing.Condition,
                TireCondition = listing.Condition,
                // What was for sale is what gets bought; older listings carry no parts and get the factory engine
                Parts = listing.Parts,
                HasPartsAssigned = listing.Parts.Count > 0,
                HasRunningGearAssigned = listing.HasRunningGearAssigned,
                PowerHp = listing.PowerHp,
                History = listing.History?.Copy() ?? new CarHistory()
            };
            car.History.ChangeHands(buyer, date, CarAcquisition.Dealer);
            CarLooks.CopyTo(listing, car);

            // A relisted car comes with its dents; the body's figure is then the dents', not the listing's average
            if (listing.BodyDamageKmh is { Length: > 0 } body)
            {
                car.BodyDamageKmh = body;
                car.BodyDamageKmh = CarCondition.Body(car); // four sane zones, a copy of the car's own
                car.BodyCondition = CarCondition.BodyCondition(car);
            }

            return car;
        }

        /// <summary>The car that was for sale, with the parts it came with, or its factory engine if it came without</summary>
        private async Task<Car> CreateCarAsync(Models.GameState.GameState gameState, UsedCarListing listing)
        {
            var carInstance = CarFrom(listing, gameState.Date, gameState.Player.Name);

            try
            {
                await _partsService.EnsurePartsAsync(carInstance);
            }
            catch (Exception ex)
            {
                // The car sells without the parts it was missing, the way a listing whose engine could not be
                // built does; the garage gives it its factory ones the next time it looks
                _logger.Warning("Could not give the {CarId} its factory parts: {Error}", listing.CarDefinitionId, ex.Message);
            }

            return carInstance;
        }
    }
}
