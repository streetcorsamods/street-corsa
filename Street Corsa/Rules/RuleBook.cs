using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Rules;

/// <summary>
/// The rules of a career: every figure and table the game plays by (what things cost, how long they take, how the
/// police and the rivals behave...), from the mods' <c>rules</c> folders. Each mod's files go over the mods' before it
/// key by key (<see cref="ModMerge"/>), so a mod changes one figure without repeating the rest; a figure no mod gives
/// is the base game's, as the classes here have it.
///
/// A career keeps the rules it was started with (<see cref="Models.GameState.GameState.RuleBook"/>): a mod updated or
/// taken out later does not change a game under way. A figure the save does not have (one the game gained since) comes
/// from the mods as they are. <see cref="Current"/> is the book of the game in play, or the mods' own with none.
///
/// A file's top level names the part of the book (<c>{ "police": { "dayChance": 0.1 } }</c>); a file may hold any
/// parts, and a part may be spread over files. Lists are taken whole, never added to.
/// </summary>
public sealed class RuleBook
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Rules");

    /// <summary>
    /// How the parts of the book are read: names whatever their case, comments and trailing commas allowed, and a part
    /// filled in over the base game's figures (a table given in part keeps the rest of its entries)
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(ModJson.Options)
    {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null
    };

    private static readonly Lazy<(JsonObject Merged, RuleBook Book)> InstalledBook = new(() =>
    {
        var problems = new List<string>();
        var merged = ModMerge.ContentFiles(ModSet.Installed, ContentTypes.Rules, problems);
        var book = Read(merged, problems);
        Report(problems);
        return (merged, book);
    });

    private static volatile RuleBook? _current;

    /// <summary>The rules of the mods next to the game, read once: a new career starts on them</summary>
    public static RuleBook Installed => InstalledBook.Value.Book;

    /// <summary>The rules of the game in play (<see cref="ForSave"/>), or the mods' own when none is</summary>
    public static RuleBook Current
    {
        get => _current ?? Installed;
        set => Use(value);
    }

    /// <summary>The book from now on (null: the mods' own)</summary>
    private static void Use(RuleBook? book) => _current = book;

    public StartRules Start { get; set; } = new();

    public DayRules Day { get; set; } = new();

    public TimeRules Time { get; set; } = new();

    public DifficultyRules Difficulty { get; set; } = new();

    public PoliceRules Police { get; set; } = new();

    public RivalRules Rivals { get; set; } = new();

    public DealerRules Dealers { get; set; } = new();

    public PartShopRules PartShops { get; set; } = new();

    public EconomyRules Economy { get; set; } = new();

    public MarketRules Market { get; set; } = new();

    public RepairRules Repairs { get; set; } = new();

    public ConditionRules Condition { get; set; } = new();

    public StreetRules Street { get; set; } = new();

    public WagerRules Wagers { get; set; } = new();

    public ReputationRules Reputation { get; set; } = new();

    public EventRules Events { get; set; } = new();

    public NewsRules News { get; set; } = new();

    public SimulationRules Simulation { get; set; } = new();

    public RaceRules Race { get; set; } = new();

    public WorldRules World { get; set; } = new();

    // The save Current was read for, so a game made current again is not read again
    private static WeakReference<Models.GameState.GameState>? _currentFor;

    /// <summary>
    /// The game in play from now on plays by its own rules (<see cref="ForSave"/>); null goes back to the mods'.
    /// Called as a game is loaded or started, before its screens read anything; a save from before rules were kept is
    /// given the mods' rules here, which it keeps from its next save.
    /// </summary>
    public static void PlayBy(Models.GameState.GameState? game)
    {
        if (game == null)
        {
            _currentFor = null;
            Use(null);
            return;
        }

        if (_currentFor != null && _currentFor.TryGetTarget(out var already) && ReferenceEquals(already, game)) return;

        if (string.IsNullOrWhiteSpace(game.RuleBook))
        {
            Logger.Information("The save {Save} has no rules of its own yet: it plays by the mods' from now on", game.SaveName);
            game.RuleBook = Installed.ToJson();
        }

        Current = ForSave(game.RuleBook);
        _currentFor = new WeakReference<Models.GameState.GameState>(game);
    }

    /// <summary>
    /// The book as a save keeps it: every figure written out, so the career plays on them whatever the mods do later
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// The rules a save plays by: the figures it was started with, over the mods' as they are now (for figures the
    /// game has gained since); the mods' own for a save from before rules were kept
    /// </summary>
    public static RuleBook ForSave(string? saved)
    {
        if (ModMerge.Parse(saved) is not { } kept) return Installed;

        var merged = ModMerge.Copy(InstalledBook.Value.Merged);
        ModMerge.Into(merged, kept);
        var problems = new List<string>();
        var book = Read(merged, problems);
        foreach (var problem in problems) Logger.Warning("Rules kept in the save: {Problem}", problem);
        return book;
    }

    /// <summary>
    /// The book from the merged files: each part read on its own over the base game's figures, so a part that does
    /// not read (a word where a number goes) is the base game's, told in <paramref name="problems"/>, and the rest stand.
    /// Names the book does not know (a typo) are told too.
    /// </summary>
    public static RuleBook Read(JsonObject merged, List<string> problems)
    {
        var book = new RuleBook();
        var parts = typeof(RuleBook).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, node) in merged)
        {
            if (name.StartsWith('_')) continue;
            if (!parts.TryGetValue(name, out var part))
            {
                problems.Add($"\"{name}\" is no part of the rules ({string.Join(", ", parts.Keys.Select(Camel))}); left out");
                continue;
            }

            if (node is not JsonObject figures)
            {
                problems.Add($"\"{name}\" is not an object of figures; the base game's are kept");
                continue;
            }

            try
            {
                // A fresh part holds the base game's figures; the files' are filled in over them, tables key by key
                var read = figures.Deserialize(part.PropertyType, Options);
                if (read == null) continue;

                CheckNames(figures, part.PropertyType, Camel(name), problems);
                part.SetValue(book, read);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
            {
                problems.Add($"\"{Camel(name)}\" could not be read ({ex.Message}); the base game's figures are kept");
            }
        }

        return book;
    }

    /// <summary>Names in the files that the book has no figure for, part by part (tables of names are anyone's)</summary>
    internal static void CheckNames(JsonObject json, Type type, string path, List<string> problems)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return;

        var known = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, node) in json)
        {
            if (name.StartsWith('_')) continue;
            if (!known.TryGetValue(name, out var property))
            {
                problems.Add($"{path}.{name} is no figure of the rules; left out");
                continue;
            }

            if (node is JsonObject inner && IsSection(property.PropertyType)) CheckNames(inner, property.PropertyType, $"{path}.{Camel(name)}", problems);
        }
    }

    private static bool IsSection(Type type) => type.IsClass && type != typeof(string) && !type.IsArray;

    private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static void Report(List<string> problems)
    {
        foreach (var problem in problems) Logger.Warning("Rules: {Problem}", problem);
        ModProblems.Add("Rules", problems);
    }
}
