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
        var loaded = new HashSet<string>(set.Mods.Select(m => Path.GetFullPath(m.Folder)), StringComparer.OrdinalIgnoreCase);

        foreach (var top in Folders(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(top);
            if (ModIds.IsBase(name)) continue;

            if (HasManifest(top))
            {
                found.Add(Read(top, "", set.Problems, loaded));
                continue;
            }

            if (IsOff(name)) continue;
            foreach (var mod in Folders(top).Where(HasManifest).Order(StringComparer.OrdinalIgnoreCase))
                found.Add(Read(mod, name, set.Problems, loaded));
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

    /// <summary>Deletes <paramref name="mod"/>'s folder and everything in it; throws <see cref="IOException"/> on one in use</summary>
    public static void Delete(InstalledMod mod)
    {
        if (ModIds.IsBase(mod.FolderName) && mod.Group.Length == 0) throw new IOException("The core comes with the game");
        if (Directory.Exists(mod.Folder)) Directory.Delete(mod.Folder, recursive: true);
    }

    private static InstalledMod Read(string folder, string group, IReadOnlyList<string> problems, HashSet<string> loaded)
    {
        var name = Path.GetFileName(folder);
        ModInfo? info = null;
        var own = new List<string>();
        try
        {
            info = ModSet.ParseManifest(File.ReadAllText(Path.Combine(folder, ModSet.ManifestFileName)), out _);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            own.Add($"{ModSet.ManifestFileName}: {ex.Message}");
        }

        var on = !IsOff(name);
        // The loader names a mod by its folder (or its place in the Content folder) at the start of each line
        var inContent = group.Length == 0 ? name : Path.Combine(group, name);
        if (on)
            own.AddRange(problems
                .Where(p => p.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase) || p.StartsWith(inContent + ":", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Substring(p.IndexOf(':') + 1).Trim())
                .Distinct());

        return new InstalledMod(folder, group, info, on, own, on && loaded.Contains(Path.GetFullPath(folder)));
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
