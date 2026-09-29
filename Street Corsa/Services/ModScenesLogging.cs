using StreetCorsa.Logging;

namespace StreetCorsa.Mods;

/// <summary>The game's way to load the mods' scenes: what is left out goes to the log (Mods itself stays plain BCL)</summary>
public static partial class ModScenes
{
    /// <param name="what">What the scenes are, for the log: "Streets"</param>
    /// <param name="load">The scene in a folder; null when it has none or it is broken (logged there)</param>
    /// <param name="idOf">The scene's own id, as its file has it</param>
    /// <param name="withId">The scene with its full id</param>
    public static IReadOnlyList<T> LoadAll<T>(ModSet mods, string type, string what, IAppLogger logger,
        Func<string, T?> load, Func<T, string> idOf, Func<T, string, T> withId) where T : class
    {
        var problems = new List<string>();
        var scenes = LoadAll(mods, type, problems, load, idOf, withId);
        foreach (var problem in problems) logger.Warning("{What}: {Problem}", what, problem);

        if (scenes.Count == 0) logger.Warning("No {What} in the mods could be loaded", what.ToLowerInvariant());
        return scenes;
    }
}
