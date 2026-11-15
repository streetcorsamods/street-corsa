using StreetCorsa.Mods;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Race.Traffic;

namespace StreetCorsa.Tests;

/// <summary>
/// The Challenges a mod writes (challenges/*.json): roads driven in order in a format run alone for the score, read and
/// tidied, what does not read told; and a car's tier in cars.json, read and held to its range.
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class ChallengeTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private (ModSet Mods, RaceFormats Formats) Mods(params (string Name, string Json)[] challenges)
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/formats/formats.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "formats", "formats.json")));
        _temp.File("roads/mod.json", """{ "id": "roads" }""");
        _temp.File("roads/formats/formats.json", """
            { "formats": [
              { "id": "traffic_run", "family": "road", "mode": "traffic_run", "win": "score", "better": "higher", "rivals": 20, "solo": true, "traffic": true }
            ] }
            """);
        _temp.File("roads/formats/traffic_run/mode.lua", "-- the mode");
        foreach (var (name, json) in challenges) _temp.File($"roads/challenges/{name}.json", json);
        var mods = ModSet.Load(_temp.Path);
        return (mods, RaceFormats.Load(mods, _temp.Combine("no_mode")));
    }

    [Fact]
    public void A_challenge_reads_its_roads_in_order_in_the_format_run_alone()
    {
        var (mods, formats) = Mods(("europe", """
            {
              "id": "europe", "name": "Europe", "region": "Europe", "blurb": "Wet roads.",
              "roads": [
                { "track": "new_forest_3_0", "laps": 2, "time": "17:30", "weather": "3_clear" },
                { "track": "rt_bannochbrae", "layout": "normal", "trafficPerLaneKm": 9, "trafficCars": 80,
                  "medals": { "bronze": 1000, "silver": 2000, "gold": 3000 } }
              ]
            }
            """));

        var book = ChallengeBook.Load(mods, formats);

        var europe = Assert.Single(book.All);
        Assert.Empty(book.Problems);
        Assert.Equal("roads:europe", europe.Id);
        Assert.Equal("roads", europe.ModId);
        Assert.Equal(formats.Solo!.Id, europe.Format);
        Assert.Null(europe.Unlock);
        Assert.Equal(["new_forest_3_0", "rt_bannochbrae"], europe.Roads.Select(r => r.Track));
        Assert.Equal(17 * 60 + 30, europe.Roads[0].Minute);
        Assert.Equal(2, europe.Roads[0].Laps);
        Assert.Equal("normal", europe.Roads[1].Layout);
        Assert.Equal(3000, europe.Roads[1].Medals!.Gold);
        Assert.Same(europe, book.Get("roads:europe"));
    }

    [Fact]
    public void What_does_not_read_is_told_and_a_challenge_with_no_road_is_left_out()
    {
        var (mods, formats) = Mods(
            ("bad", """
                {
                  "id": "bad", "name": "",
                  "roads": [
                    { "laps": 3 },
                    { "track": "a", "laps": 99, "time": "25:61", "trafficPerLaneKm": 100, "trafficCars": 5,
                      "medals": { "bronze": 3000, "silver": 2000, "gold": 1000 } }
                  ],
                  "unlock": { "challenge": "nowhere", "score": -5 }
                }
                """),
            ("empty", """{ "id": "empty", "name": "Empty", "roads": [ { "laps": 1 } ] }"""),
            ("drag", """{ "id": "drag", "name": "Drag", "format": "drag", "roads": [ { "track": "a" } ] }"""),
            ("lost", """{ "id": "lost", "name": "Lost", "format": "no_such_format", "roads": [ { "track": "a" } ] }"""));

        var book = ChallengeBook.Load(mods, formats);

        var bad = Assert.Single(book.All);
        Assert.Equal("roads:bad", bad.Id);
        Assert.Equal("bad", bad.Name);
        var road = Assert.Single(bad.Roads);
        Assert.Equal(RaceFormats.MaxLaps, road.Laps);
        Assert.Null(road.Minute);
        Assert.Null(road.TrafficPerLaneKm);
        Assert.Equal(TrafficSetup.LeastCars, road.TrafficCars);
        Assert.Null(road.Medals);
        Assert.Equal(0, bad.Unlock!.Score);
        Assert.Equal("nowhere", bad.Unlock.Challenge);

        var problems = string.Join("\n", book.Problems);
        Assert.Contains("road 1 names no track", problems);
        Assert.Contains("no name", problems);
        Assert.Contains("time \"25:61\"", problems);
        Assert.Contains("trafficPerLaneKm 100", problems);
        Assert.Contains("trafficCars 5", problems);
        Assert.Contains("medals", problems);
        Assert.Contains("challenge nowhere, which no mod has", problems);
        Assert.Contains("challenge empty has no road", problems);
        Assert.Contains("not run alone", problems);
        Assert.Contains("no_such_format", problems);
    }

    [Fact]
    public void A_challenge_opens_after_another_and_an_unlock_of_nothing_is_open_from_the_start()
    {
        var (mods, formats) = Mods(
            ("first", """{ "id": "first", "name": "First", "roads": [ { "track": "a" } ], "unlock": { "score": 0 } }"""),
            ("second", """{ "id": "second", "name": "Second", "roads": [ { "track": "b" } ], "unlock": { "challenge": "first", "gold": 3 } }"""));

        var book = ChallengeBook.Load(mods, formats);

        Assert.Empty(book.Problems);
        Assert.Null(book.Get("roads:first")!.Unlock);
        var second = book.Get("roads:second")!.Unlock!;
        Assert.Equal("roads:first", second.Challenge);
        Assert.Equal(3, second.Gold);
    }

    [Theory]
    [InlineData("7:05", 7 * 60 + 5)]
    [InlineData("00:00", 0)]
    [InlineData("23:59", 23 * 60 + 59)]
    [InlineData("noon", null)]
    [InlineData("24:00", null)]
    public void A_time_of_day_is_read_in_minutes_from_midnight(string text, int? minute) =>
        Assert.Equal(minute, ChallengeRoad.ReadTime(text));

    [Fact]
    public void A_cars_tier_is_read_and_one_out_of_range_is_told_and_dropped()
    {
        var problems = new List<string>();
        var merged = System.Text.Json.Nodes.JsonNode.Parse("""
            { "cars": { "ks_mazda_mx5_nd": { "in": true, "tier": 1 }, "ks_pagani_huayra_bc": { "tier": 11 } } }
            """)!.AsObject();

        var book = CarBook.Read(merged, problems);

        Assert.Equal(1, book.Car("ks_mazda_mx5_nd")?.Tier);
        Assert.Null(book.Car("ks_pagani_huayra_bc")?.Tier);
        Assert.Contains(problems, p => p.Contains("cars.ks_pagani_huayra_bc.tier 11"));
    }
}
