using System.IO;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// Where a racer's portrait is. A save keeps it as the mod's id and the path in the mod's folder
    /// ("mymod/racers/drv_001.png"), so it holds wherever the game is installed and whatever the mod's folder is
    /// called.
    /// </summary>
    public static class Portraits
    {
        /// <summary>A mod's portraitPath (next to its racers files) as the save keeps it; null when it leads out of the mod</summary>
        public static string? FromMod(ModSet mods, ModInfo mod, string? portraitPath)
        {
            if (string.IsNullOrWhiteSpace(portraitPath)) return null;

            var relative = portraitPath.TrimStart('/', '\\');
            var racersFolder = Path.Combine(mod.Folder, ContentTypes.Racers);
            if (!PathNames.TryCombineUnder(racersFolder, relative, out var full)) return null;

            return mod.Id + "/" + Path.GetRelativePath(mod.Folder, full).Replace('\\', '/');
        }

        /// <summary>The file a saved portrait path names; null when its mod is not installed or it would lead out of the mod</summary>
        public static string? Resolve(ModSet mods, string? saved)
        {
            if (string.IsNullOrWhiteSpace(saved)) return null;

            var trimmed = saved.TrimStart('/', '\\');
            var slash = trimmed.IndexOfAny(['/', '\\']);
            if (slash <= 0) return null;

            var id = trimmed[..slash];
            var mod = ModIds.IsBase(id) ? mods.Base : mods.Get(id);
            return mod != null && PathNames.TryCombineUnder(mod.Folder, trimmed[(slash + 1)..], out var path) ? path : null;
        }
    }
}
