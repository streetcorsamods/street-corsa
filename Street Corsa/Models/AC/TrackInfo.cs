using System.Text.Json.Serialization;

namespace StreetCorsa.Models.AC;

/// <summary>
/// Defines the type of racing a track supports
/// </summary>
public enum TrackType
{
    Unknown,
    Dragstrip,
    Circuit
}

/// <summary>
/// Represents track metadata from Assetto Corsa's ui_track.json file
/// </summary>
public class TrackInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("length")]
    public string Length { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public string Width { get; set; } = string.Empty;

    [JsonPropertyName("pitboxes")]
    public string Pitboxes { get; set; } = string.Empty;

    [JsonPropertyName("run")]
    public string Run { get; set; } = string.Empty;

    /// <summary>
    /// Gets the track type based on the Run property
    /// </summary>
    [JsonIgnore]
    public TrackType Type => Run?.ToLowerInvariant() switch
    {
        "dragstrip" => TrackType.Dragstrip,
        "clockwise" or "anticlockwise" => TrackType.Circuit,
        _ => TrackType.Unknown
    };

    /// <summary>
    /// A layout raced from A to B, whose finish no car crosses twice: its ui_track.json says so (the layout's own, else
    /// the track's) in "run" or a tag: point to point, A to B, p2p, hillclimb, touge. A race of more than one lap cannot
    /// be run there. AC keeps no surer mark, so a layout that says nothing counts as a loop.
    /// </summary>
    public bool IsPointToPoint(string? configFolder)
    {
        var layout = configFolder == null ? null
            : Configurations.FirstOrDefault(c => string.Equals(c.FolderName, configFolder, StringComparison.OrdinalIgnoreCase));
        var run = layout is { Run.Length: > 0 } ? layout.Run : Run;
        var tags = layout is { Tags.Count: > 0 } ? layout.Tags : Tags;
        return SaysPointToPoint(run) || tags.Any(SaysPointToPoint);
    }

    private static readonly string[] PointToPointWords = ["pointtopoint", "atob", "p2p", "a2b", "hillclimb", "touge"];

    private static bool SaysPointToPoint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var plain = new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return PointToPointWords.Contains(plain);
    }

    /// <summary>
    /// The track's folder ID (e.g., "ks_brands_hatch")
    /// </summary>
    public string TrackId { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the track's content folder
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Available configurations/variants for this track
    /// </summary>
    public List<TrackConfiguration> Configurations { get; set; } = new();

    /// <summary>A copy of the track with only <paramref name="layouts"/> of its layouts</summary>
    public TrackInfo WithLayouts(List<TrackConfiguration> layouts)
    {
        var copy = (TrackInfo)MemberwiseClone();
        copy.Configurations = layouts;
        return copy;
    }
}

/// <summary>
/// Represents a track configuration/layout variant
/// </summary>
public class TrackConfiguration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The folder name for this configuration (e.g., "drag200", "gp")
    /// </summary>
    [JsonIgnore]
    public string FolderName { get; set; } = string.Empty;

    [JsonPropertyName("length")]
    public string Length { get; set; } = string.Empty;

    [JsonPropertyName("pitboxes")]
    public string Pitboxes { get; set; } = string.Empty;

    [JsonPropertyName("run")]
    public string Run { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}
