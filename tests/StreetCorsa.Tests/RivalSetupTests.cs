using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;

namespace StreetCorsa.Tests;

/// <summary>
/// The rivals' setups (chassis tuning, "Later"): each row moved where a setup puts it within what the car's parts let it
/// move, the engine's specs to their best on the dyno, and the rules' setups checked as a mod writes them
/// </summary>
public sealed class RivalSetupTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private PartsCatalog Catalog()
    {
        _temp.Parts("parts/rival/test", """
            [
                { "id": "rival/test/block", "specs": { "idle_rpm": 800, "adjust": { "idle_rpm": { "min": 600, "max": 1200, "step": 50 } } } },
                { "id": "rival/test/coilover", "specs": { "damping": 2000, "adjust": { "damping": { "min": 1000, "max": 4000, "step": 250 } } } },
                { "id": "rival/test/coil_kit", "specs": { "adjust": { "ride_height_front": { "min": -40, "max": 10, "step": 5 } } } }
              ]
            """);
        return PartsCatalog.Load(_temp.Combine("parts"));
    }

    private static Car CarWith(params PartInstance[] parts) => new() { DefinitionId = "test_car", Parts = parts.ToList(), HasPartsAssigned = true };

    private static PartInstance On(string id, int slot) => new(id) { ParentSlot = slot };

    private static List<TuneRow> Rows(Car car, PartsCatalog catalog) =>
        CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()));

    [Fact]
    public void A_setup_puts_each_row_it_names_where_it_says_and_leaves_the_rest()
    {
        var catalog = Catalog();
        var car = CarWith(On("rival/test/coilover", 200), On("rival/test/coil_kit", 201));
        var setup = new Dictionary<string, string>
        {
            [CarSetup.RideHeightFront] = RivalSetup.Min,
            ["damping"] = RivalSetup.Max,
            [CarSetup.PressureRear] = RivalSetup.Mid,
            // Nothing on the car opens the steering lock: no row, nothing set
            [CarSetup.SteerLock] = RivalSetup.Max
        };

        var wanted = RivalSetup.Wanted(car, Rows(car, catalog), setup, _ => null).ToDictionary(w => w.Row.Item, w => w.Value);

        Assert.Equal(-40, wanted[CarSetup.RideHeightFront]);
        Assert.Equal(4000, wanted["damping"]);
        Assert.Equal(0, wanted[CarSetup.PressureRear]);
        Assert.False(wanted.ContainsKey(CarSetup.SteerLock));
        Assert.False(wanted.ContainsKey(CarSetup.CamberFront));

        // The pressure's middle is where it stood: two rows change
        Assert.Equal(2, CarTune.Apply(car, RivalSetup.Wanted(car, Rows(car, catalog), setup, _ => null)));
        Assert.Equal(-40, car.Setup[CarSetup.RideHeightFront]);
        Assert.Equal(4000, car.Parts[0].Tuning["damping"]);
    }

    [Fact]
    public void A_set_up_car_shows_lowered_and_tuned_and_a_stock_one_shows_nothing()
    {
        var catalog = Catalog();
        var ranges = new TuningRules().Ranges();
        var car = CarWith(On("rival/test/coilover", 200), On("rival/test/coil_kit", 201), On("rival/test/block", PartInstance.CarEngineSlot));

        Assert.False(SetupLook.Of(car, catalog, ranges).Shows);

        car.Setup[CarSetup.RideHeightFront] = -20;
        car.Parts[2].Tuning["idle_rpm"] = 1000;
        var look = SetupLook.Of(car, catalog, ranges);

        Assert.True(look.Lowered);
        Assert.True(look.EngineTuned);
        Assert.False(look.AngleKit);
        Assert.Equal(2, look.Changed);
    }

    [Fact]
    public void A_part_that_drops_the_car_shows_it_lowered_and_its_ad_may_say_so()
    {
        _temp.Parts("parts/rival/test", """
            [ { "id": "rival/test/blocks", "specs": { "drop": 50 } } ]
            """);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        var car = CarWith(On("rival/test/blocks", 201));

        Assert.True(SetupLook.Of(car, catalog, new TuningRules().Ranges()).Lowered);

        var facts = StreetCorsa.Services.Market.AdBlurbs.FactsOf(car.Parts, catalog, null, null, modified: true, 0.6, 90_000);
        Assert.True(facts.Lowered);
        var lowered = Services.Text.GameText.Installed.Lines("ads.car.lowered");
        Assert.Contains(Enumerable.Range(0, 100).Select(seed => StreetCorsa.Services.Market.AdBlurbs.Write(facts, new Random(seed))),
            line => lowered.Any(line.Contains));
    }

    [Fact]
    public void Engine_power_sets_the_engine_specs_where_the_dyno_makes_the_most()
    {
        var catalog = Catalog();
        var car = CarWith(On("rival/test/block", PartInstance.CarEngineSlot));

        // A dyno that peaks at 950 rpm idle (made up: only the search is under test)
        double? Dyno(PartInstance engine)
        {
            var idle = engine.Tuning.TryGetValue("idle_rpm", out var set) ? set : 800;
            return 300 - Math.Abs(idle - 950) / 10;
        }

        var wanted = RivalSetup.Wanted(car, Rows(car, catalog), new Dictionary<string, string> { [RivalSetup.Engine] = RivalSetup.Power }, Dyno);

        var idle = Assert.Single(wanted, w => w.Row.Item == "idle_rpm");
        Assert.Equal(950, idle.Value);
    }

    [Fact]
    public void An_engine_that_never_does_better_keeps_its_specs()
    {
        var catalog = Catalog();
        var car = CarWith(On("rival/test/block", PartInstance.CarEngineSlot));

        var wanted = RivalSetup.Wanted(car, Rows(car, catalog), new Dictionary<string, string> { [RivalSetup.Engine] = RivalSetup.Power }, _ => 200);

        Assert.Equal(800, Assert.Single(wanted, w => w.Row.Item == "idle_rpm").Value);
    }

    [Fact]
    public void The_rules_setups_drop_a_word_they_do_not_know_and_tell_a_default_no_setup_has()
    {
        var rules = new TuningRules();
        rules.Rivals.Setup = "rally";
        rules.Rivals.Setups["mine"] = new Dictionary<string, string> { ["camber_front"] = "lots", ["engine"] = "max", ["damping"] = "mid" };
        var problems = new List<string>();

        rules.Check("tuning", problems);

        Assert.Equal(["damping"], rules.Rivals.Setups["mine"].Keys);
        Assert.Contains(problems, p => p.Contains("camber_front"));
        Assert.Contains(problems, p => p.Contains("engine"));
        Assert.Contains(problems, p => p.Contains("rally"));
        Assert.Null(rules.Rivals.Get("rally"));
        // The core's street setup names nothing: a racer by it leaves the car as it is
        Assert.Null(new TuningRules().Rivals.Get("street"));
        Assert.NotNull(new TuningRules().Rivals.Get("drift"));
    }

    [Fact]
    public void A_setting_that_names_only_its_default_keeps_the_core_setups_and_the_base_rules_read_clean()
    {
        var merged = System.Text.Json.Nodes.JsonNode.Parse("""
            { "tuning": { "rivals": { "setup": "drag", "setups": { "drift": { "steer_lock": "mid" } } } } }
            """)!.AsObject();
        var problems = new List<string>();

        var book = RuleBook.Read(merged, problems);

        Assert.Empty(problems);
        Assert.Equal("drag", book.Tuning.Rivals.Setup);
        Assert.NotNull(book.Tuning.Rivals.Get("grip"));
        Assert.Equal(RivalSetup.Mid, book.Tuning.Rivals.Get("drift")![CarSetup.SteerLock]);
        // A setup the mod names is its own whole: the core's drift items are not kept under it
        Assert.Single(book.Tuning.Rivals.Get("drift")!);

        // The core's own tuning.json says what the code says
        var core = new List<string>();
        var read = RuleBook.Read(ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, core), core);
        Assert.Empty(core);
        var fresh = new TuningRules().Rivals;
        Assert.Equal(fresh.Setups.Keys.Order(), read.Tuning.Rivals.Setups.Keys.Order());
        foreach (var (id, setup) in fresh.Setups) Assert.Equal(setup.OrderBy(i => i.Key), read.Tuning.Rivals.Setups[id].OrderBy(i => i.Key));
    }
}
