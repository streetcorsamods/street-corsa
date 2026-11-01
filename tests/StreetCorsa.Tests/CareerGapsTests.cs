using System.Runtime.CompilerServices;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Career;
using StreetCorsa.Dialogs.Victory;
using StreetCorsa.Models.Career.Milestones;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Career;

namespace StreetCorsa.Tests;

/// <summary>
/// The career's loose ends: the events in the paper, the counters they read, the victory and the prize camshaft. The
/// events and the paths are a made-up setting's (<see cref="TestCareer"/>), played for each test.
/// </summary>
[Collection(GlobalsCollection.Name)]
public class CareerGapsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IDisposable _played;

    public CareerGapsTests() => _played = TestPlaces.Play(TestCareer.Mods(_temp));

    public void Dispose()
    {
        _played.Dispose();
        _temp.Dispose();
    }

    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsRoots));

    private static RaceEventService Events() => new(new CarFilterService());

    [Fact]
    public void A_new_career_sees_the_events_its_starting_reputation_opens()
    {
        var game = GameState.CreateNew("Tester");
        var events = Events();

        // Before the counters followed the player, reputation read 0 and hid every event that asked for any
        Assert.DoesNotContain(events.GetEligibleEvents(game.Career), e => e.Id == "known_names");

        game.Career.SyncStanding(game.Player);

        Assert.Equal(game.Player.Stats.Reputation, game.Career.GetCounter(MilestoneTrigger.ReputationReached));
        Assert.Contains(events.GetEligibleEvents(game.Career), e => e.Id == "known_names");
        Assert.Contains(events.GetEligibleEvents(game.Career), e => e.Id == "open_1");
    }

    [Fact]
    public void The_paper_never_has_more_than_five_invitations_open()
    {
        var game = GameState.CreateNew("Tester");
        game.Career.SyncStanding(game.Player);
        var events = Events();
        Assert.True(events.GetEligibleEvents(game.Career).Count() > RaceEventService.MaxOpenEvents);

        var day = game.Date;
        for (var i = 0; i < 30; i++, day = day.AddDays(1))
        {
            events.GenerateEvents(game.Career, day);
            Assert.True(game.Career.ActiveEvents.Count(e => e.IsAvailable(day)) <= RaceEventService.MaxOpenEvents);
        }
    }

    /// <summary>The reputation path reached (the counter at its target) and the boss beaten, both at once</summary>
    private static GameState TwoPathsReached()
    {
        var game = GameState.CreateNew("Tester");
        game.Career.SetCounter(MilestoneTrigger.ReputationReached, 100);
        game.Career.SetCounter(MilestoneTrigger.TotalWins, 50);
        game.Racers.AddRacer(new Opponent("Big Al", 41, Gender.Male, 100, 65) { DefinitionId = "big_al", IsBoss = true, BossPath = "Crown" });
        Services.Opponents.Bosses.Refresh(game);
        game.Career.RecordDefeatedOpponent("Big Al");
        return game;
    }

    [Fact]
    public void Only_the_chosen_path_wins()
    {
        var victories = new VictoryConditionService();
        var game = TwoPathsReached();
        var crown = victories.GetVictoryCondition("Crown")!;
        Assert.True(crown.IsAchieved(game.Career));

        game.Career.ActiveVictoryType = "Collector";
        Assert.Null(victories.CheckForVictory(game));

        game.Career.ActiveVictoryType = "Crown";
        Assert.Equal("Crown", victories.CheckForVictory(game)!.VictoryType);
    }

    [Fact]
    public void With_no_path_chosen_the_first_reached_wins()
    {
        var victories = new VictoryConditionService();
        var game = TwoPathsReached();

        Assert.NotNull(victories.CheckForVictory(game));
    }

    private static CareerDialogViewModel Career(DialogService dialogs, GameState game, StrongBox<CareerSection> lastSection) =>
        new(dialogs, new VictoryConditionService(), new MilestoneService(), game, null!, lastSection, () => { });

    [Fact]
    public void Pursuing_a_path_already_reached_shows_the_victory_ahead_of_the_dialogs_waiting()
    {
        var game = TwoPathsReached();
        var dialogs = new DialogService();
        var career = Career(dialogs, game, new StrongBox<CareerSection>());
        var waiting = new FakeDialog();
        dialogs.ShowDialog(career);
        dialogs.ShowDialog(waiting);

        career.SetActiveVictoryCommand.Execute("Crown");

        Assert.True(game.Career.HasWonGame);
        Assert.IsType<VictoryDialogViewModel>(dialogs.CurrentDialog);
        dialogs.CloseDialog();
        Assert.Same(waiting, dialogs.CurrentDialog);
        dialogs.CloseDialog();
        Assert.False(dialogs.IsDialogOpen);
    }

    [Fact]
    public void The_career_opens_on_the_section_last_looked_at_in_the_same_game()
    {
        var game = TwoPathsReached();
        var dialogs = new DialogService();
        var lastSection = new StrongBox<CareerSection>();

        Career(dialogs, game, lastSection).ShowSectionCommand.Execute("Milestones");

        Assert.True(Career(dialogs, game, lastSection).ShowMilestones);
        Assert.True(Career(dialogs, game, new StrongBox<CareerSection>()).ShowVictory);
    }

    [Fact]
    public void A_victory_is_claimed_once_and_the_game_goes_on()
    {
        var victories = new VictoryConditionService();
        var game = TwoPathsReached();
        game.Career.ActiveVictoryType = "Crown";

        var won = victories.ClaimVictory(game);

        Assert.Equal("Crown", won!.VictoryType);
        Assert.True(game.Career.HasWonGame);
        Assert.Equal("Crown", game.Career.WinningVictoryType);
        Assert.Null(victories.ClaimVictory(game));
    }

    [Fact]
    public void The_victory_message_is_shown_as_the_victory_screen()
    {
        var victories = new VictoryConditionService();
        var game = TwoPathsReached();
        var crown = victories.GetVictoryCondition("Crown")!;

        var message = CareerProgressService.VictoryMessage(game, crown, [], []);

        Assert.NotNull(message.Victory);
        Assert.Equal(crown.Name, message.Victory!.VictoryName);
        Assert.Contains(message.Victory.Stats, s => s.Label == "Reputation");
    }

    [Fact]
    public void The_prize_camshaft_fits_the_engine_and_beats_its_own()
    {
        var catalog = Catalog.Value;
        var build = EngineBuildIndex.Create(catalog).Runnable
            .Single(b => b.Build.Id == "rated/test-v8");
        var engine = EngineFactory.CreateStock(catalog, build, 0.8, new Random(1))!.Root;
        var current = engine.SelfAndDescendants()
            .Select(p => catalog.Get(p.DefinitionId))
            .First(d => d != null && PartKinds.GroupOf(d) == PrizeParts.CamshaftGroup)!;

        var prize = PrizeParts.BestCamshaft(catalog, engine);

        Assert.NotNull(prize);
        var (cam, count) = prize!.Value;
        Assert.Equal(PrizeParts.CamshaftGroup, PartKinds.GroupOf(cam));
        Assert.True(PartPricing.NewPrice(cam) > PartPricing.NewPrice(current));
        Assert.Equal(1, count);
    }

    [Fact]
    public void No_engine_no_prize_camshaft()
    {
        Assert.Null(PrizeParts.BestCamshaft(Catalog.Value, null));
    }
}
