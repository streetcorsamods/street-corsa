using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa;

/// <summary>The catalog the bench works on, and the mods' makes, groups, classes and fuels it is worked by</summary>
public static partial class Program
{
    /// <summary>
    /// The parts of one folder, or of every mod when the folder is a Content folder (no engine builds of its own, a
    /// base game among its folders): mod builds and parts are benched as the game loads them
    /// </summary>
    /// <param name="useContent">
    /// Whether the folder's makes and part groups become the ones in use (process-wide): false for a second catalog
    /// (renew's old one), which must not change the rules the first is worked by
    /// </param>
    /// <param name="baseOnly">
    /// <c>--base-only</c>: the base game's makes, groups, classes and fuels alone, and of a Content folder its parts alone,
    /// whatever other mods the folder holds; the before-merge check runs the same on every clone that way
    /// </param>
    private static PartsCatalog LoadCatalog(string folder, bool useContent = true, bool baseOnly = false)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!File.Exists(Path.Combine(folder, EngineBuild.FileName)) && Directory.Exists(Path.Combine(folder, Mods.ModIds.BaseId)))
        {
            var mods = baseOnly ? Mods.ModSet.BaseOnly(Path.Combine(folder, Mods.ModIds.BaseId)) : Mods.ModSet.Load(folder);
            if (useContent) UseContent(mods);
            return PartsCatalog.Load(mods.ContentOf(Mods.ContentTypes.Parts).ToList());
        }

        // A parts folder of a Content folder (a mod's, the base game's): the makes and the part groups of that Content
        // folder's mods. One outside any (a git archive, a scratch run): the source tree's, with the folder's own
        // groups over them
        if (useContent)
        {
            var content = Directory.GetParent(folder)?.Parent;
            if (content != null && Directory.Exists(Path.Combine(content.FullName, Mods.ModIds.BaseId)))
            {
                var baseFolder = Path.Combine(content.FullName, Mods.ModIds.BaseId);
                // Base only: a mod's own parts folder still brings its groups, as the base game's cannot know them
                if (!baseOnly) UseContent(Mods.ModSet.Load(content.FullName));
                else UseContent(Mods.ModSet.BaseOnly(baseFolder), Directory.GetParent(folder)!.FullName == baseFolder ? null : folder);
            }
            else UseContent(SourceContent(), folder);
        }

        // A mod's parts folder: what its mod.json holds (packs, builds, aliases) with the files, the ids bare as ever.
        // The mod is read as the base game, so a mod that is its mod.json alone benches as one of files does
        var modFolder = Path.GetFileName(folder).Equals(Mods.ContentTypes.Parts, StringComparison.OrdinalIgnoreCase) ? Directory.GetParent(folder)?.FullName : null;
        var owner = new Mods.ModInfo
        {
            Id = Mods.ModIds.BaseId,
            Name = "Street Corsa",
            Folder = modFolder != null && File.Exists(Path.Combine(modFolder, Mods.ModSet.ManifestFileName)) ? modFolder : string.Empty
        };
        return PartsCatalog.Load(new[] { (owner, folder) });
    }

    /// <summary>The source tree's Content folder, above the program; null when it does not run from a build of the source</summary>
    private static Mods.ModSet? SourceContent()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var content = Path.Combine(dir.FullName, "Street Corsa", Mods.ModSet.FolderName);
            if (Directory.Exists(Path.Combine(content, Mods.ModIds.BaseId))) return Mods.ModSet.Load(content);
        }

        return null;
    }

    /// <summary>The makes (which family a car or an engine is) and the part groups of the mods, as the game reads them</summary>
    /// <param name="partsFolder">A parts folder whose own groups go over the mods'</param>
    private static void UseContent(Mods.ModSet? mods, string? partsFolder = null)
    {
        if (mods == null) Console.WriteLine("content: no Content folder found for the makes and part groups; the folder's own groups only");
        CarBook.Current = mods == null ? CarBook.Empty : CarBook.Load(mods);
        var folders = mods?.Folders(Mods.ContentTypes.Parts).Select(f => f.Dir).ToList() ?? new List<string>();
        if (partsFolder != null) folders.Add(partsFolder);
        PartGroups.Current = PartGroups.Load(folders);
        PartClasses.Current = PartClasses.Load(folders);
        Fuels.Current = Fuels.Load(folders);
        foreach (var problem in CarBook.Current.Problems.Concat(PartGroups.Current.Problems).Concat(PartClasses.Current.Problems).Concat(Fuels.Current.Problems)) Console.WriteLine($"content: {problem}");
    }
}
