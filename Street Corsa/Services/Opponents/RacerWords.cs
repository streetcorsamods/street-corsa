using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// A racer's own words: where a mod has a pool's key with his racer id after it (<c>talk.pulled_up.gino</c>, or
    /// <c>talk.pulled_up.my_mod:gino</c> from another mod), he says those; otherwise what every racer says
    /// </summary>
    public static class RacerWords
    {
        /// <summary>The text key for the racer: his own (<c>key.&lt;racer id&gt;</c>, by <see cref="Bosses.OwnSuffixes"/>) when a mod has it, else <paramref name="key"/></summary>
        public static string Key(string key, Opponent racer) =>
            Bosses.OwnSuffixes(racer.DefinitionId).Select(s => key + "." + s).FirstOrDefault(GameText.Installed.Has) ?? key;
    }
}
