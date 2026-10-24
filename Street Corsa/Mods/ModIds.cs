using System.Text.RegularExpressions;

namespace StreetCorsa.Mods;

/// <summary>
/// Content ids across mods. The core's ids have no namespace ("engines/stock/battery"), and neither have a setting's
/// (<see cref="MarkSetting"/>): a game plays the core and one setting, and the game's own ids read the same whatever
/// mod holds them. Every add-on's ids get its id in front ("mymod:engines/x"), so two mods can never take each
/// other's ids by accident. In a mod's own files an id is written without its namespace, and a reference to another
/// entry finds the mod's own first, then the game's (the core's or the setting's); one written in full ("othermod:x",
/// "streetrod:x") means exactly that entry.
///
/// Plain BCL on purpose: the tools compile it by link.
/// </summary>
public static partial class ModIds
{
    /// <summary>The core's mod id: its folder under Content, and the namespace its ids go without</summary>
    public const string BaseId = "streetcorsa";

    public const char Separator = ':';

    // "car:<id>" already names an installed AC car's own engine sound
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "car" };

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,39}$")]
    private static partial Regex ModIdPattern();

    /// <summary>Lower case letters, digits, '_' and '-', at most 40, not a reserved word</summary>
    public static bool IsValidModId(string? id) => id != null && ModIdPattern().IsMatch(id) && !Reserved.Contains(id);

    public static bool IsBase(string? modId) => string.Equals(modId, BaseId, StringComparison.OrdinalIgnoreCase);

    // The mods read as settings: their ids go bare, as the core's do. Only one setting plays at a time, so they never
    // meet; a mod is a setting or not for as long as the game runs
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> SettingIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Marks a mod as a setting: its ids go without a namespace from now on (<see cref="ModSet"/> marks each it reads)</summary>
    public static void MarkSetting(string modId) => SettingIds[modId] = true;

    /// <summary>The mod's ids go without a namespace: the core's and a setting's</summary>
    public static bool GoesBare(string? modId) => IsBase(modId) || (modId != null && SettingIds.ContainsKey(modId));

    /// <summary>An id as a mod wrote it (no namespace) as the game knows it</summary>
    public static string Qualify(string modId, string localId) => GoesBare(modId) ? localId : modId + Separator + localId;

    /// <summary>True when an id is written with its namespace</summary>
    public static bool IsQualified(string id) => id.IndexOf(Separator) > 0;

    /// <summary>The mod an id belongs to: its namespace, or the core for one without (or the setting played: a bare id is the game's own)</summary>
    public static string ModOf(string id)
    {
        var at = id.IndexOf(Separator);
        return at > 0 ? id[..at] : BaseId;
    }

    /// <summary>The id without its namespace, as its mod wrote it</summary>
    public static string Local(string id)
    {
        var at = id.IndexOf(Separator);
        return at > 0 ? id[(at + 1)..] : id;
    }

    /// <summary>A full id as the game keeps it: the core's or a setting's namespace written out is dropped</summary>
    public static string Normalize(string id) =>
        IsQualified(id) && GoesBare(ModOf(id)) ? Local(id) : id;

    /// <summary>
    /// A reference in a mod's file to the id it means: one written in full is that id; otherwise the mod's own
    /// entry when <paramref name="exists"/> knows it, else the game's (the core's or the setting's).
    /// </summary>
    public static string Resolve(string modId, string reference, Func<string, bool> exists)
    {
        if (IsQualified(reference)) return Normalize(reference);
        if (GoesBare(modId)) return reference;

        var own = Qualify(modId, reference);
        return exists(own) ? own : reference;
    }
}
