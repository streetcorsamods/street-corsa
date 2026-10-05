using System.Text.Json;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Services.Scripting
{
    /// <summary>
    /// The car scripts and drive feels of every mod (a <c>scripts</c> list and a <c>feels</c> list in its scripts
    /// folder; a file may hold either, or be one script), read once. A mod's entries get its namespace; one that
    /// <c>replaces</c> the base game's takes its place. A script is read strictly: a key it does not know, a step that
    /// is no step, a feel no mod has, and the script is left out with a line in the mods' problems (one replacing the
    /// base game's leaves that one standing).
    /// </summary>
    public sealed class ScriptBook
    {
        public const string ScriptsList = "scripts";
        public const string FeelsList = "feels";

        // The feels a drive has when its script names none
        private const string DefaultRoadFeel = "road";
        private const string DefaultPullFeel = "pull_away";

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Scripts");

        private static readonly Lazy<ScriptBook> InstalledBook = new(() => Load(ModSet.Installed));

        private readonly List<CarScript> _scripts;
        private readonly Dictionary<string, DriveFeel> _feels;

        private ScriptBook(List<CarScript> scripts, Dictionary<string, DriveFeel> feels, List<string> problems)
        {
            _scripts = scripts;
            _feels = feels;
            Problems = problems;
        }

        /// <summary>The scripts of the mods next to the game, read once</summary>
        public static ScriptBook Installed => InstalledBook.Value;

        /// <summary>A book with no scripts at all: nothing moves on its own</summary>
        public static readonly ScriptBook Empty = new([], new Dictionary<string, DriveFeel>(StringComparer.OrdinalIgnoreCase), []);

        /// <summary>Every script, in the order the mods have them</summary>
        public IReadOnlyList<CarScript> Scripts => _scripts;

        public IReadOnlyDictionary<string, DriveFeel> Feels => _feels;

        public IReadOnlyList<string> Problems { get; }

        /// <summary>
        /// The script that plays at <paramref name="trigger"/> now: of those set off by it, the latest mod's whose
        /// <c>when</c> holds; null when none does (nothing moves, the game goes on at once)
        /// </summary>
        public CarScript? For(string trigger, ScriptFacts facts, IScriptEngine? engine)
        {
            for (var i = _scripts.Count - 1; i >= 0; i--)
            {
                var script = _scripts[i];
                if (script.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase) && script.When.Holds(facts, engine)) return script;
            }

            return null;
        }

        public static ScriptBook Load(ModSet mods)
        {
            var problems = new List<string>();
            var book = Load(mods, problems);
            foreach (var problem in problems) Logger.Warning("Scripts: {Problem}", problem);
            ModProblems.Add("Scripts", problems);
            Logger.Information("Loaded {Scripts} car scripts and {Feels} drive feels", book._scripts.Count, book._feels.Count);
            return book;
        }

        /// <summary>The book of <paramref name="mods"/>; what is left out is a line each in <paramref name="problems"/></summary>
        public static ScriptBook Load(ModSet mods, List<string> problems)
        {
            var feels = ModJson.Load(mods, ContentTypes.Scripts, FeelsList, "feel", problems,
                entry => ReadFeel(entry, problems), listOnly: true);
            var feelsById = new Dictionary<string, DriveFeel>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, feel, _) in feels.Entries) feelsById[id] = feel with { Id = id };

            var scripts = ModJson.Load(mods, ContentTypes.Scripts, ScriptsList, "script", problems,
                entry => new ScriptReader(entry, feels, feelsById).Read(), otherLists: [FeelsList]);
            foreach (var line in feels.Replaced.Concat(scripts.Replaced)) Logger.Information("{Line}", line);

            return new ScriptBook(scripts.Entries.Select(e => e.Entry with { Id = e.Id }).ToList(), feelsById, problems);
        }

        private static readonly Dictionary<string, DriveKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["road"] = DriveKind.Road, ["launch"] = DriveKind.Launch, ["pull"] = DriveKind.Pull, ["roll_in"] = DriveKind.RollIn
        };

        private static DriveFeel? ReadFeel(ModEntry entry, List<string> problems)
        {
            var e = entry.Element;
            var kindName = ModJson.Text(e, "kind") ?? "road";
            if (!Kinds.TryGetValue(kindName, out var kind)) throw new FormatException($"kind \"{kindName}\" is none of {string.Join(", ", Kinds.Keys)}");

            var feel = DriveFeel.Defaults(kind);
            foreach (var property in e.EnumerateObject())
            {
                var v = property.Value;
                double D() => v.ValueKind == JsonValueKind.Number && double.IsFinite(v.GetDouble()) && v.GetDouble() >= 0
                    ? v.GetDouble()
                    : throw new FormatException($"\"{property.Name}\" is not a number of 0 or more");
                float F() => (float)D();

                feel = property.Name.ToLowerInvariant() switch
                {
                    "id" or "replaces" or "kind" or "_comment" => feel,
                    "cruisespeed" => feel with { CruiseSpeed = Positive(F(), property.Name) },
                    "topspeed" => feel with { TopSpeed = Positive(F(), property.Name) },
                    "acceleration" => feel with { Acceleration = Positive(F(), property.Name) },
                    "brake" => feel with { Brake = Positive(F(), property.Name) },
                    "corneraccel" => feel with { CornerAccel = Positive(F(), property.Name) },
                    "clutchspeed" => feel with { ClutchSpeed = F() },
                    "clutchseconds" => feel with { ClutchSeconds = F() },
                    "cruisethrottle" => feel with { CruiseThrottle = Math.Min(1.0, D()) },
                    "throttle" => feel with { Throttle = Math.Min(1.0, D()) },
                    "gear" => feel with { Gear = (int)F() },
                    "shiftseconds" => feel with { ShiftSeconds = F() },
                    "rpmpermetrepersecond" => feel with { RpmPerMetrePerSecond = D() },
                    "lean" => feel with { Lean = Math.Min(2f, F()) },
                    "straightout" => feel with { StraightOut = F() },
                    "turningradius" => feel with { TurningRadius = Positive(F(), property.Name) },
                    "steergain" => feel with { SteerGain = F() },
                    "steerseconds" => feel with { SteerSeconds = F() },
                    "steerlead" => feel with { SteerLead = F() },
                    "halfwheelbase" => feel with { HalfWheelbase = Positive(F(), property.Name) },
                    "seconds" => feel with { Seconds = Math.Clamp(F(), 0.1f, 60f) },
                    "ratios" => feel with { Ratios = Ratios(v) },
                    "finaldrive" => feel with { FinalDrive = Positive(F(), property.Name) },
                    "tyreradius" => feel with { TyreRadius = Positive(F(), property.Name) },
                    _ => throw new FormatException($"\"{property.Name}\" is no number a feel has")
                };
            }

            if (feel.Gear > feel.Ratios.Count) throw new FormatException($"gear {feel.Gear} is past the {feel.Ratios.Count} gears it has");
            return feel;
        }

        private static float Positive(float value, string name) =>
            value > 0 ? value : throw new FormatException($"\"{name}\" must be more than 0");

        private static float[] Ratios(JsonElement v)
        {
            if (v.ValueKind != JsonValueKind.Array) throw new FormatException("\"ratios\" is not a list of numbers");
            var ratios = v.EnumerateArray().Select(r => r.ValueKind == JsonValueKind.Number && r.GetSingle() > 0 && float.IsFinite(r.GetSingle())
                ? r.GetSingle()
                : throw new FormatException("\"ratios\" has something that is not a number over 0")).ToArray();
            return ratios.Length > 0 ? ratios : throw new FormatException("\"ratios\" is empty");
        }

        /// <summary>Reads one script, strictly: whatever is wrong with it throws, with what and where</summary>
        private sealed class ScriptReader(ModEntry entry, ContentRegistry<DriveFeel> feels, Dictionary<string, DriveFeel> feelsById)
        {
            public CarScript? Read()
            {
                var e = entry.Element;
                Only(e, "script", "id", "replaces", "trigger", "when", "most", "steps", "_comment");
                var trigger = ModJson.Text(e, "trigger");
                if (string.IsNullOrWhiteSpace(trigger)) throw new FormatException("it has no \"trigger\"");
                if (!ModJson.TryGet(e, "steps", out var steps)) throw new FormatException("it has no \"steps\"");

                var when = ModJson.TryGet(e, "when", out var w) ? Condition(w, "when") : ScriptCondition.Always;
                var most = ModJson.TryGet(e, "most", out var m) ? Number(m, "most", 1f, 600f) : CarScript.DefaultMostSeconds;
                return new CarScript(entry.Id ?? entry.Replaces ?? "", trigger.Trim(), when, most, Steps(steps, "steps"));
            }

            private List<ScriptStep> Steps(JsonElement list, string where)
            {
                if (list.ValueKind != JsonValueKind.Array) throw new FormatException($"{where} is not a list");
                var steps = new List<ScriptStep>();
                var i = 0;
                foreach (var step in list.EnumerateArray())
                {
                    steps.Add(Step(step, $"{where}[{i}]"));
                    i++;
                }

                return steps;
            }

            private ScriptStep Step(JsonElement s, string where)
            {
                if (s.ValueKind != JsonValueKind.Object) throw new FormatException($"{where} is not a step");
                var car = ModJson.TryGet(s, "car", out var c) ? Text(c, $"{where}.car") : null;

                if (!ModJson.TryGet(s, "do", out var doValue))
                {
                    if (!ModJson.TryGet(s, "if", out var condition)) throw new FormatException($"{where} has no \"do\"");
                    Only(s, where, "if", "then", "else", "car", "_comment");
                    return new IfStep(car, Condition(condition, $"{where}.if"),
                        ModJson.TryGet(s, "then", out var then) ? Steps(then, $"{where}.then") : [],
                        ModJson.TryGet(s, "else", out var otherwise) ? Steps(otherwise, $"{where}.else") : []);
                }

                var kind = Text(doValue, $"{where}.do");
                where = $"{where} ({kind})";
                switch (kind.ToLowerInvariant())
                {
                    case "lights":
                    {
                        Only(s, where, "do", "car", "head", "brake", "_comment");
                        bool? head = ModJson.TryGet(s, "head", out var h) ? Bool(h, where) : null;
                        bool? brake = ModJson.TryGet(s, "brake", out var b) ? Bool(b, where) : null;
                        if (head == null && brake == null) throw new FormatException($"{where} says neither \"head\" nor \"brake\"");
                        return new LightsStep(car, head, brake);
                    }
                    case "engine":
                    {
                        Only(s, where, "do", "car", "on", "crank", "rev", "seconds", "wait", "most", "_comment");
                        bool? on = ModJson.TryGet(s, "on", out var o) ? Bool(o, where) : null;
                        double? rev = ModJson.TryGet(s, "rev", out var r) ? Number(r, $"{where}.rev", 0f, 1f) : null;
                        if (on == null == (rev == null)) throw new FormatException($"{where} needs \"on\" or \"rev\", one of them");
                        return new EngineStep(car, on, !ModJson.TryGet(s, "crank", out var k) || Bool(k, where), rev,
                            ModJson.TryGet(s, "seconds", out var sec) ? Number(sec, $"{where}.seconds", 0f, 30f) : 0.5f,
                            !ModJson.TryGet(s, "wait", out var wt) || Bool(wt, where),
                            ModJson.TryGet(s, "most", out var mo) ? Number(mo, $"{where}.most", 0f, 60f) : 4f);
                    }
                    case "wait":
                    {
                        Only(s, where, "do", "car", "seconds", "until", "early", "_comment");
                        var until = WaitFor.Time;
                        if (ModJson.TryGet(s, "until", out var u))
                        {
                            until = Text(u, where).ToLowerInvariant() switch
                            {
                                "settled" => WaitFor.Settled,
                                "running" => WaitFor.Running,
                                "drive_end" => WaitFor.DriveEnd,
                                var other => throw new FormatException($"{where} waits until \"{other}\", which is none of settled, running, drive_end")
                            };
                        }

                        var fallback = until switch { WaitFor.Settled => 1.5f, WaitFor.Running => 4f, WaitFor.DriveEnd => 30f, _ => -1f };
                        var seconds = ModJson.TryGet(s, "seconds", out var sec) ? Number(sec, $"{where}.seconds", 0f, 120f) : fallback;
                        if (seconds < 0) throw new FormatException($"{where} says neither \"seconds\" nor \"until\"");
                        var early = ModJson.TryGet(s, "early", out var ea) ? Number(ea, $"{where}.early", 0f, 60f) : 0f;
                        if (early > 0 && until != WaitFor.DriveEnd) throw new FormatException($"{where}: \"early\" goes with \"until\": \"drive_end\" only");
                        return new WaitStep(car, until, seconds, early);
                    }
                    case "drive":
                        return Drive(s, car, where);
                    case "place":
                    {
                        Only(s, where, "do", "car", "at", "path", "end", "heading", "_comment");
                        var at = ModJson.TryGet(s, "at", out var a) ? Mark(a, $"{where}.at") : null;
                        var path = ModJson.TryGet(s, "path", out var p) ? Text(p, where) : null;
                        if (at == null == (path == null)) throw new FormatException($"{where} needs \"at\" or \"path\", one of them");
                        var end = ModJson.TryGet(s, "end", out var en) ? Text(en, where).ToLowerInvariant() : "start";
                        if (end is not ("start" or "end")) throw new FormatException($"{where}: \"end\" is \"start\" or \"end\"");
                        float? heading = ModJson.TryGet(s, "heading", out var hd) ? Number(hd, $"{where}.heading", -720f, 720f) : null;
                        return new PlaceStep(car, at, path, end == "end", heading);
                    }
                    case "fade":
                    {
                        Only(s, where, "do", "car", "to", "seconds", "ease", "engine", "_comment");
                        if (!ModJson.TryGet(s, "to", out var to)) throw new FormatException($"{where} has no \"to\"");
                        var ease = ModJson.TryGet(s, "ease", out var es)
                            ? Text(es, where).ToLowerInvariant() switch
                            {
                                "none" => FadeEase.None,
                                "in" => FadeEase.In,
                                "out" => FadeEase.Out,
                                var other => throw new FormatException($"{where}: ease \"{other}\" is none of none, in, out")
                            }
                            : FadeEase.None;
                        return new FadeStep(car, Number(to, $"{where}.to", 0f, 1f),
                            ModJson.TryGet(s, "seconds", out var sec) ? Number(sec, $"{where}.seconds", 0f, 30f) : 1f, ease,
                            ModJson.TryGet(s, "engine", out var en) && Bool(en, where));
                    }
                    case "camera":
                    {
                        Only(s, where, "do", "car", "mode", "_comment");
                        var mode = ModJson.TryGet(s, "mode", out var md) ? Text(md, where).ToLowerInvariant() : "";
                        return new CameraStep(car, mode switch
                        {
                            "hold" => CameraMode.Hold,
                            "follow" => CameraMode.Follow,
                            _ => throw new FormatException($"{where}: \"mode\" is \"hold\" or \"follow\"")
                        });
                    }
                    case "sound":
                    {
                        Only(s, where, "do", "cue", "_comment");
                        if (!ModJson.TryGet(s, "cue", out var cue)) throw new FormatException($"{where} has no \"cue\"");
                        return new SoundStep(Text(cue, $"{where}.cue"));
                    }
                    case "release":
                        Only(s, where, "do", "_comment");
                        return new ReleaseStep();
                    case "end":
                        Only(s, where, "do", "_comment");
                        return new EndStep();
                    default:
                        throw new FormatException($"{where}: \"{kind}\" is no step (lights, engine, wait, drive, place, fade, camera, sound, release, end, or an \"if\")");
                }
            }

            private DriveStep Drive(JsonElement s, string? car, string where)
            {
                Only(s, where, "do", "car", "path", "from", "to", "toward", "distance", "reverse", "feel", "start", "end", "wait", "_comment");
                var path = ModJson.TryGet(s, "path", out var p) ? Text(p, where) : null;
                var from = ModJson.TryGet(s, "from", out var f) ? Mark(f, $"{where}.from") : null;
                var to = ModJson.TryGet(s, "to", out var t) ? Mark(t, $"{where}.to") : null;
                var toward = ModJson.TryGet(s, "toward", out var tw) ? Mark(tw, $"{where}.toward") : null;
                float? distance = ModJson.TryGet(s, "distance", out var d) ? Number(d, $"{where}.distance", -500f, 500f) : null;

                var ways = (path != null ? 1 : 0) + (to != null ? 1 : 0) + (toward != null ? 1 : 0) + (distance != null ? 1 : 0);
                if (from != null && to == null) throw new FormatException($"{where}: \"from\" goes with \"to\" only");

                var reverse = ModJson.TryGet(s, "reverse", out var rv) && Bool(rv, where) || distance < 0;
                var feel = Feel(ModJson.TryGet(s, "feel", out var fl) ? Text(fl, where) : null, toward != null, where);
                if (toward != null && feel.Kind != DriveKind.Pull) throw new FormatException($"{where}: \"toward\" needs a feel of kind pull, not {feel.Id}");

                // A pull goes toward a mark, or straight on out of its place; every other drive goes one way of the four
                if (feel.Kind == DriveKind.Pull)
                {
                    if (ways != (toward != null ? 1 : 0))
                        throw new FormatException($"{where}: a pull ({feel.Id}) goes \"toward\" a mark, or straight on with none of \"path\", \"to\", \"distance\"");
                    if (reverse) throw new FormatException($"{where}: a pull ({feel.Id}) does not go in reverse");
                }
                else if (ways != 1)
                {
                    throw new FormatException($"{where} needs one of \"path\", \"to\", \"toward\", \"distance\"");
                }

                var start = ModJson.TryGet(s, "start", out var st) ? Text(st, where).ToLowerInvariant() : "standing";
                if (start is not ("standing" or "rolling")) throw new FormatException($"{where}: \"start\" is \"standing\" or \"rolling\"");
                var end = ModJson.TryGet(s, "end", out var en) ? Text(en, where).ToLowerInvariant() : "stop";
                if (end is not ("stop" or "keep_going")) throw new FormatException($"{where}: \"end\" is \"stop\" or \"keep_going\"");

                // A launch and a pull keep on; a roll in stops; the rest as the step says
                var keepGoing = feel.Kind switch { DriveKind.Launch or DriveKind.Pull => true, DriveKind.RollIn => false, _ => end == "keep_going" };
                return new DriveStep(car, path, from, to, toward, distance, reverse, feel, start == "rolling", keepGoing,
                    !ModJson.TryGet(s, "wait", out var wt) || Bool(wt, where));
            }

            private DriveFeel Feel(string? reference, bool pull, string where)
            {
                if (reference == null)
                {
                    var fallback = pull ? DefaultPullFeel : DefaultRoadFeel;
                    return feelsById.TryGetValue(fallback, out var feel) ? feel
                        : pull ? DriveFeel.Defaults(DriveKind.Pull) with { Id = DefaultPullFeel } : DriveFeel.Road;
                }

                var id = feels.Resolve(entry.Mod, reference.Trim());
                return feelsById.TryGetValue(id, out var found) ? found : throw new FormatException($"{where}: no mod has the feel \"{reference}\"");
            }

            private ScriptCondition Condition(JsonElement c, string where)
            {
                if (c.ValueKind != JsonValueKind.Object) throw new FormatException($"{where} is not a condition");
                Only(c, where, "engine", "towed", "light", "hours", "from", "to", "not", "any", "_comment");
                var condition = ScriptCondition.Always;
                if (ModJson.TryGet(c, "engine", out var e))
                {
                    condition = condition with
                    {
                        Engine = Text(e, where).ToLowerInvariant() switch
                        {
                            "runs" => EngineIs.Runs,
                            "dead" => EngineIs.Dead,
                            "running" => EngineIs.Running,
                            "off" => EngineIs.Off,
                            var other => throw new FormatException($"{where}: engine \"{other}\" is none of runs, dead, running, off")
                        }
                    };
                }

                if (ModJson.TryGet(c, "towed", out var t)) condition = condition with { Towed = Bool(t, where) };
                if (ModJson.TryGet(c, "light", out var l))
                {
                    var names = l.ValueKind == JsonValueKind.Array ? l.EnumerateArray().Select(n => Text(n, where)).ToList() : [Text(l, where)];
                    var lights = new HashSet<StreetLight>();
                    foreach (var name in names)
                    {
                        if (!SceneLighting.TryParse(name, out var light)) throw new FormatException($"{where}: light \"{name}\" is none of day, dusk, night");
                        lights.Add(light);
                    }

                    condition = condition with { Light = lights };
                }

                if (ModJson.TryGet(c, "hours", out var h))
                {
                    var hours = h.ValueKind == JsonValueKind.Array ? h.EnumerateArray().Select(x => (int)Number(x, $"{where}.hours", 0f, 24f)).ToList() : [];
                    if (hours.Count != 2) throw new FormatException($"{where}: \"hours\" is [from, to], hours of the day");
                    condition = condition with { Hours = (hours[0], hours[1]) };
                }

                if (ModJson.TryGet(c, "from", out var fr)) condition = condition with { From = Screens(fr, $"{where}.from") };
                if (ModJson.TryGet(c, "to", out var to)) condition = condition with { To = Screens(to, $"{where}.to") };
                if (ModJson.TryGet(c, "not", out var n)) condition = condition with { Not = Condition(n, $"{where}.not") };
                if (ModJson.TryGet(c, "any", out var a))
                {
                    if (a.ValueKind != JsonValueKind.Array) throw new FormatException($"{where}.any is not a list of conditions");
                    condition = condition with { Any = a.EnumerateArray().Select((x, i) => Condition(x, $"{where}.any[{i}]")).ToList() };
                }

                return condition;
            }

            /// <summary>One screen id or a list of them, matched whatever their case</summary>
            private static IReadOnlySet<string> Screens(JsonElement s, string where)
            {
                var ids = s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().Select(x => Text(x, where)).ToList() : [Text(s, where)];
                if (ids.Count == 0 || ids.Any(string.IsNullOrWhiteSpace)) throw new FormatException($"{where} is not a screen id or a list of them");
                return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            }

            private static MarkSpec Mark(JsonElement m, string where) => SceneMarks.ParseMark(m, where, names: true);

            private static void Only(JsonElement e, string where, params string[] keys)
            {
                foreach (var property in e.EnumerateObject())
                {
                    if (!keys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                        throw new FormatException($"{where}: \"{property.Name}\" is not one of {string.Join(", ", keys.Where(k => k != "_comment"))}");
                }
            }

            private static string Text(JsonElement v, string where) =>
                v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : throw new FormatException($"{where}: {v} is not a word");

            private static bool Bool(JsonElement v, string where) =>
                v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw new FormatException($"{where}: {v} is not true or false");

            private static float Number(JsonElement v, string where, float min, float max) => SceneMarks.Number(v, where, min, max);
        }
    }
}
