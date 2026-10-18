using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Services;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Race.Validation;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// E10: the kinds of race as data (Content/*/formats): the base game's four, a mod's own with its Lua module or its
/// own mode, what race.ini tells the mode, a format won on score, and races saved before formats
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class RaceFormatTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static RaceFormats Base => RaceFormats.Current;

    // ---- the base game's formats ----

    [Fact]
    public void The_base_game_has_drag_circuit_sprint_and_time_attack()
    {
        Assert.Empty(Base.Problems);
        Assert.Equal(["drag", "circuit", "sprint", "time_attack"], Base.All.Select(f => f.Id));
        Assert.True(Base.IsDrag("drag"));
        Assert.False(Base.IsDrag("time_attack"));
        Assert.True(Base.Get("drag")!.Bracket);
        Assert.True(Base.Get("circuit")!.Police);
        Assert.False(Base.Get("time_attack")!.Police);
        Assert.Equal("format_road", RaceFormats.ModuleOf(Base.Get("sprint")!));
        Assert.Equal("format_time_attack", RaceFormats.ModuleOf(Base.Get("time_attack")!));
    }

    [Theory]
    [InlineData("DragRace", "drag")]
    [InlineData("Circuit", "circuit")]
    [InlineData("sprint", "sprint")]
    [InlineData("streetcorsa:time_attack", "time_attack")]
    public void The_race_types_from_before_formats_are_the_base_formats_aliases(string written, string id) =>
        Assert.Equal(id, Base.IdOf(written));

    [Fact]
    public void A_format_no_mod_has_is_run_as_a_road_race_and_counts_as_one()
    {
        Assert.Null(Base.Get("gone:drift"));
        Assert.Equal("circuit", Base.Of("gone:drift").Id);
        Assert.False(Base.IsDrag("gone:drift"));
        Assert.Equal("gone:drift", Base.IdOf("gone:drift"));
    }

    [Fact]
    public void The_street_races_the_first_street_format_of_each_family_and_the_diner_offers_its_own()
    {
        Assert.Equal("drag", Base.Street(RaceFormatFamilies.Drag)!.Id);
        Assert.Equal("circuit", Base.Street(RaceFormatFamilies.Road)!.Id);
        Assert.Equal(["circuit", "time_attack"], Base.ForDiner(RaceFormatFamilies.Road).Select(f => f.Id));
        Assert.Equal(["drag"], Base.ForDiner(RaceFormatFamilies.Drag).Select(f => f.Id));
    }

    [Fact]
    public void The_race_mode_has_every_module_the_base_game_names()
    {
        var mode = Path.Combine(RepoPaths.Root, "apps", "new-modes", "sc_race");
        foreach (var format in Base.All)
            Assert.True(File.Exists(Path.Combine(mode, RaceFormats.ModuleOf(format) + ".lua")), format.Id);
    }

    // ---- a mod's formats ----

    private ModSet Mods(string formatsJson, params (string Path, string Text)[] files)
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/formats/formats.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "formats", "formats.json")));
        _temp.File("jdm/mod.json", """{ "id": "jdm" }""");
        _temp.File("jdm/formats/formats.json", formatsJson);
        foreach (var (path, text) in files) _temp.File(Path.Combine("jdm", path), text);
        return ModSet.Load(_temp.Path);
    }

    private string ModeFolder()
    {
        var folder = _temp.Combine("build", "sc_race");
        foreach (var name in new[] { "mode.lua", "format_drag.lua", "format_road.lua", "format_time_attack.lua" })
            _temp.File(Path.Combine("build", "sc_race", name), "-- " + name);
        return folder;
    }

    [Fact]
    public void A_mods_format_brings_its_module_under_its_own_name()
    {
        var mods = Mods("""
            { "formats": [
              { "id": "drift", "family": "road", "module": "drift", "laps": 2, "win": "score", "better": "higher" },
              { "id": "touge", "family": "road", "module": "road", "police": true }
            ] }
            """, ("formats/drift.lua", "return {}"));

        var formats = RaceFormats.Load(mods, ModeFolder());

        Assert.Empty(formats.Problems);
        var drift = formats.Get("jdm:drift")!;
        Assert.True(drift.ByScore);
        Assert.True(drift.HigherWins);
        Assert.Equal("format_jdm-drift", RaceFormats.ModuleOf(drift));
        // A mod's format may run on one of the mode's own modules
        Assert.Equal("format_road", RaceFormats.ModuleOf(formats.Get("jdm:touge")!));
        var module = Assert.Single(formats.ModuleFiles);
        Assert.Equal("format_jdm-drift.lua", module.Name);
        Assert.Equal("jdm:drift", formats.IdOf("JDM:Drift"));
    }

    [Fact]
    public void A_format_that_does_not_read_is_left_out_and_told()
    {
        var mods = Mods("""
            { "formats": [
              { "id": "wrong_family", "family": "oval", "module": "road" },
              { "id": "wrong_win", "family": "road", "module": "road", "win": "style" },
              { "id": "no_module", "family": "road", "module": "missing" },
              { "id": "bad_name", "family": "road", "module": "../x" },
              { "id": "road_bracket", "family": "road", "module": "road", "bracket": true, "laps": 500 }
            ] }
            """);

        var formats = RaceFormats.Load(mods, ModeFolder());

        Assert.Null(formats.Get("jdm:wrong_family"));
        Assert.Null(formats.Get("jdm:wrong_win"));
        Assert.Null(formats.Get("jdm:no_module"));
        Assert.Null(formats.Get("jdm:bad_name"));
        var kept = formats.Get("jdm:road_bracket")!;
        Assert.False(kept.Bracket);
        Assert.Equal(RaceFormats.MaxLaps, kept.Laps);
        Assert.Equal(6, formats.Problems.Count);
    }

    [Fact]
    public void A_mods_own_mode_is_its_folder_and_its_results_are_taken()
    {
        var mods = Mods("""{ "formats": [ { "id": "gymkhana", "family": "road", "mode": "gymkhana" } ] }""",
            ("formats/gymkhana/mode.lua", "-- a whole mode"), ("formats/gymkhana/manifest.ini", "[ABOUT]"), ("formats/gymkhana/lib/util.lua", "--"));
        var formats = RaceFormats.Load(mods, ModeFolder());
        var gymkhana = formats.Get("jdm:gymkhana")!;

        Assert.Equal("jdm-gymkhana", RaceFormats.ModeIdOf(gymkhana));
        Assert.Contains("jdm-gymkhana", formats.ModeIds);
        Assert.Contains(IniModificationService.RaceModeId, formats.ModeIds);

        // Installed whole beside sc_race, sub-folders too, with the race's part models and the script that puts them on
        var ac = _temp.Combine("ac");
        Directory.CreateDirectory(Path.Combine(ac, "extension"));
        var source = ModeFolder();
        _temp.File(Path.Combine("build", "sc_race", "car_parts.lua"), "-- parts");
        var model = _temp.File("cragar.kn5", "kn5");
        Assert.Null(new ScRaceMode(source, () => formats).Install(ac, gymkhana,
            [new StreetCorsa.Parts.Cars.ShownModel(StreetCorsa.Parts.Cars.ShownParts.FileName("rims/mopar/cragar_torq_thrust_wheel_15x8"), model)]));
        var installed = Path.Combine(ac, "extension", "lua", "new-modes", "jdm-gymkhana");
        Assert.True(File.Exists(Path.Combine(installed, "mode.lua")));
        Assert.True(File.Exists(Path.Combine(installed, "lib", "util.lua")));
        Assert.Equal("-- parts", File.ReadAllText(Path.Combine(installed, "car_parts.lua")));
        Assert.Equal("kn5", File.ReadAllText(Path.Combine(installed, "parts", "rims_mopar_cragar_torq_thrust_wheel_15x8.kn5")));
    }

    [Fact]
    public void A_mode_folder_without_a_mode_lua_is_no_format()
    {
        var mods = Mods("""{ "formats": [ { "id": "empty", "family": "road", "mode": "empty" } ] }""", ("formats/empty/readme.txt", "-"));

        Assert.Null(RaceFormats.Load(mods, ModeFolder()).Get("jdm:empty"));
    }

    [Fact]
    public void The_mods_modules_go_in_beside_the_mode_and_a_module_no_longer_there_goes()
    {
        var mods = Mods("""{ "formats": [ { "id": "drift", "family": "road", "module": "drift" } ] }""", ("formats/drift.lua", "return { drift = true }"));
        var formats = RaceFormats.Load(mods, ModeFolder());
        var ac = _temp.Combine("ac");
        Directory.CreateDirectory(Path.Combine(ac, "extension"));
        var stale = Path.Combine(ScRaceMode.InstalledFolder(ac), "format_old_gone.lua");
        _temp.File(Path.GetRelativePath(_temp.Path, stale), "--");

        Assert.Null(new ScRaceMode(ModeFolder(), () => formats).Install(ac));

        Assert.Equal("return { drift = true }", File.ReadAllText(Path.Combine(ScRaceMode.InstalledFolder(ac), "format_jdm-drift.lua")));
        Assert.True(File.Exists(Path.Combine(ScRaceMode.InstalledFolder(ac), "format_time_attack.lua")));
        Assert.False(File.Exists(stale));
    }

    // ---- events ----

    [Fact]
    public void An_event_names_its_format_or_its_race_type_from_before_and_may_set_its_laps()
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/events/events.json", """
            { "events": [
              { "id": "old", "name": "Old", "raceType": "DragRace" },
              { "id": "attack", "name": "Attack", "format": "time_attack", "laps": 5 },
              { "id": "none", "name": "None" },
              { "id": "unknown", "name": "Unknown", "format": "drift" }
            ] }
            """);

        var content = CareerContent.Load(ModSet.Load(_temp.Path), RaceFormats.Installed);

        Assert.Equal("drag", content.Events["old"].Format);
        Assert.Equal("time_attack", content.Events["attack"].Format);
        Assert.Equal(5, content.Events["attack"].Laps);
        Assert.Equal("drag", content.Events["none"].Format);
        Assert.False(content.Events.ContainsKey("unknown"));
        Assert.Contains(content.Problems, p => p.Contains("drift"));
    }

    [Fact]
    public void The_base_game_has_a_time_attack_event()
    {
        Assert.Contains(CareerContent.Installed.Events.Values, e => e.Format == "time_attack");
    }

    // ---- race.ini ----

    private string[] RaceIni(DragRaceIntent intent)
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        Assert.True(new IniModificationService(cfg, _temp.Combine("AcRestore")).ApplyIntent(intent));
        return File.ReadAllLines(Path.Combine(cfg, "race.ini"));
    }

    private static DragRaceIntent Intent(string format, int? laps = null) => new()
    {
        PlayerCarId = "a", OpponentCarId = "b", PlayerName = "P", OpponentName = "O", Format = format, Laps = laps,
        Police = new RacePolice { CarId = "police_car", Skins = ["skin1", "skin2"], SpotShare = 0.4 }
    };

    [Fact]
    public void A_time_attack_tells_the_mode_its_module_laps_and_score_and_gives_AC_more_laps()
    {
        var ini = RaceIni(Intent("time_attack"));

        Assert.Contains("FORMAT=time_attack", ini);
        Assert.Contains("MODULE=format_time_attack", ini);
        Assert.Contains("LAPS=3", ini);
        Assert.Contains("SCORE=LOWER", ini);
        Assert.Contains("RACE_LAPS=99", ini);
        Assert.Contains("RACE_TYPE=ROAD", ini);
        Assert.Contains("NAME=Time Attack", ini);
        // No police at a time attack, whatever was sent
        Assert.DoesNotContain(ini, l => l.StartsWith("POLICE="));
        Assert.Contains("CARS=2", ini);
    }

    [Fact]
    public void An_events_laps_go_over_the_formats_and_a_road_race_brings_its_police()
    {
        var ini = RaceIni(Intent("Circuit", laps: 3));

        Assert.Contains("FORMAT=circuit", ini);
        Assert.Contains("MODULE=format_road", ini);
        Assert.Contains("LAPS=3", ini);
        Assert.Contains("RACE_LAPS=3", ini);
        Assert.Contains("POLICE=2,3", ini);
        Assert.Contains("__CM_CUSTOM_MODE=sc_race", ini);
    }

    [Fact]
    public void A_drag_race_is_one_run_whatever_the_event_says()
    {
        var ini = RaceIni(Intent("drag", laps: 2));

        Assert.Contains("LAPS=1", ini);
        Assert.Contains("RACE_LAPS=1", ini);
    }

    [Fact]
    public void A_test_and_tune_runs_in_sc_race_s_own_drag_module_whatever_a_mod_made_of_drag()
    {
        var before = RaceFormats.Current;
        try
        {
            RaceFormats.Current = RaceFormats.From([new RaceFormat { Id = "drag", ModId = "jdm", Family = RaceFormatFamilies.Drag, Mode = "strip" }]);
            var intent = Intent("drag");
            intent.TunePasses = 3;

            var ini = RaceIni(intent);

            Assert.Contains("__CM_CUSTOM_MODE=sc_race", ini);
            Assert.Contains("MODULE=format_drag", ini);
        }
        finally
        {
            RaceFormats.Current = before;
        }
    }

    // ---- where a race is run ----

    private static Models.AC.TrackInfo Layouts(string id, string run, params (string Folder, string Run, string[] Tags)[] layouts) => new()
    {
        TrackId = id,
        Run = run,
        Configurations = layouts.Select(l => new Models.AC.TrackConfiguration { FolderName = l.Folder, Run = l.Run, Tags = [.. l.Tags] }).ToList()
    };

    [Fact]
    public void A_layout_raced_from_A_to_B_says_so_in_its_run_or_its_tags_else_its_tracks()
    {
        var track = Layouts("mountain", "clockwise", ("up", "Point-to-point", []), ("loop", "", []), ("down", "", ["Touge"]));

        Assert.True(track.IsPointToPoint("up"));
        Assert.False(track.IsPointToPoint("loop"));
        Assert.True(track.IsPointToPoint("down"));
        Assert.True(Layouts("hill", "A to B").IsPointToPoint(null));
        Assert.False(Layouts("ring", "anticlockwise").IsPointToPoint(null));
    }

    [Fact]
    public void A_race_of_more_than_one_lap_is_never_picked_on_a_layout_raced_from_A_to_B()
    {
        Assert.True(Base.Get("time_attack")!.NeedsLoop());
        Assert.False(Base.Get("circuit")!.NeedsLoop());
        Assert.True(Base.Get("circuit")!.NeedsLoop(3));
        Assert.False(Base.Get("drag")!.NeedsLoop(3));

        Models.AC.TrackInfo[] tracks = [Layouts("mountain", "clockwise", ("up", "point to point", []), ("loop", "clockwise", []))];

        // The named track's layout that comes back round, for a time attack; any of them for a sprint
        Assert.Equal<(string, string?)?>(("mountain", "loop"), Screens.Shared.RaceSetupBuilder.PickTrack(tracks, "time_attack", "mountain", 1));
        Assert.Equal<(string, string?)?>(("mountain", "up"), Screens.Shared.RaceSetupBuilder.PickTrack(tracks, "sprint", "mountain", 1));
        // An event's own laps count too; nothing that loops is nothing to race on
        Assert.Null(Screens.Shared.RaceSetupBuilder.PickTrack([Layouts("hill", "A to B")], "circuit", null, 1, laps: 2));
        Assert.NotNull(Screens.Shared.RaceSetupBuilder.PickTrack([Layouts("hill", "A to B")], "circuit", null, 1));
    }

    // ---- words ----

    [Fact]
    public void A_mods_format_never_takes_the_base_games_words_nor_shares_another_mods()
    {
        var mine = new RaceFormat { Id = "x:time_attack", ModId = "x" };
        var a = new RaceFormat { Id = "a:drift", ModId = "a" };
        var b = new RaceFormat { Id = "b:drift", ModId = "b" };
        var formats = RaceFormats.From([Base.Get("time_attack")!, mine, a, b]);

        Assert.Equal("Time Attack", formats.NameOf("time_attack"));
        Assert.Null(formats.OwnKey("paper.race_type", mine));
        Assert.Equal("Race", formats.NameOf(mine));
        Assert.Equal(GameText.Say("result.score.won"), formats.SayFor(mine, "result.score.won"));
        Assert.Null(formats.OwnKey("paper.race_type", a));
        Assert.Equal("Bracket Race", formats.NameOf("drag", bracket: true));
    }

    [Fact]
    public void A_road_format_with_drag_in_its_id_is_told_to_the_AI_as_a_road_race()
    {
        var before = RaceFormats.Current;
        try
        {
            RaceFormats.Current = RaceFormats.From([new RaceFormat { Id = "jdm:drag_circuit", ModId = "jdm", Family = RaceFormatFamilies.Road }]);

            Assert.Equal(GameText.Say("chat.fact.kind_road"), Services.Talk.TalkFacts.KindOf("jdm:drag_circuit"));
            Assert.Equal(GameText.Say("chat.fact.kind_drag"), Services.Talk.TalkFacts.KindOf("gone:drag"));
        }
        finally
        {
            RaceFormats.Current = before;
        }
    }

    // ---- the result ----

    private static RaceResultJson Attack(double? playerScore, double? rivalScore, int playerPosition, string? rivalOut = null)
    {
        var json = RaceResultValidatorTests.Fixture("1.1", RaceFakes.PlayerName, RaceFakes.OpponentName);
        json["session"]!["format"] = "time_attack";
        json["participants"]![0]!["score"] = playerScore is { } p ? new JValue(p) : JValue.CreateNull();
        json["participants"]![1]!["score"] = rivalScore is { } r ? new JValue(r) : JValue.CreateNull();
        json["participants"]![0]!["lap_times_ms"] = new JArray(70000.0, playerScore ?? 71000.0);
        json["participants"]![0]!["performance"]!["final_position"] = playerPosition;
        json["participants"]![1]!["performance"]!["final_position"] = 3 - playerPosition;
        if (rivalOut == "crashed") json["participants"]![1]!["crash"]!["crashed"] = true;
        if (rivalOut == "broke_down") json["participants"]![1]!["broke_down"] = true;
        return JsonConvert.DeserializeObject<RaceResultJson>(json.ToString())!;
    }

    private RaceResultProcessor Processor(RaceResultProcessorTests.FakeRepository repository) =>
        new(repository, new RaceSessionRepository(new SaveDatabase(_temp.Combine("unused-saves"))), new RaceFakes.FakeCareer(), new RaceFakes.FakeEvents());

    [Theory]
    [InlineData(66650.0, 80000.0, 2, true)]   // the better lap wins, whoever led AC's race
    [InlineData(80000.0, 66650.0, 1, false)]
    [InlineData(66650.0, null, 2, true)]      // a rival without a lap is beaten
    [InlineData(null, 66650.0, 1, false)]
    public void A_time_attack_is_decided_on_the_best_lap(double? player, double? rival, int position, bool playerWon)
    {
        var world = RaceFakes.World(format: "time_attack");
        var repository = new RaceResultProcessorTests.FakeRepository();

        var messages = Processor(repository).ProcessRaceResult(Attack(player, rival, position), world.Context, world.State);

        Assert.Equal(playerWon, repository.Recorded.Single().PlayerWon);
        Assert.Equal("time_attack", repository.Recorded.Single().RaceType);
        // The laps come first, under the format's name
        Assert.Equal("Time Attack", messages[0].Title);
        Assert.Contains(playerWon ? "The best lap is yours" : "set the best lap", messages[0].Text);
        if (player == 66650.0) Assert.Contains("1:06.650", messages[0].Text);
    }

    [Theory]
    [InlineData("crashed", 66650.0, 60000.0, 2, false)]   // the rival set the better lap before it went out: it wins
    [InlineData("broke_down", 66650.0, 60000.0, 2, false)]
    [InlineData("crashed", 60000.0, 66650.0, 1, true)]
    [InlineData(null, 60000.0, 60000.0, 2, false)]          // the same lap both ways: the places the mode gave
    public void A_time_attack_rival_out_of_the_race_keeps_its_best_lap(string? rivalOut, double player, double rival, int position, bool playerWon)
    {
        var world = RaceFakes.World(format: "time_attack");
        var repository = new RaceResultProcessorTests.FakeRepository();

        Processor(repository).ProcessRaceResult(Attack(player, rival, position, rivalOut), world.Context, world.State);

        Assert.Equal(playerWon, repository.Recorded.Single().PlayerWon);
    }

    [Fact]
    public void A_score_race_without_scores_goes_by_the_order_at_the_line()
    {
        var world = RaceFakes.World(format: "time_attack");
        var repository = new RaceResultProcessorTests.FakeRepository();

        Processor(repository).ProcessRaceResult(Attack(null, null, 1), world.Context, world.State);

        Assert.True(repository.Recorded.Single().PlayerWon);
    }

    [Fact]
    public void A_time_attack_won_counts_as_a_road_win()
    {
        var world = RaceFakes.World(format: "time_attack");
        var career = new RaceFakes.FakeCareer();
        new RaceResultProcessor(new RaceResultProcessorTests.FakeRepository(), new RaceSessionRepository(new SaveDatabase(_temp.Combine("s"))), career, new RaceFakes.FakeEvents())
            .ProcessRaceResult(Attack(60000, 70000, 2), world.Context, world.State);

        Assert.Equal(1, world.State.Career.GetCounter(Models.Career.Milestones.MilestoneTrigger.RoadWins));
        Assert.Equal(0, world.State.Career.GetCounter(Models.Career.Milestones.MilestoneTrigger.DragWins));
    }

    [Theory]
    [InlineData(66650.4, "1:06.650")]
    [InlineData(59999.6, "1:00.000")]
    [InlineData(5000, "0:05.000")]
    public void A_lap_time_shows_as_the_stopwatch_does(double ms, string shown) =>
        Assert.Equal(shown, RaceResultProcessor.LapTime(ms));

    [Fact]
    public async Task A_result_from_a_formats_own_mode_is_taken_and_one_from_nowhere_is_not()
    {
        var json = RaceResultValidatorTests.Fixture("1.1", "P", "O");
        json["metadata"]!["source"] = "nobody_mode";
        var path = _temp.File("x.json", json.ToString());

        Assert.False((await new RaceResultValidator().ValidateFileAsync(path)).IsValid);
    }

    [Fact]
    public void A_race_counts_by_the_family_it_was_set_up_with_even_when_its_format_is_gone()
    {
        Assert.True(new RaceContext { Format = "gone:strip", Family = RaceFormatFamilies.Drag }.IsDrag);
        Assert.False(new RaceContext { Format = "gone:strip" }.IsDrag);
        Assert.True(new RaceContext { Format = "DragRace" }.IsDrag);
    }

    // ---- saves from before formats ----

    [Fact]
    public void A_pending_race_saved_as_a_race_type_reads_as_its_format()
    {
        var mapper = SaveMapper.Create();
        var document = mapper.ToDocument(new RaceContext { OpponentName = "Ace", Format = "x" });
        Assert.True(document.ContainsKey("RaceType"));
        document["RaceType"] = "Circuit";

        var context = mapper.ToObject<RaceContext>(document);

        Assert.Equal("Circuit", context.Format);
        Assert.Equal("circuit", Base.IdOf(context.Format));
        Assert.False(Base.IsDrag(context.Format));
    }
}
