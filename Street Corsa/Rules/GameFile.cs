using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Rules;

/// <summary>
/// The game's own settings, from the <c>game.json</c> in each mod's folder, merged key by key in load order (the core's
/// first, so a mod changes only what it names): what the game is called, the garage it is played from. Unlike
/// the rules (<see cref="RuleBook"/>) they are the program's, not a career's: a mod that changes them changes every
/// save. The player's own choices (the AC folder, the volumes) are not here but in their settings.json.
/// </summary>
public sealed class GameFile
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Rules");

    private static readonly ContentCache<GameFile> InstalledFile = new(() => Load(ModSet.Installed));

    /// <summary>The settings of the mods next to the game, read once</summary>
    public static GameFile Installed => InstalledFile.Value;

    /// <summary>The game's name: the window's title</summary>
    public string Title { get; set; } = "Street Corsa";

    /// <summary>The garage the game is played from: a mod's <c>garages\&lt;id&gt;</c>, else the AC showroom of that id</summary>
    public string Garage { get; set; } = "workshop";

    /// <summary>
    /// The language the files directly in the mods' <c>text</c> are written in ("en"): the one the game is played in
    /// when the player picks none. Another language is a folder of that name in <c>text</c>.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>How the game writes money, units, numbers and dates</summary>
    public GameLocale Locale { get; set; } = new();

    /// <summary>Which of the installed AC cars are in the game (every one, in the core)</summary>
    public GameCars Cars { get; set; } = new();

    /// <summary>Which of the installed AC tracks the game races on (every one, in the core)</summary>
    public GameTracks Tracks { get; set; } = new();

    public static GameFile Load(ModSet mods)
    {
        var problems = new List<string>();
        var merged = ModMerge.ModFiles(mods, ModSet.GameFileName, problems);
        var file = new GameFile();
        try
        {
            file = merged.Deserialize<GameFile>(RuleBook.Options) ?? file;
            RuleBook.CheckNames(merged, typeof(GameFile), "game", problems);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            problems.Add($"{ModSet.GameFileName} could not be read ({ex.Message}); the core's settings are used");
        }

        if (string.IsNullOrWhiteSpace(file.Title)) file.Title = "Street Corsa";
        if (string.IsNullOrWhiteSpace(file.Garage)) file.Garage = "workshop";
        if (string.IsNullOrWhiteSpace(file.Language)) file.Language = "en";
        file.Locale = GameLocale.Checked(file.Locale, problems);
        file.Cars = GameCars.Checked(file.Cars, problems, mods);
        file.Tracks = GameTracks.Checked(file.Tracks);

        foreach (var problem in problems) Logger.Warning("Game settings: {Problem}", problem);
        ModProblems.Add("Game settings", problems);
        return file;
    }
}

/// <summary>
/// game.json's <c>locale</c>: the culture numbers and dates are written in, the currency, and the units the game is
/// played in (the player may pick the others in Settings)
/// </summary>
public sealed class GameLocale
{
    /// <summary>A .NET culture name: "en-US", "ja-JP", "de-DE"</summary>
    public string Culture { get; set; } = "en-US";

    public MoneyStyle Money { get; set; } = new();

    /// <summary>"imperial" or "metric"</summary>
    public string Units { get; set; } = "imperial";

    /// <summary>The culture, or US English for one the machine does not know</summary>
    [JsonIgnore]
    public CultureInfo CultureInfo => Helpers.Locale.FindCulture(Culture) ?? CultureInfo.GetCultureInfo("en-US");

    /// <summary>The units, or imperial for a name that is neither</summary>
    [JsonIgnore]
    public UnitSystem UnitSystem => Helpers.Locale.ParseUnits(Units) ?? UnitSystem.Imperial;

