using System.Collections.ObjectModel;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.Challenges
{
    /// <summary>
    /// The Challenges, over the screen it was opened from (a setting's garage tile): each Challenge on a card in the list,
    /// open or what still keeps it shut, and the one picked with its roads in order, their medals and bests, the go under
    /// way and its Drive button. A road is driven from here with the selected car, getting its traffic ready in front of
    /// the player as the racing hub's Free Drive does; it comes back to the screen under it.
    /// </summary>
    public sealed class ChallengesDialogViewModel : BaseDialogViewModel
    {
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Challenges");
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly PracticeRuns _practice;
        private readonly IGameStateRepository _repository;
        private readonly Func<bool> _stillHome;
        private readonly Action<DragRaceLaunchIntent> _race;
        private readonly IReadOnlyList<PracticeTrack> _roads;
        private ChallengeCardViewModel? _selected;
        private bool _isSettingUp;
        private CancellationTokenSource? _setupCancel;
        private string? _trafficNote;

        /// <param name="roads">The installed roads, one entry per layout (<see cref="PracticeTracks.All"/>)</param>
        /// <param name="stillHome">Whether the player is still on the screen this was opened over</param>
        /// <param name="race">Goes to the race, this closed</param>
        public ChallengesDialogViewModel(DialogService dialogService, GameState gameState, PracticeRuns practice, IGameStateRepository repository,
            IReadOnlyList<PracticeTrack> roads, Func<bool> stillHome, Action<DragRaceLaunchIntent> race)
            : base(dialogService)
        {
            _dialogService = dialogService;
            _gameState = gameState;
            _practice = practice;
            _repository = repository;
            _roads = roads;
            _stillHome = stillHome;
            _race = race;

            CloseCommand = new RelayCommand(() => Close(), () => !IsSettingUp);
            SelectCommand = new RelayCommand<ChallengeCardViewModel>(card => Selected = card, card => card != null && !IsSettingUp);
            DriveCommand = new RelayCommand(OnDrive, () => WhyNoDrive() == null);
            StartOverCommand = new RelayCommand(OnStartOver, () => !IsSettingUp && Selected is { IsOpen: true, HasGo: true });
            CancelSetupCommand = new RelayCommand(() =>
            {
                _setupCancel?.Cancel();
                OnPropertyChanged(nameof(CanCancelSetup));
            }, () => CanCancelSetup);

            ChallengeCareer.SyncCounters(_gameState.Career);
            Load();
        }

        public RelayCommand CloseCommand { get; }
        public RelayCommand<ChallengeCardViewModel> SelectCommand { get; }
        public RelayCommand DriveCommand { get; }
        public RelayCommand StartOverCommand { get; }
        public RelayCommand CancelSetupCommand { get; }

        public ObservableCollection<ChallengeCardViewModel> Cards { get; } = [];
        public bool HasCards => Cards.Count > 0;

        /// <summary>The roads of the Challenge picked, in order</summary>
        public ObservableCollection<ChallengeRoadViewModel> Roads { get; } = [];

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

        /// <summary>Why the road cannot be driven now, under the button; empty when it can</summary>
        public string DriveTip => WhyNoDrive() ?? string.Empty;

        public bool HasDriveTip => DriveTip.Length > 0;

        private void Load()
        {
            var keep = Selected?.Definition.Id;
            Cards.Clear();
            foreach (var challenge in ChallengeBook.Installed.All)
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

        private void ShowRoads()
        {
            Roads.Clear();
            if (Selected is not { } card) return;

            var challenge = card.Definition;
            var format = RaceFormats.Current.Get(challenge.Format);
            var progress = _gameState.Career.Challenges?.GetValueOrDefault(challenge.Id);
            var next = ChallengeCareer.NextRoad(_gameState.Career, challenge);
            var rules = Rules.RuleBook.Current.Unlocks;
            for (var i = 0; i < challenge.Roads.Count; i++)
            {
                var road = challenge.Roads[i];
                var installed = RoadOf(road);
                var laps = road.Laps ?? format?.Laps ?? 1;
                var medals = installed == null ? null : ChallengeCareer.MedalsOf(road, laps, installed.Track.FolderPath, rules);
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
        /// and the traffic made with this in front, then the race, this closed
        /// </summary>
        private async void OnDrive()
        {
            if (WhyNoDrive() != null || Selected is not { } card || PracticeRuns.CarOf(_gameState) is not { } car) return;
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
                DragRaceLaunchIntent? intent;
                _setupCancel = new CancellationTokenSource();
                IsSettingUp = true;
                try
                {
                    intent = await _practice.PrepareSoloRunAsync(_gameState, car, track, format,
                        new Progress<Services.Race.Traffic.TrafficProgress>(OnTrafficProgress), _setupCancel.Token,
                        new SoloRunOptions
                        {
                            Laps = road.Laps,
                            Minute = road.Minute,
                            Weather = road.Weather,
                            TrafficPerLaneKm = road.TrafficPerLaneKm,
                            TrafficCars = road.TrafficCars,
                            ChallengeId = challenge.Id,
                            ChallengeRoad = next
                        });
                }
                finally
                {
                    _setupCancel.Dispose();
                    _setupCancel = null;
                    _trafficNote = null;
                    IsSettingUp = false;
                }

                if (intent == null)
                {
                    Load();
                    return;
                }

                if (!_stillHome())
                {
                    _logger.Information("The screen was left while the road was got ready; no run");
                    return;
                }

                Close();
                _race(intent);
            }
            catch (Exception ex)
            {
                // Nothing may escape an async void
                _logger.Error(ex, "Could not drive challenge {Id}", challenge.Id);
                IsSettingUp = false;
                _dialogService.ShowDialog(new Information.InformationDialogViewModel(_dialogService,
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
        public string Blurb => Definition.Blurb ?? string.Empty;
        public bool HasBlurb => Blurb.Length > 0;
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
            Facts = IsInstalled ? string.Join("  ·  ", facts) : GameText.Say("challenges.road.missing");
            Targets = medals == null ? GameText.Say("challenges.road.no_medals")
                : GameText.Say("challenges.road.medals", ("bronze", medals.Bronze.ToString("N0", Locale.Culture)),
                    ("silver", medals.Silver.ToString("N0", Locale.Culture)), ("gold", medals.Gold.ToString("N0", Locale.Culture)));
            Best = best > 0 ? GameText.Say("challenges.road.best", ("score", best.ToString("N0", Locale.Culture))) : string.Empty;
            Medal = medal;
            ThisGo = thisGo is { } run ? GameText.Say("challenges.road.this_go", ("score", run.ToString("N0", Locale.Culture))) : string.Empty;
            IsNext = isNext;
        }

        /// <summary>AC's weather folder as words: "5_light_clouds" is "light clouds"</summary>
        private static string WeatherName(string folder)
        {
            var name = folder.Trim();
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
