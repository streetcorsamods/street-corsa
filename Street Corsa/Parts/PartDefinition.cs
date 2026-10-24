using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StreetCorsa.Parts;

/// <summary>
/// The files of a mod's parts folder: one .json per part anywhere under it (its model beside it), and directly in the
/// folder the catalog's own files, which are no parts
/// </summary>
public static class PartFiles
{
    /// <summary>
    /// Next to the parts: ids of parts that are gone, with the part that took their place. Saves and profiles made
    /// before keep working through it.
    /// </summary>
    public const string AliasesFileName = "part_aliases.json";

    /// <summary>
    /// Next to the parts: models for parts of any mod, the game's own too, by full id (or the start of one, ending in
    /// <c>*</c>), each a KN5 under this parts folder. The parts stay as they are; only how they look changes, the latest
    /// mod's model winning (a model pack, or a model whose licence keeps it out of the game's own content).
    /// </summary>
    public const string ModelsFileName = "models.json";

    /// <summary>
    /// The file a group of parts was before each part had a file of its own (a "parts" list in it): not read, and told. A
    /// part's own file of the name is a part as any other
    /// </summary>
    public const string OlderPackFileName = "pack.json";

    /// <summary>
    /// The files directly in a parts folder that are the catalog's, not parts. A new file of the catalog's goes here too,
    /// or every mod's copy of it is read as a part (PartsContentTests holds the core's parts folder to this)
    /// </summary>
    public static readonly IReadOnlySet<string> CatalogFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AliasesFileName, ModelsFileName, EngineBuild.FileName, EngineBuild.AliasesFileName, PartClasses.FileName,
        PartGroups.FileName, Logic.Fuels.FileName, PartRebuilds.FileName, EngineKits.FileName, SlotShifts.FileName, WorkOrder.FileName
    };
}

/// <summary>
/// One physical part: its 3D model plus the slots that say where it mounts and what mounts on it.
/// Geometry is in metres in the part's own space: left-handed, Y up.
/// </summary>
public class PartDefinition
{
    /// <summary>
    /// Unique id within its mod, e.g. "engines/chrysler/chrysler_340_v8_block": what saves and builds name the part by,
    /// whatever its file is called or where it sits
    /// </summary>
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

    /// <summary>The id as its file wrote it, without the mod's namespace</summary>
    [JsonIgnore]
    public string LocalId
    {
        get => string.IsNullOrEmpty(_localId) ? Id : _localId;
        set => _localId = value;
    }

    private string? _localId;

    /// <summary>The folder of the part's file: its model is found there</summary>
    [JsonIgnore]
    public string? Folder { get; set; }

    /// <summary>
    /// Where its <see cref="Model"/> is when another mod's <see cref="PartFiles.ModelsFileName"/> gives it one: that mod's
    /// parts folder; null for the model beside the part's own file
    /// </summary>
    [JsonIgnore]
    public string? ModelFolder { get; set; }

    /// <summary>
    /// The make the part is for, as the part shops name it ("mopar", "ford", "gm"): a make specialist stocks every part
    /// of its makes, a mod's too. Null for a part of no make (the universal-fit parts, the running gear).
    /// </summary>
    [JsonProperty("make", NullValueHandling = NullValueHandling.Ignore)]
    public string? Make { get; set; }

    /// <summary>
    /// Whose model the part's is, as its licence asks it named (a CC BY model's maker and licence): shown in the game's
    /// credits (<see cref="PartsCatalog.Credits"/>). None for a model of the mod's own.
    /// </summary>
    [JsonProperty("credit", NullValueHandling = NullValueHandling.Ignore)]
    public string? Credit { get; set; }

