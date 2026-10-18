namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// Where the garage's lift has a car (<see cref="Car.LiftStop"/>): on the floor, up a little with its wheels hanging
    /// clear of it, or at full height to work underneath
    /// </summary>
    public enum LiftStop
    {
        Down,
        WheelsClear,
        Full
    }
}
