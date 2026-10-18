using System.Runtime.CompilerServices;

namespace StreetCorsa.Parts;

/// <summary>
/// What kind of thing a part is, read off its script classes: the class of the source game's own framework
/// nearest to the part ("OHV_Camshaft", "Transmission") is the reliable taxonomy, the catalog categories of
/// the mods are not.
/// </summary>
public static class PartKinds
{
    public const string Accessories = PartGroups.Accessories;

    private const string FrameworkPackage = "java.game.parts.";

    // The running gear: what goes on the car's own wheel slots, and what goes on it
    public const string Tyres = PartGroups.Tyres;
    public const string Rims = PartGroups.Rims;
    public const string Brakes = PartGroups.Brakes;
    public const string Springs = PartGroups.Springs;
    public const string Shocks = PartGroups.Shocks;
    public const string SwayBars = PartGroups.SwayBars;

    // What goes on a leaf spring, and what it hangs from on the car: a hanger and a shackle, the U-bolts on the leaf
    public const string TractionBars = PartGroups.TractionBars;
    public const string LeafHardware = PartGroups.LeafHardware;

    // A drum brake: the backing plate on the corner's brake slot, the shoes, the wheel cylinder and the drum on it, the
    // hub on a front drum or disc
    public const string BackingPlates = PartGroups.BackingPlates;
    public const string BrakeShoes = PartGroups.BrakeShoes;
    public const string WheelCylinders = PartGroups.WheelCylinders;
    public const string Hubs = PartGroups.Hubs;

    // The rear axle: on the car's own axle slot, with its carrier, gears and shafts on it
    public const string RearAxles = PartGroups.RearAxles;
    public const string Differentials = PartGroups.Differentials;
    public const string RingPinions = PartGroups.RingPinions;
    public const string AxleShafts = PartGroups.AxleShafts;

    /// <summary>
    /// Simple name of the framework class nearest to the part (a simple name as the part gives it, for a part of
    /// figures), "" for parts without a script class.
    /// Classes of the engine packs and cars sit under java.game.parts.engines / java.game.cars.
    /// </summary>
    public static string FrameworkClass(PartDefinition part)
    {
        foreach (var name in part.ClassChain)
        {
            // A part of figures alone names its classes simply
            if (!name.Contains('.')) return name;
            if (!name.StartsWith(FrameworkPackage, StringComparison.Ordinal)) continue;
            if (name.StartsWith(FrameworkPackage + "engines.", StringComparison.Ordinal)) continue;

            return name[(name.LastIndexOf('.') + 1)..];
        }

        return string.Empty;
    }

    // A part's class chain does not change once it is loaded, and shops, the workbench and the engine factory ask
    // for its group per candidate in their loops: worked out once per part, for the groups in use
    private static readonly ConditionalWeakTable<PartDefinition, Tuple<PartGroups, string>> GroupCache = new();

    /// <summary>Id of the shelf the part is found on in a shop (<see cref="PartGroups"/>)</summary>
    public static string GroupOf(PartDefinition part)
    {
        var groups = PartGroups.Current;
        if (GroupCache.TryGetValue(part, out var known) && ReferenceEquals(known.Item1, groups)) return known.Item2;

        var group = groups.GroupOfClass(FrameworkClass(part));
        GroupCache.AddOrUpdate(part, Tuple.Create(groups, group));
        return group;
    }

    /// <summary>What the player reads for a group: its words in the text (<c>part_group.&lt;id&gt;</c>), else its name, else its id</summary>
    public static string GroupName(string group)
    {
        var key = "part_group." + group;
        var said = Cars.PartWords.Say(key);
        if (said != key) return said;
        return PartGroups.Current.Get(group)?.Name is { Length: > 0 } name ? name : group.Replace('_', ' ');
    }

    /// <summary>
    /// Kinds that never mount on their own kind: a head does not go on a head, whatever a config says.
    /// Checked against every engine build. Intake manifolds are not among them: some go on a converter plate
    /// that is a manifold itself; clutches go on flywheels, accessories on accessories.
    /// </summary>
    public static bool NeverStacks(string group) => PartGroups.Current.NeverStacks(group);

    public static bool IsBlock(PartDefinition part) => Is(part, "Block");

    /// <summary>
    /// True when the part's script descends from the class, given by simple name. A part of figures alone may name its
    /// classes simply ("class_chain": ["Camshaft"]).
    /// </summary>
    public static bool Is(PartDefinition part, string className) =>
        part.ClassChain.Any(c => c.EndsWith("." + className, StringComparison.Ordinal) || c == className);
}
