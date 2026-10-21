using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StreetCorsa.Mods;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The makes and the cars, from every mod's <c>cars</c> folder (<c>makes.json</c>, <c>cars.json</c>), merged key by key in
/// load order (<see cref="ModMerge"/>): a mod adds a make or a car, or changes one figure of one, without repeating the
/// rest. Lists (a make's words) are taken whole.
///
/// A make is told by the words of a car's brand, else of its name ("Chevrolet", "Camaro"); it gives the car its
/// corporate family (a Pontiac turns up with a Chevrolet V8, never a Hemi), its country, and what its badge does to the
/// price and to how common the car is. A make without a family has none: its words name no engine. The countries know
/// the names a ui_car.json writes for them ("Great Britain" is the UK) and the regions they are in ("Europe").
///
/// A car (by its AC folder name) may be given its make, factory engine, engine layout and how common it is: what is not
/// given is worked out from its data as before. Its price is always worked out: its body and its parts.
///
/// Plain BCL and linked into EngineBench: the problems are kept (<see cref="Problems"/>) for the game to tell.
/// </summary>
public sealed class CarBook
{
    public const string MakesFile = "makes.json";
    public const string CarsFile = "cars.json";

    /// <summary>The most any car price figure of the rules may be: well inside what money can hold</summary>
    public const decimal MaxCarPrice = 1_000_000_000m;

    private static readonly Lazy<CarBook> InstalledBook = new(() => Load(ModSet.Installed));
    private static volatile CarBook? _current;

    /// <summary>The makes and cars of the mods next to the game, read once</summary>
    public static CarBook Installed => InstalledBook.Value;

    /// <summary>The book in use: the installed one, or one a test sets</summary>
    public static CarBook Current
    {
        get => _current ?? Installed;
        set => _current = value;
    }

