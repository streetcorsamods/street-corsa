using System.Text.Json;
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

        foreach (var problem in problems) Logger.Warning("Game settings: {Problem}", problem);
        ModProblems.Add("Game settings", problems);
        return file;
    }
}
