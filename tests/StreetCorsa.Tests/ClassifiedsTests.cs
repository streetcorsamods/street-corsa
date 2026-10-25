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
[Collection(GlobalsCollection.Name)]
public class ClassifiedsTests
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsRoots));

    private static PartDefinition FirstOf(string className) =>
        Catalog.Value.Parts.Values.OrderBy(p => p.Id, StringComparer.Ordinal).First(p => p.IsRealPart && PartKinds.Is(p, className));

    // ----- the facts -----

    [Fact]
    public void A_cars_blower_stands_out_first_and_a_stock_cars_cam_is_no_news()
    {
        var blower = FirstOf(PartClasses.Supercharger);
        var cam = FirstOf(PartClasses.Camshaft);
        var block = new PartInstance(FirstOf(PartClasses.EngineBlock).Id) { Children = [new PartInstance(cam.Id), new PartInstance(blower.Id)] };

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
        var blockId = FirstOf(PartClasses.EngineBlock).Id;
        var engine = new PartAd(new PartInstance(blockId) { Children = [new PartInstance(FirstOf(PartClasses.Camshaft).Id)] }, 900m, "Ace") { SellerRival = "Ace" };
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
    public void A_car_that_shows_its_rust_or_a_cracked_windshield_is_never_called_mint_and_now_and_then_says_so()
    {
        var shiny = new CarAdFacts(null, Modified: false, Condition: 0.95, MileageKm: 20_000, [], new CarAdLooks(0, 0, 0, 0));
        var rusty = shiny with { Looks = new CarAdLooks(0.3, 0.1, 0.5, 0.2) };
        var mint = GameText.Installed.Lines("ads.car.mint");

        var shinyLines = Enumerable.Range(0, 200).Select(seed => AdBlurbs.Write(shiny, new Random(seed))).ToList();
        var rustyLines = Enumerable.Range(0, 200).Select(seed => AdBlurbs.Write(rusty, new Random(seed))).ToList();

        Assert.Contains(shinyLines, line => mint.Any(line.Contains));
        Assert.DoesNotContain(rustyLines, line => mint.Any(line.Contains));
        Assert.Contains(rustyLines, line => GameText.Installed.Lines("ads.car.rust").Any(line.Contains));
        Assert.Contains(rustyLines, line => GameText.Installed.Lines("ads.car.cracked_screen").Any(line.Contains));
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
        Assert.Equal("AMC Javelin SST 360", CarAdViewModel.ModelOf(new CarDefinition { Brand = "American Motor Company", Name = "AMC Javelin SST 360", Year = 1970 }));
        Assert.Equal("The Batmobile", CarAdViewModel.ModelOf(new CarDefinition { Brand = "Batmobile", Name = "The Batmobile", Year = 1966 }));
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
        // The events are a setting's (the core has none), one of them for Fords only
        using var temp = new TempDir();
        using var played = TestPlaces.Play(TestCareer.Mods(temp));
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
[Collection(GlobalsCollection.Name)]
public class AdWriterTests
{
    private sealed class Writer(Func<AdFacts, string, string?> write, bool takes = true) : IAdWriter
    {
        public int Asked { get; private set; }

        public bool CanImprove => true;

        /// <summary>Ads it has room for; one that takes none has room for none, and is never handed one</summary>
        public int Room { get; set; } = takes ? int.MaxValue : 0;

        public bool HasRoom => Room > 0;

        public event Action? RoomAgain;

        /// <summary>How the last ad taken is written back (or let go, told null)</summary>
        public Action<string?>? LastRewrite { get; private set; }

        public bool Improve(AdFacts facts, string written, Action<string?> rewrite)
        {
            Asked++;
            if (Room <= 0) return false;
            Room--;
            LastRewrite = rewrite;
            if (write(facts, written) is { } better) rewrite(better);
            return true;
        }

        public void SayRoomAgain() => RoomAgain?.Invoke();
    }

    [Fact]
    public void An_ad_the_writer_has_no_room_for_waits_and_is_handed_to_a_writer_with_room_when_the_paper_opens_again()
    {
        var listing = new UsedCarListing { CarDefinitionId = "test_car", Condition = 0.5f, Mileage = 100_000 };
        try
        {
            AdBlurbs.Forget();
            var full = new Writer((_, _) => "Never said.", takes: false);
            AdBlurbs.Writer = full;
            AdBlurbs.Describe(listing, null, null);
            // Not even asked: its facts are not worked out for a writer with no room
            Assert.Equal(0, full.Asked);
            Assert.False(listing.AiPolished);

            // The same session, the paper opened again: handed to a writer with room now
            var free = new Writer((_, _) => "Mean and green.");
            AdBlurbs.Writer = free;
            AdBlurbs.Polish(listing, null, () => null);
            Assert.Equal(1, free.Asked);
            Assert.Equal("Mean and green.", listing.Description);
            Assert.True(listing.AiPolished);
        }
        finally
        {
            AdBlurbs.Writer = null;
            AdBlurbs.Forget();
        }
    }

    [Fact]
    public void Ads_a_full_writer_turned_away_are_handed_over_in_their_order_as_it_says_it_has_room_again()
    {
        var first = new UsedCarListing { CarDefinitionId = "test_car", Condition = 0.5f, Mileage = 100_000 };
        var second = new UsedCarListing { CarDefinitionId = "test_car", Condition = 0.9f, Mileage = 10_000 };
        try
        {
            AdBlurbs.Forget();
            var writer = new Writer((_, written) => "AI: " + written) { Room = 0 };
            AdBlurbs.Writer = writer;
            AdBlurbs.Describe(first, null, null);
            AdBlurbs.Describe(second, null, null);
            Assert.Equal(0, writer.Asked);
            Assert.False(first.AiPolished);
            Assert.False(second.AiPolished);

            // Room for one: the first in line goes, the second keeps waiting; room again, and it goes too
            writer.Room = 1;
            writer.SayRoomAgain();
            Assert.Equal(1, writer.Asked);
            Assert.True(first.AiPolished);
            Assert.False(second.AiPolished);

            writer.Room = 1;
            writer.SayRoomAgain();
            Assert.Equal(2, writer.Asked);
            Assert.True(second.AiPolished);
            Assert.StartsWith("AI: ", second.Description);

            // Nothing left waiting: room again hands nothing over
            writer.Room = 1;
            writer.SayRoomAgain();
            Assert.Equal(2, writer.Asked);
        }
        finally
        {
            AdBlurbs.Writer = null;
            AdBlurbs.Forget();
        }
    }

    [Fact]
    public void An_ad_let_go_unwritten_waits_for_room_again_and_one_written_anew_meanwhile_is_not_handed_over_twice()
    {
        var listing = new UsedCarListing { CarDefinitionId = "test_car", Condition = 0.5f, Mileage = 100_000 };
        try
        {
            AdBlurbs.Forget();
            var writer = new Writer((_, _) => null);
            AdBlurbs.Writer = writer;
            AdBlurbs.Describe(listing, null, null);
            Assert.Equal(1, writer.Asked);

            // Let go unwritten (the AI switched off under the desk): handed over again as the paper opens with room there
            writer.LastRewrite!(null);
            Assert.False(listing.AiPolished);
            AdBlurbs.Polish(listing, null, () => null);
            Assert.Equal(2, writer.Asked);

            // Let go once more, but its words written anew meanwhile: the old wait is dropped, the ad as it is now goes once
            writer.LastRewrite!(null);
            listing.Description = "Other words.";
            AdBlurbs.Polish(listing, null, () => null);
            Assert.Equal(2, writer.Asked);
            writer.SayRoomAgain();
            Assert.Equal(2, writer.Asked);
        }
        finally
        {
            AdBlurbs.Writer = null;
            AdBlurbs.Forget();
        }
    }

    [Fact]
    public void A_better_writer_rewrites_an_ad_and_one_that_fails_leaves_the_games_words()
    {
        var listing = new UsedCarListing { CarDefinitionId = "test_car", Condition = 0.5f, Mileage = 100_000, EngineSummary = "Ford 390" };
        try
        {
            CarAdFacts? seen = null;
            var writer = new Writer((facts, written) =>
            {
                seen = facts as CarAdFacts;
                return "  Mean and green.  ";
            });
            AdBlurbs.Writer = writer;

            // Offered as it is written, and only the once: the paper's sweep does not ask again
            AdBlurbs.Describe(listing, null, null);
            Assert.Equal(1, writer.Asked);
            Assert.Equal("Mean and green.", listing.Description);
            Assert.True(listing.AiPolished);
            Assert.Equal("Ford 390", seen?.Engine);
            AdBlurbs.Polish(listing, null, () => null);
            Assert.Equal(1, writer.Asked);

            // One written while there was no writer is offered by the sweep
            AdBlurbs.Writer = null;
            var before = new UsedCarListing { CarDefinitionId = "test_car", Condition = 0.5f, Mileage = 100_000 };
            AdBlurbs.Describe(before, null, null);
            AdBlurbs.Writer = writer;
            AdBlurbs.Polish(before, null, () => null);
            Assert.Equal(2, writer.Asked);
            Assert.Equal("Mean and green.", before.Description);

            var other = new UsedCarListing { CarDefinitionId = "test_car", Condition = 0.5f, Mileage = 100_000 };
            AdBlurbs.Writer = new Writer((_, _) => throw new InvalidOperationException("offline"));
            AdBlurbs.Describe(other, null, null);
            var own = other.Description;
            Assert.False(string.IsNullOrWhiteSpace(other.Description));
            Assert.Equal(own, other.Description);
            Assert.False(other.AiPolished);
        }
        finally
        {
            AdBlurbs.Writer = null;
        }
    }
}

