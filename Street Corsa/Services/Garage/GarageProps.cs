using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Garage;

/// <summary>
/// A prop set into a garage: its model (empty for the game's own, <see cref="GarageProps.Find"/>), and where it stands
/// there (on the floor or on a bench top), turned about the vertical by <see cref="Heading"/>; a prop laid down or leant
/// against a wall is first tipped forward by <see cref="Tilt"/> and over to its side by <see cref="Roll"/> (degrees).
/// </summary>
public sealed record PlacedProp(string Id, string Name, string Model, Vector3 Position, float Heading, string? Credit = null, float Tilt = 0f, float Roll = 0f)
{
    /// <summary>Where the model goes, as the renderer takes it: turned about the vertical, then moved to its spot</summary>
    public Matrix4x4 Placement => Matrix4x4.CreateRotationY(Heading * MathF.PI / 180f) * Matrix4x4.CreateTranslation(Position);

    /// <summary>Tipped by <see cref="Tilt"/> or <see cref="Roll"/>: <see cref="PlacementOf"/> sets it back on its base</summary>
    public bool IsLaid => Tilt != 0f || Roll != 0f;

    /// <summary>
    /// Where the model whose bounds are <paramref name="min"/> to <paramref name="max"/> goes: a prop not laid as
    /// <see cref="Placement"/> says; a laid one tipped, then stood back on what is now its base, the middle of its
    /// footprint on its spot, turned and moved there
    /// </summary>
    public Matrix4x4 PlacementOf(Vector3 min, Vector3 max)
    {
        if (!IsLaid) return Placement;

        var lay = Matrix4x4.CreateRotationX(Tilt * MathF.PI / 180f) * Matrix4x4.CreateRotationZ(Roll * MathF.PI / 180f);
        var (low, high) = Helpers.Box3.Bounds(min, max, lay);
        var stood = Matrix4x4.CreateTranslation(-(low.X + high.X) / 2f, -low.Y, -(low.Z + high.Z) / 2f);
        return lay * stood * Placement;
    }
}

/// <summary>
/// The props in the mods' props folders (<c>Content/&lt;mod&gt;/props/&lt;id&gt;/prop.json</c>): a model and, by garage
/// (the showroom's folder name), where it stands in it: one place, or a list of them for a prop that is in the garage
/// more than once (a stack of tyres, two stools). A later mod's prop of an id replaces an earlier one's, so a mod can swap
/// the radio for another set, or put it somewhere else in its own garage; one that names no model (and has no
/// <c>&lt;id&gt;.kn5</c> of its own) keeps the earlier one's model, name and credit, and gives only its places.
/// <code>
/// { "name": "Radio", "model": "radio.kn5", "credit": "...", "places": { "garage": { "position": [2.62, 1.29, -8.6], "heading": 67 } } }
/// { "name": "Old tyre", "places": { "garage": [ { "position": [-4.4, 0, 3] }, { "position": [-4.4, 0.25, 3], "heading": 30 } ] } }
/// </code>
/// The props the game draws in a place of its own (the radio, the workbench, the lift, the engine stand) are found by
/// their id (<see cref="Find"/>) and take their first place; every other prop with a place in the garage is part of its
/// dressing (<see cref="FindAll"/>), drawn where its places say.
/// </summary>
public static class GarageProps
{
    public const string FileName = "prop.json";

