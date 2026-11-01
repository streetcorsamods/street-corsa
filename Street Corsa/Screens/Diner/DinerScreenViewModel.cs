using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.AC;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Police;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;
using System.Collections.ObjectModel;
using System.IO;
using StreetCorsa.Helpers;

namespace StreetCorsa.Screens.Diner
{
    /// <summary>One line of the street talk, with when it happened ("Today", "Yesterday", "Jun 3")</summary>
    public sealed record StreetTalkLine(string When, string Text);

    public partial class DinerScreenViewModel : DriveInScreenViewModel
    {
        /// <summary>The sound of the place: out on the street, in the car</summary>
        public override string? Ambience => "ambience.street";

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IOpponentChallengeService _challengeService;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly ITalkService _talkService;
        private readonly IGameTimeService _timeService;
        private readonly RaceSetupBuilder _raceSetup;
        private readonly Services.Parts.ICarPartsService _partsService;
        private readonly ChallengeLauncher _launcher;
        private readonly IAppLogger _logger;

        // Back from a race: the player never left, so the visit is not paid for again
        private readonly bool _returning;

        public RelayCommand GarageCommand { get; }

        /// <summary>Out to the street, to sit at the curb and wait for a rival to pull up</summary>
        public RelayCommand CruiseCommand { get; }

        /// <summary>Parked, and the way out to the street leads somewhere: a game may have no street</summary>
        public bool CanCruise => IsParked && _navigationService.CanLeave(this, "cruise");

        /// <summary>Off to another diner: the diners' map, once the car has driven off</summary>
        public RelayCommand MapCommand { get; }

        /// <summary>Parked, and the way out to the diners' map leads somewhere</summary>
        public bool CanMap => IsParked && _navigationService.CanLeave(this, "map");
        public RelayCommand<OpponentDisplayViewModel> SelectOpponentCommand { get; }
        public RelayCommand<TrackCardViewModel> SelectTrackCommand { get; }
        public AsyncRelayCommand ChallengeCommand { get; }

        /// <summary>The bet: cash, or the cars' pink slips</summary>
        public RelayCommand CashBetCommand { get; }
        public RelayCommand PinkSlipBetCommand { get; }

        private ObservableCollection<OpponentDisplayViewModel> _opponents;
        public ObservableCollection<OpponentDisplayViewModel> Opponents
        {
            get => _opponents;
            set
            {
                _opponents = value;
                OnPropertyChanged(nameof(Opponents));
            }
        }

        private OpponentDisplayViewModel? _selectedOpponent;
        public OpponentDisplayViewModel? SelectedOpponent
        {
            get => _selectedOpponent;
            set
            {
                _selectedOpponent = value;
                OnPropertyChanged(nameof(SelectedOpponent));
                OnPropertyChanged(nameof(HasSelectedOpponent));
                OnPropertyChanged(nameof(CanTalk));
                OnPropertyChanged(nameof(SelectedOpponentDisplay));
                OnPropertyChanged(nameof(BracketNote));

                // Update matchup stats when opponent changes
                UpdateMatchupStats();
            }
        }

        public bool HasSelectedOpponent => SelectedOpponent != null;

        /// <summary>The racer picked can be talked to while the player's AI is on</summary>
        public bool CanTalk => SelectedOpponent != null && Services.Talk.TalkDesk.Shared.IsAvailable;

        /// <summary>A word with the racer picked, at their table</summary>
        public RelayCommand TalkCommand { get; }

        public string SelectedOpponentDisplay => SelectedOpponent != null
            ? $"{SelectedOpponent.Name} \"{SelectedOpponent.Nickname}\""
            : GameText.Say("diner.select_an_opponent");

        private string _opponentMessage = "";
        public string OpponentMessage
        {
            get => _opponentMessage;
            set
            {
                _opponentMessage = value;
                OnPropertyChanged(nameof(OpponentMessage));
                OnPropertyChanged(nameof(HasOpponentMessage));
            }
        }

        public bool HasOpponentMessage => !string.IsNullOrEmpty(OpponentMessage);

        // Track collections
        public ObservableCollection<TrackCardViewModel> DragTracks { get; } = new();
        public ObservableCollection<TrackCardViewModel> RoadTracks { get; } = new();

