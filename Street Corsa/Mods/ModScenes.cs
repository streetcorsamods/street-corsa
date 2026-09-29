using System.IO;

namespace StreetCorsa.Mods;

/// <summary>
/// The scenes of a content type in every mod, a folder each (the streets, the gas stations): each loaded, its own id
/// qualified with its mod's (a mod's ids are written without it), one with an id that is taken or already has a ':'
/// left out.
///
/// Plain BCL on purpose, like the rest of Mods: what goes wrong is a line in the problems, for the caller to log
/// (the game's overload that logs them is in Services).
/// </summary>
public static partial class ModScenes
{
    /// <param name="problems">A scene left out, one line each</param>
    /// <param name="load">The scene in a folder; null when it has none or it is broken (told there)</param>
    /// <param name="idOf">The scene's own id, as its file has it</param>
    /// <param name="withId">The scene with its full id</param>
    public static IReadOnlyList<T> LoadAll<T>(ModSet mods, string type, List<string> problems,
        Func<string, T?> load, Func<T, string> idOf, Func<T, string, T> withId) where T : class
    {
        var scenes = new List<T>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (mod, folder) in mods.SubFolders(type))
        {
            if (load(folder) is not { } scene) continue;

            var own = idOf(scene);
            var id = ModIds.IsQualified(own) ? null : ModIds.Qualify(mod.Id, own);
            if (id == null || !ids.Add(id))
            {
                problems.Add($"{mod.Id}/{Path.GetFileName(folder)} has the id {own}, which is taken or has a ':' in it; left out");
                continue;
            }

            scenes.Add(withId(scene, id));
        }

        return scenes;
    }
}
