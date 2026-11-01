using StreetCorsa.Mods;
using StreetCorsa.Models.Career.Victory;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// Implementation of victory condition management
    /// </summary>
    public class VictoryConditionService : IVictoryConditionService
    {
        // The paths given, or those of the content in play (read again once another plays)
        private readonly Dictionary<string, IVictoryCondition>? _given;
        private readonly ContentCache<Dictionary<string, IVictoryCondition>> _inPlay = new(() => Index(CareerContent.Installed));

        private Dictionary<string, IVictoryCondition> _victoryConditions => _given ?? _inPlay.Value;
        private readonly Func<GameState, int>? _getTotalOpponents;
        private readonly Func<GameState, bool>? _playerHasMostWins;

        /// <param name="getTotalOpponents">How many racers Domination asks for; the game passes
        /// <see cref="OpponentsToDominate"/>, and nothing is worked out when null</param>
        /// <param name="playerHasMostWins">Whether the player leads on wins; the game passes
        /// <see cref="IsPlayerSeasonChampion"/>, and nothing is worked out when null</param>
        /// <param name="content">The victory paths the mods define; the content in play's when null</param>
        public VictoryConditionService(
            Func<GameState, int>? getTotalOpponents = null,
            Func<GameState, bool>? playerHasMostWins = null,
            CareerContent? content = null)
        {
            _getTotalOpponents = getTotalOpponents;
            _playerHasMostWins = playerHasMostWins;

            if (content != null) _given = Index(content);
        }

        // In the order the mods list them: with no path picked, the first reached wins
        private static Dictionary<string, IVictoryCondition> Index(CareerContent content)
        {
            var conditions = new Dictionary<string, IVictoryCondition>(StringComparer.OrdinalIgnoreCase);
            foreach (var victory in content.Victories) conditions[victory.Id] = new DataVictory(victory);
            return conditions;
        }

        public IEnumerable<IVictoryCondition> GetAllVictoryConditions()
        {
            return _victoryConditions.Values;
        }

        public IEnumerable<IVictoryCondition> GetUnlockedVictoryConditions(CareerState career)
        {
            return _victoryConditions.Values.Where(v => v.IsUnlocked(career));
        }

        public IVictoryCondition? GetVictoryCondition(string victoryType)
        {
            return _victoryConditions.TryGetValue(victoryType, out var condition) ? condition : null;
        }

        public IVictoryCondition? CheckForVictory(GameState gameState)
        {
            RefreshStanding(gameState);

            // The path the player set out on is the one that wins (in a game with no career screen, the one the
            // rules give); with none picked (or one the game no longer knows), whichever is reached first
            if (ActivePath(gameState) is { } active && _victoryConditions.TryGetValue(active, out var chosen))
            {
                return chosen.IsAchieved(gameState.Career) ? chosen : null;
            }

            return _victoryConditions.Values.FirstOrDefault(c => c.IsAchieved(gameState.Career));
        }

        /// <summary>
        /// The victory path the game is played for: the player's pick, or in a game with no career screen to pick it in
        /// (<see cref="Rules.SystemRules.Career"/> off), the rules' <see cref="Rules.SystemRules.CareerPath"/>
        /// </summary>
        public static string? ActivePath(GameState gameState)
        {
            var systems = Rules.RuleBook.Current.Systems;
            return gameState.Career.ActiveVictoryType ?? (Navigation.FlowBook.InPlay("career") || string.IsNullOrWhiteSpace(systems.CareerPath) ? null : systems.CareerPath);
        }

        public IVictoryCondition? ClaimVictory(GameState gameState)
        {
            if (gameState.Career.HasWonGame)
            {
                // Won already: the Career screen still shows where the player stands
                RefreshStanding(gameState);
                return null;
            }

            if (CheckForVictory(gameState) is not { } victory)
                return null;

            gameState.Career.HasWonGame = true;
            gameState.Career.WinningVictoryType = victory.VictoryType;
            return victory;
        }

        public VictoryProgress GetProgress(string victoryType, CareerState career)
        {
            if (_victoryConditions.TryGetValue(victoryType, out var condition))
            {
                return condition.GetProgress(career);
            }

            return VictoryProgress.NotStarted(GameText.Say("career.victory.unknown"));
        }

        public Dictionary<string, VictoryProgress> GetAllProgress(CareerState career)
        {
            var progress = new Dictionary<string, VictoryProgress>();

            foreach (var (type, condition) in _victoryConditions)
            {
                progress[type] = condition.GetProgress(career);
            }

            return progress;
        }

        public List<IVictoryCondition> CheckForNewUnlocks(CareerState career)
        {
            var newlyUnlocked = new List<IVictoryCondition>();

            foreach (var condition in _victoryConditions.Values)
            {
                if (condition.IsUnlocked(career) &&
                    !career.UnlockedVictories.ContainsKey(condition.VictoryType))
                {
                    career.UnlockedVictories[condition.VictoryType] = true;
                    newlyUnlocked.Add(condition);
                }
            }

            return newlyUnlocked;
        }

        /// <summary>
        /// The figures the victories need from the whole game (how many racers, who has the most wins, the bosses),
        /// put on the career where every victory reads them
        /// </summary>
        public void RefreshStanding(GameState gameState)
        {
            var career = gameState.Career;
            if (_getTotalOpponents != null) career.TotalOpponents = _getTotalOpponents(gameState);
            if (_playerHasMostWins != null) career.PlayerHasMostWins = _playerHasMostWins(gameState);

            Opponents.Bosses.Refresh(gameState);
        }

        /// <summary>
        /// How many racers Total Domination asks the player to have beaten: everybody on the scene, and those who left
        /// after the player beat them (they stay among the beaten, so they stay in the count). One who left unbeaten
        /// is not asked for until they come back. A boss is not asked for while he keeps out of sight (his path is
        /// not open), unless the player has beaten him already.
        /// </summary>
        public static int OpponentsToDominate(GameState gameState) =>
            gameState.Racers.TotalCount + gameState.Racers.Departed.Keys.Count(gameState.Career.DefeatedOpponentIds.Contains)
            - gameState.Racers.All.OfType<Opponent>().Count(o => o.IsBoss && !gameState.Career.DefeatedOpponentIds.Contains(o.Name)
                                                                 && !Opponents.Bosses.PathOpen(o, gameState.Career));

        /// <summary>
        /// Check if the player has the most wins for Season Champion victory
        /// </summary>
        public static bool IsPlayerSeasonChampion(GameState gameState)
        {
            var playerWins = gameState.Player.Stats.Wins;

            // Every racer of the street, the bosses aside (his record is from before the season); those who left
            // town too, or the champion would be whoever is left the day the best of them is away
            return gameState.Racers.ReadyToRace.Values
                .Concat(gameState.Racers.Inactive.Values)
                .Concat(gameState.Racers.Retired.Values)
                .Concat(gameState.Racers.Departed.Values)
                .Where(racer => racer is not Opponent { IsBoss: true })
                .All(racer => racer.Stats.Wins <= playerWins);
        }
    }
}
