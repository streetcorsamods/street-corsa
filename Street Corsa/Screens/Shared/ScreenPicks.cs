using System.Runtime.CompilerServices;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// What the player last picked on the screens built again on every visit (the Challenge, the road and its search), for
    /// the game played while the program runs: back from a race or from the garage with another car, the screen opens on
    /// it again. Never saved: a game loaded again starts afresh.
    /// </summary>
    public sealed class ScreenPicks
    {
        private static readonly ConditionalWeakTable<GameState, ScreenPicks> ByGame = new();

        /// <summary>The picks kept for this game</summary>
        public static ScreenPicks Of(GameState gameState) => ByGame.GetValue(gameState, _ => new ScreenPicks());

        /// <summary>The Challenge picked on the Challenges (its id)</summary>
        public string? Challenge { get; set; }

        /// <summary>The road picked on the roads (<see cref="PracticeTrack.Key"/>)</summary>
        public string? Road { get; set; }

        /// <summary>The search typed over the roads</summary>
        public string? RoadSearch { get; set; }
    }
}
