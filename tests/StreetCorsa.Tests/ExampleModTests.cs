using StreetCorsa.Models.Career.Filters;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// The example mod in the repo (examples/mods/small_imports) loaded over the base game through the game's own loaders:
/// everything it has reads, and its engines of figures alone run near what the real ones made
/// </summary>
public class ExampleModTests
{
    private const string ModId = "small_imports";

    private static readonly Lazy<ModSet> Set = new(() =>
        ModSet.Load(RepoPaths.Content, alsoFrom: [Path.Combine(RepoPaths.Root, "examples", "mods", ModId)]));

    private static ModSet Mods => Set.Value;

    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(Mods.Folders(ContentTypes.Parts).ToList()));

    private static string Id(string local) => ModIds.Qualify(ModId, local);

    [Fact]
    public void The_mod_loads_after_the_base_game_without_a_problem()
    {
        Assert.Contains(Mods.Mods, m => m.Id == ModId);
        Assert.Empty(Mods.Problems);
        Assert.DoesNotContain(Catalog.Value.Problems, p => p.Contains(ModId, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_part_of_the_game_and_the_mod_is_a_file_of_its_own_and_of_a_known_class()
    {
        Assert.DoesNotContain(Catalog.Value.Problems, p => p.Contains(PartFiles.OlderPackFileName, StringComparison.Ordinal)
                                                          || p.Contains("is in no " + PartClasses.FileName, StringComparison.Ordinal)
                                                          || p.Contains(SlotShifts.FileName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cars/abarth_595ss/stock", 32)]
    [InlineData("cars/alfa_gta/stock", 115)]
    [InlineData("cars/escort_rs1600/stock", 120)]
    [InlineData("cars/abarth_500/stock", 160)]
    public void Each_cars_engine_of_figures_runs_near_its_power(string build, double rated)
    {
        var report = Evaluate(Id(build));

        Assert.True(report.Runs, report.Problem);
        Assert.True(report.FromFigures);
        Assert.InRange(report.Dyno!.MaxPowerHp, rated * 0.85, rated * 1.15);
        Assert.NotEmpty(report.GearRatios);
    }

    [Theory]
    [InlineData("kits/abarth_695", "cars/abarth_595ss/stock")]
    [InlineData("kits/alfa_gta_corsa", "cars/alfa_gta/stock")]
    [InlineData("kits/bda_1800", "cars/escort_rs1600/stock")]
    [InlineData("kits/tjet_garrett", "cars/abarth_500/stock")]
    public void The_speed_parts_make_more(string kit, string stock)
    {
        var tuned = Evaluate(Id(kit));
        Assert.True(tuned.Runs, tuned.Problem);
        Assert.True(tuned.Dyno!.MaxPowerHp > Evaluate(Id(stock)).Dyno!.MaxPowerHp * 1.15);
    }

    [Fact]
    public void Its_parts_are_on_the_shelves_they_say_and_the_turbo_is_a_turbo_of_its_own()
    {
        var catalog = Catalog.Value;
        Assert.Equal("engine_blocks", PartKinds.GroupOf(catalog.Get(Id("engines/fiat_tjet/block"))!));
        Assert.Equal("camshafts", PartKinds.GroupOf(catalog.Get(Id("engines/ford_bda/cams_bd3"))!));
        Assert.Equal("chargers", PartKinds.GroupOf(catalog.Get(Id("engines/fiat_tjet/turbo_garrett"))!));
        Assert.Equal("fiat", catalog.Get(Id("engines/fiat_tjet/turbo_garrett"))!.Make);

        var report = Evaluate(Id("cars/abarth_500/stock"));
        Assert.False(report.Turbocharged);
        Assert.Equal(0.985, Assert.Single(report.Inputs!.Chargers).LagUp);
    }

    [Fact]
    public void Its_cars_have_their_engines_layouts_and_makes()
    {
        var book = CarBook.Load(Mods);
        Assert.Empty(book.Problems);

        var abarth = book.Cars["abarth500"];
        Assert.Equal(EngineLayout.FrontTransverse, abarth.EngineLayout);
        Assert.Equal("abarth", abarth.Make);
        Assert.NotNull(Catalog.Value.EngineBuilds.SingleOrDefault(b => b.Id == abarth.Engine));
        Assert.Equal(EngineLayout.Rear, book.Cars["ks_abarth_595ss"].EngineLayout);
        Assert.Equal("ford_uk", book.Makes["ford_uk"].Family);
        Assert.Equal("fiat", book.Makes["abarth"].Family);
    }

    [Fact]
    public void Its_autocross_is_a_format_and_import_night_an_event_of_it()
    {
        var formats = RaceFormats.Load(Mods, Path.Combine(RepoPaths.Root, "apps", "new-modes", "sc_race"));
        var autocross = formats.Get(Id("autocross"));
        Assert.NotNull(autocross);
        Assert.Equal("time_attack", autocross!.Module);
        Assert.Empty(formats.Problems);

        var career = CareerContent.Load(Mods);
        var night = career.Events[Id("import_night")];
        Assert.Equal(Id("autocross"), night.Format);
        Assert.NotNull(night.EntryRequirements);
        Assert.Equal(Id("engines/ford_bda/cams_bd3"), night.Reward.SpecialItem?.Replace("part:", ""));
    }

    [Fact]
    public void Its_rival_its_dealer_and_its_shop_are_in_the_game()
    {
        // He drives European cars, and the lot deals in them
        var racers = new OpponentRepository(Mods).LoadAllOpponents();
        var gino = racers.Single(r => r.DefinitionId == Id("gino"));
        Assert.Equal("European", Assert.IsType<OriginFilter>(gino.Drives).Origin);

        var dealers = new DealerCatalog(Mods);
        Assert.Equal("European", Assert.IsType<OriginFilter>(dealers.Get(Id("continental_imports"))!.Cars).Origin);

        var shops = new PartShopCatalog(Mods);
        var shop = shops.All.Single(s => s.Id == Id("euro_speed"));
        Assert.Contains("ford_uk", shop.Makes);
    }

    [Fact]
    public void Each_engine_is_pinned_to_the_sound_of_the_car_it_came_in()
    {
        var library = StreetCorsa.Parts.Sounds.SoundLibrary.Load(Mods.Folders(ContentTypes.Sounds).ToList(), id => Catalog.Value.Get(id) != null);

        Assert.DoesNotContain(library.Problems, p => p.Contains(ModId, StringComparison.Ordinal));
        Assert.Equal("car:abarth500", library.Overrides.Pins[Id("engines/fiat_tjet/block")]);
        Assert.Equal("car:ks_ford_escort_mk1", library.Overrides.Pins[Id("engines/ford_bda/block")]);
    }

    [Fact]
    public void Its_words_are_in_the_text()
    {
        var text = GameText.Load(Mods);
        Assert.Empty(text.Problems);
        Assert.True(text.Has("talk.pulled_up.gino"));
        Assert.True(text.Has("paper.race_type.autocross"));
    }

    private static EngineReport Evaluate(string build)
    {
        var catalog = Catalog.Value;
        var tree = PartTreeBuilder.BuildEngine(catalog, catalog.EngineBuilds.Single(b => b.Id == build))!;
        Assert.Empty(tree.Unplaced);
        Assert.Empty(tree.Missing);
        return EngineEvaluator.Evaluate(catalog, tree);
    }
}
