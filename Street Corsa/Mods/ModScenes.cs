using System.IO;
using StreetCorsa.Logging;

namespace StreetCorsa.Mods;

/// <summary>
/// The scenes of a content type in every mod, a folder each (the streets, the gas stations): each loaded, its own id
/// qualified with its mod's (a mod's ids are written without it), one with an id that is taken or already has a ':'
/// left out
/// </summary>
public static class ModScenes
{
    /// <param name="what">What the scenes are, for the log: "Streets"</param>
    /// <param name="load">The scene in a folder; null when it has none or it is broken (logged there)</param>
    /// <param name="idOf">The scene's own id, as its file has it</param>
    /// <param name="withId">The scene with its full id</param>
    public static IReadOnlyList<T> LoadAll<T>(ModSet mods, string type, string what, IAppLogger logger,
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
                logger.Warning("{What}: {Mod}/{Folder} has the id {Id}, which is taken or has a ':' in it; left out", what, mod.Id, Path.GetFileName(folder), own);
                continue;
            }

            scenes.Add(withId(scene, id));
        }

        if (scenes.Count == 0) logger.Warning("No {What} in the mods could be loaded", what.ToLowerInvariant());
        return scenes;
    }
}
