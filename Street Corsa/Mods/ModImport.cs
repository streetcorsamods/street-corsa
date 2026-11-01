using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace StreetCorsa.Mods;

/// <summary>Why a mod in a zip is not installed</summary>
public enum ImportRefusal
{
    /// <summary>The file is no zip, or it cannot be read</summary>
    NotAZip,

    /// <summary>The zip has no mod.json in it</summary>
    NoMod,

    /// <summary>Its mod.json cannot be read</summary>
    Unreadable,

    /// <summary>Its id is no mod id</summary>
    BadId,

    /// <summary>It is the core, which comes with the game</summary>
    Core,

    /// <summary>It needs a newer game (its "gameVersion")</summary>
    NeedsNewerGame,

    /// <summary>A file in it would land outside its own folder</summary>
    UnsafePath,

    /// <summary>Another mod picked with it has its id or its folder</summary>
    Twice,

    /// <summary>The disk has no room for it</summary>
    NoRoom
}

/// <summary>A mod found in a zip that can be installed</summary>
/// <param name="ZipPath">The zip it is in</param>
/// <param name="Prefix">Its folder in the zip, '/' separated with a trailing '/'; "" for a mod.json at the zip's root</param>
/// <param name="Info">Its mod.json</param>
/// <param name="Target">Where it goes: Content\&lt;group&gt;\&lt;folder&gt;</param>
/// <param name="Replaces">The installed folders it takes the place of: the mod of its id, and whatever is at <paramref name="Target"/></param>
/// <param name="ReplacesVersion">The installed one's version, "" when none or it gives none</param>
/// <param name="Size">Its files' size unpacked, in bytes</param>
/// <param name="Missing">The mods it requires that are neither installed nor picked with it</param>
public sealed record ModToImport(
    string ZipPath,
    string Prefix,
    ModInfo Info,
    string Target,
    IReadOnlyList<string> Replaces,
    string ReplacesVersion,
    long Size,
    IReadOnlyList<string> Missing)
{
    /// <summary>The installed mod is newer than this one</summary>
    public bool IsOlder =>
        ModVersion.TryParse(ReplacesVersion, out var installed) && ModVersion.TryParse(Info.Version, out var mine) && mine < installed;
}

/// <summary>A mod (or a whole zip, <paramref name="Mod"/> null) not installed, and why</summary>
public sealed record ImportRefused(string ZipPath, string? Mod, ImportRefusal Why, string Detail = "");

/// <summary>What a pick of zips holds: the mods to install, the rest refused</summary>
public sealed record ImportPlan(IReadOnlyList<ModToImport> Mods, IReadOnlyList<ImportRefused> Refused)
{
    /// <summary>Some mod in it takes an installed one's place</summary>
    public bool ReplacesAny => Mods.Any(m => m.Replaces.Count > 0);
}

/// <summary>How an install went: the mods put in, and those that failed with why</summary>
public sealed record ImportOutcome(IReadOnlyList<ModToImport> Installed, IReadOnlyList<(ModToImport Mod, string Error)> Failed);

/// <summary>
/// Installs mods from zips into the Content folder (<see cref="ModSet.DefaultRoot"/>), as the player would unzip them:
/// a mod is a folder with a mod.json, found at the zip's root, one folder down or in a group (<c>settings\mysetting</c>,
/// as installer\pack-mod.ps1 lays a zip out), or under one folder more that wraps it all. It goes into the group the
/// zip has it in (settings, engines, addons), else by its type: a setting under settings, the rest under addons.
///
/// A mod is unpacked beside the Content folder's mods in a switched-off folder first, then moved into place, so a
/// broken unpack never leaves half a mod: what it replaces is moved aside first (Content\_replaced) and put back if the
/// move fails; it is deleted only once the new one is in place. One that could not go back stays there, switched off.
/// Plain BCL, like the rest of Mods.
/// </summary>
public static class ModImport
{
    /// <summary>The groups a zip may file a mod in; any other parent folder is only a wrapper</summary>
    public static readonly IReadOnlyList<string> Groups = ["settings", "engines", "addons"];

    // Unpacked here, switched off ('_'), so the loader never reads a mod half unpacked; and the replaced ones set aside here
    internal const string StagingFolder = "_installing";
    internal const string ReplacedFolder = "_replaced";

    // A mod.json this deep at most (wrapper\group\mod\mod.json): deeper ones are a mod's own files
    private const int MaxDepth = 3;