    /// <summary>A book with nothing in it: no make tells, no car is given anything</summary>
    public static CarBook Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase),
        new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase),
        new(StringComparer.OrdinalIgnoreCase), []);

    private readonly Dictionary<string, MakeEntry> _makes;
    private readonly Dictionary<string, CountryEntry> _countries;
    private readonly Dictionary<string, string> _countryNames;
    // Read by workers while the garage's placement mode gives a car its engine offset on the UI thread
    private readonly ConcurrentDictionary<string, CarEntry> _cars;
    private readonly Dictionary<string, string> _aliases;
    private readonly HashSet<string> _noise;

    // First word of each make's words -> the whole words (longest first) and the make
    private readonly Dictionary<string, List<(string[] Words, string Make)>> _wordsByFirst = new(StringComparer.OrdinalIgnoreCase);

    private CarBook(Dictionary<string, MakeEntry> makes, Dictionary<string, CountryEntry> countries, Dictionary<string, string> countryNames,
        Dictionary<string, CarEntry> cars, Dictionary<string, string> aliases, HashSet<string> noise, List<string> problems)
    {
        _makes = makes;
        _countries = countries;
        _countryNames = countryNames;
        _cars = new ConcurrentDictionary<string, CarEntry>(cars, cars.Comparer);
        _aliases = aliases;
        _noise = noise;
        Problems = problems;

        foreach (var (id, make) in makes)
        {
            foreach (var word in make.Words)
            {
                var words = Split(word).ToArray();
                if (words.Length == 0) continue;
                if (!_wordsByFirst.TryGetValue(words[0], out var list)) _wordsByFirst[words[0]] = list = new();
                if (list.FirstOrDefault(w => w.Words.SequenceEqual(words, StringComparer.OrdinalIgnoreCase)) is { Make: { } other } && other != id)
                {
                    problems.Add($"makes: \"{word}\" is a word of both {other} and {id}; it tells {id}");
                    list.RemoveAll(w => w.Words.SequenceEqual(words, StringComparer.OrdinalIgnoreCase));
                }

                list.Add((words, id));
                list.Sort((a, b) => b.Words.Length.CompareTo(a.Words.Length));
            }
        }
    }

    /// <summary>What could not be used, for the game to tell</summary>
    public IReadOnlyList<string> Problems { get; }

    public IReadOnlyDictionary<string, MakeEntry> Makes => _makes;

    public IReadOnlyDictionary<string, CarEntry> Cars => _cars;

    public MakeEntry? Make(string? id) => id != null && _makes.TryGetValue(id, out var make) ? make : null;

    /// <summary>What a car is given by the mods, by its AC folder name; null when nothing is</summary>
    public CarEntry? Car(string? carId) => carId != null && _cars.TryGetValue(carId, out var car) ? car : null;

    /// <summary>
    /// Gives a car its engine offset in this book (what the garage's placement mode moved it to, written to cars.json
    /// beside): the car gets an entry if it had none; zero takes the offset away
    /// </summary>
    public void SetEngineOffset(string carId, Vector3 offset)
    {
        _cars.GetOrAdd(carId, _ => new CarEntry()).EngineOffset = offset == Vector3.Zero ? null : [offset.X, offset.Y, offset.Z];
    }

    /// <summary>
    /// The make of a car: the one its entry names, else the first its brand's words tell, else its name's; null when none
    /// does
    /// </summary>
    public string? MakeOf(string? carId, string? brand, string? name)
    {
        if (Car(carId)?.Make is { Length: > 0 } given && _makes.ContainsKey(given)) return given;
        return FirstMake(brand, familyOnly: false) ?? FirstMake(name, familyOnly: false);
    }

    /// <summary>Family of whatever the text names, "" when no word of a make with a family gives it away. The first word that tells wins.</summary>
    public string FamilyOf(string? text) => FirstMake(text, familyOnly: true) is { } make ? _makes[make].Family! : string.Empty;

    /// <summary>
    /// The family of a car's make: its entry's make (one without a family gives none), else its brand's and name's
    /// words in turn
    /// </summary>
    public string FamilyOfCar(string? carId, string? brand, string? name)
    {
        if (Car(carId)?.Make is { Length: > 0 } given && Make(given) is { } make) return make.Family ?? string.Empty;
        return FamilyOf($"{brand} {name}");
    }

    private string? FirstMake(string? text, bool familyOnly)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var tokens = Split(text).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!_wordsByFirst.TryGetValue(tokens[i], out var candidates)) continue;
            foreach (var (words, make) in candidates)
            {
                if (familyOnly && _makes[make].Family is not { Length: > 0 }) continue;
                if (i + words.Length > tokens.Length) continue;

                var all = true;
                for (var w = 1; w < words.Length && all; w++) all = tokens[i + w].Equals(words[w], StringComparison.OrdinalIgnoreCase);
                if (all) return make;
            }
        }

        return null;
    }

    /// <summary>Lower-case words and numbers of a name that are worth comparing</summary>
    public IReadOnlySet<string> Tokens(string? text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text)) return tokens;

        foreach (var token in Split(text))
        {
            if (_noise.Contains(token)) continue;

            // Model years come as '69 or 1969; displacements are three digits and stay
            var isNumber = token.All(char.IsDigit);
            if (isNumber ? token.Length == 3 : token.Length >= 3) tokens.Add(_aliases.GetValueOrDefault(token, token).ToLowerInvariant());
        }

        return tokens;
    }

    /// <summary>The country a name stands for ("Great Britain", "American": its id, "UK", "USA"), else the name as it is</summary>
    public string Country(string name)
    {
        var trimmed = name.Trim();
        return _countryNames.GetValueOrDefault(trimmed, trimmed);
    }

    /// <summary>Whether the country is the place, or in it: a country, or a region one of the countries is in</summary>
    public bool IsIn(string country, string place)
    {
        var id = Country(country);
        var where = Country(place);
        if (id.Equals(where, StringComparison.OrdinalIgnoreCase)) return true;
        return _countries.TryGetValue(id, out var entry) && entry.Regions.Any(r => Country(r).Equals(where, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The country of a make, null for one that names none</summary>
    public string? CountryOfMake(string? make) => Make(make)?.Country is { Length: > 0 } country ? Country(country) : null;

    private static IEnumerable<string> Split(string text) =>
        text.Split(new[] { ' ', '-', '_', '/', '\'', '(', ')', ',', '.', '´' }, StringSplitOptions.RemoveEmptyEntries);

    public static CarBook Load(ModSet mods)
    {
        var problems = new List<string>();
        return Read(ModMerge.ContentFiles(mods, ContentTypes.Cars, problems, ResolveEngines), problems);
    }

    /// <summary>
    /// A mod's cars' engines as the builds they mean, before the mods are merged and it is no longer known whose they
    /// were: an id written short is the mod's own build when it has one of that id (its parts\engine_builds.json or its
    /// mod.json's builds), else the base game's, as a mod's builds name their parts
    /// </summary>
    private static void ResolveEngines(ModInfo mod, JsonObject read)
    {
        if (mod.IsBase || read["cars"] is not JsonObject cars) return;

        HashSet<string>? own = null;
        foreach (var (_, entry) in cars)
        {
            if (entry is not JsonObject car || car["engine"] is not JsonValue value || !value.TryGetValue<string>(out var engine)
                || string.IsNullOrWhiteSpace(engine)) continue;

            own ??= OwnBuilds(mod);
            car["engine"] = ModIds.Resolve(mod.Id, engine.Trim(), own.Contains);
        }
    }

    /// <summary>The full ids of the builds a mod gives; a file or section that does not read gives none (the parts catalog tells it)</summary>
    private static HashSet<string> OwnBuilds(ModInfo mod)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var file = mod.Dir(ContentTypes.Parts) is { } parts ? Path.Combine(parts, EngineBuild.FileName) : null;
        try
        {
            if (file != null && File.Exists(file)) Add(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        Add(mod.Section(ModSections.Builds));
        return ids;

        void Add(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                if (JsonNode.Parse(json, ModMerge.NodeOptions, ModJson.DocumentOptions) is not JsonArray builds) return;
                foreach (var build in builds)
                {
                    if (build is JsonObject fields && fields["id"] is JsonValue id && id.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                        ids.Add(ModIds.Qualify(mod.Id, text.Trim()));
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
            }
        }
    }

    /// <summary>The book from the merged files; what does not read is told in <paramref name="problems"/> and left out</summary>
    public static CarBook Read(JsonObject merged, List<string> problems)
    {
        var makes = ReadTable<MakeEntry>(merged, "makes", problems);
        var countries = ReadTable<CountryEntry>(merged, "countries", problems);
        var cars = ReadTable<CarEntry>(merged, "cars", problems);
        var words = Read<WordsEntry>(merged, "words", problems) ?? new WordsEntry();

        foreach (var (name, _) in merged)
        {
            if (name.StartsWith('_') || name.Equals("makes", StringComparison.OrdinalIgnoreCase) || name.Equals("countries", StringComparison.OrdinalIgnoreCase)
                || name.Equals("cars", StringComparison.OrdinalIgnoreCase) || name.Equals("words", StringComparison.OrdinalIgnoreCase)) continue;
            problems.Add($"cars: \"{name}\" is none of makes, countries, cars, words; left out");
        }

        // Prices are the game's own (the car's body and parts): a mod's older figure is told, not taken
        foreach (var table in new[] { "makes", "cars" })
        {
            if (merged[table] is not JsonObject entries) continue;
            foreach (var (id, entry) in entries)
            {
                if (entry is JsonObject fields && fields.Any(f => f.Key.Equals("price", StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"{table}.{id}.price is no longer read: a car costs what its body and parts do");
            }
        }

        foreach (var (id, make) in makes)
        {
            // A mod may write null for a list: as good as none
            make.Words = (make.Words ?? []).Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
            if (!float.IsFinite(make.Common))
            {
                problems.Add($"makes.{id}.common is not a number; 0 is used");
                make.Common = 0;
            }

            if (make.Family != null && make.Family.Trim().Length == 0) make.Family = null;
        }

        var countryNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, country) in countries)
        {
            country.Names = (country.Names ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            country.Regions = (country.Regions ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            foreach (var name in country.Names)
            {
                if (countryNames.TryGetValue(name.Trim(), out var other) && !other.Equals(id, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"countries: \"{name}\" is a name of both {other} and {id}; it stands for {id}");
                countryNames[name.Trim()] = id;
            }
        }

        foreach (var (id, car) in cars)
        {
            if (car.Make is { Length: > 0 } make && !makes.ContainsKey(make))
            {
                problems.Add($"cars.{id}.make \"{make}\" is no make; its brand tells");
                car.Make = null;
            }

            if (car.Layout is { Length: > 0 } layout && EngineLayouts.Parse(layout) == null)
            {
                problems.Add($"cars.{id}.layout \"{layout}\" is none of front, mid, rear, front_transverse, mid_transverse; guessed from the car");
                car.Layout = null;
            }

            if (car.Common is { } common && (!float.IsFinite(common) || common is < 0 or > 1))
            {
                problems.Add($"cars.{id}.common {common} is not 0 to 1; worked out from the car");
                car.Common = null;
            }

            if (car.RunningGear?.Ratio is { } ratio && (!double.IsFinite(ratio) || ratio is < 1 or > 10))
            {
                problems.Add($"cars.{id}.running_gear.ratio {ratio} is not 1 to 10; guessed from the car");
                car.RunningGear.Ratio = null;
            }
        }

        var aliases = new Dictionary<string, string>((words.Aliases ?? []).Where(a => !string.IsNullOrWhiteSpace(a.Value)), StringComparer.OrdinalIgnoreCase);
        var noise = new HashSet<string>((words.Noise ?? []).Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
        return new CarBook(makes, countries, countryNames, cars, aliases, noise, problems);
    }

    private static Dictionary<string, T> ReadTable<T>(JsonObject merged, string name, List<string> problems) where T : class
    {
        var table = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (merged[name] is not { } node) return table;
        if (node is not JsonObject entries)
        {
            problems.Add($"cars: \"{name}\" is not an object of entries by id; left out");
            return table;
        }

        foreach (var (id, entry) in entries)
        {
            if (id.StartsWith('_')) continue;
            if (entry is not JsonObject)
            {
                problems.Add($"{name}.{id} is not an object; left out");
                continue;
            }

            try
            {
                if (entry.Deserialize<T>(ModJson.Options) is { } read) table[id] = read;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
            {
                problems.Add($"{name}.{id} could not be read ({ex.Message}); left out");
            }
        }

        return table;
    }

    private static T? Read<T>(JsonObject merged, string name, List<string> problems) where T : class
    {
        if (merged[name] is not { } node) return null;
        try
        {
            return node.Deserialize<T>(ModJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            problems.Add($"cars: \"{name}\" could not be read ({ex.Message}); left out");
            return null;
        }
    }

    private sealed class WordsEntry
    {
        public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Noise { get; set; } = new();
    }
}

/// <summary>A make in makes.json, by its id</summary>
public sealed class MakeEntry
{
    /// <summary>The corporate family its cars and engines belong to ("gm"); none: its words name no engine</summary>
    public string? Family { get; set; }

    /// <summary>A country's id or one of its names</summary>
    public string? Country { get; set; }

    /// <summary>Words of a brand or a car's name that tell the make, any case; a word may be two ("alfa romeo")</summary>
    public List<string> Words { get; set; } = new();

    /// <summary>What the badge does to how common a car is, added to it (0 rare to 1 common): -0.3 rarer</summary>
    public float Common { get; set; }
}

/// <summary>A country in makes.json, by its id ("USA")</summary>
public sealed class CountryEntry
{
    /// <summary>Other names for it, any case: "American", "United States"</summary>
    public List<string> Names { get; set; } = new();

    /// <summary>Places it is part of: "Europe"</summary>
    public List<string> Regions { get; set; } = new();
}

/// <summary>What the mods give an AC car in cars.json, by its folder name; what is left out is worked out from the car</summary>
public sealed class CarEntry
{
    /// <summary>The id of a make in makes.json</summary>
    public string? Make { get; set; }

    /// <summary>The engine build it leaves the factory with (engine_builds.json)</summary>
    public string? Engine { get; set; }

    /// <summary>"front", "mid", "rear", "front_transverse" or "mid_transverse"</summary>
    public string? Layout { get; set; }

    /// <summary>How often it turns up for sale, 0 rare to 1 common</summary>
    public float? Common { get; set; }

    /// <summary>
    /// Where its engine sits from where the hubs and the layout put it, x y z metres in the car's space (moved by hand in
    /// the garage's placement mode, F5 on the engine block)
    /// </summary>
    [JsonPropertyName("engine_offset")]
    public float[]? EngineOffset { get; set; }

    /// <summary>The running gear it leaves the factory with, as far as it is given: the rest is guessed from its data</summary>
    [JsonPropertyName("running_gear")]
    public RunningGearPicks? RunningGear { get; set; }

    public EngineLayout? EngineLayout => EngineLayouts.Parse(Layout);

    /// <summary>The engine offset; zero when none is given or it is not three numbers within a metre</summary>
    public Vector3 EngineShift =>
        EngineOffset is { Length: 3 } o && o.All(v => float.IsFinite(v) && Math.Abs(v) <= MaxEngineOffset) ? new Vector3(o[0], o[1], o[2]) : Vector3.Zero;

    /// <summary>The furthest an engine is moved from its place, metres</summary>
    public const float MaxEngineOffset = 1f;
}

/// <summary>
/// A car's factory running gear as cars.json gives it (<c>"running_gear"</c>), part ids as engine builds are given; what is
/// left out, or names no part of the catalog, is guessed (<see cref="RunningGearGuess"/>)
/// </summary>
public sealed class RunningGearPicks
{
    /// <summary>The rear axle housing</summary>
    [JsonPropertyName("axle")]
    public string? Axle { get; set; }

    /// <summary>The differential carrier</summary>
    [JsonPropertyName("diff")]
    public string? Diff { get; set; }

    /// <summary>The final drive: the ring and pinion of this ratio that goes on the carrier (the nearest there is)</summary>
    [JsonPropertyName("ratio")]
    public double? Ratio { get; set; }

    /// <summary>The axle shafts</summary>
    [JsonPropertyName("shafts")]
    public string? Shafts { get; set; }

    /// <summary>The brakes of each axle (a drum brake gets its shoes and wheel cylinder as the guess gives them)</summary>
    [JsonPropertyName("brakes_front")]
    public string? BrakesFront { get; set; }

    [JsonPropertyName("brakes_rear")]
    public string? BrakesRear { get; set; }

    /// <summary>The springs of each axle: a coil, a leaf spring, a torsion bar</summary>
    [JsonPropertyName("springs_front")]
    public string? SpringsFront { get; set; }

    [JsonPropertyName("springs_rear")]
    public string? SpringsRear { get; set; }

    public string? Brakes(bool front) => front ? BrakesFront : BrakesRear;

    public string? Springs(bool front) => front ? SpringsFront : SpringsRear;

    public bool IsEmpty => string.IsNullOrEmpty(Axle) && string.IsNullOrEmpty(Diff) && Ratio == null && string.IsNullOrEmpty(Shafts) &&
                           string.IsNullOrEmpty(BrakesFront) && string.IsNullOrEmpty(BrakesRear) && string.IsNullOrEmpty(SpringsFront) && string.IsNullOrEmpty(SpringsRear);

    public RunningGearPicks Copy() => new()
    {
        Axle = Axle, Diff = Diff, Ratio = Ratio, Shafts = Shafts, BrakesFront = BrakesFront, BrakesRear = BrakesRear,
        SpringsFront = SpringsFront, SpringsRear = SpringsRear
    };
}
