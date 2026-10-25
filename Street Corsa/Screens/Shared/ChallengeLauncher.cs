using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.AC;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Police;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>A race the player puts to a rival, wherever they met: across a diner table or at the curb</summary>
    public sealed class ChallengeRequest
    {
        public required Opponent Opponent { get; init; }
        public required Car OpponentCar { get; init; }
        public required CarDefinition OpponentDefinition { get; init; }
        public required Car PlayerCar { get; init; }
        public required TrackInfo Track { get; init; }
        public TrackConfiguration? Configuration { get; init; }
        /// <summary>The race's format id (<see cref="Services.Race.RaceFormats"/>)</summary>
        public string Format { get; init; } = string.Empty;
        public bool IsPinkSlip { get; init; }
        public decimal CashWager { get; init; }

        /// <summary>Run as a bracket race: the player dials in first, and may back out there</summary>
        public bool Bracket { get; init; }

        /// <summary>The rival put these terms up themselves: they are not asked again</summary>
        public bool AlreadyAgreed { get; init; }

        /// <summary>
        /// The diner the player is at, or came out of onto the street: the race comes back there. Across its tables
        /// (<see cref="AtDiner"/>) its regulars will not race a car below its class (<see cref="Services.Diner.DinerBook.Snubs(Models.Diners.Diner, Models.GameState.Opponent, double?)"/>).
        /// </summary>
        public Models.Diners.Diner? Diner { get; init; }

        /// <summary>The race is put across the diner's tables, where the regulars keep to their class; false at the curb</summary>
        public bool AtDiner { get; init; }
    }

    public enum ChallengeStatus
    {
        /// <summary>The race is on: the loading screen has it</summary>
        Launched,

        /// <summary>The rival turned it down; the message says why, in their words</summary>
        Declined,

        /// <summary>The player backed out at the dial-in</summary>
        BackedOut,

        /// <summary>The player's car cannot go; they have been told</summary>
        CarWontRun,

        /// <summary>The player left the screen while the cars were being set up</summary>
        LeftScreen
    }

    public sealed record ChallengeOutcome(ChallengeStatus Status, string? Message = null);

    /// <summary>
    /// Takes a challenge from the rival's answer to the start of the race, the same way wherever it was made: the
    /// answer, the bracket dial-in, whether the police show up, both cars set up on their parts, and off to the
    /// loading screen. What the screen then shows of it is the screen's.
    /// </summary>
    public sealed class ChallengeLauncher
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IOpponentChallengeService _challengeService;
        private readonly RaceSetupBuilder _raceSetup;
        private readonly IAppLogger _logger;

        public ChallengeLauncher(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            IContentCatalogRepository catalogRepository,
            IOpponentChallengeService challengeService,
            RaceSetupBuilder raceSetup,
            IAppLogger logger)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _challengeService = challengeService;
            _raceSetup = raceSetup;
            _logger = logger;
        }

        /// <summary>The reputation the police go by: the better known of the two racers</summary>
        public static int PoliceReputation(Player player, Opponent opponent) =>
            Math.Max(player.Stats.Reputation, opponent.Stats.Reputation);

        /// <param name="screen">The screen the challenge was made on: a player who has left it by the time the cars are ready has called it off</param>
        public async Task<ChallengeOutcome> RunAsync(ChallengeRequest request, object screen)
        {
            var opponent = request.Opponent;
            var playerCar = request.PlayerCar;
            var opponentCar = request.OpponentCar;

            _logger.Information("Challenge setup: Track={TrackId}, Config={Config}, IsPinkSlip={PinkSlip}, Wager={Wager}",
                request.Track.TrackId, request.Configuration?.FolderName ?? "(none)", request.IsPinkSlip, request.CashWager);

            // The regulars of a diner above the player's car do not race them, whatever the terms; a boss and a rival out
            // for a rematch do
            if (request.AtDiner && request.Diner is { } diner
                && Services.Diner.DinerBook.Snubs(diner, opponent, CarPower.Of(playerCar, _catalogRepository.GetCar(playerCar.DefinitionId))))
            {
                _logger.Information("{Name} won't race the player's car at {Diner}: below its class", opponent.Name, diner.Id);
                return new ChallengeOutcome(ChallengeStatus.Declined, SnubReply(opponent));
            }

            // Whether the opponent takes it on, before any work goes into the cars
            if (!request.AlreadyAgreed)
            {
                var response = _challengeService.EvaluateChallenge(
                    opponent,
                    _gameState.Player,
                    playerCar,
                    opponentCar,
                    request.IsPinkSlip,
                    request.CashWager,
                    _gameState.Rules.PinkSlipFactor);

                if (!response.Accepted)
                {
                    _logger.Information("Opponent declined: {Message}", response.Message);
                    return new ChallengeOutcome(ChallengeStatus.Declined, response.Message);
                }
            }

            _logger.Information("Opponent accepted challenge - launching race");

            var ai = OpponentAIAdapter.ToAssettoCorsaAI(opponent, _gameState.Rules);

            // A bracket race: the rival dials in from its car, the player picks theirs
            BracketSetup? bracket = null;
            if (request.Bracket)
            {
                var rivalDialIn = Services.Race.BracketRules.RivalDialInFor(opponentCar, request.OpponentDefinition);
                var playerDialIn = await Dialogs.DialIn.DialInDialogViewModel.AskAsync(_dialogService, playerCar,
                    _catalogRepository.GetCar(playerCar.DefinitionId), CarNames.Of(_catalogRepository, playerCar.DefinitionId),
                    opponent.Name, rivalDialIn);
                if (playerDialIn == null)
                {
                    _logger.Information("The player backed out of the bracket race at the dial-in");
                    return new ChallengeOutcome(ChallengeStatus.BackedOut);
                }

                bracket = Services.Race.BracketRules.Setup(playerDialIn.Value, rivalDialIn, ai.AILevel);
                _logger.Information("Bracket race: dial-ins {Player} and {Rival}", bracket.PlayerDialIn, bracket.OpponentDialIn);
            }

            // Whether the police turn up, rolled once the race is on
            var police = PoliceCars.Patrol(PoliceCars.Installed(), Services.Race.RaceFormats.Current.Of(request.Format).Police, _gameState.Date,
                PoliceReputation(_gameState.Player, opponent), request.IsPinkSlip, request.CashWager,
                request.Configuration?.Pitboxes ?? request.Track.Pitboxes,
                [playerCar.DefinitionId, request.OpponentDefinition.Id], Random.Shared);
            if (police != null)
                _logger.Information("The police will show up: {Count} car(s), {Share:P0} into the race", police.Count, police.SpotShare);

            // Both cars race on what their parts make of them, the same way an event does; a player's car that
            // will not go stays home
            var setup = await _raceSetup.BuildAsync(new RaceEntry
            {
                PlayerName = _gameState.Player.Name,
                PlayerCar = playerCar,
                OpponentName = opponent.Name,
                OpponentCarId = request.OpponentDefinition.Id,
                OpponentSkin = opponentCar.SkinId,
                OpponentCar = opponentCar,
                OpponentAI = ai,
                TrackId = request.Track.TrackId,
                TrackConfig = request.Configuration?.FolderName,
                Format = request.Format,
                CashWager = request.CashWager,
                IsPinkSlip = request.IsPinkSlip,
                DamagePercent = _gameState.Rules.RaceDamagePercent,
                Police = police,
                RaceTime = _gameState.Date,
                Bracket = bracket
            });

            // Setting the cars up takes a moment: a player who walked out meanwhile has called it off
            if (!ReferenceEquals(_navigationService.CurrentScreen, screen))
            {
                _logger.Information("The player left while the challenge was set up; no race");
                return new ChallengeOutcome(ChallengeStatus.LeftScreen);
            }

            if (setup.Intent == null)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    GameText.Say("challenge.car_wont_run", ("problem", setup.PlayerCarProblem)),
                    GameText.Say("challenge.car_wont_run.title")));
                return new ChallengeOutcome(ChallengeStatus.CarWontRun);
            }

            // The loading screen launches the race, spends its time and comes back here when AC is closed
            _logger.Information("Navigating to race loading screen");
            setup.Intent.ReturnDiner = request.Diner?.Id;
            _navigationService.GoRace(_gameState, setup.Intent);
            return new ChallengeOutcome(ChallengeStatus.Launched);
        }

        /// <summary>What a regular says to a player whose car is below the diner's class, in the racer's own words when a mod gives them some</summary>
        public static string SnubReply(Opponent opponent) =>
            GameText.Installed.Pick(RacerWords.Key("replies.snub", opponent), Random.Shared);
    }
}
