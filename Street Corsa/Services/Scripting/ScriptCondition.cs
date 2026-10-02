using StreetCorsa.Services.Street;

namespace StreetCorsa.Services.Scripting
{
    /// <summary>What a script is told about the moment it plays in</summary>
    /// <param name="Time">The game's clock; null when the screen does not keep one</param>
    /// <param name="Light">The light of the day; null when the screen does not say</param>
    /// <param name="Towed">The car came on the tow truck</param>
    public sealed record ScriptFacts(DateTime? Time = null, StreetLight? Light = null, bool Towed = false)
    {
        public static readonly ScriptFacts None = new();
    }

    /// <summary>What a car's engine is, for a condition</summary>
    public enum EngineIs
    {
        /// <summary>It would run: the car has an engine that starts</summary>
        Runs,

        /// <summary>It would not: no engine, or one that does not start</summary>
        Dead,

        /// <summary>It is running now</summary>
        Running,

        /// <summary>It is not running now</summary>
        Off
    }

    /// <summary>
    /// A condition (<c>"when"</c> on a script, <c>"if"</c> on a step): every key it has must hold. <c>engine</c>
    /// (runs, dead, running, off), <c>towed</c> (true, false), <c>light</c> (day, dusk, night, or a list of them),
    /// <c>hours</c> ([from, to), on the game's clock, round midnight when from is the later), <c>not</c> (a condition
    /// that must not hold), <c>any</c> (a list of conditions, one of which must). A fact the screen does not know holds
    /// for no condition on it.
    /// </summary>
    public sealed record ScriptCondition
    {
        public EngineIs? Engine { get; init; }

        public bool? Towed { get; init; }

        public IReadOnlySet<StreetLight>? Light { get; init; }

        public (int From, int To)? Hours { get; init; }

        public ScriptCondition? Not { get; init; }

        public IReadOnlyList<ScriptCondition>? Any { get; init; }

        /// <summary>The condition that always holds</summary>
        public static readonly ScriptCondition Always = new();

        /// <param name="engine">The engine of the car the condition is about; null when it has none</param>
        public bool Holds(ScriptFacts facts, IScriptEngine? engine)
        {
            if (Engine is { } wanted && !EngineHolds(wanted, engine)) return false;
            if (Towed is { } towed && facts.Towed != towed) return false;
            if (Light is { } lights && (facts.Light is not { } light || !lights.Contains(light))) return false;
            if (Hours is { } hours && (facts.Time is not { } time || !Within(time.Hour, hours.From, hours.To))) return false;
            if (Not != null && Not.Holds(facts, engine)) return false;
            if (Any != null && !Any.Any(c => c.Holds(facts, engine))) return false;
            return true;
        }

        private static bool EngineHolds(EngineIs wanted, IScriptEngine? engine) => wanted switch
        {
            EngineIs.Runs => engine?.Runs == true,
            EngineIs.Dead => engine?.Runs != true,
            EngineIs.Running => engine?.IsRunning == true,
            _ => engine?.IsRunning != true
        };

        /// <summary>An hour within [from, to), round midnight when from is the later</summary>
        private static bool Within(int hour, int from, int to) => from <= to ? hour >= from && hour < to : hour >= from || hour < to;
    }
}
