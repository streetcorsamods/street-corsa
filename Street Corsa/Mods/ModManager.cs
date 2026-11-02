using System.IO;

namespace StreetCorsa.Mods;

/// <summary>A mod in the Content folder, as the Mod Manager lists it</summary>
/// <param name="Folder">Its folder, switched off ('_') or not</param>
/// <param name="Group">The group folder it is in (settings, engines, addons...), "" for one at the top</param>
/// <param name="Info">Its mod.json; null when that cannot be read</param>
/// <param name="On">Not switched off: the game reads it</param>
/// <param name="Problems">What the game says of it when it reads it (left out, needs another mod...)</param>
/// <param name="Loaded">On and read by the game; false for one left out over a problem</param>
public sealed record InstalledMod(string Folder, string Group, ModInfo? Info, bool On, IReadOnlyList<string> Problems, bool Loaded)
{
    /// <summary>Its id; its folder's name, without the switch, when its mod.json gives none</summary>
    public string Id => string.IsNullOrWhiteSpace(Info?.Id) ? FolderName.TrimStart('_', '.').ToLowerInvariant() : Info.Id;

    public string FolderName => Path.GetFileName(Folder);

    public bool IsSetting => Info?.IsSetting == true;
}

/// <summary>
/// The Mod Manager's work on the Content folder: every mod listed (the core left out: it comes with the game), switched
/// off and on (a '_' before its folder's name, as <see cref="ModSet"/> reads it) and deleted. Plain BCL, like the rest
/// of Mods.
/// </summary>
public static class ModManager
{
    /// <summary>
    /// The mods under <paramref name="root"/>, switched-off ones too, by group then name: a folder with a mod.json at
    /// the top or one level down in a group. A switched-off group is left out whole (the player parked it by hand), and
    /// so are an install's own folders. Never throws on a mod that cannot be read.
    /// </summary>
    public static IReadOnlyList<InstalledMod> List(string root)
    {
        var found = new List<InstalledMod>();
        if (!Directory.Exists(root)) return found;

        // What the game makes of them: the problems it tells, the mods it reads
        var set = ModSet.Load(root, everything: true);
        // The mod.json the game read, by folder: read again only for the mods it left out
        var loaded = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in set.Mods) loaded[Path.GetFullPath(mod.Folder)] = mod;
        var problems = set.Problems;

        foreach (var top in Folders(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(top);
            if (ModIds.IsBase(name)) continue;

            if (HasManifest(top))
            {
                found.Add(Read(root, top, "", problems, loaded));
                continue;
            }

            if (IsOff(name)) continue;
            foreach (var mod in Folders(top).Where(HasManifest).Order(StringComparer.OrdinalIgnoreCase))
                found.Add(Read(root, mod, name, problems, loaded));
        }

        return found
            .OrderBy(m => m.IsSetting ? 0 : 1)
            .ThenBy(m => m.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Info?.Name is { Length: > 0 } n ? n : m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Switches <paramref name="mod"/> on or off: its folder renamed with or without the '_' before it. The folder it
    /// is now; throws <see cref="IOException"/> when that name is taken or the folder is in use.
    /// </summary>
    public static string Switch(InstalledMod mod, bool on)
    {
        if (mod.On == on) return mod.Folder;
        var parent = Path.GetDirectoryName(mod.Folder)!;
        var name = on ? mod.FolderName.TrimStart('_', '.') : "_" + mod.FolderName;
        if (name.Length == 0) throw new IOException($"{mod.FolderName} has no name to switch it on by");
        var target = Path.Combine(parent, name);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException($"{target} is there already");
        Directory.Move(mod.Folder, target);
        return target;
    }

    /// <summary>
    /// Deletes <paramref name="mod"/>'s folder and everything in it: moved out of the game's sight first (Content\_replaced),
    /// so a delete that fails halfway never leaves half a mod switched on. Throws <see cref="IOException"/> when the
    /// folder is in use and cannot be moved; one moved but not all deleted stays there, switched off.
    /// </summary>
    public static void Delete(InstalledMod mod)
    {
        if (ModIds.IsBase(mod.FolderName) && mod.Group.Length == 0) throw new IOException("The core comes with the game");
        if (!Directory.Exists(mod.Folder)) return;

        var parent = Path.GetDirectoryName(mod.Folder)!;
        var root = mod.Group.Length == 0 ? parent : Path.GetDirectoryName(parent)!;
        var aside = Path.Combine(root, ModImport.ReplacedFolder);
        Directory.CreateDirectory(aside);
        var gone = Path.Combine(aside, Guid.NewGuid().ToString("N"));
        Directory.Move(mod.Folder, gone);
        try
        {
            Directory.Delete(gone, recursive: true);
            if (!Directory.EnumerateFileSystemEntries(aside).Any()) Directory.Delete(aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Out of the game's sight already: what is left goes with the next install's clean-up or by hand
        }
    }

    private static InstalledMod Read(string root, string folder, string group, IReadOnlyList<string> problems, Dictionary<string, ModInfo> loaded)
    {
        var name = Path.GetFileName(folder);
        var own = new List<string>();
        var isLoaded = loaded.TryGetValue(Path.GetFullPath(folder), out var info);
        if (!isLoaded)
        {
            try
            {
                info = ModSet.ParseManifest(File.ReadAllText(Path.Combine(folder, ModSet.ManifestFileName)), out _);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
            {
                own.Add($"{ModSet.ManifestFileName}: {ex.Message}");
            }
        }

        var on = !IsOff(name);
        // The loader starts each line with the mod's place in the Content folder (ModSet.Label): a mod at the top by its
        // name, one in a group as engines\myengine, so two of one name in two groups never get each other's
        var label = ModSet.Label(root, folder) + ":";
        if (on)
            own.AddRange(problems
                .Where(p => p.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Substring(label.Length).Trim())
                .Distinct());

        return new InstalledMod(folder, group, info, on, own, on && isLoaded);
    }

    private static IEnumerable<string> Folders(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(f => !string.Equals(Path.GetFileName(f), ModImport.StagingFolder, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(Path.GetFileName(f), ModImport.ReplacedFolder, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool HasManifest(string folder) => File.Exists(Path.Combine(folder, ModSet.ManifestFileName));

    private static bool IsOff(string name) => name.StartsWith('_') || name.StartsWith('.');
}
