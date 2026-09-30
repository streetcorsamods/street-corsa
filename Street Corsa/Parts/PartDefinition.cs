using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StreetCorsa.Parts;

/// <summary>
/// A group of parts converted together (one SLRR rpk), stored as pack.json next to the part models
/// </summary>
public class PartPack
{
    public const string FileName = "pack.json";

    /// <summary>
    /// Next to the packs: ids of parts that are gone, with the part that took their place (a pack replaced by a
    /// later release). Saves and profiles made before keep working through it.
    /// </summary>
    public const string AliasesFileName = "part_aliases.json";

    /// <summary>Pack id, also its folder under the parts root, e.g. "engines/Mopar"</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Where the pack was converted from, relative to the source game</summary>
    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// The make the pack's parts are for, as the part shops name it ("mopar", "ford", "gm"): a make specialist stocks
    /// every pack of its makes, a mod's too. None for parts of no make (the universal-fit parts, the running gear).
    /// </summary>
    [JsonProperty("make", NullValueHandling = NullValueHandling.Ignore)]
    public string? Make { get; set; }

    [JsonProperty("parts")]
    public List<PartDefinition> Parts { get; set; } = new();
}

/// <summary>
/// One physical part: its 3D model plus the slots that say where it mounts and what mounts on it.
/// Geometry data follows the SLRR part cfg: metres, left-handed, Y up.
/// </summary>
public class PartDefinition
{
    /// <summary>Unique id: pack id + config name, e.g. "engines/Mopar/block_340"</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// In a mod: the full id of a part of another mod this one takes the place of, on purpose (it gets that id).
    /// Null for a part of its own.
    /// </summary>
    [JsonProperty("replaces", NullValueHandling = NullValueHandling.Ignore)]
    public string? Replaces { get; set; }

    /// <summary>The mod the part came from</summary>
    [JsonIgnore]
    public string ModId { get; set; } = Mods.ModIds.BaseId;

    /// <summary>The id as its pack wrote it, without the mod's namespace</summary>
    [JsonIgnore]
    public string LocalId
    {
        get => string.IsNullOrEmpty(_localId) ? Id : _localId;
        set => _localId = value;
    }

    private string? _localId;

    /// <summary>The folder of the pack.json the part came from: its model is found there</summary>
    [JsonIgnore]
    public string? PackFolder { get; set; }

    /// <summary>The make of the part's pack (<see cref="PartPack.Make"/>); null for a part of no make</summary>
    [JsonIgnore]
    public string? Make { get; set; }

    /// <summary>The compiled scripts of the part's mod: its <see cref="SourceScript"/> is found there</summary>
    [JsonIgnore]
    public string? ScriptRoot { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The name the player reads, e.g. "Chrysler 340 V8 block": the script's at conversion, written for the game since (docs/modding.md)</summary>
    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Script base class that defines how the part behaves,
    /// e.g. "java.game.parts.enginepart.block.block_vee.Block_Vee_OHV"
    /// </summary>
    [JsonProperty("base_class")]
    public string? BaseClass { get; set; }

    /// <summary>Every script class above the part, nearest first, ending at "java.game.parts.Part"</summary>
    [JsonProperty("class_chain")]
    public List<string> ClassChain { get; set; } = new();

    /// <summary>
    /// What job the part is on an engine, for a mod whose part names do not say it: a kind of
    /// <see cref="WorkOrder.FileName"/> ("timing_cover"). Null leaves it to the work order to tell.
    /// </summary>
    [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)]
    public string? WorkKind { get; set; }

    /// <summary>
    /// How this part comes off, leg by leg, where it is mounted otherwise than its kind usually is: an intake manifold
    /// on the side of the head comes off ["out"], not up. It stays of its kind, rules and all. Null goes by the kind
    /// (<see cref="Parts.WorkKind.ComesOff"/>).
    /// </summary>
    [JsonProperty("comes_off", NullValueHandling = NullValueHandling.Ignore)]
    public List<string?>? ComesOff { get; set; }

