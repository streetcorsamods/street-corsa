using System.Globalization;
using Newtonsoft.Json.Linq;

namespace StreetCorsa.Parts.Logic;

/// <summary>
/// A part's figures as it sits in an assembly: the player's tuning over the pack's specs. Reading a spec never throws:
/// what a part does not give is the fallback.
/// </summary>
public static class PartSpecs
{
    public static double Spec(this InstalledPart part, string key, double fallback = 0)
    {
        if (part.Tuning.TryGetValue(key, out var tuned) && double.IsFinite(tuned)) return tuned;
        return part.Definition.Number(key, fallback);
    }

    /// <summary>One element of a list spec ("ratio", 2), the tuning's "ratio[2]" over it</summary>
    public static double SpecAt(this InstalledPart part, string key, int index, double fallback = 0)
    {
        if (part.Tuning.TryGetValue($"{key}[{index.ToString(CultureInfo.InvariantCulture)}]", out var tuned) && double.IsFinite(tuned)) return tuned;
        return part.Definition.Element(key, index, fallback);
    }

    /// <summary>The slot that takes a role on the part ("crankshaft" → 8); 0 when it has none</summary>
    public static int RoleSlot(this InstalledPart part, string role) => part.Definition.SlotRoles.GetValueOrDefault(role);

    /// <summary>What is mounted on the part's slot of that role, when it is of the class (any, for none); null otherwise</summary>
    public static InstalledPart? OnRole(this InstalledPart part, string role, string? className = null) =>
        part.RoleSlot(role) is > 0 and var slot ? part.OnSlot(slot, className) : null;

    /// <summary>
    /// What is on one of the part's slots, when it is of the class: a child, or the parent through the slot the part
    /// hangs by. Null otherwise.
    /// </summary>
    public static InstalledPart? OnSlot(this InstalledPart part, int slot, string? className)
    {
        var found = slot == part.OwnSlot && part.Parent != null ? part.Parent : part.Children.GetValueOrDefault(slot);
        return found != null && (className == null || found.Is(className)) ? found : null;
    }

    /// <summary>A number of a list among the part's specs; the fallback for one it does not have</summary>
    public static double Element(this PartDefinition part, string key, int index, double fallback = 0)
    {
        var list = part.Specs.GetValueOrDefault(key);
        object? item = list switch
        {
            JArray array => index >= 0 && index < array.Count ? (array[index] as JValue)?.Value : null,
            IList<object> objects => index >= 0 && index < objects.Count ? objects[index] : null,
            _ => null
        };

        return item is long or int or double or float or decimal ? Convert.ToDouble(item, CultureInfo.InvariantCulture) : fallback;
    }
}
