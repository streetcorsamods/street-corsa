namespace StreetCorsa.Rules;

/// <summary>The rivals racing among themselves off screen (<c>"simulation"</c> in the rules)</summary>
public sealed class SimulationRules
{
    /// <summary>The loser of a road race crashes this often, a drag racer less</summary>
    public double RoadCrashChance { get; set; } = 0.03;

    public double DragCrashChance { get; set; } = 0.005;

    /// <summary>Share of road races between rivals run for pink slips on the medium pink-slip frequency</summary>
    public double RoadPinkSlipChance { get; set; } = 0.1;

    /// <summary>Each chance from 0 to 1</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(RoadCrashChance), 0, 1, problems);
        Figures.Within(this, path, nameof(DragCrashChance), 0, 1, problems);
        Figures.Within(this, path, nameof(RoadPinkSlipChance), 0, 1, problems);
    }
}
