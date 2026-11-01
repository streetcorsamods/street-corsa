using System.Text.Json;

namespace StreetCorsa.Parts;

/// <summary>
/// The classes of part and what each is a kind of, from part_classes.json: a part of a class counts as one of every
/// class above it, so a disc brake is a brake and an engine block an engine part. The engine's logic, the shops'
/// shelves and the work order go by class. A mod adds classes, or puts one under another parent, in its own file.
/// </summary>
public sealed class PartClasses
{
    public const string FileName = "part_classes.json";

    // The classes the game's own logic reads; a mod's file can add classes under them, never take them away
    public const string Part = "part";
    public const string EnginePart = "engine_part";
    public const string EngineBlock = "engine_block";
    public const string CylinderHead = "cylinder_head";
    public const string Camshaft = "camshaft";
    public const string Crankshaft = "crankshaft";
    public const string ConnectingRods = "connecting_rods";
    public const string Pistons = "pistons";
    public const string Flywheel = "flywheel";
    public const string Clutch = "clutch";
    public const string Transmission = "transmission";
    public const string OilPan = "oil_pan";
    public const string IntakeManifold = "intake_manifold";
    public const string FuelSystem = "fuel_system";
    public const string AirCleaner = "air_cleaner";
    public const string Charger = "charger";
    public const string Supercharger = "supercharger";
    public const string Turbocharger = "turbocharger";
    public const string Nitrous = "nitrous";
    public const string ExhaustHeader = "exhaust_header";
    public const string Radiator = "radiator";
    public const string DrumBrake = "drum_brake";
    public const string DiscBrake = "disc_brake";
    public const string LeafSpring = "leaf_spring";

    // Most classes a class may have above it: a loop in a mod's file ends there
    private const int MaxDepth = 32;

    private static readonly PartsFiles<PartClasses> Files = new(Load);

    /// <summary>The classes of the mods next to the game, read once</summary>
    public static PartClasses Installed => Files.Installed;

    /// <summary>The classes in use: the installed ones, or ones a test or a tool sets</summary>
    public static PartClasses Current
    {
        get => Files.Current;
        set => Files.Current = value;
    }

    private readonly Dictionary<string, string> _parents;

    private PartClasses(Dictionary<string, string> parents, List<string> problems)
    {
        _parents = parents;
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }

    /// <summary>Every class named, with the one above it ("" for the root)</summary>
    public IReadOnlyDictionary<string, string> Parents => _parents;

    /// <summary>True when a part of class <paramref name="cls"/> counts as <paramref name="of"/>: the same class or one above it</summary>
    public bool IsA(string? cls, string of)
    {
        for (var depth = 0; cls != null && depth < MaxDepth; depth++)
        {
            if (cls.Equals(of, StringComparison.OrdinalIgnoreCase)) return true;
            cls = _parents.GetValueOrDefault(cls);
        }

        return false;
    }

    /// <summary>Every mod's file, in load order (the core's first); a later file's parent for a class wins</summary>
    public static PartClasses Load(IEnumerable<string> partsFolders)
    {
        var problems = new List<string>();
        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        PartsFiles.ReadEach(partsFolders, FileName, problems, (file, root) =>
        {
            foreach (var entry in root.GetProperty("classes").EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Name))
                {
                    problems.Add($"{file}: class {entry.Name} names no parent class; left out");
                    continue;
                }

                parents[entry.Name] = entry.Value.GetString()!;
            }
        });

        return new PartClasses(parents, problems);
    }
}
