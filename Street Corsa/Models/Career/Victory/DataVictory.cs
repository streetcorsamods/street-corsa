using StreetCorsa.Models.Career.Milestones;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Models.Career.Victory
{
    /// <summary>What a victory path asks of the career, one step of it</summary>
    public class VictoryStep
    {
        public const string CounterType = "counter";
        public const string BeatBossType = "beatBoss";

        /// <summary>beatBoss as it was written before bosses; read as beatBoss (<c>CareerContent</c>)</summary>
        public const string BeatKingType = "beatKing";
        public const string BeatAllType = "beatAll";
        public const string MostWinsType = "mostWins";

        /// <summary>counter (a career counter at a target), beatBoss, beatAll (every racer beaten once) or mostWins</summary>
        public string Type { get; set; } = CounterType;

        /// <summary>For beatBoss: the racer id of the boss to beat, as the path's mod wrote it; none: every boss whose path this is</summary>
        public string? Boss { get; set; }

        /// <summary>For a counter: which</summary>
        public MilestoneTrigger? Counter { get; set; }

        /// <summary>For a counter: the target</summary>
        public int AtLeast { get; set; }

        /// <summary>The step once done; {target}, {current}, {remaining} are filled in</summary>
        public string Step { get; set; } = string.Empty;

        /// <summary>The step while still to do; the done text when left out</summary>
        public string? Todo { get; set; }
    }

    /// <summary>The line under the path's name, by where the career stands; counters by name ({TotalWins}), {defeated} and {total} are filled in</summary>
    public class VictoryTexts
    {
        public string Achieved { get; set; } = string.Empty;
        public string Unlocked { get; set; } = string.Empty;
        public string Locked { get; set; } = string.Empty;
    }

    /// <summary>A victory path as a mod's victories file has it</summary>
    public class VictoryDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>The mod the path comes from, for the boss its steps name (<see cref="VictoryStep.Boss"/>); the base game's when none</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string ModId { get; set; } = Mods.ModIds.BaseId;

        /// <summary>What opens the path; none for a path open from the start</summary>
        public List<VictoryStep> Unlock { get; set; } = [];

        /// <summary>What wins the game on it, once open</summary>
        public List<VictoryStep> Goal { get; set; } = [];

        public VictoryTexts Progress { get; set; } = new();
    }

    /// <summary>
    /// A way to win the game, from data: open once every unlock step is done, reached once every goal step is too.
    /// Its progress is the share of all its steps done (a counter counts by how far along it is).
    /// </summary>
    public class DataVictory : IVictoryCondition
    {
        /// <summary>The King's name for a career that has not looked his name up yet</summary>
        public const string DefaultKingName = "The King";

        private readonly VictoryDefinition _definition;

        public DataVictory(VictoryDefinition definition)
        {
            _definition = definition;
        }

        public string VictoryType => _definition.Id;
        public string Name => _definition.Name;
        public string Description => _definition.Description;
        public VictoryDefinition Definition => _definition;

        public bool IsUnlocked(CareerState career) => _definition.Unlock.All(s => IsDone(s, career));

        public bool IsAchieved(CareerState career) => IsUnlocked(career) && _definition.Goal.Count > 0 && _definition.Goal.All(s => IsDone(s, career));

        /// <summary>
        /// The names of the bosses a beatBoss step asks to beat: the one it names (the path's mod's own racer, else the
        /// base game's), or every boss whose path this is. A career that has not looked its bosses up yet (a save from
        /// before them) has only the King, on the King path.
        /// </summary>
        private List<string> BossNames(VictoryStep step, CareerState career)
        {
            if (!career.BossesKnown)
                return _definition.Id == Bosses.KingPathId && ModIds.IsBase(_definition.ModId) && step.Boss is null or "" or Bosses.KingId
                    ? [string.IsNullOrEmpty(career.KingName) ? DefaultKingName : career.KingName]
                    : [];

            var bosses = career.Bosses ?? [];
            if (step.Boss is { Length: > 0 } named)
            {
                var id = ModIds.Resolve(_definition.ModId, named, x => bosses.Any(b => b.Id.Equals(x, StringComparison.OrdinalIgnoreCase)));
                return bosses.Where(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Select(b => b.Name).ToList();
            }

            return bosses.Where(b => b.Path.Equals(_definition.Id, StringComparison.OrdinalIgnoreCase)).Select(b => b.Name).ToList();
        }

        public VictoryProgress GetProgress(CareerState career)
        {
            var progress = new VictoryProgress();
            var unlocked = IsUnlocked(career);

            // A goal step of a locked path is still to do, however the career stands
            var steps = _definition.Unlock.Select(s => (Step: s, Counts: true))
                .Concat(_definition.Goal.Select(s => (Step: s, Counts: unlocked))).ToList();

            // The counters once for the whole path; each step only puts its own figures over them
            var values = Values(career);
            foreach (var (step, counts) in steps)
            {
                SetStepValues(values, step, career);
                if (counts && IsDone(step, career))
                {
                    progress.CompletedSteps.Add(GameText.Fill(step.Step, values));
                }
                else
                {
                    var todo = GameText.Fill(step.Todo ?? step.Step, values);
                    progress.RemainingSteps.Add(counts ? todo : GameText.Say("career.step.locked", ("step", todo)));
                }
            }

            progress.Percentage = steps.Count == 0 ? 0f : 100f * steps.Average(s => s.Counts ? Share(s.Step, career) : 0f);
            var text = IsAchieved(career) ? _definition.Progress.Achieved : unlocked ? _definition.Progress.Unlocked : _definition.Progress.Locked;
            foreach (var key in StepKeys) values.Remove(key);
            progress.ProgressDescription = GameText.Fill(text, values);
            return progress;
        }

        private bool IsDone(VictoryStep step, CareerState career) => step.Type switch
        {
            // No boss to beat (none in the game, or the one named is not in it): never won
            VictoryStep.BeatBossType => BossNames(step, career) is { Count: > 0 } bosses && bosses.All(career.DefeatedOpponentIds.Contains),
            VictoryStep.BeatAllType => career.TotalOpponents > 0 && career.DefeatedOpponentIds.Count >= career.TotalOpponents,
            VictoryStep.MostWinsType => career.PlayerHasMostWins,
            _ => step.Counter is { } counter && career.GetCounter(counter) >= step.AtLeast
        };

        private float Share(VictoryStep step, CareerState career)
        {
            if (IsDone(step, career)) return 1f;

            return step.Type switch
            {
                VictoryStep.BeatBossType when BossNames(step, career) is { Count: > 1 } bosses =>
                    bosses.Count(career.DefeatedOpponentIds.Contains) / (float)bosses.Count,
                VictoryStep.BeatAllType => career.TotalOpponents > 0 ? Math.Min(1f, career.DefeatedOpponentIds.Count / (float)career.TotalOpponents) : 0f,
                VictoryStep.CounterType when step.Counter is { } counter && step.AtLeast > 0 =>
                    Math.Clamp(career.GetCounter(counter) / (float)step.AtLeast, 0f, 1f),
                _ => 0f
            };
        }

        /// <summary>What only a step's texts are filled with, not the path's line</summary>
        private static readonly string[] StepKeys = ["target", "current", "remaining"];

        /// <summary>What a path's texts are filled with: every counter by name, {defeated} and {total}</summary>
        private static TextValues Values(CareerState career)
        {
            var values = new TextValues
            {
                ["defeated"] = career.DefeatedOpponentIds.Count,
                ["total"] = career.TotalOpponents
            };
            foreach (var counter in Enum.GetValues<MilestoneTrigger>()) values[counter.ToString()] = career.GetCounter(counter);
            return values;
        }

        /// <summary>A step's {target}, {current} and {remaining}, over the last step's</summary>
        private void SetStepValues(TextValues values, VictoryStep step, CareerState career)
        {
            var (current, target) = step.Type switch
            {
                VictoryStep.BeatBossType when BossNames(step, career) is var bosses =>
                    (bosses.Count(career.DefeatedOpponentIds.Contains), bosses.Count),
                VictoryStep.BeatAllType => (career.DefeatedOpponentIds.Count, career.TotalOpponents),
                VictoryStep.CounterType when step.Counter is { } counter => (career.GetCounter(counter), step.AtLeast),
                _ => (0, 0)
            };
            values["target"] = target;
            values["current"] = current;
            values["remaining"] = Math.Max(0, target - current);
        }
    }
}
