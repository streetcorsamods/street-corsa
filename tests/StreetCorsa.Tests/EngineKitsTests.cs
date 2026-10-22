using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;

namespace StreetCorsa.Tests;

/// <summary>Engine kits (roadmap step 40): the sizes, putting one together, an engine taken apart for one</summary>
public class EngineKitsTests : IDisposable
{
    private static readonly CatalogParts Parts = new();
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PartsCatalog Catalog => Parts.Catalog;

    private static RatedBuild Build(string idEnd) => Parts.Builds.Runnable.Single(b => b.Build.Id.EndsWith(idEnd, StringComparison.Ordinal));

    private static KitSize Size(string id) => Catalog.Kits.Get(id)!;

    private static string? KindOf(PartInstance part) => Catalog.WorkOrder.KindOf(Catalog.Get(part.DefinitionId)!);

    [Fact]
    public void The_base_game_has_three_sizes_smallest_first_and_a_long_block_holds_the_short_one()
    {
        Assert.Equal(["short_block", "long_block", "complete"], Catalog.Kits.Sizes.Select(s => s.Id));
        Assert.Same(Size("short_block"), Catalog.Kits.Smallest);
        Assert.Same(Size("complete"), Catalog.Kits.Complete);

        var longBlock = Catalog.Kits.KindsOf(Size("long_block"));
        Assert.Contains("crankshaft", longBlock);
        Assert.Contains("cylinder_head", longBlock);
        Assert.DoesNotContain("carburettor", longBlock);
        Assert.DoesNotContain(Catalog.Problems, p => p.Contains(EngineKits.FileName, StringComparison.Ordinal));
    }

    [Fact]
    public void A_short_block_is_the_block_and_what_turns_in_it_a_complete_kit_everything()
    {
        var build = Build("rated/chrysler-la-340-275-hp").Build;
        var shortBlock = KitWork.NewParts(Catalog, build, Size("short_block"));
        var complete = KitWork.NewParts(Catalog, build, Size("complete"));

        Assert.True(PartKinds.IsBlock(Catalog.Get(shortBlock[0].DefinitionId)!));
        Assert.Contains(shortBlock, p => KindOf(p) == "crankshaft");
        Assert.Contains(shortBlock, p => KindOf(p) == "pistons");
        Assert.DoesNotContain(shortBlock, p => KindOf(p) == "cylinder_head");
        Assert.DoesNotContain(shortBlock, p => KindOf(p) == "carburettor");
        Assert.Equal(build.Parts.Count(id => Catalog.Get(id) != null), complete.Count);
        Assert.All(complete, p => Assert.Equal(1.0, p.Wear));
    }

    [Fact]
    public void A_complete_kit_put_together_runs_as_its_build_does()
    {
        var rated = Build("rated/chrysler-la-340-275-hp");
        var built = KitWork.Assemble(Catalog, KitWork.NewParts(Catalog, rated.Build, Size("complete")));

        Assert.NotNull(built);
        var report = EngineFactory.Evaluate(Catalog, built!.Value.Engine);
        Assert.True(report!.Runs);
        Assert.Equal(rated.PowerHp, report.Dyno!.MaxPowerHp, 1);
        Assert.Equal(0, built.Value.Engine.ParentSlot);
    }

    [Fact]
    public void Each_part_goes_on_as_the_very_one_out_of_the_crate_and_what_finds_no_place_is_left()
    {
        var build = Build("rated/chrysler-la-340-275-hp").Build;
        var parts = KitWork.NewParts(Catalog, build, Size("short_block"));
        for (var i = 0; i < parts.Count; i++) parts[i].Wear = 0.5 + i * 0.01;
        // A tyre goes nowhere on an engine
        var tyre = new PartInstance(Catalog.Parts.Values.First(p => p.IsRealPart && PartKinds.GroupOf(p) == PartKinds.Tyres).Id);
        parts.Add(tyre);

        var (engine, left) = KitWork.Assemble(Catalog, parts)!.Value;

        var placed = engine.SelfAndDescendants().ToDictionary(p => p.InstanceId);
        Assert.Equal(parts.Count - 1, placed.Count);
        foreach (var part in parts.Where(p => p != tyre)) Assert.Equal(part.Wear, placed[part.InstanceId].Wear);
        Assert.Equal([tyre], left);
    }

