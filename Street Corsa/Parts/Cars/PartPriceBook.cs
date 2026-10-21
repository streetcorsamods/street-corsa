using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// What every part of the catalog costs new, worked out by what they do
/// (<see cref="PartPrice"/> in part_groups.json). The power a part is worth is measured once on the dyno:
/// <list type="bullet">
/// <item>an engine block is worth the power of its own engine: the least of its factory builds that run (any of its
/// builds when none is a factory one);</item>
/// <item>any other part of an engine is worth what it makes over the factory part in its place, on the build it does
/// the most for (what was on the factory part moved over, as a tuner does it). A factory part is worth nothing over
/// itself: its kind's base price, the engine's power being its block's.</item>
/// </list>
/// A part no engine takes, and running gear, is worth no power: its kind's base price and figure only. These are the
/// prices of loose parts; a whole engine is priced by its own power (<see cref="PartPricing.EngineNewPrice"/>).
///
/// Plain BCL and linked into EngineBench, which makes its own; the game makes it once per session from the catalog, the
/// measuring (several seconds) kept on disk against the <see cref="Fingerprint"/> of the parts it was made from.
/// </summary>
public sealed class PartPriceBook
{
    // More than any engine makes, well inside what a price can hold
    private const double MaxPower = 100_000;

    /// <summary>
    /// Moved on when the measuring changes (what a part is measured against): every kept one is made again. The engine
    /// model and the dyno are code, and that code itself is in the <see cref="Fingerprint"/> besides.
    /// </summary>
    private const int MeasuringVersion = 2;

    // The dyno runs make garbage fast: more threads than this only fight over the heap
    private static int Threads => Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    private readonly Dictionary<string, double> _power;
    private readonly Dictionary<string, double> _middles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _carbMounts;

    private PartPriceBook(Dictionary<string, double> power, Dictionary<string, int> carbMounts, IReadOnlyList<string> faults)
    {
        _power = power;
        _carbMounts = carbMounts;
        Faults = faults;
    }