    /// <summary>
    /// Whether the part does its job better than a factory one: a performance radiator, water pump or fan, a deep race
    /// oil pan. Null leaves it to the words of the part's name, as the source game's parts are read.
    /// </summary>
    [JsonProperty("performance", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Performance { get; set; }

    /// <summary>
    /// What the part does for the engine's cooling as its kind in the work order says (<see cref="Parts.WorkKind.Cooling"/>):
    /// "radiator", "water_pump", "fan" or "none". Null when its kind does not say, which leaves it to the words of
    /// its name. Set by the catalog as it loads.
    /// </summary>
    [JsonIgnore]
    public string? CoolingRole { get; set; }

    /// <summary>
    /// A radiator with a fan of its own: its kind in the work order serves a kind that is a fan
    /// (<see cref="Parts.WorkKind.Serves"/>). Only read with <see cref="CoolingRole"/>; set by the catalog as it loads.
    /// </summary>
    [JsonIgnore]
    public bool CoolingOwnFan { get; set; }

    /// <summary>
    /// Whether the part has a script of its own that was run: the base game's root entries (what mods declare their
    /// fit against) have none, so no properties and no classes. A script whose parent class is missing still counts:
    /// it has no chain, but it was constructed.
    /// </summary>
    [JsonIgnore]
    public bool IsScripted => ClassChain.Count > 0 || Properties.Count > 0;

    /// <summary>
    /// The part's script fields once constructed (bore, stroke, ratio, value, max_wear...), in the script's
    /// own names and units, with the pack's <see cref="Overrides"/> over them. Values are numbers, strings,
    /// "rpk path#0xID" resources or lists of those. What is read from the pack about the part (its price, running
    /// gear, sump) is read here; the engine is worked out by running the script itself.
    /// </summary>
    [JsonProperty("properties")]
    public Dictionary<string, object> Properties { get; set; } = new();

    /// <summary>
    /// Script fields a mod sets over what the part's script constructs, before the player's tuning: a number
    /// ("stroke": 76.2), a number for one element of an array field ("ratio[2]": 1.0), or a list of numbers for an
    /// array field from its first element on ("ratio": [0, 1.76, 1.0]; null keeps an element). This is how a part
    /// sharing a stock script gets its own figures. As written in the pack; what is used is <see cref="FieldOverrides"/>.
    /// </summary>
    [JsonProperty("overrides", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, JToken>? Overrides { get; set; }

    /// <summary>
    /// <see cref="Overrides"/> as the catalog read them (<see cref="PartOverrides"/>): checked against the script's
    /// fields, whole fields cut to whole numbers, lists before single elements. Also put in <see cref="Properties"/>.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<PartOverride> FieldOverrides { get; internal set; } = Array.Empty<PartOverride>();

    /// <summary>
    /// Fields the script recomputes in updatevariables(), as they come out for a brand new part on its own.
    /// Reference values to check the game's own part logic against.
    /// </summary>
    [JsonProperty("derived")]
    public Dictionary<string, object> Derived { get; set; } = new();

    /// <summary>What the script expects on which of the part's slots, e.g. "crankshaft" -> 8</summary>
    [JsonProperty("slot_roles")]
    public Dictionary<string, int> SlotRoles { get; set; } = new();

    /// <summary>Parts the script mounts on a factory build of this part</summary>
    [JsonProperty("stock_parts")]
    public List<PartStockReference> StockParts { get; set; } = new();

    /// <summary>Slots that must be filled for the part to work, with the script's explanation</summary>
    [JsonProperty("required_slots")]
    public List<PartSlotRule> RequiredSlots { get; set; } = new();

    /// <summary>
    /// A number among <see cref="Properties"/>. They come back from JSON as long or double, whichever the
    /// script's number happened to look like, so they are never to be read by type.
    /// </summary>
    public double Number(string property, double fallback = 0) =>
        Properties.GetValueOrDefault(property) is { } value and (long or int or double or float or decimal)
            ? System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
            : fallback;

    /// <summary>Catalog categories from the most specific up, e.g. ["blocks", "Main", "engine", "parts"]</summary>
    [JsonProperty("categories")]
    public List<string> Categories { get; set; } = new();

    /// <summary>Model file next to pack.json; null for parts without usable geometry</summary>
    [JsonProperty("model")]
    public string? Model { get; set; }

    [JsonProperty("mass")]
    public float Mass { get; set; }

    [JsonProperty("slots")]
    public List<PartSlot> Slots { get; set; } = new();

    /// <summary>Remaining cfg lines by keyword (category, damage, wing, wheel, ...), kept verbatim for the part logic</summary>
    [JsonProperty("config")]
    public Dictionary<string, List<string>> Config { get; set; } = new();

    /// <summary>Source ids, to trace a part back to the original data</summary>
    [JsonProperty("source_type_id")]
    public int SourceTypeId { get; set; }

    [JsonProperty("source_script")]
    public string? SourceScript { get; set; }

    /// <summary>
    /// A pack.json that says null for a list (or has a null in one) gets it empty: the catalog's mating index is
    /// built once, and one null in it would fail every lookup after
    /// </summary>
    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context)
    {
        Name ??= string.Empty;
        ClassChain = NonNull(ClassChain);
        Properties ??= new();
        Derived ??= new();
        SlotRoles ??= new();
        StockParts = NonNull(StockParts);
        RequiredSlots = NonNull(RequiredSlots);
        Categories = NonNull(Categories);
        Slots = NonNull(Slots);
        Config ??= new();
        foreach (var key in Config.Where(c => c.Value == null).Select(c => c.Key).ToList()) Config.Remove(key);
    }

    /// <summary>The list without its nulls; an empty one for null</summary>
    internal static List<T> NonNull<T>(List<T>? values) where T : class
    {
        if (values == null) return new();
        values.RemoveAll(v => v is null);
        return values;
    }
}

public class PartSlot
{
    /// <summary>
    /// Id of the slot a manifold (or blower) with a row of carburettor pads has over the row, for an air cleaner
    /// that spans them all. A carburettor in the row whose own air horn is empty counts that cleaner as its own.
    /// </summary>
    public const int SharedAirSlot = 311;

    /// <summary>
    /// Fittings of air cleaners and scoops start with it ("air:single" on one carburettor's horn, "air:2x4" over a
    /// row): a slot that takes one is an air horn
    /// </summary>
    public const string AirFittingPrefix = "air:";

    /// <summary>Whether the slot is an air horn: it takes an air cleaner or scoop by a standard fitting</summary>
    [JsonIgnore]
    public bool TakesAir => Takes.Any(t => t.StartsWith(AirFittingPrefix, StringComparison.OrdinalIgnoreCase));

    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>X, Y, Z in metres, in the part's own space</summary>
    [JsonProperty("position")]
    public float[] Position { get; set; } = new float[3];

    /// <summary>Yaw, pitch, roll in radians</summary>
    [JsonProperty("rotation")]
    public float[] Rotation { get; set; } = new float[3];

    [JsonProperty("damage_mode")]
    public int? DamageMode { get; set; }

    /// <summary>Parent slots this slot mounts on. Empty for slots that only receive other parts.</summary>
    [JsonProperty("attaches_to")]
    public List<PartSlotReference> AttachesTo { get; set; } = new();

    /// <summary>
    /// Slots of other parts this slot stands in for: the part fits wherever those fit.
    /// This is how parts that mount on a car (engine blocks, wheels, wings) declare what they fit.
    /// </summary>
    [JsonProperty("compatible_with")]
    public List<PartSlotReference> CompatibleWith { get; set; } = new();

    /// <summary>
    /// Standard fittings this slot mounts by, e.g. "carb:4bbl" on a four-barrel carburettor's base: it goes on
    /// any slot that <see cref="Takes"/> one of them, whatever pack the other part is from. Empty for slots that
    /// only fit what their attach lines name.
    /// </summary>
    [JsonProperty("fits")]
    public List<string> Fits { get; set; } = new();

    /// <summary>Standard fittings this slot receives, e.g. "carb:4bbl" on a manifold's carburettor pad</summary>
    [JsonProperty("takes")]
    public List<string> Takes { get; set; } = new();

    public bool ShouldSerializeFits() => Fits.Count > 0;

    public bool ShouldSerializeTakes() => Takes.Count > 0;

    /// <summary>
    /// Position and rotation are read as [0], [1], [2] everywhere (the assembler, the layout, slot shifts): a
    /// pack.json that has fewer, more or none gets exactly three, the missing ones zero. Lists it says null for
    /// come out empty.
    /// </summary>
    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context)
    {
        Position = ThreeOf(Position);
        Rotation = ThreeOf(Rotation);

        // Null lists (or nulls in them) out of a pack.json: as in PartDefinition, empty
        Name ??= string.Empty;
        AttachesTo = PartDefinition.NonNull(AttachesTo);
        CompatibleWith = PartDefinition.NonNull(CompatibleWith);
        Fits = PartDefinition.NonNull(Fits);
        Takes = PartDefinition.NonNull(Takes);

        static float[] ThreeOf(float[]? values) =>
            values is { Length: 3 } ? values : new[] { At(values, 0), At(values, 1), At(values, 2) };

        static float At(float[]? values, int index) => values != null && index < values.Length ? values[index] : 0;
    }
}

public class PartStockReference
{
    /// <summary>Id of the mounted part; null when it is outside the converted parts</summary>
    [JsonProperty("part")]
    public string? Part { get; set; }

    /// <summary>The script's label for it, e.g. "103er crankshaft"</summary>
    [JsonProperty("name")]
    public string? Name { get; set; }

    /// <summary>True when the script only mounts it on some cars or at random</summary>
    [JsonProperty("conditional")]
    public bool Conditional { get; set; }

    /// <summary>Original reference, "rpk path#0xTYPEID", always set</summary>
    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;
}

public class PartSlotRule
{
    [JsonProperty("slot")]
    public int Slot { get; set; }

    /// <summary>The script's words when the slot is empty, e.g. "the engine is missing the alternator."</summary>
    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// True when the game holds the part to the rule itself (<c>EngineEvaluator</c>): a rule a pack adds of its own,
    /// such as the extra pads of a manifold whose stock script reads only one. A rule read out of a script is left to
    /// that script, which may ask it only on some cars.
    /// </summary>
    [JsonProperty("enforce", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool Enforce { get; set; }
}

public class PartSlotReference
{
    /// <summary>Id of the target part; null when the target is outside the converted parts (cars, base game)</summary>
    [JsonProperty("part")]
    public string? Part { get; set; }

    [JsonProperty("slot")]
    public int Slot { get; set; }

    /// <summary>Original reference, "rpk path#0xTYPEID", always set</summary>
    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;
}
