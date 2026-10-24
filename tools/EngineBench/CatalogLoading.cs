using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa;

/// <summary>The catalog the bench works on, and the mods' makes, groups, classes and fuels it is worked by</summary>
public static partial class Program
{
    /// <summary>
    /// The parts of one folder, or of every mod when the folder is a Content folder (no engine builds of its own, a
    /// core among its folders): mod builds and parts are benched as the game loads them. A mod's own folder (its
    /// mod.json in it) is its parts folder
    /// </summary>
    /// <param name="useContent">
    /// Whether the folder's makes and part groups become the ones in use (process-wide): false for a second catalog
    /// (renew's old one), which must not change the rules the first is worked by
    /// </param>
    /// <param name="baseOnly">
    /// <c>--base-only</c>: the makes, groups, classes and fuels of the game as it ships (the core and the street rods)
    /// alone, and of a Content folder its parts alone, whatever other mods the folder holds; the before-merge check runs the same on every clone that way
    /// </param>
    private static PartsCatalog LoadCatalog(string folder, bool useContent = true, bool baseOnly = false)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        // A mod's own folder: its parts folder is what is benched (every .json under the mod would be read as a part,
        // its mod.json, sounds and cars among them)
        if (File.Exists(Path.Combine(folder, Mods.ModSet.ManifestFileName)) && Directory.Exists(Path.Combine(folder, Mods.ContentTypes.Parts)))
            folder = Path.Combine(folder, Mods.ContentTypes.Parts);
        if (!File.Exists(Path.Combine(folder, EngineBuild.FileName)) && Directory.Exists(Path.Combine(folder, Mods.ModIds.BaseId)))
        {
            var mods = baseOnly ? Shipped(folder) : Mods.ModSet.Load(folder);
            if (useContent) UseContent(mods);
            return PartsCatalog.Load(mods.ContentOf(Mods.ContentTypes.Parts).ToList(), mods);
        }

        // A parts folder of a Content folder (a mod's, the core's, the setting's): the makes and the part groups of that Content
        // folder's mods. One outside any (a git archive, a scratch run): the source tree's, with the folder's own
        // groups over them
        if (useContent)
        {
            var content = Directory.GetParent(folder)?.Parent;
            if (content != null && Directory.Exists(Path.Combine(content.FullName, Mods.ModIds.BaseId)))
            {
                var baseFolder = Path.Combine(content.FullName, Mods.ModIds.BaseId);
                // Base only: a mod's own parts folder still brings its groups, as the shipped game's cannot know them
                if (!baseOnly) UseContent(Mods.ModSet.Load(content.FullName));
                else UseContent(Shipped(content.FullName), Directory.GetParent(folder)!.FullName == baseFolder ? null : folder);
            }
            else UseContent(SourceContent(), folder);
        }

        // A mod's parts folder: what its mod.json holds (builds, aliases) with the part files, the ids bare as ever.
        // The mod is read as the core, so a mod that is its mod.json alone benches as one of files does
        var modFolder = Path.GetFileName(folder).Equals(Mods.ContentTypes.Parts, StringComparison.OrdinalIgnoreCase) ? Directory.GetParent(folder)?.FullName : null;
        var owner = new Mods.ModInfo
        {
            Id = Mods.ModIds.BaseId,
            Name = "Street Corsa",
            Folder = modFolder != null && File.Exists(Path.Combine(modFolder, Mods.ModSet.ManifestFileName)) ? modFolder : string.Empty
        };
        return PartsCatalog.Load(new[] { (owner, folder) });
    }

    /// <summary>The setting the game ships with, played over the core</summary>
    private const string DefaultSetting = "streetrod";

    /// <summary>The core and the street rods of a Content folder, the game as it ships: none of the other mods there</summary>
    private static Mods.ModSet Shipped(string content) => Mods.ModSet.Load(content, only: [DefaultSetting], setting: DefaultSetting);

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
