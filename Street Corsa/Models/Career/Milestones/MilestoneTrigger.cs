namespace StreetCorsa.Models.Career.Milestones
{
    /// <summary>
    /// Types of events that can trigger milestone progress
    /// </summary>
    public enum MilestoneTrigger
    {
        /// <summary>
        /// Total race wins (any type)
        /// </summary>
        TotalWins,

        /// <summary>
        /// Drag race wins specifically
        /// </summary>
        DragWins,

        /// <summary>
        /// Road/circuit race wins
        /// </summary>
        RoadWins,

        /// <summary>
        /// Pink slip race wins
        /// </summary>
        PinkSlipWins,

        /// <summary>
        /// Reputation level reached
        /// </summary>
        ReputationReached,

        /// <summary>
        /// Total money earned from races
        /// </summary>
        MoneyEarned,

        /// <summary>
        /// Number of cars currently owned
        /// </summary>
        CarsOwned,

        /// <summary>
        /// Game days played
        /// </summary>
        DaysPlayed,

        /// <summary>
        /// Unique opponents defeated at least once
        /// </summary>
        OpponentsDefeated,

        /// <summary>
        /// The banked score of the runs for the score in Challenges: the best run on each road, summed
        /// (<see cref="Services.Career.ChallengeCareer.CareerScore"/>)
        /// </summary>
        TrafficScore,

        /// <summary>
        /// Gold medals won on the Challenges' roads, each road of each Challenge once
        /// </summary>
        GoldMedals,

        /// <summary>
        /// Runs for the score in a Challenge driven without a crash
        /// </summary>
        CleanRuns,

        /// <summary>
        /// Challenges driven to the end, each once
        /// </summary>
        ChallengesDone
    }
}
