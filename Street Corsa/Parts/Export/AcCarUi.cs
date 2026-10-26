using System.IO;
using Newtonsoft.Json.Linq;

namespace StreetCorsa.Parts.Export;

/// <summary>
/// A car's ui/ui_car.json and its skins' ui_skin.json, read the lenient way of <see cref="AcUiJson"/>, the one way every
/// reader in the game and the tools reads them.
/// </summary>
public static class AcCarUi
{
    public static string PathIn(string carFolder) => Path.Combine(carFolder, "ui", "ui_car.json");

    /// <summary>The largest ui_car.json or ui_skin.json read: a real one is a few kilobytes</summary>
    public const long MaxUiJsonBytes = AcUiJson.MaxBytes;

    /// <summary>The parsed file, or null with the reason (missing, unreadable, larger than any real one, not an object)</summary>
    public static JObject? TryRead(string carFolder, out string? error) => AcUiJson.TryRead(PathIn(carFolder), out error);

    /// <summary>
    /// The name a skin's ui_skin.json gives it ("skinname"), read as leniently as ui_car.json; null when it has none, says
    /// "undefined", or cannot be read (missing, unreadable, larger than any real one)
    /// </summary>
    public static string? SkinName(string carFolder, string skin)
    {
        var name = GetString(AcUiJson.TryRead(Path.Combine(carFolder, "skins", skin, "ui_skin.json"), out _), "skinname")?.Trim();
        return string.IsNullOrEmpty(name) || name.Equals("undefined", StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    public static JObject? TryParse(string json, out string? error) => AcUiJson.TryParse(json, out error);

    /// <inheritdoc cref="AcUiJson.GetString"/>
    public static string? GetString(JToken? parent, string name) => AcUiJson.GetString(parent, name);

    /// <inheritdoc cref="AcUiJson.GetInt"/>
    public static int? GetInt(JToken? parent, string name) => AcUiJson.GetInt(parent, name);

    /// <inheritdoc cref="AcUiJson.GetStrings"/>
    public static IReadOnlyList<string> GetStrings(JToken? parent, string name) => AcUiJson.GetStrings(parent, name);

    /// <inheritdoc cref="AcUiJson.GetObject"/>
    public static JObject? GetObject(JToken? parent, string name) => AcUiJson.GetObject(parent, name);
}