        public bool HasDragTracks => DragTracks.Count > 0;
        public bool HasRoadTracks => RoadTracks.Count > 0;
        public bool HasAnyTracks => HasDragTracks || HasRoadTracks;
        public bool HasOpponents => Opponents.Count > 0;

        /// <summary>What the rivals have been up to, newest first (<see cref="Models.GameState.GameState.StreetTalk"/>)</summary>
        public ObservableCollection<StreetTalkLine> StreetTalk { get; } = new();

        public bool HasStreetTalk => StreetTalk.Count > 0;

        /// <summary>How many lines of talk the diner shows</summary>
        private const int StreetTalkShown = 15;

        private TrackCardViewModel? _selectedTrack;
        public TrackCardViewModel? SelectedTrack
        {
            get => _selectedTrack;
            set
            {
                if (_selectedTrack != value)
                {
                    // Deselect previous track
                    if (_selectedTrack != null)
                        _selectedTrack.IsSelected = false;

                    _selectedTrack = value;

                    // Select new track
                    if (_selectedTrack != null)
                        _selectedTrack.IsSelected = true;

                    OnPropertyChanged(nameof(SelectedTrack));
                    OnPropertyChanged(nameof(HasSelectedTrack));
                    OfferFormats();

                    // Update matchup stats when track changes
                    UpdateMatchupStats();

                    // Update wager limits when track changes (different limits for drag vs road)
                    UpdateWagerLimits();

                    // Update talk message when track changes
                    if (SelectedOpponent != null && _selectedTrack != null && !SelectedOpponent.IsSnubbing)
                    {
                        _ = LoadOpponentMessageAsync(SelectedOpponent, TalkTrigger.TrackSelected);
                    }
                }
            }
        }

        public bool HasSelectedTrack => SelectedTrack != null;

        /// <summary>The races the diner offers on the selected track (<see cref="RaceFormat.Diner"/> of its family)</summary>
        public ObservableCollection<FormatChoice> FormatChoices { get; } = [];

        /// <summary>A choice to make: the track's family has more than one race the diner offers</summary>
        public bool HasFormatChoice => FormatChoices.Count > 1;

        private FormatChoice? _selectedFormat;

        /// <summary>The race picked for the selected track</summary>
        public FormatChoice? SelectedFormat
        {
            get => _selectedFormat;
            set
            {
                if (ReferenceEquals(_selectedFormat, value)) return;
                _selectedFormat = value;
                OnPropertyChanged(nameof(SelectedFormat));
                OnPropertyChanged(nameof(CanBracket));
                OnPropertyChanged(nameof(BracketNote));
                OnPropertyChanged(nameof(PoliceNote));
                OnPropertyChanged(nameof(HasPoliceNote));
                OnPropertyChanged(nameof(FormatNote));
                OnPropertyChanged(nameof(HasFormatNote));
                UpdateWagerLimits();
            }
        }

        /// <summary>The format the race is run as: the one picked, else the family's street race</summary>
        private string? SelectedFormatId => SelectedFormat?.Id
            ?? (SelectedTrack is { } track ? Services.Race.RaceFormats.Current.Street(track.Family)?.Id : null);

        /// <summary>What a race picked at the diner is about, when it is not a plain race to the line (a time attack)</summary>
        public string FormatNote
        {
            get
            {
                var formats = Services.Race.RaceFormats.Current;
                return SelectedFormat is { } choice && formats.Get(choice.Id) is { } format
                    && formats.OwnKey("diner.format.about", format) is { } key ? GameText.Say(key) : string.Empty;
            }
        }

        public bool HasFormatNote => FormatNote.Length > 0;

        /// <summary>The selected track's family's races, the one picked kept when it is still among them</summary>
        private void OfferFormats()
        {
            var keep = SelectedFormat?.Id;
            FormatChoices.Clear();
            if (SelectedTrack is { } track)
            {
                // A race of more than one lap is not offered where the finish is never crossed twice
                foreach (var format in Services.Race.RaceFormats.Current.ForDiner(track.Family).Where(f => !track.IsPointToPoint || !f.NeedsLoop()))
                    FormatChoices.Add(new FormatChoice(format.Id, Services.Race.RaceFormats.Current.NameOf(format)));
            }

            _selectedFormat = null;
            SelectedFormat = FormatChoices.FirstOrDefault(c => c.Id == keep) ?? FormatChoices.FirstOrDefault();
            OnPropertyChanged(nameof(HasFormatChoice));
            OnPropertyChanged(nameof(CanBracket));
        }

