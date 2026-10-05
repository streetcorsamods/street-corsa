namespace StreetCorsa.Rules;

/// <summary>The race events (<c>"events"</c> in the rules): how often invitations come</summary>
public sealed class EventRules
{
    /// <summary>How likely an eligible event is to turn up when events are generated</summary>
    public double EventChance { get; set; } = 0.5;

    /// <summary>Most invitations open in the paper at once</summary>
    public int MaxOpenEvents { get; set; } = 5;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(MaxOpenEvents), 0, 100, problems);
    }
}
