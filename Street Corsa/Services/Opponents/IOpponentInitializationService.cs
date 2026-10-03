using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// Service for initializing opponents when creating a new game
    /// </summary>
    public interface IOpponentInitializationService
    {
        /// <summary>
        /// Initialize opponents for a new game
        /// Loads opponent definitions and adds them to the game state
        /// </summary>
        /// <param name="gameState">The game state to initialize</param>
        void InitializeOpponents(GameState gameState);

        /// <summary>
        /// Puts every boss the mods define in the game when he isn't in it yet (a save from before him, a mod added
        /// since): out of sight, with his money, record and car. Once per game state.
        /// </summary>
        void EnsureBosses(GameState gameState);

        /// <summary>
        /// Racers defined since the save began (a bigger pool in a newer version) join it, not on the street yet, each
        /// with a car and money; racers who have a portrait in their definition but none on the save get it. Once per
        /// game state.
        /// </summary>
        void EnsureNewcomers(GameState gameState);

        /// <summary>What a racer starts out with in the bank; a racer coming back to town brings as much</summary>
        decimal StartingMoney(Opponent opponent);
    }
}
