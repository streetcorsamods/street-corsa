using StreetCorsa.Services.Time;

namespace StreetCorsa.Rules;

/// <summary>How long things take, in minutes of the game's clock (<c>"time"</c> in the rules)</summary>
public sealed class TimeRules
{
    /// <summary>
    /// Each action the player takes, by its name (<see cref="GameAction"/>); one the table leaves out takes
    /// <see cref="OtherAction"/>
    /// </summary>
    public Dictionary<string, int> Actions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(GameAction.VisitNewspaper)] = 30,
        [nameof(GameAction.BuyCar)] = 120,
        [nameof(GameAction.VisitDiner)] = 30,
        [nameof(GameAction.DragRace)] = 30,
        [nameof(GameAction.RoadRace)] = 60,
        [nameof(GameAction.VisitUsedParts)] = 30,
        [nameof(GameAction.BuyPart)] = 60,
        [nameof(GameAction.GarageWorkMinor)] = 30,
        [nameof(GameAction.GarageWorkMajor)] = 120,
        [nameof(GameAction.SellCar)] = 60,
        [nameof(GameAction.PlaceAd)] = 30,
        [nameof(GameAction.SwitchCar)] = 15,
        [nameof(GameAction.FreeRun)] = 60,
        [nameof(GameAction.CollectFromImpound)] = 60,
        [nameof(GameAction.TestAndTune)] = 60,
        [nameof(GameAction.GarageChore)] = 15,
        [nameof(GameAction.DriveToStation)] = 20,
        [nameof(GameAction.TowToStation)] = 60,
        [nameof(GameAction.PaintJob)] = 240
    };

    /// <summary>An action the table has no time for</summary>
    public int OtherAction { get; set; } = 30;

    /// <summary>Each part taken off or put on at the workbench</summary>
    public int PerPart { get; set; } = 5;

    /// <summary>An engine taken out of a car or put in one: a big job</summary>
    public int EngineInOut { get; set; } = 120;

    /// <summary>An engine bolted to the stand or taken off it</summary>
    public int Stand { get; set; } = 20;

    /// <summary>The garage's lift taking the car from one stop to the next</summary>
    public int Lift { get; set; } = 5;

    /// <summary>Each tick of the clock while cruising the street</summary>
    public int CruiseTick { get; set; } = 5;

    /// <summary>The least a trip across town takes, however close (a dealer's or a shop's own travel time is longer)</summary>
    public int ShortestTrip { get; set; } = 15;

    /// <summary>The minutes an action takes: the table's, never less than 0</summary>
    public int Of(GameAction action) =>
        Math.Max(0, Actions.TryGetValue(action.ToString(), out var minutes) ? minutes : OtherAction);

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(CruiseTick), 1, 1440, problems);
        Figures.Within(this, path, nameof(OtherAction), 0, 1440, problems);
        Figures.Within(this, path, nameof(PerPart), 0, 1440, problems);
        Figures.Within(this, path, nameof(EngineInOut), 0, 1440, problems);
        Figures.Within(this, path, nameof(Stand), 0, 1440, problems);
        Figures.Within(this, path, nameof(Lift), 0, 1440, problems);
        Figures.Within(this, path, nameof(ShortestTrip), 0, 1440, problems);
    }
}
