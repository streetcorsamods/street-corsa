using System.Diagnostics;
using System.IO;
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
        private readonly Func<string?, Task<bool>> _play;
        private readonly Action _drop;
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

        /// <summary>
        /// The settings installed, a card each, in the order they are listed: a career is played in one, for good, its
        /// content read when it starts
        /// </summary>
        public IReadOnlyList<SettingCard> Cards { get; }

        /// <summary>No setting is installed: the core alone is no game to play, the screen says what to do</summary>
        public bool HasNoSettings => Cards.Count == 0;

        /// <summary>Where a setting goes: the game's Content folder</summary>
        public string ContentFolder => Mods.ModSet.DefaultRoot;

        /// <summary>Starts a career in the card's setting (given none: the only one, or the only one open)</summary>
        public RelayCommand<SettingCard?> StartGameCommand { get; }
        public RelayCommand BackCommand { get; }

        /// <summary>Opens the Content folder in Explorer, for the player to put a setting in</summary>
        public RelayCommand OpenContentFolderCommand { get; }

        /// <summary>Install mods from zips (the main screen's); null hides the button</summary>
        public System.Windows.Input.ICommand? InstallModsCommand { get; init; }

        public bool CanInstallMods => InstallModsCommand != null;

        /// <summary>A save name the catalog uses for itself; a player with that name gets a different file</summary>
        private const string ReservedSaveName = "catalog";

        /// <param name="setCurrentGame">Makes the new game the one the app saves on exit and races with</param>
        /// <param name="back">Closes the card, back to the main screen's menu</param>
        /// <param name="firstMarket">Starts making the new game's first classifieds in the background (<see cref="Services.Market.FirstMarket"/>)</param>
        /// <param name="play">
        /// Reads a setting's content (by id; null for the bare core) for the game to play in; false when it could not
        /// </param>
        /// <param name="drop">Gives that content up when the game is not started after all: the main screen's core again</param>
        public NewGameScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IGameStateRepository repository,
            IRaceEventService raceEventService,
            Action<GameState?> setCurrentGame,
            Action back,
            Action<GameState>? firstMarket = null,
            Func<string?, Task<bool>>? play = null,
            Action? drop = null)
        {
            _play = play ?? (_ => Task.FromResult(true));
            _drop = drop ?? (() => { });
            Cards = Mods.ModSet.Available.Settings.Select(setting => new SettingCard(setting)).ToList();
            // One setting alone: its difficulty on show from the start, as there is nothing else to look at
            if (Cards.Count == 1) Cards[0].IsConfigOpen = true;

            _navigationService = navigationService;
            _back = back;
            _dialogService = dialogService;
            _repository = repository;
            _raceEventService = raceEventService;
            _setCurrentGame = setCurrentGame;
            _firstMarket = firstMarket;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

            StartGameCommand = new RelayCommand<SettingCard?>(OnStartGame);
            BackCommand = new RelayCommand(OnBack);
            OpenContentFolderCommand = new RelayCommand(OnOpenContentFolder);
        }

        private void OnOpenContentFolder()
        {
            try
            {
                Directory.CreateDirectory(ContentFolder);
                Process.Start(new ProcessStartInfo { FileName = ContentFolder, UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                _logger.Warning(ex, "The Content folder {Folder} could not be opened", ContentFolder);
            }
        }

        private void OnStartGame(SettingCard? card)
        {
            // Return in the name: the one setting there is, or the one whose difficulty is open; else the player picks
            var open = Cards.Where(c => c.IsConfigOpen).ToList();
            card ??= Cards.Count == 1 ? Cards[0] : open.Count == 1 ? open[0] : null;
            if (card == null)
            {
                if (Cards.Count > 0) ErrorMessage = GameText.Say("newgame.pick_setting");
                return;
            }

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

            CreateNewGame(trimmedName, card);
        }

        private void CreateNewGame(string playerName, SettingCard card)
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
                StartGame(saveName, playerName, card);
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
                    if (confirmed && !_closed) StartGame(saveName, playerName, card);
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        // A start under way: the content is being read, a second Start would read it again
        private bool _starting;

        /// <summary>
        /// Reads the setting's content, creates the save, its first race events, and goes to the garage. The save is a
        /// file write: a locked or read-only saves folder is told to the player, and they stay on this screen.
        /// </summary>
        private async void StartGame(string saveName, string playerName, SettingCard card)
        {
            if (_starting) return;
            _starting = true;
            try
            {
                if (!await _play(card.Mod.Id))
                {
                    ErrorMessage = GameText.Say("newgame.setting_failed");
                    return;
                }

                // Backed out of (Back, Escape, another card) while the content was read: no save made, nothing opened
                if (_closed)
                {
                    _logger.Information("The new game {SaveName} was backed out of while its setting was read; not made", saveName);
                    _drop();
                    return;
                }

                if (!StartGameIn(saveName, playerName, card)) _drop();
            }
            catch (Exception ex)
            {
                _drop();
                ReportFailure(ex, saveName);
            }
            finally
            {
                _starting = false;
            }
        }

        /// <summary>The career made and its garage on show; false when it was not</summary>
        private bool StartGameIn(string saveName, string playerName, SettingCard card)
        {
            GameState gameState;
            try
            {
                gameState = _repository.CreateNew(saveName, playerName, card.Rules.Build());

                // Generate initial race events, for the reputation the player starts with
                gameState.Career.SyncStanding(gameState.Player);
                _raceEventService.GenerateEvents(gameState.Career, gameState.Date, gameState.Rules.PinkSlipFactor);

                // Saved again with them: a game that ends before its first save would otherwise start with no invitations
                _repository.Save(gameState, saveName);
            }
            catch (Exception ex)
            {
                ReportFailure(ex, saveName);
                return false;
            }

            // Navigate to game screen, then store the game in App for saving on exit: only a garage that opened makes it
            // the game the app saves and races with
            if (!_navigationService.GoHome(gameState)) return false;
            _setCurrentGame(gameState);

            // The lots and the paper's ads, made now so they are there from the first day
            _firstMarket?.Invoke(gameState);
            return true;
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

        /// <summary>True once the card has closed: a start still reading its content then makes nothing</summary>
        private bool _closed;

        public override void Enter()
        {
            base.Enter();
            _closed = false;
        }

        public override void Exit()
        {
            _closed = true;
            base.Exit();
        }
    }
}