    /// <summary>No part measured: every part at its kind's base price and figure</summary>
    public static PartPriceBook Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), Array.Empty<string>());

    /// <summary>The parts measured on the dyno</summary>
    public int Measured => _power.Count;

    /// <summary>
    /// The builds that broke while the parts were measured on them, one line each, for the log (a build a part breaks
    /// prices nothing, the rest still do); empty for a measuring taken back from disk
    /// </summary>
    public IReadOnlyList<string> Faults { get; }

    /// <summary>The power a part is worth on the dyno, hp; null for one no engine takes</summary>
    public double? PowerOf(string partId) => _power.TryGetValue(partId, out var power) ? power : null;

    /// <summary>Every measured part's power, by id: the measuring, to keep (<see cref="Create"/> takes it back)</summary>
    public IReadOnlyDictionary<string, double> Power => _power;

    /// <summary>
    /// What the measuring rests on: every file under the parts folders and every mod.json given (its path, size and time) and the code that works
    /// the engines out and measures them (<see cref="Logic.EngineAssembly"/>, the dyno, this book: <see cref="CodeOf"/>).
    /// The same parts and code give the same fingerprint, and a kept measuring of it stands; a change to a screen
    /// elsewhere in the game does not move it.
    /// </summary>
    public static string Fingerprint(IEnumerable<string> partsFolders)
    {
        var text = new StringBuilder();
        text.Append(MeasuringVersion).Append('\n');
        var engine = typeof(Logic.EngineAssembly);
        CodeOf(text, engine.Assembly.GetTypes().Where(t => t.Namespace == engine.Namespace || t == typeof(PartPriceBook) || t.DeclaringType == typeof(PartPriceBook)));
        foreach (var folder in partsFolders)
        {
            // A mod's mod.json, which may hold its packs and builds
            if (File.Exists(folder))
            {
                var file = new FileInfo(folder);
                text.Append(file.FullName).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append('\n');
                continue;
            }

            if (!Directory.Exists(folder)) continue;

            text.Append(Path.GetFullPath(folder)).Append('\n');

            // A folder the game may not read is passed over, and a junction is not followed (a loop among them would
            // never end): the measuring rests on what the catalog reads (PartsCatalog.PackFiles passes them over too)
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(folder, "*", options).Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                text.Append(Path.GetRelativePath(folder, file)).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>
    /// The code of <paramref name="types"/> as text: each method's IL, with what it calls, reads and makes by name rather
    /// than by metadata token (a token moves when a type elsewhere in the game gains a member), and the fields with
    /// their constants. The same code gives the same text whatever else the build holds.
    /// </summary>
    private static void CodeOf(StringBuilder text, IEnumerable<Type> types)
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            text.Append(type.FullName).Append('\n');
            foreach (var field in type.GetFields(declared).OrderBy(f => f.Name, StringComparer.Ordinal))
                text.Append(field.FieldType.FullName).Append(' ').Append(field.Name).Append('=').Append(field.IsLiteral ? field.GetRawConstantValue() : null).Append('\n');

            var methods = type.GetMethods(declared).Cast<MethodBase>().Concat(type.GetConstructors(declared))
                .Select(m => (Method: m, Name: m.ToString() ?? m.Name)).OrderBy(m => m.Name, StringComparer.Ordinal);
            foreach (var (method, name) in methods)
            {
                text.Append(name).Append(':');
                if (method.GetMethodBody()?.GetILAsByteArray() is { } il) IlOf(text, method, il);
                text.Append('\n');
            }
        }
    }

    private static void IlOf(StringBuilder text, MethodBase method, byte[] il)
    {
        var typeArgs = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var at = 0; at < il.Length;)
        {
            var value = il[at] == 0xFE && at + 1 < il.Length ? (short)(0xFE00 | il[at + 1]) : il[at];
            if (!OpCodesByValue.TryGetValue(value, out var op))
            {
                // Not IL as the runtime knows it: the bytes as they are
                text.Append(Convert.ToHexString(il, at, il.Length - at));
                return;
            }

            at += op.Size;
            text.Append(op.Name).Append(' ');
            var size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => at + 4 <= il.Length ? 4 + 4 * BitConverter.ToInt32(il, at) : 4,
                _ => 4
            };
            size = Math.Clamp(size, 0, il.Length - at);

            if (size == 4 && op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok
                    or OperandType.InlineString or OperandType.InlineSig)
                text.Append(TokenName(method.Module, BitConverter.ToInt32(il, at), op.OperandType, typeArgs, methodArgs));
            else
                text.Append(Convert.ToHexString(il, at, size));

            text.Append(';');
            at += size;
        }
    }

    private static string? TokenName(Module module, int token, OperandType kind, Type[]? typeArgs, Type[]? methodArgs)
    {
        try
        {
            return kind switch
            {
                OperandType.InlineString => module.ResolveString(token),
                OperandType.InlineSig => Convert.ToHexString(module.ResolveSignature(token)),
                _ => module.ResolveMember(token, typeArgs, methodArgs) is { } member ? $"{member.DeclaringType?.FullName}::{member}" : null
            };
        }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException)
        {
            return token.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// What <paramref name="part"/> costs new, loose, in the game's dollars before the economy's and a career's factors:
    /// its kind's base, its power (to the kind's power of it) at the kind's dollars per hp, scaled by its figure against
    /// the middle one of its kind
    /// </summary>
    public double Price(PartDefinition part) => Price(part, PowerOf(part.Id) ?? 0);

    /// <summary>What <paramref name="part"/> costs new for nothing but being what it is: its price at no power</summary>
    public double BasePrice(PartDefinition part) => Price(part, 0);

    /// <summary>
    /// What a whole engine making <paramref name="hp"/> on the dyno is worth for its power: a block of that power, on the
    /// blocks' curve. An engine is priced by what it makes as a whole (<see cref="PartPricing.EngineNewPrice"/>), wherever
    /// in it the power is: in the block, or in its heads, crank and carburettors.
    /// </summary>
    public static double PowerPrice(double hp)
    {
        var price = PartGroups.Current.PriceOf(PartGroups.Blocks);
        var value = Positive(price.Base) + Positive(price.PerHp) * Powered(Math.Clamp(double.IsFinite(hp) ? hp : 0, 0, MaxPower), price);
        return double.IsFinite(value) ? Math.Max(0, value) : 0;
    }

    private static double Powered(double hp, PartPrice price) =>
        price.HpPower is { } power && double.IsFinite(power) && power > 0 ? Math.Pow(hp, power) : hp;

    private double Price(PartDefinition part, double hp)
    {
        var group = PartKinds.GroupOf(part);
        var price = PartGroups.Current.PriceOf(group);
        var value = Positive(price.Base) + Positive(price.PerHp) * Powered(hp, price);

        if (price.By is { } by && Figure(part, by) is { } figure && figure > 0 && Middle(group, by) is { } middle && middle > 0)
        {
            var scaled = value * Math.Pow(figure / middle, price.ByPower is { } byPower && double.IsFinite(byPower) ? byPower : 1);
            if (double.IsFinite(scaled)) value = scaled;
        }

        return double.IsFinite(value) ? Math.Max(0, value) : 0;
    }

    private static double Positive(double? figure) => figure is { } value && double.IsFinite(value) && value > 0 ? value : 0;

    private double? Middle(string group, string by) => _middles.TryGetValue(group + "|" + by, out var middle) ? middle : null;

    /// <summary>
    /// A figure a kind can be priced by (<see cref="PartPrice.Figures"/>); null for a name that is none. A manifold's
    /// carburettors (<c>carb_mounts</c>) are counted when the book is made: a dual or four-carb manifold is a kit that no
    /// dyno run of the manifold alone can measure (the plain one has nowhere for the carbs, and the other way round)
    /// </summary>
    public double? Figure(PartDefinition part, string by) => by.ToLowerInvariant() switch
    {
        "grip" => RunningGear.TyreGrip(part) * RunningGear.TyreWidth(part),
        "brake_torque" => RunningGear.BrakeTorque(part),
        "rim_size" => RunningGear.RimWidth(part) * part.Number("wheel_radius"),
        "spring_rate" => RunningGear.SpringRate(part),
        "damping" => RunningGear.Damping(part).Bump,
        "carb_mounts" => _carbMounts.TryGetValue(part.Id, out var mounts) ? mounts : null,
        "strength" => RearAxle.Strength(part),
        "diff_lock" => RearAxle.LockFigure(part),
        "arb_rate" => RunningGear.SwayBarRate(part),
        _ => null
    };

    /// <summary>
    /// Every part of <paramref name="catalog"/> measured on the builds of <paramref name="index"/> that run (a few
    /// thousand dyno runs, the builds side by side), or the power <paramref name="measured"/> before of the same parts
    /// </summary>
    public static PartPriceBook Create(PartsCatalog catalog, EngineBuildIndex index, IReadOnlyDictionary<string, double>? measured = null)
    {
        var faults = new List<string>();
        var power = measured != null
            ? new Dictionary<string, double>(measured.Where(p => double.IsFinite(p.Value)).ToDictionary(p => p.Key, p => Math.Clamp(p.Value, 0, MaxPower)),
                StringComparer.OrdinalIgnoreCase)
            : Measure(catalog, index, faults);

        // The carburettors each part of the kinds priced by them takes
        var carbMounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in catalog.Parts.Values.Where(p => PartGroups.Current.PriceOf(PartKinds.GroupOf(p)).By == "carb_mounts"))
        {
            var mounts = part.Slots.Count(slot => catalog.FindMountable(part, slot).Any(m => PartKinds.GroupOf(m.Part) == PartGroups.Carburettors));
            if (mounts > 0) carbMounts[part.Id] = mounts;
        }

        var book = new PartPriceBook(power, carbMounts, faults);
        book.FindMiddles(catalog);
        return book;
    }

    /// <param name="faults">Gets a line for every build that broke while measured</param>
    private static Dictionary<string, double> Measure(PartsCatalog catalog, EngineBuildIndex index, List<string> faults)
    {
        var power = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // A block: the least of its factory engines
        foreach (var blocks in index.Runnable.GroupBy(b => b.BlockId, StringComparer.OrdinalIgnoreCase))
        {
            var factory = blocks.Where(b => !b.Build.Aftermarket).ToList();
            var least = (factory.Count > 0 ? factory : blocks.ToList()).Min(b => b.PowerHp);
            if (double.IsFinite(least) && least > 0) power[blocks.Key] = Math.Min(least, MaxPower);
        }

        // Every other part: what it makes over the factory part in its place, on the build it does the most for
        var gains = new ConcurrentDictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var broke = new ConcurrentBag<string>();
        Parallel.ForEach(index.Runnable, new ParallelOptions { MaxDegreeOfParallelism = Threads }, build =>
        {
            try
            {
                foreach (var (partId, gain) in Gains(catalog, build))
                    gains.AddOrUpdate(partId, gain, (_, most) => Math.Max(most, gain));
            }
            catch (Exception ex)
            {
                // One build a part breaks is a build that prices nothing: the rest still do, and the log hears of it
                broke.Add($"{build.Build.Id}: {ex.GetType().Name}: {ex.Message}; priced no part");
            }
        });

        foreach (var (partId, most) in gains)
        {
            if (!power.ContainsKey(partId)) power[partId] = Math.Clamp(most, 0, MaxPower);
        }

        faults.AddRange(broke.Order(StringComparer.Ordinal));
        return power;
    }

    /// <summary>What each part of a kind on <paramref name="build"/> makes over the factory part in its place</summary>
    private static IEnumerable<(string PartId, double Gain)> Gains(PartsCatalog catalog, RatedBuild build)
    {
        if (EngineFactory.CreateStock(catalog, build, 1.0, new Random(0)) is not { } engine) yield break;

        // As new: the dyno measures the parts, not their wear
        foreach (var part in engine.Root.SelfAndDescendants()) part.Wear = 1;
        if (EngineTuner.PowerOf(catalog, engine.Root) is not { } now) yield break;

        // Only what goes where a part of its own kind is: of the catalog's parts most go nowhere on any one engine
        var candidates = new Dictionary<string, PartDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var parent in engine.Root.SelfAndDescendants())
        {
            if (catalog.Get(parent.DefinitionId) is not { } parentDefinition) continue;
            foreach (var child in parent.Children)
            {
                if (catalog.Get(child.DefinitionId) is not { } childDefinition || PartKinds.GroupOf(childDefinition) is var group && group == PartGroups.Blocks) continue;
                if (parentDefinition.Slots.FirstOrDefault(s => s.Id == child.ParentSlot) is not { } slot) continue;

                foreach (var (part, _) in catalog.FindMountable(parentDefinition, slot))
                {
                    if (PartKinds.GroupOf(part) == group) candidates.TryAdd(part.Id, part);
                }
            }
        }

        foreach (var part in candidates.Values)
        {
            if (EngineTuner.PowerWith(catalog, engine.Root, part, now) is { } with && double.IsFinite(with.After))
                yield return (part.Id, with.After - now);
        }
    }

    /// <summary>The middle figure of every kind by every figure it can be priced by, over the parts that have one</summary>
    private void FindMiddles(PartsCatalog catalog)
    {
        foreach (var group in catalog.Parts.Values.GroupBy(PartKinds.GroupOf))
        {
            foreach (var by in PartPrice.Figures)
            {
                var figures = group.Select(p => Figure(p, by) ?? 0).Where(f => double.IsFinite(f) && f > 0).OrderBy(f => f).ToList();
                if (figures.Count > 0) _middles[group.Key + "|" + by] = figures[figures.Count / 2];
            }
        }
    }
}