        /// <summary>A bracket race can be run on the selected track: a drag strip that runs the format's strip, in a race that can be one</summary>
        public bool CanBracket => SelectedTrack is { } track && Services.Race.RaceFormats.Current.Get(SelectedFormatId) is { Bracket: true } format
            && Services.Race.BracketRules.RunsTheStrip(track.Track, track.Configuration, Services.Race.BracketRules.StripMetres(format));

        private bool _isBracket;

        /// <summary>
        /// Run it as a bracket race: dial-ins, a staggered start, and a breakout loses (<see cref="Services.Race.BracketRules"/>).
        /// Only on a track that can take one (<see cref="CanBracket"/>).
        /// </summary>
        public bool IsBracket
        {
            get => _isBracket;
            set
            {
                if (_isBracket == value) return;
                _isBracket = value;
                OnPropertyChanged(nameof(IsBracket));
                OnPropertyChanged(nameof(BracketNote));
            }
        }

        /// <summary>What the selected rival would dial in, for the bracket option</summary>
        public string BracketNote => SelectedOpponent?.Opponent.Cars.FirstOrDefault() is { } car
            ? GameText.Say("diner.bracket.rival_dial_in", ("name", SelectedOpponent.Name),
                ("dial_in", Services.Race.BracketRules.Show(Services.Race.BracketRules.RivalDialInFor(car, SelectedOpponent.CarDefinition,
                    Services.Race.BracketRules.StripMetres(SelectedFormatId)))))
            : string.Empty;

        // Matchup stats
        public ObservableCollection<MatchupStatViewModel> MatchupStats { get; } = new();

        public bool HasMatchupStats => MatchupStats.Count > 0;

        // Bet options
        private bool _isCashBet = true;
        public bool IsCashBet
        {
            get => _isCashBet;
            set
            {
                if (_isCashBet != value)
                {
                    _isCashBet = value;
                    _pinkSlipBySelection = false;
                    OnPropertyChanged(nameof(IsCashBet));
                    OnPropertyChanged(nameof(IsPinkSlipBet));
                    OnPropertyChanged(nameof(IsCashBetEnabled));
                    UpdateWagerLimits();

                    // Trigger talk message when bet type changes
                    if (SelectedOpponent != null && !SelectedOpponent.IsSnubbing)
                    {
                        _ = LoadOpponentMessageAsync(SelectedOpponent, TalkTrigger.BetTypeChanged);
                    }
                }
            }
        }

        // The pink slips were put on the table by picking a boss or a rival who wants a rematch, not by the player:
        // picking somebody else takes them off again
        private bool _pinkSlipBySelection;

        public bool IsPinkSlipBet
        {
            get => !_isCashBet;
            set => IsCashBet = !value;
        }

        public bool IsCashBetEnabled => IsCashBet;

        private decimal _wagerAmount = 50;
        public decimal WagerAmount
        {
            get => _wagerAmount;
            set
            {
                if (_wagerAmount != value)
                {
                    _wagerAmount = value;
                    OnPropertyChanged(nameof(WagerAmount));
                    OnPropertyChanged(nameof(WagerAmountDisplay));
                    OnPropertyChanged(nameof(PoliceNote));
                    OnPropertyChanged(nameof(HasPoliceNote));
                }
            }
        }

        public string WagerAmountDisplay => Money.Text(WagerAmount);

        /// <summary>Why the stakes are higher than usual: the hour, the rival's name; empty when they aren't</summary>
        public string StakesNote
        {
            get
            {
                var reasons = new List<string>();
                if (Diner is { Stakes: > 1m }) reasons.Add(GameText.Say("diner.stakes.place"));
                if (MatchupCalculator.IsNight(_gameState.Date)) reasons.Add(GameText.Say("diner.stakes.night"));
                if ((SelectedOpponent?.Opponent.Stats.Reputation ?? 0) > MatchupCalculator.StakesReputation) reasons.Add(GameText.Say("diner.stakes.big_name"));
                return reasons.Count == 0 ? string.Empty : GameText.Say("diner.stakes", ("reasons", string.Join(", ", reasons)));
            }
        }

        public bool HasStakesNote => IsCashBet && StakesNote.Length > 0;

        /// <summary>The reputation the police go by: the better known of the two racers</summary>
        private int PoliceReputation => SelectedOpponent is { } selected
            ? ChallengeLauncher.PoliceReputation(_gameState.Player, selected.Opponent)
            : _gameState.Player.Stats.Reputation;

