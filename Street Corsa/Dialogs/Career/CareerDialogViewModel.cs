using System.Collections.ObjectModel;
using StreetCorsa.Logging;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Storage;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.Career
{
    /// <summary>The career dialog's three sections, one on show at a time</summary>
    public enum CareerSection
    {
        Victory,
        Stats,
        Milestones
    }

    /// <summary>
    /// The career, over the garage: the victory paths (one of which the player pursues), the player's record, and the
    /// milestones. One section shows at a time; the dialog opens on the one last looked at.
    /// </summary>
    public class CareerDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly IVictoryConditionService _victoryService;
        private readonly IMilestoneService _milestoneService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly Action _toMainMenu;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Career");

        /// <summary>The section the player last looked at, for the next time the dialog opens</summary>
        private static CareerSection _lastSection = CareerSection.Victory;

        public RelayCommand CloseCommand { get; }
        public RelayCommand<string> SetActiveVictoryCommand { get; }

        public override bool IsWide => true;

        // The section on show: each of the section buttons is on while its section is
        private CareerSection _section = _lastSection;
        public CareerSection Section
        {
            get => _section;
            set
            {
                if (_section == value) return;
                _section = _lastSection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowVictory));
                OnPropertyChanged(nameof(ShowStats));
                OnPropertyChanged(nameof(ShowMilestones));
            }
        }

        // Set by the section buttons: turning one on shows its section, turning it off is the other one's doing
        public bool ShowVictory
        {
            get => Section == CareerSection.Victory;
            set { if (value) Section = CareerSection.Victory; }
        }

        public bool ShowStats
        {
            get => Section == CareerSection.Stats;
            set { if (value) Section = CareerSection.Stats; }
        }

        public bool ShowMilestones
        {
            get => Section == CareerSection.Milestones;
            set { if (value) Section = CareerSection.Milestones; }
        }

        // Victory conditions
        public ObservableCollection<VictoryDisplayViewModel> Victories { get; } = [];

        // Milestones
        public ObservableCollection<MilestoneDisplayViewModel> Milestones { get; } = [];

        public int CompletedMilestoneCount => Milestones.Count(m => m.IsCompleted);
        public int TotalMilestoneCount => Milestones.Count;

        // Stats
        public int TotalWins => _gameState.Player.Stats.Wins;
        public int TotalLosses => _gameState.Player.Stats.Losses;
        public int TotalRaces => _gameState.Player.Stats.Races;
        public string WinRate => TotalRaces > 0
            ? $"{(_gameState.Player.Stats.WinRate * 100):F0}%"
            : "N/A";

        public int Reputation => _gameState.Player.Stats.Reputation;
        public string ReputationTier => _gameState.Player.Stats.GetReputationTier();

        public int CarsOwned => _gameState.Player.Cars.Count;
        public int PinkSlipsWon => _gameState.Player.Stats.PinkSlipsWon;
        public int PinkSlipsLost => _gameState.Player.Stats.PinkSlipsLost;

        public int OpponentsDefeated => _gameState.Career.DefeatedOpponentIds.Count;

        public string TotalEarnings => $"${_gameState.Player.Stats.TotalEarnings:N0}";
        public string TotalLossesAmount => $"${_gameState.Player.Stats.TotalLosses:N0}";
        public string NetEarnings => $"${_gameState.Player.Stats.NetEarnings:N0}";
        public bool IsNetLoss => _gameState.Player.Stats.NetEarnings < 0;

        // Game won state
        public bool HasWonGame => _gameState.Career.HasWonGame;
        public string? WinningVictoryName => HasWonGame && _gameState.Career.WinningVictoryType != null
            ? _victoryService.GetVictoryCondition(_gameState.Career.WinningVictoryType)?.Name
            : null;

        /// <param name="toMainMenu">Where the victory screen's Main Menu button goes, when picking a path wins the game</param>
        public CareerDialogViewModel(
            DialogService dialogService,
            IVictoryConditionService victoryService,
            IMilestoneService milestoneService,
            Models.GameState.GameState gameState,
            IGameStateRepository gameStateRepo,
            Action toMainMenu)
        {
            _dialogService = dialogService;
            _victoryService = victoryService;
            _milestoneService = milestoneService;
            _gameState = gameState;
            _gameStateRepo = gameStateRepo;
            _toMainMenu = toMainMenu;

            CloseCommand = new RelayCommand(() => _dialogService.CloseDialog());
            SetActiveVictoryCommand = new RelayCommand<string>(OnSetActiveVictory);

            // A save from before the counters followed the player starts them where the player stands
            _gameState.Career.SyncStanding(_gameState.Player);

            LoadVictories();
            LoadMilestones();
        }

        private void LoadVictories()
        {
            // As of now, not as of the last race
            _victoryService.RefreshStanding(_gameState);

            // Active first, then achieved, then unlocked, then locked
            var sorted = _victoryService.GetAllVictoryConditions()
                .Select(condition => VictoryDisplayViewModel.FromCondition(condition, _gameState.Career))
                .OrderByDescending(v => v.IsActive)
                .ThenByDescending(v => v.IsAchieved)
                .ThenByDescending(v => v.IsUnlocked)
                .ThenBy(v => v.Name)
                .ToList();

            Victories.Clear();
            foreach (var v in sorted)
            {
                Victories.Add(v);
            }
        }

        private void LoadMilestones()
        {
            // Completed first, then by progress percentage descending
            var sorted = _milestoneService.GetAllMilestones()
                .Select(milestone => MilestoneDisplayViewModel.FromMilestone(milestone, _gameState.Career))
                .OrderByDescending(m => m.IsCompleted)
                .ThenByDescending(m => m.ProgressPercentage)
                .ThenBy(m => m.Name)
                .ToList();

            Milestones.Clear();
            foreach (var m in sorted)
            {
                Milestones.Add(m);
            }

            OnPropertyChanged(nameof(CompletedMilestoneCount));
            OnPropertyChanged(nameof(TotalMilestoneCount));
        }

        private void OnSetActiveVictory(string? victoryType)
        {
            if (string.IsNullOrEmpty(victoryType))
                return;

            // Find the victory
            var victory = Victories.FirstOrDefault(v => v.VictoryType == victoryType);
            if (victory == null || !victory.CanSetActive)
                return;

            // Update game state
            _gameState.Career.ActiveVictoryType = victoryType;

            // A path already reached wins the game the moment it is picked
            var won = _victoryService.ClaimVictory(_gameState);
            if (won != null)
                _logger.Information("Player achieved victory on picking it: {VictoryType} - {VictoryName}", won.VictoryType, won.Name);

            Save();

            // Refresh display
            LoadVictories();
            OnPropertyChanged(nameof(HasWonGame));
            OnPropertyChanged(nameof(WinningVictoryName));

            if (won != null)
            {
                // The victory screen takes this dialog's place: queued behind it, it would wait for the player to close it
                _dialogService.CloseDialog();
                var message = CareerProgressService.VictoryMessage(_gameState, won, [], []);
                PlayerMessageDialogs.Show(_dialogService, message, _toMainMenu);
            }
        }

        private void Save()
        {
            try
            {
                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the game after picking a victory path");
            }
        }
    }
}
