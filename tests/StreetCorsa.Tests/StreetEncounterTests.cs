using StreetCorsa.Controls.Street;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>The Cruise screen: the street's light, the scene file, the rival's drive, and who pulls up with what</summary>
public class StreetEncounterTests
{
    private static Opponent Rival(string name, int reputation = 50, int aggression = 50, decimal money = 5000m) =>
        new(name, 30, Gender.Male, 95, aggression) { Money = money, Stats = { Reputation = reputation } };

    #region Light

    [Fact]
    public void Night_falls_earlier_in_winter_than_in_summer()
    {
        var winter = new DateTime(1969, 12, 20, 17, 30, 0);
        var summer = new DateTime(1969, 6, 20, 17, 30, 0);

        Assert.Equal(StreetLight.Night, StreetLights.At(winter));
        Assert.Equal(StreetLight.Day, StreetLights.At(summer));
        Assert.InRange(StreetLights.SunsetHour(summer) - StreetLights.SunsetHour(winter), 3.0, 4.2);
    }

    [Fact]
    public void Dusk_comes_before_the_sun_goes_down()
    {
        var june = new DateTime(1969, 6, 21);
        var sunset = StreetLights.SunsetHour(june);

        Assert.Equal(StreetLight.Day, StreetLights.At(june.AddHours(12)));
        Assert.Equal(StreetLight.Dusk, StreetLights.At(june.AddHours(sunset - 0.5)));
        Assert.Equal(StreetLight.Night, StreetLights.At(june.AddHours(sunset + 1)));
        Assert.Equal(StreetLight.Dusk, StreetLights.At(june.AddHours(6.5)));
    }

    #endregion

    #region The scene file

    [Fact]
    public void The_shipped_street_loads_with_a_model_for_every_light()
    {
        var scene = Assert.Single(StreetScenes.LoadAll(ModSet.Load(RepoPaths.Content)));

        Assert.Equal("mainstreet", scene.Id);
        Assert.Equal("street.kn5", scene.Model);
        foreach (var light in Enum.GetValues<StreetLight>())
        {
            Assert.True(File.Exists(scene.Kn5For(light)), light.ToString());
        }

        Assert.True(scene.LaneOffset > 2f);
        Assert.True(scene.ApproachFrom > 20f && scene.LeaveTo > 20f);
        Assert.Equal(0f, scene.LightingFor(StreetLight.Night).CubemapAmbient);
        Assert.NotEmpty(scene.LightingFor(StreetLight.Night).Lamps);
    }

    [Fact]
    public void A_street_is_used_at_the_lights_it_has_a_model_for_and_left_out_when_one_it_names_is_missing()
    {
        using var dir = new TempDir();
        const string Street = """{ "id": "half", "player": { "x": 0, "z": 0, "heading": 0 }, "laneOffset": 3, "rivalStop": 0, "approachFrom": 40, "leaveTo": 60 """;
        dir.File("half/street.json", Street + "}");
        dir.File("half/street_night.kn5", "");
        dir.File("named/street.json", Street + """, "times": [ "day", "night" ] }""");
        dir.File("named/street_night.kn5", "");
        dir.File("none/street.json", Street + "}");

        Assert.Equal([StreetLight.Night], StreetScenes.Load(dir.Combine("half"))!.Times);
        Assert.Null(StreetScenes.Load(dir.Combine("named")));
        Assert.Null(StreetScenes.Load(dir.Combine("none")));
    }

    [Fact]
    public void A_street_not_used_at_a_light_stays_in_the_nearest_it_has()
    {
        var street = new StreetScene("s", "S", "", new StreetSpot(0, 0, 0), 3, 0, 40, 60, [0, 1, 0], new Dictionary<StreetLight, StreetLighting>())
        {
            Times = new HashSet<StreetLight> { StreetLight.Day, StreetLight.Dusk }
        };

        Assert.Equal(StreetLight.Dusk, street.NearestTime(StreetLight.Night));
        Assert.Equal(StreetLight.Day, street.NearestTime(StreetLight.Day));
        Assert.Equal(StreetLight.Night, (street with { Times = new HashSet<StreetLight> { StreetLight.Day, StreetLight.Night } }).NearestTime(StreetLight.Dusk));
    }

