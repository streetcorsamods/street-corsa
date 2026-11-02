using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Controls.Showcase;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Navigation;
using StreetCorsa.Services;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Showcase;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.MainMenu
{
    /// <summary>What the main screen shows over its cars: the menu, or one of these</summary>
    public enum MainMenuCard
    {
        None,
        NewGame,
        LoadGame,
        Mods,
        Settings
    }

    /// <summary>A button of the main menu, as the flow's card has it: what it says, which card it opens</summary>
    /// <param name="Card">The card's kind (<see cref="MenuCards"/>), which the view gives its picture</param>
    public sealed record MainMenuButton(string Card, string Text, RelayCommand Command);

    /// <summary>
    /// The main screen. Installed cars stand in the showrooms behind it, one at a time; the menu is laid over them, and
    /// New Game, Load Game and Settings open as cards in its place rather than as screens of their own. Only starting
    /// or loading a game leaves.
    /// </summary>
    public class MainMenuScreenViewModel : BaseScreenViewModel
    {
        /// <summary>Outside any game: no money, date or time over it</summary>
        public override bool InGame => false;

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly IContentCatalogRepository _catalog;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("MainMenu");

        private BaseScreenViewModel? _currentCard;
        private IReadOnlyList<ShowcaseCar> _showcaseCars = [];
        private IReadOnlyList<ShowcaseScene> _showcaseScenes = [];
        private bool _left;
        private bool _nothingToShow;
        private string? _cspProblem;

        /// <summary>The card that was open when the screen was left, opened again if the player is sent back</summary>
        private MainMenuCard _cardOnExit;
        private MainMenuCard _openCard;

        /// <summary>A card asked for before the screen was entered</summary>
        private MainMenuCard _pendingCard;

        /// <summary>The menu's buttons from the left, as the flow's cards have them (flow/*.json, "cards")</summary>
        public IReadOnlyList<MainMenuButton> Buttons { get; }

        public RelayCommand ExitCommand { get; }

        /// <summary>Install mods from zips on the PC (<see cref="ModInstallFlow"/>): the Mod Manager's, and New Game's with no setting</summary>
        public ModInstallFlow ModInstall { get; }

        /// <summary>
        /// Why races will not run on the Assetto Corsa folder's Custom Shaders Patch (missing, or older than
        /// <see cref="CspVersion.MinimumName"/>), for the player; null when they will or it cannot be told
        /// </summary>
        public string? CspProblem
        {
            get => _cspProblem;
            private set
            {
                if (SetProperty(ref _cspProblem, value)) OnPropertyChanged(nameof(HasCspProblem));
            }
        }

        public bool HasCspProblem => _cspProblem != null;

        /// <summary>The warning's button: the problem and how to fix it</summary>
        public RelayCommand ShowCspProblemCommand { get; }

        /// <summary>Esc: back to the menu from whichever card is open</summary>
        public RelayCommand CloseCardCommand { get; }

        /// <summary>The game's title covers the screen when it opens, while the showroom loads behind it</summary>
        public bool ShowIntro { get; }

        /// <param name="openCard">A card to open straight away</param>
        /// <param name="showIntro">The game is starting: the title first</param>
        public MainMenuScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IContentCatalogRepository catalog,
            MainMenuCard openCard = MainMenuCard.None,
            bool showIntro = false)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _catalog = catalog;
            ShowIntro = showIntro;

            Buttons = navigationService.Flow.CardsInOrder()
                .Select(c => new MainMenuButton(
                    c.Card.Card,
                    GameText.Say(c.Card.Text ?? "menu." + c.Card.Card),
                    new RelayCommand(() => Open(CardOf(c.Card.Card)))))
                .ToList();
            ExitCommand = new RelayCommand(OnExit);
            ShowCspProblemCommand = new RelayCommand(ShowCspProblem);
            ModInstall = new ModInstallFlow(dialogService, OnModsInstalled);
            // Not while a dialog is up: Esc belongs to the dialog then
            CloseCardCommand = new RelayCommand(CloseCard, () => _currentCard != null && !_dialogService.IsDialogOpen);

            _pendingCard = openCard;
        }

        private static MainMenuCard CardOf(string card) => card switch
        {
            MenuCards.NewGame => MainMenuCard.NewGame,
            MenuCards.LoadGame => MainMenuCard.LoadGame,
            MenuCards.Settings => MainMenuCard.Settings,
            MenuCards.Mods => MainMenuCard.Mods,
            _ => MainMenuCard.None
        };

        /// <summary>The card open over the cars, or null while the menu shows</summary>
        public BaseScreenViewModel? CurrentCard
        {
            get => _currentCard;
            private set => SetProperty(ref _currentCard, value);
        }

        /// <summary>The installed cars of the catalog, for the showroom behind the menu</summary>
        public IReadOnlyList<ShowcaseCar> ShowcaseCars
        {
            get => _showcaseCars;
            private set => SetProperty(ref _showcaseCars, value);
        }

        /// <summary>
        /// The showroom has been looked for and there is nothing for it: no cars or no rooms (no Assetto Corsa folder,
        /// say). The screen shows its old picture then.
        /// </summary>
        public bool NothingToShow
        {
            get => _nothingToShow;
            private set => SetProperty(ref _nothingToShow, value);
        }

        /// <summary>The showrooms they stand in</summary>
        public IReadOnlyList<ShowcaseScene> ShowcaseScenes
        {
            get => _showcaseScenes;
            private set => SetProperty(ref _showcaseScenes, value);
        }

        public override void Enter()
        {
            base.Enter();

            if (_pendingCard != MainMenuCard.None)
            {
                Open(_pendingCard);
                _pendingCard = MainMenuCard.None;
            }

            CheckCsp();
            _ = LoadShowcaseAsync();
        }

        public override void Exit()
        {
            _left = true;
            _cardOnExit = _openCard;
            CloseCard();
            base.Exit();
        }

        /// <summary>
        /// Back after starting or loading a game failed: the card the player was using opens again, and the showroom
        /// is looked for again if leaving cut that short.
        /// </summary>
        public override void Resume()
        {
            base.Resume();
            _left = false;

            if (_cardOnExit != MainMenuCard.None)
            {
                Open(_cardOnExit);
                _cardOnExit = MainMenuCard.None;
            }

            CheckCsp();
            if (_showcaseCars.Count == 0 && !NothingToShow) _ = LoadShowcaseAsync();
        }

        /// <summary>
        /// Reads the Custom Shaders Patch of the Assetto Corsa folder (a few hundred bytes) and tells whether races run on
        /// it. No AC folder is no CSP problem: the game asks for the folder itself.
        /// </summary>
        private void CheckCsp() => CspProblem = CspProblemOf(AppSettings.Instance.AssettoCorsaPath);

        /// <summary>Why races will not run on the CSP in <paramref name="acRoot"/>; null when they will or it cannot be told. Never throws.</summary>
        public static string? CspProblemOf(string? acRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(acRoot) || !AppSettings.IsInstall(acRoot)) return null;
                var csp = CspVersion.Check(acRoot);
                if (csp.Missing) return GameText.Say("menu.csp.missing", ("minimum", CspVersion.MinimumName));
                return csp.TooOld
                    ? GameText.Say("menu.csp.old", ("version", csp.Version!.Name), ("minimum", CspVersion.MinimumName))
                    : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The CSP read again first: one updated meanwhile has nothing to tell</summary>
        private void ShowCspProblem()
        {
            CheckCsp();
            if (CspProblem is not { } problem) return;
            _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(_dialogService, problem, GameText.Say("menu.csp.title")));
        }

        /// <summary>Mods installed: the Mod Manager lists them; a setting installed while New Game is open opens it again, to list it</summary>
        private void OnModsInstalled(bool settingInstalled)
        {
            if (_left) return;
            if (CurrentCard is ModManager.ModManagerScreenViewModel manager) manager.Refresh();
            else if (settingInstalled && _openCard == MainMenuCard.NewGame) Open(MainMenuCard.NewGame);
        }

        /// <summary>
        /// Reads what the showroom behind the menu has to show. Off the UI thread: the catalog and the cars folder are
        /// read from disk. Nothing found leaves the lists empty, and the screen shows its picture.
        /// </summary>
        private async Task LoadShowcaseAsync()
        {
            try
            {
                var settings = AppSettings.Instance;
                var (cars, scenes) = await Task.Run(() =>
                {
                    var installed = InstalledCars.Only(_catalog.GetCarsByStatus(ContentStatus.Active), out _);
                    return (
                        ShowcaseContent.Cars(installed, settings.CarsPath),
                        ShowcaseContent.LoadScenes(Mods.ModSet.Installed, settings.ShowroomsPath));
                });

                if (_left) return;

                _logger.Information("Main screen shows {Cars} cars in {Scenes} scenes", cars.Count, scenes.Count);
                ShowcaseScenes = scenes;
                ShowcaseCars = cars;
                NothingToShow = cars.Count == 0 || scenes.Count == 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not find the cars for the main screen; it shows its picture");
                NothingToShow = true;
            }
        }

        private void Open(MainMenuCard card)
        {
            CloseCard();

            BaseScreenViewModel? opened = card switch
            {
                MainMenuCard.NewGame => _navigationService.SafeOpenCard<BaseScreenViewModel>("new game screen", () => _navigationService.CreateNewGameCard(CloseCard, ModInstall.Command)),
                MainMenuCard.LoadGame => _navigationService.SafeOpenCard<BaseScreenViewModel>("saved games", () => _navigationService.CreateLoadGameCard(CloseCard)),
                MainMenuCard.Settings => _navigationService.SafeOpenCard<BaseScreenViewModel>("settings", () => _navigationService.CreateSettingsCard(CloseCard)),
                MainMenuCard.Mods => _navigationService.SafeOpenCard<BaseScreenViewModel>("mod manager", () => _navigationService.CreateModManagerCard(ModInstall, CloseCard)),
                _ => null
            };

            if (opened == null) return;

            _openCard = card;
            CurrentCard = opened;
        }

        private void CloseCard()
        {
            var card = _currentCard;
            if (card == null) return;

            CurrentCard = null;
            _openCard = MainMenuCard.None;
            // Settings may have changed the Assetto Corsa folder
            if (!_left) CheckCsp();
            try
            {
                card.Exit();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "A main menu card did not close cleanly");
            }
        }

        private void OnExit()
        {
            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                GameText.Say("menu.exit.message"),
                GameText.Say("menu.exit.title"),
                confirmed =>
                {
                    if (confirmed)
                    {
                        System.Windows.Application.Current.Shutdown();
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
        }
    }
}
