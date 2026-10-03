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

        /// <summary>The base game's King's racer id</summary>
        public const string KingId = "king";

        /// <summary>
        /// The path a boss's mod names, as the game keeps it (<see cref="ModIds.Resolve"/>): one written in full is
        /// that one, otherwise the mod's own path when it has one, else the base game's
        /// </summary>
        public static string ResolvePath(string modId, string? path, IEnumerable<VictoryDefinition> victories) =>
            string.IsNullOrWhiteSpace(path) ? string.Empty
            : ModIds.Resolve(modId, path.Trim(), id => victories.Any(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));

        /// <summary>The path <paramref name="pathId"/> is among <paramref name="victories"/></summary>
        public static VictoryDefinition? FindPath(string pathId, IEnumerable<VictoryDefinition> victories) =>
            pathId.Length == 0 ? null : victories.FirstOrDefault(v => v.Id.Equals(pathId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether the mod a boss comes from is installed (the base game's always is)</summary>
        public static bool ModInstalled(Opponent boss) =>
            boss.DefinitionId.Length == 0 || ModIds.IsBase(ModIds.ModOf(boss.DefinitionId)) || ModSet.Installed.Get(ModIds.ModOf(boss.DefinitionId)) != null;

        /// <summary>
        /// The boss comes out once his path is open; with no path, from the start. One whose path no installed mod has,
        /// or whose own mod is gone, stays out of sight: beating him would win nothing.
        /// </summary>
        public static bool PathOpen(Opponent boss, CareerState career)
        {
            if (!ModInstalled(boss)) return false;
            if (boss.BossPath.Length == 0) return true;

            return FindPath(boss.BossPath, CareerContent.Installed.Victories) is { } path && new DataVictory(path).IsUnlocked(career);
        }

        /// <summary>
        /// What a boss's own text keys end in: his racer id in full ("my_mod:big_jim"), then as his mod wrote it
        /// ("big_jim")
        /// </summary>
        public static IEnumerable<string> OwnSuffixes(string bossId)
        {
            if (bossId.Length == 0) yield break;

            yield return bossId;
            if (ModIds.IsQualified(bossId)) yield return ModIds.Local(bossId);
        }

        /// <summary>
        /// The text key for a boss: his own (<c>key.&lt;racer id&gt;</c>, by <see cref="OwnSuffixes"/>) when a mod
        /// has it, else every boss's (<see cref="RacerWords.Key"/>, as for any racer)
        /// </summary>
        public static string Key(string key, Opponent boss) => RacerWords.Key(key, boss);

        /// <summary>
        /// Puts what the career needs to know of its bosses on it (<see cref="CareerState.Bosses"/>): who they are,
        /// by the name they are beaten under, and their paths. A boss whose mod is gone is no boss of any path.
        /// </summary>
        public static void Refresh(GameState gameState)
        {
            gameState.Career.Bosses = gameState.Racers.All.Concat(gameState.Racers.Departed.Values)
                .OfType<Opponent>()
                .Where(o => o.IsBoss && ModInstalled(o))
                .Select(o => new BossStanding { Id = o.DefinitionId, Name = o.Name, Path = o.BossPath })
                .ToList();
            gameState.Career.BossesKnown = true;
        }

        /// <summary>A save from before bosses: its King is the King path's boss</summary>
        public static void BringUpToDate(GameState gameState)
        {
            gameState.Racers.Departed ??= [];
            foreach (var king in gameState.Racers.All.Concat(gameState.Racers.Departed.Values)
                         .OfType<Opponent>().Where(o => o.IsKing))
            {
                king.IsBoss = true;
                if (king.BossPath.Length == 0) king.BossPath = KingPathId;
                king.IsKing = false;
            }

            Refresh(gameState);
        }
    }
}