        /// <summary>The chance of the police on a road race with the selected rival, now; 0 for a drag race or with no police car installed</summary>
        private double PoliceChance =>
            SelectedTrack != null && Services.Race.RaceFormats.Current.Get(SelectedFormatId)?.Police == true && PoliceCars.Installed() != null
                ? PoliceRules.Chance(_gameState.Date, PoliceReputation, IsPinkSlipBet, IsCashBet ? WagerAmount : 0m)
                : 0;

        /// <summary>The word on the police for the race as it is set up: "Police: Moderate"; empty when none could come</summary>
        public string PoliceNote => PoliceChance > 0
            ? GameText.Say(PoliceRules.IsNight(_gameState.Date) ? "diner.police.night" : "diner.police", ("risk", PoliceRules.RiskLabel(PoliceChance)))
            : string.Empty;

        public bool HasPoliceNote => PoliceNote.Length > 0;

        public bool CanAffordMinBet
        {
            get
            {
                if (!IsCashBet) return true; // Pink slip doesn't require cash
                return _gameState.Player.Money >= MatchupCalculator.MinimumWager(SelectedFormatId);
            }
        }

        public string InsufficientFundsMessage
        {
            get
            {
                var baseMin = MatchupCalculator.MinimumWager(SelectedFormatId);
                return GameText.Say("diner.wager.too_poor", ("amount", baseMin));
            }
        }

        private decimal _minWager = 10;
        public decimal MinWager
        {
            get => _minWager;
            private set
            {
                if (_minWager != value)
                {
                    _minWager = value;
                    OnPropertyChanged(nameof(MinWager));
                }
            }
        }

        private decimal _maxWager = 100;
        public decimal MaxWager
        {
            get => _maxWager;
            private set
            {
                if (_maxWager != value)
                {
                    _maxWager = value;
                    OnPropertyChanged(nameof(MaxWager));
                }
            }
        }

        public DinerScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IContentCatalogRepository catalogRepository,
            IOpponentChallengeService challengeService,
            IAssettoCorsaContentService contentService,
            ITalkService talkService,
            IGameTimeService timeService,
            IGameStateRepository gameStateRepo,
            RaceSetupBuilder raceSetup,
            Services.Parts.ICarPartsService partsService,
            bool returning = false,
            string? dinerId = null)
            : base(navigationService, dialogService, gameState, gameStateRepo, timeService, new Controls.Street.DriveInStage(), AppLoggerFactory.CreateLogger("Diner"))
        {
            _returning = returning;
            Diner = Services.Diner.DinerBook.Current.GetOrFirst(dinerId);
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _challengeService = challengeService;
            _contentService = contentService;
            _talkService = talkService;
            _timeService = timeService;
            _raceSetup = raceSetup;
            _partsService = partsService;
            _logger = AppLoggerFactory.CreateLogger("Diner");
            _launcher = new ChallengeLauncher(navigationService, dialogService, gameState, catalogRepository, challengeService,
                raceSetup, _logger);

            GarageCommand = new RelayCommand(OnGarage, () => IsParked);
            CruiseCommand = new RelayCommand(() => DriveOff("cruise"), () => CanCruise);
            MapCommand = new RelayCommand(() => DriveOff("map"), () => CanMap);
            MakeStreetCommands();
            TalkCommand = new RelayCommand(() =>
            {
                if (SelectedOpponent?.Opponent is { } rival && Services.Talk.TalkDesk.Shared.IsAvailable
                    && Services.Talk.TalkDesk.Shared.ForRival(_gameState, rival, Services.Talk.TalkPlace.Diner) is var talk
                    && Services.Talk.TalkDesk.Shared.Answers(talk))
                    Dialogs.Talk.TalkDialogViewModel.Show(_dialogService, talk);
            }, () => CanTalk);
            SelectOpponentCommand = new RelayCommand<OpponentDisplayViewModel>(OnSelectOpponent);
            SelectTrackCommand = new RelayCommand<TrackCardViewModel>(OnSelectTrack);
            // Async: preparing the cars' data takes a moment, and a second click meanwhile must not start a second race
            ChallengeCommand = new AsyncRelayCommand(OnChallenge, CanChallenge);
            CashBetCommand = new RelayCommand(() => IsCashBet = true);
            PinkSlipBetCommand = new RelayCommand(() => IsPinkSlipBet = true);

            _opponents = new ObservableCollection<OpponentDisplayViewModel>();

            // Tracks and opponents are loaded in Enter, once: the screen is always entered right after it is made
        }

