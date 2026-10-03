using StreetCorsa.Models.Career.Victory;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// The bosses: racers at the top of a victory path (<c>"boss"</c> in the racers' data; the base game's one is the
    /// King). A boss only races for pink slips, comes out once his path is open, never races where nobody watches and
    /// never leaves town. A path's <c>beatBoss</c> step is won by beating its bosses.
    /// </summary>
    public static class Bosses
    {
        /// <summary>The path the King was at before bosses, and the base game's King's still</summary>
        public const string KingPathId = "King";

        /// <summary>
        /// A path or racer id a mod names, as the game keeps it: one written in full is that one, otherwise the mod's own
        /// (<see cref="Means"/> falls back to the base game's when the mod has none)
        /// </summary>
        public static string Qualify(string modId, string? path) =>
            string.IsNullOrWhiteSpace(path) ? string.Empty
            : ModIds.IsQualified(path) ? ModIds.Normalize(path.Trim())
            : ModIds.Qualify(modId, path.Trim());

        /// <summary>Whether <paramref name="id"/> names what <paramref name="wanted"/> means: itself, or the base game's of the same name</summary>
        public static bool Means(string wanted, string id) =>
            id.Equals(wanted, StringComparison.OrdinalIgnoreCase)
            || (!ModIds.IsQualified(id) && ModIds.IsQualified(wanted) && id.Equals(ModIds.Local(wanted), StringComparison.OrdinalIgnoreCase));

        /// <summary>The path <paramref name="pathId"/> means among <paramref name="victories"/>: its own id first, then the base game's</summary>
        public static VictoryDefinition? FindPath(string pathId, IEnumerable<VictoryDefinition> victories)
        {
            if (pathId.Length == 0) return null;

            var list = victories as IReadOnlyCollection<VictoryDefinition> ?? victories.ToList();
            return list.FirstOrDefault(v => v.Id.Equals(pathId, StringComparison.OrdinalIgnoreCase))
                   ?? list.FirstOrDefault(v => Means(pathId, v.Id));
        }

        /// <summary>The boss comes out once his path is open; with no path, or one no mod has, from the start</summary>
        public static bool PathOpen(Opponent boss, CareerState career) =>
            FindPath(boss.BossPath, CareerContent.Installed.Victories) is not { } path || new DataVictory(path).IsUnlocked(career);

        /// <summary>
        /// The text key for a boss: his own (<c>key.&lt;racer id&gt;</c>, "talk.pulled_up.king.my_mod:big_jim") when
        /// a mod has it, else every boss's
        /// </summary>
        public static string Key(string key, Opponent boss)
        {
            if (boss.DefinitionId.Length == 0) return key;

            var own = key + "." + boss.DefinitionId;
            return GameText.Installed.Has(own) ? own : key;
        }

        /// <summary>
        /// Puts what the career needs to know of its bosses on it (<see cref="CareerState.Bosses"/>): who they are,
        /// by the name they are beaten under, and their paths
        /// </summary>
        public static void Refresh(GameState gameState)
        {
            var bosses = gameState.Racers.All.Concat(gameState.Racers.Departed.Values)
                .OfType<Opponent>()
                .Where(o => o.IsBoss)
                .Select(o => new BossStanding { Id = o.DefinitionId, Name = o.Name, Path = o.BossPath })
                .ToList();
            if (bosses.Count > 0 || gameState.Career.Bosses.Count > 0) gameState.Career.Bosses = bosses;
        }

        /// <summary>A save from before bosses: its King is the King path's boss</summary>
        public static void BringUpToDate(GameState gameState)
        {
            foreach (var king in gameState.Racers.All.Concat(gameState.Racers.Departed?.Values ?? Enumerable.Empty<Racer>())
                         .OfType<Opponent>().Where(o => o.IsKing))
            {
                king.IsBoss = true;
                if (king.BossPath.Length == 0) king.BossPath = KingPathId;
                king.IsKing = false;
            }

            gameState.Career.Bosses ??= [];
        }
    }
}