    [Fact]
    public void A_broken_street_file_is_no_street()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "street.json"), "{ not json");

        Assert.Null(StreetScenes.Load(dir.Path));
    }

    [Fact]
    public void Colours_are_read_as_numbers_and_a_missing_light_falls_back_to_the_day()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "street.json"),
            """
            { "id": "t", "player": { "x": 1, "z": 2, "heading": 90 }, "laneOffset": 3, "rivalStop": 0.5, "approachFrom": 40, "leaveTo": 60,
              "lights": { "day": { "brightness": 1.2, "color": [ 255, 128, 0 ], "ambient": 2, "ambientUp": [ 1, 2, 3 ], "ambientDown": [ 4, 5, 6 ] } } }
            """);
        foreach (var light in new[] { "day", "dusk", "night" }) File.WriteAllText(Path.Combine(dir.Path, $"street_{light}.kn5"), "");

        var scene = StreetScenes.Load(dir.Path);

        Assert.NotNull(scene);
        Assert.Equal(new StreetSpot(1f, 2f, 90f), scene.Player);
        Assert.Equal(new byte[] { 255, 128, 0 }, scene.LightingFor(StreetLight.Day).Color);
        Assert.Equal(0.5f, scene.LightingFor(StreetLight.Day).CubemapAmbient);
        Assert.Same(scene.LightingFor(StreetLight.Day), scene.LightingFor(StreetLight.Night));
    }

    #endregion

    #region The rival's drive

    [Fact]
    public void Arriving_the_rival_brakes_to_a_stop_where_the_street_says()
    {
        var drive = RivalArrives(-45f, 0.35f, 800);
        var last = drive.At(0f);
        var braked = false;

        for (var t = 0.05f; t <= drive.Duration + 0.5f; t += 0.05f)
        {
            var state = drive.At(t);
            Assert.True(state.Z >= last.Z - 1e-4f, "never rolls back");
            Assert.True(state.Speed <= last.Speed + 1e-4f, "never speeds up");
            braked |= state.Braking && state.Acceleration < 0;
            last = state;
        }

        Assert.True(braked);
        Assert.True(last.Done);
        Assert.Equal(0.35f, last.Z, 3);
        Assert.Equal(0f, last.Speed);
        Assert.Null(last.Rpm);
    }

    [Fact]
    public void The_clutch_goes_in_before_the_car_stops_and_the_engine_never_drops_under_idle()
    {
        var drive = RivalArrives(-45f, 0f, 850);

        Assert.True(drive.At(0.5f).Rpm >= 850);
        var nearlyStopped = drive.At(drive.Duration - 0.3f);
        Assert.False(nearlyStopped.Done);
        Assert.Null(nearlyStopped.Rpm);
    }

    [Fact]
    public void Leaving_the_rival_launches_up_through_the_gears_and_out_of_sight()
    {
        var drive = DrivePlan.Launch(DrivePath.Through([new GroundPoint(0, 0f), new GroundPoint(0, 70f)]), BaseFeel("launch"), 800, 6000);
        var early = drive.At(0.5f);
        var later = drive.At(3f);

        Assert.True(early.Acceleration > 3f);
        Assert.True(later.Speed > early.Speed);
        Assert.InRange(drive.Duration, 3f, 12f);
        Assert.True(drive.At(drive.Duration).Done);
        Assert.True(drive.At(drive.Duration).Z >= 70f - 1e-3f);

        for (var t = 0f; t < drive.Duration; t += 0.1f) Assert.True(drive.At(t).Rpm <= 6000 * 1.05, $"over the limiter at {t}");
    }

    [Fact]
    public void The_nose_dives_on_the_brakes_and_settles_when_stopped()
    {
        var pitch = new ChassisPitch();
        for (var i = 0; i < 60; i++) pitch.Tick(1 / 60.0, -4.2);
        Assert.True(pitch.Pitch < -0.01f);

        for (var i = 0; i < 600; i++) pitch.Tick(1 / 60.0, 0);
        Assert.True(pitch.IsSettled);

        for (var i = 0; i < 60; i++) pitch.Tick(1 / 60.0, 5);
        Assert.True(pitch.Pitch > 0.01f);
    }

    [Fact]
    public void A_wheel_turns_once_for_its_own_circumference()
    {
        Assert.Equal(2 * MathF.PI, DriveFeel.Road.WheelAngle(2 * MathF.PI * 0.33f), 3);
    }

    /// <summary>A feel of the base game's scripts</summary>
    private static DriveFeel BaseFeel(string id) => ScriptBook.Load(ModSet.Load(RepoPaths.Content), []).Feels[id];

    /// <summary>The rival up a straight lane along +z from <paramref name="from"/> to a stop at <paramref name="stopAt"/>, as its script drives it</summary>
    private static DrivePlan RivalArrives(float from, float stopAt, double idle) =>
        DrivePlan.Road(DrivePath.Through([new GroundPoint(0, from), new GroundPoint(0, stopAt)]), BaseFeel("lane"), idle, rolling: true, stop: true);

    #endregion

    #region Who pulls up

    [Fact]
    public void The_street_is_busier_at_night()
    {
        var random = new Random(7);
        var noon = new DateTime(1969, 6, 21, 12, 0, 0);
        var night = new DateTime(1969, 6, 21, 21, 0, 0);

        var byDay = Enumerable.Range(0, 2000).Average(_ => StreetEncounters.MinutesToNext(noon, random));
        var atNight = Enumerable.Range(0, 2000).Average(_ => StreetEncounters.MinutesToNext(night, random));

        Assert.True(atNight < byDay / 2, $"{atNight} at night against {byDay} by day");
        Assert.All(Enumerable.Range(0, 500).Select(_ => StreetEncounters.MinutesToNext(noon, random)),
            m => Assert.InRange(m, StreetEncounters.ShortestWait, StreetEncounters.LongestWait));
    }

    [Fact]
    public void A_rival_met_tonight_does_not_come_round_again()
    {
        var a = Rival("A");
        var b = Rival("B");
        var time = new DateTime(1969, 6, 21, 21, 0, 0);

        for (var i = 0; i < 50; i++)
        {
            Assert.Same(b, StreetEncounters.PickRival([a, b], 50, time, new HashSet<Guid> { a.OpponentId }, new Random(i)));
        }

        Assert.Null(StreetEncounters.PickRival([a, b], 50, time, new HashSet<Guid> { a.OpponentId, b.OpponentId }, new Random(1)));
        Assert.Null(StreetEncounters.PickRival([], 50, time, new HashSet<Guid>(), new Random(1)));
    }

    [Fact]
    public void A_rival_with_a_grudge_comes_looking_and_the_King_seldom_shows()
    {
        var plain = Rival("Plain", reputation: 90);
        var sore = Rival("Sore", reputation: 90);
        sore.Grudge = new Grudge();
        var king = Rival("King", reputation: 100);
        king.IsBoss = true;
        king.BossPath = "King";
        var time = new DateTime(1969, 6, 21, 14, 0, 0);

        var random = new Random(3);
        var picks = Enumerable.Range(0, 3000)
            .Select(_ => StreetEncounters.PickRival([plain, sore, king], 20, time, new HashSet<Guid>(), random)!)
            .GroupBy(o => o.Name)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.True(picks["Sore"] > picks["Plain"] * 3);
        Assert.True(picks["King"] < picks["Plain"] / 2);
    }

    [Fact]
    public void The_bold_ones_are_out_at_night()
    {
        var night = new DateTime(1969, 6, 21, 21, 0, 0);
        var noon = new DateTime(1969, 6, 21, 12, 0, 0);
        var bold = Rival("Bold", aggression: 95);
        var shy = Rival("Shy", aggression: 5);

        Assert.True(StreetEncounters.Weight(bold, 50, night) > StreetEncounters.Weight(shy, 50, night) * 2.5);
        Assert.True(StreetEncounters.Weight(bold, 50, noon) < StreetEncounters.Weight(shy, 50, noon) * 1.5);
    }

    [Fact]
    public void The_race_asked_for_fits_the_tracks_installed()
    {
        var time = new DateTime(1969, 6, 21, 12, 0, 0);

        Assert.Null(StreetEncounters.PickFormat(time, false, false, new Random(1)));
        Assert.Equal("drag", StreetEncounters.PickFormat(time, true, false, new Random(1)));
        Assert.Equal("circuit", StreetEncounters.PickFormat(time, false, true, new Random(1)));

        var random = new Random(5);
        var drags = Enumerable.Range(0, 2000).Count(_ => StreetEncounters.PickFormat(time, true, true, random) == "drag");
        Assert.InRange(drags, 1150, 1450);
    }

    [Fact]
    public void The_King_and_a_sore_loser_want_pink_slips()
    {
        var king = Rival("King");
        king.IsBoss = true;
        king.BossPath = "King";
        var sore = Rival("Sore");
        sore.Grudge = new Grudge();
        var time = new DateTime(1969, 6, 21, 12, 0, 0);

        Assert.True(StreetEncounters.OfferFrom(king, 1000m, "drag", time, 1.0, true, new Random(1))!.PinkSlips);
        Assert.True(StreetEncounters.OfferFrom(sore, 1000m, "drag", time, 1.0, true, new Random(1))!.PinkSlips);
    }

    [Fact]
    public void Pink_slips_are_only_offered_against_a_car_the_rival_would_stake_theirs_on()
    {
        var king = Rival("King");
        king.IsBoss = true;
        king.BossPath = "King";
        var sore = Rival("Sore");
        sore.Grudge = new Grudge();
        var bold = Rival("Bold", aggression: 100, money: 500m);
        var time = new DateTime(1969, 6, 21, 12, 0, 0);

        // The King and a sore loser want the car or nothing; anybody else puts cash up instead
        Assert.Null(StreetEncounters.OfferFrom(king, 1000m, "drag", time, 1.0, false, new Random(1)));
        Assert.Null(StreetEncounters.OfferFrom(sore, 1000m, "drag", time, 1.0, false, new Random(1)));

        var random = new Random(3);
        for (var i = 0; i < 300; i++)
        {
            var offer = StreetEncounters.OfferFrom(bold, 1000m, "drag", time, 100.0, false, random);
            Assert.NotNull(offer);
            Assert.False(offer.PinkSlips);
        }
    }

    [Fact]
    public void A_cash_offer_stays_inside_the_limits_of_the_hour_and_both_wallets()
    {
        var rival = Rival("Cash", reputation: 50, aggression: 80, money: 60m);
        var time = new DateTime(1969, 6, 21, 12, 0, 0);
        var random = new Random(11);

        for (var i = 0; i < 300; i++)
        {
            var offer = StreetEncounters.OfferFrom(rival, 1000m, "drag", time, 0.0, true, random);
            Assert.NotNull(offer);
            Assert.False(offer.PinkSlips);
            Assert.InRange(offer.Wager, 10m, 60m);
            Assert.Equal(0m, offer.Wager % 5m);
        }
    }

    [Fact]
    public void Nobody_with_money_means_no_cash_offer()
    {
        var broke = Rival("Broke", money: 0m);
        var time = new DateTime(1969, 6, 21, 12, 0, 0);

        Assert.Null(StreetEncounters.OfferFrom(broke, 1000m, "drag", time, 0.0, true, new Random(2)));
    }

    #endregion

    [Theory]
    [InlineData("Dodge", "Charger R/T 1969 440 Magnum 4-speed", "Charger R/T")]
    [InlineData("Dodge", "Dodge Dart Phoenix Hardtop 1960 361 D-500 Ram", "Dart Phoenix Hardtop")]
    [InlineData("Ford", "Fairlane 500 GT 1966 390", "Fairlane 500 GT")]
    [InlineData("Chevrolet", "Bel Air", "Bel Air")]
    [InlineData("Plymouth", "1970", "1970")]
    public void A_car_is_named_in_passing_by_its_model(string brand, string name, string expected)
    {
        Assert.Equal(expected, StreetCorsa.Services.Catalog.CarNames.Short(new StreetCorsa.Models.Catalog.CarDefinition { Brand = brand, Name = name }));
    }

    [Theory]
    [InlineData("AC", "Ace", "AC Ace")]
    [InlineData("AC", "Aceca", "AC Aceca")]
    [InlineData("Chevrolet", "Chevrolet Camaro 1969 Z/28", "Chevrolet Camaro 1969 Z/28")]
    [InlineData("Batmobile", "The Batmobile", "The Batmobile")]
    [InlineData("American Motor Company", "AMC Javelin", "AMC Javelin")]
    [InlineData("Ford", "Fairlane", "Ford Fairlane")]
    public void A_car_names_its_make_once_as_a_word(string brand, string name, string expected)
    {
        Assert.Equal(expected, StreetCorsa.Services.Catalog.CarNames.MakeAndModel(new StreetCorsa.Models.Catalog.CarDefinition { Brand = brand, Name = name }));
    }

    #region The stage

    [Fact]
    public void With_nothing_to_show_it_a_rival_is_there_at_once_and_gone_at_once()
    {
        var stage = new StreetStage();
        var alongside = 0;
        var gone = 0;
        stage.RivalAlongside += () => alongside++;
        stage.RivalGone += () => gone++;

        stage.Arrive(new StreetCar("car", null), new StreetCorsa.Audio.EngineRunner(StreetCorsa.Audio.EngineChannel.Second));
        Assert.Equal(RivalPhase.Alongside, stage.Phase);
        Assert.Equal(1, alongside);

        stage.Leave();
        Assert.Equal(RivalPhase.None, stage.Phase);
        Assert.Equal(1, gone);
        Assert.Null(stage.RivalCar);

        stage.Leave();
        Assert.Equal(1, gone);
    }

    #endregion
}