        private void LoadOpponents()
        {
            // A new day can bring other racers to the tables: whoever was picked before stays picked only if
            // they are still here, so the player can never challenge someone who has left
            var previous = SelectedOpponent?.Opponent;
            Opponents.Clear();

            // Who is in: the regulars of this diner, those whose car's power puts them here
            var racersOut = RacersOut.Today(_gameState, _challengeService, _catalogRepository, _logger);
            if (Diner != null) racersOut = RacersOut.AtDiner(racersOut, Diner, Services.Diner.DinerBook.Current);
            var power = PlayerPower();
            IsSnubbed = Diner != null && Services.Diner.DinerBook.Snubs(Diner, power);
            if (racersOut.Count == 0)
                _logger.Information("No opponents available at diner");
            else
                _logger.Information("Loading {Count} opponents", racersOut.Count);

            foreach (var (opponent, _, carDef) in racersOut)
            {
                Opponents.Add(new OpponentDisplayViewModel
                {
                    Opponent = opponent,
                    CarDefinition = carDef,
                    PortraitPath = RacersOut.PortraitOf(opponent, _logger),
                    Difficulty = CalculateDifficulty(opponent),
                    IsSnubbing = IsSnubbed && Services.Diner.DinerBook.Snubs(Diner!, opponent, power)
                });
            }

            _logger.Information("Loaded {Count} opponents into diner view", Opponents.Count);

            // Notify UI about opponents availability
            OnPropertyChanged(nameof(HasOpponents));

            // Keep the one picked before if they are still here; else nobody, and the diner opens on everyone who is in
            var keep = Opponents.FirstOrDefault(o => ReferenceEquals(o.Opponent, previous));
            if (keep != null)
            {
                OnSelectOpponent(keep);
            }
            else
            {
                SelectedOpponent = null;
                OpponentMessage = "";
                UpdateWagerLimits();
            }
            RelayCommand.RaiseCanExecuteChanged();

            // The player's AI, if on, writes the other racers' greetings ahead, so a click shows one at once; regulars who
            // snub the player's car say so in the game's own words
            foreach (var opponentVm in Opponents.Where(o => !o.IsSnubbing && !ReferenceEquals(o, SelectedOpponent)))
                _talkService.Prepare(TalkContextFor(opponentVm, TalkTrigger.OpponentSelected));

            LoadStreetTalk();
            OnPropertyChanged(nameof(RacersIn));
            ParkRacersCars();
        }

        private void LoadStreetTalk()
        {
            StreetTalk.Clear();
            var today = _gameState.Date.Date;
            foreach (var item in (_gameState.StreetTalk ?? []).AsEnumerable().Reverse().Take(StreetTalkShown))
            {
                var days = (today - item.Date.Date).Days;
                var when = days <= 0 ? GameText.Say("diner.talk.today") : days == 1 ? GameText.Say("diner.talk.yesterday") : GameText.Say("diner.talk.date", ("date", item.Date));
                StreetTalk.Add(new StreetTalkLine(when, Services.Ai.AiWriter.Said(item)));
            }

            OnPropertyChanged(nameof(HasStreetTalk));
        }

        private OpponentDifficulty CalculateDifficulty(Opponent opponent) =>
            MatchupCalculator.DifficultyOf(opponent.Stats.Reputation, _gameState.Player.Stats.Reputation);

        private void OnSelectOpponent(OpponentDisplayViewModel? opponentVm)
        {
            if (opponentVm != null)
            {
                SelectedOpponent = opponentVm;
                _logger.Information("Selected opponent: {Name}", opponentVm.Name);

                // A boss races for pink slips and nothing else, and a rival who wants a rematch wants the pink slips.
                // Pink slips only they asked for go back off the table when the player turns to somebody else.
                if (opponentVm.Opponent.IsBoss || opponentVm.WantsRematch)
                {
                    if (!IsPinkSlipBet)
                    {
                        IsPinkSlipBet = true;
                        _pinkSlipBySelection = true;
                    }
                }
                else if (_pinkSlipBySelection)
                {
                    IsCashBet = true;
                }

                // Update wager limits based on opponent's money
                UpdateWagerLimits();

                // Load opponent dialogue; a regular who won't race the player's car says so instead
                if (opponentVm.IsSnubbing) OpponentMessage = ChallengeLauncher.SnubReply(opponentVm.Opponent);
                else _ = LoadOpponentMessageAsync(opponentVm, TalkTrigger.OpponentSelected);
            }
        }

