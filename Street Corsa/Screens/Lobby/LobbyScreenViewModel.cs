using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using StreetCorsa.Configuration;
using StreetCorsa.Controls.Showcase;
using StreetCorsa.Dialogs;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.Garage;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Garage;
using StreetCorsa.Services.Showcase;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Lobby
{
    /// <summary>
    /// A game's front page (a lobby, shown as Home), for a game whose garage is only where a car is picked: the car the
    /// player drives filmed in the garage's room as the main screen films its cars, what to do as the flow's tiles of
    /// the screen (drive, the Challenges, the garage to pick another car, the high scores, the settings, the way out),
    /// and how far the career has got. The tiles of its "left" spot (and of "right" and the bottom ones) are its menu,
    /// one under the other in order; "top_left" and "top_right" are round buttons in its corners. A tile does one of the
    /// lobby's own things (<see cref="TileActions.Quit"/>, <see cref="TileActions.Radio"/>, <see cref="TileActions.Settings"/>:
    /// the Settings card over the screen, as the main screen opens it) or goes to its screen: an overlay (the Challenges,
    /// the high scores) opens over the lobby, any other takes its place.
    /// </summary>
    public sealed class LobbyScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Lobby");

        private BaseScreenViewModel? _card;
        private IReadOnlyList<ShowcaseCar> _showcaseCars = [];
        private IReadOnlyList<ShowcaseScene> _showcaseScenes = [];
        private bool _going;

        public LobbyScreenViewModel(NavigationService navigationService, DialogService dialogService, GameState gameState,
            IContentCatalogRepository catalogRepository)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            CloseCardCommand = new RelayCommand(StepBackCard, () => _card != null && !_dialogService.IsDialogOpen);
        }

        /// <summary>The game's name over the menu (game.json's title)</summary>
        public string Title => Rules.GameFile.Installed.Title;

        /// <summary>The menu, one under the other</summary>
        public IReadOnlyList<LobbyTile> Menu { get; private set; } = [];

        /// <summary>The round buttons of the top left corner</summary>
        public IReadOnlyList<LobbyTile> TopLeft { get; private set; } = [];

        /// <summary>The round buttons of the top right corner</summary>
        public IReadOnlyList<LobbyTile> TopRight { get; private set; } = [];

        /// <summary>The car driven, filmed: none with no car (the screen shows its picture instead)</summary>
        public IReadOnlyList<ShowcaseCar> ShowcaseCars
        {
            get => _showcaseCars;
            private set => SetProperty(ref _showcaseCars, value);
        }

        /// <summary>The room it is filmed in: the garage's</summary>
        public IReadOnlyList<ShowcaseScene> ShowcaseScenes
        {
            get => _showcaseScenes;
            private set => SetProperty(ref _showcaseScenes, value);
        }

        /// <summary>The car driven, as the panel tells of it; null with none</summary>
        public DrivenCar? Car { get; private set; }

        public bool HasCar => Car != null;

        /// <summary>How far the career has got: the banked score, the next tier, the Challenges finished; null for a game with none</summary>
        public LobbyProgress? Progress { get; private set; }

        public bool HasProgress => Progress != null;

        /// <summary>The Settings card over the screen, while it is up</summary>
        public BaseScreenViewModel? CurrentCard
        {
            get => _card;
            private set
            {
                if (!SetProperty(ref _card, value)) return;
                OnPropertyChanged(nameof(HasCard));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public bool HasCard => _card != null;

        /// <summary>Escape: the card steps back (closing once it is on its first page)</summary>
        public RelayCommand CloseCardCommand { get; }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered the lobby");
            Refresh();
        }

        public override void Exit()
        {
            if (_card != null)
            {
                try
                {
                    _card.Exit();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "The settings card could not be left cleanly");
                }

                CurrentCard = null;
            }

            base.Exit();
        }

        public override void Resume()
        {
            base.Resume();
            _going = false;
            RelayCommand.RaiseCanExecuteChanged();
        }

        /// <summary>The tiles, the car and the progress as things stand</summary>
        private void Refresh()
        {
            try
            {
                Car = DrivenCar.Of(_gameState, _catalogRepository);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not tell of the car driven");
                Car = null;
            }

            try
            {
                Progress = LobbyProgress.Of(_gameState, Rules.RuleBook.Current.Unlocks, ChallengeBook.Installed);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not tell how far the career has got");
                Progress = null;
            }

            BuildTiles();
            OnPropertyChanged(nameof(Car));
            OnPropertyChanged(nameof(HasCar));
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(HasProgress));
            Film();
        }

        /// <summary>
        /// The car driven in the garage's room, for the showcase: the room lit as its scene.json has it, the camera kept
        /// inside its walls (a round room's radius, else its nearest wall, else the main screen's smallest room)
        /// </summary>
        private void Film()
        {
            ShowcaseCar? car = null;
            ShowcaseScene? scene = null;
            try
            {
                if (Car is { } driven && Directory.Exists(driven.Directory))
                    car = new ShowcaseCar(driven.DefinitionId, driven.Directory, driven.Title,
                        string.IsNullOrEmpty(driven.SkinId) ? [] : [driven.SkinId]);

                var kn5 = AppSettings.Instance.GarageShowroomKn5;
                if (car != null && !string.IsNullOrEmpty(kn5) && File.Exists(kn5))
                {
                    var room = GarageRooms.For(AppSettings.Instance.GarageId);
                    scene = new ShowcaseScene(AppSettings.Instance.GarageId, kn5, WallRadiusOf(room), room);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not set the car up to be filmed");
            }

            // The room before the car: the showcase starts once both are there
            ShowcaseScenes = scene == null ? [] : [scene];
            ShowcaseCars = car == null || scene == null ? [] : [car];
        }

        /// <summary>The main screen's smallest room, for a garage that says nothing of its walls</summary>
        private const float DefaultWallRadius = 12f;

        /// <summary>How far the camera may go from the car's place: a round room's radius, else its nearest wall</summary>
        public static float WallRadiusOf(GarageRoom room)
        {
            if (room.Walls is not { } walls) return DefaultWallRadius;
            if (walls.Radius is { } radius && radius > 0) return radius;
            var nearest = new[] { -walls.MinX, walls.MaxX, -walls.MinZ, walls.MaxZ }.Where(d => d > 0).DefaultIfEmpty(DefaultWallRadius).Min();
            return Math.Max(2f, nearest);
        }

        private void BuildTiles()
        {
            var flow = _navigationService.Flow;
            var hub = _navigationService.IdOf(this) ?? flow.FirstOfKind(ScreenKinds.Lobby);
            var menu = new List<LobbyTile>();
            var topLeft = new List<LobbyTile>();
            var topRight = new List<LobbyTile>();
            foreach (var (id, tile) in flow.TilesOf(hub))
            {
                if (CommandOf(tile) is not { } command)
                {
                    _logger.Warning("Lobby tile {Tile}: \"{To}\" is nothing a lobby does; not shown", id, tile.To);
                    continue;
                }

                var text = tile.Text is { } key ? GameText.Say(key) : null;
                var icon = tile.Icon is { } image ? Styles.ThemeImages.Icon(image, 128) : null;
                var item = new LobbyTile(id, text, NoteOf(tile), icon, command,
                    IsRadio: string.Equals(tile.To, TileActions.Radio, StringComparison.OrdinalIgnoreCase));
                switch (tile.Spot)
                {
                    case TileSpots.TopLeft: topLeft.Add(item); break;
                    case TileSpots.TopRight: topRight.Add(item); break;
                    default: menu.Add(item); break;
                }
            }

            Menu = menu;
            TopLeft = topLeft;
            TopRight = topRight;
            OnPropertyChanged(nameof(Menu));
            OnPropertyChanged(nameof(TopLeft));
            OnPropertyChanged(nameof(TopRight));
        }

        /// <summary>The line under a menu tile: how far the Challenges have got, the car the garage has out; none for the rest</summary>
        private string? NoteOf(FlowTile tile)
        {
            var kind = _navigationService.Flow.KindOf(tile.To);
            return kind switch
            {
                ScreenKinds.Challenges when Progress is { ChallengeCount: > 0 } progress =>
                    GameText.Say("lobby.note.challenges", ("done", progress.ChallengesDone), ("count", progress.ChallengeCount)),
                ScreenKinds.Garage => Car is { } car ? GameText.Say("lobby.note.car", ("car", car.Title)) : GameText.Say("lobby.note.no_car"),
                _ => null
            };
        }

        /// <summary>What a tile does: one of the lobby's own things, or a way to its screen; null for anything else</summary>
        private ICommand? CommandOf(FlowTile tile)
        {
            bool Has() => tile.Requires.All(need => need switch
            {
                TileRequirements.Car or TileRequirements.CarHere => PracticeRuns.CarOf(_gameState) is { IsImpounded: false },
                _ => true
            });

            switch (tile.To.ToLowerInvariant())
            {
                case TileActions.Quit:
                    return new RelayCommand(OnQuit, () => CanAct && Has());
                case TileActions.Radio:
                    return Audio.Radio.RadioPanelViewModel.ToggleCommand;
                case TileActions.Settings:
                    return new RelayCommand(OpenSettings, () => CanAct && Has());
                case TileActions.CarInfo or TileActions.Calendar:
                    return null;
            }

            var to = tile.To;
            return new RelayCommand(() => GoTo(to), () => CanAct && Has());
        }

        /// <summary>Nothing is on its way, and no card is over the menu</summary>
        private bool CanAct => !_going && _card == null && !_navigationService.IsLeaving(this);

        private void GoTo(string to)
        {
            if (!CanAct) return;

            if (ScreenKinds.IsOverlay(_navigationService.Flow.KindOf(to)))
            {
                _navigationService.Go(to, _gameState);
                return;
            }

            _logger.Information("From the lobby to {Screen}", to);
            _going = true;
            RelayCommand.RaiseCanExecuteChanged();
            if (!_navigationService.GoFrom(this, to, _gameState))
            {
                _going = false;
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The way out: back to the main menu (the game stays saved), or out of the game altogether</summary>
        private void OnQuit()
        {
            _dialogService.ShowDialog(new Dialogs.Quit.QuitDialogViewModel(
                _dialogService,
                choice =>
                {
                    switch (choice)
                    {
                        case Dialogs.Quit.QuitChoice.MainMenu when CanAct:
                            _logger.Information("From the lobby back to the main menu");
                            _navigationService.Leave(this, "quit", null);
                            break;
                        case Dialogs.Quit.QuitChoice.Desktop when CanAct:
                            System.Windows.Application.Current.Shutdown();
                            break;
                    }
                }));
        }

        /// <summary>The Settings card over the menu, as the main screen opens it, but with the game in play under it</summary>
        private void OpenSettings()
        {
            if (!CanAct) return;
            CurrentCard = _navigationService.SafeOpenCard<BaseScreenViewModel>("settings",
                () => _navigationService.CreateSettingsCard(CloseCard, duringGame: true));
        }

        /// <summary>Escape: a card that steps back itself (Settings' credits) does, else it closes</summary>
        private void StepBackCard()
        {
            if (_card is IMenuCard card) card.StepBack();
            else CloseCard();
        }

        /// <summary>The card's Back: it closes, and what the settings changed (the units, the words) shows in the panel</summary>
        private void CloseCard()
        {
            if (_card is not { } card) return;
            CurrentCard = null;
            try
            {
                card.Exit();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "The settings card could not be left cleanly");
            }

            Refresh();
        }
    }

    /// <summary>A tile of the lobby: its words, the line under it, its picture and what it does</summary>
    public sealed record LobbyTile(string Id, string? Text, string? Note, ImageSource? Icon, ICommand Command, bool IsRadio = false)
    {
        public bool HasNote => !string.IsNullOrEmpty(Note);
        public bool HasIcon => Icon != null;
    }

    /// <summary>How far a career of runs for the score has got: what is banked, the next tier, the Challenges finished</summary>
    public sealed record LobbyProgress(string Banked, string? NextTier, int ChallengesDone, int ChallengeCount, string? Challenges)
    {
        public bool HasNextTier => !string.IsNullOrEmpty(NextTier);
        public bool HasChallenges => !string.IsNullOrEmpty(Challenges);

        /// <summary>The career as it stands; null for a game with no Challenges and no tiers to open (nothing to tell)</summary>
        public static LobbyProgress? Of(GameState gameState, Rules.UnlockRules rules, ChallengeBook challenges)
        {
            var all = challenges.All;
            if (all.Count == 0 && rules.Tiers.Length == 0) return null;

            var career = gameState.Career;
            var score = ChallengeCareer.CareerScore(career);
            var banked = GameText.Say("lobby.banked", ("score", score.ToString("N0", Locale.Culture)));

            string? next = null;
            if (rules.Tiers.Length > 0)
            {
                var open = ChallengeCareer.OpenTier(score, rules);
                next = open >= rules.Tiers.Length
                    ? GameText.Say("lobby.tiers.all")
                    : GameText.Say("lobby.tiers.next", ("tier", open + 1),
                        ("short", Math.Max(0, (ChallengeCareer.TierScore(open + 1, rules) ?? 0) - score).ToString("N0", Locale.Culture)));
            }

            var done = all.Count(c => ChallengeCareer.IsFinished(career, c.Id));
            var line = all.Count == 0 ? null : GameText.Say("lobby.challenges", ("done", done), ("count", all.Count));
            return new LobbyProgress(banked, next, done, all.Count, line);
        }
    }
}