    /// <summary>The locale with what cannot be used told and put back to the core's</summary>
    internal static GameLocale Checked(GameLocale? locale, List<string> problems)
    {
        locale ??= new GameLocale();
        locale.Money ??= new MoneyStyle();
        if (Helpers.Locale.FindCulture(locale.Culture) == null)
        {
            problems.Add($"locale.culture \"{locale.Culture}\" is no culture this machine knows; en-US is used");
            locale.Culture = "en-US";
        }

        if (Helpers.Locale.ParseUnits(locale.Units) == null)
        {
            problems.Add($"locale.units \"{locale.Units}\" is neither imperial nor metric; imperial is used");
            locale.Units = "imperial";
        }

        locale.Money.Symbol ??= string.Empty;
        if (locale.Money.Decimals is < 0 or > 6)
        {
            problems.Add($"locale.money.decimals {locale.Money.Decimals} is not 0 to 6; 0 is used");
            locale.Money.Decimals = 0;
        }

        return locale;
    }
}

/// <summary>
/// game.json's <c>cars</c>: which of the installed AC cars the game uses (the lots, the rivals, the events, the main
/// screen). A car is in when its model year is within <see cref="From"/> and <see cref="To"/>, its country is (or is in)
/// one of <see cref="Countries"/>, and its make one of <see cref="Makes"/>, each only where given; a car listed in
/// <see cref="Ids"/> is in whatever they say, one in <see cref="Never"/> never is. With <see cref="OnlyIds"/> the
/// game has the cars of <see cref="Ids"/> and no other. Countries and makes are as the mods' makes.json tells them.
/// </summary>
public sealed class GameCars
{
    /// <summary>The oldest and newest model year; a car whose data gives no year is out when either is given</summary>
    public int? From { get; set; }

    public int? To { get; set; }

    /// <summary>Countries or regions ("USA", "Europe"), any of their names</summary>
    public List<string> Countries { get; set; } = [];

    /// <summary>Ids of makes in makes.json</summary>
    public List<string> Makes { get; set; } = [];

    /// <summary>AC folder names of cars that are in, whatever the rest says</summary>
    public List<string> Ids { get; set; } = [];

    /// <summary>AC folder names of cars that are never in</summary>
    public List<string> Never { get; set; } = [];

    /// <summary>The cars of <see cref="Ids"/> and no other</summary>
    public bool OnlyIds { get; set; }

    /// <summary>Whether every installed car is in: nothing is filtered</summary>
    [JsonIgnore]
    public bool Everything => From == null && To == null && Countries.Count == 0 && Makes.Count == 0 && Never.Count == 0 && !OnlyIds;

    /// <summary>Whether the car is in the game</summary>
    public bool Allows(string id, string? brand, string? name, string? country, int? year, Parts.Cars.CarBook? book = null)
    {
        if (Never.Contains(id, StringComparer.OrdinalIgnoreCase)) return false;
        if (Ids.Contains(id, StringComparer.OrdinalIgnoreCase)) return true;
        if (OnlyIds) return false;

        if ((From != null || To != null) && year == null) return false;
        if (year < From || year > To) return false;

        if (Countries.Count == 0 && Makes.Count == 0) return true;
        book ??= Parts.Cars.CarBook.Current;
        var make = book.MakeOf(id, brand, name);
        if (Makes.Count > 0 && (make == null || !Makes.Contains(make, StringComparer.OrdinalIgnoreCase))) return false;
        if (Countries.Count == 0) return true;

        var where = !string.IsNullOrWhiteSpace(country) ? country : book.CountryOfMake(make);
        return where != null && Countries.Any(c => book.IsIn(where, c));
    }

