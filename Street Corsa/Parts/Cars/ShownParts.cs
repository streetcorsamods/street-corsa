using System.IO;
using System.Numerics;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>A part's model a car shows in a race: the file the race mode loads (next to it) and where it comes from</summary>
/// <param name="Name">The file in the race mode's folder, as race.ini names it (<c>parts\rims_mopar_cragar_8.kn5</c>)</param>
/// <param name="Source">The model in the parts catalog</param>
public sealed record ShownModel(string Name, string Source);

/// <summary>
/// The wheel a corner shows in place of the car's own: the rim, and the tyre when the car's own will not do
/// </summary>
/// <param name="Rim">The rim's model</param>
/// <param name="Tyre">The tyre's model; null keeps the car's own tyre round the rim</param>
/// <param name="TyreOnRim">Where the tyre sits in the rim's model, as it mounts on the rim's slot</param>
/// <param name="TyreScale">The tyre's size over the factory tyre's, as the car's data scales AC's tyre for the race: the
/// game's scenes size the tyre by it (AC by its own tyre)</param>
/// <param name="TyreOptional">The tyre is the factory one: the car keeps its own round the rim when the rim fits in it,
/// and shows this one only when the rim is too wide for the car's (<see cref="WheelFit"/>)</param>
public sealed record ShownWheel(ShownModel Rim, ShownModel? Tyre = null, Matrix4x4 TyreOnRim = default, float TyreScale = 1,
    bool TyreOptional = false);

/// <summary>
/// The parts a car shows that its own model does not have (the race mode's <c>car_parts.lua</c>): the wheels. A corner
/// on its factory rim and tyre keeps the car's own wheel: the car's model is what those look like. Another rim goes in
/// round the car's tyre, or with the factory tyre's model when it is too wide for the car's; another tyre brings the
/// whole wheel, the rim's model with it (the factory rim's too).
/// </summary>
public static class ShownParts
{
    /// <summary>The folder in the race mode the models go in</summary>
    public const string Folder = "parts";

    /// <summary>
    /// The wheel each corner shows, null where the car keeps its own: a corner on its factory rim and tyre, without a
    /// rim, or on a car whose factory running gear is not known (nothing to tell its own from)
    /// </summary>
    public static ShownWheel?[] Wheels(List<PartInstance> carParts, PartsCatalog catalog,
        (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? factory)
    {
        var wheels = new ShownWheel?[RunningGear.Corners];
        if (factory is not { } axles) return wheels;

        foreach (var rim in RunningGear.Rims(carParts))
        {
            var corner = RunningGear.CornerOf(rim.ParentSlot);
            if (corner < 0 || catalog.Get(rim.DefinitionId) is not { } rimDefinition || Model(catalog, rimDefinition) is not { } rimModel) continue;

            var own = RunningGear.IsFront(corner) ? axles.Front : axles.Rear;
            var tyre = rim.Children.FirstOrDefault(c => c.ParentSlot == RunningGear.TyreSlotOnRim);
            var tyreDefinition = tyre == null ? null : catalog.Get(tyre.DefinitionId);
            var otherTyre = tyreDefinition != null && !Same(tyreDefinition, own.Tyre);
            var otherRim = !Same(rimDefinition, own.Rim);
            if (!otherTyre && !otherRim) continue;

            // The tyre where the rim's slot puts it (one without that place is no tyre to show): always for another tyre,
            // and for another rim in case the rim is too wide for the car's own
            if (tyreDefinition != null && Model(catalog, tyreDefinition) is { } tyreModel
                && PartSlots.ChildWorld(rimDefinition, RunningGear.TyreSlotOnRim, tyreDefinition, MountedBy(tyre!, tyreDefinition), Matrix4x4.Identity) is { } onRim)
            {
                var factoryRadius = RunningGear.TyreRadius(own.Tyre);
                var scale = factoryRadius > 0 ? (float)(RunningGear.TyreRadius(tyreDefinition) / factoryRadius) : 1;
                wheels[corner] = new ShownWheel(rimModel, tyreModel, onRim, scale > 0 ? scale : 1, TyreOptional: !otherTyre);
            }
            else if (otherRim)
            {
                wheels[corner] = new ShownWheel(rimModel);
            }
        }
        return wheels;
    }

    /// <summary>A part's model file in the race mode's folder: its id made a file name</summary>
    public static string FileName(string partId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(partId.Select(c => c == '/' || c == '\\' || invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(Folder, name + ".kn5");
    }

    private static bool Same(PartDefinition a, PartDefinition b) => string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);

    private static ShownModel? Model(PartsCatalog catalog, PartDefinition part) =>
        catalog.GetModelPath(part) is { } path && File.Exists(path) ? new ShownModel(FileName(part.Id), path) : null;

    /// <summary>The tyre's own slot it is mounted by: as the save has it, else its first</summary>
    private static int MountedBy(PartInstance tyre, PartDefinition definition) =>
        tyre.OwnSlot != 0 ? tyre.OwnSlot : definition.Slots.FirstOrDefault()?.Id ?? 0;
}

/// <summary>A part's model a car shows, and where it sits in the space of the car's model (its main KN5)</summary>
public sealed record ShownModelAt(ShownModel Model, Matrix4x4 At);

/// <summary>
/// What a car shows that its own model does not have, as a race or a scene is given it: the wheels of its corners (AC's
/// order, null keeps the car's own) and the engine parts that stand out (ShownEngineParts). Equal when all of it is, so a
/// scene that compares what it is shown sees the same car as the same.
/// </summary>
public sealed class ShownSet : IEquatable<ShownSet>
{
    private ShownSet(IReadOnlyList<ShownWheel?> wheels, IReadOnlyList<ShownModelAt> extras)
    {
        Wheels = wheels;
        Extras = extras;
    }

    public IReadOnlyList<ShownWheel?> Wheels { get; }

    public IReadOnlyList<ShownModelAt> Extras { get; }

    /// <summary>The models of all of it, once each</summary>
    public IEnumerable<ShownModel> Models =>
        Wheels.OfType<ShownWheel>().SelectMany(w => w.Tyre is { } tyre ? new[] { w.Rim, tyre } : [w.Rim])
            .Concat(Extras.Select(e => e.Model))
            .DistinctBy(m => m.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The set; null when the car shows nothing but its own model</summary>
    public static ShownSet? Of(IReadOnlyList<ShownWheel?>? wheels, IReadOnlyList<ShownModelAt>? extras = null)
    {
        var someWheel = wheels != null && wheels.Any(w => w != null);
        if (!someWheel && (extras == null || extras.Count == 0)) return null;
        return new ShownSet(someWheel ? wheels!.ToArray() : new ShownWheel?[RunningGear.Corners], extras?.ToArray() ?? []);
    }

    /// <summary>The same set without its engine parts (the garage's parts view draws the whole engine itself)</summary>
    public ShownSet? WithoutExtras() => Of(Wheels);

    public bool Equals(ShownSet? other) =>
        other != null && Wheels.SequenceEqual(other.Wheels) && Extras.SequenceEqual(other.Extras);

    public override bool Equals(object? obj) => Equals(obj as ShownSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var wheel in Wheels) hash.Add(wheel);
        foreach (var extra in Extras) hash.Add(extra);
        return hash.ToHashCode();
    }
}
