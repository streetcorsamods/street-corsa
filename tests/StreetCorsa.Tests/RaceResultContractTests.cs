using Newtonsoft.Json;
using StreetCorsa.Models.Race;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Race.Validation;

namespace StreetCorsa.Tests;

/// <summary>
/// The race mode's result file, as the mode writes it, read the way the game reads it. The fixtures come out of the
/// mode itself (tools/sc_race_harness/test_chase.py, test_strip.py and test_time_attack.py --golden, which also fail when
/// the mode's output drifts from them): a road race with the police, won at the line after getting away; a bracket race
/// won on the dial; a test-and-tune of two passes; a time attack won on the best lap.
/// </summary>
public sealed class RaceResultContractTests : IDisposable
{
    private static string Golden => Fixture("sc_race_result.json");

    private static string Fixture(string name) => Path.Combine(RepoPaths.Root, "tests", "StreetCorsa.Tests", "Fixtures", name);

    private async Task<RaceResultJson> ValidAsync(string fixture)
    {
        var path = _temp.File(Guid.NewGuid() + ".json", File.ReadAllText(Fixture(fixture)));
        var validation = await new RaceResultValidator().ValidateFileAsync(path);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        return validation.ParsedResult!;
    }

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task What_the_race_mode_writes_passes_validation_and_reads_into_every_field()
    {
        // Under a session-id name in a folder of its own, as it lands in the inbox
        var text = File.ReadAllText(Golden);
        var path = _temp.File(Guid.NewGuid() + ".json", text);

        var validation = await new RaceResultValidator().ValidateFileAsync(path);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        var result = validation.ParsedResult!;
        Assert.Equal("1.8", result.Metadata.SchemaVersion);
        Assert.Equal(RaceMetadata.ModeSource, result.Metadata.Source);
        Assert.True(Guid.TryParse(result.Session.SessionId, out _));
        Assert.Equal(Guid.Parse("6f1c2d3e-4b5a-4c6d-8e7f-90a1b2c3d4e5"), Guid.Parse(result.Session.ContextId!));
        Assert.True(RaceResultValidator.TryParseTimestamp(result.Session.StartTimestamp, out _));
        Assert.Equal(EndReasons.Finished, result.Session.EndReason);
        Assert.Equal("ROAD", result.Session.RaceType);
        Assert.Equal("circuit", result.Session.Format);

        var player = Assert.Single(result.Participants, p => p.IsPlayer == true);
        Assert.Equal(0, player.CarIndex);
        Assert.Equal(1, player.Performance.FinalPosition);
        Assert.Equal(1, player.Performance.LapsCompleted);
        Assert.True(player.Performance.DistanceKm > 0);
        Assert.False(player.Crash.Crashed);
        Assert.Empty(player.Crash.CrashIntensitiesG);
        Assert.Equal(4, player.Condition!.BodyDamageKmh!.Count);
        Assert.Equal(1000.0, player.Condition.EngineLife);
        Assert.Equal(new int?[] { 0, 1, 2, 3 }, player.Condition.Wheels!.Select(w => w.Wheel));
        Assert.All(player.Condition.Wheels!, w => Assert.NotNull(w.TyreWear));
        // Step 14: the tank, the dirt, the engine's heat and oil
        Assert.True(player.Condition.MaxFuelLitres > 0);
        Assert.NotNull(player.Condition.Dirt);
        Assert.NotNull(player.Condition.Heat!.PeakWaterC);
        Assert.Equal(1.0, player.Condition.Heat.LowestOilSupply);
        // A road race has no timeslip
        Assert.Null(player.Timeslip);

        Assert.NotNull(result.Pursuit);
        Assert.True(result.Pursuit!.PlayerEscaped);
        Assert.Equal(2, result.Pursuit.Police);
    }

    [Theory]
    [InlineData("sc_race_result.json")]
    [InlineData("sc_race_bracket_result.json")]
    [InlineData("sc_race_tune_result.json")]
    [InlineData("sc_race_time_attack_result.json")]
    public void Nothing_the_race_mode_writes_is_left_unread(string fixture)
    {
        // Every key in the file has a property to go to: a key the game drops is a contract that drifted
        var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error };
        var result = JsonConvert.DeserializeObject<RaceResultJson>(File.ReadAllText(Fixture(fixture)), settings);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task A_bracket_race_as_the_mode_writes_it_is_decided_the_same_way_by_the_career()
    {
        var result = await ValidAsync("sc_race_bracket_result.json");

        Assert.Equal(RaceTypes.Drag, result.Session.RaceType);
        var player = Assert.Single(result.Participants, p => p.IsPlayer == true);
        var rival = Assert.Single(result.Participants, p => p.IsPlayer == false);
        Assert.NotNull(player.DialInSeconds);
        Assert.False(player.Breakout);
        Assert.NotNull(player.Timeslip!.GreenSeconds);
        Assert.NotNull(rival.Timeslip!.GreenSeconds);

        // The mode's call (its final positions) and the career's (the slips and the dial-ins) agree
        var decided = BracketRules.Decide(player.Timeslip, player.DialInSeconds!.Value, rival.Timeslip, rival.DialInSeconds!.Value)!.Value;
        Assert.Equal(player.Performance.FinalPosition == 1, decided.PlayerWon);
        Assert.Equal(rival.Breakout, decided.OpponentBrokeOut);
    }

    [Fact]
    public async Task A_test_and_tune_as_the_mode_writes_it_is_one_car_and_its_passes()
    {
        var result = await ValidAsync("sc_race_tune_result.json");

        Assert.Equal(RaceTypes.TestAndTune, result.Session.RaceType);
        var player = Assert.Single(result.Participants);
        Assert.True(player.IsPlayer);
        Assert.Equal([1, 2], player.Passes!.Select(p => p.Pass!.Value));
        Assert.All(player.Passes!, p => Assert.NotNull(p.QuarterMileSeconds));
        Assert.Equal(player.Passes!.Min(p => p.QuarterMileSeconds), player.Timeslip!.QuarterMileSeconds);
        Assert.Null(player.DialInSeconds);
    }

    [Fact]
    public async Task A_time_attack_as_the_mode_writes_it_has_each_racers_best_lap_as_the_score()
    {
        var result = await ValidAsync("sc_race_time_attack_result.json");

        Assert.Equal("time_attack", result.Session.Format);
        var player = Assert.Single(result.Participants, p => p.IsPlayer == true);
        var rival = Assert.Single(result.Participants, p => p.IsPlayer == false);
        Assert.Equal(player.LapTimesMs!.Min(), player.Score);
        Assert.Equal(2, player.LapTimesMs!.Count);
        Assert.True(player.Score < rival.Score);
        // The mode's call (its final positions) and the career's (the scores) agree
        Assert.Equal(1, player.Performance.FinalPosition);
        Assert.Null(player.Timeslip);
    }
}
