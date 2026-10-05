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
    /// days, the car out on its own (a free run, a test-and-tune, each picking its track from cards), and the way out
    /// onto the streets. An invitation taken up is entered as it always was: the entry, a bracket
    /// race's dial-in, the race set up, the race. The hub closes before the entry shows (dialogs never nest), so the
    /// entry's answer comes back here with the hub gone; it comes back in front, with nothing to press, while the race
    /// is set up. A free run or a test-and-tune goes the same way: the hub closes for the track picker, and comes back
    /// in front while the car is made ready.
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
        private readonly PracticeRuns _practice;
        private readonly Func<bool> _stillHome;
        private readonly Action<DragRaceLaunchIntent, bool> _race;
        private readonly Action? _hitTheStreets;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("RaceHub");

        // True from the moment the player confirms an entry until the race is set up (or not): the hub can be opened
        // again meanwhile, and a second entry would start a second race
        private static bool _isEntering;

        private bool _isSettingUp;
        private bool _isPractice;

        /// <summary>
        /// True while the race entered is being set up: the hub stands in front meanwhile, with nothing to press, so the
        /// garage behind it (the end of the day, the workbench, the way out) cannot change what is being set up
        /// </summary>
        public bool IsSettingUp
        {
            get => _isSettingUp;
            private set
            {
                if (SetProperty(ref _isSettingUp, value)) RaisePracticeChanged();
            }
        }

        /// <summary>What the hub says while it waits in front: the cars got ready for a race, or the car for a run on its own</summary>
        public string SettingUpText => GameText.Say(_isPractice ? "racehub.setting_up.practice" : "racehub.setting_up");

        public RelayCommand CloseCommand { get; }
        public RelayCommand HitTheStreetsCommand { get; }
        public RelayCommand<EventInvitationViewModel> EnterEventCommand { get; }

        /// <summary>The selected car out on its own on a track the player picks: no opponent, no stake, an hour goes by</summary>
        public RelayCommand FreeRunCommand { get; }

        /// <summary>The selected car at a drag strip the player picks, pass after pass, for timeslips</summary>
        public RelayCommand TestAndTuneCommand { get; }

        // The tracks of the install as each run offers them, listed once as the hub opens
        private readonly List<PracticeTrack> _freeRunTracks;
        private readonly List<PracticeTrack> _strips;

        public ObservableCollection<EventInvitationViewModel> RaceInvitations { get; } = [];
        public bool HasRaceInvitations => RaceInvitations.Count > 0;

        /// <summary>What the paper wrote about the last few days' races, the lead story first</summary>
        public ObservableCollection<NewsArticleViewModel> Articles { get; } = [];
        public bool HasArticles => Articles.Count > 0;

        /// <summary>The day's date line over the news</summary>
        public string EditionDisplay => GameText.Date(_gameState.Date, "format.date.long");

        /// <param name="stillHome">Whether the player is still on the screen the hub was opened over: setting a race up
        /// takes a moment, and one who left meanwhile has called it off</param>
        /// <param name="race">Goes to the race; true: the screen is left as by its own way out (its leave script first),
        /// as the garage's test-and-tune always was</param>
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
            PracticeRuns practice,
            Func<bool> stillHome,
            Action<DragRaceLaunchIntent, bool> race,
            Action? hitTheStreets)
        {
            _dialogService = dialogService;
            _gameState = gameState;
            _eventService = eventService;
            _catalogRepository = catalogRepository;
            _eventOpponentService = eventOpponentService;
            _contentService = contentService;
            _raceSetup = raceSetup;
            _practice = practice;
            _stillHome = stillHome;
            _race = race;
            _hitTheStreets = hitTheStreets;

            CloseCommand = new RelayCommand(() => _dialogService.CloseDialog(), () => !IsSettingUp);
            HitTheStreetsCommand = new RelayCommand(OnHitTheStreets, () => !IsSettingUp && _hitTheStreets != null && _streetsChecked && _streetsWhyNot == null);
            EnterEventCommand = new RelayCommand<EventInvitationViewModel>(OnEnterEvent, _ => !_isEntering);
            _carListed = IsCarListed();
            _freeRunTracks = practice.FreeRunTracks();
            _strips = practice.Strips();
            if (hitTheStreets != null) CheckStreets();
            FreeRunCommand = new RelayCommand(OnFreeRun, () => CanPractice && _freeRunTracks.Count > 0);
            TestAndTuneCommand = new RelayCommand(OnTestAndTune, () => CanPractice && _strips.Count > 0);

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

        // Why the selected car cannot go out on the streets, worked out once as the hub opens (whether the engine starts takes
        // the dyno); the button waits for it
        private string? _streetsWhyNot;
        private bool _streetsChecked;

        /// <summary>Under the buttons: why the car cannot go out on the streets, or the word on going out</summary>
        public string StreetsNote => _streetsWhyNot ?? GameText.Say("racehub.streets.note");

        /// <summary>The car cannot go out on the streets now (<see cref="StreetsNote"/> says why)</summary>
        public bool HasStreetsProblem => _streetsWhyNot != null;

        private async void CheckStreets()
        {
            try
            {
                _streetsWhyNot = await _practice.WhyNotOnTheStreetsAsync(_gameState, _ => _carListed);
            }
            catch (Exception ex)
            {
                // Nothing may escape an async void; not knowing is no reason to keep the player in
                _logger.Error(ex, "Could not tell whether the car can go out on the streets");
                _streetsWhyNot = null;
            }

            _streetsChecked = true;
            OnPropertyChanged(nameof(StreetsNote));
            OnPropertyChanged(nameof(HasStreetsProblem));
            RelayCommand.RaiseCanExecuteChanged();
        }

        private void OnHitTheStreets()
        {
            if (_hitTheStreets == null || _streetsWhyNot != null) return;
            _dialogService.CloseDialog();
            _hitTheStreets();
        }

        // Whether the selected car is in the catalog, looked up once as the hub opens: the buttons ask on every input, and
        // the catalog is a database that may wait on its lock (the car cannot change while the hub is up)
        private readonly bool _carListed;

        private bool IsCarListed()
        {
            try
            {
                return PracticeRuns.CarOf(_gameState) is { } car && _catalogRepository.GetCar(car.DefinitionId) != null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not look the selected car up in the catalog");
                return false;
            }
        }

        /// <summary>Why the selected car cannot go out on its own now; null when it can</summary>
        private string? WhyNoPractice()
        {
            if (PracticeRuns.CarOf(_gameState) is not { } car || !_carListed) return GameText.Say("racehub.practice.no_car");
            if (car.IsImpounded) return GameText.Say("racehub.practice.impounded");
            if (_practice.IsBusy) return GameText.Say("racehub.practice.busy");
            return null;
        }

        private bool CanPractice => !IsSettingUp && !_isEntering && WhyNoPractice() == null;

        /// <summary>What the Free Run button does, or why it cannot</summary>
        public string FreeRunTip => WhyNoPractice()
            ?? (_freeRunTracks.Count == 0 ? GameText.Say("racehub.free_run.no_tracks") : GameText.Say("garage.free_run.tip"));

        /// <summary>What the Test and Tune button does, or why it cannot</summary>
        public string TestAndTuneTip => WhyNoPractice()
            ?? (_strips.Count == 0 ? GameText.Say("garage.no_strip") : GameText.Say("garage.test_and_tune.tip"));

        /// <summary>The buttons and their tips asked again: what they depend on changed</summary>
        private void RaisePracticeChanged()
        {
            RelayCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(FreeRunTip));
            OnPropertyChanged(nameof(TestAndTuneTip));
        }

        private void OnFreeRun() => PickTrack(strip: false);

        private void OnTestAndTune() => PickTrack(strip: true);

        /// <summary>
        /// The track picker in the hub's place (dialogs never nest): the run goes ahead from its pick, and Back brings the
        /// hub back
        /// </summary>
        private void PickTrack(bool strip)
        {
            if (!CanPractice) return;

            try
            {
                var tracks = strip ? _strips : _freeRunTracks;
                var last = strip
                    ? PracticeTracks.StripDefault(tracks, _practice.RememberedStrip)
                    : PracticeTracks.FreeRunDefault(tracks, _practice.RememberedFreeRun);
                _dialogService.ShowDialog(new TrackPicker.TrackPickerDialogViewModel(
                    _dialogService,
                    GameText.Say(strip ? "trackpicker.test_and_tune.title" : "trackpicker.free_run.title"),
                    GameText.Say(strip ? "trackpicker.test_and_tune.note" : "trackpicker.free_run.note"),
                    tracks,
                    last,
                    picked: track => OnPracticePicked(track, strip),
                    back: ShowAgain), jumpQueue: true);
                _dialogService.Withdraw(this);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the track picker");
                _dialogService.Withdraw(this);
                ShowPracticeError(ex, strip);
            }
        }

        /// <summary>
        /// The track picked: the car made ready with the hub in front, then the run. A free run runs Assetto Corsa from
        /// here and the player is in the garage again after it; a test-and-tune goes through the race, which comes back
        /// to the garage.
        /// </summary>
        private async void OnPracticePicked(PracticeTrack track, bool strip)
        {
            // Read once: the car checked is the one sent. One that cannot go now (Assetto Corsa got busy, the car was
            // impounded) brings the hub back, its button saying why
            if (_isEntering || PracticeRuns.CarOf(_gameState) is not { IsImpounded: false } car || _practice.IsBusy)
            {
                _logger.Information("The {Run} cannot go now: {Why}", strip ? "test-and-tune" : "free run", WhyNoPractice() ?? "a race is being entered");
                ShowAgain();
                return;
            }

            _isEntering = true;
            RaisePracticeChanged();
            try
            {
                Services.Configuration.Models.FreeRunLaunchIntent? freeRun = null;
                DragRaceLaunchIntent? testAndTune = null;

                // Checking the car takes a moment: the hub comes back in front meanwhile, as for a race
                SetPractice(true);
                IsSettingUp = true;
                _dialogService.ShowDialog(this, jumpQueue: true);
                try
                {
                    if (strip) testAndTune = await _practice.PrepareTestAndTuneAsync(_gameState, car, track);
                    else freeRun = await _practice.PrepareFreeRunAsync(_gameState, car, track);
                }
                finally
                {
                    _dialogService.Withdraw(this);
                    IsSettingUp = false;
                    SetPractice(false);
                }

                // A player who left meanwhile has called it off
                if (!_stillHome())
                {
                    _logger.Information("The garage was left while the car was made ready; no {Run}", strip ? "test-and-tune" : "free run");
                    return;
                }

                if (testAndTune != null)
                {
                    _race(testAndTune, true);
                }
                else if (freeRun != null)
                {
                    // Assetto Corsa holds its own lock from here: the hub's entry is done with
                    _isEntering = false;
                    RaisePracticeChanged();
                    await _practice.FreeRunAsync(_gameState, freeRun);
                }
            }
            catch (Exception ex)
            {
                // Nothing may escape an async void
                _logger.Error(ex, "Could not take the car out on {Track}", track.Key);
                ShowPracticeError(ex, strip);
            }
            finally
            {
                _isEntering = false;
                RaisePracticeChanged();
            }
        }

        /// <summary>The hub in front again, its buttons and tips as things are now</summary>
        private void ShowAgain()
        {
            RaisePracticeChanged();
            _dialogService.ShowDialog(this, jumpQueue: true);
        }

        private void ShowPracticeError(Exception ex, bool strip)
        {
            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                GameText.Say(strip ? "garage.strip_failed" : "garage.free_run_failed", ("error", ex.Message)),
                GameText.Say("garage.launch_error.title")));
        }

        private void SetPractice(bool practice)
        {
            _isPractice = practice;
            OnPropertyChanged(nameof(SettingUpText));
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
                : RaceSetupBuilder.PickTrack(_contentService.GetTracks(), definition.Format, definition.TrackId, result.EventInstanceId.GetHashCode(), definition.Laps);
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
            _race(setup.Intent, false);

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
                Format = definition.Format,
                Laps = definition.Laps,
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
