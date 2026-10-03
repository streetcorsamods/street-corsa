using System.IO;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>A part's model a car shows in a race: the file the race mode loads (next to it) and where it comes from</summary>
/// <param name="Name">The file in the race mode's folder, as race.ini names it (<c>parts\rims_mopar_cragar_8.kn5</c>)</param>
/// <param name="Source">The model in the parts catalog</param>
public sealed record ShownModel(string Name, string Source);

/// <summary>
/// The parts a car shows in a race that its own model does not have (the race mode's <c>car_parts.lua</c>): the rims.
/// A corner on its factory rim keeps the car's own: the car's model is what that rim looks like.
/// </summary>
public static class ShownParts
{
    /// <summary>The folder in the race mode the models go in</summary>
    public const string Folder = "parts";

    /// <summary>
    /// The rim each corner shows, null where the car keeps its own: a corner with the factory rim, without a rim, or on
    /// a car whose factory rims are not known (nothing to tell its own from)
    /// </summary>
    public static ShownModel?[] Rims(List<PartInstance> carParts, PartsCatalog catalog,
        (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? factory)
    {
        var rims = new ShownModel?[RunningGear.Corners];
        if (factory is not { } axles) return rims;

        foreach (var rim in RunningGear.Rims(carParts))
        {
            var corner = RunningGear.CornerOf(rim.ParentSlot);
            if (corner < 0 || catalog.Get(rim.DefinitionId) is not { } definition) continue;

            var own = RunningGear.IsFront(corner) ? axles.Front.Rim : axles.Rear.Rim;
            if (string.Equals(own.Id, definition.Id, StringComparison.OrdinalIgnoreCase)) continue;

            if (catalog.GetModelPath(definition) is { } path && File.Exists(path))
                rims[corner] = new ShownModel(FileName(definition.Id), path);
        }
        return rims;
    }

    /// <summary>A part's model file in the race mode's folder: its id made a file name</summary>
    public static string FileName(string partId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(partId.Select(c => c == '/' || c == '\\' || invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(Folder, name + ".kn5");
    }
}