    /// <summary>
    /// What <paramref name="zips"/> hold for the Content folder <paramref name="root"/>, read now. Nothing is written.
    /// Never throws on a zip: one that cannot be read is refused.
    /// </summary>
    public static ImportPlan Plan(IEnumerable<string> zips, string root, ModVersion? gameVersion = null)
    {
        var game = gameVersion ?? ModSet.ProgramVersion;
        var installed = ModSet.InstalledFolders(root);
        var mods = new List<ModToImport>();
        var refused = new List<ImportRefused>();

        foreach (var zip in zips)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zip);
                ReadZip(zip, archive, root, game, installed, mods, refused);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                refused.Add(new ImportRefused(zip, null, ImportRefusal.NotAZip, ex.Message));
            }
        }

        // What each requires, from what is installed and what comes with it
        var ids = new HashSet<string>(installed.Select(i => i.Id).Concat(mods.Select(m => m.Info.Id)), StringComparer.OrdinalIgnoreCase);
        mods = mods.Select(m => m with { Missing = m.Info.Requires.Keys.Where(r => !ModIds.IsBase(r) && !ids.Contains(r)).ToList() }).ToList();

        // Room for all of it on the Content folder's disk, with some to spare
        var free = FreeSpace(root);
        if (free is { } bytes)
        {
            long used = 0;
            foreach (var mod in mods.ToList())
            {
                used += mod.Size;
                if (used + used / 10 <= bytes) continue;
                mods.Remove(mod);
                refused.Add(new ImportRefused(mod.ZipPath, mod.Info.Id, ImportRefusal.NoRoom, $"{mod.Size / (1024 * 1024)} MB"));
                used -= mod.Size;
            }
        }

        return new ImportPlan(mods, refused);
    }

    private static void ReadZip(string zip, ZipArchive archive, string root, ModVersion game,
        IReadOnlyList<(string Id, string Folder, string Version)> installed, List<ModToImport> mods, List<ImportRefused> refused)
    {
        // The mods: each mod.json not too deep and not inside another mod's folder
        var manifests = archive.Entries
            .Select(e => (Entry: e, Path: Normal(e.FullName)))
            .Where(e => !e.Path.Split('/').Contains("__MACOSX", StringComparer.OrdinalIgnoreCase))
            .Where(e => string.Equals(LastSegment(e.Path), ModSet.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Path.Count(c => c == '/') <= MaxDepth)
            .OrderBy(e => e.Path.Count(c => c == '/'))
            .ToList();
        var prefixes = new List<string>();
        foreach (var (_, path) in manifests)
        {
            var prefix = path.Substring(0, path.Length - ModSet.ManifestFileName.Length);
            if (prefixes.Any(p => prefix.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            prefixes.Add(prefix);
        }

        if (prefixes.Count == 0)
        {
            refused.Add(new ImportRefused(zip, null, ImportRefusal.NoMod));
            return;
        }

        foreach (var prefix in prefixes)
        {
            var manifest = manifests.First(m => m.Path.Equals(prefix + ModSet.ManifestFileName, StringComparison.OrdinalIgnoreCase)).Entry;
            var segments = prefix.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var folderName = segments.Length > 0 ? segments[^1] : null;
            var name = folderName ?? Path.GetFileNameWithoutExtension(zip);

            ModInfo info;
            try
            {
                using var reader = new StreamReader(manifest.Open(), Encoding.UTF8);
                info = ModSet.ParseManifest(reader.ReadToEnd(), out _);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
            {
                refused.Add(new ImportRefused(zip, name, ImportRefusal.Unreadable, ex.Message));
                continue;
            }

            // As the loader has it: no id is the folder's name
            if (string.IsNullOrWhiteSpace(info.Id)) info.Id = (folderName ?? string.Empty).ToLowerInvariant();
            if (ModIds.IsBase(info.Id) || (folderName != null && ModIds.IsBase(folderName) && segments.Length == 1))
            {
                refused.Add(new ImportRefused(zip, name, ImportRefusal.Core));
                continue;
            }

            if (!ModIds.IsValidModId(info.Id))
            {
                refused.Add(new ImportRefused(zip, name, ImportRefusal.BadId, info.Id));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(info.GameVersion) && ModVersion.TryParse(info.GameVersion, out var wanted) && game < wanted)
            {
                refused.Add(new ImportRefused(zip, info.Id, ImportRefusal.NeedsNewerGame, wanted.ToString()));
                continue;
            }

            var files = archive.Entries.Where(e => Normal(e.FullName).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (files.FirstOrDefault(e => !IsSafe(Normal(e.FullName).Substring(prefix.Length))) is { } unsafeEntry)
            {
                refused.Add(new ImportRefused(zip, info.Id, ImportRefusal.UnsafePath, unsafeEntry.FullName));
                continue;
            }

            // The group the zip files it in, else by type; its folder's name when that is a fine one, else its id
            var parent = segments.Length > 1 ? segments[^2] : null;
            var group = Groups.FirstOrDefault(g => g.Equals(parent, StringComparison.OrdinalIgnoreCase))
                        ?? (info.IsSetting ? "settings" : "addons");
            var folder = folderName != null && IsFolderName(folderName) ? folderName : info.Id;
            var target = Path.Combine(root, group, folder);

            if (mods.FirstOrDefault(m => m.Info.Id.Equals(info.Id, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(m.Target, target, StringComparison.OrdinalIgnoreCase)) is { } twin)
            {
                refused.Add(new ImportRefused(zip, info.Id, ImportRefusal.Twice, Path.GetFileName(twin.ZipPath)));
                continue;
            }

            var sameId = installed.Where(i => i.Id.Equals(info.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            var replaces = sameId.Select(i => i.Folder).ToList();
            if (Directory.Exists(target) && !replaces.Contains(target, StringComparer.OrdinalIgnoreCase)) replaces.Add(target);
            var size = files.Sum(e => e.Length);
            mods.Add(new ModToImport(zip, prefix, info, target, replaces, sameId.FirstOrDefault().Version ?? string.Empty, size, []));
        }
    }

    /// <summary>
    /// Installs the mods of <paramref name="plan"/> into <paramref name="root"/>, one by one: each unpacked aside, what it
    /// replaces set aside, then moved into place (on a failure the replaced one goes back). Never throws on a mod.
    /// </summary>
    public static ImportOutcome Install(ImportPlan plan, string root)
    {
        var installed = new List<ModToImport>();
        var failed = new List<(ModToImport, string)>();
        var staging = Path.Combine(root, StagingFolder);
        var aside = Path.Combine(root, ReplacedFolder);
        TryDelete(staging);

        foreach (var mod in plan.Mods)
        {
            var stage = Path.Combine(staging, Guid.NewGuid().ToString("N"));
            var setAside = new List<(string From, string To)>();
            var placed = false;
            try
            {
                Unpack(mod, stage);

                foreach (var old in mod.Replaces.Where(Directory.Exists))
                {
                    Directory.CreateDirectory(aside);
                    var to = Path.Combine(aside, Guid.NewGuid().ToString("N"));
                    Directory.Move(old, to);
                    setAside.Add((old, to));
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(mod.Target)!);
                    Directory.Move(stage, mod.Target);
                }
                catch
                {
                    // Back as it was. One that cannot go back stays set aside (switched off), never deleted
                    foreach (var (from, to) in setAside.AsEnumerable().Reverse())
                    {
                        try { Directory.Move(to, from); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }

                    setAside.Clear();
                    throw;
                }

                placed = true;
                installed.Add(mod);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                failed.Add((mod, ex.Message));
            }
            finally
            {
                TryDelete(stage);
                // What it replaced goes only once it is in place
                if (placed)
                    foreach (var (_, to) in setAside) TryDelete(to);
            }
        }

        TryDelete(staging);
        TryDeleteIfEmpty(aside);
        return new ImportOutcome(installed, failed);
    }

    /// <summary>The mod's files out of its zip into <paramref name="stage"/>, each checked to land inside it</summary>
    private static void Unpack(ModToImport mod, string stage)
    {
        var top = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(top);
        using var archive = ZipFile.OpenRead(mod.ZipPath);
        foreach (var entry in archive.Entries)
        {
            var path = Normal(entry.FullName);
            if (!path.StartsWith(mod.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = path.Substring(mod.Prefix.Length);
            if (relative.Length == 0) continue;
            if (!IsSafe(relative)) throw new InvalidDataException($"{entry.FullName} lands outside the mod's folder");

            var target = Path.GetFullPath(Path.Combine(top, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(top, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{entry.FullName} lands outside the mod's folder");

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>A zip's path with '/' only, no leading one</summary>
    private static string Normal(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string LastSegment(string path) => path.Substring(path.LastIndexOf('/') + 1);

    /// <summary>A path inside a mod's folder that stays there: not rooted, no drive, no "..", no "." step</summary>
    public static bool IsSafe(string relative)
    {
        if (relative.Length == 0) return true;
        if (relative.Contains(':') || relative.StartsWith('/')) return false;
        var segments = relative.TrimEnd('/').Split('/');
        return segments.All(s => s.Length > 0 && s != "." && s != ".." && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    /// <summary>A folder name the Content folder can keep: no path in it, not switched off</summary>
    private static bool IsFolderName(string name) =>
        name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.StartsWith('_') && !name.StartsWith('.')
        && name.Trim() == name && !name.EndsWith('.');

    private static long? FreeSpace(string root)
    {
        try
        {
            var drive = Path.GetPathRoot(Path.GetFullPath(root));
            return string.IsNullOrEmpty(drive) ? null : new DriveInfo(drive).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDeleteIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left switched off; the next install takes it away
        }
    }
}
