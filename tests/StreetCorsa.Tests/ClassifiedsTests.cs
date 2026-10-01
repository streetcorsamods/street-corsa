using StreetCorsa.Dialogs.RaceHub;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Screens.Newspaper;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// Step 25's racing hub and classifieds: the ads' few words (<see cref="AdBlurbs"/>), the paper a page at a time
/// (<see cref="AdPages{T}"/>), and the invitations the hub lists (<see cref="RaceInvitations"/>)
/// </summary>
public class ClassifiedsTests
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));

    private static PartDefinition FirstOf(string className) =>
        Catalog.Value.Parts.Values.OrderBy(p => p.Id, StringComparer.Ordinal).First(p => p.IsScripted && PartKinds.Is(p, className));

    // ----- the facts -----

    [Fact]
    public void A_cars_blower_stands_out_first_and_a_stock_cars_cam_is_no_news()
    {
        var blower = FirstOf("SuperCharger");
        var cam = FirstOf("Camshaft");
        var block = new PartInstance(FirstOf("Block").Id) { Children = [new PartInstance(cam.Id), new PartInstance(blower.Id)] };

        var worked = AdBlurbs.FactsOf([block], Catalog.Value, null, "GM 327, 300 hp", modified: true, 0.7, 80_000);
        Assert.Equal(["blower", "cam"], worked.Standouts.Select(s => s.Kind));
        Assert.Equal(AdBlurbs.NameOf(blower), worked.Standouts[0].Part);

        // As it left the factory: a blower is still worth a word, the cam is the one it came with
        var stock = AdBlurbs.FactsOf([block], Catalog.Value, null, "GM 327, 300 hp", modified: false, 0.7, 80_000);
        Assert.Equal(["blower"], stock.Standouts.Select(s => s.Kind));

        // Worked on, but the cam is the factory's: no news either
        var factoryCam = AdBlurbs.FactsOf([block], Catalog.Value, new HashSet<string> { cam.Id, blower.Id }, "GM 327", modified: true, 0.7, 80_000);
        Assert.Equal(["blower"], factoryCam.Standouts.Select(s => s.Kind));

        // The block's notes in brackets stay out of the ad
        Assert.Equal("Chevrolet 235 inline six block, 128 hp",
            AdBlurbs.FactsOf([], null, null, "Chevrolet 235 inline six block (3-9/16 in bore, stand-in casting), 128 hp", false, 0.7, 1).Engine);

        // No catalog, no parts to name
        Assert.Empty(AdBlurbs.FactsOf([block], null, null, null, modified: true, 0.7, 80_000).Standouts);
    }

    [Fact]
    public void A_part_ad_knows_a_whole_engine_from_a_bare_block_and_a_racers_spare()
    {
        var blockId = FirstOf("Block").Id;
        var engine = new PartAd(new PartInstance(blockId) { Children = [new PartInstance(FirstOf("Camshaft").Id)] }, 900m, "Ace") { SellerRival = "Ace" };
        var bare = new PartAd(new PartInstance(blockId) { Wear = 0.3 }, 200m, "Joe");

        var engineFacts = AdBlurbs.FactsOf(engine, Catalog.Value)!;
        Assert.True(engineFacts.CompleteEngine);
        Assert.True(engineFacts.FromRacer);

        var bareFacts = AdBlurbs.FactsOf(bare, Catalog.Value)!;
        Assert.False(bareFacts.CompleteEngine);
        Assert.False(bareFacts.FromRacer);
        Assert.Equal(0.3, bareFacts.Wear);

        Assert.Null(AdBlurbs.FactsOf(new PartAd(new PartInstance("no_such_part"), 10m, "Joe"), Catalog.Value));
    }

    [Fact]
    public void An_ad_names_a_part_without_the_catalogs_notes_in_brackets()
    {
        Assert.Equal("Holley 4160 4-bbl carburetor",
            AdBlurbs.NameOf(new PartDefinition { Id = "x", Name = "Holley 4160 4-bbl carburetor (stand-in model: Holley 4160) (L79)" }));
        Assert.Equal("(Unnamed)", AdBlurbs.NameOf(new PartDefinition { Id = "y", Name = "(Unnamed)" }));
    }

    // ----- the words -----

    [Fact]
    public void Every_pool_the_writer_draws_from_has_lines_in_the_base_game()
    {
        var text = GameText.Load(Mods.ModSet.Load(RepoPaths.Content));
        var pools = AdBlurbs.StandoutKinds.Select(k => "ads.car." + k)
            .Concat(["ads.car.built", "ads.car.engine", "ads.car.original", "ads.car.mint", "ads.car.rough", "ads.car.low_miles", "ads.car.character"])
            .Concat(["ads.part.engine", "ads.part.speed", "ads.part.fresh", "ads.part.worn", "ads.part.racer", "ads.part.character"]);

        Assert.All(pools, pool => Assert.NotEmpty(text.Lines(pool)));
    }

    [Fact]
    public void Car_ads_vary_and_a_blown_car_often_names_its_blower()
    {
        var facts = new CarAdFacts("GM 327, 340 hp", Modified: true, Condition: 0.6, MileageKm: 90_000, [new AdStandout("blower", "Jimmy 6-71 Blower")]);

        var lines = Enumerable.Range(0, 200).Select(seed => AdBlurbs.Write(facts, new Random(seed))).ToList();

        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));
        Assert.DoesNotContain(lines, line => line.Contains('{'));
        Assert.True(lines.Distinct().Count() >= 8, "not every ad the same");
        Assert.InRange(lines.Count(line => line.Contains("Jimmy 6-71 Blower")), 60, 200);
    }

    [Fact]
    public void A_car_without_an_engine_to_name_never_leaves_a_hole_for_it()
    {
        var facts = new CarAdFacts(null, Modified: false, Condition: 0.95, MileageKm: 20_000, []);

        var lines = Enumerable.Range(0, 200).Select(seed => AdBlurbs.Write(facts, new Random(seed))).ToList();

        Assert.DoesNotContain(lines, line => line.Contains('{') || line.Trim().Length == 0);
        Assert.True(lines.Distinct().Count() >= 6);
    }

    [Fact]
    public void A_whole_engine_for_sale_mostly_says_so()
    {
        var facts = new PartAdFacts("Rocket 371", "Engine blocks", CompleteEngine: true, SpeedPart: false, Wear: 0.6, FromRacer: false);

        var lines = Enumerable.Range(0, 100).Select(seed => AdBlurbs.Write(facts, new Random(seed))).ToList();

        Assert.True(lines.Count(line => line.Contains("Rocket 371")) > 40);
        Assert.DoesNotContain(lines, line => line.Contains('{'));
    }

    // ----- onto the ads -----

    [Fact]
    public void An_old_ad_gets_its_words_when_first_shown_and_keeps_them()
    {
        var listing = new UsedCarListing { CarDefinitionId = "test_car", Condition = 0.5f, Mileage = 100_000 };

        Assert.True(AdBlurbs.Ensure(listing, null, () => null));
        var written = listing.Description;
        Assert.False(string.IsNullOrWhiteSpace(written));

        Assert.False(AdBlurbs.Ensure(listing, null, () => throw new InvalidOperationException("written already: not looked up")));
        Assert.Equal(written, listing.Description);
    }

    [Fact]
    public void A_car_ad_names_the_make_and_model_once_and_leaves_the_year_to_the_front()
    {
        Assert.Equal("Chevrolet Camaro Z/28", CarAdViewModel.ModelOf(new CarDefinition { Brand = "Chevrolet", Name = "Chevrolet Camaro 1969 Z/28", Year = 1969 }));
        Assert.Equal("Plymouth Fury", CarAdViewModel.ModelOf(new CarDefinition { Brand = "Plymouth", Name = "Fury", Year = 1959 }));
        Assert.Equal("1969", CarAdViewModel.ModelOf(new CarDefinition { Brand = "", Name = "1969", Year = 1969 }));
    }

    // ----- the paper a page at a time -----

    [Fact]
    public void The_paper_shows_a_page_of_ads_and_another_each_time_the_end_is_reached()
    {
        var pages = new AdPages<string>(30);
        pages.Fill(Enumerable.Range(0, 70).Select(i => $"ad {i}"));

        Assert.Equal(30, pages.Shown.Count);
        Assert.True(pages.HasMore);
        Assert.True(pages.ShowMore());
        Assert.Equal(60, pages.Shown.Count);
        Assert.True(pages.ShowMore());
        Assert.Equal(70, pages.Shown.Count);
        Assert.False(pages.ShowMore());
        Assert.Equal("ad 69", pages.Shown[^1]);
    }

    [Fact]
    public void An_ad_bought_leaves_the_rest_where_they_were()
    {
        var pages = new AdPages<string>(10);
        var ads = Enumerable.Range(0, 25).Select(i => $"ad {i}").ToList();
        pages.Fill(ads);

        pages.Remove(ads[3]);

        Assert.Equal(9, pages.Shown.Count);
        Assert.Equal(24, pages.Count);
        Assert.DoesNotContain(ads[3], pages.Shown);

        // The next page picks up where the shown ones end, none skipped or doubled
        pages.ShowMore();
        Assert.Equal(ads.Where((_, i) => i != 3).Take(19), pages.Shown);
    }

    // ----- the racing hub -----

    [Fact]
    public void The_hub_lists_every_open_event_the_ones_a_car_can_enter_first()
    {
        var game = GameState.CreateNew("Tester");
        game.Career.SyncStanding(game.Player);
        var events = new RaceEventService(new CarFilterService());
        for (var day = game.Date; day < game.Date.AddDays(6); day = day.AddDays(1)) events.GenerateEvents(game.Career, day);

        var catalog = new MemoryCatalog();
        catalog.UpsertCar(new CarDefinition { Id = "test_car", Brand = "Chevrolet", Name = "Bel Air", Year = 1957, Status = ContentStatus.Active });
        game.Player.Cars.Add(new Car("test_car"));

        var invitations = RaceInvitations.Load(game, events, new CarFilterService(), catalog);

        Assert.Equal(events.GetActiveEvents(game.Career, game.Date).Count(), invitations.Count);
        Assert.NotEmpty(invitations);
        var firstClosed = invitations.FindIndex(i => !i.HasEligibleCars);
        if (firstClosed >= 0) Assert.DoesNotContain(invitations.Skip(firstClosed), i => i.HasEligibleCars);
        Assert.All(invitations.Where(i => i.HasEligibleCars), i => Assert.Equal("1957 Chevrolet Bel Air", Assert.Single(i.EligibleCars).DisplayName));
    }
}