    /// <summary>A short name of the part's own (optional): the last part of its id when the file gives none</summary>
    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? LocalId[(LocalId.LastIndexOf('/') + 1)..] : _name;
        set => _name = value;
    }

    private string? _name;

    public bool ShouldSerializeName() => !string.IsNullOrEmpty(_name);

    /// <summary>The name the player reads, e.g. "Chrysler 340 V8 block" (docs/modding.md, "A part's name")</summary>
    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// What the part is: a class of <see cref="PartClasses"/> ("engine_block", "camshaft", "disc_brake"), which says how
    /// it works and what it counts as (a disc brake is a brake). Null for a part that is no kind of part: one with no
    /// specs or figures either is never sold or fitted (<see cref="IsRealPart"/>).
    /// </summary>
    [JsonProperty("class", NullValueHandling = NullValueHandling.Ignore)]
    public string? Class { get; set; }

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
    /// oil pan. Null leaves it to the words of the part's name.
    /// </summary>
    [JsonProperty("performance", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Performance { get; set; }

    /// <summary>
    /// What a job turns this part into, by the job's id (a job of <see cref="PartRebuilds"/> with <c>"becomes"</c>): an open
    /// carrier with its spiders welded is a welded carrier, <c>{ "weld_spiders": "running_gear/axles_gm/gm_10bolt_welded_low" }</c>.
    /// Null for a part no job turns into another.
    /// </summary>
    [JsonProperty("becomes", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, string>? Becomes { get; set; }

    /// <summary>
    /// A part made for speed or strength, never put on a car at the factory: no car's factory running gear is guessed
    /// from it, however close its figures (a Super Stock spring, a 90/10 shock)
    /// </summary>
    [JsonProperty("aftermarket", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool Aftermarket { get; set; }

    /// <summary>
    /// The cars a running gear part left the factory on (<see cref="Parts.CameIn"/>): what the factory running gear of a car
    /// is guessed from, where the mods' cars.json picks none. Null for a part no car came with (a spool, an alloy shaft).
    /// </summary>
    [JsonProperty("came_in", NullValueHandling = NullValueHandling.Ignore)]
    public CameIn? CameIn { get; set; }

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
    /// Whether the part is one at all: a fitting of the core's or the setting's (what other parts declare their fit against) has no
    /// class, no specs and no figures, and is never sold, fitted or shown on its own
    /// </summary>
    [JsonIgnore]
    public bool IsRealPart => Class != null || Specs.Count > 0 || ReadFigures != null;

    /// <summary>
    /// What the part is made like, in the figures its class reads (docs/modding.md, "Specs"): a block's "bore" and
    /// "cylinders", a camshaft's "intake_duration", a tyre's "friction". Plain metric units. Values are numbers, words
    /// or lists of numbers.
    /// </summary>
    [JsonProperty("specs")]
    public Dictionary<string, object> Specs { get; set; } = new();

    /// <summary>
    /// Engine figures of the part's own, in plain metric units, over what the engine's parts work out from their specs
    /// (<see cref="Logic.PartFigures"/>): "bore": 86, "intake_flow": { "times": 1.1 },
    /// "gears": [3.5, 2.1, 1.4, 1.0], "turbo": { "boost": 0.6, "full_boost_rpm": 4000 }. As written in the file;
    /// what is used is <see cref="ReadFigures"/>.
    /// </summary>
    [JsonProperty("figures", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, JToken>? Figures { get; set; }

    /// <summary><see cref="Figures"/> as the catalog read them, checked; null when the part has none that read</summary>
    [JsonIgnore]
    public Logic.PartFigureSet? ReadFigures { get; internal set; }

    /// <summary>Which of the part's slots takes what its class works with, e.g. "crankshaft" -> 8</summary>
    [JsonProperty("slot_roles")]
    public Dictionary<string, int> SlotRoles { get; set; } = new();

    /// <summary>Slots that must be filled for the part to work, with what the player reads when one is empty</summary>
    [JsonProperty("needs")]
    public List<PartSlotRule> Needs { get; set; } = new();

    /// <summary>
    /// A number among <see cref="Specs"/>. They come back from JSON as long or double, whichever the number
    /// happened to look like, so they are never to be read by type.
    /// </summary>
    public double Number(string spec, double fallback = 0) =>
        Specs.GetValueOrDefault(spec) is { } value and (long or int or double or float or decimal)
            ? System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
            : fallback;

    /// <summary>Model file next to the part's file; null for parts without usable geometry</summary>
    [JsonProperty("model")]
    public string? Model { get; set; }

    [JsonProperty("mass")]
    public float Mass { get; set; }

    [JsonProperty("slots")]
    public List<PartSlot> Slots { get; set; } = new();

    /// <summary>
    /// A part file that says null for a list (or has a null in one) gets it empty: the catalog's mating index is
    /// built once, and one null in it would fail every lookup after
    /// </summary>
    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context)
    {
        Specs ??= new();
        SlotRoles ??= new();
        Needs = NonNull(Needs);
        Slots = NonNull(Slots);
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

    /// <summary>
    /// The fittings this slot mounts by, e.g. "carb:4bbl" on a four-barrel carburettor's base: it goes on any slot that
    /// <see cref="Takes"/> one of them, whatever mod the other part is from. Empty for slots that only receive parts.
    /// </summary>
    [JsonProperty("fits")]
    public List<string> Fits { get; set; } = new();

    /// <summary>The fittings this slot receives, e.g. "carb:4bbl" on a manifold's carburettor pad</summary>
    [JsonProperty("takes")]
    public List<string> Takes { get; set; } = new();

    public bool ShouldSerializeFits() => Fits.Count > 0;

    public bool ShouldSerializeTakes() => Takes.Count > 0;

    /// <summary>
    /// Position and rotation are read as [0], [1], [2] everywhere (the assembler, the layout, slot shifts): a
    /// part file that has fewer, more or none gets exactly three, the missing ones zero. Lists it says null for
    /// come out empty.
    /// </summary>
    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context)
    {
        Position = ThreeOf(Position);
        Rotation = ThreeOf(Rotation);

        // Null lists (or nulls in them) out of a part file: as in PartDefinition, empty
        Name ??= string.Empty;
        Fits = PartDefinition.NonNull(Fits);
        Takes = PartDefinition.NonNull(Takes);

        static float[] ThreeOf(float[]? values) =>
            values is { Length: 3 } ? values : new[] { At(values, 0), At(values, 1), At(values, 2) };

        static float At(float[]? values, int index) => values != null && index < values.Length ? values[index] : 0;
    }
}

public class PartSlotRule
{
    [JsonProperty("slot")]
    public int Slot { get; set; }

    /// <summary>What the player reads when the slot is empty, e.g. "the engine is missing the alternator."</summary>
    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
}
