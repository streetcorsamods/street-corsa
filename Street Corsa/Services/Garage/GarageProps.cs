using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Garage
{
    /// <summary>A prop set into a garage: its model, and where it stands there (on the floor or on a bench top)</summary>
    public sealed record PlacedProp(string Id, string Name, string Model, Vector3 Position, float Heading)
    {
        /// <summary>Where the model goes, as the renderer takes it: turned about the vertical, then moved to its spot</summary>
        public Matrix4x4 Placement => Matrix4x4.CreateRotationY(Heading * MathF.PI / 180f) * Matrix4x4.CreateTranslation(Position);
    }

    /// <summary>
    /// The props in the mods' props folders (<c>Content/&lt;mod&gt;/props/&lt;id&gt;/prop.json</c>): a model and, by garage
    /// (the showroom's folder name), where it stands in it. A later mod's prop of an id replaces an earlier one's, so a
    /// mod can swap the radio for another set, or put it somewhere else in its own garage.
    /// <code>
    /// { "name": "Radio", "model": "radio.kn5", "places": { "garage": { "position": [2.62, 1.29, -8.6], "heading": -23 } } }
    /// </code>
    /// </summary>
    public static class GarageProps
    {
        public const string FileName = "prop.json";

        private sealed class PropJson
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("model")] public string? Model { get; set; }
            [JsonPropertyName("places")] public Dictionary<string, PlaceJson>? Places { get; set; }
        }

        private sealed class PlaceJson
        {
            [JsonPropertyName("position")] public float[]? Position { get; set; }
            [JsonPropertyName("heading")] public float Heading { get; set; }
        }

        /// <summary>The prop <paramref name="id"/> in the garage whose showroom is <paramref name="showroomKn5"/>; null when it has no place there</summary>
        public static PlacedProp? Find(ModSet mods, string id, string? showroomKn5, List<string>? problems = null)
        {
            if (string.IsNullOrEmpty(showroomKn5)) return null;
            var garage = Path.GetFileName(Path.GetDirectoryName(showroomKn5)) ?? "";

            PlacedProp? found = null;
            foreach (var (mod, dir) in mods.SubFolders(ContentTypes.Props))
            {
                if (!string.Equals(Path.GetFileName(dir), id, StringComparison.OrdinalIgnoreCase)) continue;

                var json = Path.Combine(dir, FileName);
                if (!File.Exists(json)) continue;
                try
                {
                    var prop = JsonSerializer.Deserialize<PropJson>(File.ReadAllText(json), ModJson.Options);
                    var model = Path.Combine(dir, prop?.Model ?? id + ".kn5");
                    if (!File.Exists(model))
                    {
                        problems?.Add($"{mod.Id}/props/{id}: no model {prop?.Model}");
                        continue;
                    }

                    // A prop of this id without a place in this garage takes the earlier one's out of it too
                    found = prop?.Places?.FirstOrDefault(p => string.Equals(p.Key, garage, StringComparison.OrdinalIgnoreCase)).Value is { Position: { Length: 3 } at } place
                        ? new PlacedProp(id, prop.Name ?? id, model, new Vector3(at[0], at[1], at[2]), place.Heading)
                        : null;
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    problems?.Add($"{mod.Id}/props/{id}/{FileName} could not be read: {ex.Message}");
                }
            }

            return found;
        }
    }
}
