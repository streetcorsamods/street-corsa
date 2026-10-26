using StreetCorsa.Mods;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts;

/// <summary>
/// The books the part logic reads process-wide besides the catalog (the makes and cars, the part groups, the classes,
/// the fuels), set from a set of mods in one place: the game reads them from the content in play, a tool or a test
/// sets the ones it works by. Plain BCL, linked into EngineBench.
/// </summary>
public static class PartsContent
{
    /// <summary>
    /// The makes and cars of <paramref name="mods"/> (none for no mods), and the part groups, classes and fuels of
    /// their parts folders with <paramref name="partsFolder"/>'s over them, made the ones in use
    /// </summary>
    /// <returns>What the books could not read, a line each</returns>
    public static IReadOnlyList<string> Use(ModSet? mods, string? partsFolder = null)
    {
        CarBook.Current = mods == null ? CarBook.Empty : CarBook.Load(mods);
        var folders = mods?.Folders(ContentTypes.Parts).Select(f => f.Dir).ToList() ?? new List<string>();
        if (partsFolder != null) folders.Add(partsFolder);
        PartGroups.Current = PartGroups.Load(folders);
        PartClasses.Current = PartClasses.Load(folders);
        Fuels.Current = Fuels.Load(folders);
        return [.. CarBook.Current.Problems, .. PartGroups.Current.Problems, .. PartClasses.Current.Problems, .. Fuels.Current.Problems];
    }
}
