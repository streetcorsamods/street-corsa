using System.IO;
using StreetCorsa.Dialogs;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.Career.Milestones;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Storage;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Tests;

/// <summary>The 2026-09-29 audit's fixes in the app shell, the helpers and the career models</summary>
[Collection(GlobalsCollection.Name)]
public class AuditFixesFTests
{
    // ----- PathNames: every device name Windows knows -----

    [Theory]
    [InlineData("COM0", "COM0_")]
    [InlineData("lpt0.txt", "lpt0_.txt")]
    [InlineData("COM¹", "COM¹_")]
    [InlineData("LPT³.save", "LPT³_.save")]
    [InlineData("CONIN$", "CONIN$_")]
    [InlineData("conout$.log", "conout$_.log")]
    [InlineData("COM10", "COM10")]
    [InlineData("CONIN", "CONIN")]
    public void Every_device_name_gets_a_mark(string name, string expected) =>
        Assert.Equal(expected, PathNames.Sanitize(name));

    // ----- SafeFile: a read-only target -----

    [Fact]
    public void A_read_only_target_is_written_and_the_mark_taken_off_is_told()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sc-audit-f-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "race.ini");
        var told = new List<string>();
        var before = SafeFile.ReadOnlyCleared;
        try
        {
            File.WriteAllText(target, "old");
            File.SetAttributes(target, FileAttributes.ReadOnly);
            SafeFile.ReadOnlyCleared = path => { lock (told) told.Add(path); };

            SafeFile.WriteAllText(target, "new");

            Assert.Equal("new", File.ReadAllText(target));
            Assert.Equal(0, (int)(File.GetAttributes(target) & FileAttributes.ReadOnly));
            lock (told) Assert.Contains(told, p => string.Equals(p, target, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            SafeFile.ReadOnlyCleared = before;
            if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
    }

    // ----- RaceStartState: no NaN reaches race.ini -----

    [Fact]
    public void Engine_life_and_gearbox_that_are_not_numbers_race_as_sound()
    {
        var keys = new RaceStartState { EngineLife = double.NaN, GearboxWear = double.PositiveInfinity, Dirt = double.NaN }
            .IniKeys(0).ToDictionary(k => k.Key, k => k.Value);

        Assert.Equal("1000.0", keys["CAR_0_ENGINE_LIFE"]);
        Assert.Equal("0.000", keys["CAR_0_GEARBOX"]);
        Assert.Equal("0.000", keys["CAR_0_DIRT"]);
    }

    [Fact]
    public void Engine_life_and_gearbox_are_held_to_what_AC_knows()
    {
        var keys = new RaceStartState { EngineLife = -5, GearboxWear = 3, Dirt = 0.25 }
            .IniKeys(1).ToDictionary(k => k.Key, k => k.Value);

        Assert.Equal("0.0", keys["CAR_1_ENGINE_LIFE"]);
        Assert.Equal("1.000", keys["CAR_1_GEARBOX"]);
        Assert.Equal("0.250", keys["CAR_1_DIRT"]);
    }

    // ----- RacerCollection: no logger made when the type loads -----

    [Fact]
    public void The_racers_take_no_logger_when_their_type_loads()
    {
        var staticLoggers = typeof(RacerCollection)
            .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(f => typeof(IAppLogger).IsAssignableFrom(f.FieldType));
        Assert.Empty(staticLoggers);

        var racers = new RacerCollection();
        racers.AddRacer(new Opponent("Ace", 30, Gender.Male, 50, 50) { Status = RacerStatus.ReadyToRace });
        racers.AddRacer(new Opponent("ace", 31, Gender.Male, 50, 50) { Status = RacerStatus.ReadyToRace });
        Assert.NotNull(racers.Find("ace (2)"));
    }

    // ----- NavigationService: an Exit that throws -----

    private sealed class Screen(bool throwOnExit = false) : IScreen
    {
        public List<string> Calls { get; } = new();
        public void Enter() => Calls.Add("Enter");

        public void Exit()
        {
            Calls.Add("Exit");
            if (throwOnExit) throw new InvalidOperationException("exit failed");
        }

        public void Resume() => Calls.Add("Resume");
    }

    private static NavigationService NewNavigation(DialogService dialogs) => TestNavigation.WithoutServices(dialogs);

    [Fact]
    public void A_screen_that_fails_to_leave_does_not_keep_the_player_on_it()
    {
        var dialogs = new DialogService();
        var navigation = NewNavigation(dialogs);
        var stuck = new Screen(throwOnExit: true);
        var next = new Screen();
        navigation.SafeNavigate("stuck", () => stuck);

        Assert.True(navigation.SafeNavigate("next", () => next));

        Assert.Same(next, navigation.CurrentScreen);
        Assert.Equal(new[] { "Enter", "Exit" }, stuck.Calls);
        Assert.Equal(new[] { "Enter" }, next.Calls);
        Assert.False(dialogs.IsDialogOpen);
    }

    // ----- DialogService: a hook that throws does not stall the queue -----

    private sealed class ThrowingDialog : IDialog
    {
        public string? DuplicateKey => null;
        public void OnOpened() => throw new InvalidOperationException("open hook");
        public void OnClosed() => throw new InvalidOperationException("close hook");
    }

    [Fact]
    public void A_dialog_whose_hooks_throw_still_opens_closes_and_lets_the_next_one_show()
    {
        var dialogs = new DialogService();
        var broken = new ThrowingDialog();
        var next = new FakeDialog();

        dialogs.ShowDialog(broken);
        dialogs.ShowDialog(next);
        Assert.Same(broken, dialogs.CurrentDialog);

        dialogs.CloseDialog();

        Assert.Same(next, dialogs.CurrentDialog);
        Assert.Equal(1, next.Opened);
    }

    // ----- AsyncRelayCommand: a failure is told by default -----

    [Fact]
    public void A_command_without_its_own_error_handler_reports_through_the_default()
    {
        var failure = new InvalidOperationException("sale failed");
        var reported = new List<Exception>();
        var before = AsyncRelayCommand.ReportFailure;
        try
        {
            AsyncRelayCommand.ReportFailure = ex => { lock (reported) reported.Add(ex); };

            // A task that has failed already: Execute gets to its catch before it returns
            new AsyncRelayCommand(() => Task.FromException(failure), name: "Sell").Execute(null);

            lock (reported) Assert.Contains(failure, reported);
        }
        finally
        {
            AsyncRelayCommand.ReportFailure = before;
        }
    }

    [Fact]
    public void A_command_with_its_own_error_handler_does_not_report_twice()
    {
        var failure = new InvalidOperationException("repair failed");
        var reported = new List<Exception>();
        Exception? own = null;
        var before = AsyncRelayCommand.ReportFailure;
        try
        {
            AsyncRelayCommand.ReportFailure = ex => { lock (reported) reported.Add(ex); };

            new AsyncRelayCommand(() => Task.FromException(failure), onError: ex => own = ex).Execute(null);

            Assert.Same(failure, own);
            lock (reported) Assert.DoesNotContain(failure, reported);
        }
        finally
        {
            AsyncRelayCommand.ReportFailure = before;
        }
    }

    // ----- The career: victories open from the start, the rules, the events' state -----

    [Fact]
    public void A_new_career_opens_the_paths_that_ask_nothing_to_unlock()
    {
        using var temp = new TempDir();
        var content = CareerContent.Load(TestCareer.Mods(temp));
        var career = CareerState.CreateNew(content.OpenFromStart);
        var open = content.Victories.Where(v => v.Unlock.Count == 0).Select(v => v.Id).ToList();

        Assert.NotEmpty(open);
        Assert.Equal(open.OrderBy(id => id), career.UnlockedVictories.Keys.OrderBy(id => id));
        Assert.Contains("Legend", career.UnlockedVictories.Keys);
        Assert.DoesNotContain("Crown", career.UnlockedVictories.Keys);

        Assert.Equal(["mod:path"], CareerState.CreateNew(["mod:path"]).UnlockedVictories.Keys);
        // The model alone opens nothing: it never reads the mods (a save being loaded builds one only to replace it)
        Assert.Empty(new GameState().Career.UnlockedVictories);
    }

    [Fact]
    public void Domination_counts_the_scene_and_the_beaten_who_left()
    {
        var game = GameState.CreateNew("P");
        game.Racers = new RacerCollection();
        game.Racers.AddRacer(new Opponent("A", 30, Gender.Male, 50, 50) { Status = RacerStatus.ReadyToRace });
        game.Racers.AddRacer(new Opponent("B", 30, Gender.Male, 50, 50) { Status = RacerStatus.Departed });
        game.Racers.AddRacer(new Opponent("C", 30, Gender.Male, 50, 50) { Status = RacerStatus.Departed });
        game.Career.RecordDefeatedOpponent("B");

        Assert.Equal(2, VictoryConditionService.OpponentsToDominate(game));
    }

    [Fact]
    public void The_champion_has_the_most_wins_the_bosses_aside()
    {
        var game = GameState.CreateNew("P");
        game.Racers = new RacerCollection();
        game.Player.Stats.Wins = 5;
        game.Racers.AddRacer(new Opponent("Boss", 50, Gender.Male, 99, 50) { Status = RacerStatus.ReadyToRace, IsBoss = true, BossPath = "Crown" });
        game.Racers.ReadyToRace["Boss"].Stats.Wins = 100;
        game.Racers.AddRacer(new Opponent("Gone", 30, Gender.Male, 50, 50) { Status = RacerStatus.Departed });

        Assert.True(VictoryConditionService.IsPlayerSeasonChampion(game));

        game.Racers.Departed["Gone"].Stats.Wins = 6;
        Assert.False(VictoryConditionService.IsPlayerSeasonChampion(game));
    }

    [Fact]
    public void An_event_instance_is_its_stored_state_and_only_the_state_is_saved()
    {
        using var temp = new TempDir();
        using var played = TestPlaces.Play(TestCareer.Mods(temp));
        var career = CareerState.CreateNew();
        career.SetCounter(MilestoneTrigger.ReputationReached, 100);
        var events = new RaceEventService(new CarFilterService());
        var start = new DateTime(1970, 6, 1, 9, 0, 0);

        List<RaceEventInstance> made = [];
        for (var tries = 0; made.Count == 0 && tries < 50; tries++) made = events.GenerateEvents(career, start);
        Assert.NotEmpty(made);

        // What the career keeps is the plain state, whatever the game showed of it
        Assert.All(career.ActiveEvents, e => Assert.Equal(typeof(RaceEventState), e.GetType()));
        var document = SaveMapper.Create().ToDocument(career);
        var stored = document["ActiveEvents"].AsArray[0].AsDocument;
        Assert.False(stored.ContainsKey("_type"));
        Assert.False(stored.ContainsKey("TrackId"));
        Assert.False(stored.ContainsKey("OpponentName"));

        // And it reads back into instances with what the definition gives them
        var reloaded = SaveMapper.Create().ToObject<CareerState>(document);
        var shown = events.GetActiveEvents(reloaded, start).ToList();
        var first = made[0];
        var again = Assert.Single(shown, e => e.InstanceId == first.InstanceId);
        Assert.Equal(first.EventDefinitionId, again.EventDefinitionId);
        Assert.Equal(events.GetEventDefinition(first.EventDefinitionId)!.TrackId, again.TrackId);
        Assert.Equal(first.ExpiresAt, again.ExpiresAt);
    }
}
