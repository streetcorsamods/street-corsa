using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Mods;

namespace StreetCorsa.Parts.Logic;

/// <summary>
/// The fuels a fuel system can run on, from fuels.json: what a kg of the mixture burns to, the air to fuel ratio it
/// burns best at and how long it takes to burn. A fuel system names its fuel (<c>"fuel": "methanol"</c>); one that
/// names none, or one there is no such fuel for, runs on <see cref="DefaultId"/>. A mod adds fuels or changes one by id.
/// </summary>
public sealed class Fuels
{
    public const string FileName = "fuels.json";
    public const string DefaultId = "regular";

    /// <summary>Pump gas as the game has always burnt it, when no file gives any fuel</summary>
    public static readonly Fuel Regular = new() { Heat = 1.0995, BestRatio = 12.5, BurnTime = 2.5, BurnTimeOff = 0.8 };

    private static readonly Lazy<Fuels> InstalledFuels = new(() => Load(ModSet.Installed.Folders(ContentTypes.Parts).Select(f => f.Dir)));
    private static volatile Fuels? _current;

    public static Fuels Installed => InstalledFuels.Value;

    /// <summary>The fuels in use: the installed ones, or ones a test or a tool sets</summary>
    public static Fuels Current
    {
        get => _current ?? Installed;
        set => _current = value;
    }

    private readonly Dictionary<string, Fuel> _fuels;

    private Fuels(Dictionary<string, Fuel> fuels, List<string> problems)
    {
        _fuels = fuels;
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }

    public IReadOnlyDictionary<string, Fuel> All => _fuels;

    public Fuel Get(string? id) =>
        (id != null ? _fuels.GetValueOrDefault(id) : null) ?? _fuels.GetValueOrDefault(DefaultId) ?? Regular;

    public static Fuels Load(IEnumerable<string> partsFolders)
    {
        var problems = new List<string>();
        var fuels = new Dictionary<string, Fuel>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in partsFolders)
        {
            var file = Path.Combine(dir, FileName);
            if (!File.Exists(file)) continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file), ModJson.DocumentOptions);
                foreach (var entry in document.RootElement.GetProperty("fuels").EnumerateObject())
                {
                    var fuel = entry.Value.Deserialize<Fuel>(ModJson.Options);
                    if (fuel is not { Heat: > 0 and < 100, BestRatio: > 0 and < 100 } || !(fuel.BurnTime > 0) || !double.IsFinite(fuel.BurnTimeOff))
                    {
                        problems.Add($"{file}: fuel {entry.Name} needs a heat, a best ratio and a burn time over 0; left out");
                        continue;
                    }

                    fuels[entry.Name] = fuel;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
            {
                problems.Add($"{file}: {ex.Message}; left out");
            }
        }

        return new Fuels(fuels, problems);
    }
}

public sealed class Fuel
{
    /// <summary>What a kg of the mixture at its best ratio gives off burning, MJ</summary>
    [JsonPropertyName("heat")]
    public double Heat { get; set; }

    /// <summary>Air to fuel by mass where it burns best</summary>
    [JsonPropertyName("best_ratio")]
    public double BestRatio { get; set; }

    /// <summary>How long the mixture takes to burn at its best ratio, ms, and how much longer at its worst</summary>
    [JsonPropertyName("burn_time")]
    public double BurnTime { get; set; }

    [JsonPropertyName("burn_time_off")]
    public double BurnTimeOff { get; set; }
}
