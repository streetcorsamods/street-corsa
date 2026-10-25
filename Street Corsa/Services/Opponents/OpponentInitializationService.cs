using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// Service for initializing opponents when creating a new game
    /// </summary>
    public class OpponentInitializationService : IOpponentInitializationService
    {
        private readonly IOpponentRepository _opponentRepository;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly ICarPartsService? _parts;
        private readonly IAppLogger _logger;
        private readonly Random _random;

        /// <summary>The game state whose bosses were last looked for: the definitions are read once per game, not once a day</summary>
        private WeakReference<GameState>? _bossesChecked;

        /// <summary>
        /// The racer definitions read for a game state, shared by <see cref="EnsureBosses(GameState)"/> and
        /// <see cref="EnsureNewcomers"/> (each takes racers of its own from them: bosses, the rest)
        /// </summary>
        private (WeakReference<GameState> Game, List<Opponent> Definitions)? _definitions;

        /// <summary>
        /// Where a racer's skill counts from for their money and their car's shape: ten under the floor
        /// (<see cref="Opponent.MinSkill"/>), so the least skilled racer counts 10 and the most skilled 20
        /// </summary>
        private const int SkillBaseline = Opponent.MinSkill - 10;

        /// <param name="parts">
        /// Gives the rivals' cars their engines and running gear (a used engine, now and then worked on); null, or no
        /// parts, and the cars get them the first time they are looked at
        /// </param>
        public OpponentInitializationService(
            IOpponentRepository opponentRepository,
            IContentCatalogRepository catalogRepo,
            ICarProfileRepository profileRepo,
            ICarPartsService? parts = null)
        {
            _opponentRepository = opponentRepository;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _parts = parts;
            _logger = AppLoggerFactory.CreateLogger("OpponentInitialization");
            _random = new Random();
        }

        /// <summary>
        /// Initialize opponents for a new game
        /// </summary>
        public void InitializeOpponents(GameState gameState)
        {
            _logger.Information("Initializing opponents for new game");

            // Load all available opponents; a boss is set up on his own
            var allOpponents = Definitions(gameState).Where(o => !o.IsBoss).ToList();

            if (allOpponents.Count == 0)
            {
                _logger.Warning("No opponent definitions found. Game will have no AI racers.");
                return;
            }

            _logger.Information("Loaded {Count} opponent definitions", allOpponents.Count);

            // Every one of them: EnsureNewcomers would bring in any left out on the first day anyway
            var selectedOpponents = allOpponents;

            // The cars they can have, looked up once for all of them: installed ones only (AC cannot race a car
            // that was deleted from content\cars, however long the catalog remembers it), with a price
            var carPool = BuildCarPool();

            // A few racers are out on the street when the career starts; the rest turn up as the weeks go by
            // (OpponentLifeService). Who comes first is the luck of the draw.
            var onTheStreet = OpponentRules.MinActive(gameState.Date, selectedOpponents.Count);
            var order = selectedOpponents.OrderBy(_ => _random.Next()).ToList();

            // Add opponents to game state
            for (var i = 0; i < order.Count; i++)
            {
                var opponent = order[i];
                opponent.Status = i < onTheStreet ? RacerStatus.ReadyToRace : RacerStatus.Inactive;

                // Generate and assign a used car to this opponent
                var car = GenerateOpponentCar(opponent, carPool, gameState.Date);
                if (car != null)
                {
                    opponent.Cars.Add(car);
                    _logger.Debug("Assigned car {CarId} to opponent {Name}",
                        car.DefinitionId, opponent.Name);
                }
                else
                {
                    _logger.Warning("Failed to assign car to opponent {Name}", opponent.Name);
                }

                // Initialize opponent's bankroll
                opponent.Money = GenerateOpponentMoney(opponent);
                _logger.Debug("Set bankroll for opponent {Name}: ${Money:N0}",
                    opponent.Name, opponent.Money);

                // Add to racer collection
                gameState.Racers.AddRacer(opponent);

                _logger.Debug("Added opponent: {Name} (Skill: {Skill}, Aggression: {Aggression})",
                    opponent.Name, opponent.Skill, opponent.Aggression);
            }

            _logger.Information("Successfully initialized {Count} opponents, {Street} of them on the street", selectedOpponents.Count, onTheStreet);

            EnsureBosses(gameState, carPool);
        }

        public void EnsureBosses(GameState gameState) => EnsureBosses(gameState, null);

        /// <summary>The game state last brought up to date: the definitions are read once per game, not once a day</summary>
        private WeakReference<GameState>? _newcomersChecked;

        public void EnsureNewcomers(GameState gameState)
        {
            if (_newcomersChecked != null && _newcomersChecked.TryGetTarget(out var done) && ReferenceEquals(done, gameState)) return;

            var definitions = Definitions(gameState);
            var racers = gameState.Racers;
            var known = racers.All.Concat(racers.Departed.Values).OfType<Opponent>().ToList();

            // The portrait is the definition's: drawn after the save began, or moved by its mod; so is the
            // kind of car he drives, which the save does not keep. A save from before empty strings were kept may hold
            // a racer without a definition id at all.
            foreach (var racer in known.Where(r => !string.IsNullOrEmpty(r.DefinitionId)))
            {
                var definition = definitions.FirstOrDefault(d => d.DefinitionId == racer.DefinitionId);
                if (!string.IsNullOrEmpty(definition?.PortraitPath)) racer.PortraitPath = definition.PortraitPath;
                racer.Drives = definition?.Drives;
            }

            // Known by definition, and by name for racers from before definitions were remembered
            var newcomers = definitions
                .Where(d => !d.IsBoss
                            && !known.Any(r => (!string.IsNullOrEmpty(d.DefinitionId) && r.DefinitionId == d.DefinitionId)
                                               || string.Equals(r.Name, d.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (newcomers.Count == 0)
            {
                _newcomersChecked = new WeakReference<GameState>(gameState);
                return;
            }

            var carPool = BuildCarPool();
            foreach (var newcomer in newcomers)
            {
                newcomer.Status = RacerStatus.Inactive;
                if (GenerateOpponentCar(newcomer, carPool, gameState.Date) is { } car) newcomer.Cars.Add(car);
                newcomer.Money = GenerateOpponentMoney(newcomer);
                racers.AddRacer(newcomer);
            }

            // Done for this game only once it all went through: a failure is tried again with tomorrow's review
            _newcomersChecked = new WeakReference<GameState>(gameState);
            _logger.Information("{Count} racer(s) defined since this game began join it, not on the street yet", newcomers.Count);
        }

        /// <summary>The racer definitions, read once for <paramref name="gameState"/></summary>
        private List<Opponent> Definitions(GameState gameState)
        {
            if (_definitions is { } read && read.Game.TryGetTarget(out var game) && ReferenceEquals(game, gameState)) return read.Definitions;

            var definitions = _opponentRepository.LoadAllOpponents();
            _definitions = (new WeakReference<GameState>(gameState), definitions);
            return definitions;
        }

        public decimal StartingMoney(Opponent opponent) => GenerateOpponentMoney(opponent);

        /// <summary>
        /// Every boss the mods define that the game does not have yet (a new game, a save from before him, a mod
        /// added since) joins it out of sight, with his money, his record and his car. One the game has, known by
        /// definition, is a boss of the path his definition names now (a racer a mod update made a boss becomes one).
        /// A boss from before definitions were remembered stands for the one of his path. Once per game state, once it
        /// all went through: a failure is tried again with the next review.
        /// </summary>
        private void EnsureBosses(GameState gameState, List<(CarDefinition Car, CarProfile Profile)>? carPool)
        {
            if (_bossesChecked != null && _bossesChecked.TryGetTarget(out var checkedState) && ReferenceEquals(checkedState, gameState)) return;

            var racers = gameState.Racers;
            var known = racers.All.Concat(racers.Departed.Values).OfType<Opponent>().ToList();
            var bosses = Definitions(gameState).Where(o => o.IsBoss).ToList();
            if (bosses.Count == 0) _logger.Warning("No boss among the racer definitions: a path that ends at one can't be won");

            foreach (var boss in bosses)
            {
                if (boss.DefinitionId.Length > 0 && known.FirstOrDefault(r => r.DefinitionId == boss.DefinitionId) is { } racer)
                {
                    if (!racer.IsBoss || racer.BossPath != boss.BossPath)
                    {
                        _logger.Information("{Racer} is now a boss, of the {Path} path", racer.Name, boss.BossPath.Length > 0 ? boss.BossPath : "(none)");
                        racer.IsBoss = true;
                        racer.BossPath = boss.BossPath;
                    }
                }
                else if (known.FirstOrDefault(r => r.IsBoss && r.DefinitionId.Length == 0 && r.BossPath == boss.BossPath) is { } old)
                {
                    _logger.Debug("{Old} stands for {Boss}, the boss of his path from before definitions were remembered", old.Name, boss.Name);
                }
                else if (known.FirstOrDefault(r => string.Equals(r.Name, boss.Name, StringComparison.OrdinalIgnoreCase)) is { } namesake)
                {
                    _logger.Warning("{Boss} is not brought in: the game has a racer by that name already ({Id})", boss.Name, namesake.DefinitionId);
                }
                else
                {
                    AddBoss(gameState, boss, carPool ??= BuildCarPool());
                    known.Add(boss);
                }
            }

            Bosses.Refresh(gameState);
            _bossesChecked = new WeakReference<GameState>(gameState);
        }

        private void AddBoss(GameState gameState, Opponent king, List<(CarDefinition Car, CarProfile Profile)> carPool)
        {
            // His own figures, the rules' (rivals.boss) for what they leave out
            var record = (king.BossRecord ?? new BossRecord()).Over(Rules.RuleBook.Current.Rivals.Boss);

            // Out of sight until the player has earned a shot at him (OpponentLifeService brings him to the diner)
            king.Status = RacerStatus.Inactive;
            king.Money = record.Money;

            // A name on the street: years of wins, pink slips taken, half of them on the strip
            var races = Math.Max(0, record.Races);
            var wins = Math.Clamp(record.Wins, 0, races);
            king.Stats.Races = races;
            king.Stats.Wins = wins;
            king.Stats.Losses = races - wins;
            king.Stats.DragRaces = races / 2;
            king.Stats.RoadRaces = races - races / 2;
            king.Stats.DragWins = wins / 2;
            king.Stats.RoadWins = wins - wins / 2;
            king.Stats.DragLosses = king.Stats.DragRaces - king.Stats.DragWins;
            king.Stats.RoadLosses = king.Stats.RoadRaces - king.Stats.RoadWins;
            king.Stats.PinkSlipsWon = Math.Max(0, record.PinkSlipsWon);
            king.Stats.Reputation = king.Stats.CalculateReputation();

            var car = CreateBossCar(carPool, gameState.Date, king, record.CarChoices);
            if (car != null) king.Cars.Add(car);

            gameState.Racers.AddRacer(king);
            _logger.Information("{Boss} is in town, in a {Car} ({Power:0} hp)", king.Name, car?.DefinitionId ?? "(no car)", car?.PowerHp ?? 0);
        }

        /// <summary>
        /// One of the strongest cars there is from the factory (the dearest, without parts to tell), in top shape, its
        /// engine worked on as much as it gets
        /// </summary>
        private Car? CreateBossCar(List<(CarDefinition Car, CarProfile Profile)> pool, DateTime today, Opponent king, int carChoices)
        {
            var choices = (_parts is { IsAvailable: true } catalogParts
                    ? pool.Select(p => (p.Car, p.Profile, Power: catalogParts.GetStockBuild(p.Car)?.PowerHp ?? 0)).Where(p => p.Power > 0)
                    : pool.Select(p => (p.Car, p.Profile, Power: (double)p.Profile.BasePrice)))
                .OrderByDescending(p => p.Power)
                .Take(Math.Max(1, carChoices))
                .Select(p => (p.Car, p.Profile))
                .ToList();
            if (choices.Count == 0) return null;

            var (definition, profile) = choices[_random.Next(choices.Count)];
            var car = new Car(definition.Id)
            {
                SkinId = definition.AvailableSkins is { Count: > 0 } skins ? skins[_random.Next(skins.Count)] : "default",
                OdometerKM = 20000 + _random.Next(0, 20000),
                EngineHealth = 0.95,
                TransmissionHealth = 0.95,
                BodyCondition = 1.0,
                TireCondition = 0.95,
                PurchaseDate = today.AddDays(-_random.Next(60, 365))
            };
            // Kept like a show car
            CarLooks.Give(car, CarLooks.Roll(Rules.RuleBook.Current.Rivals.Boss.CarCondition, LookSource.Dealer, _random) with { Dirt = 0 });

            if (_parts is { IsAvailable: true } parts && parts.GetStockBuild(definition) is { } build)
            {
                try
                {
                    AddEngine(car, EngineFactory.CreateTuned(parts.Catalog, parts.Builds, build, 0.95, 1.0, _random));
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not build {Boss}'s engine: {Error}", king.Name, ex.Message);
                }
            }

            // The car everybody on the street has lost to: his since he had it, and it shows in its worth (before
            // FinishCar prices it)
            car.History.ChangeHands(king.Name, car.PurchaseDate, CarAcquisition.Unknown);
            car.History.Races = Math.Max(0, king.Stats.Races - _random.Next(0, 10));
            car.History.Wins = Math.Clamp(car.History.Races - _random.Next(1, 4), 0, Math.Min(car.History.Races, king.Stats.Wins));
            car.History.PinkSlipsWon = Math.Min(king.Stats.PinkSlipsWon, car.History.Wins);

            FinishCar(car, definition, profile);
            return car;
        }

        /// <summary>
        /// Generate starting money for an opponent based on their traits
        /// Higher skill, age, and reputation = more money
        /// </summary>
        private decimal GenerateOpponentMoney(Opponent opponent)
        {
            var rules = Rules.RuleBook.Current.Rivals.Money;

            // Base money, then more for skill (skills run 90-100, counted from SkillBaseline)
            var baseMoney = Between(rules.BaseLow, rules.BaseHigh);
            var skillBonus = (opponent.Skill - SkillBaseline) * rules.PerSkill;

            // Older opponents have had more time to earn: the first age band the racer is under, else the last
            var bands = rules.ByAge ?? [];
            var band = bands.FirstOrDefault(b => opponent.Age < b.Under) ?? bands.LastOrDefault();
            var ageBonus = band == null ? 0m : Between(band.Low, band.High);

            // A name brings money: so much a point over the reputation it counts from
            var reputationBonus = Math.Max(0, (opponent.Stats.Reputation - rules.ReputationFrom) * rules.PerReputation);

            var totalMoney = baseMoney + skillBonus + ageBonus + reputationBonus;

            // Some random variation either way
            var spread = (decimal)Math.Clamp(rules.Spread, 0, 1);
            totalMoney *= 1.0m + ((decimal)_random.NextDouble() * 2 * spread - spread);

            // Rounded, and never less than the least
            var round = rules.RoundTo > 0 ? rules.RoundTo : 1m;
            totalMoney = Math.Round(totalMoney / round) * round;
            return Math.Max(rules.Least, totalMoney);

            // Whole amounts, every one as likely, as the draws have always been made
            decimal Between(decimal low, decimal high) =>
                high <= low ? low : low + _random.Next(0, (int)Math.Min(int.MaxValue - 1, Math.Floor(high - low)) + 1);
        }

        /// <summary>The installed, active catalog cars that have a profile to price them, with that profile</summary>
        private List<(CarDefinition Car, CarProfile Profile)> BuildCarPool()
        {
            var installed = InstalledCars.Only(_catalogRepo.GetCarsByStatus(ContentStatus.Active), out var notInstalled);
            if (notInstalled > 0)
            {
                _logger.Warning("{Count} catalog cars are not installed; no opponent gets one of them", notInstalled);
            }

            var profiles = _profileRepo.GetAllProfiles().ToDictionary(p => p.CarDefinitionId, StringComparer.OrdinalIgnoreCase);
            return [.. installed.Where(c => profiles.ContainsKey(c.Id)).Select(c => (c, profiles[c.Id]))];
        }

        /// <summary>
        /// Generate a used car for an opponent
        /// Creates a car instance with appropriate condition and mileage based on opponent traits
        /// </summary>
        private Car? GenerateOpponentCar(Opponent opponent, List<(CarDefinition Car, CarProfile Profile)> pool, DateTime today)
        {
            if (pool.Count == 0)
            {
                _logger.Warning("No installed car with a profile to assign to opponent {Name}", opponent.Name);
                return null;
            }

            // A random car, of the kind the racer drives when the install has one
            var liked = opponent.Drives is { } kind ? pool.Where(c => kind.MatchesOffer(c.Car, null, c.Profile.BasePrice, null)).ToList() : [];
            if (liked.Count > 0) pool = liked;
            var (carDefinition, profile) = pool[_random.Next(pool.Count)];

            // Select random skin
            var skinId = "default";
            if (carDefinition.AvailableSkins != null && carDefinition.AvailableSkins.Count > 0)
            {
                skinId = carDefinition.AvailableSkins[_random.Next(carDefinition.AvailableSkins.Count)];
            }

            // Generate condition based on opponent's skill and age
            // Higher skill opponents tend to have better maintained cars
            // Younger opponents might have more worn cars (less money for maintenance)
            var baseCondition = 0.5f + (opponent.Skill - SkillBaseline) / 100f; // 0.6 to 0.7 (skills run 90-100)
            var ageAdjustment = opponent.Age > 35 ? 0.1f : -0.05f; // Older opponents have better cars
            var condition = Math.Clamp(baseCondition + ageAdjustment + ((float)_random.NextDouble() * 0.2f - 0.1f), 0.4f, 0.9f);

            // Generate mileage (higher skill opponents might have lower mileage)
            var baseMileage = 50000 + _random.Next(0, 100000);
            var skillAdjustment = (100 - opponent.Skill) * 500; // Lower skill = more mileage
            var mileage = baseMileage + skillAdjustment;

            // Create the car instance
            var car = new Car(carDefinition.Id)
            {
                SkinId = skinId,
                OdometerKM = mileage,
                // The gearbox and the body are the parts' and the damage's once FinishCar has worked the figures out
                EngineHealth = condition,
                TransmissionHealth = condition,
                BodyCondition = 1.0,
                TireCondition = 0.6f + (float)_random.NextDouble() * 0.3f, // Tires vary more
                PurchaseDate = today.AddDays(-_random.Next(30, 365)) // Owned for 1 month to 1 year, in game time
            };

            // A street racer's car, as its owner keeps it: rough cars rough to look at too
            CarLooks.Give(car, CarLooks.Roll(condition, LookSource.Private, _random));

            // A car that has done that many miles has been through a few hands before theirs
            car.History.EarlierOwners = 1 + _random.Next(0, 1 + mileage / 50_000);
            car.History.ChangeHands(opponent.Name, car.PurchaseDate, CarAcquisition.Unknown);

            // A used engine, now and then worked on, like the ones on the lots
            if (_parts is { IsAvailable: true } parts)
            {
                try
                {
                    AddEngine(car, parts.CreateUsedEngine(carDefinition, condition));
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not build the engine of {Name}'s {Car}: {Error}", opponent.Name, carDefinition.Id, ex.Message);
                }
            }

            FinishCar(car, carDefinition, profile);

            _logger.Debug("Generated car {CarId} for opponent {Name}: Condition={Condition:F2}, Mileage={Mileage}",
                carDefinition.Id, opponent.Name, condition, mileage);

            return car;
        }

        private static void AddEngine(Car car, BuiltEngine? engine)
        {
            if (engine == null) return;

            car.Parts.Add(engine.Root);
            car.HasPartsAssigned = true;
            car.PowerHp = UsedCarMarketService.PowerOf(engine.Report);
        }

        /// <summary>The running gear, the car's figures from its parts, and what it is worth</summary>
        private void FinishCar(Car car, CarDefinition definition, CarProfile profile)
        {
            if (_parts is { IsAvailable: true } parts)
            {
                try
                {
                    parts.EnsureParts(car);
                    CarCondition.RefreshFigures(car, CarCondition.Groups(parts.Catalog));
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not give the {Car} its running gear: {Error}", car.DefinitionId, ex.Message);
                }
            }

            // Paid what a car like that is worth: the same sum the market prices its cars with
            car.PurchasePrice = CarValuation.ValueOf(car, profile, _parts is { IsAvailable: true } withParts ? withParts.Catalog : null);
        }
    }
}
