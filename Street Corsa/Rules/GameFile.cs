using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Rules;

/// <summary>
/// The game's own settings, from the <c>game.json</c> in each mod's folder, merged key by key in load order (the base
/// game's first, so a mod changes only what it names): what the game is called, the garage it is played from. Unlike
/// the rules (<see cref="RuleBook"/>) they are the program's, not a career's: a mod that changes them changes every
/// save. The player's own choices (the AC folder, the volumes) are not here but in their settings.json.
/// </summary>
public sealed class GameFile
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Rules");

    private static readonly Lazy<GameFile> InstalledFile = new(() => Load(ModSet.Installed));

    /// <summary>The settings of the mods next to the game, read once</summary>
    public static GameFile Installed => InstalledFile.Value;

    /// <summary>The game's name: the window's title</summary>
    public string Title { get; set; } = "Street Corsa";

    /// <summary>The garage the game is played from: a mod's <c>garages\&lt;id&gt;</c>, else the AC showroom of that id</summary>
    public string Garage { get; set; } = "garage";

    /// <summary>
    /// The language the files directly in the mods' <c>text</c> are written in ("en"): the one the game is played in
    /// when the player picks none. Another language is a folder of that name in <c>text</c>.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>How the game writes money, units, numbers and dates</summary>
    public GameLocale Locale { get; set; } = new();

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
            problems.Add($"{ModSet.GameFileName} could not be read ({ex.Message}); the base game's settings are used");
        }

        if (string.IsNullOrWhiteSpace(file.Title)) file.Title = "Street Corsa";
        if (string.IsNullOrWhiteSpace(file.Garage)) file.Garage = "garage";
        if (string.IsNullOrWhiteSpace(file.Language)) file.Language = "en";
        file.Locale = GameLocale.Checked(file.Locale, problems);

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

    /// <summary>The locale with what cannot be used told and put back to the base game's</summary>
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