    [Fact]
    public void A_crate_without_a_block_makes_no_engine()
    {
        var build = Build("rated/chrysler-la-340-275-hp").Build;
        var parts = KitWork.NewParts(Catalog, build, Size("long_block")).Skip(1).ToList();

        Assert.Null(KitWork.Assemble(Catalog, parts));
    }

    [Fact]
    public void An_engine_taken_apart_is_never_short_of_its_short_block()
    {
        var engine = EngineFactory.CreateStock(Catalog, Build("rated/chrysler-la-340-275-hp"), 0.6, new Random(2))!.Root;
        var count = engine.SelfAndDescendants().Count();
        var shortKinds = Catalog.Kits.KindsOf(Size("short_block"));

        for (var seed = 0; seed < 30; seed++)
        {
            var (parts, kept) = KitWork.TearDown(Catalog, engine, new Random(seed), 3);

            Assert.Equal(count, parts.Count + kept.Count);
            Assert.InRange(kept.Count, 0, 3);
            Assert.Equal(engine.InstanceId, parts[0].InstanceId);
            Assert.All(parts.Concat(kept), p => Assert.Empty(p.Children));
            Assert.All(parts.Concat(kept), p => Assert.Equal(0, p.ParentSlot));
            Assert.All(kept, p => Assert.DoesNotContain(KindOf(p) ?? string.Empty, shortKinds));
        }

        // The engine itself is left as it was
        Assert.Equal(count, engine.SelfAndDescendants().Count());
    }

    [Fact]
    public void A_mod_replaces_a_size_takes_one_out_and_its_broken_sizes_are_told()
    {
        _temp.File("base/engine_kits.json", """
            { "sizes": [ { "id": "short_block", "kinds": [ "block", "crankshaft" ] }, { "id": "long_block", "includes": "short_block", "kinds": [ "cylinder_head" ] },
                         { "id": "complete", "all": true } ] }
            """);
        _temp.File("mod/engine_kits.json", """
            { "sizes": [ { "id": "LONG_BLOCK", "kinds": [ "oil_pan" ], "includes": "nothing" }, { "id": "complete", "disabled": true }, { "id": "empty" }, { "kinds": [ "block" ] } ] }
            """);

        var problems = new List<string>();
        var kits = EngineKits.Load([(ModIds.BaseId, true, _temp.Combine("base")), ("mymod", false, _temp.Combine("mod"))], problems);

        Assert.Equal(["short_block", "LONG_BLOCK"], kits.Sizes.Select(s => s.Id));
        Assert.Null(kits.Complete);
        Assert.Equal(["oil_pan"], kits.KindsOf(kits.Get("long_block")!));
        Assert.Equal(3, problems.Count);
        Assert.All(problems, p => Assert.StartsWith("mymod/engine_kits.json: ", p));
    }
}

/// <summary>Engine kits at the part shops, on the player's shelf and in the paper (roadmap step 40)</summary>
public class EngineKitShopTests
{
    private static readonly CatalogParts Parts = new();

    private static readonly PartShopDefinition Mopar = new()
    {
        Id = "hemi", Name = "Hemi Hut", Region = "Burbank", Kind = "specialist", Makes = ["mopar"], StockLow = 5, StockHigh = 5
    };

    private static readonly PartShopDefinition Speed = new() { Id = "speed", Name = "Speed City", Region = "Downey", Kind = "speed" };

    private static readonly PartShopDefinition Yard = new()
    {
        Id = "yard", Name = "Wreckers", Region = "Docks", Kind = "junkyard", StockLow = 4, StockHigh = 4
    };

    private readonly GameState _state = new();
    private readonly PartShopService _service;

    public EngineKitShopTests()
    {
        _service = new PartShopService(new FakePartShops(Mopar, Speed, Yard), Parts, new NewspaperPartsService(Parts));
        PartShopRoster.Sync(_state, _service.Catalog);
        _state.Player.Money = 1_000_000m;
    }

    private PartShopLocation Shop(string id) => PartShopRoster.Find(_state, id)!;

    private static PartsCatalog Catalog => Parts.Catalog;

