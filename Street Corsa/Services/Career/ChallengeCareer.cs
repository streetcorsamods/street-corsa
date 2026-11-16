using System.Collections.Concurrent;
using System.IO;
using StreetCorsa.Logging;
using StreetCorsa.Models.Career.Milestones;
using StreetCorsa.Models.GameState;
using StreetCorsa.Rules;
using StreetCorsa.Services.Race.Traffic;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// The career of runs for the score in Challenges (<see cref="ChallengeBook"/>): a go driven road after road, the medals
    /// each road is worth, the banked score (the best Challenge run on each road, summed) and the tiers it opens
    /// (<see cref="UnlockRules"/>). Free Drive runs go on the roads' tables only; they bank nothing.
    /// </summary>
    public static class ChallengeCareer
    {
        private static IAppLogger Logger => LazyLogger.Value;

        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("Challenges"));

        // The reference score of a road (its AI line and laps), read once a run of the game: an AI line is megabytes
        private static readonly ConcurrentDictionary<string, double?> References = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The key a road's best is banked under: format, track and layout</summary>
        public static string RoadKey(string format, string trackId, string? layout) =>
            $"{format}|{trackId}|{layout ?? string.Empty}".ToLowerInvariant();

        /// <summary>The banked score: the best Challenge run on each road, summed</summary>
        public static double CareerScore(CareerState career) => career.RoadBests?.Values.Where(double.IsFinite).Sum() ?? 0;

        /// <summary>The highest tier open at <paramref name="score"/>: every tier when the rules hold none back</summary>
        public static int OpenTier(double score, UnlockRules rules)
        {
            if (rules.Tiers.Length == 0) return StreetCorsa.Parts.Cars.CarEntry.LastTier;

            var open = 0;
            for (var i = 0; i < rules.Tiers.Length; i++)
                if (score >= rules.Tiers[i]) open = i + 1;
            // A tier past the list opens with its last
            return open == rules.Tiers.Length ? StreetCorsa.Parts.Cars.CarEntry.LastTier : Math.Max(1, open);
        }

        /// <summary>The score <paramref name="tier"/> opens at; null when the rules hold nothing back</summary>
        public static long? TierScore(int tier, UnlockRules rules) =>
            rules.Tiers.Length == 0 ? null : rules.Tiers[Math.Clamp(tier, 1, rules.Tiers.Length) - 1];

        /// <summary>
        /// A car's tier: its setting's (<see cref="StreetCorsa.Parts.Cars.CarEntry.Tier"/>), else its power to weight's band
        /// (<see cref="UnlockRules.TierHpPerTonne"/>); one whose figures cannot be read is in the band after the last
        /// </summary>
        public static int TierOf(int? setTier, double? hp, double? kg, UnlockRules rules)
        {
            if (setTier is { } tier) return Math.Clamp(tier, StreetCorsa.Parts.Cars.CarEntry.FirstTier, StreetCorsa.Parts.Cars.CarEntry.LastTier);

            var bands = rules.TierHpPerTonne;
            var last = Math.Min(bands.Length + 1, StreetCorsa.Parts.Cars.CarEntry.LastTier);
            var plausible = RuleBook.Current.Cars;
            if (hp is not { } power || kg is not { } weight || !double.IsFinite(power) || power <= 0
                || weight < plausible.MinPlausibleWeightKg || weight > plausible.MaxPlausibleWeightKg)
                return last;

            var perTonne = power / weight * 1000;
            for (var i = 0; i < bands.Length; i++)
                if (perTonne < bands[i]) return Math.Min(i + 1, last);
            return last;
        }

        /// <summary>
        /// A road's medals: those its Challenge pins, else the rules' shares of its reference score (its distance at its
        /// AI line's speed, km × km/h × 10, rounded to tens); null when the road has neither (no AI line with speeds)
        /// </summary>
        /// <param name="layout">The layout driven: the road's own, or the track's first when it names none</param>
        public static ChallengeMedals? MedalsOf(ChallengeRoad road, int laps, string? trackFolder, string? layout, UnlockRules rules)
        {
            if (road.Medals is { InOrder: true } pinned) return pinned;
            if (ReferenceScore(trackFolder, layout, laps) is not { } reference) return null;

            static long Round(double score) => Math.Max(10, (long)Math.Round(score / 10) * 10);
            return new ChallengeMedals
            {
                Bronze = Round(reference * rules.Bronze),
                Silver = Round(reference * rules.Silver),
                Gold = Round(reference * rules.Gold)
            };
        }

        /// <summary>
        /// Whether a road's medals can be told without reading its AI line (megabytes, off the disk): they are pinned,
        /// the line was read before, or there is none
        /// </summary>
        public static bool MedalsAtHand(ChallengeRoad road, string? trackFolder, string? layout) =>
            road.Medals is { InOrder: true } || string.IsNullOrEmpty(trackFolder) || AiLine.FileOf(trackFolder, layout) is not { } file
            || References.ContainsKey(file);

        /// <summary>The score of <paramref name="laps"/> laps of a road's AI line at its own speed; null without one</summary>
        public static double? ReferenceScore(string? trackFolder, string? layout, int laps)
        {
            if (string.IsNullOrEmpty(trackFolder) || AiLine.FileOf(trackFolder, layout) is not { } file) return null;

            var perLap = References.GetOrAdd(file, f =>
            {
                try
                {
                    var line = AiLine.Read(f);
                    return line.AverageKmh() is { } kmh ? line.Length() / 1000 * kmh * 10 : null;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    Logger.Warning("No reference speed from {File}: {Error}", f, ex.Message);
                    return null;
                }
            });
            return perLap * Math.Max(1, laps);
        }

        /// <summary>The best medal <paramref name="score"/> wins against <paramref name="medals"/></summary>
        public static Medal MedalFor(double score, ChallengeMedals? medals) =>
            medals == null || !double.IsFinite(score) ? Medal.None
            : score >= medals.Gold ? Medal.Gold
            : score >= medals.Silver ? Medal.Silver
            : score >= medals.Bronze ? Medal.Bronze
            : Medal.None;

        /// <summary>The player's progress with a Challenge, made when they have none yet</summary>
        public static ChallengeProgress ProgressOf(CareerState career, string challengeId)
        {
            career.Challenges ??= new(StringComparer.OrdinalIgnoreCase);
            if (!career.Challenges.TryGetValue(challengeId, out var progress) || progress == null)
                career.Challenges[challengeId] = progress = new ChallengeProgress();
            progress.Go ??= [];
            progress.BestRoads ??= [];
            progress.Medals ??= [];
            return progress;
        }

        /// <summary>Whether the player has driven <paramref name="challengeId"/> to the end</summary>
        public static bool IsFinished(CareerState career, string challengeId) =>
            career.Challenges != null && career.Challenges.TryGetValue(challengeId, out var progress) && progress is { Finished: > 0 };

        /// <summary>Gold medals won, each road of each Challenge once</summary>
        public static int GoldMedals(CareerState career) =>
            career.Challenges?.Values.Sum(p => p?.Medals?.Count(m => m == Medal.Gold) ?? 0) ?? 0;

        /// <summary>What still keeps a Challenge shut; empty when it is open</summary>
        public static List<ChallengeLock> WhatKeepsShut(ChallengeDefinition challenge, CareerState career)
        {
            var locks = new List<ChallengeLock>();
            if (challenge.Unlock is not { } unlock) return locks;

            if (unlock.Challenge is { } after && !IsFinished(career, after))
                locks.Add(new ChallengeLock(ChallengeLockKind.Challenge, after, 0));
            if (unlock.Score is > 0 and var score && CareerScore(career) < score)
                locks.Add(new ChallengeLock(ChallengeLockKind.Score, null, score - CareerScore(career)));
            if (unlock.Gold is > 0 and var gold && GoldMedals(career) < gold)
                locks.Add(new ChallengeLock(ChallengeLockKind.Gold, null, gold - GoldMedals(career)));
            return locks;
        }

        public static bool IsOpen(ChallengeDefinition challenge, CareerState career) => WhatKeepsShut(challenge, career).Count == 0;

        /// <summary>A go from the first road: the one under way, if any, is given up</summary>
        public static void Start(CareerState career, ChallengeDefinition challenge)
        {
            var progress = ProgressOf(career, challenge.Id);
            progress.Go.Clear();
            progress.Started = true;
            Logger.Information("Challenge {Id}: a go from the first road", challenge.Id);
        }

        /// <summary>The road the go under way is on (0 the first); null when no go is under way</summary>
        public static int? NextRoad(CareerState career, ChallengeDefinition challenge) =>
            career.Challenges != null && career.Challenges.TryGetValue(challenge.Id, out var progress)
            && progress is { Started: true } && (progress.Go?.Count ?? 0) < challenge.Roads.Count
                ? progress.Go?.Count ?? 0
                : null;

        /// <summary>
        /// A Challenge run kept: its road's score in the go under way (when the go is on that road; a run of a road the
        /// go has passed, or that no go is on, counts for nothing), the road's best and medal, the banked score and the
        /// counters (<see cref="MilestoneTrigger.TrafficScore"/>, <see cref="MilestoneTrigger.GoldMedals"/>,
        /// <see cref="MilestoneTrigger.CleanRuns"/>, <see cref="MilestoneTrigger.ChallengesDone"/>); the go's total when
        /// it was its last road. A run with no score (<paramref name="score"/> null) scores 0 and the go goes on.
        /// </summary>
        /// <param name="layout">The layout driven, which the road is banked under: the track's first for a road that
        /// names none, so the same road named with its layout in another Challenge counts once; none: the road's own</param>
        public static ChallengeRunOutcome Record(CareerState career, ChallengeDefinition challenge, int road, double? score, bool clean,
            ChallengeMedals? medals, UnlockRules rules, string? layout = null)
        {
            var progress = ProgressOf(career, challenge.Id);
            var tierBefore = OpenTier(CareerScore(career), rules);
            var scoreBefore = CareerScore(career);
            if (!progress.Started || progress.Go.Count != road || road < 0 || road >= challenge.Roads.Count)
            {
                Logger.Warning("Challenge {Id}: a run of road {Road}, but the go is {Where}; not counted", challenge.Id, road + 1,
                    progress.Started ? $"on road {progress.Go.Count + 1}" : "not under way");
                return new ChallengeRunOutcome { Counted = false, Road = road, TierBefore = tierBefore, TierAfter = tierBefore, ScoreBefore = scoreBefore, ScoreAfter = scoreBefore };
            }

            var points = score is { } s && double.IsFinite(s) && s > 0 ? s : 0;
            progress.Go.Add(points);
            while (progress.BestRoads.Count <= road) progress.BestRoads.Add(0);
            while (progress.Medals.Count <= road) progress.Medals.Add(Medal.None);
            var roadBest = points > progress.BestRoads[road];
            if (roadBest) progress.BestRoads[road] = points;

            var medal = MedalFor(points, medals);
            var medalBefore = progress.Medals[road];
            if (medal > medalBefore) progress.Medals[road] = medal;

            // The banked score: the best of this road, whichever Challenge it was driven in
            var spec = challenge.Roads[road];
            var key = RoadKey(challenge.Format ?? string.Empty, spec.Track, string.IsNullOrEmpty(layout) ? spec.Layout : layout);
            career.RoadBests ??= new(StringComparer.OrdinalIgnoreCase);
            if (!career.RoadBests.TryGetValue(key, out var banked) || points > banked) career.RoadBests[key] = points;
            if (clean && score != null) career.IncrementCounter(MilestoneTrigger.CleanRuns);

            double? total = null;
            var newBest = false;
            if (progress.Go.Count == challenge.Roads.Count)
            {
                total = progress.Go.Sum();
                newBest = progress.BestTotal is not { } best || total > best;
                if (newBest) progress.BestTotal = total;
                progress.Finished++;
                progress.Started = false;
                Logger.Information("Challenge {Id} driven to the end: {Total:N0}{Best}", challenge.Id, total, newBest ? ", its best" : string.Empty);
            }

            SyncCounters(career);
            var scoreAfter = CareerScore(career);
            return new ChallengeRunOutcome
            {
                Counted = true,
                Road = road,
                Score = points,
                Medal = medal,
                NewMedal = medal > medalBefore,
                RoadBest = roadBest,
                Total = total,
                NewBestTotal = newBest,
                FirstFinish = total != null && progress.Finished == 1,
                ScoreBefore = scoreBefore,
                ScoreAfter = scoreAfter,
                TierBefore = tierBefore,
                TierAfter = OpenTier(scoreAfter, rules)
            };
        }

        /// <summary>The counters that stand for where the player is (the banked score, the golds, the Challenges done)</summary>
        public static void SyncCounters(CareerState career)
        {
            career.SetCounter(MilestoneTrigger.TrafficScore, (int)Math.Min(int.MaxValue, CareerScore(career)));
            career.SetCounter(MilestoneTrigger.GoldMedals, GoldMedals(career));
            career.SetCounter(MilestoneTrigger.ChallengesDone, career.Challenges?.Values.Count(p => p is { Finished: > 0 }) ?? 0);
        }
    }

    /// <summary>What a Challenge run did</summary>
    public sealed class ChallengeRunOutcome
    {
        /// <summary>It was the road the go was on: it counted</summary>
        public bool Counted { get; init; }

        /// <summary>The road, 0 the first</summary>
        public int Road { get; init; }

        public double Score { get; init; }

        /// <summary>The medal the run won (none: below bronze)</summary>
        public Medal Medal { get; init; }

        /// <summary>A better medal on the road than it had</summary>
        public bool NewMedal { get; init; }

        /// <summary>The best run on the road in this Challenge so far</summary>
        public bool RoadBest { get; init; }

        /// <summary>The go's total, when this was its last road</summary>
        public double? Total { get; init; }

        public bool NewBestTotal { get; init; }

        /// <summary>The first time the Challenge was driven to the end</summary>
        public bool FirstFinish { get; init; }

        /// <summary>The banked score before the run and after it</summary>
        public double ScoreBefore { get; init; }

        public double ScoreAfter { get; init; }

        /// <summary>The highest tier open before the run and after it</summary>
        public int TierBefore { get; init; }

        public int TierAfter { get; init; }
    }

    /// <summary>What keeps a Challenge shut</summary>
    public enum ChallengeLockKind
    {
        /// <summary>Another Challenge to drive to the end first (<see cref="ChallengeLock.Challenge"/>)</summary>
        Challenge,

        /// <summary>Points to bank (<see cref="ChallengeLock.Short"/> to go)</summary>
        Score,

        /// <summary>Gold medals to win (<see cref="ChallengeLock.Short"/> to go)</summary>
        Gold
    }

    /// <param name="Challenge">The Challenge to finish first, for <see cref="ChallengeLockKind.Challenge"/></param>
    /// <param name="Short">How much is still wanted: points or golds</param>
    public readonly record struct ChallengeLock(ChallengeLockKind Kind, string? Challenge, double Short);
}
