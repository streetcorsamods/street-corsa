using System.Runtime.InteropServices;
using Newtonsoft.Json;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Services;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-09-29 audit's fixes to the AC install, the race and the saves: lost kept copies, writes that must not
/// reach a hard link, restore problems the player is told of, untrusted timeslips, phantom track layouts, cfg paths,
/// the race's sun in the street's seasons, event-only opponents and the saves listing
/// </summary>
[Collection(RaceSessionTests.Name)]
public sealed class AuditFixesATests : IDisposable
{
    private const string Car = "sr_audit_car";

    private readonly TempDir _temp = new();
    private readonly string _cars;
    private readonly string _keep;
    private readonly string _cfg;

    public AuditFixesATests()
    {
        _cars = _temp.Combine("ac", "content", "cars");
        _keep = _temp.Combine("AcRestore");
        _cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(_cars);
        Directory.CreateDirectory(_cfg);
    }

    public void Dispose() => _temp.Dispose();

    private CarDataOverlay Overlay() => new(_cars, _keep);

    private string EngineIni(string car = Car) => Path.Combine(_cars, car, "data", "engine.ini");

    private void MakeCar(string car = Car) => _temp.File(EngineIni(car), "[HEADER]\r\nVERSION=1\r\n");

    // ----- kept copies -----

