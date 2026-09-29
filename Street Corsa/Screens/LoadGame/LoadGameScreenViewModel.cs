using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Storage;
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
        public LoadGameScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IGameStateRepository repository,
            ICarPartsService partsService,
            Action<GameState?> setCurrentGame,
            Action back)
        {
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
                ErrorMessage = $"Failed to load saved games: {ex.Message}";
                HasSaves = false;
            }
        }

        private string FormatLastPlayed(DateTime lastPlayed)
        {
            var timeSpan = DateTime.Now - lastPlayed;

            if (timeSpan.TotalMinutes < 1)
                return "Just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minute{((int)timeSpan.TotalMinutes != 1 ? "s" : "")} ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours != 1 ? "s" : "")} ago";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays != 1 ? "s" : "")} ago";
            if (timeSpan.TotalDays < 30)
                return $"{(int)(timeSpan.TotalDays / 7)} week{((int)(timeSpan.TotalDays / 7) != 1 ? "s" : "")} ago";

            return lastPlayed.ToString("MMM d, yyyy");
        }

        /// <summary>Reads the save off the UI thread (a whole game can take a moment), then opens it</summary>
        private async void OnLoadSave(SaveGameInfo? saveInfo)
        {
            if (saveInfo == null || IsBusy)
                return;

            // Nothing may escape an async void
            try
            {
                GameState? gameState;
                IsBusy = true;
                try
                {
                    gameState = await Task.Run(() => _repository.Load(saveInfo.SaveName));
                }
                finally
                {
                    IsBusy = false;
                }

                if (gameState == null)
                {
                    ErrorMessage = "Failed to load save game";
                    return;
                }

                // Played with mods that are not here now: the player says whether to go on without them
                var missing = ModsInSave.Missing(gameState, Mods.ModSet.Installed);
                if (missing.Count > 0)
                {
                    _logger.Warning("Save {SaveName} was played with mods that are not installed: {Mods}", saveInfo.SaveName, string.Join(", ", missing));
                    _dialogService.ShowDialog(new ConfirmationDialogViewModel(_dialogService, ModsInSave.Warning(missing), "Mods Missing",
                        confirmed =>
                        {
                            if (confirmed) Open(saveInfo, gameState);
                        }));
                    return;
                }

                Open(saveInfo, gameState);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not load save {SaveName}", saveInfo.SaveName);
                ErrorMessage = $"Failed to load game: {ex.Message}";
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

            // Nothing may escape an async void
            try
            {
                await Task.Run(() =>
                {
                    // Parts saved under ids of a pack that has been replaced since, and what no mod holds any more
                    var changed = _partsService.BringUpToDate(gameState);
                    changed |= ModsInSave.Tidy(gameState, CareerContent.Installed, Mods.ModSet.Installed);
                    if (changed) _repository.Save(gameState, saveInfo.SaveName);
                });

                // Navigate to game screen with loaded state, then store it in App for saving on exit
                if (_navigationService.NavigateToGarage(gameState)) _setCurrentGame(gameState);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not load save {SaveName}", saveInfo.SaveName);
                ErrorMessage = $"Failed to load game: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnDeleteSave(SaveGameInfo? saveInfo)
        {
            if (saveInfo == null || IsBusy)
                return;

            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                $"Are you sure you want to delete the save '{saveInfo.PlayerName}'? This action cannot be undone.",
                "Delete Save",
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
                            ErrorMessage = $"Failed to delete save: {ex.Message}";
                        }
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        private void OnBack()
        {
            _back();
        }

        public override void Enter()
        {
            base.Enter();
            // Refresh the list when entering the screen
            LoadSavedGames();
        }

        public override void Exit()
        {
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