    [Fact]
    public void A_specialist_deals_in_kits_of_its_makes_factory_engines_a_speed_shop_in_the_speed_shop_builds()
    {
        var mopar = _service.KitAssortmentOf(Mopar);
        var speed = _service.KitAssortmentOf(Speed);

        Assert.NotEmpty(mopar);
        Assert.All(mopar, l => Assert.Equal("mopar", l.Parts.First(PartKinds.IsBlock).Make));
        Assert.All(mopar, l => Assert.False(l.Build.Aftermarket));
        Assert.All(speed, l => Assert.True(l.Build.Aftermarket));
        Assert.Empty(_service.KitAssortmentOf(Yard));

        // One line of each crate, whichever build told it
        Assert.Equal(mopar.Count, mopar.Select(l => string.Join("|", l.Parts.Select(p => p.Id).Order())).Distinct().Count());
    }

    [Fact]
    public async Task A_delivery_brings_a_few_kits_one_or_two_of_each()
    {
        await _service.RestockAsync(_state, _state.Date, new Random(5));
        var shop = Shop("hemi");

        Assert.InRange(shop.Kits.Count, PartShopRules.KitLines.Low, PartShopRules.KitLines.High);
        Assert.All(shop.Kits, k => Assert.InRange(k.Count, PartShopRules.KitsPerLine.Low, PartShopRules.KitsPerLine.High));
        Assert.All(shop.Kits, k => Assert.NotNull(_service.LineOf(k)));
    }

    [Fact]
    public void A_kit_costs_its_parts_at_the_shops_price_less_the_bundles_discount()
    {
        var shop = Shop("hemi");
        var line = _service.KitAssortmentOf(Mopar).First(l => l.Size.All);
        var kit = new ShopKit { BuildId = line.Build.Id, Size = line.Size.Id, Count = 1 };
        shop.Kits.Add(kit);

        var parts = line.Parts.Sum(p => _service.MarkedPriceOfNew(_state, shop, p));
        var price = _service.PriceOfKit(_state, shop, kit);

        Assert.Equal((double)parts * (1 - PartShopRules.KitDiscount), (double)price, (double)parts * 0.01);
        Assert.True(price < parts);
        // Still well over what a junkyard pays for the parts new
        Assert.True((double)price > (double)line.Parts.Sum(p => PartPricing.NewPrice(p)) * PartShopRules.BuyFactor(Yard));
    }

    [Fact]
    public void A_kit_bought_lies_on_the_shelf_in_its_crate_and_opens_once_a_part_is_taken_out()
    {
        var shop = Shop("hemi");
        var line = _service.KitAssortmentOf(Mopar).First(l => l.Size.Id == "short_block");
        var kit = new ShopKit { BuildId = line.Build.Id, Size = line.Size.Id, Count = 1 };
        shop.Kits.Add(kit);
        var money = _state.Player.Money;
        var price = _service.PriceOfKit(_state, shop, kit);

        Assert.True(_service.BuyKit(_state, shop, kit));
        Assert.False(_service.BuyKit(_state, shop, kit));

        Assert.Equal(0, kit.Count);
        Assert.Equal(money - price, _state.Player.Money);
        var crate = Assert.Single(_state.Player.Crates);
        Assert.True(crate.New);
        Assert.Equal(line.Parts.Count, KitCrates.PartsOf(_state.Player, crate).Count);
        Assert.Equal(line.Parts.Count, KitCrates.Crated(_state.Player).Count);

        // Sold off the shelf, one part breaks the crate open: the rest are loose
        _state.Player.Parts.Remove(KitCrates.PartsOf(_state.Player, crate)[^1]);
        Assert.True(KitCrates.Tidy(_state.Player));
        Assert.Empty(_state.Player.Crates);
        Assert.Equal(line.Parts.Count - 1, _state.Player.Parts.Count);
    }

    [Fact]
    public void A_crate_put_together_leaves_the_shelf_for_the_engine_but_what_finds_no_place()
    {
        var line = _service.KitAssortmentOf(Mopar).First(l => l.Size.Id == "long_block");
        var parts = KitWork.NewParts(Catalog, line.Build, line.Size);
        var tyre = new PartInstance(Catalog.Parts.Values.First(p => p.IsRealPart && PartKinds.GroupOf(p) == PartKinds.Tyres).Id);
        var loose = new PartInstance(tyre.DefinitionId);
        _state.Player.Parts.Add(loose);
        var crate = KitCrates.Pack(_state.Player, [.. parts, tyre], line.Build.Id, line.Size.Id, line.Name, isNew: true);

        var engine = KitCrates.Assemble(Catalog, _state.Player, crate);

        Assert.NotNull(engine);
        Assert.Empty(_state.Player.Crates);
        Assert.Equal([loose, tyre], _state.Player.Parts);
        Assert.Equal(parts.Count, engine!.SelfAndDescendants().Count());
    }