        /// <summary>The power of the player's selected car, the figure the diners' classes go by; null without a car or a figure</summary>
        private double? PlayerPower()
        {
            var car = PracticeRuns.CarOf(_gameState);
            return car == null ? null : CarPower.Of(car, _catalogRepository.GetCar(car.DefinitionId));
        }

        private async Task LoadOpponentMessageAsync(OpponentDisplayViewModel opponentVm, TalkTrigger trigger)
        {
            try
            {
                OpponentMessage = await _talkService.GetMessageAsync(TalkContextFor(opponentVm, trigger));
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to load opponent message: {Error}", ex.Message);
                OpponentMessage = "";
            }
        }

        /// <summary>The moment a racer at the tables speaks in: who, to whom, about which cars, track and bet</summary>
        private TalkContext TalkContextFor(OpponentDisplayViewModel opponentVm, TalkTrigger trigger)
        {
            // Get player's car for context
            CarDefinition? playerCarDef = null;
            if (_gameState.Player.SelectedCarInstanceId != null)
            {
                var playerCar = _gameState.Player.Cars.FirstOrDefault(c =>
                    c.InstanceId == _gameState.Player.SelectedCarInstanceId);
                if (playerCar != null)
                {
                    playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
                }
            }

            var grudge = opponentVm.Opponent.Grudge;
            return new TalkContext
            {
                Opponent = opponentVm.Opponent,
                Player = _gameState.Player,
                OpponentCar = opponentVm.CarDefinition,
                PlayerCar = playerCarDef,
                Trigger = trigger,
                IsPinkSlipBet = IsPinkSlipBet,
                SelectedTrackName = SelectedTrack?.DisplayName,
                Grudge = grudge,
                GrudgeCarName = grudge == null ? null : CarNames.Of(_catalogRepository, grudge.CarDefinitionId),
                PlayerHasGrudgeCar = grudge != null && _gameState.Player.Cars.Any(c => c.InstanceId == grudge.CarInstanceId)
            };
        }

        private void LoadTracks()
        {
            DragTracks.Clear();
            RoadTracks.Clear();

            var tracks = _contentService.GetTracks();
            _logger.Information("Loading {Count} tracks for diner", tracks.Count);

            foreach (var track in tracks)
            {
                // A strip takes the drag races, any other track the road races
                var family = track.Type == TrackType.Dragstrip ? RaceFormatFamilies.Drag : RaceFormatFamilies.Road;

                // Get preview path
                var previewPath = _contentService.GetTrackPreviewPath(track.TrackId);

                // If track has configurations, create a card for each
                if (track.Configurations.Count > 0)
                {
                    foreach (var config in track.Configurations)
                    {
                        var trackVm = new TrackCardViewModel
                        {
                            Track = track,
                            Configuration = config,
                            PreviewPath = previewPath,
                            Family = family
                        };

                        if (trackVm.IsDrag)
                            DragTracks.Add(trackVm);
                        else
                            RoadTracks.Add(trackVm);
                    }
                }
                else
                {
                    // No configurations - create single card for the track
                    var trackVm = new TrackCardViewModel
                    {
                        Track = track,
                        Configuration = null,
                        PreviewPath = previewPath,
                        Family = family
                    };

                    if (trackVm.IsDrag)
                        DragTracks.Add(trackVm);
                    else
                        RoadTracks.Add(trackVm);
                }
            }

            _logger.Information("Loaded {DragCount} drag tracks and {RoadCount} road tracks",
                DragTracks.Count, RoadTracks.Count);

            // Notify UI
            OnPropertyChanged(nameof(HasDragTracks));
            OnPropertyChanged(nameof(HasRoadTracks));
            OnPropertyChanged(nameof(HasAnyTracks));

            // Auto-select the first track of what the street races most: a drag strip, unless it drags less than half the
            // time (a setting whose street never drags starts on a road)
            var street = Rules.RuleBook.Current.Street;
            var dragFirst = Math.Max(street.DragShareByDay, street.DragShareAtNight) >= 0.5;
            if (DragTracks.Count > 0 && (dragFirst || RoadTracks.Count == 0))
            {
                SelectedTrack = DragTracks[0];
            }
            else if (RoadTracks.Count > 0)
            {
                SelectedTrack = RoadTracks[0];
            }
        }

