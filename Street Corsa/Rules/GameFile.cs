using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public string Garage { get; set; } = StreetCorsa.Configuration.AppSettings.FallbackShowroomId;

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

    /// <summary>
    /// How the radio sounds in the car (the Cruise screen, the gas station, a race): <c>stereo</c>, the music as it is,
    /// or <c>am</c>, an old AM set's small speaker and its static
    /// </summary>
    public string CarRadio { get; set; } = StereoRadio;

    public const string StereoRadio = "stereo";
    public const string AmRadio = "am";

    [JsonIgnore]
    public bool AmCarRadio => CarRadio == AmRadio;

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
        if (string.IsNullOrWhiteSpace(file.Garage)) file.Garage = StreetCorsa.Configuration.AppSettings.FallbackShowroomId;
        if (string.IsNullOrWhiteSpace(file.Language)) file.Language = "en";
        file.Locale = GameLocale.Checked(file.Locale, problems);
        file.Cars = GameCars.Checked(file.Cars, problems, mods);
        // The core takes every car; a setting that says nothing of its cars gets every one too, sports cars of any age
        // among its lots and rivals: told, as a setting's author would otherwise find out only by playing
        if (mods.Setting != null && !SaysCars(mods.Setting))
            problems.Add($"{mods.Setting.Id}'s {ModSet.GameFileName} gives no \"cars\": every installed AC car is in the game");
        file.Tracks = GameTracks.Checked(file.Tracks);
        file.CarRadio = file.CarRadio?.Trim().ToLowerInvariant() ?? StereoRadio;
        if (file.CarRadio is not (StereoRadio or AmRadio))
        {
            problems.Add($"carRadio \"{file.CarRadio}\" is neither {StereoRadio} nor {AmRadio}; {StereoRadio}");
            file.CarRadio = StereoRadio;
        }

        foreach (var problem in problems) Logger.Warning("Game settings: {Problem}", problem);
        ModProblems.Add("Game settings", problems);
        return file;
    }

    // Whether the setting's own game.json names its cars, an add-on's not counted, the key in any case (as it is read)
    private static bool SaysCars(ModInfo setting)
    {
        try
        {
            var path = Path.Combine(setting.Folder, ModSet.GameFileName);
            return File.Exists(path)
                   && JsonNode.Parse(File.ReadAllText(path), ModMerge.NodeOptions, ModJson.DocumentOptions) is JsonObject own
                   && own.Any(p => p.Key.Equals("cars", StringComparison.OrdinalIgnoreCase) && p.Value != null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Not read: the merge has told it already
            return true;
        }
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
/// screen). A car is in when its model year is within <see cref="From"/> and <see cref="To"/>, its class (ui_car.json's)
/// one of <see cref="Classes"/> and none of <see cref="NeverClasses"/>, one of its tags one of <see cref="Tags"/> and
/// none of <see cref="NeverTags"/>, its country is (or is in) one of <see cref="Countries"/>, and its make one of
/// <see cref="Makes"/>, each only where given; a car listed in <see cref="Ids"/> is in whatever they say, one in
/// <see cref="Never"/> never is. With <see cref="OnlyIds"/> the game has the cars of <see cref="Ids"/> and no other.
/// Countries and makes are as the mods' makes.json tells them. Below those two lists, a mod's cars.json may say of a
/// car <c>"in": false</c> (out) or, the setting's own or an add-on's that requires a setting, <c>true</c> (in whatever
/// the rest says, <see cref="OnlyIds"/> aside), and give its model year (<see cref="Services.Catalog.CarYear"/>): an
/// add-on for one setting brings its cars into that game that way, one for any setting by their years.
/// </summary>
public sealed class GameCars
{
    /// <summary>The oldest and newest model year; a car whose year is not known is out when either is given</summary>
    public int? From { get; set; }

    public int? To { get; set; }

    /// <summary>Countries or regions ("USA", "Europe"), any of their names</summary>
    public List<string> Countries { get; set; } = [];

    /// <summary>Ids of makes in makes.json</summary>
    public List<string> Makes { get; set; } = [];

    /// <summary>Classes of ui_car.json ("street"), any case: a car of another class, or of none, is out</summary>
    public List<string> Classes { get; set; } = [];

    /// <summary>Classes of ui_car.json ("race"), any case: a car of one is out</summary>
    public List<string> NeverClasses { get; set; } = [];

    /// <summary>Tags of ui_car.json, any case: a car with none of them is out</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Tags of ui_car.json ("race", "singleseater"), any case: a car with one is out</summary>
    public List<string> NeverTags { get; set; } = [];

    /// <summary>AC folder names of cars that are in, whatever the rest says</summary>
    public List<string> Ids { get; set; } = [];

    /// <summary>AC folder names of cars that are never in</summary>
    public List<string> Never { get; set; } = [];

    /// <summary>The cars of <see cref="Ids"/> and no other</summary>
    public bool OnlyIds { get; set; }

    /// <summary>Whether a model year is asked: only then need a car's be worked out</summary>
    [JsonIgnore]
    public bool ByYear => From != null || To != null;

    /// <summary>Why the car is not in the game; null when it is</summary>
    public CarOut? WhyOut(GameCar car, Parts.Cars.CarBook? book = null)
    {
        book ??= Parts.Cars.CarBook.Current;
        if (Never.Contains(car.Id, StringComparer.OrdinalIgnoreCase)) return new(CarOutBecause.Never, "named in game.json's never");
        if (Ids.Contains(car.Id, StringComparer.OrdinalIgnoreCase)) return null;
        if (OnlyIds) return new(CarOutBecause.NotListed, "not in game.json's ids");
        switch (book.Car(car.Id)?.In)
        {
            case false: return new(CarOutBecause.Mod, "cars.json says \"in\": false");
            case true: return null;
        }

        if (ByYear && car.Year == null) return new(CarOutBecause.NoYear, "no model year known");
        if (car.Year < From || car.Year > To) return new(CarOutBecause.Year, $"model year {car.Year}");

        var carClass = car.Class?.Trim() ?? string.Empty;
        if (carClass.Length > 0 && NeverClasses.Contains(carClass, StringComparer.OrdinalIgnoreCase)) return new(CarOutBecause.Class, $"class \"{carClass}\"");
        if (Classes.Count > 0 && !Classes.Contains(carClass, StringComparer.OrdinalIgnoreCase))
            return new(CarOutBecause.Class, carClass.Length > 0 ? $"class \"{carClass}\"" : "no class");

        var tags = GameTags.Clean(car.Tags);
        if (tags.FirstOrDefault(t => NeverTags.Contains(t, StringComparer.OrdinalIgnoreCase)) is { } never) return new(CarOutBecause.Tag, $"tagged \"{never}\"");
        if (Tags.Count > 0 && !tags.Any(t => Tags.Contains(t, StringComparer.OrdinalIgnoreCase))) return new(CarOutBecause.Tag, "none of game.json's tags");

        if (Countries.Count == 0 && Makes.Count == 0) return null;
        var make = book.MakeOf(car.Id, car.Brand, car.Name);
        if (Makes.Count > 0 && (make == null || !Makes.Contains(make, StringComparer.OrdinalIgnoreCase)))
            return new(CarOutBecause.Make, make == null ? "no make known" : $"make {make}");
        if (Countries.Count == 0) return null;

        var where = !string.IsNullOrWhiteSpace(car.Country) ? car.Country : book.CountryOfMake(make);
        if (where == null) return new(CarOutBecause.Country, "no country known");
        return Countries.Any(c => book.IsIn(where, c)) ? null : new(CarOutBecause.Country, $"from {where}");
    }

    /// <param name="mods">The mods whose makes a make named must be among</param>
    internal static GameCars Checked(GameCars? cars, List<string> problems, ModSet mods)
    {
        cars ??= new GameCars();
        cars.Countries = (cars.Countries ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        cars.Makes = (cars.Makes ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        cars.Ids = (cars.Ids ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        cars.Never = (cars.Never ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        cars.Classes = GameTags.Clean(cars.Classes);
        cars.NeverClasses = GameTags.Clean(cars.NeverClasses);
        cars.Tags = GameTags.Clean(cars.Tags);
        cars.NeverTags = GameTags.Clean(cars.NeverTags);
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

/// <summary>The tags of a ui_car.json or ui_track.json as game.json's are matched against them</summary>
internal static class GameTags
{
    /// <summary>Trimmed, the empty left out: a ui_*.json may say null among its tags</summary>
    public static List<string> Clean(IEnumerable<string?>? tags) =>
        (tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
}

/// <summary>What <see cref="GameCars"/> asks of a car: its AC folder name, and what its ui_car.json (and the mods) say of it</summary>
public sealed record GameCar(string Id, string? Brand, string? Name, string? Country, int? Year, string? Class = null, IReadOnlyList<string>? Tags = null);

/// <summary>What keeps a car out of the game</summary>
public enum CarOutBecause
{
    Never,
    /// <summary>A mod's cars.json says <c>"in": false</c></summary>
    Mod,
    NotListed,
    NoYear,
    Year,
    Class,
    Tag,
    Make,
    Country
}

/// <summary>Why a car is not in the game: what keeps it out, and in words</summary>
public sealed record CarOut(CarOutBecause Because, string Text)
{
    public override string ToString() => Text;
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

    private bool Tagged(List<string>? tags) => GameTags.Clean(tags).Any(tag => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

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