    [Fact]
    public void A_used_kit_in_the_yard_never_goes_under_what_the_yard_pays_for_its_parts()
    {
        var shop = Shop("yard");
        var engine = EngineFactory.CreateStock(Catalog, Parts.Builds.Runnable[0], 0.5, new Random(1))!.Root;
        var (parts, _) = KitWork.TearDown(Catalog, engine, new Random(1), 2);
        var kit = new KitAd { Parts = parts, Name = "test", AskingPrice = 1m, SellerName = "Wreckers", PostedDate = _state.Date };
        shop.YardKits.Add(kit);

        var price = _service.PriceOfUsedKit(_state, shop, kit);
        Assert.True(price > parts.Sum(p => _service.BuyingPrice(shop, p)));

        Assert.True(_service.BuyUsedKit(_state, shop, kit));
        Assert.Empty(shop.YardKits);
        var crate = Assert.Single(_state.Player.Crates);
        Assert.False(crate.New);
        Assert.Equal(parts.Count, KitCrates.PartsOf(_state.Player, crate).Count);
    }

    [Fact]
    public void A_rival_sells_a_swapped_out_engine_as_a_kit_saying_what_they_kept()
    {
        var rival = new Opponent { Name = "Rocco", Money = 0m };
        _state.Racers.ReadyToRace[rival.Name] = rival;
        var engine = EngineFactory.CreateStock(Catalog, Parts.Builds.Runnable[0], 0.6, new Random(3))!.Root;

        KitAd? ad = null;
        for (var seed = 0; ad == null || ad.Missing.Count == 0; seed++)
        {
            _state.NewspaperAds.Kits.Clear();
            rival.Money = 0m;
            ad = RivalPartAds.PostKit(_state, Catalog, rival, engine, _state.Date, new Random(seed));
            Assert.True(seed < 50);
        }

        Assert.Equal("Rocco", ad.SellerRival);
        Assert.Same(ad, Assert.Single(_state.NewspaperAds.Kits));
        Assert.True(rival.Money > 0m);
        Assert.Equal(engine.SelfAndDescendants().Count(), ad.Parts.Count + ad.Missing.Count);

        // The player buys it: the rival is paid, the kit is in a crate saying what it lacks
        var paper = new NewspaperPartsService(Parts);
        var before = rival.Money;
        Assert.True(paper.BuyKit(_state, ad));
        Assert.Equal(before + ad.AskingPrice, rival.Money);
        Assert.Equal(ad.Missing, Assert.Single(_state.Player.Crates).Missing);
        Assert.False(paper.BuyKit(_state, ad));
    }

    [Fact]
    public void A_rival_puts_in_a_kit_of_a_bigger_engine_when_it_comes_cheap()
    {
        var stock = Parts.Builds.Runnable.Single(b => b.Build.Id.EndsWith("rated/chrysler-la-340-275-hp", StringComparison.Ordinal));
        var engine = EngineFactory.CreateStock(Catalog, stock, 0.8, new Random(1))!;
        var power = engine.Report.Dyno!.MaxPowerHp;
        var bigger = Parts.Builds.Runnable.First(b => b.Family == stock.Family && !b.Build.Aftermarket
                                                     && b.PowerHp >= power * (1 + EngineTuner.MinGain) * 1.05 && b.PowerHp <= power * EngineTuner.MaxSwapPower * 0.95);
        var kit = new KitOffer(null, "hemi", bigger.Build.Id, "complete", bigger.Build.Name,
            KitWork.NewParts(Catalog, bigger.Build, Catalog.Kits.Complete!), 1m);

        var result = EngineTuner.TuneUp(Catalog, Parts.Builds, engine.Root, 1_000_000m, 1.0, new Random(7), maxUpgrades: 1, kits: [kit]);

        Assert.NotNull(result);
        var upgrade = Assert.Single(result!.Upgrades, u => u.Group != EngineTuner.CoolingGroup);
        Assert.Same(kit, upgrade.Kit);
        Assert.Equal(EngineTuner.BlockGroup, upgrade.Group);
        Assert.Equal(bigger.BlockId, result.Engine.DefinitionId);
        // The kit was put together from copies: the offer's parts are as they were
        Assert.All(kit.Parts, p => Assert.Empty(p.Children));
    }

