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
    /// What <see cref="PlayBy"/> was last given, to go back to with <see cref="BookInPlay.Restore"/>: a game being
    /// opened plays by its rules from the start, and one that is not opened after all (backed out of, failed) must
    /// not leave them to the game still in play
    /// </summary>
    public static BookInPlay InPlay() => new(_current, _currentFor);

    /// <summary>The book in play at one moment, and the game it was for</summary>
    public readonly struct BookInPlay
    {
        private readonly RuleBook? _book;
        private readonly WeakReference<Models.GameState.GameState>? _for;

        internal BookInPlay(RuleBook? book, WeakReference<Models.GameState.GameState>? game)
        {
            _book = book;
            _for = game;
        }

        public void Restore()
        {
            _currentFor = _for;
            Use(_book);
        }
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

        // The first saves to keep their rules wrote out the day's hours as worked out, which are no figures to read
        if (kept["day"] is JsonObject day)
        {
            day.Remove("firstHour");
            day.Remove("lastHour");
        }

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
    /// Names the book does not know (a typo) are told too, a null is the base game's figure, and a least and a most
    /// given the wrong way round are turned round.
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
                DropNulls(figures, Camel(name), problems);

                // A fresh part holds the base game's figures; the files' are filled in over them, tables key by key
                var read = figures.Deserialize(part.PropertyType, Options);
                if (read == null) continue;

                if (read is PartShopRules shops) shops.NewKindsFromOtherKind(figures);
                CheckNames(figures, part.PropertyType, Camel(name), problems);
                part.SetValue(book, read);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
            {
                problems.Add($"\"{Camel(name)}\" could not be read ({ex.Message}); the base game's figures are kept");
            }
        }

        book.HoldRanges(problems);
        return book;
    }

    /// <summary>
    /// Takes out every null in the figures, told: a null figure or table is the base game's (a table read as null would
    /// stop the game where it is used), a null in a list is no entry
    /// </summary>
    private static void DropNulls(JsonNode node, string path, List<string> problems)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    if (value == null)
                    {
                        obj.Remove(name);
                        if (!name.StartsWith('_')) problems.Add($"{path}.{name} is null; the base game's is kept");
                    }
                    else
                    {
                        DropNulls(value, $"{path}.{name}", problems);
                    }
                }

                break;
            case JsonArray array:
                for (var i = array.Count - 1; i >= 0; i--)
                {
                    if (array[i] is { } item)
                    {
                        DropNulls(item, $"{path}[{i}]", problems);
                        continue;
                    }

                    array.RemoveAt(i);
                    problems.Add($"{path}[{i}] is null; left out of the list");
                }

                break;
        }
    }

    /// <summary>
    /// Each least and most the game draws between (or holds a figure to) in the right order: given the wrong way round
    /// (a mod that lowers the most below the base game's least) they are turned round, told
    /// </summary>
    private void HoldRanges(List<string> problems)
    {
        Range(Market, "market", nameof(MarketRules.StockLow), nameof(MarketRules.StockHigh), problems);
        Range(Market, "market", nameof(MarketRules.MinCondition), nameof(MarketRules.MaxCondition), problems);
        Range(Market, "market", nameof(MarketRules.MinMileage), nameof(MarketRules.MaxMileage), problems);
        Range(Street, "street", nameof(StreetRules.ShortestWait), nameof(StreetRules.LongestWait), problems);
        Range(Economy, "economy", nameof(EconomyRules.PartAdsLow), nameof(EconomyRules.PartAdsHigh), problems);
        Range(Economy, "economy", nameof(EconomyRules.PartAdMinWear), nameof(EconomyRules.PartAdMaxWear), problems);
        Range(PartShops, "partShops", nameof(PartShopRules.JunkyardMinWear), nameof(PartShopRules.JunkyardMaxWear), problems);
        Range(PartShops, "partShops", nameof(PartShopRules.HaggleMinCut), nameof(PartShopRules.HaggleMaxCut), problems);
        foreach (var (path, rules) in PartShops.Kinds.Select(k => ($"partShops.kinds.{k.Key}", k.Value))
                     .Append(("partShops.otherKind", PartShops.OtherKind)))
        {
            if (rules == null) continue;
            Range(rules, path, nameof(PartShopKindRules.StockLow), nameof(PartShopKindRules.StockHigh), problems);
            Range(rules, path, nameof(PartShopKindRules.PerShelfLow), nameof(PartShopKindRules.PerShelfHigh), problems);
        }

        Range(Rivals, "rivals", nameof(RivalRules.MinCashShare), nameof(RivalRules.MaxCashShare), problems);
        Range(Rivals, "rivals", nameof(RivalRules.MinCashInjection), nameof(RivalRules.MaxCashInjection), problems);
        Range(Rivals, "rivals", nameof(RivalRules.MinAskShare), nameof(RivalRules.MaxAskShare), problems);
        Range(Rivals.Money, "rivals.money", nameof(RivalMoneyRules.BaseLow), nameof(RivalMoneyRules.BaseHigh), problems);
        for (var i = 0; i < Rivals.Money.ByAge.Length; i++)
            Range(Rivals.Money.ByAge[i], $"rivals.money.byAge[{i}]", nameof(RivalMoneyByAge.Low), nameof(RivalMoneyByAge.High), problems);
        Range(World, "world", nameof(WorldRules.WindMinKmh), nameof(WorldRules.WindMaxKmh), problems);
        Range(World, "world", nameof(WorldRules.DawnHour), nameof(WorldRules.DayFromHour), problems);
    }

    private static void Range(object figures, string path, string least, string most, List<string> problems)
    {
        var type = figures.GetType();
        var low = type.GetProperty(least)!;
        var high = type.GetProperty(most)!;
        var lowValue = low.GetValue(figures)!;
        var highValue = high.GetValue(figures)!;
        if (((IComparable)lowValue).CompareTo(highValue) <= 0) return;

        low.SetValue(figures, highValue);
        high.SetValue(figures, lowValue);
        problems.Add(FormattableString.Invariant(
            $"{path}.{Camel(least)} ({lowValue}) is over {path}.{Camel(most)} ({highValue}); taken the other way round"));
    }

    /// <summary>
    /// Names in the files that the book has no figure for, part by part, the entries of its lists and of its tables of
    /// figures included (the names of a table are anyone's: a kind of shop, an action)
    /// </summary>
    internal static void CheckNames(JsonObject json, Type type, string path, List<string> problems)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var valueType = type.GetGenericArguments()[1];
            if (!IsSection(valueType)) return;
            foreach (var (name, node) in json)
            {
                if (!name.StartsWith('_') && node is JsonObject entry) CheckNames(entry, valueType, $"{path}.{name}", problems);
            }

            return;
        }

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

            var inPath = $"{path}.{Camel(name)}";
            var itemType = property.PropertyType.IsArray ? property.PropertyType.GetElementType()! : null;
            if (node is JsonObject inner && IsSection(property.PropertyType))
            {
                CheckNames(inner, property.PropertyType, inPath, problems);
            }
            else if (node is JsonArray list && itemType != null && IsSection(itemType))
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] is JsonObject entry) CheckNames(entry, itemType, $"{inPath}[{i}]", problems);
                }
            }
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