    private sealed class PropJson
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }

        /// <summary>Who made the model, as its licence wants them named in the game</summary>
        [JsonPropertyName("credit")] public string? Credit { get; set; }

        /// <summary>By garage, a place or a list of places</summary>
        [JsonPropertyName("places")] public Dictionary<string, JsonElement>? Places { get; set; }
    }

    private sealed class PlaceJson
    {
        [JsonPropertyName("position")] public float[]? Position { get; set; }
        [JsonPropertyName("heading")] public float Heading { get; set; }

        /// <summary>Degrees the model is tipped forward, about its own x (a tyre laid flat: 90)</summary>
        [JsonPropertyName("tilt")] public float Tilt { get; set; }

        /// <summary>Degrees the model is tipped over to its side, about its own z</summary>
        [JsonPropertyName("roll")] public float Roll { get; set; }
    }

    /// <summary>The prop <paramref name="id"/> in the garage whose showroom is <paramref name="showroomKn5"/>, at its first place there; null when it has no place there</summary>
    /// <param name="builtIn">The game draws a model of its own for the prop (the workbench): a prop.json naming no model has that one, its <see cref="PlacedProp.Model"/> empty</param>
    public static PlacedProp? Find(ModSet mods, string id, string? showroomKn5, List<string>? problems = null, bool builtIn = false) =>
        Places(mods, id, showroomKn5, problems, builtIn).FirstOrDefault();

    /// <summary>
    /// Every place of every prop in the garage whose showroom is <paramref name="showroomKn5"/>, but the ones the game draws
    /// in places of its own (<paramref name="except"/>): the garage's dressing, by the props' ids
    /// </summary>
    public static IReadOnlyList<PlacedProp> FindAll(ModSet mods, string? showroomKn5, IEnumerable<string> except, List<string>? problems = null)
    {
        if (string.IsNullOrEmpty(showroomKn5)) return [];
        var skipped = except.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return mods.SubFolders(ContentTypes.Props)
            .Select(f => Path.GetFileName(f.Dir))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => !skipped.Contains(id))
            .Order(StringComparer.OrdinalIgnoreCase)
            .SelectMany(id => Places(mods, id, showroomKn5, problems, builtIn: false))
            .ToList();
    }

    /// <summary>The corners of the box round a prop's model, its nodes where they put their meshes; null for one with no mesh</summary>
    public static (Vector3 Min, Vector3 Max)? BoundsOf(AcTools.Kn5File.IKn5 kn5) => Of(kn5.RootNode, Matrix4x4.Identity);

    private static (Vector3 Min, Vector3 Max)? Of(AcTools.Kn5File.Kn5Node node, Matrix4x4 parent)
    {
        if (node.NodeClass != AcTools.Kn5File.Kn5NodeClass.Base) return StreetCorsa.Parts.Kn5Bounds.Of(node.Vertices, parent);

        var world = Matrices.ToNumerics(node.Transform) * parent;
        return node.Children.Aggregate(((Vector3, Vector3)?)null, (box, child) => StreetCorsa.Parts.Kn5Bounds.Union(box, Of(child, world)));
    }

    private static List<PlacedProp> Places(ModSet mods, string id, string? showroomKn5, List<string>? problems, bool builtIn)
    {
        if (string.IsNullOrEmpty(showroomKn5)) return [];
        var garage = Path.GetFileName(Path.GetDirectoryName(showroomKn5)) ?? "";

        var found = new List<PlacedProp>();
        // The model an earlier mod's prop of this id has, with its name and credit: a later one naming no model keeps them
        (string Model, string? Name, string? Credit)? earlier = null;
        foreach (var (mod, dir) in mods.SubFolders(ContentTypes.Props))
        {
            if (!string.Equals(Path.GetFileName(dir), id, StringComparison.OrdinalIgnoreCase)) continue;

            var json = Path.Combine(dir, FileName);
            if (!File.Exists(json)) continue;
            try
            {
                var prop = JsonSerializer.Deserialize<PropJson>(File.ReadAllText(json), ModJson.Options);
                // The model is the prop's own, in its folder: a path out of it (rooted, "..") is not looked for. One naming
                // none and with no <id>.kn5 of its own keeps an earlier mod's (a setting puts the core's stand in its own
                // garage), else a prop the game draws itself has the game's
                var model = "";
                var name = prop?.Name;
                var credit = prop?.Credit;
                var named = !string.IsNullOrWhiteSpace(prop?.Model);
                if (!named && earlier is { } kept && !File.Exists(Path.Combine(dir, id + ".kn5")))
                {
                    model = kept.Model;
                    name ??= kept.Name;
                    credit ??= kept.Credit;
                }
                else if ((!builtIn || named) && !TryModel(mod.Id, dir, id, prop?.Model, problems, out model))
                {
                    continue;
                }

                if (model.Length > 0) earlier = (model, name, credit);

                // A prop of this id without a place in this garage takes the earlier one's out of it too (no entry for the
                // garage is a default element, Undefined: no place, not a bad one)
                found = [];
                var entry = prop?.Places?.FirstOrDefault(p => string.Equals(p.Key, garage, StringComparison.OrdinalIgnoreCase)).Value;
                if (prop == null || entry is not { ValueKind: not JsonValueKind.Undefined } places) continue;

                var list = places.ValueKind == JsonValueKind.Array ? places.EnumerateArray().ToList() : [places];
                foreach (var element in list)
                {
                    // A spot or an angle past what a float holds would put the model nowhere the renderer can draw
                    if (element.ValueKind != JsonValueKind.Object
                        || element.Deserialize<PlaceJson>(ModJson.Options) is not { Position: { Length: 3 } at } place
                        || !(at.All(float.IsFinite) && float.IsFinite(place.Heading) && float.IsFinite(place.Tilt) && float.IsFinite(place.Roll)))
                    {
                        problems?.Add($"{mod.Id}/props/{id}: a place in {garage} is no position [x, y, z] with angles the game can use; left out");
                        continue;
                    }

                    found.Add(new PlacedProp(id, name ?? id, model, new Vector3(at[0], at[1], at[2]), place.Heading, credit, place.Tilt, place.Roll));
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems?.Add($"{mod.Id}/props/{id}/{FileName} could not be read: {ex.Message}");
            }
        }

        return found;
    }

    /// <summary>
    /// A prop's model, the file its prop.json names (<paramref name="named"/>; <c>&lt;id&gt;.kn5</c> when it names none) in
    /// its folder <paramref name="dir"/>: false, with a line in <paramref name="problems"/>, for one outside the folder
    /// (rooted, "..": not looked for) or missing. Shared by the garage's props, the actors (<see cref="Station.ActorProps"/>)
    /// and the streets' pieces (<see cref="Street.StreetKit"/>), told by the content type they are of.
    /// </summary>
    internal static bool TryModel(string modId, string dir, string id, string? named, List<string>? problems, out string model,
        string type = ContentTypes.Props)
    {
        if (!PathNames.TryCombineUnder(dir, string.IsNullOrWhiteSpace(named) ? id + ".kn5" : named, out model))
        {
            problems?.Add($"{modId}/{type}/{id}: the model {named} is outside its folder; left out");
            return false;
        }

        if (!File.Exists(model))
        {
            problems?.Add($"{modId}/{type}/{id}: no model {Path.GetFileName(model)}");
            return false;
        }

        return true;
    }
}
