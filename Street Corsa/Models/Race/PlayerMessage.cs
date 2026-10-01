using StreetCorsa.Services.Text;

namespace StreetCorsa.Models.Race
{
    /// <summary>
    /// Something the player should be told after a race (an event reward, a forfeit, a milestone). Services
    /// return these instead of opening dialogs; the screen that ran the race shows them, one at a time.
    /// </summary>
    public sealed record PlayerMessage(string Title, string Text)
    {
        /// <summary>A drag race's timeslips, shown as a timeslip rather than as text; null for any other message</summary>
        public TimeslipCard? Timeslip { get; init; }

        /// <summary>
        /// The player's car was towed home after a crash: the race hands the player back in the garage, where the car
        /// is, rather than on the streets
        /// </summary>
        public bool TowedToGarage { get; init; }

        /// <summary>The game is won: shown as the victory screen rather than as text; null for any other message</summary>
        public VictoryCard? Victory { get; init; }
    }

    /// <summary>The game won: the path that won it, and the career that got there</summary>
    public sealed record VictoryCard(string VictoryName, string Description, IReadOnlyList<VictoryStat> Stats, string Footnote)
    {
        public static VictoryCard For(string victoryName, string description, GameState.GameState gameState, string footnote = "")
        {
            var stats = gameState.Player.Stats;
            var days = gameState.Career.GetCounter(Career.Milestones.MilestoneTrigger.DaysPlayed);
            return new VictoryCard(victoryName, description,
            [
                new(GameText.Say("victory.won_on"), GameText.Say("victory.won_on.date", ("date", gameState.Date))),
                new(GameText.Say("victory.days"), days.ToString("N0")),
                new(GameText.Say("victory.races"), GameText.Say("victory.races.value", ("races", stats.Races), ("wins", stats.Wins), ("losses", stats.Losses))),
                new(GameText.Say("victory.reputation"), GameText.Say("victory.reputation.value", ("reputation", stats.Reputation), ("tier", stats.GetReputationTier()))),
                new(GameText.Say("victory.pink_slips"), stats.PinkSlipsWon.ToString("N0")),
                new(GameText.Say("victory.cars"), gameState.Player.Cars.Count.ToString("N0")),
                new(GameText.Say("victory.net_earnings"), Helpers.Money.Dollars(stats.NetEarnings))
            ], footnote);
        }
    }

    /// <summary>One line of the victory screen's career sheet</summary>
    public sealed record VictoryStat(string Label, string Value);

    /// <summary>
    /// Timeslips as the strip hands them out, a column each: both lanes of a drag race, or a test-and-tune's passes.
    /// <paramref name="Note"/> is a line under the slip (who won a bracket race, a new best); null for none.
    /// </summary>
    public sealed record TimeslipCard(IReadOnlyList<TimeslipLane> Lanes, string? Note = null, string? Title = null);

    /// <summary>One column of a <see cref="TimeslipCard"/>: whose it is, the slip (null for a car that never left), its dial-in in a bracket race</summary>
    public sealed record TimeslipLane(string Name, Timeslip? Slip, double? DialIn = null);
}
