using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// Service for handling opponent challenge logic (acceptance/decline decisions)
    /// </summary>
    public class OpponentChallengeService : IOpponentChallengeService
    {
        private readonly Random _random;
        private readonly IAppLogger _logger;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly ICarProfileRepository _carProfileRepository;
        private readonly ICarPartsService? _partsService;

        /// <param name="partsService">Weighs what has been done to the two engines in a pink-slip challenge; without
        /// it the cars are valued as if they had their factory engines</param>
        public OpponentChallengeService(
            IContentCatalogRepository catalogRepository,
            ICarProfileRepository carProfileRepository,
            ICarPartsService? partsService = null)
        {
            _random = new Random();
            _logger = AppLoggerFactory.CreateLogger("OpponentChallenge");
            _catalogRepository = catalogRepository;
            _carProfileRepository = carProfileRepository;
            _partsService = partsService;
        }

        /// <summary>
        /// For testing races without being turned down: every challenge is accepted. Off unless a debug build
        /// or a test turns it on; it never ships on.
        /// </summary>
        public bool AlwaysAccept { get; set; }

        public bool CanRace(Car car)
        {
            try
            {
                var groupOf = _partsService is { IsAvailable: true } parts ? CarCondition.Groups(parts.Catalog) : null;
                return CarCondition.WhyCannotRace(car, groupOf).Count == 0;
            }
            catch (Exception ex)
            {
                // Not knowing is no reason to keep a racer from the tables
                _logger.Warning("Could not tell whether the {Car} can race: {Error}", car.DefinitionId, ex.Message);
                return true;
            }
        }

        /// <summary>
        /// Evaluate whether an opponent accepts a challenge
        /// </summary>
        public ChallengeResponse EvaluateChallenge(
            Opponent opponent,
            Player player,
            Car playerCar,
            Car opponentCar,
            bool isPinkSlip,
            decimal cashWager = 0,
            double pinkSlipFactor = 1.0)
        {
            _logger.Information("Evaluating challenge: {OpponentName} vs {PlayerName}, PinkSlip: {IsPinkSlip}, Wager: {Wager}",
                opponent.Name, player.Name, isPinkSlip, cashWager);

            if (AlwaysAccept)
            {
                _logger.Warning("Challenge acceptance bypassed for testing - opponent automatically accepts");
                return new ChallengeResponse
                {
                    Accepted = true,
                    Message = Reply(RacerWords.Key("replies.always_accept", opponent), new TextValues { ["name"] = opponent.Name }),
                    DeclineReason = null
                };
            }

            if (!CanRace(opponentCar))
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = Reply(RacerWords.Key("replies.car_laid_up", opponent)),
                    DeclineReason = ChallengeDeclineReason.CarNotAvailable
                };
            }

            // A boss races for one thing, and he takes on anybody who has made it to his table
            if (opponent.IsBoss)
            {
                return isPinkSlip
                    ? new ChallengeResponse { Accepted = true, Message = Reply(RacerWords.Key("replies.king.pink_slips", opponent)) }
                    : new ChallengeResponse
                    {
                        Accepted = false,
                        Message = Reply(RacerWords.Key("replies.king.no_pink_slip", opponent)),
                        DeclineReason = ChallengeDeclineReason.NotInterested
                    };
            }

            // Get car definitions for value comparison
            var playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
            var opponentCarDef = _catalogRepository.GetCar(opponentCar.DefinitionId);

            if (playerCarDef == null || opponentCarDef == null)
            {
                _logger.Warning("Could not find car definitions for challenge evaluation");
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = Reply(RacerWords.Key("replies.car_unknown", opponent)),
                    DeclineReason = ChallengeDeclineReason.CarNotAvailable
                };
            }

            // Evaluate based on bet type
            if (isPinkSlip)
            {
                return EvaluatePinkSlipChallenge(opponent, player, playerCar, playerCarDef, opponentCar, opponentCarDef, pinkSlipFactor);
            }
            else
            {
                return EvaluateCashChallenge(opponent, player, cashWager);
            }
        }

        public bool WouldStakePinkSlips(Opponent opponent, Car playerCar, Car opponentCar)
        {
            // A boss takes on anybody who has made it to his table
            if (opponent.IsBoss) return true;

            var playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
            var opponentCarDef = _catalogRepository.GetCar(opponentCar.DefinitionId);
            return playerCarDef != null && opponentCarDef != null
                   && ValueMismatch(opponent, playerCar, playerCarDef, opponentCar, opponentCarDef) == null;
        }

        /// <summary>
        /// The rival's no to a pink-slip race over what the cars are worth, or null when the values are no reason
        /// to turn it down. A rival out for a rematch looks past the difference, not past a car worth nothing.
        /// </summary>
        private ChallengeResponse? ValueMismatch(Opponent opponent, Car playerCar, CarDefinition playerCarDef,
            Car opponentCar, CarDefinition opponentCarDef)
        {
            // What each car is worth as it stands: model, condition, engine. The same sum the market uses.
            var playerCarValue = ValueOf(playerCar);
            var opponentCarValue = ValueOf(opponentCar);

            // A car worth nothing (no price for its model, a wreck) is nothing to stake either way. Without a
            // value to compare against, the ratio would divide by zero.
            if (opponentCarValue <= 0 || playerCarValue <= 0)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetCarValueMismatchMessage(opponent, isPlayerCarCheaper: playerCarValue <= 0),
                    DeclineReason = ChallengeDeclineReason.CarValueMismatch
                };
            }

            if (Grudges.WantsRematch(opponent)) return null;

            // Won't risk an expensive car for a cheap one
            var valueRatio = (double)(playerCarValue / opponentCarValue);
            if (valueRatio < 0.6)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetCarValueMismatchMessage(opponent, isPlayerCarCheaper: true),
                    DeclineReason = ChallengeDeclineReason.CarValueMismatch
                };
            }

            return null;
        }

        /// <summary>
        /// Evaluate a pink slip challenge
        /// </summary>
        private ChallengeResponse EvaluatePinkSlipChallenge(
            Opponent opponent,
            Player player,
            Car playerCar,
            CarDefinition playerCarDef,
            Car opponentCar,
            CarDefinition opponentCarDef,
            double pinkSlipFactor)
        {
            if (ValueMismatch(opponent, playerCar, playerCarDef, opponentCar, opponentCarDef) is { } mismatch) return mismatch;

            // A rival who lost a pink slip to the player takes any pink-slip race with them, whatever the cars are
            // worth: that is what they've been waiting for (Grudges). A car worth nothing is still nothing to stake.
            if (Grudges.WantsRematch(opponent))
            {
                _logger.Information("{Name} wants the rematch: pink slips accepted", opponent.Name);
                return new ChallengeResponse { Accepted = true, Message = Reply(RacerWords.Key("replies.rematch", opponent)) };
            }

            // Calculate acceptance chance based on multiple factors
            var acceptanceChance = 0.5; // Base 50%

            // Reputation factor
            var reputationDiff = opponent.Stats.Reputation - player.Stats.Reputation;
            if (reputationDiff > 20)
            {
                // Opponent is much better - more confident, more likely to accept
                acceptanceChance += 0.2;
            }
            else if (reputationDiff < -20)
            {
                // Player is much better - opponent less confident
                acceptanceChance -= 0.2;
            }

            // Aggression factor (high aggression = more willing to risk)
            acceptanceChance += (opponent.Aggression - 50) * 0.004; // ±0.2 max

            // Age factor (younger = more reckless)
            if (opponent.Age < 26)
            {
                acceptanceChance += 0.15;
            }
            else if (opponent.Age > 45)
            {
                acceptanceChance -= 0.1;
            }

            // Skill confidence factor: ±0.1 from the least to the most skilled
            acceptanceChance += (OpponentRules.SkillShare(opponent.Skill) - 0.5) * 0.2;

            // Pink slip history factor
            if (opponent.Stats.PinkSlipsLost > opponent.Stats.PinkSlipsWon)
            {
                // Bad pink slip history makes them more cautious
                acceptanceChance -= 0.15;
            }

            // Car value ratio factor (more willing if player's car is worth more); both are worth something by now
            var valueRatio = (double)(ValueOf(playerCar) / ValueOf(opponentCar));
            if (valueRatio > 1.2)
            {
                acceptanceChance += 0.15;
            }

            // Where pink slips are rare (the game's difficulty), rivals are warier of staking their car
            acceptanceChance += (pinkSlipFactor - 1) * 0.2;

            // Clamp to 5-95%
            acceptanceChance = Math.Clamp(acceptanceChance, 0.05, 0.95);

            _logger.Information("Pink slip acceptance chance for {Name}: {Chance:P0}", opponent.Name, acceptanceChance);

            // Make decision
            if (_random.NextDouble() < acceptanceChance)
            {
                return new ChallengeResponse
                {
                    Accepted = true,
                    Message = GetAcceptanceMessage(opponent)
                };
            }
            else
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetPinkSlipDeclineMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.TooRisky
                };
            }
        }

        /// <summary>
        /// Evaluate a cash wager challenge
        /// </summary>
        private ChallengeResponse EvaluateCashChallenge(
            Opponent opponent,
            Player player,
            decimal cashWager)
        {
            // Check if opponent has any money at all (safety check)
            if (opponent.Money <= 0)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetInsufficientFundsMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.InsufficientFunds
                };
            }

            // Check if opponent has enough money
            if (cashWager > opponent.Money)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetInsufficientFundsMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.InsufficientFunds
                };
            }

            // Check if bet is too high relative to their bankroll
            var wagerRatio = (double)(cashWager / opponent.Money);
            if (wagerRatio > 0.5 && opponent.Aggression < 60)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetBetTooHighMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.BetTooHigh
                };
            }

            // Check reputation requirements
            var reputationDiff = opponent.Stats.Reputation - player.Stats.Reputation;

            // Very low player reputation - opponent might refuse
            if (player.Stats.Reputation < 30 && reputationDiff > 25)
            {
                if (_random.NextDouble() < 0.4) // 40% chance to refuse rookies
                {
                    return new ChallengeResponse
                    {
                        Accepted = false,
                        Message = GetReputationTooLowMessage(opponent),
                        DeclineReason = ChallengeDeclineReason.ReputationTooLow
                    };
                }
            }

            // Calculate acceptance chance
            var acceptanceChance = 0.7; // Base 70% for cash races (less risky than pink slips)

            // Aggression factor
            acceptanceChance += (opponent.Aggression - 50) * 0.003; // ±0.15 max

            // Wager ratio factor (higher wagers = more caution unless aggressive)
            if (wagerRatio > 0.3)
            {
                acceptanceChance -= 0.2;
                // But aggressive opponents don't mind high stakes
                if (opponent.Aggression > 70)
                {
                    acceptanceChance += 0.15;
                }
            }

            // Age factor
            if (opponent.Age < 26)
            {
                acceptanceChance += 0.1;
            }

            // Recent form factor
            if (opponent.Stats.Races >= 3)
            {
                var recentWins = opponent.Stats.Wins;
                var recentRaces = opponent.Stats.Races;
                if (recentRaces > 0)
                {
                    var recentWinRate = (double)recentWins / recentRaces;
                    if (recentWinRate > 0.7)
                    {
                        // On a winning streak, more confident
                        acceptanceChance += 0.1;
                    }
                    else if (recentWinRate < 0.3)
                    {
                        // On a losing streak, less confident
                        acceptanceChance -= 0.15;
                    }
                }
            }

            // Clamp to 10-95%
            acceptanceChance = Math.Clamp(acceptanceChance, 0.1, 0.95);

            _logger.Information("Cash challenge acceptance chance for {Name}: {Chance:P0}", opponent.Name, acceptanceChance);

            // Make decision
            if (_random.NextDouble() < acceptanceChance)
            {
                return new ChallengeResponse
                {
                    Accepted = true,
                    Message = GetAcceptanceMessage(opponent)
                };
            }
            else
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetGenericDeclineMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.NotInterested
                };
            }
        }

        /// <summary>The car's worth by <see cref="CarValuation"/>; a model without a price is worth what was paid for the car</summary>
        private decimal ValueOf(Car car) => CarValuation.WorthOf(car, _carProfileRepository.GetProfile, _partsService);

        #region Message Generation

        // The words are the mods' ("replies.*" in their text); which answer is the game's
        private string Reply(string pool, TextValues? values = null) => GameText.Installed.Pick(pool, _random, values);

        private string GetAcceptanceMessage(Opponent opponent) => Reply(RacerWords.Key(opponent.Aggression switch
        {
            >= 70 => "replies.accept.aggressive",
            <= 40 => "replies.accept.calm",
            _ => "replies.accept"
        }, opponent));

        private string GetPinkSlipDeclineMessage(Opponent opponent) => Reply(RacerWords.Key("replies.no_pink_slips", opponent));

        private string GetCarValueMismatchMessage(Opponent opponent, bool isPlayerCarCheaper) =>
            Reply(RacerWords.Key(isPlayerCarCheaper ? "replies.your_car_is_worth_less" : "replies.your_car_is_worth_more", opponent));

        private string GetInsufficientFundsMessage(Opponent opponent) => Reply(RacerWords.Key("replies.no_money", opponent));

        private string GetBetTooHighMessage(Opponent opponent) => Reply(RacerWords.Key("replies.bet_too_high", opponent));

        private string GetReputationTooLowMessage(Opponent opponent) => Reply(RacerWords.Key("replies.reputation_too_low", opponent));

        private string GetGenericDeclineMessage(Opponent opponent) => Reply(RacerWords.Key("replies.decline", opponent));

        #endregion
    }
}
