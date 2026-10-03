using System.Collections.ObjectModel;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.News;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.RaceHub
{
    /// <summary>
    /// The racing hub, over the garage (its "Go race!" button): the race invitations, what the street did the last few
    /// days, and the way out onto the streets. An invitation taken up is entered as it always was: the entry, a bracket
    /// race's dial-in, the race set up, the race. The hub closes before the entry shows (dialogs never nest), so the
    /// entry's answer comes back here with the hub gone; it comes back in front, with nothing to press, while the race
    /// is set up.
    /// </summary>
    public class RaceHubDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IRaceEventService _eventService;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IEventOpponentService _eventOpponentService;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly RaceSetupBuilder _raceSetup;
        private readonly Func<bool> _stillHome;
        private readonly Action<DragRaceLaunchIntent> _race;
        private readonly Action? _hitTheStreets;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("RaceHub");

        // True from the moment the player confirms an entry until the race is set up (or not): the hub can be opened
        // again meanwhile, and a second entry would start a second race
        private static bool _isEntering;

        private bool _isSettingUp;

        /// <summary>
        /// True while the race entered is being set up: the hub stands in front meanwhile, with nothing to press, so the
        /// garage behind it (the end of the day, the workbench, the way out) cannot change what is being set up
        /// </summary>
        public bool IsSettingUp
        {
            get => _isSettingUp;
            private set
            {
                if (SetProperty(ref _isSettingUp, value)) RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public RelayCommand CloseCommand { get; }
        public RelayCommand HitTheStreetsCommand { get; }
        public RelayCommand<EventInvitationViewModel> EnterEventCommand { get; }

        public ObservableCollection<EventInvitationViewModel> RaceInvitations { get; } = [];
        public bool HasRaceInvitations => RaceInvitations.Count > 0;

        /// <summary>What the paper wrote about the last few days' races, the lead story first</summary>
        public ObservableCollection<NewsArticleViewModel> Articles { get; } = [];
        public bool HasArticles => Articles.Count > 0;

        /// <summary>The day's date line over the news</summary>
        public string EditionDisplay => GameText.Date(_gameState.Date, "format.date.long");

        /// <param name="stillHome">Whether the player is still on the screen the hub was opened over: setting a race up
        /// takes a moment, and one who left meanwhile has called it off</param>
        /// <param name="race">Goes to the race</param>
        /// <param name="hitTheStreets">Where the "Hit the streets" button goes, once the hub is closed; none: no such button</param>
        public RaceHubDialogViewModel(
            DialogService dialogService,
            GameState gameState,
            IRaceEventService eventService,
            ICarFilterService filterService,
            IContentCatalogRepository catalogRepository,
            IEventOpponentService eventOpponentService,
            IAssettoCorsaContentService contentService,
            RaceSetupBuilder raceSetup,
            Func<bool> stillHome,
            Action<DragRaceLaunchIntent> race,
            Action? hitTheStreets)
        {
            _dialogService = dialogService;
            _gameState = gameState;
            _eventService = eventService;
            _catalogRepository = catalogRepository;
            _eventOpponentService = eventOpponentService;
            _contentService = contentService;
            _raceSetup = raceSetup;
            _stillHome = stillHome;
            _race = race;
            _hitTheStreets = hitTheStreets;

            CloseCommand = new RelayCommand(() => _dialogService.CloseDialog(), () => !IsSettingUp);
            HitTheStreetsCommand = new RelayCommand(OnHitTheStreets, () => !IsSettingUp && _hitTheStreets != null);
            EnterEventCommand = new RelayCommand<EventInvitationViewModel>(OnEnterEvent, _ => !_isEntering);

            try
            {
                foreach (var invitation in Dialogs.RaceHub.RaceInvitations.Load(gameState, eventService, filterService, catalogRepository))
                    RaceInvitations.Add(invitation);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the race invitations");
            }

            LoadArticles();
        }

        private void LoadArticles()
        {
            try
            {
                var page = NewsWriter.FrontPage(_gameState.News, _gameState.Date);
                for (var i = 0; i < page.Count; i++) Articles.Add(new NewsArticleViewModel(page[i], _gameState.Date, isLead: i == 0));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not lay out the street news");
            }
        }

        /// <summary>The way out onto the streets is there (a game may have none)</summary>
        public bool CanHitTheStreets => _hitTheStreets != null;

        private void OnHitTheStreets()
        {
            if (_hitTheStreets == null) return;
            _dialogService.CloseDialog();
            _hitTheStreets();
        }

        private void OnEnterEvent(EventInvitationViewModel? eventVm)
        {
            if (eventVm == null || !eventVm.CanEnter || _isEntering)
                return;

            try
            {
                var eventDef = _eventService.GetEventDefinition(eventVm.EventId);
                if (eventDef == null)
                    return;

                // What comes next takes the hub's place, ahead of any dialog waiting behind it
                var opponent = _eventOpponentService.GetOpponentForEvent(eventDef, _gameState);
                if (opponent == null)
                {
                    _dialogService.ShowDialog(new InformationDialogViewModel(
                        _dialogService,
                        GameText.Say(eventDef.IsPinkSlip ? "paper.enter.no_pink_slip" : "paper.enter.no_opponent"),
                        GameText.Say("paper.enter.cannot.title")), jumpQueue: true);
                    _dialogService.Withdraw(this);
                    return;
                }

                var opponentCarDef = _catalogRepository.GetCar(opponent.CarDefinitionId);
                var opponentCarDisplay = opponentCarDef != null
                    ? CarNames.Of(opponentCarDef)
                    : GameText.Say("paper.unknown_car");

                _dialogService.ShowDialog(new EventEntry.EventEntryDialogViewModel(
                    _dialogService,
                    eventVm,
                    eventDef,
                    opponent,
                    opponentCarDisplay,
                    OnEventEntryComplete), jumpQueue: true);
                _dialogService.Withdraw(this);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the entry for {Event}", eventVm.EventId);
                _dialogService.Withdraw(this);
                ShowRaceError(ex);
            }
        }

        /// <summary>
        /// The player entered: the event is raced on its own track, with each car on what its parts make of it,
        /// through the same set-up as a challenge at the diner.
        /// </summary>
        private async void OnEventEntryComplete(EventEntry.EventEntryResult? result)
        {
            if (result == null || _isEntering)
                return;

            _isEntering = true;
            RelayCommand.RaiseCanExecuteChanged();
            try
            {
                await EnterEventAsync(result);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not set up the race for {Event}", result.EventId);
                ShowRaceError(ex);
            }
            finally
            {
                _isEntering = false;
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task EnterEventAsync(EventEntry.EventEntryResult result)
        {
            var playerCar = _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == result.PlayerCarInstanceId);
            if (playerCar == null)
                return;

            var playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
            var opponentCarDef = _catalogRepository.GetCar(result.Opponent.CarDefinitionId);
            if (playerCarDef == null || opponentCarDef == null)
            {
                _logger.Warning("Event {Event}: a car is missing from the catalog (player {Player}, opponent {Opponent})",
                    result.EventId, playerCar.DefinitionId, result.Opponent.CarDefinitionId);
                return;
            }

            // A bracket race is run to the quarter, on a strip that runs it: the mode only ends one there
            var definition = result.EventDefinition;
            var isBracket = definition.IsBracketRace;
            var track = isBracket
                ? RaceSetupBuilder.PickStrip(_contentService.GetTracks(), quarterOnly: true, definition.TrackId)
                : RaceSetupBuilder.PickTrack(_contentService.GetTracks(), definition.RaceType, definition.TrackId, result.EventInstanceId.GetHashCode());
            if (track == null)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    GameText.Say(isBracket ? "paper.enter.no_strip" : "paper.enter.no_track"),
                    GameText.Say("paper.enter.cannot.title")));
                return;
            }

            // The pool racer drives as they are; a one-off entrant is given the event's skill and aggression.
            // Either way the AC values come from the adapter, the one place they are made.
            var opponent = result.Opponent;
            var driver = opponent.PoolOpponent
                ?? new Opponent(opponent.OpponentName, 25, Gender.Other, opponent.Skill, opponent.Aggression);
            var ai = OpponentAIAdapter.ToAssettoCorsaAI(driver, _gameState.Rules);

            // A bracket race: the rival dials in from its car (a one-off entrant's has no history), the player picks theirs
            BracketSetup? bracket = null;
            if (isBracket)
            {
                var rivalDialIn = BracketRules.RivalDialInFor(opponent.PoolOpponentCar ?? new Car(opponent.CarDefinitionId), opponentCarDef);
                var playerDialIn = await DialIn.DialInDialogViewModel.AskAsync(_dialogService, playerCar, playerCarDef,
                    CarNames.Of(playerCarDef), opponent.OpponentName, rivalDialIn);
                if (playerDialIn == null)
                {
                    _logger.Information("The player backed out of {Event} at the dial-in", result.EventId);
                    return;
                }

                bracket = BracketRules.Setup(playerDialIn.Value, rivalDialIn, ai.AILevel);
            }

            // Setting the cars up takes a moment: the hub comes back in front meanwhile, so nothing in the garage behind it
            // changes the cars or the day the race is being set up from
            IsSettingUp = true;
            _dialogService.ShowDialog(this, jumpQueue: true);
            RaceSetupResult setup;
            try
            {
                setup = await BuildSetupAsync();
            }
            finally
            {
                _dialogService.Withdraw(this);
                IsSettingUp = false;
            }

            // The player could not have left the garage meanwhile, but the screen may have been changed under it
            if (!_stillHome())
            {
                _logger.Information("The garage was left while {Event} was set up; no race", result.EventId);
                return;
            }

            if (setup.Intent == null)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    GameText.Say("paper.enter.wont_run", ("problem", setup.PlayerCarProblem)),
                    GameText.Say("paper.enter.wont_run.title")));
                return;
            }

            _logger.Information("Entering {Event} on {Track} {Config}", result.EventId, track.Value.TrackId, track.Value.TrackConfig ?? "");
            _race(setup.Intent);

            Task<RaceSetupResult> BuildSetupAsync() => _raceSetup.BuildAsync(new RaceEntry
            {
                PlayerName = _gameState.Player.Name,
                PlayerCar = playerCar,
                OpponentName = opponent.OpponentName,
                OpponentCarId = opponent.CarDefinitionId,
                OpponentSkin = opponent.CarSkin,
                OpponentCar = opponent.PoolOpponentCar,
                OpponentAI = ai,
                TrackId = track.Value.TrackId,
                TrackConfig = track.Value.TrackConfig,
                RaceType = definition.RaceType,
                CashWager = 0, // Event rewards handled separately
                IsPinkSlip = result.IsPinkSlip,
                DamagePercent = _gameState.Rules.RaceDamagePercent,
                EventId = result.EventId,
                EventInstanceId = result.EventInstanceId,
                IsEventOnlyOpponent = !opponent.IsPoolOpponent,
                // An organised event: the police stay away, but it runs at the game's hour, after dark too
                RaceTime = _gameState.Date,
                Bracket = bracket
            });
        }

        private void ShowRaceError(Exception ex)
        {
            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                GameText.Say("paper.enter.failed", ("error", ex.Message)),
                GameText.Say("paper.enter.failed.title")));
        }
    }
}
