namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// How far the player has got with one Challenge (<see cref="Services.Career.ChallengeDefinition"/>): the go under way,
    /// road by road, and the best of every go. A go is driven road after road, one run each; the Challenge's score is the
    /// sum of its runs, and it counts once the last road is driven.
    /// </summary>
    public class ChallengeProgress
    {
        /// <summary>The scores of the go under way, road by road (a road with no score is 0); empty when none is</summary>
        public List<double> Go { get; set; } = [];

        /// <summary>A go is under way: <see cref="Go"/> holds its roads driven so far, the next is the one after them</summary>
        public bool Started { get; set; }

        /// <summary>The best total of a go driven to the end; null before one has been</summary>
        public double? BestTotal { get; set; }

        /// <summary>How many goes have been driven to the end</summary>
        public int Finished { get; set; }

        /// <summary>The best run on each road of the Challenge, by its place in it; a road not driven yet is 0</summary>
        public List<double> BestRoads { get; set; } = [];

        /// <summary>The best medal won on each road, by its place in it (<see cref="Medal"/>)</summary>
        public List<Medal> Medals { get; set; } = [];

        /// <summary>The best on road <paramref name="road"/>, 0 when it has not been driven</summary>
        public double BestOn(int road) => road >= 0 && road < BestRoads.Count ? BestRoads[road] : 0;

        public Medal MedalOn(int road) => road >= 0 && road < Medals.Count ? Medals[road] : Medal.None;
    }

    /// <summary>A road's medals, the best last</summary>
    public enum Medal
    {
        None,
        Bronze,
        Silver,
        Gold
    }
}
