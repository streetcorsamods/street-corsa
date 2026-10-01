namespace StreetCorsa.Rules;

/// <summary>The race events (<c>"events"</c> in the rules): how often invitations come</summary>
public sealed class EventRules
{
    /// <summary>How likely an eligible event is to turn up when events are generated</summary>
    public double EventChance { get; set; } = 0.5;

    /// <summary>Most invitations open in the paper at once</summary>
    public int MaxOpenEvents { get; set; } = 5;
}
