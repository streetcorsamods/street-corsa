using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;
using System.Collections.ObjectModel;
using StreetCorsa.Services.Career;

namespace StreetCorsa.Screens.LoadGame
{
    public class LoadGameScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly Action _back;
        private readonly DialogService _dialogService;
        private readonly IGameStateRepository _repository;
        private readonly ICarPartsService _partsService;
        private readonly Action<GameState?> _setCurrentGame;
        private readonly Func<string?, Task<bool>> _play;
        private readonly Action _drop;
        private readonly IAppLogger _logger;
        private string _errorMessage = string.Empty;
        private bool _hasSaves;

        public ObservableCollection<SaveGameInfo> SavedGames { get; }

        public bool HasSaves
        {
            get => _hasSaves;
            set => SetProperty(ref _hasSaves, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private bool _isBusy;

        /// <summary>A save is being read or brought up to date, off the UI thread: nothing else is loaded or deleted meanwhile</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value)) RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public RelayCommand<SaveGameInfo> LoadSaveCommand { get; }
        public RelayCommand<SaveGameInfo> DeleteSaveCommand { get; }
        public RelayCommand BackCommand { get; }

        /// <param name="setCurrentGame">Makes the loaded game the one the app saves on exit and races with</param>
        /// <param name="back">Closes the card, back to the main screen's menu</param>
        /// <param name="play">
        /// Reads the content of a save's setting (by id; null for the bare core) for it to be played in; false when it
        /// could not
        /// </param>
        /// <param name="drop">Gives that content up when the save is not opened after all: the main screen's core again</param>
        public LoadGameScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IGameStateRepository repository,
            ICarPartsService partsService,
            Action<GameState?> setCurrentGame,
            Action back,
            Func<string?, Task<bool>>? play = null,
            Action? drop = null)
        {
            _play = play ?? (_ => Task.FromResult(true));
            _drop = drop ?? (() => { });
            _navigationService = navigationService;
            _back = back;
            _dialogService = dialogService;
            _repository = repository;
            _partsService = partsService;
            _setCurrentGame = setCurrentGame;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

            SavedGames = new ObservableCollection<SaveGameInfo>();

            LoadSaveCommand = new RelayCommand<SaveGameInfo>(OnLoadSave, _ => !IsBusy);
            DeleteSaveCommand = new RelayCommand<SaveGameInfo>(OnDeleteSave, _ => !IsBusy);
            BackCommand = new RelayCommand(OnBack);

            // The saves are listed in Enter, once: the screen is always entered right after it is made
        }

        private void LoadSavedGames()
        {
            try
            {
                SavedGames.Clear();

                // Headers only: listing no longer loads every whole game, and never closes the save in use.
                // A save that cannot be read is logged and left off by the repository.
                foreach (var header in _repository.ListSaveHeaders())
                {
                    SavedGames.Add(new SaveGameInfo
                    {
                        SaveName = header.SaveName,
                        PlayerName = header.PlayerName,
                        LastPlayedDate = header.LastPlayedDate,
                        LastPlayedText = FormatLastPlayed(header.LastPlayedDate)
                    });
                }

                HasSaves = SavedGames.Count > 0;

                if (!HasSaves)
                {
                    ErrorMessage = string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the saved games");
                ErrorMessage = GameText.Say("loadgame.list_failed", ("error", ex.Message));
                HasSaves = false;
            }
        }

        private string FormatLastPlayed(DateTime lastPlayed)
        {
            var timeSpan = DateTime.Now - lastPlayed;

            if (timeSpan.TotalMinutes < 1)
                return GameText.Say("loadgame.played.just_now");
            if (timeSpan.TotalMinutes < 60)
                return Ago("minutes", (int)timeSpan.TotalMinutes);
            if (timeSpan.TotalHours < 24)
                return Ago("hours", (int)timeSpan.TotalHours);
            if (timeSpan.TotalDays < 7)
                return Ago("days", (int)timeSpan.TotalDays);
            if (timeSpan.TotalDays < 30)
                return Ago("weeks", (int)(timeSpan.TotalDays / 7));

            return GameText.Date(lastPlayed, "format.date.short");
        }

        private static string Ago(string unit, int count) => count == 1
            ? GameText.Say($"loadgame.played.{unit}.one", ("count", count))
            : GameText.Say($"loadgame.played.{unit}.other", ("count", count));

        /// <summary>Reads the save off the UI thread (a whole game can take a moment), then opens it</summary>
        private async void OnLoadSave(SaveGameInfo? saveInfo)
        {
            if (saveInfo == null || IsBusy)
                return;

            // Busy until the save is handed on (opened, or a question about it asked and answered): a second save picked
            // meanwhile would ask a second question
            IsBusy = true;
            var handedOn = false;

            // Nothing may escape an async void
            try
            {
                var gameState = await Task.Run(() => _repository.Load(saveInfo.SaveName));

                // Backed out of (Back, Escape, another card) while it was read: nothing is asked over the main menu
                if (_closed)
                {
                    _logger.Information("Save {SaveName} read after its card was closed; not opened", saveInfo.SaveName);
                    return;
                }

                if (gameState == null)
                {
                    ErrorMessage = GameText.Say("loadgame.read_failed");
                    return;
                }

                // Played in a setting that is gone (or cannot be played now): never opened in another one
                if (!Mods.ModSet.Available.IsPlayable(gameState.Setting))
                {
                    _logger.Warning("Save {SaveName} is played in the setting {Setting}, which cannot be played now; not opened", saveInfo.SaveName, gameState.Setting);
                    ErrorMessage = GameText.Say("loadgame.setting_missing", ("setting", gameState.Setting!));
                    return;
                }

                // The content of the setting it is played in, read before anything of the save is looked up in it
                if (!await _play(gameState.Setting))
                {
                    ErrorMessage = GameText.Say("newgame.setting_failed");
                    return;
                }

                if (_closed)
                {
                    _drop();
                    return;
                }

                // Played with mods that are not here now, or newer ones: the player says whether to go on without them
                var missing = ModsInSave.Missing(gameState, Mods.ModSet.Installed);
                var older = ModsInSave.Older(gameState, Mods.ModSet.Installed);
                if (missing.Count > 0 || older.Count > 0)
                {
                    if (missing.Count > 0)
                        _logger.Warning("Save {SaveName} was played with mods that are not installed: {Mods}", saveInfo.SaveName, string.Join(", ", missing));
                    if (older.Count > 0)
                        _logger.Warning("Save {SaveName} was played with newer mods than are installed: {Mods}", saveInfo.SaveName,
                            string.Join(", ", older.Select(m => $"{m.Saved} (now {m.Installed})")));
                    var title = GameText.Say(missing.Count > 0 ? "loadgame.mods_missing.title" : "loadgame.mods_older.title");
                    _dialogService.ShowDialog(new ConfirmationDialogViewModel(_dialogService, ModsInSave.Warning(missing, older), title,
                        confirmed =>
                        {
                            IsBusy = false;
                            if (confirmed && !_closed) Open(saveInfo, gameState);
                            else _drop();
                        }));
                    handedOn = true;
                    return;
                }

                IsBusy = false;
                handedOn = true;
                Open(saveInfo, gameState);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not load save {SaveName}", saveInfo.SaveName);
                ErrorMessage = GameText.Say("loadgame.load_failed", ("error", ex.Message));
                _drop();
            }
            finally
            {
                if (!handedOn) IsBusy = false;
            }
        }

        /// <summary>
        /// Brings the save up to date off the UI thread (the parts catalog may load for it; nothing else has the game
        /// yet), then goes to its garage. Only a garage that opened makes it the game the app saves and races with.
        /// </summary>
        private async void Open(SaveGameInfo saveInfo, GameState gameState)
        {
            if (IsBusy) return;
            IsBusy = true;

            // Everything from here on plays by the save's own rules; a save not opened after all leaves the game in play its own
            var before = StreetCorsa.Rules.RuleBook.InPlay();
            var opened = false;

            // Nothing may escape an async void
            try
            {
                StreetCorsa.Rules.RuleBook.PlayBy(gameState);

                await Task.Run(() =>
                {
                    // Parts saved under ids of a pack that has been replaced since, and what no mod holds any more
                    var changed = _partsService.BringUpToDate(gameState);
                    changed |= ModsInSave.Tidy(gameState, CareerContent.Installed, Mods.ModSet.Installed);
                    changed |= Services.Street.StreetLayouts.Prune(gameState, Services.Street.StreetScenes.Installed, Services.Diner.DinerBook.Current);
                    if (changed) _repository.Save(gameState, saveInfo.SaveName);
                });

                // Backed out of (Back, Escape, another card) while it loaded: the player no longer wants this game
                if (_closed)
                {
                    _logger.Information("Save {SaveName} loaded after its card was closed; not opened", saveInfo.SaveName);
                    return;
                }

                // Navigate to game screen with loaded state, then store it in App for saving on exit
                if (_navigationService.GoHome(gameState))
                {
                    opened = true;
                    _setCurrentGame(gameState);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not load save {SaveName}", saveInfo.SaveName);
                ErrorMessage = GameText.Say("loadgame.load_failed", ("error", ex.Message));
            }
            finally
            {
                if (!opened)
                {
                    before.Restore();
                    _drop();
                }
                IsBusy = false;
            }
        }

        private void OnDeleteSave(SaveGameInfo? saveInfo)
        {
            if (saveInfo == null || IsBusy)
                return;

            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                GameText.Say("loadgame.delete", ("player", saveInfo.PlayerName)),
                GameText.Say("loadgame.delete.title"),
                confirmed =>
                {
                    if (confirmed)
                    {
                        try
                        {
                            _repository.Delete(saveInfo.SaveName);
                            SavedGames.Remove(saveInfo);
                            HasSaves = SavedGames.Count > 0;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Could not delete save {SaveName}", saveInfo.SaveName);
                            ErrorMessage = GameText.Say("loadgame.delete_failed", ("error", ex.Message));
                        }
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        private void OnBack()
        {
            _back();
        }

        /// <summary>True once the card has closed: a save still loading then goes nowhere</summary>
        private bool _closed;

        public override void Enter()
        {
            base.Enter();
            _closed = false;
            // Refresh the list when entering the screen
            LoadSavedGames();
        }

        public override void Exit()
        {
            _closed = true;
            base.Exit();
        }
    }

    public class SaveGameInfo
    {
        public string SaveName { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public DateTime LastPlayedDate { get; set; }
        public string LastPlayedText { get; set; } = string.Empty;
    }
}
