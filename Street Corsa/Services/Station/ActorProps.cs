using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;
using StreetCorsa.Services.Garage;

namespace StreetCorsa.Services.Station;

/// <summary>
/// A figure that moves (the gas station's attendant): its model, a KN5 with a skin on bones (tools/Props/ActorBuilder),
/// the animation that moves it round and round (a .ksanim next to the model) and how long one turn of it takes
/// </summary>
public sealed record ActorProp(string Id, string Name, string Model, string? Animation, float Seconds, string? Credit);

/// <summary>
/// The actors in the mods' props folders (<c>Content/&lt;mod&gt;/props/&lt;id&gt;/prop.json</c>): a prop whose file names an
/// <c>animation</c>, with the <c>seconds</c> one turn of it takes. A later mod's prop of an id replaces an earlier one's,
/// as the garage's props do; one that names no model (and has no <c>&lt;id&gt;.kn5</c> of its own) keeps the earlier one's
/// model and credit, and its animation unless it gives one. A scene names the actor it wants by id and says where it stands.
/// <code>
/// { "name": "Attendant", "model": "attendant.kn5", "animation": "talk.ksanim", "seconds": 24.8, "credit": "..." }
/// </code>
/// </summary>
public static class ActorProps
{
    private sealed class PropJson
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("animation")] public string? Animation { get; set; }
        [JsonPropertyName("seconds")] public float? Seconds { get; set; }
        [JsonPropertyName("credit")] public string? Credit { get; set; }
    }

    /// <summary>The most one turn of an animation may take; a file asking more, or nothing, plays nothing</summary>
    public const float MostSeconds = 3600f;

    /// <summary>
    /// The actor <paramref name="id"/> of the last mod that has it with a model; null when none does (a line in
    /// <paramref name="problems"/> for one that is there but broken). A model or animation named outside the prop's
    /// folder is not looked for.
    /// </summary>
    public static ActorProp? Find(ModSet mods, string id, List<string>? problems = null)
    {
        ActorProp? found = null;
        foreach (var (mod, dir) in mods.SubFolders(ContentTypes.Props))
        {
            if (!string.Equals(Path.GetFileName(dir), id, StringComparison.OrdinalIgnoreCase)) continue;
            var json = Path.Combine(dir, GarageProps.FileName);
            if (!File.Exists(json)) continue;
            try
            {
                var prop = JsonSerializer.Deserialize<PropJson>(File.ReadAllText(json), ModJson.Options);
                // One naming no model and with no <id>.kn5 of its own keeps the earlier one's, as a garage prop does
                var kept = string.IsNullOrWhiteSpace(prop?.Model) && !File.Exists(Path.Combine(dir, id + ".kn5")) ? found : null;
                var model = kept?.Model ?? "";
                if (kept == null && !GarageProps.TryModel(mod.Id, dir, id, prop?.Model, problems, out model)) continue;

                var animation = kept?.Animation;
                var seconds = kept?.Seconds ?? 0f;
                if (prop?.Animation is { Length: > 0 } named)
                {
                    if (!PathNames.TryCombineUnder(dir, named, out var file) || !File.Exists(file))
                        problems?.Add($"{mod.Id}/props/{id}: no animation {named} in its folder; it stands still");
                    else if (prop.Seconds is not { } s || !(s > 0f) || s > MostSeconds)
                        problems?.Add($"{mod.Id}/props/{id}: the animation's seconds must be more than 0 and at most {MostSeconds}; it stands still");
                    else (animation, seconds) = (file, s);
                }

                found = new ActorProp(id, prop?.Name ?? kept?.Name ?? id, model, animation, seconds, kept != null ? kept.Credit : prop?.Credit);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems?.Add($"{mod.Id}/props/{id}/{GarageProps.FileName} could not be read: {ex.Message}");
            }
        }

        return found;
    }
}
