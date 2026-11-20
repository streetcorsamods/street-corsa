using Newtonsoft.Json;

namespace StreetCorsa.Models.Race
{
    /// <summary>
    /// How a drive for the score went, as a mode that scores one writes it beside the racer's <c>score</c>
    /// (<c>participants[].drive</c>): a crash costs points, not the run, so the car is never <c>crash.crashed</c>
    /// </summary>
    public class ScoredDrive
    {
        /// <summary>How many times the car hit something</summary>
        [JsonProperty("crashes")]
        public int Crashes { get; set; }

        /// <summary>The run had no crash</summary>
        [JsonProperty("clean")]
        public bool Clean { get; set; }

        /// <summary>The average speed over the run, km/h</summary>
        [JsonProperty("average_kmh")]
        public double AverageKmh { get; set; }

        /// <summary>What the crashes (or a clean run) made of the score: 1 is neither</summary>
        [JsonProperty("factor")]
        public double? Factor { get; set; }
    }

    /// <summary>The traffic on the road, as a mode that runs one writes it (<c>session.traffic</c>)</summary>
    public class SessionTraffic
    {
        /// <summary>How many traffic cars the road carried at once</summary>
        [JsonProperty("density")]
        public int? Density { get; set; }

        /// <summary>The cast, <c>id:weight</c> separated by spaces; empty: every model there was</summary>
        [JsonProperty("cars")]
        public string? Cars { get; set; }
    }
}
