namespace StreetCorsa.Models.Race
{
    /// <summary>
    /// The traffic on the road of a run in traffic (<see cref="RaceFormat.Traffic"/>), as race.ini's [STREET_CORSA] gives
    /// it to the mode: <c>TRAFFIC_DENSITY</c>, <c>TRAFFIC_CARS</c>, <c>TRAFFIC_PLAN</c>, <c>TRAFFIC_MODELS</c>
    /// </summary>
    public sealed class RaceTraffic
    {
        /// <summary>How many traffic cars the road carries at once</summary>
        public int Cars { get; set; }

        /// <summary>The cast, <c>id:weight</c> separated by spaces; null: every model the traffic tool has</summary>
        public string? Cast { get; set; }

        /// <summary>The player's density the count was scaled by (1: the road's own), kept with the run</summary>
        public double Density { get; set; } = 1;

        /// <summary>A traffic.json of the game's own to drive on; null: the track's</summary>
        public string? Plan { get; set; }

        /// <summary>A folder of traffic models of the game's own; null: the traffic tool's</summary>
        public string? Models { get; set; }
    }
}
