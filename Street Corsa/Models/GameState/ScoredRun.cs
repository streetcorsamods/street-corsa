namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// A run alone for the score on one road (a format that may be run so: <c>solo</c>), as the career keeps it: the
    /// road's high-score table is its best runs of as many laps, ranked by score (higher or lower as the format is won),
    /// then time, then the older run first. Three laps of a road score three times one: they are tables apart.
    /// </summary>
    public class ScoredRun
    {
        public string Format { get; set; } = string.Empty;
        public string TrackId { get; set; } = string.Empty;
        public string? TrackConfig { get; set; }

        /// <summary>The laps the run was of (a Free Drive's are the format's, a Challenge road's its own)</summary>
        public int Laps { get; set; } = 1;

        /// <summary>Where the run was driven from: <see cref="Sources.Free"/> (the racing hub) or <see cref="Sources.Challenge"/></summary>
        public string Source { get; set; } = Sources.Free;

        public double Score { get; set; }

        /// <summary>The run's time, in milliseconds</summary>
        public double TimeMs { get; set; }

        public double DistanceKm { get; set; }
        public double AverageKmh { get; set; }
        public int Crashes { get; set; }
        public bool Clean { get; set; }

        /// <summary>The player's car: its model and its instance (a car sold since still names its model)</summary>
        public string CarDefinitionId { get; set; } = string.Empty;
        public Guid CarInstanceId { get; set; }

        /// <summary>The traffic it was driven in: the cars on the road at once, and the player's density (1: the road's own)</summary>
        public int TrafficCars { get; set; }
        public double TrafficDensity { get; set; } = 1;

        /// <summary>The game's date of the run</summary>
        public DateTime Date { get; set; }

        /// <summary>The Challenge it was a road of; null for a Free Drive</summary>
        public string? Challenge { get; set; }

        /// <summary>The race it came from, so one result is never kept twice</summary>
        public Guid ContextId { get; set; }

        /// <summary>Where a run was driven from</summary>
        public static class Sources
        {
            public const string Free = "free";

            /// <summary>A road of a Challenge (<see cref="Challenge"/> names it)</summary>
            public const string Challenge = "challenge";
        }

        /// <summary>Whether this run is on a table: one format, one track, one layout, as many laps</summary>
        public bool SameRoad(string format, string trackId, string? trackConfig, int laps = 1) =>
            string.Equals(Format, format, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TrackId, trackId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TrackConfig ?? string.Empty, trackConfig ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && Math.Max(1, Laps) == Math.Max(1, laps);

        /// <summary>A road's table of runs of <paramref name="laps"/> laps, best first</summary>
        public static List<ScoredRun> Table(IEnumerable<ScoredRun> runs, string format, string trackId, string? trackConfig, bool higherWins, int laps = 1) =>
            Rank(runs.Where(r => r.SameRoad(format, trackId, trackConfig, laps)), higherWins).ToList();

        /// <summary>Best first: by score, then the quicker, then the older</summary>
        private static IOrderedEnumerable<ScoredRun> Rank(IEnumerable<ScoredRun> runs, bool higherWins) =>
            (higherWins ? runs.OrderByDescending(r => r.Score) : runs.OrderBy(r => r.Score))
                .ThenBy(r => r.TimeMs)
                .ThenBy(r => r.Date);

        /// <summary>
        /// The run put on its road's table, which keeps its <paramref name="kept"/> best (the rules'
        /// <see cref="Rules.UnlockRules.TableRuns"/>); returns its place on it (1 the best), or null when it did not make
        /// the table (or was kept already)
        /// </summary>
        public static int? Record(List<ScoredRun> runs, ScoredRun run, bool higherWins, int kept)
        {
            if (runs.Any(r => r.ContextId == run.ContextId && run.ContextId != Guid.Empty)) return null;

            kept = Math.Max(1, kept);
            runs.Add(run);
            var table = Table(runs, run.Format, run.TrackId, run.TrackConfig, higherWins, run.Laps);
            foreach (var dropped in table.Skip(kept)) runs.Remove(dropped);

            var place = table.IndexOf(run);
            return place >= 0 && place < kept ? place + 1 : null;
        }
    }
}
