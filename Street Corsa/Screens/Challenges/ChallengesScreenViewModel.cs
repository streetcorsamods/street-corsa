using System.Collections.ObjectModel;
using StreetCorsa.Controls;
using StreetCorsa.Dialogs;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Challenges
{
    /// <summary>
    /// The Challenges on a screen of their own: each on a card down the left, open or what still keeps it shut; behind
    /// them a camera flown along the roads of the one picked, a few seconds on each (<see cref="RoadBackdropViewport3D"/>);
    /// at the right its roads in order with their medals and bests and the go under way; Start over and Drive at the
    /// foot. A road is driven with the selected car, its traffic got ready in front of the player as the road picker
    /// does, through the race, which comes back here. Its ways out: <c>back</c> (Escape), and <c>garage</c> (the car's
    /// button at the top right, to pick another), shown while it leads somewhere.
    /// </summary>
    public sealed class ChallengesScreenViewModel : BaseScreenViewModel
    {
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Challenges");
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly PracticeRuns _practice;
        private readonly IGameStateRepository _repository;
        private readonly Func<IReadOnlyList<PracticeTrack>> _installed;
        private readonly Func<List<ShowroomCar>> _cars;
        private IReadOnlyList<PracticeTrack> _roads = [];
        private ChallengeCardViewModel? _selected;
        private IReadOnlyList<BackdropRoad> _backdropRoads = [];
        private bool _isSettingUp;
        private bool _going;
        private CancellationTokenSource? _setupCancel;
        private string? _trafficNote;
        private DrivenCar? _car;

        /// <param name="installed">The installed roads, one entry per layout (<see cref="PracticeTracks.All"/>), read at Enter</param>
        /// <param name="cars">The game's cars by tier, for a road's rivals (<see cref="Services.Career.Showroom.Cars"/>)</param>
        public ChallengesScreenViewModel(NavigationService navigationService, DialogService dialogService, GameState gameState,
            IContentCatalogRepository catalogRepository, PracticeRuns practice, IGameStateRepository repository,
            Func<IReadOnlyList<PracticeTrack>> installed, Func<List<ShowroomCar>>? cars = null)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _practice = practice;
            _repository = repository;
            _installed = installed;
            _cars = cars ?? (() => []);

            SelectCommand = new RelayCommand<ChallengeCardViewModel>(card => Selected = card, card => card != null && !IsBusy);
            DriveCommand = new RelayCommand(OnDrive, () => !IsBusy && WhyNoDrive() == null);
            StartOverCommand = new RelayCommand(OnStartOver, () => !IsBusy && Selected is { IsOpen: true, HasGo: true });
            BackCommand = new RelayCommand(() => Leave("back"), () => !IsBusy);
            ChangeCarCommand = new RelayCommand(() => Leave("garage"), () => CanChangeCar && !IsBusy);
            CancelSetupCommand = new RelayCommand(() =>
            {
                _setupCancel?.Cancel();
                OnPropertyChanged(nameof(CanCancelSetup));
            }, () => CanCancelSetup);
        }

        public RelayCommand<ChallengeCardViewModel> SelectCommand { get; }
        public RelayCommand DriveCommand { get; }
        public RelayCommand StartOverCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand ChangeCarCommand { get; }
        public RelayCommand CancelSetupCommand { get; }

        public ObservableCollection<ChallengeCardViewModel> Cards { get; } = [];
        public bool HasCards => Cards.Count > 0;

        /// <summary>The roads of the Challenge picked, in order</summary>
        public ObservableCollection<ChallengeRoadViewModel> Roads { get; } = [];

        /// <summary>The roads of the Challenge picked that are installed, for the camera behind the screen to fly</summary>
        public IReadOnlyList<BackdropRoad> BackdropRoads
        {
            get => _backdropRoads;
            private set => SetProperty(ref _backdropRoads, value);
        }

        /// <summary>The flow gives the screen a way to the garage, to pick another car</summary>
        public bool CanChangeCar => _navigationService.CanLeave(this, "garage");

        /// <summary>The car driven, on its card at the top right (shared with Home and the roads); null with none</summary>
        public DrivenCar? Car
        {
            get => _car;
            private set
            {
                _car = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCar));
            }
        }

        public bool HasCar => _car != null;

        /// <summary>The banked score, and where the tiers stand</summary>
        public string Banked
        {
            get
            {
                var rules = Rules.RuleBook.Current.Unlocks;
                var score = ChallengeCareer.CareerScore(_gameState.Career);
                var tier = ChallengeCareer.OpenTier(score, rules);
                var banked = GameText.Say("challenges.banked", ("score", score.ToString("N0", Locale.Culture)));
                if (rules.Tiers.Length == 0 || tier >= rules.Tiers.Length) return banked;
                var next = rules.Tiers[tier];
                return banked + GameText.Say("challenges.next_tier", ("tier", tier + 1), ("short", (next - score).ToString("N0", Locale.Culture)));
            }
        }

        public ChallengeCardViewModel? Selected
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value)) return;
                if (_selected != null) _selected.IsSelected = false;
                _selected = value;
                if (_selected != null) _selected.IsSelected = true;
                ShowRoads();
                BackdropRoads = BackdropRoadsOf(_selected);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelected));
                RaiseDriveChanged();
            }
        }

        public bool HasSelected => Selected != null;

        public bool IsSettingUp
        {
            get => _isSettingUp;
            private set
            {
                _isSettingUp = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SettingUpText));
                OnPropertyChanged(nameof(CanCancelSetup));
                RaiseDriveChanged();
            }
        }

        /// <summary>True while traffic models are being made: the one wait long enough to want a way out</summary>
        public bool CanCancelSetup => _trafficNote != null && _setupCancel is { IsCancellationRequested: false };

        /// <summary>What is going on while the road is got ready: the car checked, the traffic made</summary>
        public string SettingUpText => _trafficNote ?? GameText.Say("challenges.setting_up");

        /// <summary>The Drive button's words: the go's next road, or the first</summary>
        public string DriveText => Selected?.Definition is { } challenge && ChallengeCareer.NextRoad(_gameState.Career, challenge) is { } next
            ? GameText.Say("challenges.drive.next", ("road", next + 1), ("roads", challenge.Roads.Count))
            : GameText.Say("challenges.drive.start");

        /// <summary>Why the road cannot be driven now, over the buttons; empty when it can</summary>
        public string DriveTip => WhyNoDrive() ?? string.Empty;

        public bool HasDriveTip => DriveTip.Length > 0;

        private bool IsBusy => _isSettingUp || _going || _navigationService.IsLeaving(this);

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered the Challenges");
            _roads = _installed();
            ChallengeCareer.SyncCounters(_gameState.Career);
            Car = DrivenCarOf();
            Load();
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
            Car = DrivenCarOf();
            Load();
        }

        private void Load()
        {
            var keep = Selected?.Definition.Id;
            Cards.Clear();
            // A region's Challenges together, the regions in the order the mods first name them
            var all = ChallengeBook.Installed.All;
            var regions = all.Select(c => c.Region?.Trim() ?? string.Empty).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var challenge in all.OrderBy(c => regions.FindIndex(r => r.Equals(c.Region?.Trim() ?? string.Empty, StringComparison.CurrentCultureIgnoreCase))))
                Cards.Add(new ChallengeCardViewModel(challenge, _gameState.Career, RoadOf));
            OnPropertyChanged(nameof(HasCards));
            OnPropertyChanged(nameof(Banked));
            _selected = null;
            Selected = Cards.FirstOrDefault(c => c.Definition.Id == keep)
                ?? Cards.FirstOrDefault(c => c.HasGo)
                ?? Cards.FirstOrDefault(c => c.IsOpen && !c.IsFinished)
                ?? Cards.FirstOrDefault();
        }

        /// <summary>The installed road a Challenge road names: its layout, or the track's first when it names none; null when it is not installed</summary>
        private PracticeTrack? RoadOf(ChallengeRoad road) =>
            _roads.FirstOrDefault(t => string.Equals(t.TrackId, road.Track, StringComparison.OrdinalIgnoreCase)
                && (road.Layout == null || string.Equals(t.Configuration, road.Layout, StringComparison.OrdinalIgnoreCase)));

        /// <summary>A Challenge's installed roads, each once, in its order: the go's next road first, for the camera to start on</summary>
        private IReadOnlyList<BackdropRoad> BackdropRoadsOf(ChallengeCardViewModel? card)
        {
            if (card == null) return [];
            var challenge = card.Definition;
            var roads = challenge.Roads.Select(RoadOf).OfType<PracticeTrack>()
                .DistinctBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                .Select(t => new BackdropRoad(t.Track.FolderPath, t.TrackId, t.Configuration, t.DisplayName))
                .ToList();
            if (ChallengeCareer.NextRoad(_gameState.Career, challenge) is { } next && RoadOf(challenge.Roads[next]) is { } nextRoad
                && roads.FindIndex(r => r.Folder == nextRoad.Track.FolderPath && r.Layout == nextRoad.Configuration) is > 0 and var at)
                roads = roads.Skip(at).Concat(roads.Take(at)).ToList();
            return roads;
        }

        // Counted up as the roads are shown: a list still waiting on its AI lines when another is asked for is dropped
        private int _roadsShown;

        /// <summary>
        /// The roads of the Challenge picked. A road's medals come from its AI line, megabytes on the disk: the lines not
        /// read yet are read off this thread first, the list filled once they are (at once when none is wanting).
        /// </summary>
        private async void ShowRoads()
        {
            var shown = ++_roadsShown;
            Roads.Clear();
            if (Selected is not { } card) return;

            var challenge = card.Definition;
            try
            {
                var unread = challenge.Roads.Select(road => (Road: road, Installed: RoadOf(road)))
                    .Where(r => r.Installed != null && !ChallengeCareer.MedalsAtHand(r.Road, r.Installed.Track.FolderPath, r.Installed.Configuration))
                    .Select(r => (r.Installed!.Track.FolderPath, r.Installed.Configuration))
                    .ToList();
                if (unread.Count > 0)
                {
                    var points = Rules.RuleBook.Current.Unlocks.ReferencePoints;
                    await Task.Run(() =>
                    {
                        foreach (var (folder, layout) in unread) ChallengeCareer.ReferenceScore(folder, layout, 1, points);
                    });
                    if (shown != _roadsShown) return;
                }

                ListRoads(challenge, withMedals: true);
            }
            catch (Exception ex)
            {
                // Nothing may escape an async void; the roads are listed all the same, without the medals a line that
                // would not read gives (never read again here, on this thread)
                _logger.Error(ex, "Could not read the roads' AI lines of challenge {Id}", challenge.Id);
                if (shown != _roadsShown) return;
                try
                {
                    Roads.Clear();
                    ListRoads(challenge, withMedals: false);
                }
                catch (Exception again)
                {
                    _logger.Error(again, "Could not list the roads of challenge {Id}", challenge.Id);
                }
            }
        }

        /// <param name="withMedals">The roads' medals worked out (their AI lines are at hand); false: only those pinned</param>
        private void ListRoads(ChallengeDefinition challenge, bool withMedals)
        {
            var format = RaceFormats.Current.Get(challenge.Format);
            var progress = _gameState.Career.Challenges?.GetValueOrDefault(challenge.Id);
            var next = ChallengeCareer.NextRoad(_gameState.Career, challenge);
            var rules = Rules.RuleBook.Current.Unlocks;
            for (var i = 0; i < challenge.Roads.Count; i++)
            {
                var road = challenge.Roads[i];
                var installed = RoadOf(road);
                var laps = road.Laps ?? format?.Laps ?? 1;
                // By the layout driven: a road that names none is driven on the track's first
                var medals = installed == null ? null
                    : withMedals ? ChallengeCareer.MedalsOf(road, laps, installed.Track.FolderPath, installed.Configuration, rules)
                    : road.Medals is { InOrder: true } pinned ? pinned : null;
                double? thisGo = progress is { Started: true } && i < progress.Go.Count ? progress.Go[i] : null;
                Roads.Add(new ChallengeRoadViewModel(i, road, installed, laps, medals, progress?.BestOn(i) ?? 0, progress?.MedalOn(i) ?? Medal.None,
                    thisGo, isNext: next == i));
            }
        }

        private string? WhyNoDrive()
        {
            if (IsSettingUp) return null;
            if (Selected is not { } card) return GameText.Say("challenges.why.none");
            if (!card.IsOpen) return card.LockText;
            if (card.Missing.Count > 0) return GameText.Say("challenges.why.missing", ("roads", string.Join(", ", card.Missing)));
            if (RaceFormats.Current.Get(card.Definition.Format) == null) return GameText.Say("challenges.why.no_format");
            if (PracticeRuns.CarOf(_gameState) is not { } car) return GameText.Say("challenges.why.no_car");
            if (car.IsImpounded) return GameText.Say("challenges.why.impounded");
            if (_practice.IsBusy) return GameText.Say("challenges.why.busy");
            return null;
        }

        private void RaiseDriveChanged()
        {
            OnPropertyChanged(nameof(DriveText));
            OnPropertyChanged(nameof(DriveTip));
            OnPropertyChanged(nameof(HasDriveTip));
            RelayCommand.RaiseCanExecuteChanged();
        }

        private DrivenCar? DrivenCarOf()
        {
            try
            {
                return DrivenCar.Of(_gameState, _catalogRepository);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not tell of the car driven");
                return null;
            }
        }

        private void OnStartOver()
        {
            if (Selected is not { } card) return;
            ChallengeCareer.Start(_gameState.Career, card.Definition);
            Save();
            Load();
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(_gameState.SaveName)) return;
            try
            {
                _repository.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the Challenge's go");
            }
        }

        private void Leave(string exit)
        {
            if (IsBusy) return;
            _going = true;
            RaiseDriveChanged();
            if (!_navigationService.Leave(this, exit, _gameState))
            {
                _going = false;
                RaiseDriveChanged();
            }
        }

        /// <summary>The road's rivals, rolled now (experimental): none when it names none</summary>
        private List<ChallengeRival> RivalsFor(ChallengeRoad road, Car car, PracticeTrack track)
        {
            if (road.Rivals is not (> 0 and var wanted)) return [];
            // No more cars than the track has pit boxes for
            if (track.Pitboxes is { } pits && wanted > pits - 1)
            {
                _logger.Warning("{Track} has {Pits} pit boxes: {Rivals} rival(s), not {Wanted}", track.Key, pits, Math.Max(0, pits - 1), wanted);
                road = new ChallengeRoad { Track = road.Track, Rivals = Math.Max(0, pits - 1), RivalCars = road.RivalCars, RivalSkill = road.RivalSkill, RivalAggression = road.RivalAggression };
            }

            var cars = _cars();
            var tier = cars.FirstOrDefault(c => string.Equals(c.Definition.Id, car.DefinitionId, StringComparison.OrdinalIgnoreCase))?.Tier ?? 1;
            return ChallengeRivals.Pick(road, cars, tier, random => GameText.Installed.Pick("names.event_racers", random), Random.Shared);
        }

        private void OnTrafficProgress(Services.Race.Traffic.TrafficProgress progress)
        {
            _trafficNote = progress.Making == null ? null
                : GameText.Say("racehub.traffic.making", ("done", progress.Done + 1), ("of", progress.Of), ("car", progress.Making));
            OnPropertyChanged(nameof(SettingUpText));
            OnPropertyChanged(nameof(CanCancelSetup));
            RelayCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// The go's next road with the selected car (a go started on the first when none is under way): the car checked
        /// and the traffic made with the screen waiting, then the race, which comes back here
        /// </summary>
        private async void OnDrive()
        {
            // Not twice: Enter or a second click while a road is being got ready would set it up again beside the first
            if (IsBusy || WhyNoDrive() != null || Selected is not { } card || PracticeRuns.CarOf(_gameState) is not { } car) return;
            var challenge = card.Definition;
            var format = RaceFormats.Current.Get(challenge.Format)!;
            try
            {
                if (ChallengeCareer.NextRoad(_gameState.Career, challenge) is not { } next)
                {
                    ChallengeCareer.Start(_gameState.Career, challenge);
                    Save();
                    next = 0;
                }

                var road = challenge.Roads[next];
                var track = RoadOf(road)!;
                var rivals = RivalsFor(road, car, track);
                DragRaceLaunchIntent? intent;
                var setupCancel = _setupCancel = new CancellationTokenSource();
                IsSettingUp = true;
                try
                {
                    intent = await _practice.PrepareSoloRunAsync(_gameState, car, track, format,
                        new Progress<Services.Race.Traffic.TrafficProgress>(OnTrafficProgress), setupCancel.Token,
                        new SoloRunOptions
                        {
                            Laps = road.Laps,
                            Minute = road.Minute,
                            Weather = road.Weather,
                            TrafficPerLaneKm = road.TrafficPerLaneKm,
                            TrafficCars = road.TrafficCars,
                            ChallengeId = challenge.Id,
                            ChallengeRoad = next,
                            Rivals = rivals,
                            // What keeps the road from being driven is told over this screen
                            TellInFront = true
                        });
                }
                finally
                {
                    setupCancel.Dispose();
                    _setupCancel = null;
                    _trafficNote = null;
                    IsSettingUp = false;
                }

                if (intent == null)
                {
                    Load();
                    return;
                }

                // A player who left meanwhile has called it off
                if (!ReferenceEquals(_navigationService.CurrentScreen, this))
                {
                    _logger.Information("The Challenges were left while the road was got ready; no run");
                    return;
                }

                _going = true;
                if (!_navigationService.GoRace(_gameState, intent)) _going = false;
                RaiseDriveChanged();
            }
            catch (Exception ex)
            {
                // Nothing may escape an async void
                _logger.Error(ex, "Could not drive challenge {Id}", challenge.Id);
                IsSettingUp = false;
                _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(_dialogService,
                    GameText.Say("challenges.failed", ("error", ex.Message)), GameText.Say("challenges.title")));
            }
        }
    }

    /// <summary>A Challenge on its card in the list: its name, region, blurb, and where the player stands with it</summary>
    public sealed class ChallengeCardViewModel : ObservableObject
    {
        private bool _isSelected;

        public ChallengeCardViewModel(ChallengeDefinition challenge, CareerState career, Func<ChallengeRoad, PracticeTrack?> installed)
        {
            Definition = challenge;
            var locks = ChallengeCareer.WhatKeepsShut(challenge, career);
            IsOpen = locks.Count == 0;
            LockText = IsOpen ? string.Empty : string.Join(" ", locks.Select(Say));
            Missing = challenge.Roads.Where(r => installed(r) == null).Select(r => r.Layout == null ? r.Track : $"{r.Track}/{r.Layout}").Distinct().ToList();
            var progress = career.Challenges?.GetValueOrDefault(challenge.Id);
            IsFinished = progress is { Finished: > 0 };
            HasGo = progress is { Started: true };
            Status = !IsOpen ? LockText
                : Missing.Count > 0 ? GameText.Say("challenges.card.missing", ("count", Missing.Count))
                : HasGo ? GameText.Say("challenges.card.go", ("road", progress!.Go.Count + 1), ("roads", challenge.Roads.Count))
                : progress?.BestTotal is { } best ? GameText.Say("challenges.card.best", ("score", best.ToString("N0", Locale.Culture)))
                : GameText.Say("challenges.card.new", ("roads", challenge.Roads.Count));
            Golds = progress?.Medals?.Count(m => m == Medal.Gold) ?? 0;
        }

        private static string Say(ChallengeLock shut) => shut.Kind switch
        {
            ChallengeLockKind.Challenge => GameText.Say("challenges.lock.challenge",
                ("challenge", ChallengeBook.Installed.Get(shut.Challenge)?.Name ?? shut.Challenge ?? string.Empty)),
            ChallengeLockKind.Score => GameText.Say("challenges.lock.score", ("short", shut.Short.ToString("N0", Locale.Culture))),
            _ => GameText.Say("challenges.lock.gold", ("short", (int)shut.Short))
        };

        public ChallengeDefinition Definition { get; }
        public string Name => Definition.Name;
        public string Region => Definition.Region ?? string.Empty;
        public bool HasRegion => Region.Length > 0;

        /// <summary>Its region is shown: it has one, and it is not the Challenge's own name over again</summary>
        public bool ShowsRegion => HasRegion && !string.Equals(Region.Trim(), Name.Trim(), StringComparison.CurrentCultureIgnoreCase);
        public string Blurb => Definition.Blurb ?? string.Empty;
        public bool HasBlurb => Blurb.Length > 0;

        /// <summary>Its picture; null with none</summary>
        public System.Windows.Media.ImageSource? Picture => _picture ??= Thumbnails.Load(Definition.PictureFile, 640);
        public bool HasPicture => Definition.PictureFile != null;
        private System.Windows.Media.ImageSource? _picture;
        public bool IsOpen { get; }

        /// <summary>What still keeps it shut, in words</summary>
        public string LockText { get; }

        /// <summary>Its roads the install lacks</summary>
        public IReadOnlyList<string> Missing { get; }

        public bool IsFinished { get; }

        /// <summary>A go is under way</summary>
        public bool HasGo { get; }

        /// <summary>Where the player stands with it, in a line</summary>
        public string Status { get; }

        public int Golds { get; }
        public string GoldsText => GameText.Say("challenges.card.golds", ("golds", Golds), ("roads", Definition.Roads.Count));

        /// <summary>Shut ones are shown dimmed</summary>
        public double Opacity => IsOpen ? 1 : 0.55;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>One road of the Challenge picked: what it is, its medals, the best on it, and this go's run</summary>
    public sealed class ChallengeRoadViewModel
    {
        public ChallengeRoadViewModel(int index, ChallengeRoad road, PracticeTrack? installed, int laps, ChallengeMedals? medals, double best,
            Medal medal, double? thisGo, bool isNext)
        {
            Number = (index + 1).ToString(Locale.Culture);
            Name = installed?.DisplayName ?? (road.Layout == null ? road.Track : $"{road.Track} - {road.Layout}");
            IsInstalled = installed != null;
            var facts = new List<string> { GameText.Say(laps == 1 ? "challenges.road.lap" : "challenges.road.laps", ("laps", laps)) };
            if (road.Time is { Length: > 0 } time && road.Minute != null) facts.Add(time);
            if (road.Weather is { Length: > 0 } weather) facts.Add(WeatherName(weather));
            if (road.Rivals is > 0 and var rivals) facts.Add(GameText.Say(rivals == 1 ? "challenges.road.rival" : "challenges.road.rivals", ("rivals", rivals)));
            Facts = IsInstalled ? string.Join("  ·  ", facts) : GameText.Say("challenges.road.missing");
            Targets = medals == null ? GameText.Say("challenges.road.no_medals")
                : GameText.Say("challenges.road.medals", ("bronze", medals.Bronze.ToString("N0", Locale.Culture)),
                    ("silver", medals.Silver.ToString("N0", Locale.Culture)), ("gold", medals.Gold.ToString("N0", Locale.Culture)));
            Best = best > 0 ? GameText.Say("challenges.road.best", ("score", best.ToString("N0", Locale.Culture))) : string.Empty;
            Medal = medal;
            ThisGo = thisGo is { } run ? GameText.Say("challenges.road.this_go", ("score", run.ToString("N0", Locale.Culture))) : string.Empty;
            IsNext = isNext;
        }

        /// <summary>
        /// A weather in words: the text pools' for its folder (<c>weather.3_clear</c>, the core's for AC's own, a
        /// setting's or a translation's for any other), else the folder's own name, "5_light_clouds" as "light clouds"
        /// </summary>
        private static string WeatherName(string folder)
        {
            var name = folder.Trim();
            var key = "weather." + name.ToLowerInvariant();
            if (GameText.Installed.Has(key)) return GameText.Say(key);

            var cut = name.IndexOf('_');
            if (cut > 0 && name[..cut].All(char.IsDigit)) name = name[(cut + 1)..];
            return name.Replace('_', ' ');
        }

        public string Number { get; }
        public string Name { get; }
        public bool IsInstalled { get; }
        public string Facts { get; }
        public string Targets { get; }
        public string Best { get; }
        public bool HasBest => Best.Length > 0;
        public Medal Medal { get; }
        public bool HasMedal => Medal != Medal.None;
        public bool IsGold => Medal == Medal.Gold;
        public bool IsSilver => Medal == Medal.Silver;
        public bool IsBronze => Medal == Medal.Bronze;

        public string MedalText => Medal switch
        {
            Medal.Gold => GameText.Say("challenges.medal.gold"),
            Medal.Silver => GameText.Say("challenges.medal.silver"),
            Medal.Bronze => GameText.Say("challenges.medal.bronze"),
            _ => string.Empty
        };

        /// <summary>The road's run in the go under way</summary>
        public string ThisGo { get; }
        public bool HasThisGo => ThisGo.Length > 0;

        /// <summary>The go's next road</summary>
        public bool IsNext { get; }
    }
}