    [Fact]
    public void A_shop_takes_a_kit_off_its_shelf_for_a_rival_and_offers_only_complete_ones()
    {
        var shop = Shop("hemi");
        var lines = _service.KitAssortmentOf(Mopar);
        var complete = lines.First(l => l.Size.All);
        var shortBlock = lines.First(l => !l.Size.All);
        shop.Kits.Add(new ShopKit { BuildId = complete.Build.Id, Size = complete.Size.Id, Count = 1 });
        shop.Kits.Add(new ShopKit { BuildId = shortBlock.Build.Id, Size = shortBlock.Size.Id, Count = 1 });

        var offer = Assert.Single(_service.RivalKits(_state));
        Assert.Equal(complete.Build.Id, offer.BuildId);
        Assert.Equal("hemi", offer.ShopId);

        Assert.True(_service.TakeKitForRival(_state, "hemi", complete.Build.Id, complete.Size.Id));
        Assert.False(_service.TakeKitForRival(_state, "hemi", complete.Build.Id, complete.Size.Id));
        Assert.Empty(_service.RivalKits(_state));
    }

    [Fact]
    public void A_shop_kept_to_some_groups_of_part_sells_no_kit_with_a_part_of_another()
    {
        var line = _service.KitAssortmentOf(Mopar).First(l => l.Size.All);
        var groups = line.Parts.Select(PartKinds.GroupOf).Distinct().ToList();
        Assert.True(groups.Count > 1);
        var oneGroup = new PartShopDefinition { Id = "carbs", Name = "Carb Corner", Region = "Burbank", Kind = "specialist", Makes = ["mopar"], Groups = [groups[^1]] };
        var allGroups = new PartShopDefinition { Id = "all", Name = "Everything Mopar", Region = "Burbank", Kind = "specialist", Makes = ["mopar"], Groups = groups };
        var paper = new NewspaperPartsService(Parts);

        Assert.Empty(new PartShopService(new FakePartShops(oneGroup), Parts, paper).KitAssortmentOf(oneGroup));
        Assert.Contains(new PartShopService(new FakePartShops(allGroups), Parts, paper).KitAssortmentOf(allGroups), l => l.Build.Id == line.Build.Id && l.Size.All);
    }

    [Fact]
    public void Kit_lines_of_one_build_and_size_become_one_line_with_their_counts_together()
    {
        var shop = Shop("hemi");
        var line = _service.KitAssortmentOf(Mopar).First(l => l.Size.All);
        shop.Kits.Add(new ShopKit { BuildId = line.Build.Id, Size = line.Size.Id, Count = 1 });
        shop.Kits.Add(new ShopKit { BuildId = line.Build.Id.ToUpperInvariant(), Size = line.Size.Id, Count = 2 });
        shop.Kits.Add(new ShopKit { BuildId = line.Build.Id, Size = "short_block", Count = 1 });

        Assert.Equal(1, PartShopService.RenameShelves(Catalog, [shop]));

        Assert.Equal(2, shop.Kits.Count);
        Assert.Equal(3, shop.Kits.Single(k => k.Size == line.Size.Id).Count);
        Assert.Equal(0, PartShopService.RenameShelves(Catalog, [shop]));
    }

