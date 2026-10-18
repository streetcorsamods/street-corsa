using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace StreetCorsa.Parts;

/// <summary>
/// A complete engine as a list of parts: what a car left the factory with, or a build someone wrote down.
/// Stored as engine_builds.json next to the part packs.
/// </summary>
public class EngineBuild
{
    public const string FileName = "engine_builds.json";

    /// <summary>Next to the builds: ids of builds that are gone, with the build that took their place</summary>
    public const string AliasesFileName = "build_aliases.json";

    public const string OriginCar = "factory";
    public const string OriginRated = "rated";
    public const string OriginKit = "kit";

    /// <summary>Unique id, e.g. "factory/dodge-charger-r-t-69" or "rated/mopar-340-six-pack-290-hp"</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>In a mod: the full id of another mod's build this one takes the place of, on purpose</summary>
    [JsonProperty("replaces", NullValueHandling = NullValueHandling.Ignore)]
    public string? Replaces { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What the build is: what a car left the factory with (<see cref="OriginCar"/>), an engine with a known output to
    /// hold the engine model to (<see cref="OriginRated"/>), a pack's engine kit (<see cref="OriginKit"/>) or a mod's
    /// </summary>
    [JsonProperty("origin")]
    public string Origin { get; set; } = string.Empty;

    /// <summary>Power the build is known to make, in hp; the engine model is held to these (EngineBench rated)</summary>
    [JsonProperty("rated_power")]
    public double? RatedPower { get; set; }

    /// <summary>
    /// Built from speed-shop parts: an engine as a hot rodder put it together, never one a car left the factory with.
    /// It turns up whole (in the paper, at a junkyard), but no car gets it as its factory engine, and no swap takes it
    /// for a bigger engine of the family: its gain is the parts', which the tune-ups buy one by one.
    /// </summary>
    [JsonProperty("aftermarket", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool Aftermarket { get; set; }

    /// <summary>Part ids, the engine block first, then everything that goes on it</summary>
    [JsonProperty("parts")]
    public List<string> Parts { get; set; } = new();

    /// <summary>An engine_builds.json that says null for the name or the parts, or has a null part, reads as if it had none</summary>
    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context)
    {
        Name ??= string.Empty;
        Origin ??= string.Empty;
        Parts ??= new();
        Parts.RemoveAll(string.IsNullOrWhiteSpace);
    }
}
