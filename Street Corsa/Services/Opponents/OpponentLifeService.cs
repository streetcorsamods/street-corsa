using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.Career.Victory;
using StreetCorsa.Services.Career;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Police;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>The rivals' day off the track: see <see cref="OpponentLifeService"/></summary>
    public interface IOpponentLifeService
    {
        /// <summary>Every rival looks after their car, their money and their place on the street; see the class</summary>
        Task ReviewDayAsync(GameState gameState, DateTime currentDate);
    }

    /// <summary>
    /// The rivals live like the player does. Every morning each racer (the GameMaker version's scr_review_racers):
    /// <list type="bullet">
    /// <item>drives the best car they own, and sells what they don't need: a wreck for scrap, a spare over
    /// <see cref="OpponentRules.MaxCars"/> through the paper (<see cref="RivalCarAds"/>), to a dealer when nobody calls;</item>
    /// <item>without a car, buys one off the same lots the player buys from, or out of another rival's ad;</item>
    /// <item>with money to spare, now and then trades up to a clearly stronger car and puts the old one in the paper;</item>
    /// <item>with a car that can't race, has it repaired if the whole bill is affordable, else sells it and buys
    /// another if that gets them racing, else sits it out (retired) until they can;</item>
    /// <item>broke, scrapes some money together (<see cref="OpponentRules.IsBankrupt"/>) and comes back;</item>
    /// <item>now and then gives up the scene for good (<see cref="OpponentRules.Leaves"/>): sells up and leaves town.</item>
    /// </list>
    /// Then more racers come out onto the street as the weeks go by (<see cref="OpponentRules.MinActive"/>), new faces
    /// first and, once there are none left, racers who left long ago; and a few with money to spare tune their engines
    /// (<see cref="EngineTuner"/>), buying used out of the paper when it asks less and selling what came off there.
    /// What the street notices goes into <see cref="GameState.StreetTalk"/>.
    ///
    /// The King lives the same way, but stays out of sight (inactive) until the player has earned a shot at him.
    ///
    /// Dyno runs and tuning happen off the UI thread, on copies of the engines; the save only changes on the
    /// thread that called. What a review looks up once for the day (<see cref="Review"/>) is the review's own: two
    /// reviews may overlap (a second time advance while the first awaits the dyno), and neither sees the other's.
    /// The cars the rivals buy and sell are in the Market file, their tuning in the Tuning file.
    /// </summary>
    public partial class OpponentLifeService(
        ICarPartsService parts,
        IUsedCarMarketService market,
        IContentCatalogRepository catalogRepo,
        IOpponentInitializationService? initialization = null,
        Random? random = null,
        IPartShopService? partShops = null) : IOpponentLifeService
    {
        private readonly ICarPartsService _parts = parts;
        private readonly IUsedCarMarketService _market = market;
        private readonly IContentCatalogRepository _catalogRepo = catalogRepo;
        private readonly IOpponentInitializationService? _initialization = initialization;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("OpponentLife");
        private readonly Random _random = random ?? new();
        private readonly IPartShopService? _partShops = partShops;

        /// <summary>
        /// One morning's review, handed down every step of it: the day's catalog lookups (each model valued and named from
        /// the catalog once per review, not per car and line), the parts' groups, and what is found once a review and kept
        /// for the rest of it (the rivals' ads, the cars' definitions). Never kept on the service: a review that overlaps
        /// another must not share or clear its caches.
        /// </summary>
        private sealed class Review(Func<Car, decimal> valueOf, Func<string, string> carNames, Func<string, string?>? groupOf)
        {
            public Func<Car, decimal> ValueOf { get; } = valueOf;
            public Func<string, string> CarNames { get; } = carNames;

            /// <summary>The group of a part by its id; null without a parts catalog (the cars are judged on their own figures)</summary>
            public Func<string, string?>? GroupOf { get; } = groupOf;

            /// <summary>The rivals' ads that stood when first asked for (<see cref="LiveAds"/>); null: not asked yet, or started over</summary>
            public List<RivalCarAds.Offer>? LiveAds { get; set; }

            /// <summary>The cars' definitions found so far (<see cref="Definition"/>)</summary>
            public Dictionary<string, Models.Catalog.CarDefinition?>? Definitions { get; set; }
        }

        public async Task ReviewDayAsync(GameState gameState, DateTime currentDate)
        {
            try
            {
                _initialization?.EnsureBosses(gameState);
            }
            catch (Exception ex)
            {
                // The racers on the street still have their day; the bosses are looked for again tomorrow
                _logger.Error(ex, "Could not bring in the bosses");
            }

            try
            {
                _initialization?.EnsureNewcomers(gameState);
            }
            catch (Exception ex)
            {
                // The racers on the street still have their day; the newcomers are looked for again tomorrow
                _logger.Error(ex, "Could not bring in the racers defined since this game began");
            }

            var review = new Review(_market.Valuer(), CarNames.Book(_catalogRepo), PartGroups());
            var talk = new List<string>();

            try
            {
                await ReviewEveryoneAsync(gameState, currentDate, review, talk);
            }
            finally
            {
                // What happened before a later step failed did happen: the street talks about it all the same
                AddTalk(gameState, currentDate, talk);
            }

            _logger.Information("Rivals reviewed: {Ready} racing, {Retired} sitting out, {Inactive} not on the street yet; {Talk} thing(s) to talk about",
                gameState.Racers.ReadyToRace.Count, gameState.Racers.Retired.Count, gameState.Racers.Inactive.Count, talk.Count);
        }

        private async Task ReviewEveryoneAsync(GameState gameState, DateTime currentDate, Review review, List<string> talk)
        {
            await GiveCarsTheirPartsAsync(gameState);
            await PutOnDynoAsync(gameState);

            // Those who sat out first: they may be back today. A boss is reviewed wherever he is kept.
            var racers = gameState.Racers.Retired.Values
                .Concat(gameState.Racers.ReadyToRace.Values)
                .Concat(gameState.Racers.Inactive.Values.Where(r => r is Opponent { IsBoss: true }))
                .OfType<Opponent>()
                .ToList();
            RivalCarAds.Tidy(gameState);
            foreach (var racer in racers) ReviewRacer(gameState, racer, currentDate, review, talk);
            ShopTheSales(gameState, currentDate, review, talk);
            RivalCarAds.Tidy(gameState);

            Activate(gameState, currentDate, review, talk);

            // The cars bought this morning off the lots get their factory parts now, all of them on one worker, before
            // the tuners look at the engines: a purchase never waits for the dyno on the caller's thread
            await GiveCarsTheirPartsAsync(gameState);

            await TuneAsync(gameState, currentDate, review, talk);

            // A rematch nobody came for is off. Last, next to the talk it makes: a review that fails before this
            // leaves the grudges for tomorrow's rather than dropping them without a word.
            Grudges.Lapse(gameState.Racers.All.OfType<Opponent>(), currentDate, review.CarNames, talk);
        }

        // ----- one racer -----

        private void ReviewRacer(GameState gameState, Opponent racer, DateTime date, Review review, List<string> talk)
        {
            try
            {
                // The race with the player is not settled yet: its result lands on the car they raced, so their cars
                // are left as they are until it is (not sold, traded, advertised, repaired or reordered)
                var carsHeld = HasPendingRace(gameState, racer);
                if (carsHeld)
                {
                    _logger.Information("{Racer} has a race with the player to settle: their cars wait for it", racer.Name);
                }
                else
                {
                    CollectFromImpound(racer, date, review, talk);
                    LookAfter(racer);
                    PickBestCar(racer, review);
                    ReviewAds(gameState, racer, date, review, talk);
                    SellSpares(gameState, racer, date, review, talk);

                    if (racer.Cars.Count == 0) TryBuy(gameState, racer, date, review, talk);

                    // A car in the impound can be neither fixed nor sold: they wait for it
                    if (racer.Cars.FirstOrDefault() is { IsImpounded: false } car && !CanRace(car, review)
                        && !TryRepair(gameState, racer, car, review, talk))
                    {
                        SellAndBuyAnother(gameState, racer, car, date, review, talk);
                    }
                }

                if (OpponentRules.IsBankrupt(IsReady(racer, review), racer.Money, CheapestCar(gameState)))
                {
                    // Once per spell of being broke, not once per day of it
                    if (!racer.IsBroke) racer.TimesBroke++;
                    racer.IsBroke = true;
                    if (racer.TimesBroke >= OpponentRules.MaxTimesBroke && CanLeave(gameState, racer))
                    {
                        Leave(gameState, racer, date, Say(review, "street.broke_quit", racer), review, talk);
                        return;
                    }

                    var cash = OpponentRules.CashInjection(CheapestCar(gameState), _random.NextDouble());
                    racer.Money += cash;
                    talk.Add(Say(review, "street.broke", racer, values: new TextValues { ["cash"] = cash }));
                    _logger.Information("{Racer} is broke: ${Cash} injected, now ${Money}", racer.Name, cash, racer.Money);
                }
                else
                {
                    racer.IsBroke = false;

                    // Not on the day they bought one: a car is driven a while before it is judged
                    if (!carsHeld && IsReady(racer, review) && racer.Cars[0].PurchaseDate.Date != date.Date
                        && _random.NextDouble() < OpponentRules.TradeUpChance)
                    {
                        TryTradeUp(gameState, racer, date, review, talk);
                    }
                }

                MoveToWhereTheyBelong(gameState, racer, date, review, talk);
                MaybeLeave(gameState, racer, date, review, talk);
            }
            catch (Exception ex)
            {
                // One racer's bad car is not everybody's day
                _logger.Error(ex, "Could not review {Racer}", racer.Name);
            }
        }

        private void MoveToWhereTheyBelong(GameState gameState, Opponent racer, DateTime date, Review review, List<string> talk)
        {
            var ready = IsReady(racer, review);
            var target = racer.IsBoss
                ? ready && Bosses.PathOpen(racer, gameState.Career) ? RacerStatus.ReadyToRace : RacerStatus.Inactive
                : ready ? RacerStatus.ReadyToRace : RacerStatus.Retired;

            // How long they have been sitting out, for whether they give up (OpponentRules.Leaves)
            racer.SittingOutSince = target == RacerStatus.Retired ? racer.SittingOutSince ?? date : null;
            if (racer.Status == target) return;

            var from = racer.Status;
            gameState.Racers.MoveRacer(racer.Name, target);

            if (racer.IsBoss)
            {
                if (target == RacerStatus.ReadyToRace) talk.Add(Say(review, RacerWords.Key("street.king_out", racer), racer));
            }
            else if (target == RacerStatus.ReadyToRace && from == RacerStatus.Retired)
            {
                talk.Add(Say(review, "street.back_on_street", racer));
            }
            else if (target == RacerStatus.Retired)
            {
                talk.Add(racer.Cars.Count == 0
                    ? Say(review, "street.no_car", racer)
                    : Say(review, racer.Cars[0].IsImpounded ? "street.car_impounded" : "street.car_laid_up", racer, racer.Cars[0]));
            }

            _logger.Information("{Racer}: {From} -> {To}", racer.Name, from, target);
        }

        // ----- leaving the scene -----

        /// <summary>Laid up for too long, or just moving on: see <see cref="OpponentRules.Leaves"/></summary>
        private void MaybeLeave(GameState gameState, Opponent racer, DateTime date, Review review, List<string> talk)
        {
            if (racer.Status is not (RacerStatus.ReadyToRace or RacerStatus.Retired)) return;

            var sittingOut = racer.SittingOutSince is { } since ? (int?)(date.Date - since.Date).Days : null;
            if (!OpponentRules.Leaves(racer.TimesBroke, sittingOut, _random.NextDouble()) || !CanLeave(gameState, racer)) return;

            var why = sittingOut >= OpponentRules.LaidUpDays && racer.Cars.FirstOrDefault() is { } car
                ? Say(review, "street.gave_up", racer, car)
                : Say(review, "street.left_town", racer);
            Leave(gameState, racer, date, why, review, talk);
        }

        /// <summary>
        /// Not the King, and not with something open with the player: a race about to start, a rematch they want, an
        /// offer on the player's car
        /// </summary>
        private static bool CanLeave(GameState gameState, Opponent racer)
        {
            if (racer.IsBoss || racer.Grudge != null) return false;
            if (HasPendingRace(gameState, racer)) return false;
            return !gameState.NewspaperAds.PlayerCars.Any(a => string.Equals(a.Offer?.RivalName, racer.Name, StringComparison.Ordinal));
        }

        /// <summary>
        /// The racer of the race with the player that has not been settled yet (<see cref="GameState.PendingRace"/>):
        /// their cars are left alone until it is, or its result would land on a car that is no longer theirs
        /// </summary>
        public static bool HasPendingRace(GameState gameState, Racer racer) =>
            string.Equals(gameState.PendingRace?.OpponentName, racer.Name, StringComparison.Ordinal);

        /// <summary>
        /// Gone for good: the cars go to a dealer (a wreck for scrap; one in the impound stays with the police), their
        /// ads come down, and they leave the street (<see cref="RacerStatus.Departed"/>). The parts they put in the
        /// paper stay there; nobody is paid for them any more.
        /// </summary>
        private void Leave(GameState gameState, Opponent racer, DateTime date, string why, Review review, List<string> talk)
        {
            foreach (var car in racer.Cars.ToList())
            {
                if (car.IsImpounded) racer.Cars.Remove(car);
                else Sell(gameState, racer, car, date, review, []);
            }

            gameState.NewspaperAds.RivalCars.RemoveAll(a => a.RivalName == racer.Name);
            foreach (var ad in gameState.NewspaperAds.Parts.Where(a => a.SellerRival == racer.Name)) ad.SellerRival = null;

            racer.Grudge = null;
            racer.LeftDate = date;
            racer.SittingOutSince = null;
            gameState.Racers.MoveRacer(racer.Name, RacerStatus.Departed);

            talk.Add(why);
            _logger.Information("{Racer} left the scene: {Why}", racer.Name, why);
        }

        /// <summary>
        /// Cars whose days in the impound are up come home, when the racer can pay for them. One left there too long
        /// (<see cref="PoliceRules.AuctionAfterDays"/>) is auctioned off by the police, and it is gone.
        /// </summary>
        private void CollectFromImpound(Opponent racer, DateTime date, Review review, List<string> talk)
        {
            foreach (var car in racer.Cars.Where(c => PoliceRules.CanCollect(c, date)).ToList())
            {
                if (racer.Money >= car.ImpoundFee)
                {
                    racer.Money -= car.ImpoundFee;
                    _logger.Information("{Racer} collected the {Car} from the impound for ${Fee}", racer.Name, car.DefinitionId, car.ImpoundFee);
                    PoliceRules.Release(car);
                    talk.Add(Say(review, "street.impound_collected", racer, car));
                }
                else if (PoliceRules.IsForAuction(car, date))
                {
                    racer.Cars.Remove(car);
                    _logger.Information("{Racer} never paid to collect the {Car}: the police auctioned it off", racer.Name, car.DefinitionId);
                    talk.Add(Say(review, "street.impound_auctioned", racer, car));
                }
            }
        }

        /// <summary>The car the racer means to drive goes to the front of their cars, where every screen looks for it</summary>
        private void PickBestCar(Opponent racer, Review review)
        {
            if (racer.Cars.Count < 2) return;

            var best = racer.Cars.OrderByDescending(c => Score(c, review)).First();
            if (ReferenceEquals(best, racer.Cars[0])) return;

            racer.Cars.Remove(best);
            racer.Cars.Insert(0, best);
            _logger.Information("{Racer} now drives the {Car}", racer.Name, best.DefinitionId);
        }

        private double Score(Car car, Review review) =>
            OpponentRules.CarScore(car.PowerHp ?? 0, CarValuation.ConditionOf(car), CanRace(car, review), ValueOf(car, review));

        /// <summary>
        /// Wrecks they don't drive go for scrap, and cars beyond the spare into the paper (a dealer takes them when
        /// nobody calls, see <see cref="ReviewAds"/>)
        /// </summary>
        private void SellSpares(GameState gameState, Opponent racer, DateTime date, Review review, List<string> talk)
        {
            // Cars in the impound can't be sold: with nothing else to spare they wait
            foreach (var wreck in racer.Cars.Skip(1).Where(c => !c.IsImpounded && CarCondition.IsTotaled(c)).ToList())
            {
                Sell(gameState, racer, wreck, date, review, talk);
            }

            var over = racer.Cars.Count - OpponentRules.MaxCars;
            if (over <= 0) return;

            var advertised = RivalCarAds.AdvertisedBy(gameState, racer);
            var spares = racer.Cars.Skip(1)
                .Where(c => !c.IsImpounded && !advertised.Contains(c.InstanceId))
                .OrderBy(c => Score(c, review))
                .Take(over - racer.Cars.Skip(1).Count(c => advertised.Contains(c.InstanceId)))
                .ToList();
            foreach (var spare in spares) Advertise(gameState, racer, spare, date, review, talk);
        }

        /// <summary>What a racer asks in the paper for a car worth <paramref name="value"/>, <paramref name="roll"/> 0 to 1 picking where in the range</summary>
        private static decimal AskingFor(GameState gameState, decimal value, double roll) =>
            OpponentRules.AskingPrice(GameRules.Scale(value, gameState.Rules.CarPriceMultiplier),
                Math.Round(value * (decimal)CarSaleService.DealerShare), roll);

        /// <summary>The car in the paper, asking less than a lot would and more than a dealer pays</summary>
        /// <param name="quiet">Nothing said about the ad: what was said about the car already told it</param>
        private void Advertise(GameState gameState, Opponent racer, Car car, DateTime date, Review review, List<string> talk, bool quiet = false)
        {
            var asking = AskingFor(gameState, ValueOf(car, review), _random.NextDouble());
            if (asking <= 0)
            {
                Sell(gameState, racer, car, date, review, talk);
                return;
            }

            // What the seller says about the engine, as a lot would: the ad keeps it, the paper's page reads it from there
            var engine = _market.DescribeEngine(car);
            RivalCarAds.Post(gameState, racer, car, asking, date, engine?.Summary, engine?.IsModified ?? false,
                _parts.IsAvailable ? _parts.Catalog : null, _market.FactoryParts(car.DefinitionId));
            review.LiveAds = null;
            if (!quiet) talk.Add(Say(review, "street.advertised", racer, car, new TextValues { ["price"] = asking }));
            _logger.Information("{Racer} advertised the {Car} for ${Asking}", racer.Name, car.DefinitionId, asking);
        }

        /// <summary>
        /// Their ads in the paper: one nobody answered for a week comes down in price, and one that ran its time goes
        /// to a dealer after all
        /// </summary>
        private void ReviewAds(GameState gameState, Opponent racer, DateTime date, Review review, List<string> talk)
        {
            foreach (var (ad, _, car) in RivalCarAds.LiveOf(gameState, racer))
            {
                // By the calendar, as the other day counts are: not by the hour the review happens to run
                var days = (date.Date - ad.PostedDate.Date).Days;
                if (days >= CarSaleService.AdDays)
                {
                    talk.Add(Say(review, "street.ad_unanswered", racer, car));
                    Sell(gameState, racer, car, date, review, talk);
                }
                else if (days >= OpponentRules.AskReducedAfterDays && !ad.Reduced)
                {
                    // Never up: a price the player talked down under the dealer's share stays where it was agreed
                    var floor = Math.Round(ValueOf(car, review) * (decimal)CarSaleService.DealerShare);
                    ad.AskingPrice = Math.Min(ad.AskingPrice, Math.Max(floor, CarValuation.RoundToHundred(ad.AskingPrice * OpponentRules.AskReduction)));
                    ad.Reduced = true;
                }
            }
        }

        /// <summary>A dealer buys the car at its share and puts it on the lot; a wreck goes for scrap</summary>
        private decimal Sell(GameState gameState, Opponent racer, Car car, DateTime date, Review review, List<string> talk)
        {
            var value = ValueOf(car, review);
            var totaled = CarCondition.IsTotaled(car);
            var paid = Math.Round(value * (decimal)(totaled ? CarSaleService.ScrapFactor(car.InstanceId) : CarSaleService.DealerShare));

            // The listing first (the ad's words can fail): a failure there must not leave the car gone and the racer paid
            var listing = totaled ? null
                : _market.ListCar(car, GameRules.Scale(value, gameState.Rules.CarPriceMultiplier), _market.TradeInLocation(gameState.DealerLocations, car), date);

            racer.Cars.Remove(car);
            racer.Money += paid;
            racer.Stats.CarsSold++;
            if (RivalCarAds.AdFor(gameState, car) is { } ad) gameState.NewspaperAds.RivalCars.Remove(ad);
            if (listing != null) gameState.UsedCarMarket.Add(listing);

            talk.Add(Say(review, totaled ? "street.sold_scrap" : "street.sold_dealer", racer, car, new TextValues { ["price"] = paid }));
            _logger.Information("{Racer} sold the {Car} for ${Paid}{Scrap}", racer.Name, car.DefinitionId, paid, totaled ? " (scrap)" : "");
            return paid;
        }

        /// <summary>
        /// A rival's car races full and clean: their races leave its tank and its oil alone (<see cref="CarCondition.ApplyRace"/>),
        /// and a car that came to them from the player is filled up, topped up with oil and washed the next morning
        /// </summary>
        private static void LookAfter(Opponent racer)
        {
            foreach (var car in racer.Cars)
            {
                car.FuelLitres = null;
                car.OilLevel = null;
                car.BodyDirt = 0;
            }
        }

        /// <summary>The whole bill or nothing: half a repair doesn't get a car racing</summary>
        private bool TryRepair(GameState gameState, Opponent racer, Car car, Review review, List<string> talk)
        {
            // What gets the car racing: a rival does not pay for its looks
            var jobs = RepairShop.Jobs(car, _parts.IsAvailable ? _parts.Catalog : null, gameState.Rules.PartPriceMultiplier)
                .Where(j => !RepairShop.IsCosmetic(j)).ToList();
            var bill = jobs.Sum(j => j.Cost);
            if (jobs.Count == 0 || bill > racer.Money) return false;

            foreach (var job in jobs) job.Apply();
            racer.Money -= bill;

            talk.Add(Say(review, "street.repaired", racer, car, new TextValues { ["price"] = bill }));
            _logger.Information("{Racer} repaired the {Car}: {Jobs} for ${Bill}", racer.Name, car.DefinitionId, string.Join(", ", jobs.Select(j => j.Id)), bill);
            return CanRace(car, review);
        }

        /// <summary>A car too dear to fix is sold, when what it fetches buys one that runs</summary>
        private void SellAndBuyAnother(GameState gameState, Opponent racer, Car car, DateTime date, Review review, List<string> talk)
        {
            // A listing with no power figure runs on the factory engine nobody has put on the dyno: a runner all the same
            var cheapestRunner = gameState.UsedCarMarket.Where(l => l.IsOnALot && l.PowerHp is null or > 0).Select(l => (decimal?)l.Price).Min();
            if (cheapestRunner == null) return;

            var value = ValueOf(car, review);
            var fetches = value * (decimal)(CarCondition.IsTotaled(car) ? CarSaleService.ScrapFactor(car.InstanceId) : CarSaleService.DealerShare);
            if (racer.Money + fetches < cheapestRunner) return;

            Sell(gameState, racer, car, date, review, talk);
            if (racer.Cars.Count == 0 || !CanRace(racer.Cars[0], review)) TryBuy(gameState, racer, date, review, talk);
        }

        // ----- the street -----

        /// <summary>More racers come out as the weeks go by; one whose car can't race yet comes out to fix it first</summary>
        private void Activate(GameState gameState, DateTime date, Review review, List<string> talk)
        {
            var racers = gameState.Racers;
            // Those who left count: once the new faces run out, they are who comes back
            var street = racers.All.Concat(racers.Departed.Values)
                .Count(r => r is not Opponent { IsBoss: true });
            var ready = racers.ReadyToRace.Values.Count(r => r is not Opponent { IsBoss: true });
            var wanted = OpponentRules.MinActive(date, street) - ready;
            if (wanted <= 0) return;

            var newcomers = racers.Inactive.Values.OfType<Opponent>().Where(o => !o.IsBoss).OrderBy(_ => _random.Next()).Take(wanted).ToList();
            var comebacks = ComeBacks(gameState, date, wanted - newcomers.Count);
            foreach (var back in comebacks)
            {
                racers.MoveRacer(back.Name, RacerStatus.Inactive);
                talk.Add(Say(review, "street.back_in_town", back));
            }

            foreach (var racer in newcomers.Concat(comebacks))
            {
                racers.MoveRacer(racer.Name, RacerStatus.Retired);
                ReviewRacer(gameState, racer, date, review, talk);
                var backOnTheStreet = Said(review, "street.back_on_street", racer);
                talk.RemoveAll(backOnTheStreet.Contains);

                // One who came back was announced as that: not a new face
                if (racer.Status == RacerStatus.ReadyToRace && !comebacks.Contains(racer))
                {
                    talk.Add(racer.Cars.FirstOrDefault() is { } car
                        ? Say(review, "street.new_face_car", racer, car)
                        : Say(review, "street.new_face", racer));
                    NewFaceArrived?.Invoke(gameState, racer);
                }
            }
        }

        /// <summary>
        /// Racers who left at least <see cref="OpponentRules.ComeBackAfterDays"/> ago, the longest gone first, with a
        /// fresh start: new money, nothing owed, broke no more
        /// </summary>
        private List<Opponent> ComeBacks(GameState gameState, DateTime date, int count)
        {
            if (count <= 0) return [];

            var back = gameState.Racers.Departed.Values.OfType<Opponent>()
                .Where(o => !o.IsBoss && o.LeftDate is { } left && (date.Date - left.Date).Days >= OpponentRules.ComeBackAfterDays)
                .OrderBy(o => o.LeftDate)
                .Take(count)
                .ToList();
            foreach (var racer in back)
            {
                racer.Money = _initialization?.StartingMoney(racer) ?? OpponentRules.CashInjection(CheapestCar(gameState), _random.NextDouble());
                racer.TimesBroke = 0;
                racer.IsBroke = false;
                racer.LeftDate = null;
                _logger.Information("{Racer} came back to town with ${Money}", racer.Name, racer.Money);
            }

            return back;
        }

        // ----- the parts and the dyno -----

        /// <summary>
        /// Cars from before rivals had parts, and the ones bought this morning, get their factory ones: all of them put
        /// together on one worker (<see cref="ICarPartsService.EnsurePartsAsync(IEnumerable{Car})"/>), the cars changed here
        /// </summary>
        private async Task GiveCarsTheirPartsAsync(GameState gameState)
        {
            if (!_parts.IsAvailable) return;

            var lacking = AllRivalCars(gameState)
                .Where(c => !c.HasPartsAssigned || !c.HasRunningGearAssigned || c.GearVersion < RunningGearFactory.GearVersion)
                .ToList();
            if (lacking.Count == 0) return;

            try
            {
                await _parts.EnsurePartsAsync(lacking);
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not give the rivals' cars their parts: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Every rival car and every car for sale without a figure goes on the dyno: the rivals' because wear and
        /// repairs change it, the lots' once. On copies, off the UI thread.
        /// </summary>
        private async Task PutOnDynoAsync(GameState gameState)
        {
            if (!_parts.IsAvailable) return;

            var cars = AllRivalCars(gameState).Select(c => (Car: c, Engine: c.Engine is { } e ? PartTrees.Clone(e) : null)).ToList();
            var listings = gameState.UsedCarMarket
                .Where(l => !l.IsSold && l.PowerHp == null)
                .Select(l => (Listing: l, Engine: l.Parts.FirstOrDefault(p => p.ParentSlot == PartInstance.CarEngineSlot) is { } e ? PartTrees.Clone(e) : null))
                .ToList();

            var catalog = _parts.Catalog;
            double Power(PartInstance? engine)
            {
                if (engine == null) return 0;
                try
                {
                    return MarketPower(EngineFactory.Evaluate(catalog, engine));
                }
                catch (Exception ex)
                {
                    _logger.Warning("An engine could not go on the dyno: {Error}", ex.Message);
                    return 0;
                }
            }

            var (carPower, listingPower) = await Task.Run(() =>
                (cars.Select(c => Power(c.Engine)).ToList(), listings.Select(l => Power(l.Engine)).ToList()));

            // A listing without an engine sells with the factory one: it runs, but nobody knows how well
            for (var i = 0; i < cars.Count; i++) cars[i].Car.PowerHp = carPower[i];
            for (var i = 0; i < listings.Count; i++) listings[i].Listing.PowerHp = listings[i].Engine == null ? null : listingPower[i];
        }

        private static IEnumerable<Car> AllRivalCars(GameState gameState) =>
            gameState.Racers.All
                .Where(r => r.Type == RacerType.AI)
                .SelectMany(r => r.Cars);

        private static double MarketPower(StreetCorsa.Parts.Logic.EngineReport? report) => UsedCarMarketService.PowerOf(report);

        private Func<string, string?>? PartGroups()
        {
            try
            {
                return _parts.IsAvailable ? CarCondition.Groups(_parts.Catalog) : null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The parts catalog could not be read: the rivals' cars are judged on their own figures");
                return null;
            }
        }

        private static bool CanRace(Car car, Review review) => CarCondition.WhyCannotRace(car, review.GroupOf).Count == 0;

        private static bool IsReady(Racer racer, Review review) =>
            racer.Cars.FirstOrDefault() is { } car && CanRace(car, review);

        /// <summary>The cheapest car on a lot (<see cref="CarsForSale"/>: the lots are what the rivals' living goes by)</summary>
        private static decimal? CheapestCar(GameState gameState) =>
            gameState.UsedCarMarket.Where(l => l.IsOnALot && l.Price > 0).Select(l => (decimal?)l.Price).Min();

        private decimal ValueOf(Car car, Review review)
        {
            try
            {
                return review.ValueOf(car);
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not value the {Car}; going by what was paid for it: {Error}", car.DefinitionId, ex.Message);
                return CarValuation.PaidFor(car);
            }
        }

        private static string Nickname(Opponent racer) => string.IsNullOrWhiteSpace(racer.Nickname) ? "" : $" \"{racer.Nickname}\"";

        /// <summary>A line of the street's talk about a racer (the mods' "street.*" text), and their car when it is about one</summary>
        // A racer may have lines of his own (RacerWords)
        private string Say(Review review, string key, Opponent racer, Car? car = null, TextValues? values = null) =>
            GameText.Installed.Pick(RacerWords.Key(key, racer), _random, With(review, racer, car, values));

        /// <summary>Every way a line can come out for this racer: to take back what was said</summary>
        private static HashSet<string> Said(Review review, string key, Opponent racer, Car? car = null) =>
            GameText.Installed.Lines(RacerWords.Key(key, racer)).Select(line => GameText.Fill(line, With(review, racer, car, null))).ToHashSet();

        private static TextValues With(Review review, Opponent racer, Car? car, TextValues? values)
        {
            values ??= new TextValues();
            values["name"] = racer.Name;
            values["nickname"] = Nickname(racer);
            values["possessive"] = OpponentRules.Possessive(racer);
            if (car != null) values["car"] = review.CarNames(car.DefinitionId);
            return values;
        }

        /// <summary>
        /// The day's talk after what was said before; after <see cref="OpponentRules.StreetTalkDays"/> nobody talks about it
        /// any more, and the street remembers <see cref="OpponentRules.MaxStreetTalk"/> things at most
        /// </summary>
        public static void AddTalk(GameState gameState, DateTime date, IEnumerable<string> lines)
        {
            gameState.StreetTalk ??= [];
            var added = lines.Select(line => new StreetTalkItem(date, line)).ToList();
            DatedLog.Append(gameState.StreetTalk, added, item => item.Date, date, OpponentRules.StreetTalkDays, OpponentRules.MaxStreetTalk);
            TalkAdded?.Invoke(gameState, added);
        }

        /// <summary>Told of every new line of street talk: the player's AI tells it again (<see cref="Ai.AiWriter.StreetTalkAdded"/>)</summary>
        public static Action<GameState, IReadOnlyList<StreetTalkItem>>? TalkAdded { get; set; }

        /// <summary>Told of a new face on the street: ComfyUI paints them a portrait (<see cref="Ai.AiWriter.NewFace"/>)</summary>
        public static Action<GameState, Opponent>? NewFaceArrived { get; set; }
    }
}