        private void OnSelectTrack(TrackCardViewModel? trackVm)
        {
            if (trackVm != null)
            {
                SelectedTrack = trackVm;
                _logger.Information("Selected track: {TrackName} ({TrackId})",
                    trackVm.DisplayName, trackVm.TrackId);
            }
        }

        private void UpdateMatchupStats()
        {
            MatchupStats.Clear();

            if (SelectedOpponent == null)
            {
                OnPropertyChanged(nameof(HasMatchupStats));
                return;
            }

            // Get player's selected car
            CarDefinition? playerCarDef = null;
            Models.GameState.Car? playerCar = null;
            if (_gameState.Player.SelectedCarInstanceId != null)
            {
                playerCar = _gameState.Player.Cars.FirstOrDefault(c =>
                    c.InstanceId == _gameState.Player.SelectedCarInstanceId);
                if (playerCar != null)
                {
                    playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
                }
            }

            if (playerCarDef == null)
            {
                OnPropertyChanged(nameof(HasMatchupStats));
                return;
            }

            foreach (var stat in MatchupCalculator.Compare(playerCarDef, SelectedOpponent.CarDefinition,
                         playerCar?.PowerHp, SelectedOpponent.Opponent.Cars.FirstOrDefault()?.PowerHp))
            {
                MatchupStats.Add(stat);
            }

            OnPropertyChanged(nameof(HasMatchupStats));
        }

        private void UpdateWagerLimits()
        {
            if (IsCashBet)
            {
                // Wager limits vary by race type (drag $10-$100, road $25-$250), go up at night and against a
                // rival with a name, and are capped by both racers' money
                var (min, max) = MatchupCalculator.WagerLimits(
                    SelectedFormatId,
                    _gameState.Player.Money,
                    SelectedOpponent?.Opponent.Money ?? 0m,
                    SelectedOpponent?.Opponent.Stats.Reputation ?? MatchupCalculator.StakesReputation,
                    _gameState.Date,
                    Diner?.Stakes ?? 1m);

                MinWager = min;
                MaxWager = max;
                WagerAmount = MatchupCalculator.ClampWager(WagerAmount, MinWager, MaxWager);
            }
            else
            {
                // Pink slip: no cash wager
                MinWager = 0m;
                MaxWager = 0m;
                WagerAmount = 0m;
            }

            // Notify affordability status
            OnPropertyChanged(nameof(StakesNote));
            OnPropertyChanged(nameof(HasStakesNote));
            OnPropertyChanged(nameof(PoliceNote));
            OnPropertyChanged(nameof(HasPoliceNote));
            OnPropertyChanged(nameof(CanAffordMinBet));
            OnPropertyChanged(nameof(InsufficientFundsMessage));
        }

        // The visit is being paid for: the day may turn over meanwhile, and the racers at the tables with it
        private bool _entering;

        // The visit's time being spent: done when there is none (back from a race, or paid for)
        private Task _visit = Task.CompletedTask;

        private bool CanChallenge()
        {
            return !_entering &&
                   IsParked &&
                   SelectedOpponent is { IsSnubbing: false } &&
                   SelectedTrack != null &&
                   _gameState.Player.Cars.Count > 0 &&
                   _gameState.Player.SelectedCarInstanceId != null;
        }

