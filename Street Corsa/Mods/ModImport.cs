using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using StreetCorsa.Helpers;

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

/// <summary>
/// How an install went: the mods put in, and those that failed with why. A failure's error names each installed
/// mod's folder set aside for it that could not be moved back: kept under Content\_replaced, switched off, for the
/// player to move back by hand.
/// </summary>
public sealed record ImportOutcome(
    IReadOnlyList<ModToImport> Installed,
    IReadOnlyList<(ModToImport Mod, string Error)> Failed);

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

    // A zip of more files than this, or whose directory is bigger than this, is none of ours: its directory alone could
    // be a zip bomb. Both are read off the zip's end record before its directory is (DirectoryTooBig)
    internal const int MostEntries = 100_000;
    internal const long MostDirectoryBytes = 64L * 1024 * 1024;

    // A mod.json bigger than this is no mod.json: never read whole into memory
    internal const int MostManifestBytes = 4 * 1024 * 1024;

    // On the name of each folder under _replaced that only waits to be deleted (a mod deleted or replaced): one without
    // it is a mod that could not be moved back, kept for the player
    internal const string GoneSuffix = ".gone";

    // Copied in pieces this big: a mod's files, its mod.json
    private const int CopyBuffer = 81920;

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
                if (DirectoryTooBig(zip) is { } why)
                {
                    refused.Add(new ImportRefused(zip, null, ImportRefusal.NotAZip, why));
                    continue;
                }

                using var archive = ZipFile.OpenRead(zip);
                ReadZip(zip, archive, root, game, installed, mods, refused);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException
                                           or ArgumentException)
            {
                refused.Add(new ImportRefused(zip, null, ImportRefusal.NotAZip, ex.Message));
            }
        }

        // Room for all of it on the Content folder's disk, with some to spare. The sizes are what the zips claim, held
        // to when they are unpacked; added up in double, as a hostile claim could overflow a long
        var free = FreeSpace(root);
        if (free is { } bytes)
        {
            double used = 0;
            foreach (var mod in mods.ToList())
            {
                used += mod.Size;
                if (used * 1.1 <= bytes) continue;
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
                info = ModSet.ParseManifest(ReadManifest(manifest), out _);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or NotSupportedException or ArgumentException)
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
            if (replaces.FirstOrDefault(r => PathNames.IsUnder(r, target) || HoldsMods(r)) is { } holding)
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
            var size = SizeOf(files);
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
            SweepReplaced(aside);
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
        var notBack = new List<(string From, string To)>();
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
                    var to = Path.Combine(aside, Guid.NewGuid().ToString("N"));
                    MoveAside(old, to);
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
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notBack.Add((from, to)); }
                }

                setAside.Clear();
                throw;
            }

            placed = true;
            placedHere.Add(Path.GetFullPath(mod.Target));
            installed.Add(mod);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException
                                       or ArgumentException)
        {
            var error = ex.Message;
            foreach (var (from, to) in notBack)
                error += $" {from} could not be moved back: it is kept, switched off, at {to}.";

            failed.Add((mod, error));
        }
        finally
        {
            TryDelete(stage);
            // What it replaced goes only once it is in place
            if (placed)
                foreach (var (_, to) in setAside) DeleteAside(to);
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

    /// <summary>
    /// A mod's folder moved to <paramref name="to"/> under _replaced, made first. Another install or a Mod Manager
    /// delete may take an empty _replaced away just then: made again and moved once more.
    /// </summary>
    internal static void MoveAside(string folder, string to)
    {
        var aside = Path.GetDirectoryName(to)!;
        Directory.CreateDirectory(aside);
        try
        {
            Directory.Move(folder, to);
        }
        catch (DirectoryNotFoundException) when (Directory.Exists(folder))
        {
            Directory.CreateDirectory(aside);
            Directory.Move(folder, to);
        }
    }

    /// <summary>
    /// A folder under _replaced deleted, named first as only waiting to go (<see cref="GoneSuffix"/>): what a delete
    /// leaves (a file in it held open) goes with the next sweep (<see cref="SweepReplaced"/>), never a mod kept there
    /// because it could not be moved back. A folder that cannot even be renamed is deleted as it is.
    /// </summary>
    internal static void DeleteAside(string folder)
    {
        var gone = folder;
        if (!folder.EndsWith(GoneSuffix, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.Move(folder, folder + GoneSuffix);
                gone = folder + GoneSuffix;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        TryDelete(gone);
    }

    /// <summary>
    /// What earlier deletes left under _replaced (<paramref name="aside"/>) taken away: the folders named as only
    /// waiting to go. The rest are kept: mods that could not be moved back, or set aside by an install running now
    /// </summary>
    internal static void SweepReplaced(string aside)
    {
        List<string> gone;
        try
        {
            if (!Directory.Exists(aside)) return;
            gone = Directory.EnumerateDirectories(aside, "*" + GoneSuffix).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var folder in gone) TryDelete(folder);
        TryDeleteIfEmpty(aside);
    }

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

    /// <summary>
    /// The mod's files out of its zip into <paramref name="stage"/>, each checked again to land inside it (the zip may
    /// have changed since the plan), none bigger than it claims and all of them no bigger than the plan's size
    /// </summary>
    private static void Unpack(ModToImport mod, string stage)
    {
        var top = Path.GetFullPath(stage);
        Directory.CreateDirectory(top);
        if (DirectoryTooBig(mod.ZipPath) is { } why) throw new InvalidDataException($"{mod.ZipPath}: {why}");
        using var archive = ZipFile.OpenRead(mod.ZipPath);

        var left = mod.Size;
        foreach (var entry in archive.Entries)
        {
            var path = Normal(entry.FullName);
            if (!path.StartsWith(mod.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = path.Substring(mod.Prefix.Length);
            if (relative.Length == 0) continue;
            if (!IsSafe(relative) || !PathNames.TryCombineUnder(top, relative.Replace('/', Path.DirectorySeparatorChar), out var target))
                throw new InvalidDataException($"{entry.FullName} lands outside the mod's folder");

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            left -= Extract(entry, target, left);
        }
    }

    /// <summary>
    /// One entry written to <paramref name="target"/>, its bytes counted as they come: more than it claims, or more
    /// than the <paramref name="left"/> of the whole mod, is a zip bomb (or a zip changed since the plan) and throws.
    /// The bytes written.
    /// </summary>
    private static long Extract(ZipArchiveEntry entry, string target, long left)
    {
        long written;
        using (var source = entry.Open())
        using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            written = CopyCapped(source, file, Math.Min(entry.Length, left), past => past > entry.Length
                ? $"{entry.FullName} unpacks to more than it claims"
                : $"{entry.FullName} unpacks to more than the mod's size when it was picked");
        }

        try { File.SetLastWriteTime(target, entry.LastWriteTime.DateTime); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }

        return written;
    }

    /// <summary>A mod.json's text, read no further than <see cref="MostManifestBytes"/> nor than it claims</summary>
    private static string ReadManifest(ZipArchiveEntry manifest)
    {
        if (manifest.Length > MostManifestBytes)
            throw new InvalidDataException($"{manifest.FullName} is bigger than {MostManifestBytes / (1024 * 1024)} MB");

        using var bytes = new MemoryStream();
        using (var source = manifest.Open())
            CopyCapped(source, bytes, manifest.Length, _ => $"{manifest.FullName} unpacks to more than it claims");

        bytes.Position = 0;
        using var reader = new StreamReader(bytes, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// <paramref name="source"/> copied to <paramref name="target"/>, its bytes counted as they come: past
    /// <paramref name="most"/> it throws with <paramref name="tooBig"/>'s words for the count reached. The bytes copied.
    /// </summary>
    private static long CopyCapped(Stream source, Stream target, long most, Func<long, string> tooBig)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBuffer);
        try
        {
            long written = 0;
            int read;
            while ((read = source.Read(buffer, 0, CopyBuffer)) > 0)
            {
                written += read;
                if (written > most) throw new InvalidDataException(tooBig(written));
                target.Write(buffer, 0, read);
            }

            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Why a zip's directory is too big to read (<see cref="MostEntries"/>, <see cref="MostDirectoryBytes"/>), from its end
    /// record (Zip64's when it has one), before <see cref="ZipFile.OpenRead(string)"/> reads the directory whole into
    /// memory; null when it is not, or when no end record is found (opening it then says it is no zip). The directory
    /// counts up to the end record, as the reader goes on through entries past the size it claims.
    /// </summary>
    internal static string? DirectoryTooBig(string zip)
    {
        const uint EndSignature = 0x06054b50, LocatorSignature = 0x07064b50, End64Signature = 0x06064b50;
        const int EndSize = 22, LocatorSize = 20, End64Size = 56;

        using var file = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read);
        var tailLength = (int)Math.Min(file.Length, EndSize + ushort.MaxValue);
        var tail = new byte[tailLength];
        file.Seek(-tailLength, SeekOrigin.End);
        file.ReadExactly(tail);

        for (var at = tailLength - EndSize; at >= 0; at--)
        {
            var end = tail.AsSpan(at);
            if (BinaryPrimitives.ReadUInt32LittleEndian(end) != EndSignature) continue;

            ulong entries = BinaryPrimitives.ReadUInt16LittleEndian(end[10..]);
            ulong size = BinaryPrimitives.ReadUInt32LittleEndian(end[12..]);
            ulong start = BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);
            var endAt = (ulong)(file.Length - tailLength + at);

            // Zip64: the figures that do not fit are in its own end record, which the locator before this one points at
            if ((entries == ushort.MaxValue || size == uint.MaxValue || start == uint.MaxValue) && at >= LocatorSize
                && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(at - LocatorSize)) == LocatorSignature)
            {
                var end64At = BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(at - LocatorSize + 8));
                if (file.Length < End64Size || end64At > (ulong)(file.Length - End64Size)) return "its end record points outside it";

                var end64 = new byte[End64Size];
                file.Seek((long)end64At, SeekOrigin.Begin);
                file.ReadExactly(end64);
                if (BinaryPrimitives.ReadUInt32LittleEndian(end64) != End64Signature) return "its end record points at nothing";
                entries = BinaryPrimitives.ReadUInt64LittleEndian(end64.AsSpan(32));
                size = BinaryPrimitives.ReadUInt64LittleEndian(end64.AsSpan(40));
                start = BinaryPrimitives.ReadUInt64LittleEndian(end64.AsSpan(48));
                endAt = end64At;
            }

            if (entries > MostEntries) return $"more than {MostEntries} files";
            var directory = Math.Max(size, start <= endAt ? endAt - start : 0);
            return directory > MostDirectoryBytes ? $"its directory is bigger than {MostDirectoryBytes / (1024 * 1024)} MB" : null;
        }

        return null;
    }

    /// <summary>The entries' claimed sizes added up; long.MaxValue for a sum that would not fit</summary>
    private static long SizeOf(IEnumerable<ZipArchiveEntry> entries)
    {
        long size = 0;
        foreach (var entry in entries)
        {
            if (entry.Length > long.MaxValue - size) return long.MaxValue;
            size += entry.Length;
        }

        return size;
    }

    /// <summary>A zip's path with '/' only, no leading one</summary>
    private static string Normal(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string LastSegment(string path) => path.Substring(path.LastIndexOf('/') + 1);

    /// <summary>
    /// A path inside a mod's folder that stays there, a zip's '\' read as '/': not rooted, no drive, each step a plain
    /// name (<see cref="PathNames.IsPlainName"/>: not "." or "..", no device name, no dot or space at its end)
    /// </summary>
    public static bool IsSafe(string relative)
    {
        relative = relative.Replace('\\', '/');
        if (relative.Length == 0) return true;
        if (relative.Contains(':') || relative.StartsWith('/')) return false;
        return relative.TrimEnd('/').Split('/').All(PathNames.IsPlainName);
    }

    /// <summary>A folder name the Content folder can keep: no path in it, not switched off</summary>
    private static bool IsFolderName(string name) =>
        PathNames.IsPlainName(name) && name.Trim() == name && !ModSet.IsSwitchedOff(name);

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
