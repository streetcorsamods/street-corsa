using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// A racer's own words: where a mod has a pool's key with his racer id after it (<c>talk.pulled_up.gino</c>, or
    /// <c>talk.pulled_up.my_mod:gino</c> from another mod), he says those; else his crew's, where a mod has the key with
    /// <c>crew.</c> and the crew's id after it (<c>talk.pulled_up.crew.road_kings</c>); otherwise what every racer says
    /// </summary>
    public static class RacerWords
    {
        /// <summary>The text key for the racer: his own (<c>key.&lt;racer id&gt;</c>, by <see cref="OwnSuffixes"/>), else his crew's (<c>key.crew.&lt;crew id&gt;</c>) when a mod has it, else <paramref name="key"/></summary>
        public static string Key(string key, Racer? racer) =>
            racer is Opponent opponent
                ? Suffixes(opponent).Select(s => key + "." + s).FirstOrDefault(GameText.Installed.Has) ?? key
                : key;

        /// <summary>What a racer's keys may end in: his own id's ways first (<see cref="OwnSuffixes"/>), then his crew's (<see cref="CrewSuffixes"/>)</summary>
        public static IEnumerable<string> Suffixes(Opponent racer) => OwnSuffixes(racer.DefinitionId).Concat(CrewSuffixes(racer.Crew));

        /// <summary>What a crew's keys end in: <c>crew.</c> and the crew's id in full, then as its mod wrote it. None for a racer riding alone.</summary>
        public static IEnumerable<string> CrewSuffixes(string? crewId)
        {
            if (string.IsNullOrEmpty(crewId)) yield break;

            yield return "crew." + crewId;
            if (ModIds.IsQualified(crewId)) yield return "crew." + ModIds.Local(crewId);
        }

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
