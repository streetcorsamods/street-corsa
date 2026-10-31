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
    /// onto the streets (Hit the streets, to the diners' map). An invitation taken up is entered as it always was: the entry, a bracket
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
        private readonly ICarFilterService _filterService;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IEventOpponentService _eventOpponentService;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly RaceSetupBuilder _raceSetup;
        private readonly PracticeRuns _practice;
        private readonly CarTuning? _tuning;
        private readonly Func<bool> _stillHome;
        private readonly Action<DragRaceLaunchIntent, bool> _race;
        private readonly Action? _hitTheStreets;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("RaceHub");

        // True from the moment the player confirms an entry until the race is set up (or not): the hub can be opened
        // again meanwhile, and a second entry would start a second race
        private static bool _isEntering;

        private bool _isSettingUp;
        private bool _isPractice;

        // True while the Tune screen is made for the hub's car (it takes the car's data a moment): nothing else to press
        private bool _openingTune;

        // The day the hub's invitations and news are of: a tune that ran the clock past it brings the new day's
        private DateTime _shownOn;

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

        /// <summary>The Tune screen for the selected car, between runs: the hub comes back once it closes</summary>
        public AsyncRelayCommand TuneCommand { get; }

        /// <summary>Whether the hub has a Tune button: it was given the Tune screen</summary>
        public bool CanTune => _tuning != null;

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
        /// <param name="hitTheStreets">Where the "Hit the streets" button goes, once the hub is closed (the diners' map); none: no such button</param>
        /// <param name="tuning">The Tune screen, for tuning between runs; none: no Tune button</param>
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
            Action? hitTheStreets,
            CarTuning? tuning = null)
            : base(dialogService)
        {
            _dialogService = dialogService;
            _gameState = gameState;
            _eventService = eventService;
            _filterService = filterService;
            _catalogRepository = catalogRepository;
            _eventOpponentService = eventOpponentService;
            _contentService = contentService;
            _raceSetup = raceSetup;
            _practice = practice;
            _stillHome = stillHome;
            _race = race;
            _hitTheStreets = hitTheStreets;
            _tuning = tuning;

            CloseCommand = new RelayCommand(() => Close(), () => !IsSettingUp && !_openingTune);
            HitTheStreetsCommand = new RelayCommand(OnHitTheStreets, () => CanGoOut);
            EnterEventCommand = new RelayCommand<EventInvitationViewModel>(OnEnterEvent, _ => !_isEntering && !_openingTune);
            _carListed = IsCarListed();
            _freeRunTracks = practice.FreeRunTracks();
            _strips = practice.Strips();
            if (hitTheStreets != null) CheckStreets();
            FreeRunCommand = new RelayCommand(OnFreeRun, () => CanPractice && _freeRunTracks.Count > 0);
            TestAndTuneCommand = new RelayCommand(OnTestAndTune, () => CanPractice && _strips.Count > 0);
            TuneCommand = new AsyncRelayCommand(OnTune, () => CanPractice && _tuning?.IsAvailable == true, name: "Tune");

            LoadInvitations();
            LoadArticles();
        }

        private void LoadInvitations()
        {
            _shownOn = _gameState.Date;
            try
            {
                foreach (var invitation in Dialogs.RaceHub.RaceInvitations.Load(_gameState, _eventService, _filterService, _catalogRepository))
                    RaceInvitations.Add(invitation);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the race invitations");
            }
        }

        /// <summary>
        /// The hub as a tune left things: whether the car can go out checked again (the engine may start now, or not), and
        /// a new day's invitations and news when the tune's time ran past the day's end
        /// </summary>
        private void RefreshAfterTune()
        {
            if (_hitTheStreets != null)
            {
                _streetsChecked = false;
                CheckStreets();
            }

            if (_gameState.Date == _shownOn) return;

            RaceInvitations.Clear();
            LoadInvitations();
            Articles.Clear();
            LoadArticles();
            OnPropertyChanged(nameof(HasRaceInvitations));
            OnPropertyChanged(nameof(HasArticles));
            OnPropertyChanged(nameof(EditionDisplay));
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

        /// <summary>The car can go out now: the way is there, and the car was checked and found fit</summary>
        private bool CanGoOut => !IsSettingUp && !_openingTune && _hitTheStreets != null && _streetsChecked && _streetsWhyNot == null;

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
                // No diner in the game (none in the core alone): nowhere to race from, whatever the car
                _streetsWhyNot = Services.Diner.DinerBook.Installed.All.Count == 0
                    ? GameText.Say("racehub.streets.no_diners")
                    : (await _practice.WhyNotOnTheStreetsAsync(_gameState, _ => _carListed))?.Why;
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

        /// <summary>Out onto the streets: the diners' map, where the player picks the diner to drive down to</summary>
        private void OnHitTheStreets()
        {
            if (!CanGoOut) return;
            Close();
            _hitTheStreets!();
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

        private bool CanPractice => !IsSettingUp && !_isEntering && !_openingTune && WhyNoPractice() == null;

        /// <summary>What the Free Run button does, or why it cannot</summary>
        public string FreeRunTip => WhyNoPractice()
            ?? (_freeRunTracks.Count == 0 ? GameText.Say("racehub.free_run.no_tracks") : GameText.Say("garage.free_run.tip"));

        /// <summary>What the Test and Tune button does, or why it cannot</summary>
        public string TestAndTuneTip => WhyNoPractice()
            ?? (_strips.Count == 0 ? GameText.Say("garage.no_strip") : GameText.Say("garage.test_and_tune.tip"));

        /// <summary>What the Tune button does, or why it cannot</summary>
        public string TuneTip => WhyNoPractice() ?? GameText.Say("garage.tune.tip");

        /// <summary>The buttons and their tips asked again: what they depend on changed</summary>
        private void RaisePracticeChanged()
        {
            RelayCommand.RaiseCanExecuteChanged();
            TuneCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(FreeRunTip));
            OnPropertyChanged(nameof(TestAndTuneTip));
            OnPropertyChanged(nameof(TuneTip));
        }

        /// <summary>
        /// The Tune screen in the hub's place (dialogs never nest), made while the hub stays up: tune, run, come back,
        /// tune. The hub comes back as it closes, unless the player has left the screen the hub was opened over.
        /// </summary>
        private async Task OnTune()
        {
            if (!CanPractice || _tuning == null || PracticeRuns.CarOf(_gameState) is not { } car) return;

            var name = CarNames.Of(_catalogRepository, car.DefinitionId);
            var tuned = false;
            bool shown;
            _openingTune = true;
            RaisePracticeChanged();
            try
            {
                // Still wanted only with the hub still in front, on the screen it was opened over, for the same car
                shown = await _tuning.ShowAsync(_gameState, car, name,
                    stillWanted: () => !IsSettingUp && !_isEntering && WhyNoPractice() == null && _stillHome()
                        && ReferenceEquals(_dialogService.CurrentDialog, this) && PracticeRuns.CarOf(_gameState) == car,
                    tuned: () => tuned = true,
                    closed: () =>
                    {
                        if (!_stillHome()) return;
                        if (tuned) RefreshAfterTune();
                        ShowAgain();
                    },
                    jumpQueue: true);
            }
            finally
            {
                _openingTune = false;
                RaisePracticeChanged();
            }

            if (shown) Close();
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
                Close();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the track picker");
                Close();
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
                    Close();
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
                    Close();
                    return;
                }

                string CarOf(EventOpponentResult rival) => _catalogRepository.GetCar(rival.CarDefinitionId) is { } car
                    ? CarNames.Of(car)
                    : GameText.Say("paper.unknown_car");

                // The rival's crew, when they ride with one: named under their car (a crew night's leader comes first)
                var crews = Services.Opponents.CrewBook.Current;
                var opponentCrew = crews.CrewOf(opponent.PoolOpponent) is { } own
                    ? GameText.Say(Services.Opponents.CrewBook.IsLeader(own, opponent.PoolOpponent) ? "event.entry.crew.leader" : "event.entry.crew", ("crew", own.Name))
                    : null;

                // A field: the rest of who lines up, each in their car; only those the event's track has room for, as
                // the race will cut it
                var fieldTrack = opponent.Field.Count > 0 ? EventTrack(eventDef, eventVm.InstanceId) : null;
                var fieldLines = (fieldTrack is { } at ? FieldThatFits(opponent, at).Skip(1) : opponent.Field)
                    .Select(r => crews.CrewOf(r.PoolOpponent) is { } crew
                        ? GameText.Say("event.field.line.crew", ("name", r.OpponentName), ("crew", crew.Name), ("car", CarOf(r)))
                        : GameText.Say("event.field.line", ("name", r.OpponentName), ("car", CarOf(r))))
                    .ToList();

                _dialogService.ShowDialog(new EventEntry.EventEntryDialogViewModel(
                    _dialogService,
                    eventVm,
                    eventDef,
                    opponent,
                    CarOf(opponent),
                    OnEventEntryComplete,
                    fieldLines,
                    _gameState.Player.Money,
                    opponentCrew), jumpQueue: true);
                Close();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the entry for {Event}", eventVm.EventId);
                Close();
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

        /// <summary>
        /// Where the event is raced: a bracket race to the quarter on a strip that runs it (the mode only ends one there),
        /// anything else on its track, the same pick every time for one invitation
        /// </summary>
        private (string TrackId, string? TrackConfig)? EventTrack(Models.Career.Events.RaceEventDefinition definition, Guid instanceId) => definition.IsBracketRace
            ? RaceSetupBuilder.PickStrip(_contentService.GetTracks(), quarterOnly: true, definition.TrackId)
            : RaceSetupBuilder.PickTrack(_contentService.GetTracks(), definition.Format, definition.TrackId, instanceId.GetHashCode(), definition.Laps);

        /// <summary>The event's rivals, the opponent first, who race on <paramref name="track"/>: in a car the catalog knows, as many as it has pit boxes for</summary>
        private List<EventOpponentResult> FieldThatFits(EventOpponentResult opponent, (string TrackId, string? TrackConfig) track)
        {
            var layout = _contentService.GetTracks().FirstOrDefault(t => string.Equals(t.TrackId, track.TrackId, StringComparison.OrdinalIgnoreCase));
            var pitboxes = layout?.Configurations.FirstOrDefault(c => string.Equals(c.FolderName, track.TrackConfig, StringComparison.OrdinalIgnoreCase))?.Pitboxes
                ?? layout?.Pitboxes;
            var known = opponent.All().Where(r => _catalogRepository.GetCar(r.CarDefinitionId) != null).ToList();
            return EventGrid.Fit(known, pitboxes);
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
            var track = EventTrack(definition, result.EventInstanceId);
            if (track == null)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    GameText.Say(isBracket ? "paper.enter.no_strip" : "paper.enter.no_track"),
                    GameText.Say("paper.enter.cannot.title")));
                return;
            }

            // A field lines up as the event says, as many as the track has pit boxes for, every rival in a car the
            // catalog knows; the first on the grid is the race's opponent. A race of two is the one rival.
            var opponent = result.Opponent;
            var field = new List<EventOpponentResult>();
            int? playerGrid = null;
            if (opponent.Field.Count > 0)
            {
                var fitting = FieldThatFits(opponent, track.Value);
                if (fitting.Count < opponent.All().Count())
                    _logger.Information("{Event}: {Track} has pit boxes for {Count} rivals of {Field}", result.EventId, track.Value.TrackId, fitting.Count, opponent.All().Count());
                var (lined, position) = EventGrid.Order(definition.Field?.Grid, _gameState.Player.Stats.Reputation, fitting, result.EventInstanceId.GetHashCode());
                opponent = lined[0];
                field = lined.Skip(1).ToList();
                playerGrid = position;
                opponentCarDef = _catalogRepository.GetCar(opponent.CarDefinitionId)!;
                _logger.Information("{Event}: a field of {Count}, the player {Position} on the grid", result.EventId, lined.Count, position);
            }

            // The pool racer drives as they are; a one-off entrant is given the event's skill and aggression.
            // Either way the AC values come from the adapter, the one place they are made.
            AssettoCorsaAIParameters AiOf(EventOpponentResult rival) => OpponentAIAdapter.ToAssettoCorsaAI(
                rival.PoolOpponent ?? new Opponent(rival.OpponentName, 25, Gender.Other, rival.Skill, rival.Aggression), _gameState.Rules);
            var ai = AiOf(opponent);

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
                Close();
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
                Bracket = bracket,
                Field = field.Select(rival => new RaceRival
                {
                    Name = rival.OpponentName,
                    CarId = rival.CarDefinitionId,
                    Skin = rival.CarSkin,
                    Car = rival.PoolOpponentCar,
                    AI = AiOf(rival),
                    IsEventOnly = !rival.IsPoolOpponent
                }).ToList(),
                PlayerGridPosition = playerGrid,
                Stagger = definition.Field?.Stagger
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
