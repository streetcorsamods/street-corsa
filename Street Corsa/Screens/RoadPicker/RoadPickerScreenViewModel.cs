using System.Collections.ObjectModel;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.HighScores;
using StreetCorsa.Dialogs.TrackPicker;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Race.Traffic;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.RoadPicker
{
    /// <summary>
    /// The roads on a screen of their own (a lobby's Drive): a search over a scrolling grid of road cards at the left,
    /// the road picked at the right with its picture, what it is and the best runs on it, and the car driven under it.
    /// Drive takes the car out alone on it for the score of the game's format run so (a Free Drive, in traffic when the
    /// format is run in traffic); Free run takes it out with nothing scored. With no format run alone, every road of the
    /// install is offered for a free run only. A run goes through the race and comes back here; a free run runs Assetto
    /// Corsa from here. Its ways out: <c>back</c> (Escape), and <c>garage</c> (Change car), shown while it leads somewhere.
    /// </summary>
    public sealed class RoadPickerScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly PracticeRuns _practice;
        private readonly CarTuning? _tuning;
        private readonly RaceFormat? _solo;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("RoadPicker");

        private List<PracticeTrack> _roads = [];
        private Dictionary<PracticeTrack, TrackPickerCardViewModel> _cards = [];
        private PracticeTrack? _last;
        private IReadOnlyList<TrackPickerCardViewModel> _shown = [];
        private TrackPickerCardViewModel? _selected;
        private string _searchText = string.Empty;
        private bool _isLoading = true;
        private bool _isSettingUp;
        private bool _openingTune;
        private bool _going;
        private string? _trafficNote;
        private CancellationTokenSource? _setupCancel;
        private string _carLine = string.Empty;

        /// <param name="tuning">The Tune screen, for tuning between runs; none: no Tune button</param>
        public RoadPickerScreenViewModel(NavigationService navigationService, DialogService dialogService, GameState gameState,
            IContentCatalogRepository catalogRepository, PracticeRuns practice, CarTuning? tuning)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _practice = practice;
            _tuning = tuning;
            _solo = PracticeRuns.SoloFormat;

            PickCommand = new RelayCommand<TrackPickerCardViewModel>(OnPick, card => card != null && !IsBusy);
            DriveCommand = new RelayCommand(() => _ = RunAsync(scored: true), () => HasSolo && CanGo);
            FreeRunCommand = new RelayCommand(() => _ = RunAsync(scored: false), () => CanGo);
            TuneCommand = new AsyncRelayCommand(OnTuneAsync, () => CanTune && !IsBusy && WhyNotOut() == null && _tuning!.IsAvailable, name: "Tune");
            BackCommand = new RelayCommand(OnBack, () => !IsBusy);
            ChangeCarCommand = new RelayCommand(OnChangeCar, () => CanChangeCar && !IsBusy);
            CancelSetupCommand = new RelayCommand(() =>
            {
                _setupCancel?.Cancel();
                OnPropertyChanged(nameof(CanCancelSetup));
            }, () => CanCancelSetup);
        }

        /// <summary>Over the screen: the format's own words (roadpicker.title.&lt;id&gt;), else the plain ones</summary>
        public string Title => _solo == null ? GameText.Say("roadpicker.title.free_run") : RaceFormats.Current.SayFor(_solo, "roadpicker.title");

        /// <summary>Under the title: what Drive does here</summary>
        public string Note => _solo == null ? GameText.Say("roadpicker.note.free_run") : RaceFormats.Current.SayFor(_solo, "roadpicker.note");

        /// <summary>The Drive button's words: the format's own (racehub.solo.&lt;id&gt;), else the plain ones</summary>
        public string DriveText => _solo == null ? string.Empty : RaceFormats.Current.SayFor(_solo, "racehub.solo");

        /// <summary>A format may be run alone for the score: Drive is offered</summary>
        public bool HasSolo => _solo != null;

        public RelayCommand<TrackPickerCardViewModel> PickCommand { get; }
        public RelayCommand DriveCommand { get; }
        public RelayCommand FreeRunCommand { get; }
        public AsyncRelayCommand TuneCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand ChangeCarCommand { get; }
        public RelayCommand CancelSetupCommand { get; }

        /// <summary>The Tune screen was given</summary>
        public bool CanTune => _tuning != null;

        /// <summary>The flow gives the screen a way to the garage, to pick another car</summary>
        public bool CanChangeCar => _navigationService.CanLeave(this, "garage");

        /// <summary>The roads are still being looked for (a format run in traffic looks on the disk for every layout's plan)</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (!SetProperty(ref _isLoading, value)) return;
                OnPropertyChanged(nameof(HasNoRoads));
            }
        }

        /// <summary>The search, as it is typed: the cards narrow down with every letter</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty)) Refilter();
            }
        }

        /// <summary>The cards the search finds</summary>
        public IReadOnlyList<TrackPickerCardViewModel> Cards
        {
            get => _shown;
            private set
            {
                _shown = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CountDisplay));
                OnPropertyChanged(nameof(HasNoRoads));
            }
        }

        /// <summary>Looked for, and the search finds none</summary>
        public bool HasNoRoads => !IsLoading && _shown.Count == 0;

        /// <summary>"12 of 34"</summary>
        public string CountDisplay => GameText.Say("trackpicker.count", ("shown", _shown.Count), ("all", _roads.Count));

        /// <summary>The road picked: its sheet at the right, and what Drive takes</summary>
        public TrackPickerCardViewModel? Selected
        {
            get => _selected;
            private set
            {
                if (ReferenceEquals(_selected, value)) return;
                if (_selected != null) _selected.IsSelected = false;
                _selected = value;
                if (_selected != null) _selected.IsSelected = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelected));
                FillBest();
                RaiseButtons();
            }
        }

        public bool HasSelected => _selected != null;

        /// <summary>The best runs on the road picked, for the format run alone</summary>
        public ObservableCollection<HighScoreRowViewModel> Best { get; } = [];

        public bool HasBest => Best.Count > 0;

        /// <summary>No run on the road yet (with a format to score one)</summary>
        public bool HasNoBest => HasSolo && HasSelected && Best.Count == 0;

        /// <summary>The car driven, or why there is none to drive</summary>
        public string CarLine
        {
            get => _carLine;
            private set => SetProperty(ref _carLine, value);
        }

        /// <summary>
        /// True while the run is being set up (the car checked, the traffic made): the screen waits with nothing to press
        /// but the cancel of a long traffic wait
        /// </summary>
        public bool IsSettingUp
        {
            get => _isSettingUp;
            private set
            {
                if (!SetProperty(ref _isSettingUp, value)) return;
                RaiseButtons();
            }
        }

        /// <summary>What the screen says while it waits: the car got ready, or how the traffic is coming on</summary>
        public string SettingUpText => _trafficNote ?? GameText.Say("racehub.setting_up.practice");

        /// <summary>Traffic models are being made: the one wait long enough to want a way out</summary>
        public bool CanCancelSetup => _trafficNote != null && _setupCancel is { IsCancellationRequested: false };

        /// <summary>Why Drive, Free run and Tune cannot be pressed, or what they do</summary>
        public string Why => WhyNotOut() ?? (Selected == null ? GameText.Say("roadpicker.why.pick") : string.Empty);

        public bool HasWhy => Why.Length > 0;

        private bool IsBusy => _isSettingUp || _openingTune || _going || _navigationService.IsLeaving(this);

        private bool CanGo => !IsBusy && Selected != null && WhyNotOut() == null;

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered the road picker");
            CarLine = CarLineOf();
            _ = LoadRoadsAsync();
        }

        public override void Exit()
        {
            // A wait for traffic is called off with the screen: nothing would go out from it
            _setupCancel?.Cancel();
            base.Exit();
        }

        public override void Resume()
        {
            base.Resume();
            _going = false;
            CarLine = CarLineOf();
            RaiseButtons();
        }

        /// <summary>
        /// The roads, looked for off the UI thread: every road of the install for a free run, of them for a format run in
        /// traffic only those with a traffic plan or an AI line to build one from. The road driven last first, picked.
        /// </summary>
        private async Task LoadRoadsAsync()
        {
            try
            {
                var solo = _solo;
                var all = _practice.FreeRunTracks();
                _roads = solo == null ? all : await Task.Run(() => PracticeRuns.SoloRoads(solo, all));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the roads");
                _roads = [];
            }

            _last = PracticeTracks.FreeRunDefault(_roads, _solo == null ? _practice.RememberedFreeRun : _practice.RememberedSoloRoad);
            _cards = _roads.ToDictionary(t => t, t => new TrackPickerCardViewModel(t, ReferenceEquals(t, _last)));
            IsLoading = false;
            Refilter();
            Selected = _last != null && _cards.TryGetValue(_last, out var card) ? card : _shown.FirstOrDefault();
        }

        private void Refilter()
        {
            if (IsLoading) return;
            Cards = PracticeTracks.Filter(_roads, _searchText, _last, null).Select(t => _cards[t]).ToList();

            // What Drive would take must be in sight: a pick the search hid gives way to the first card found
            if (_selected != null && !_shown.Contains(_selected)) Selected = _shown.FirstOrDefault();
            else if (_selected == null) Selected = _shown.FirstOrDefault();
        }

        private void OnPick(TrackPickerCardViewModel? card)
        {
            if (card != null && !IsBusy) Selected = card;
        }

        /// <summary>The road's table for the format run alone, its first few runs</summary>
        private const int BestRows = 5;

        private void FillBest()
        {
            Best.Clear();
            try
            {
                if (_solo != null && Selected is { } card)
                {
                    var road = card.Track;
                    var place = 0;
                    foreach (var run in ScoredRun.Table(_gameState.Career.ScoredRuns ?? [], _solo.Id, road.TrackId, road.Configuration, _solo.HigherWins)
                                 .Take(BestRows))
                        Best.Add(HighScoreRowViewModel.Run(++place, run, _solo.ScoreIsTime,
                            _catalogRepository.GetCar(run.CarDefinitionId)?.Name ?? run.CarDefinitionId));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not read the road's best runs");
            }

            OnPropertyChanged(nameof(HasBest));
            OnPropertyChanged(nameof(HasNoBest));
        }

        /// <summary>Why the car driven cannot go out now; null when it can</summary>
        private string? WhyNotOut()
        {
            if (PracticeRuns.CarOf(_gameState) is not { } car || !IsListed(car)) return GameText.Say("racehub.practice.no_car");
            if (car.IsImpounded) return GameText.Say("racehub.practice.impounded");
            if (_practice.IsBusy) return GameText.Say("racehub.practice.busy");
            return null;
        }

        // Whether the catalog has the car, by its model: looked up once a car (the buttons ask on every input, and the
        // catalog is a database that may wait on its lock)
        private string? _listedId;
        private bool _listed;

        private bool IsListed(Car car)
        {
            if (string.Equals(_listedId, car.DefinitionId, StringComparison.OrdinalIgnoreCase)) return _listed;
            _listedId = car.DefinitionId;
            try
            {
                _listed = _catalogRepository.GetCar(car.DefinitionId) != null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not look the car driven up in the catalog");
                _listed = false;
            }

            return _listed;
        }

        private string CarLineOf()
        {
            try
            {
                return PracticeRuns.CarOf(_gameState) is { } car && _catalogRepository.GetCar(car.DefinitionId) is { } definition
                    ? GameText.Say("roadpicker.car", ("car", Services.Showcase.ShowcaseContent.Title(definition)))
                    : GameText.Say("racehub.practice.no_car");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not name the car driven");
                return GameText.Say("racehub.practice.no_car");
            }
        }

        private void RaiseButtons()
        {
            RelayCommand.RaiseCanExecuteChanged();
            TuneCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(Why));
            OnPropertyChanged(nameof(HasWhy));
            OnPropertyChanged(nameof(HasNoBest));
        }

        /// <summary>
        /// Out on the road picked: the car made ready with the screen waiting, then the run. A run for the score goes
        /// through the race, which comes back here; a free run runs Assetto Corsa from here.
        /// </summary>
        private async Task RunAsync(bool scored)
        {
            // Read once: the car checked is the one sent
            if (!CanGo || Selected is not { } card || PracticeRuns.CarOf(_gameState) is not { IsImpounded: false } car) return;
            if (scored && _solo == null) return;

            var road = card.Track;
            IsSettingUp = true;
            try
            {
                Services.Configuration.Models.FreeRunLaunchIntent? freeRun = null;
                Services.Configuration.Models.DragRaceLaunchIntent? throughRace = null;
                try
                {
                    if (scored)
                    {
                        _setupCancel = new CancellationTokenSource();
                        throughRace = await _practice.PrepareSoloRunAsync(_gameState, car, road, _solo!,
                            new Progress<TrafficProgress>(OnTrafficProgress), _setupCancel.Token);
                    }
                    else
                    {
                        freeRun = await _practice.PrepareFreeRunAsync(_gameState, car, road);
                    }
                }
                finally
                {
                    _setupCancel?.Dispose();
                    _setupCancel = null;
                    _trafficNote = null;
                    OnPropertyChanged(nameof(SettingUpText));
                    OnPropertyChanged(nameof(CanCancelSetup));
                }

                // A player who left meanwhile has called it off
                if (!ReferenceEquals(_navigationService.CurrentScreen, this))
                {
                    _logger.Information("The road picker was left while the car was made ready; no run");
                    return;
                }

                if (throughRace != null)
                {
                    _going = true;
                    if (!_navigationService.GoRace(_gameState, throughRace)) _going = false;
                }
                else if (freeRun != null)
                {
                    IsSettingUp = false;
                    await _practice.FreeRunAsync(_gameState, freeRun);
                    FillBest();
                }
            }
            catch (Exception ex)
            {
                // Nothing may escape a fire-and-forget run
                _logger.Error(ex, "Could not take the car out on {Track}", road.Key);
                _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                    _dialogService,
                    GameText.Say("garage.free_run_failed", ("error", ex.Message)),
                    GameText.Say("garage.launch_error.title")));
            }
            finally
            {
                IsSettingUp = false;
                RaiseButtons();
            }
        }

        private void OnTrafficProgress(TrafficProgress progress)
        {
            _trafficNote = progress.Making == null ? null
                : GameText.Say("racehub.traffic.making", ("done", progress.Done + 1), ("of", progress.Of), ("car", progress.Making));
            OnPropertyChanged(nameof(SettingUpText));
            OnPropertyChanged(nameof(CanCancelSetup));
            RelayCommand.RaiseCanExecuteChanged();
        }

        /// <summary>The Tune screen for the car driven, between runs</summary>
        private async Task OnTuneAsync()
        {
            if (_tuning == null || IsBusy || WhyNotOut() != null || PracticeRuns.CarOf(_gameState) is not { } car) return;

            _openingTune = true;
            RaiseButtons();
            try
            {
                await _tuning.ShowAsync(_gameState, car, CarNames.Of(_catalogRepository, car.DefinitionId),
                    stillWanted: () => ReferenceEquals(_navigationService.CurrentScreen, this) && PracticeRuns.CarOf(_gameState) == car
                        && !_dialogService.IsDialogOpen);
            }
            finally
            {
                _openingTune = false;
                RaiseButtons();
            }
        }

        private void OnBack()
        {
            if (IsBusy) return;
            Leave("back");
        }

        private void OnChangeCar()
        {
            if (IsBusy || !CanChangeCar) return;
            Leave("garage");
        }

        private void Leave(string exit)
        {
            _going = true;
            RaiseButtons();
            if (!_navigationService.Leave(this, exit, _gameState))
            {
                _going = false;
                RaiseButtons();
            }
        }
    }
}
