namespace StreetCorsa.Models.GameState
{
    public enum RacerType
    {
        Player,
        AI
    }

    public enum RacerStatus
    {
        Inactive,
        Retired,
        ReadyToRace,

        /// <summary>Gone for good: sold up and left town (<see cref="RacerCollection.Departed"/>)</summary>
        Departed
    }
}