    [Fact]
    public void Two_rivals_putting_in_the_same_shop_kit_get_parts_of_their_own_a_used_kit_the_very_parts_of_its_ad()
    {
        var stock = Parts.Builds.Runnable.Single(b => b.Build.Id.EndsWith("rated/chrysler-la-340-275-hp", StringComparison.Ordinal));
        var engine = EngineFactory.CreateStock(Catalog, stock, 0.8, new Random(1))!;
        var power = engine.Report.Dyno!.MaxPowerHp;
        var bigger = Parts.Builds.Runnable.First(b => b.Family == stock.Family && !b.Build.Aftermarket
                                                     && b.PowerHp >= power * (1 + EngineTuner.MinGain) * 1.05 && b.PowerHp <= power * EngineTuner.MaxSwapPower * 0.95);
        var kit = new KitOffer(null, "hemi", bigger.Build.Id, "complete", bigger.Build.Name, KitWork.NewParts(Catalog, bigger.Build, Catalog.Kits.Complete!), 1m);

        var first = EngineTuner.TuneUp(Catalog, Parts.Builds, engine.Root, 1_000_000m, 1.0, new Random(7), maxUpgrades: 1, kits: [kit])!;
        var second = EngineTuner.TuneUp(Catalog, Parts.Builds, engine.Root, 1_000_000m, 1.0, new Random(8), maxUpgrades: 1, kits: [kit])!;

        Assert.Equal(bigger.BlockId, first.Engine.DefinitionId);
        Assert.Equal(bigger.BlockId, second.Engine.DefinitionId);
        var firstIds = first.Engine.SelfAndDescendants().Select(p => p.InstanceId).ToHashSet();
        Assert.DoesNotContain(second.Engine.SelfAndDescendants(), p => firstIds.Contains(p.InstanceId));
        Assert.DoesNotContain(kit.Parts, p => firstIds.Contains(p.InstanceId));

        // Out of the paper, the kit is the very parts of its ad
        var used = kit with { AdId = Guid.NewGuid(), ShopId = null };
        var fromPaper = EngineTuner.TuneUp(Catalog, Parts.Builds, engine.Root, 1_000_000m, 1.0, new Random(7), maxUpgrades: 1, kits: [used])!;
        Assert.Equal(bigger.BlockId, fromPaper.Engine.DefinitionId);
        var adIds = used.Parts.Select(p => p.InstanceId).ToHashSet();
        Assert.All(fromPaper.Engine.SelfAndDescendants(), p => Assert.Contains(p.InstanceId, adIds));
    }

    [Fact]
    public async Task A_rivals_kit_ad_that_ran_out_or_lost_a_part_pays_them_the_trade_in_of_what_is_left()
    {
        var rival = new Opponent { Name = "Rocco", Money = 0m };
        _state.Racers.ReadyToRace[rival.Name] = rival;
        var engine = EngineFactory.CreateStock(Catalog, Parts.Builds.Runnable[0], 0.6, new Random(3))!.Root;
        var paper = new NewspaperPartsService(Parts);
        var lost = RivalPartAds.PostKit(_state, Catalog, rival, engine, _state.Date, new Random(1))!;
        lost.Parts.Add(new PartInstance("nobody/no-such-part"));
        var expired = RivalPartAds.PostKit(_state, Catalog, rival, engine, _state.Date.AddDays(-365), new Random(2))!;
        rival.Money = 0m;
        var owed = lost.Parts.Where(p => Catalog.Get(p.DefinitionId) != null).Concat(expired.Parts).Sum(paper.TradeInPrice);

        await paper.RefreshAdsAsync(_state, _state.Date);

        Assert.DoesNotContain(lost, _state.NewspaperAds.Kits);
        Assert.DoesNotContain(expired, _state.NewspaperAds.Kits);
        Assert.Equal(owed, rival.Money);
        // No more kits than the kit chance's share of a full paper
        var economy = Rules.RuleBook.Current.Economy;
        Assert.InRange(_state.NewspaperAds.Kits.Count, 0, Math.Max(1, (int)Math.Ceiling(economy.PartAdsHigh * economy.PartAdKitChance)));
    }

    [Fact]
    public void The_street_says_where_what_a_rival_put_in_came_from_in_the_text_pools_words()
    {
        var kit = new KitOffer(null, "hemi", "b", "complete", "Chrysler 383", [], 1m);
        TuneUpgrade Block(Guid? adId = null, KitOffer? offer = null, string what = "Chrysler 383") =>
            new("log", EngineTuner.BlockGroup, 200, 300, 1m, 0m, [], adId) { Kit = offer, What = what };

        Assert.Equal("a Chrysler 383 kit", OpponentLifeService.ChangeText(Block(offer: kit)));
        Assert.Equal("a used Chrysler 383 kit out of the paper", OpponentLifeService.ChangeText(Block(offer: kit with { AdId = Guid.NewGuid() })));
        Assert.Equal("a used 383 out of the paper", OpponentLifeService.ChangeText(Block(Guid.NewGuid(), what: "383")));
        Assert.Equal("the engine of a Road Runner", OpponentLifeService.ChangeText(Block(what: "Road Runner")));
        Assert.Equal("used Holley 750", OpponentLifeService.ChangeText(new TuneUpgrade("used Holley 750", "carburettors", 1, 2, 1m, 0m, [], Guid.NewGuid()) { What = "Holley 750" }));
        Assert.Equal("Holley 750", OpponentLifeService.ChangeText(new TuneUpgrade("Holley 750", "carburettors", 1, 2, 1m, 0m, [])));
    }
}
