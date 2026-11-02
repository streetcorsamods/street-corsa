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
    NoRoom,

    /// <summary>What it would replace holds the folder it goes in, or other mods, which would go with it</summary>
    HoldsOthers
}

/// <summary>A mod found in a zip that can be installed</summary>
/// <param name="ZipPath">The zip it is in</param>
/// <param name="Prefix">Its folder in the zip, '/' separated with a trailing '/'; "" for a mod.json at the zip's root</param>
/// <param name="Info">Its mod.json</param>
/// <param name="Target">Where it goes: Content\&lt;group&gt;\&lt;folder&gt;</param>
/// <param name="Replaces">The installed folders it takes the place of: the mod of its id, and whatever is at <paramref name="Target"/></param>
/// <param name="ReplacesVersion">The installed one's version, "" when none or it gives none</param>
/// <param name="Size">Its files' size unpacked, in bytes</param>
/// <param name="Missing">
/// The mods it requires that are neither installed nor installed with it, or not in a version new enough ("two 2.0.0")
/// </param>
/// <param name="Others">
/// The other mods it deletes, by id: installed at <paramref name="Target"/> under another id (a folder there that is no
/// mod by its place in the Content folder)
/// </param>
public sealed record ModToImport(
    string ZipPath,
    string Prefix,
    ModInfo Info,
    string Target,
    IReadOnlyList<string> Replaces,
    string ReplacesVersion,
    long Size,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string>? Others = null)
{
    /// <summary><see cref="Others"/>, never null</summary>
    public IReadOnlyList<string> OtherMods => Others ?? [];

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

        // What each requires, from what is installed and what goes in with it (once the ones with no room are out),
        // in a version new enough, as the loader holds it
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, _, version) in installed) versions[id] = version;
        foreach (var mod in mods) versions[mod.Info.Id] = mod.Info.Version;
        mods = mods.Select(m => m with { Missing = MissingOf(m.Info, versions) }).ToList();

        return new ImportPlan(mods, refused);
    }

    /// <summary>What <paramref name="info"/> requires that is not in <paramref name="versions"/> (id -> version), or older</summary>
    private static List<string> MissingOf(ModInfo info, Dictionary<string, string> versions)
    {
        var missing = new List<string>();
        foreach (var (id, version) in info.Requires)
        {
            if (ModIds.IsBase(id) || id.Equals(info.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (!versions.TryGetValue(id, out var has))
            {
                missing.Add(id);
                continue;
            }

            if (string.IsNullOrWhiteSpace(version) || version.Trim() == "*" || !ModVersion.TryParse(version, out var wanted)) continue;
            if (!ModVersion.TryParse(has, out var at) || at < wanted) missing.Add($"{id} {wanted}");
        }

        return missing;
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

            var sameId = installed.Where(i => i.Id.Equals(info.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            var replaces = sameId.Select(i => i.Folder).ToList();
            if (Directory.Exists(target) && !replaces.Contains(target, StringComparer.OrdinalIgnoreCase)) replaces.Add(target);

            // Another mod picked with it of its id or its folder, or one that goes where it replaces (or replaces where
            // it goes): the second would take the first one's place as it goes in
            if (mods.FirstOrDefault(m => m.Info.Id.Equals(info.Id, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(m.Target, target, StringComparison.OrdinalIgnoreCase)
                                         || replaces.Contains(m.Target, StringComparer.OrdinalIgnoreCase)
                                         || m.Replaces.Contains(target, StringComparer.OrdinalIgnoreCase)) is { } twin)
            {
                refused.Add(new ImportRefused(zip, info.Id, ImportRefusal.Twice, Path.GetFileName(twin.ZipPath)));
                continue;
            }

            // A replaced folder that holds the one it goes in (a group with a stray mod.json), or other mods, would
            // take them with it: never
            if (replaces.FirstOrDefault(r => IsInside(target, r) || HoldsMods(r)) is { } holding)
            {
                refused.Add(new ImportRefused(zip, info.Id, ImportRefusal.HoldsOthers, ModSet.Label(root, holding)));
                continue;
            }

            var others = installed
                .Where(i => replaces.Contains(i.Folder, StringComparer.OrdinalIgnoreCase) && !i.Id.Equals(info.Id, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (Directory.Exists(target) && !installed.Any(i => string.Equals(i.Folder, target, StringComparison.OrdinalIgnoreCase)))
                others.Add(ModSet.Label(root, target));
            var size = files.Sum(e => e.Length);
            mods.Add(new ModToImport(zip, prefix, info, target, replaces, sameId.FirstOrDefault().Version ?? string.Empty, size, [], others));
        }
    }

    /// <summary>
    /// Installs the mods of <paramref name="plan"/> into <paramref name="root"/>, one by one: each unpacked aside, what it
    /// replaces set aside, then moved into place (on a failure the replaced ones go back). Never throws on a mod. Each
    /// install unpacks in a folder of its own under Content\_installing, its lock held while it runs, so two at once
    /// never take each other's files away; the folders of one that stopped halfway are.
    /// </summary>
    public static ImportOutcome Install(ImportPlan plan, string root)
    {
        var installed = new List<ModToImport>();
        var failed = new List<(ModToImport, string)>();
        var staging = Path.Combine(root, StagingFolder);
        var aside = Path.Combine(root, ReplacedFolder);
        var run = Guid.NewGuid().ToString("N");
        var runFolder = Path.Combine(staging, run);
        // The folders this install has put a mod in: no later mod of it may take them away
        var placedHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        FileStream hold;
        try
        {
            Directory.CreateDirectory(staging);
            hold = new FileStream(Path.Combine(staging, run + LockSuffix), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImportOutcome([], plan.Mods.Select(m => (m, ex.Message)).ToList());
        }

        using (hold)
        {
            ClearLeftOvers(staging, run);
            foreach (var mod in plan.Mods)
                InstallOne(mod, Path.Combine(runFolder, Guid.NewGuid().ToString("N")), aside, placedHere, installed, failed);

            TryDelete(runFolder);
        }

        TryDeleteIfEmpty(staging);
        TryDeleteIfEmpty(aside);
        return new ImportOutcome(installed, failed);
    }

    // Beside each install's folder under _installing, held open while it runs
    private const string LockSuffix = ".lock";

    private static void InstallOne(ModToImport mod, string stage, string aside, HashSet<string> placedHere,
        List<ModToImport> installed, List<(ModToImport, string)> failed)
    {
        var setAside = new List<(string From, string To)>();
        var placed = false;
        try
        {
            Unpack(mod, stage);

            try
            {
                foreach (var old in mod.Replaces.Where(Directory.Exists))
                {
                    if (placedHere.Contains(Path.GetFullPath(old)))
                        throw new IOException($"{old} was just installed by another mod picked with it");
                    Directory.CreateDirectory(aside);
                    var to = Path.Combine(aside, Guid.NewGuid().ToString("N"));
                    Directory.Move(old, to);
                    setAside.Add((old, to));
                }

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
            placedHere.Add(Path.GetFullPath(mod.Target));
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

    /// <summary>The folders under _installing of installs no longer running (their lock free, or none), taken away</summary>
    private static void ClearLeftOvers(string staging, string run)
    {
        List<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(staging).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var folder in folders)
        {
            if (string.Equals(Path.GetFileName(folder), run, StringComparison.OrdinalIgnoreCase)) continue;
            var lockFile = folder + LockSuffix;
            try
            {
                // A running install holds its lock: opening it fails
                if (File.Exists(lockFile))
                    using (new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            TryDelete(folder);
            try { File.Delete(lockFile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary><paramref name="folder"/> is inside <paramref name="outer"/>, below it</summary>
    private static bool IsInside(string folder, string outer) =>
        Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)
            .StartsWith(Path.GetFullPath(outer).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>A folder with a mod in a folder of it (a group's, with a stray mod.json): replaced, they would go with it</summary>
    private static bool HoldsMods(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).Any(d => File.Exists(Path.Combine(d, ModSet.ManifestFileName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
