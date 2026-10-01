using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Time;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Market
{
    /// <summary>What came of trying to sell a car, or to put it in the paper</summary>
    public enum SaleOutcome
    {
        Done,

        /// <summary>The car isn't the player's (any more)</summary>
        NotOwned,

        /// <summary>The car is out racing: it can't change hands until the race is settled</summary>
        Racing,

        /// <summary>The ad has no buyer on the phone, or has left the paper</summary>
        NoOffer,

        NotEnoughMoney,

        /// <summary>Nobody reading the paper wants it (a wreck), or the price makes no sense</summary>
        Refused
    }

    public readonly record struct SaleResult(SaleOutcome Outcome, string Message, bool SaveFailed = false)
    {
        public bool Succeeded => Outcome == SaleOutcome.Done;
    }

    /// <summary>What a dealer pays for a car today, or the scrapyard for a wreck</summary>
    /// <param name="Value">What the car is worth (<see cref="CarValuation"/>)</param>
    /// <param name="BuyerName">The dealer, or the scrapyard</param>
    /// <param name="LocationId">The lot the car goes on; null when it goes for scrap</param>
    public sealed record DealerQuote(decimal Value, decimal Amount, bool IsScrap, string BuyerName, string? LocationId);

    /// <summary>
    /// Selling the player's cars. A dealer buys at once, for <see cref="DealerShare"/> of what the car is worth, and
    /// puts it on its lot at the full price. A totaled car only goes to the scrapyard, for
    /// <see cref="ScrapShare"/> of its worth give or take <see cref="ScrapSpread"/> (as in the GameMaker version).
    /// Or the player puts the car in the paper at a price of their own: buyers call over the days, fewer and
    /// haggling harder the more is asked over what the car is worth (<see cref="OfferChance"/>). An offer holds
    /// for <see cref="OfferDays"/> days. The car stays in the garage and can race until an offer is taken.
    /// </summary>
    public class CarSaleService(
        IUsedCarMarketService market,
        IGameTimeService gameTimeService,
        IGameStateRepository gameStateRepository) : ICarSaleService
    {
        /// <summary>Share of a car's worth a dealer pays for it</summary>
        public static double DealerShare => Rules.RuleBook.Current.Market.DealerShare;

        /// <summary>Share of a wreck's worth the scrapyard pays, before <see cref="ScrapSpread"/></summary>
        public static double ScrapShare => Rules.RuleBook.Current.Market.ScrapShare;

        /// <summary>How far the scrapyard's price strays either way, as a share of its price</summary>
        public static double ScrapSpread => Rules.RuleBook.Current.Market.ScrapSpread;

        /// <summary>What an ad in the classifieds costs; it runs for <see cref="AdDays"/> days</summary>
        public static decimal AdFee => Rules.RuleBook.Current.Market.AdFee;

        public static int AdDays => Rules.RuleBook.Current.Market.AdDays;

        /// <summary>How long a buyer waits for an answer</summary>
        public static int OfferDays => Rules.RuleBook.Current.Market.OfferDays;

        /// <summary>The least a car can be put in the paper for</summary>
        public static decimal MinAskingPrice => Rules.RuleBook.Current.Market.MinAskingPrice;

        /// <summary>How far over its worth a car can be put in the paper: past this nobody would even call</summary>
        public static decimal MaxAskingShare => Rules.RuleBook.Current.Market.MaxAskingShare;

        public static string ScrapyardName => GameText.Say("sell.scrapyard_name");

        /// <summary>The chance the buyer who calls is one of the rivals, when one of them wants the car and can pay</summary>
        public static double RivalBuyerChance => Rules.RuleBook.Current.Market.RivalBuyerChance;

        private readonly IUsedCarMarketService _market = market;
        private readonly IGameTimeService _gameTimeService = gameTimeService;
        private readonly IGameStateRepository _gameStateRepository = gameStateRepository;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarSale");

        public decimal ValueOf(Car car) => _market.ValueOf(car);

        public DealerQuote QuoteDealer(GameState gameState, Car car)
        {
            var value = _market.ValueOf(car);
            if (CarCondition.IsTotaled(car))
                return new DealerQuote(value, RoundToTen((double)value * ScrapFactor(car.InstanceId)), true, ScrapyardName, null);

            var location = _market.TradeInLocation(gameState.DealerLocations);
            var name = (gameState.DealerLocations?.FirstOrDefault(d => d.Id == location)
                        ?? _market.GetDefaultDealers().FirstOrDefault(d => d.Id == location))?.Name ?? GameText.Say("sell.dealer_name");
            return new DealerQuote(value, RoundToTen((double)value * DealerShare), false, name, location);
        }

        public async Task<SaleResult> SellToDealerAsync(GameState gameState, Car car)
        {
            if (WhyNotSellable(gameState, car) is { } refusal) return refusal;

            var quote = QuoteDealer(gameState, car);
            RemoveFromGarage(gameState, car);
            gameState.Player.Money += quote.Amount;

            // The dealer sells it on at its worth, marked up or down like every car on the lot; the scrapyard breaks it up
            if (quote.LocationId != null)
            {
                gameState.UsedCarMarket.Add(_market.ListCar(car, GameRules.Scale(quote.Value, gameState.Rules.CarPriceMultiplier),
                    quote.LocationId, gameState.Date));
            }

            _logger.Information("Sold {Car} to {Buyer} for ${Amount} (worth ${Value})", car.DefinitionId, quote.BuyerName, quote.Amount, quote.Value);

            var saveFailed = await SpendTimeAndSave(gameState, GameAction.SellCar);
            var message = quote.IsScrap
                ? GameText.Say("sell.scrapped", ("amount", Money.Text(quote.Amount)))
                : GameText.Say("sell.traded_in", ("buyer", quote.BuyerName), ("amount", Money.Text(quote.Amount)));
            return new SaleResult(SaleOutcome.Done, message, saveFailed);
        }

        public CarSaleAd? AdFor(GameState gameState, Car car) =>
            gameState.NewspaperAds.PlayerCars.FirstOrDefault(ad => ad.CarInstanceId == car.InstanceId);

        public decimal SuggestedAskingPrice(Car car) => Math.Max(MinAskingPrice, RoundToTen((double)_market.ValueOf(car)));

        public (decimal Min, decimal Max) AskingPriceRange(Car car) =>
            (MinAskingPrice, Math.Max(MinAskingPrice, RoundToTen((double)(_market.ValueOf(car) * MaxAskingShare))));

        public async Task<SaleResult> PlaceAdAsync(GameState gameState, Car car, decimal askingPrice)
        {
            if (WhyNotSellable(gameState, car) is { } refusal) return refusal;

            if (CarCondition.IsTotaled(car))
                return new SaleResult(SaleOutcome.Refused, GameText.Say("sell.refused.wreck"));

            var (min, max) = AskingPriceRange(car);
            if (askingPrice < min || askingPrice > max)
                return new SaleResult(SaleOutcome.Refused, GameText.Say("sell.refused.price", ("min", Money.Text(min)), ("max", Money.Text(max))));

            if (AdFor(gameState, car) != null)
                return new SaleResult(SaleOutcome.Refused, GameText.Say("sell.refused.in_paper"));

            if (gameState.Player.Money < AdFee)
                return new SaleResult(SaleOutcome.NotEnoughMoney, GameText.Say("sell.refused.no_money", ("fee", Money.Text(AdFee))));

            gameState.Player.Money -= AdFee;
            gameState.NewspaperAds.PlayerCars.Add(new CarSaleAd
            {
                CarInstanceId = car.InstanceId,
                AskingPrice = Math.Round(askingPrice),
                PostedDate = gameState.Date
            });
            _logger.Information("Put {Car} in the paper for ${Price}", car.DefinitionId, askingPrice);

            var saveFailed = await SpendTimeAndSave(gameState, GameAction.PlaceAd);
            return new SaleResult(SaleOutcome.Done,
                GameText.Say("sell.ad.placed", ("days", AdDays), ("price", Money.Text(askingPrice))), saveFailed);
        }

        public SaleResult WithdrawAd(GameState gameState, CarSaleAd ad)
        {
            if (!gameState.NewspaperAds.PlayerCars.Remove(ad)) return new SaleResult(SaleOutcome.NoOffer, GameText.Say("sell.ad.run_out"));

            _logger.Information("Took the ad for {Car} out of the paper", ad.CarInstanceId);
            return new SaleResult(SaleOutcome.Done, GameText.Say("sell.ad.removed"), !Save(gameState));
        }

        public async Task<SaleResult> AcceptOfferAsync(GameState gameState, CarSaleAd ad)
        {
            if (!gameState.NewspaperAds.PlayerCars.Contains(ad) || ad.Offer is not { } offer || offer.Expires < gameState.Date)
                return new SaleResult(SaleOutcome.NoOffer, GameText.Say("sell.offer.gone"));

            var car = gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == ad.CarInstanceId);
            if (car == null)
            {
                gameState.NewspaperAds.PlayerCars.Remove(ad);
                return new SaleResult(SaleOutcome.NotOwned, GameText.Say("sell.refused.not_owned"), !Save(gameState));
            }

            if (WhyNotSellable(gameState, car) is { } refusal) return refusal;

            // A rival who called has to have the money still; they may have spent it since
            Opponent? rival = null;
            if (offer.RivalName != null)
            {
                rival = gameState.Racers.Find(offer.RivalName) as Opponent;
                if (rival == null || rival.Money < offer.Amount)
                {
                    ad.Offer = null;
                    _logger.Information("{Rival} can no longer pay ${Amount} for {Car}", offer.RivalName, offer.Amount, car.DefinitionId);
                    return new SaleResult(SaleOutcome.NoOffer, GameText.Say("sell.offer.no_money", ("rival", offer.RivalName)), !Save(gameState));
                }
            }

            RemoveFromGarage(gameState, car);
            gameState.Player.Money += offer.Amount;

            if (rival != null)
            {
                // It races under them now; tomorrow's review decides whether it is the car they drive
                rival.Money -= offer.Amount;
                rival.Cars.Add(car);
                rival.Stats.CarsOwned++;
                car.History.ChangeHands(rival.Name, gameState.Date, CarAcquisition.PrivateSale);
                Grudges.CarBack(rival, car);
                OpponentLifeService.AddTalk(gameState, gameState.Date,
                    [GameText.Installed.Pick("street.bought_players_car", Random.Shared, new TextValues { ["name"] = rival.Name, ["player"] = gameState.Player.Name })]);
            }

            // Anybody else drives it away: it leaves the game
            _logger.Information("Sold {Car} to {Buyer} out of the paper for ${Amount}", car.DefinitionId, offer.BuyerName, offer.Amount);

            var saveFailed = await SpendTimeAndSave(gameState, GameAction.SellCar);
            return new SaleResult(SaleOutcome.Done, GameText.Say("sell.offer.sold", ("buyer", Capitalized(offer.BuyerName)), ("amount", Money.Text(offer.Amount))), saveFailed);
        }

        public SaleResult DeclineOffer(GameState gameState, CarSaleAd ad)
        {
            if (ad.Offer == null) return new SaleResult(SaleOutcome.NoOffer, GameText.Say("sell.offer.none"));

            _logger.Information("Turned down {Buyer}'s ${Amount} for {Car}", ad.Offer.BuyerName, ad.Offer.Amount, ad.CarInstanceId);
            ad.Offer = null;
            return new SaleResult(SaleOutcome.Done, GameText.Say("sell.offer.declined"), !Save(gameState));
        }

        /// <summary>
        /// A day in the classifieds: ads of cars the player no longer has and ads that ran their time leave the
        /// paper, buyers who got no answer give up, and a new buyer may call about each ad without one.
        /// </summary>
        public void ReviewAds(GameState gameState, DateTime currentDate, Random random)
        {
            var ads = gameState.NewspaperAds.PlayerCars;
            var gone = ads.RemoveAll(ad =>
                gameState.Player.Cars.All(c => c.InstanceId != ad.CarInstanceId) || (currentDate - ad.PostedDate).TotalDays > AdDays);

            var valueOf = _market.Valuer();
            foreach (var ad in ads)
            {
                if (ad.Offer is { } old && old.Expires < currentDate) ad.Offer = null;
                if (ad.Offer != null) continue;

                var car = gameState.Player.Cars.First(c => c.InstanceId == ad.CarInstanceId);
                decimal value;
                try
                {
                    value = valueOf(car);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not value {Car} for its ad: {Error}", car.DefinitionId, ex.Message);
                    continue;
                }

                if (random.NextDouble() >= OfferChance(ad.AskingPrice, value)) continue;

                var amount = OfferAmount(ad.AskingPrice, value, random.NextDouble());
                var rival = RivalBuyer(gameState, car, amount, random);
                ad.Offer = new CarOffer
                {
                    BuyerName = rival?.Name ?? GameText.Installed.Pick("names.car_buyers", random),
                    RivalName = rival?.Name,
                    Amount = amount,
                    Expires = currentDate.Date.AddDays(OfferDays).AddHours(GameState.DayEndHour)
                };
                _logger.Information("{Buyer} offers ${Amount} for {Car} (asking ${Asking}, worth ${Value})",
                    ad.Offer.BuyerName, ad.Offer.Amount, car.DefinitionId, ad.AskingPrice, value);
            }

            if (gone > 0) _logger.Information("{Count} of the player's ads left the paper", gone);
        }

        /// <summary>
        /// A rival who wants the car and can pay for it, now and then (<see cref="RivalBuyerChance"/>): one without a car
        /// that can race, or whose car makes less power than this one, with room in the garage. Null for a buyer
        /// from outside the racing crowd. Rolls the dice only when there is such a rival.
        /// </summary>
        public static Opponent? RivalBuyer(GameState gameState, Car car, decimal amount, Random random)
        {
            var power = Simulation.RaceSimulatorService.GetCarHP(car);
            var wanting = gameState.Racers.ReadyToRace.Values.Concat(gameState.Racers.Retired.Values)
                .OfType<Opponent>()
                .Where(o => !o.IsKing && o.Money >= amount && o.Cars.Count < OpponentRules.MaxCars
                            // A rival whose race with the player is still to be settled keeps their garage as it is
                            && !Opponents.OpponentLifeService.HasPendingRace(gameState, o)
                            && (o.Cars.Count == 0 || Simulation.RaceSimulatorService.GetCarHP(o.Cars[0]) < power))
                .OrderBy(o => o.Name, StringComparer.Ordinal)
                .ToList();
            if (wanting.Count == 0 || random.NextDouble() >= RivalBuyerChance) return null;

            return wanting[random.Next(wanting.Count)];
        }

        /// <summary>
        /// The chance a buyer calls on a given day: 60% for a car asked at 90% of its worth or less, and down by 12
        /// points for every tenth more, to nobody at 140%
        /// </summary>
        public static double OfferChance(decimal askingPrice, decimal value)
        {
            if (value <= 0) return 0.1;
            var ratio = (double)(askingPrice / value);
            return ratio <= 0.9 ? 0.6 : Math.Max(0, 0.6 - (ratio - 0.9) * 1.2);
        }

        /// <summary>
        /// What a buyer offers: the asking price for a car asked at no more than its worth; over that they haggle
        /// it down by up to 15% (<paramref name="roll"/> 0 to 1), though never below what the car is worth
        /// </summary>
        public static decimal OfferAmount(decimal askingPrice, decimal value, double roll)
        {
            if (askingPrice <= value) return askingPrice;

            var haggled = RoundToTen((double)askingPrice * (0.85 + 0.15 * Unit.Clamp01(roll, ifNotFinite: 0)));
            return Math.Clamp(haggled, Math.Max(0m, value), askingPrice);
        }

        /// <summary>The scrapyard's price as a share of the car's worth: <see cref="ScrapShare"/> ± <see cref="ScrapSpread"/>, the same for a car every time it is asked</summary>
        public static double ScrapFactor(Guid carInstanceId)
        {
            var roll = (BitConverter.ToUInt32(carInstanceId.ToByteArray(), 0) % 1000) / 999.0;
            return ScrapShare * (1 - ScrapSpread + 2 * ScrapSpread * roll);
        }

        private static decimal RoundToTen(double amount) =>
            double.IsFinite(amount) && amount > 0 ? Math.Round((decimal)Math.Min(amount, 1e12) / 10) * 10 : 0m;

        /// <summary>A buyer's name at the start of a sentence ("a kid from the high school" → "A kid ...")</summary>
        public static string Capitalized(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

        private SaleResult? WhyNotSellable(GameState gameState, Car car)
        {
            if (!gameState.Player.Cars.Contains(car))
                return new SaleResult(SaleOutcome.NotOwned, GameText.Say("sell.refused.not_owned"));

            if (gameState.PendingRace is { } race && race.PlayerCarInstanceId == car.InstanceId)
                return new SaleResult(SaleOutcome.Racing, GameText.Say("sell.refused.racing"));

            if (car.IsImpounded)
                return new SaleResult(SaleOutcome.Refused, GameText.Say("sell.refused.impounded"));

            return null;
        }

        /// <summary>The car leaves the garage, its ad with it; the selected car moves on to another one</summary>
        private static void RemoveFromGarage(GameState gameState, Car car)
        {
            var player = gameState.Player;
            player.Cars.Remove(car);
            car.ClearPlace();
            gameState.NewspaperAds.PlayerCars.RemoveAll(ad => ad.CarInstanceId == car.InstanceId);
            player.Stats.CarsSold++;
            gameState.Career.SyncStanding(player);
            if (player.SelectedCarInstanceId == car.InstanceId) player.SelectedCarInstanceId = player.Cars.FirstOrDefault()?.InstanceId;
        }

        /// <summary>Spends the time, then saves (the time and the new day it may end in belong in the save). True when the save failed</summary>
        private async Task<bool> SpendTimeAndSave(GameState gameState, GameAction action)
        {
            try
            {
                await _gameTimeService.SpendTimeAsync(gameState, action);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for {Action}", action);
            }

            return !Save(gameState);
        }

        private bool Save(GameState gameState) => GameSaves.TrySave(_gameStateRepository, gameState, _logger, "a sale");
    }
}