    [Fact]
    public void A_cfg_file_whose_kept_copy_is_gone_is_dropped_and_told_not_retried_forever()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "[RACE]\r\nMODEL=own\r\n");
        var backup = new AcConfigBackup(_cfg, _keep);
        backup.Keep(race);
        File.WriteAllText(race, "ours");

        var keepFolder = Path.Combine(_keep, AcConfigBackup.FolderName);
        foreach (var kept in Directory.GetFiles(keepFolder).Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            File.Delete(kept);

        var problems = new List<string>();
        Assert.Equal(0, backup.RestoreAll(problems));

        Assert.False(backup.HasPending);
        Assert.Contains(problems, p => p.Contains(race, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ours", File.ReadAllText(race));

        problems.Clear();
        Assert.Equal(0, backup.RestoreAll(problems));
        Assert.Empty(problems);
    }

    [Fact]
    public void A_cfg_file_goes_back_whole_with_its_read_only_flag()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "own");
        File.SetAttributes(race, FileAttributes.ReadOnly);
        var backup = new AcConfigBackup(_cfg, _keep);
        backup.Keep(race);
        File.SetAttributes(race, FileAttributes.Normal);
        File.WriteAllText(race, "ours");

        var problems = new List<string>();
        Assert.Equal(1, backup.RestoreAll(problems));

        Assert.Empty(problems);
        Assert.Equal("own", File.ReadAllText(race));
        Assert.True(File.GetAttributes(race).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void A_car_file_whose_kept_original_is_gone_is_told_and_the_car_is_off_the_list()
    {
        MakeCar();
        var overlay = Overlay();
        overlay.Apply(Car, new Dictionary<string, string> { ["engine.ini"] = "race" });
        File.Delete(Path.Combine(_keep, Car, "files", "engine.ini"));

        var problems = new List<string>();
        Assert.True(overlay.Restore(Car, problems));

        Assert.Single(problems);
        Assert.Contains("engine.ini", problems[0]);
        Assert.False(overlay.IsApplied(Car));
    }

    [Fact]
    public void Race_data_never_reaches_a_hard_linked_name_of_the_file()
    {
        MakeCar();
        var other = _temp.Combine("elsewhere_engine.ini");
        if (!CreateHardLink(other, EngineIni(), IntPtr.Zero)) return; // a volume without hard links: nothing to show

        var overlay = Overlay();
        overlay.Apply(Car, new Dictionary<string, string> { ["engine.ini"] = "race" });

        Assert.Equal("race", File.ReadAllText(EngineIni()));
        Assert.Equal("[HEADER]\r\nVERSION=1\r\n", File.ReadAllText(other));

        overlay.RestoreAll();
        Assert.Equal("[HEADER]\r\nVERSION=1\r\n", File.ReadAllText(EngineIni()));
        Assert.Equal("[HEADER]\r\nVERSION=1\r\n", File.ReadAllText(other));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    // ----- restore problems reach the player -----

    [Fact]
    public void A_car_that_cannot_be_put_back_comes_out_of_RestoreInstall_as_a_line_for_the_player()
    {
        MakeCar();
        _temp.File(Path.Combine("AcRestore", Car, "manifest.json"), "null");
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "own");
        var ini = new IniModificationService(_cfg, _keep);

        var problems = new List<string>();
        AssettoCorsaLauncher.RestoreInstall(Overlay(), ini, null, problems);

        Assert.Single(problems);
        Assert.Contains(Car, problems[0]);
        Assert.Equal("own", File.ReadAllText(race));

        var message = AssettoCorsaLauncher.RestoreProblemsMessage(problems);
        Assert.Contains(problems[0], message.Text);
    }

    [Fact]
    public void Everything_put_back_leaves_no_problems()
    {
        MakeCar();
        var overlay = Overlay();
        overlay.Apply(Car, new Dictionary<string, string> { ["engine.ini"] = "race" });

        var problems = new List<string>();
        Assert.Equal(1, AssettoCorsaLauncher.RestoreInstall(overlay, new IniModificationService(_cfg, _keep), null, problems));

        Assert.Empty(problems);
        Assert.Equal("[HEADER]\r\nVERSION=1\r\n", File.ReadAllText(EngineIni()));
    }

    // ----- cfg paths -----

    [Theory]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData(@"..\race.ini")]
    [InlineData(@"sub\race.ini")]
    [InlineData("")]
    public void A_cfg_file_is_one_file_name_in_the_cfg_folder(string name)
    {
        var ini = new IniModificationService(_cfg, _keep);
        Assert.Throws<ArgumentException>(() => ini.GetIniFilePath(name));
    }

    [Fact]
    public void A_cfg_file_name_resolves_in_the_cfg_folder()
    {
        var ini = new IniModificationService(_cfg, _keep);
        Assert.Equal(Path.Combine(_cfg, "race.ini"), ini.GetIniFilePath("race.ini"));
    }

    // ----- bracket slips from an untrusted file -----

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(300.0)]
    [InlineData(double.NaN)]
    public void An_elapsed_time_no_car_runs_is_no_quarter(double et)
    {
        Assert.Null(BracketRules.FinishOf(new Timeslip { QuarterMileSeconds = et, GreenSeconds = 1, ReactionSeconds = 0.5 }));
    }

    [Fact]
    public void A_real_elapsed_time_is_a_quarter()
    {
        var finish = BracketRules.FinishOf(new Timeslip { QuarterMileSeconds = 13.2, GreenSeconds = 1, ReactionSeconds = 0.5 });
        Assert.NotNull(finish);
        Assert.InRange(finish.Value.At, 14.699, 14.701);
        Assert.Equal(13.2, finish.Value.Et);
    }

    // ----- track layouts -----

    [Fact]
    public void Only_layout_folders_are_layouts_not_loose_ui_json_files()
    {
        var tracks = _temp.Combine("tracks");
        _temp.File(Path.Combine("tracks", "strip", "ui", "ui_track.json"), """{ "name": "Strip" }""");
        _temp.File(Path.Combine("tracks", "strip", "ui", "ui_gp.json"), """{ "name": "Phantom" }""");
        _temp.File(Path.Combine("tracks", "strip", "ui", "drag400", "ui_track.json"), """{ "name": "Quarter" }""");

        var loaded = new AssettoCorsaContentService().LoadTracks(tracks);

        var track = Assert.Single(loaded);
        var layout = Assert.Single(track.Configurations);
        Assert.Equal("drag400", layout.FolderName);
    }

    // ----- the race's sun -----

    [Fact]
    public void On_a_day_of_the_mean_sunset_the_sun_is_Content_Managers()
    {
        // Somewhere around the equinoxes the model's sunset is its mean: find that day
        var day = Enumerable.Range(0, 365).Select(d => new DateTime(2001, 1, 1).AddDays(d))
            .MinBy(d => Math.Abs(StreetLights.SunsetHour(d) - SeasonalSun.MeanSunset));

        foreach (var hour in new[] { 14.0, 17.0, 19.0, 22.0 })
        {
            var cm = Math.Round(16.0 * (hour - 13), 2);
            Assert.InRange(SeasonalSun.AcSunAngle(day.AddHours(hour)), cm - 1, cm + 1);
        }
    }

    [Fact]
    public void A_winter_evening_is_later_in_AC_than_on_the_clock_and_a_summer_one_earlier()
    {
        var december = new DateTime(1970, 12, 20, 17, 30, 0);
        var june = new DateTime(1970, 6, 21, 19, 0, 0);

        // December's sunset (about 16:25) is AC's sunset hour: 17:30 is past it, and past 18:00's 80 degrees
        Assert.True(IniModificationService.SunAngle(december) > 16.0 * (SeasonalSun.MeanSunset - 13));
        // June's (about 20:10) is later: 19:00 is still before AC's sunset
        Assert.True(IniModificationService.SunAngle(june) < 16.0 * (19 - 13));

        // The sun is where the street screens light the street: after dark in AC when the street is at night
        Assert.Equal(StreetLight.Night, StreetLights.At(december.AddHours(0.5)));
    }

    [Fact]
    public void The_morning_and_noon_are_as_Content_Manager_has_them_and_every_minute_stays_in_ACs_range()
    {
        Assert.Equal(-80.0, IniModificationService.SunAngle(new DateTime(1970, 12, 20, 8, 0, 0)));
        Assert.Equal(0.0, IniModificationService.SunAngle(new DateTime(1970, 6, 21, 13, 0, 0)));
        Assert.Equal(IniModificationService.NoonSunAngle, IniModificationService.SunAngle(null));

        foreach (var date in new[] { new DateTime(1970, 1, 1), new DateTime(1970, 6, 21), new DateTime(1970, 12, 20) })
        {
            var last = double.MinValue;
            for (var minute = 0; minute < 24 * 60; minute++)
            {
                var angle = IniModificationService.SunAngle(date.AddMinutes(minute));
                Assert.InRange(angle, SeasonalSun.MinAngle, SeasonalSun.MaxAngle);
                Assert.True(angle >= last, $"the sun went back at {date.AddMinutes(minute):g}");
                last = angle;
            }
        }
    }

    // ----- event-only opponents -----

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Only_a_racer_of_the_scene_counts_as_beaten(bool eventOnly, bool counted)
    {
        var world = RaceFakes.World();
        world.Context.IsEventOnlyOpponent = eventOnly;
        var json = RaceResultValidatorTests.Fixture("1.1", RaceFakes.PlayerName, RaceFakes.OpponentName);
        json["participants"]![0]!["performance"]!["final_position"] = 1;
        json["participants"]![1]!["performance"]!["final_position"] = 2;
        var result = JsonConvert.DeserializeObject<RaceResultJson>(json.ToString())!;

        new RaceResultProcessor(new RaceResultProcessorTests.FakeRepository(), new RaceSessionRepository(new SaveDatabase(_temp.Combine("unused-saves"))),
            new RaceFakes.FakeCareer(), new RaceFakes.FakeEvents()).ProcessRaceResult(result, world.Context, world.State);

        Assert.Equal(counted, world.State.Career.DefeatedOpponentIds.Contains(RaceFakes.OpponentName));
    }

    // ----- saves -----

    [Fact]
    public void Saves_in_a_folder_under_a_backup_path_are_listed()
    {
        var saves = _temp.Combine("old.backup", "Saves");
        _temp.File(Path.Combine("old.backup", "Saves", "career.db"), "");

        using var repository = new GameStateRepository(database: new SaveDatabase(saves));

        Assert.Equal("career", Assert.Single(repository.ListSaves()));
    }

    [Fact]
    public async Task A_race_context_is_found_settled_through_its_index()
    {
        var database = new SaveDatabase(_temp.Combine("saves"));
        var sessions = new RaceSessionRepository(database);
        var context = Guid.NewGuid();

        await sessions.SaveAsync("career", new ProcessedRaceSession { SessionId = "s1", RaceContextId = context });

        Assert.True(await sessions.IsContextSettledAsync("career", context));
        Assert.False(await sessions.IsContextSettledAsync("career", Guid.NewGuid()));
        var indexed = database.Use("career", db =>
            db.GetCollection("$indexes").Find(LiteDB.Query.EQ("collection", "ProcessedRaceSessions")).Any(i => i["name"].AsString == "RaceContextId"));
        Assert.True(indexed);
        database.Dispose();
    }
}
