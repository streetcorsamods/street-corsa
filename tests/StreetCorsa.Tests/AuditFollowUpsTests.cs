using StreetCorsa.Models.Career.Filters;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Diner;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Simulation;

namespace StreetCorsa.Tests;

/// <summary>The 2026-09-29 audit's follow-ups across the slices: one horsepower, one car copy, the part walk</summary>
public class AuditFollowUpsTests
{
    private static CarDefinition Rated(string? bhp) => new() { Specs = new CarSpecsData { Bhp = bhp, Weight = "1,500 kg" } };

    // ---- one horsepower ----

    [Fact]
    public void The_dyno_beats_the_spec_sheet()
    {
        Assert.Equal(410, CarPower.Of(new Car("a") { PowerHp = 410 }, Rated("250 bhp")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_car_off_the_dyno_goes_by_its_spec_sheet(double? dyno)
    {
        Assert.Equal(250, CarPower.Of(new Car("a") { PowerHp = dyno }, Rated("250hp @ 5000rpm")));
    }

    [Fact]
    public void A_car_that_does_not_run_passes_no_power_filter()
    {
        var filter = new StreetCorsa.Models.Career.Filters.PowerFilter(minHP: 200, maxHP: null);
        Assert.False(filter.Matches(Rated("250 bhp"), new Car("a") { PowerHp = 0 }));
        Assert.True(filter.Matches(Rated("250 bhp"), new Car("a")));
        Assert.Equal(0, CarPower.Racing(new Car("a") { PowerHp = 0 }, Rated("250 bhp")));
    }

    [Fact]
    public void Without_either_there_is_no_power()
    {
        Assert.Null(CarPower.Of((Car?)null, null));
        Assert.Null(CarPower.Of(new Car("a"), Rated(null)));
    }

    [Fact]
    public void A_power_limit_judges_the_car_by_its_engine()
    {
        var filter = new PowerFilter(minHP: null, maxHP: 300);
        var definition = Rated("250 bhp");

        Assert.True(filter.Matches(definition));
        Assert.False(filter.Matches(definition, new Car("a") { PowerHp = 420 }));
        Assert.True(new PowerFilter(400, null).Matches(Rated(null), new Car("a") { PowerHp = 420 }));
    }

    [Fact]
    public void The_simulator_keeps_a_dead_engine_at_zero_and_takes_the_spec_before_the_price()
    {
        Assert.Equal(0, RaceSimulatorService.GetCarHP(new Car("a") { PowerHp = 0 }, Rated("250 bhp")));
        Assert.Equal(250, RaceSimulatorService.GetCarHP(new Car("a") { PurchasePrice = 1000m }, Rated("250 bhp")));
        Assert.Equal(120, RaceSimulatorService.GetCarHP(new Car("a") { PurchasePrice = 1000m }, Rated(null)));
    }

    [Fact]
    public void The_diner_card_and_the_matchup_agree()
    {
        var definition = Rated("250 bhp");
        var opponent = new Opponent { Name = "Rival" };
        opponent.Cars.Add(new Car("a") { PowerHp = 410 });
        var card = new OpponentDisplayViewModel { Opponent = opponent, CarDefinition = definition };

        var matchup = MatchupCalculator.Compare(definition, definition, null, opponent.Cars[0].PowerHp);

        Assert.Equal("410 hp", card.CarPower);
        Assert.Contains(matchup, s => s.Label == "Horsepower" && s.OpponentValue == "410 hp");
    }

    [Fact]
    public void A_dial_in_estimate_uses_the_dyno()
    {
        var definition = Rated("150 bhp");
        Assert.True(BracketRules.EstimateFor(new Car("a") { PowerHp = 450 }, definition)
                    < BracketRules.EstimateFor(new Car("a"), definition));
    }

    // ---- one car copy ----

    [Fact]
    public void A_snapshot_copies_the_car_and_its_parts()
    {
        var engine = new PartInstance("block") { ParentSlot = PartInstance.CarEngineSlot };
        engine.Children.Add(new PartInstance("head"));
        var car = new Car("a") { FuelLitres = 20, OilLevel = 0.8, BodyDirt = 0.4, PowerHp = 300, Parts = [engine] };

        var copy = car.Snapshot();

        Assert.Equal(car.InstanceId, copy.InstanceId);
        Assert.Equal(20, copy.FuelLitres);
        Assert.Equal(0.8, copy.OilLevel);
        Assert.Equal(0.4, copy.BodyDirt);
        Assert.Equal(300, copy.PowerHp);
        Assert.NotSame(engine, copy.Parts[0]);
        Assert.Equal(engine.InstanceId, copy.Parts[0].InstanceId);
        Assert.Equal(engine.Children[0].InstanceId, copy.Parts[0].Children[0].InstanceId);
    }

    // ---- the part walk ----

    [Fact]
    public void A_part_walk_goes_in_mounting_order()
    {
        var block = new PartInstance("block");
        var head = new PartInstance("head");
        head.Children.Add(new PartInstance("intake"));
        block.Children.Add(head);
        block.Children.Add(new PartInstance("pan"));

        Assert.Equal(["block", "head", "intake", "pan"], block.SelfAndDescendants().Select(p => p.DefinitionId));
    }

    [Fact]
    public void A_part_walk_stops_at_the_depth_cap_and_skips_what_a_save_left_null()
    {
        var root = new PartInstance("0");
        var part = root;
        for (var i = 1; i <= 10_000; i++)
        {
            var next = new PartInstance(i.ToString());
            part.Children.Add(next);
            part = next;
        }

        root.Children.Add(null!);

        Assert.Equal(PartInstance.MaxDepth + 1, root.SelfAndDescendants().Count());
        Assert.Equal(PartTrees.MaxDepth, PartInstance.MaxDepth);
        Assert.Single(new PartInstance("loose") { Children = null! }.SelfAndDescendants());
    }
}