    /// <param name="mods">The mods whose makes a make named must be among</param>
    internal static GameCars Checked(GameCars? cars, List<string> problems, ModSet mods)
    {
        cars ??= new GameCars();
        cars.Countries = (cars.Countries ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        cars.Makes = (cars.Makes ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        cars.Ids = (cars.Ids ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        cars.Never = (cars.Never ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        if (cars.From > cars.To)
        {
            problems.Add($"cars.from {cars.From} is after cars.to {cars.To}; taken the other way round");
            (cars.From, cars.To) = (cars.To, cars.From);
        }

        if (cars.OnlyIds && cars.Ids.Count == 0) problems.Add("cars.onlyIds is set and cars.ids names no car: the game has no cars");

        // A make no makes.json has (a typo, or one a mod that is not installed adds) lets no car in: told, as a game
        // left with no cars would otherwise just be empty
        if (cars.Makes.Count == 0) return cars;
        var makes = (ReferenceEquals(mods, ModSet.Installed) ? Parts.Cars.CarBook.Installed : Parts.Cars.CarBook.Load(mods)).Makes;
        foreach (var make in cars.Makes.Where(m => !makes.ContainsKey(m)))
            problems.Add($"cars.makes \"{make}\" is no make in makes.json: no car is of it");
        if (cars.Makes.All(m => !makes.ContainsKey(m))) problems.Add("cars.makes names no make there is: the game has no cars");
        return cars;
    }
}

/// <summary>
/// game.json's <c>tracks</c>: which of the installed AC tracks the game races on (the events, the diner, the street,
/// a free run). A track is in when its id is in <see cref="Ids"/> or it (or one of its layouts, which alone come in then) is
/// tagged with one of <see cref="Tags"/> in its ui_track.json, any case; one in <see cref="Never"/> never is. A drag strip is always in
/// (the strip's races and test-and-tune need one) unless it is in <see cref="Never"/>. With neither ids nor tags given,
/// every track is. When no road track installed is in, every one is (logged): a game must race somewhere.
/// </summary>
public sealed class GameTracks
{
    /// <summary>AC folder names of tracks that are in</summary>
    public List<string> Ids { get; set; } = [];

    /// <summary>Tags of ui_track.json (<c>touge</c>, <c>drift</c>, <c>mountain pass</c>): a track tagged with one is in</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>AC folder names of tracks that are never in</summary>
    public List<string> Never { get; set; } = [];

    /// <summary>Whether every installed track is in: nothing is filtered</summary>
    [JsonIgnore]
    public bool Everything => Ids.Count == 0 && Tags.Count == 0 && Never.Count == 0;

    /// <summary>Whether the track is in the game</summary>
    public bool Allows(Models.AC.TrackInfo track) => Kept(track) != null;

    /// <summary>
    /// The track as the game races it: none when it is out; a track in by its tags keeps only its layouts tagged (a
    /// layout's own tags, else the track's), so a circuit with one drift layout brings in that layout alone
    /// </summary>
    public Models.AC.TrackInfo? Kept(Models.AC.TrackInfo track)
    {
        if (Never.Contains(track.TrackId, StringComparer.OrdinalIgnoreCase)) return null;
        if (Ids.Count == 0 && Tags.Count == 0) return track;
        if (track.Type == Models.AC.TrackType.Dragstrip) return track;
        if (Ids.Contains(track.TrackId, StringComparer.OrdinalIgnoreCase)) return track;
        if (track.Configurations.Count == 0) return Tagged(track.Tags) ? track : null;

        var layouts = track.Configurations.Where(c => Tagged(c.Tags is { Count: > 0 } ? c.Tags : track.Tags)).ToList();
        if (layouts.Count == 0) return null;
        return layouts.Count == track.Configurations.Count ? track : track.WithLayouts(layouts);
    }

    // A ui_track.json may say null among its tags: no tag then
    private bool Tagged(List<string>? tags) =>
        (tags ?? []).Any(tag => tag != null && Tags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The tracks of <paramref name="tracks"/> in the game, in their order (as <see cref="Kept"/> keeps them); every one
    /// when none of the road tracks is (<paramref name="noneIn"/> true then)
    /// </summary>
    public List<Models.AC.TrackInfo> Of(IReadOnlyList<Models.AC.TrackInfo> tracks, out bool noneIn)
    {
        noneIn = false;
        if (Everything) return [.. tracks];
        var kept = tracks.Select(Kept).OfType<Models.AC.TrackInfo>().ToList();
        if (kept.Any(t => t.Type != Models.AC.TrackType.Dragstrip) || !tracks.Any(t => t.Type != Models.AC.TrackType.Dragstrip)) return kept;
        noneIn = true;
        return [.. tracks.Where(t => !Never.Contains(t.TrackId, StringComparer.OrdinalIgnoreCase))];
    }

    internal static GameTracks Checked(GameTracks? tracks)
    {
        tracks ??= new GameTracks();
        tracks.Ids = (tracks.Ids ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        tracks.Tags = (tracks.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
        tracks.Never = (tracks.Never ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        return tracks;
    }
}
