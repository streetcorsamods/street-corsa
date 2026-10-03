using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.NewGame
{
    public class NewGameScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly Action _back;
        private readonly DialogService _dialogService;
        private readonly IGameStateRepository _repository;
        private readonly IRaceEventService _raceEventService;
        private readonly Action<GameState?> _setCurrentGame;
        private readonly Action<GameState>? _firstMarket;
        private readonly IAppLogger _logger;
        private string _playerName = string.Empty;
        private string _errorMessage = string.Empty;

        public string PlayerName
        {
            get => _playerName;
            set
            {
                if (SetProperty(ref _playerName, value))
                {
                    ErrorMessage = string.Empty;
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        /// <summary>The difficulty the career starts on</summary>
        public GameRulesEditor Rules { get; } = new();

        public RelayCommand StartGameCommand { get; }
        public RelayCommand BackCommand { get; }

        /// <summary>A save name the catalog uses for itself; a player with that name gets a different file</summary>
        private const string ReservedSaveName = "catalog";

        /// <param name="setCurrentGame">Makes the new game the one the app saves on exit and races with</param>
        /// <param name="back">Closes the card, back to the main screen's menu</param>
        /// <param name="firstMarket">Starts making the new game's first classifieds in the background (<see cref="Services.Market.FirstMarket"/>)</param>
        public NewGameScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IGameStateRepository repository,
            IRaceEventService raceEventService,
            Action<GameState?> setCurrentGame,
            Action back,
            Action<GameState>? firstMarket = null)
        {
            _navigationService = navigationService;
            _back = back;
            _dialogService = dialogService;
            _repository = repository;
            _raceEventService = raceEventService;
            _setCurrentGame = setCurrentGame;
            _firstMarket = firstMarket;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

            StartGameCommand = new RelayCommand(OnStartGame);
            BackCommand = new RelayCommand(OnBack);
        }

        private void OnStartGame()
        {
            var trimmedName = PlayerName.Trim();

            // Validate player name
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                ErrorMessage = GameText.Say("newgame.no_name");
                return;
            }

            if (trimmedName.Length > 50)
            {
                ErrorMessage = GameText.Say("newgame.name_too_long");
                return;
            }

            CreateNewGame(trimmedName);
        }

        private void CreateNewGame(string playerName)
        {
            // Use player name as save file name (sanitize for filename)
            var saveName = SaveNameFor(playerName);

            bool exists;
            try
            {
                exists = _repository.Exists(saveName);
            }
            catch (Exception ex)
            {
                ReportFailure(ex, saveName);
                return;
            }

            if (!exists)
            {
                StartGame(saveName, playerName);
                return;
            }

            // Show confirmation dialog
            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                GameText.Say("newgame.overwrite", ("save", saveName)),
                GameText.Say("newgame.overwrite.title"),
                confirmed =>
                {
                    // Create new save, overwriting the existing one
                    if (confirmed) StartGame(saveName, playerName);
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        /// <summary>
        /// Creates the save, its first race events, and goes to the garage. The save is a file write: a locked
        /// or read-only saves folder is told to the player, and they stay on this screen.
        /// </summary>
        private void StartGame(string saveName, string playerName)
        {
            GameState gameState;
            try
            {
                gameState = _repository.CreateNew(saveName, playerName, Rules.Build());

                // Generate initial race events, for the reputation the player starts with
                gameState.Career.SyncStanding(gameState.Player);
                _raceEventService.GenerateEvents(gameState.Career, gameState.Date, gameState.Rules.PinkSlipFactor);

                // Saved again with them: a game that ends before its first save would otherwise start with no invitations
                _repository.Save(gameState, saveName);
            }
            catch (Exception ex)
            {
                ReportFailure(ex, saveName);
                return;
            }

            // Navigate to game screen, then store the game in App for saving on exit: only a garage that opened makes it
            // the game the app saves and races with
            if (!_navigationService.GoHome(gameState)) return;
            _setCurrentGame(gameState);

            // The lots and the paper's ads, made now so they are there from the first day
            _firstMarket?.Invoke(gameState);
        }

        private void ReportFailure(Exception ex, string saveName)
        {
            _logger.Error(ex, "Could not create save {SaveName}", saveName);
            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                GameText.Say("newgame.failed", ("error", ex.Message)),
                GameText.Say("newgame.failed.title")));
        }

        /// <summary>
        /// The player's name as a save file name: one safe file name (<see cref="PathNames.Sanitize"/>), never the
        /// name the catalog database uses.
        /// </summary>
        public static string SaveNameFor(string playerName)
        {
            var saveName = PathNames.Sanitize(playerName, maxLength: 50, fallback: "player");
            return string.Equals(saveName, ReservedSaveName, StringComparison.OrdinalIgnoreCase)
                ? saveName + "_player"
                : saveName;
        }

        private void OnBack()
        {
            _back();
        }

        public override void Enter()
        {
            base.Enter();
        }

        public override void Exit()
        {
            base.Exit();
        }
    }
}