        /// <summary>Everything in here reads the catalog and the save: whatever throws is the player's to hear about, not the end of the game</summary>
        private async Task OnChallenge()
        {
            try
            {
                await ChallengeAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The challenge could not be set up");
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    GameText.Say("challenge.not_set_up", ("problem", ex.Message)),
                    GameText.Say("challenge.not_set_up.title")));
            }
        }

        private async Task ChallengeAsync()
        {
            if (SelectedOpponent == null)
            {
                _logger.Warning("Cannot challenge - no opponent selected");
                return;
            }

            if (SelectedTrack == null)
            {
                _logger.Warning("Cannot challenge - no track selected");
                return;
            }

            // Get player's selected car
            var playerCar = _gameState.Player.Cars.FirstOrDefault(c =>
                c.InstanceId == _gameState.Player.SelectedCarInstanceId);

            if (playerCar == null)
            {
                var errorDialog = new InformationDialogViewModel(
                    _dialogService,
                    GameText.Say("diner.no_car"),
                    GameText.Say("diner.no_car.title"));
                _dialogService.ShowDialog(errorDialog);
                return;
            }

            if (_catalogRepository.GetCar(playerCar.DefinitionId) == null)
            {
                _logger.Error("Player car definition not found");
                return;
            }

            // Get opponent's car
            var opponent = SelectedOpponent.Opponent;
            var opponentCar = opponent.Cars.FirstOrDefault();
            if (opponentCar == null)
            {
                _logger.Error("Opponent has no cars");
                return;
            }

            // The loading screen launches the race, spends its time and comes back when AC is closed; a rival who
            // turns it down says why at the table
            var outcome = await _launcher.RunAsync(new ChallengeRequest
            {
                Opponent = opponent,
                OpponentCar = opponentCar,
                OpponentDefinition = SelectedOpponent.CarDefinition,
                PlayerCar = playerCar,
                Track = SelectedTrack.Track,
                Configuration = SelectedTrack.Configuration,
                Format = SelectedFormatId ?? string.Empty,
                IsPinkSlip = IsPinkSlipBet,
                CashWager = IsCashBet ? WagerAmount : 0m,
                Bracket = IsBracket && CanBracket,
                Diner = Diner,
                AtDiner = true
            }, this);

            if (outcome.Status == ChallengeStatus.Declined) OpponentMessage = outcome.Message ?? "";
        }

        /// <summary>Home: the car backs out of its bay and drives off, and drives into the garage</summary>
        private void OnGarage()
        {
            _logger.Information("Driving home from the diner");
            DriveOff("back");
        }

        /// <summary>Why the player's car cannot be out on the streets (<see cref="StreetReady"/>); null when it can, or nobody can tell</summary>
        private NotStreetReady? WhyNotHere()
        {
            try
            {
                return StreetReady.WhyNot(_gameState, id => _catalogRepository.GetCar(id) != null, _partsService.IsAvailable ? _partsService.Catalog : null);
            }
            catch (Exception ex)
            {
                // The catalog waiting on its lock, or the parts catalog not loading: not knowing is no reason to send them home
                _logger.Error(ex, "Could not tell whether the car can be out on the streets");
                return null;
            }
        }

        public override async Task EnterAsync()
        {
            _logger.Information("Entered diner screen");

            // Not out in a car that could not have got here (a mod's way in that skips the racing hub's check): home again
            if (!_returning && WhyNotHere() is { } why)
            {
                _logger.Information("The car cannot be out on the streets: {Why}", why.Why);
                SendHome(why.Why, why.Title);
                return;
            }

            // Each on its own: a track folder that can't be read still leaves the racers at their tables
            try
            {
                LoadTracks();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the tracks at the diner");
            }

            try
            {
                LoadOpponents();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the racers at the diner");
            }

            // The car on the street: it comes down the street into its bay, or stands there already when back from a race.
            // A street that cannot be set up still leaves the car in its bay, the ways out open
            IsActive = true;
            try
            {
                SetUpStreet();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not set up the diner's street: the car stands in its bay");
                StandThereAtOnce();
            }

            if (_returning) return;

            // Nobody is challenged until the visit is paid for: a race set up against a racer who left with the old day
            // would stay against them
            _entering = true;
            RelayCommand.RaiseCanExecuteChanged();
            _visit = PayForVisitAsync();
            await _visit;
        }

        /// <summary>
        /// The visit's half hour, spent and saved; leaving waits for it (<see cref="OnGone"/>), as the day may roll over
        /// meanwhile
        /// </summary>
        private async Task PayForVisitAsync()
        {
            try
            {
                // Spend time for visiting the diner (30 min). Late in the evening that is the next morning, when
                // the racers at the tables have changed: the room is shown again, and the new day is saved.
                var spent = await _timeService.SpendTimeAsync(_gameState, GameAction.VisitDiner);
                if (spent.NewDayStarted) LoadOpponents();
                FollowTheClock();
                Save();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend and save the time for the diner");
            }
            finally
            {
                _entering = false;
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public override void Exit()
        {
            base.Exit();
            EndVisit();
            _logger.Information("Exited diner screen");
        }
    }
}
