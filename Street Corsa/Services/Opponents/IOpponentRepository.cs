using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// Repository for loading opponent definitions from storage
    /// </summary>
    public interface IOpponentRepository
    {
        /// <summary>
        /// Load all opponent definitions from the opponent definitions JSON file
        /// </summary>
        /// <returns>List of all available opponents</returns>
        List<Opponent> LoadAllOpponents();
    }
}
