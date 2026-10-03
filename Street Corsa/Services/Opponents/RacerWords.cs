using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// A racer's own words: where a mod has a pool's key with his racer id after it (<c>talk.pulled_up.gino</c>, or
    /// <c>talk.pulled_up.my_mod:gino</c> from another mod), he says those; otherwise what every racer says
    /// </summary>
    public static class RacerWords
    {
        /// <summary>The text key for the racer: his own (<c>key.&lt;racer id&gt;</c>, by <see cref="OwnSuffixes"/>) when a mod has it, else <paramref name="key"/></summary>
        public static string Key(string key, Racer? racer) =>
            (racer as Opponent)?.DefinitionId is { } id
                ? OwnSuffixes(id).Select(s => key + "." + s).FirstOrDefault(GameText.Installed.Has) ?? key
                : key;

        /// <summary>
        /// What a racer's own text keys end in: his racer id in full ("my_mod:big_jim"), then as his mod wrote it
        /// ("big_jim"). None for a racer with no id (an old save can hold one).
        /// </summary>
        public static IEnumerable<string> OwnSuffixes(string? racerId)
        {
            if (string.IsNullOrEmpty(racerId)) yield break;

            yield return racerId;
            if (ModIds.IsQualified(racerId)) yield return ModIds.Local(racerId);
        }
    }
}
