using System.Text.Json;
using System.Text.Json.Serialization;

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

    private static readonly PartsFiles<Fuels> Files = new(Load);

    public static Fuels Installed => Files.Installed;

    /// <summary>The fuels in use: the installed ones, or ones a test or a tool sets</summary>
    public static Fuels Current
    {
        get => Files.Current;
        set => Files.Current = value;
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

    /// <summary>
    /// Every mod's file, in load order (the base game's first). A fuel a later file names again takes the figures it
    /// gives over the earlier ones (a mod's <c>"methanol": { "heat": 1.5 }</c> changes the heat alone).
    /// </summary>
    public static Fuels Load(IEnumerable<string> partsFolders)
    {
        var problems = new List<string>();
        var fuels = new Dictionary<string, Fuel>(StringComparer.OrdinalIgnoreCase);
        PartsFiles.ReadEach(partsFolders, FileName, problems, (file, root) =>
        {
            foreach (var entry in root.GetProperty("fuels").EnumerateObject())
            {
                var fuel = fuels.TryGetValue(entry.Name, out var earlier) ? earlier.Copy() : new Fuel();
                if (entry.Value.ValueKind != JsonValueKind.Object || !fuel.Over(entry.Value)
                    || fuel is not { Heat: > 0 and < 100, BestRatio: > 0 and < 100 } || !(fuel.BurnTime > 0) || !double.IsFinite(fuel.BurnTimeOff))
                {
                    problems.Add($"{file}: fuel {entry.Name} needs a heat, a best ratio and a burn time over 0; left out");
                    continue;
                }

                fuels[entry.Name] = fuel;
            }
        });

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

    public Fuel Copy() => (Fuel)MemberwiseClone();

    /// <summary>The figures <paramref name="entry"/> gives over this fuel's; false when one of them is not a number</summary>
    public bool Over(JsonElement entry)
    {
        return Take("heat", v => Heat = v) && Take("best_ratio", v => BestRatio = v) && Take("burn_time", v => BurnTime = v)
               && Take("burn_time_off", v => BurnTimeOff = v);

        bool Take(string name, Action<double> set)
        {
            if (!entry.TryGetProperty(name, out var value)) return true;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)) return false;

            set(number);
            return true;
        }
    }
}
