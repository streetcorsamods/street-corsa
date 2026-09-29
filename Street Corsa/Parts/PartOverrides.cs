using System.Globalization;
using Newtonsoft.Json.Linq;
using StreetCorsa.Helpers;
using StreetCorsa.Parts.Scripting;

namespace StreetCorsa.Parts;

/// <summary>One script field a part's pack sets over what its script constructs: a number, or one element of an array</summary>
public sealed record PartOverride(string Field, int? Index, double Value);

/// <summary>
/// A part's <see cref="PartDefinition.Overrides"/> read once, as the catalog loads: each checked against the field its
/// script declares, what does not fit told in the catalog's problems and left out, and the rest put in
/// <see cref="PartDefinition.FieldOverrides"/> for the script runtime and in <see cref="PartDefinition.Properties"/>,
/// so that a price or a spring rate read from the pack is the part's own too.
/// </summary>
public static class PartOverrides
{
    public static void Apply(ScriptClassLoader scripts, IEnumerable<PartDefinition> parts, List<string> problems)
    {
        foreach (var part in parts.Where(p => p.Overrides is { Count: > 0 })) Apply(scripts, part, problems);
    }

    public static void Apply(ScriptClassLoader scripts, PartDefinition part, List<string> problems)
    {
        var read = new List<PartOverride>();
        if (part.Overrides is not { Count: > 0 } overrides)
        {
            part.FieldOverrides = read;
            return;
        }

        if (part.SourceScript == null)
        {
            problems.Add($"{part.Id} has overrides but no script to put them on; left out");
            part.FieldOverrides = read;
            return;
        }

        var chain = ChainOf(scripts, part);

        // A list first, then single elements, whatever order the file has them in: an element named on its own wins
        foreach (var (key, token) in overrides.OrderBy(o => o.Key.Contains('[') ? 1 : 0))
        {
            var label = $"{part.Id} override \"{key}\"";
            if (!TryParseKey(key, out var field, out var index))
            {
                problems.Add($"{label} is neither a field nor a field[element]; left out");
                continue;
            }

            var signature = chain?.Select(c => c.FieldSignature(field)).FirstOrDefault(s => s != null);
            var recorded = part.Properties.GetValueOrDefault(field);
            if (signature == null && recorded == null)
            {
                problems.Add($"{label}: the part's script has no such field (names are the script's, case and all); left out");
                continue;
            }

            if (part.Derived.ContainsKey(field))
            {
                problems.Add($"{label}: the script works that field out itself, from others; left out");
                continue;
            }

            var isArray = signature?.StartsWith('[') ?? recorded is JArray;
            var type = isArray ? signature?[1..] : signature;
            if (type != null ? !ScriptTypes.IsWhole(type) && !ScriptTypes.IsFraction(type) : !IsNumbers(recorded, isArray))
            {
                problems.Add($"{label}: the field holds {(isArray ? "an array of " : "")}something other than numbers; left out");
                continue;
            }

            var length = (recorded as JArray)?.Count;
            if (index != null)
            {
                if (!isArray) problems.Add($"{label}: the field is not an array; left out");
                else if (index >= length) problems.Add($"{label}: the array has {length} elements; left out");
                else if (Number(token, label, type, problems) is { } value) read.Add(new(field, index, value));
            }
            else if (isArray)
            {
                if (token is not JArray items)
                {
                    problems.Add($"{label}: the field is an array; give a list, or name one element (\"{field}[1]\"); left out");
                    continue;
                }

                if (items.Count > length) problems.Add($"{label}: {items.Count} numbers for an array of {length}; the ones past it are left out");
                for (var i = 0; i < Math.Min(items.Count, length ?? items.Count); i++)
                {
                    if (items[i].Type != JTokenType.Null && Number(items[i], $"{label} element {i}", type, problems) is { } value)
                        read.Add(new(field, i, value));
                }
            }
            else if (Number(token, label, type, problems) is { } value)
            {
                read.Add(new(field, null, value));
            }
        }

        part.FieldOverrides = read;
        foreach (var entry in read) Record(part, entry);
    }

    /// <summary>The override as the pack's record of the part: what reads the figures from it gets the part's own</summary>
    private static void Record(PartDefinition part, PartOverride entry)
    {
        if (entry.Index is not { } index) part.Properties[entry.Field] = entry.Value;
        else if (part.Properties.GetValueOrDefault(entry.Field) is JArray array && index < array.Count) array[index] = entry.Value;
    }

    /// <summary>A finite number, cut to a whole one for a whole type as the script would store it; null (and told) otherwise</summary>
    private static double? Number(JToken token, string label, string? type, List<string> problems)
    {
        if (token.Type is not (JTokenType.Integer or JTokenType.Float))
        {
            problems.Add($"{label}: \"{token}\" is not a number; left out");
            return null;
        }

        var value = (double)token;
        if (!double.IsFinite(value))
        {
            problems.Add($"{label}: {token} is not a finite number; left out");
            return null;
        }

        if (!ScriptTypes.IsWhole(type) || value == Math.Truncate(value)) return value;

        problems.Add($"{label}: the field holds whole numbers; {value.ToString(CultureInfo.InvariantCulture)} is taken as {Math.Truncate(value).ToString(CultureInfo.InvariantCulture)}");
        return Math.Truncate(value);
    }

    /// <summary>For a part whose script is not there to tell: whether the pack's record of the field is a number (or numbers)</summary>
    private static bool IsNumbers(object? recorded, bool isArray) =>
        isArray
            ? recorded is JArray array && array.All(t => t.Type is JTokenType.Integer or JTokenType.Float)
            : recorded is long or int or double or float or decimal;

    private static bool TryParseKey(string key, out string field, out int? index)
    {
        index = null;
        field = key;
        var bracket = key.IndexOf('[');
        if (bracket < 0) return key.Length > 0 && !key.Contains(']');

        field = key[..bracket];
        if (field.Length == 0 || !key.EndsWith(']')
            || !int.TryParse(key[(bracket + 1)..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            return false;

        index = parsed;
        return true;
    }

    /// <summary>The part's script and what it extends, as the runtime will make it; null when the script is not there</summary>
    private static List<ScriptClass>? ChainOf(ScriptClassLoader scripts, PartDefinition part) =>
        PathNames.TryCombineUnder(part.ScriptRoot ?? scripts.Root, part.SourceScript, out var file) && scripts.Load(file) is { } script
            ? scripts.Chain(script)
            : null;
}
