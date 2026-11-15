using System.Text.Json.Nodes;
using StreetCorsa.Models.Career.Milestones;
using StreetCorsa.Models.GameState;
using StreetCorsa.Rules;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Race.Traffic;

namespace StreetCorsa.Tests;

/// <summary>
/// The career of runs for the score in Challenges: a go driven road after road, its medals, the banked score (the best
/// on each road, once), the tiers it opens, what keeps a Challenge shut, and a car's tier from its power to weight
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class ChallengeCareerTests
{
    private static UnlockRules Rules(params long[] tiers) => new() { Tiers = [.. tiers] };

    private static ChallengeDefinition Challenge(string id, int roads, ChallengeUnlock? unlock = null) => new()
    {
        Id = id,
        Name = id,
        Format = "open_drive",
        Roads = Enumerable.Range(1, roads).Select(i => new ChallengeRoad { Track = $"road_{i}" }).ToList(),
        Unlock = unlock
    };

    private static readonly ChallengeMedals Medals = new() { Bronze = 1000, Silver = 2000, Gold = 3000 };

    [Fact]
    public void A_tier_opens_at_its_score_and_with_no_tiers_every_car_is_open()
    {
        var rules = Rules(0, 25000, 80000);
        Assert.Equal(1, ChallengeCareer.OpenTier(0, rules));
        Assert.Equal(1, ChallengeCareer.OpenTier(24999, rules));
        Assert.Equal(2, ChallengeCareer.OpenTier(25000, rules));
        // Past the list, every tier after it opens with its last
        Assert.Equal(StreetCorsa.Parts.Cars.CarEntry.LastTier, ChallengeCareer.OpenTier(80000, rules));
        Assert.Equal(StreetCorsa.Parts.Cars.CarEntry.LastTier, ChallengeCareer.OpenTier(0, Rules()));
        Assert.Equal(25000, ChallengeCareer.TierScore(2, rules));
        Assert.Null(ChallengeCareer.TierScore(2, Rules()));
    }

    [Fact]
    public void A_cars_tier_is_its_settings_else_its_power_to_weights_band()
    {
        var rules = new UnlockRules();
        Assert.Equal(4, ChallengeCareer.TierOf(4, 100, 1000, rules));
        Assert.Equal(1, ChallengeCareer.TierOf(null, 160, 1050, rules));   // 152 hp/t: an MX-5
        Assert.Equal(2, ChallengeCareer.TierOf(null, 300, 1300, rules));   // 231
        Assert.Equal(3, ChallengeCareer.TierOf(null, 450, 1450, rules));   // 310
        Assert.Equal(4, ChallengeCareer.TierOf(null, 600, 1500, rules));   // 400
        Assert.Equal(5, ChallengeCareer.TierOf(null, 950, 1500, rules));   // 633
        // No figures, or a weight no car has: the last band
        Assert.Equal(5, ChallengeCareer.TierOf(null, null, 1200, rules));
        Assert.Equal(5, ChallengeCareer.TierOf(null, 200, 1.2, rules));
    }

    [Fact]
    public void A_medal_is_the_best_target_the_score_reaches()
    {
        Assert.Equal(Medal.None, ChallengeCareer.MedalFor(999, Medals));
        Assert.Equal(Medal.Bronze, ChallengeCareer.MedalFor(1000, Medals));
        Assert.Equal(Medal.Silver, ChallengeCareer.MedalFor(2500, Medals));
        Assert.Equal(Medal.Gold, ChallengeCareer.MedalFor(3000, Medals));
        Assert.Equal(Medal.None, ChallengeCareer.MedalFor(5000, null));
    }

    [Fact]
    public void A_go_is_driven_road_after_road_and_counts_at_its_last()
    {
        var career = CareerState.CreateNew();
        var challenge = Challenge("od:europe", 3);
        var rules = Rules(0, 5000);
        Assert.Null(ChallengeCareer.NextRoad(career, challenge));

        ChallengeCareer.Start(career, challenge);
        Assert.Equal(0, ChallengeCareer.NextRoad(career, challenge));

        var first = ChallengeCareer.Record(career, challenge, 0, 2500, clean: true, Medals, rules);
        Assert.True(first.Counted);
        Assert.Equal(Medal.Silver, first.Medal);
        Assert.Null(first.Total);
        Assert.Equal(1, ChallengeCareer.NextRoad(career, challenge));

        // A run of a road the go is not on counts for nothing
        Assert.False(ChallengeCareer.Record(career, challenge, 2, 9000, clean: false, Medals, rules).Counted);

        // No score: the road scores 0, and the go goes on
        var second = ChallengeCareer.Record(career, challenge, 1, null, clean: false, Medals, rules);
        Assert.Equal(0, second.Score);
        Assert.Equal(Medal.None, second.Medal);

        var last = ChallengeCareer.Record(career, challenge, 2, 3100, clean: false, Medals, rules);
        Assert.Equal(5600, last.Total);
        Assert.True(last.FirstFinish);
        Assert.Equal(1, last.TierBefore);
        Assert.Equal(StreetCorsa.Parts.Cars.CarEntry.LastTier, last.TierAfter);
        Assert.Null(ChallengeCareer.NextRoad(career, challenge));

        var progress = career.Challenges["od:europe"];
        Assert.Equal(5600, progress.BestTotal);
        Assert.Equal(1, progress.Finished);
        Assert.Equal([Medal.Silver, Medal.None, Medal.Gold], progress.Medals);
        Assert.Equal(5600, career.GetCounter(MilestoneTrigger.TrafficScore));
        Assert.Equal(1, career.GetCounter(MilestoneTrigger.GoldMedals));
        Assert.Equal(1, career.GetCounter(MilestoneTrigger.CleanRuns));
        Assert.Equal(1, career.GetCounter(MilestoneTrigger.ChallengesDone));
    }

    [Fact]
    public void The_banked_score_is_the_best_on_each_road_once_whichever_challenge_it_was_driven_in()
    {
        var career = CareerState.CreateNew();
        var rules = Rules(0, 100000);
        var one = Challenge("od:one", 1);
        var two = Challenge("od:two", 1);   // the same road_1

        ChallengeCareer.Start(career, one);
        ChallengeCareer.Record(career, one, 0, 4000, false, Medals, rules);
        ChallengeCareer.Start(career, two);
        ChallengeCareer.Record(career, two, 0, 3000, false, Medals, rules);
        Assert.Equal(4000, ChallengeCareer.CareerScore(career));

        // A better run adds what it beat the best by, a worse one nothing
        ChallengeCareer.Start(career, one);
        var better = ChallengeCareer.Record(career, one, 0, 4500, false, Medals, rules);
        Assert.Equal(4000, better.ScoreBefore);
        Assert.Equal(4500, better.ScoreAfter);
        Assert.False(better.FirstFinish);
        Assert.True(better.NewBestTotal);
        ChallengeCareer.Start(career, one);
        Assert.Equal(4500, ChallengeCareer.Record(career, one, 0, 100, false, Medals, rules).ScoreAfter);
        Assert.Equal(4500, career.Challenges["od:one"].BestTotal);
        Assert.Equal(2, career.GetCounter(MilestoneTrigger.ChallengesDone));
    }

    [Fact]
    public void A_challenge_stays_shut_until_all_its_unlock_asks_for_is_done()
    {
        var career = CareerState.CreateNew();
        var rules = Rules();
        var first = Challenge("od:first", 1);
        var next = Challenge("od:next", 1, new ChallengeUnlock { Challenge = "od:first", Score = 3000, Gold = 1 });

        var locks = ChallengeCareer.WhatKeepsShut(next, career);
        Assert.Equal([ChallengeLockKind.Challenge, ChallengeLockKind.Score, ChallengeLockKind.Gold], locks.Select(l => l.Kind));
        Assert.True(ChallengeCareer.IsOpen(first, career));

        // Finished, but short of the score and the gold
        ChallengeCareer.Start(career, first);
        ChallengeCareer.Record(career, first, 0, 2500, false, Medals, rules);
        Assert.Equal([ChallengeLockKind.Score, ChallengeLockKind.Gold], ChallengeCareer.WhatKeepsShut(next, career).Select(l => l.Kind));
        Assert.Equal(500, ChallengeCareer.WhatKeepsShut(next, career)[0].Short);

        ChallengeCareer.Start(career, first);
        ChallengeCareer.Record(career, first, 0, 3200, false, Medals, rules);
        Assert.True(ChallengeCareer.IsOpen(next, career));
    }

    [Fact]
    public void A_road_without_pinned_medals_gets_them_from_its_ai_lines_speed()
    {
        using var temp = new TempDir();
        // A straight kilometre at 20 m/s (72 km/h): one lap scores 1 × 72 × 10 = 720
        var points = Enumerable.Range(0, 101).Select(i => new AiLine.Point(0, 0, i * 10, 4, 4, 0, 1, 0, 20)).ToList();
        var line = AiLine.Of(points);
        Assert.Equal(1000, line.Length(), 6);
        Assert.Equal(72, line.AverageKmh()!.Value, 6);

        temp.File("road/ai/fast_lane.ai", "");
        File.WriteAllBytes(temp.Combine("road/ai/fast_lane.ai"), AiLineBytes(points));
        Assert.Equal(1440, ChallengeCareer.ReferenceScore(temp.Combine("road"), null, 2)!.Value, 3);

        var rules = new UnlockRules { Bronze = 0.5, Silver = 0.75, Gold = 1 };
        var medals = ChallengeCareer.MedalsOf(new ChallengeRoad { Track = "road" }, 2, temp.Combine("road"), rules)!;
        Assert.Equal((720, 1080, 1440), (medals.Bronze, medals.Silver, medals.Gold));

        // Pinned ones stand; a road without an AI line has none
        var pinned = new ChallengeMedals { Bronze = 1, Silver = 2, Gold = 3 };
        Assert.Same(pinned, ChallengeCareer.MedalsOf(new ChallengeRoad { Track = "road", Medals = pinned }, 2, temp.Combine("road"), rules));
        Assert.Null(ChallengeCareer.MedalsOf(new ChallengeRoad { Track = "none" }, 1, temp.Combine("none"), rules));
    }

    [Fact]
    public void The_unlocks_rules_hold_their_medals_rising_and_their_tiers_not_falling()
    {
        var merged = JsonNode.Parse("""
            { "unlocks": { "tiers": [0, 30000, 20000], "bronze": 0.6, "silver": 0.5, "gold": 0.7, "tierHpPerTonne": [200, 100] } }
            """)!.AsObject();
        var problems = new List<string>();
        var unlocks = RuleBook.Read(merged, problems).Unlocks;
        Assert.Equal([0L, 30000, 30000], unlocks.Tiers);
        Assert.Equal((0.3, 0.42, 0.55), (unlocks.Bronze, unlocks.Silver, unlocks.Gold));
        Assert.Equal(new UnlockRules().TierHpPerTonne, unlocks.TierHpPerTonne);
        Assert.Equal(3, problems.Count(p => p.StartsWith("unlocks")));
    }

    /// <summary>An AI line file (version 7) of these points, each with its 18 extras: speed first, the sides, the normal</summary>
    private static byte[] AiLineBytes(IReadOnlyList<AiLine.Point> points)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(7);
        writer.Write(points.Count);
        writer.Write(0);
        writer.Write(0);
        for (var i = 0; i < points.Count; i++)
        {
            writer.Write((float)points[i].X);
            writer.Write((float)points[i].Y);
            writer.Write((float)points[i].Z);
            writer.Write(0f);
            writer.Write(i);
        }

        writer.Write(points.Count);
        foreach (var point in points)
        {
            var extras = new float[18];
            extras[0] = (float)point.Speed;
            extras[5] = (float)point.SideLeft;
            extras[6] = (float)point.SideRight;
            extras[9] = (float)point.NormalX;
            extras[10] = (float)point.NormalY;
            extras[11] = (float)point.NormalZ;
            foreach (var extra in extras) writer.Write(extra);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