/// <summary>The better writer's seam: alone, since it is the whole app's</summary>
[Collection(AdWriterCollection.Name)]
public class AdWriterTests
{
    private sealed class Writer(Func<AdFacts, string, string?> write) : IAdWriter
    {
        public void Improve(AdFacts facts, string written, Action<string> rewrite)
        {
            if (write(facts, written) is { } better) rewrite(better);
        }
    }

    [Fact]
    public void A_better_writer_rewrites_an_ad_and_one_that_fails_leaves_the_games_words()
    {
        var listing = new UsedCarListing { CarDefinitionId = "test_car", Condition = 0.5f, Mileage = 100_000, EngineSummary = "Ford 390" };
        try
        {
            CarAdFacts? seen = null;
            AdBlurbs.Writer = new Writer((facts, written) =>
            {
                seen = facts as CarAdFacts;
                return "  Mean and green.  ";
            });
            AdBlurbs.Describe(listing, null, null);
            Assert.Equal("Mean and green.", listing.Description);
            Assert.Equal("Ford 390", seen?.Engine);

            AdBlurbs.Writer = new Writer((_, _) => throw new InvalidOperationException("offline"));
            AdBlurbs.Describe(listing, null, null);
            Assert.False(string.IsNullOrWhiteSpace(listing.Description));
            Assert.NotEqual("Mean and green.", listing.Description);
        }
        finally
        {
            AdBlurbs.Writer = null;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AdWriterCollection
{
    public const string Name = "Ad writer";
}
