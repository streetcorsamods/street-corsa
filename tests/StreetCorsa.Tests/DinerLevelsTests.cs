using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.Diners;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Screens.Diner;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Diner;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// Step 37, phase 3: the diners by level. Each takes the cars from a horsepower up; a racer is a regular of the diner
/// their car's power puts them in (a boss of the top one); the regulars of a diner above the player's car will not race
/// it; the diners' map lists them.
/// </summary>
public sealed class DinerLevelsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static ModSet BaseMods => ModSet.Load(RepoPaths.Content);

    private static DinerBook Base => DinerBook.Load(BaseMods, DinerScenes.LoadAll(BaseMods));

    /// <summary>Four diners with the base game's floors, over no streets</summary>
    private static DinerBook Four() => DinerBook.From(
    [
        new Diner { Id = "starlite", PowerFrom = 380, Stakes = 3 },
        new Diner { Id = "mainstreet", PowerFrom = 0 },
        new Diner { Id = "surfside", PowerFrom = 300, Stakes = 2 },
        new Diner { Id = "hiway", PowerFrom = 220, Stakes = 1.5m }
    ]);

    private static Opponent Racer(string name, double? powerHp, bool boss = false)
    {
        var racer = new Opponent(name, 30, Gender.Male, 95, 50) { IsBoss = boss };
        racer.Cars.Add(new Car("car") { PowerHp = powerHp });
        return racer;
    }

    private static RacerOut Out(Opponent racer, string? bhp = null) =>
        new(racer, racer.Cars[0], new CarDefinition { Id = "car", Specs = bhp == null ? null : new CarSpecsData { Bhp = bhp } });

    // ----- the base game's diners -----

    [Fact]
    public void The_base_game_has_four_diners_from_main_street_up_each_with_a_name_and_a_word_about_it()
    {
        var book = Base;

        Assert.Equal(["mainstreet", "hiway", "surfside", "starlite"], book.All.Select(d => d.Id));
        Assert.Equal([0, 1, 2, 3], book.All.Select(d => d.Level));
        Assert.Equal(0, book.First!.PowerFrom);
        Assert.True(book.First.IsFirst);
        Assert.Same(book.All[^1], book.Top);
        for (var i = 1; i < book.Count; i++)
        {
            Assert.True(book.All[i].PowerFrom > book.All[i - 1].PowerFrom, book.All[i].Id);
            Assert.True(book.All[i].Stakes > book.All[i - 1].Stakes, book.All[i].Id);
        }

        var text = GameText.Load(BaseMods);
        foreach (var diner in book.All)
        {
            Assert.True(DinerBook.NameKeys(diner).Any(text.Has), diner.Id);
            Assert.True(DinerBook.AboutKeys(diner).Any(text.Has), diner.Id);
        }

        Assert.Equal("The Main Street Diner", book.NameOf(book.First));
        Assert.True(text.Has("replies.snub"));
    }

    [Fact]
    public void Every_base_diner_is_on_the_map_within_the_part_of_town_shown_and_clear_of_the_others()
    {
        var book = Base;
        var map = new Services.Dealers.DealerCatalog(BaseMods).Map!;
        var text = GameText.Load(BaseMods);

        foreach (var diner in book.All)
        {
            Assert.True(diner.IsOnMap, diner.Id);
            Assert.InRange(diner.MapX!.Value, map.Anchor.X, map.Anchor.X + map.Anchor.Width);
            Assert.InRange(diner.MapY!.Value, map.Anchor.Y, map.Anchor.Y + map.Anchor.Height);
            Assert.True(DinerBook.RegionKeys(diner).Any(text.Has), diner.Id);
        }

        // Far enough apart for their plates not to cover each other
        var spots = book.All.Select(d => (d.MapX!.Value, d.MapY!.Value)).ToList();
        for (var i = 0; i < spots.Count; i++)
        for (var j = i + 1; j < spots.Count; j++)
            Assert.True(Math.Sqrt(Math.Pow(spots[i].Item1 - spots[j].Item1, 2) + Math.Pow(spots[i].Item2 - spots[j].Item2, 2)) > 0.03, $"{book.All[i].Id} and {book.All[j].Id}");
    }

    [Fact]
    public void A_diner_off_the_picture_is_told_and_marked_along_the_maps_foot_so_it_can_be_driven_to()
    {
        var mods = Mods("""{ "diners": [ { "id": "lost", "powerFrom": 250, "mapX": 1.4, "mapY": 0.5 } ] }""");
        var book = DinerBook.Load(mods, DinerScenes.LoadAll(mods));

        var lost = book.Get("mymod:lost")!;
        Assert.False(lost.IsOnMap);
        Assert.Null(lost.MapX);
        Assert.Contains(book.Problems, p => p.Contains("lost") && p.Contains("not on the map"));
        Assert.True(book.First!.IsOnMap);

        // The spots along the foot: inside the part of the picture shown, low on it, none on another
        var shown = new System.Windows.Rect(0.1, 0.2, 0.5, 0.6);
        var spots = Enumerable.Range(0, 20).Select(i => Screens.DealerMap.DealerMapScreenViewModel.OffMapSpot(shown, i)).ToList();
        Assert.All(spots, s =>
        {
            Assert.InRange(s.X, shown.Left, shown.Right);
            Assert.InRange(s.Y, shown.Top, shown.Bottom);
        });
        Assert.True(spots[0].Y > shown.Top + shown.Height * 0.8);
        Assert.Equal(spots.Count, spots.Distinct().Count());
    }

    [Fact]
    public void A_mods_street_with_a_diner_that_no_diner_names_is_where_the_beginners_diner_is_seen()
    {
        _temp.File("mymod/mod.json", """{ "id": "mymod" }""");
        foreach (var name in new[] { "street.json", "diner.json" })
            _temp.File($"mymod/streets/myblock/{name}", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "streets", "mainstreet", name))
                .Replace("\"id\": \"mainstreet\"", "\"id\": \"myblock\""));
        var mods = Mods(null);
        var scenes = DinerScenes.LoadAll(mods);
        var book = DinerBook.Load(mods, scenes);
        var mine = Assert.Single(scenes, s => s.Id.StartsWith("mymod", StringComparison.Ordinal));

        Assert.Equal("mainstreet", book.First!.Id);
        Assert.Equal(mine.Id, book.First.Street);
        Assert.Equal(mine.Folder, book.SceneOf(book.First)!.Folder);
        Assert.Null(book.First.Look);

        // The diners whose streets are not built are seen there too; the Starlite keeps its own look
        Assert.Equal(mine.Folder, book.SceneOf(book.Get("starlite"))!.Folder);
        Assert.NotNull(book.Get("starlite")!.Look?.Layout);
    }

    [Fact]
    public void The_first_diner_is_on_main_street_and_the_others_are_seen_there_until_their_streets_are_built()
    {
        var scenes = DinerScenes.LoadAll(BaseMods);
        var book = DinerBook.Load(BaseMods, scenes);
        var mainStreet = Assert.Single(scenes);

        Assert.Equal(mainStreet.Id, book.First!.Street);
        foreach (var diner in book.All) Assert.Equal(mainStreet.Kn5, book.SceneOf(diner)!.Kn5);

        // The streets still to build are named all the same, for when they are; and they are no problem to show the
        // player at every start
        Assert.Equal(["mainstreet", "hiway", "surfside", "starlite"], book.All.Select(d => d.Street));
        Assert.Empty(book.Problems);
    }

    // ----- the classes -----

    [Fact]
    public void A_car_belongs_at_the_highest_diner_whose_floor_it_reaches()
    {
        var book = Four();

        Assert.Equal("mainstreet", book.Of(150)!.Id);
        Assert.Equal("mainstreet", book.Of(219.9)!.Id);
        Assert.Equal("hiway", book.Of(220)!.Id);
        Assert.Equal("hiway", book.Of(299.9)!.Id);
        Assert.Equal("surfside", book.Of(300)!.Id);
        Assert.Equal("starlite", book.Of(380)!.Id);
        Assert.Equal("starlite", book.Of(1000)!.Id);

        // No figure, or no figure that is one: the beginners' diner
        Assert.Equal("mainstreet", book.Of(null)!.Id);
        Assert.Equal("mainstreet", book.Of(double.NaN)!.Id);
        Assert.Equal("mainstreet", book.Of(-5)!.Id);
        Assert.Null(DinerBook.From([]).Of(300));
    }

    [Fact]
    public void The_regulars_of_a_diner_above_the_players_car_will_not_race_it_and_the_first_diner_turns_nobody_away()
    {
        var book = Four();
        var first = book.Get("mainstreet")!;
        var hiway = book.Get("hiway")!;
        var top = book.Get("starlite")!;

        Assert.False(DinerBook.Snubs(first, 0));
        Assert.False(DinerBook.Snubs(first, null));
        Assert.True(DinerBook.Snubs(hiway, 150));
        Assert.True(DinerBook.Snubs(hiway, 219.9));
        Assert.False(DinerBook.Snubs(hiway, 220));
        Assert.False(DinerBook.Snubs(hiway, 400));

        // A car the game has no figure for is not turned away for what nobody has measured
        Assert.False(DinerBook.Snubs(top, null));
        Assert.False(DinerBook.Snubs(top, double.NaN));
        Assert.True(DinerBook.Snubs(top, 200));

        // The King takes on anybody at his table, and a rival out for a rematch wants it whatever the player drives
        Assert.True(DinerBook.Snubs(top, Racer("regular", 400), 200));
        Assert.False(DinerBook.Snubs(top, Racer("king", 400, boss: true), 200));
        var rival = Racer("rival", 400);
        rival.Grudge = new Grudge();
        Assert.False(DinerBook.Snubs(top, rival, 200));
    }

    [Fact]
    public void Two_diners_on_one_floor_share_its_racers_and_a_car_of_that_power_belongs_at_the_first()
    {
        var book = DinerBook.From(
        [
            new Diner { Id = "b", PowerFrom = 0, Order = 2 },
            new Diner { Id = "a", PowerFrom = 0, Order = 1 },
            new Diner { Id = "up", PowerFrom = 300 }
        ]);

        Assert.Equal("a", book.Of(100)!.Id);
        Assert.Equal("up", book.Of(300)!.Id);

        // Each racer always at the same one, and both get some of the twenty
        var racers = Enumerable.Range(0, 20).Select(i => Racer($"racer {i}", 150)).ToList();
        var at = racers.Select(r => book.Of(r, r.Cars[0], null)!.Id).ToList();
        Assert.Equal(at, racers.Select(r => book.Of(r, r.Cars[0], null)!.Id));
        Assert.Contains("a", at);
        Assert.Contains("b", at);
        Assert.DoesNotContain("up", at);
    }

    [Fact]
    public void The_racers_out_keep_to_the_diner_of_their_cars_power_and_a_boss_to_the_top_one()
    {
        var book = Four();
        var today = new List<RacerOut>
        {
            Out(Racer("Six", 140)),
            Out(Racer("Small block", 250)),
            Out(Racer("Big block", 410)),
            Out(Racer("The King", 100, boss: true)),
            // No dyno figure: the spec sheet's power stands in
            Out(Racer("Spec sheet", null), bhp: "350bhp"),
            Out(Racer("Unknown", null))
        };

        Assert.Equal(["Six", "Unknown"], RacersOut.AtDiner(today, book.Get("mainstreet")!, book).Select(r => r.Opponent.Name));
        Assert.Equal(["Small block"], RacersOut.AtDiner(today, book.Get("hiway")!, book).Select(r => r.Opponent.Name));
        Assert.Equal(["Spec sheet"], RacersOut.AtDiner(today, book.Get("surfside")!, book).Select(r => r.Opponent.Name));
        Assert.Equal(["Big block", "The King"], RacersOut.AtDiner(today, book.Get("starlite")!, book).Select(r => r.Opponent.Name));
    }

    // ----- the stakes -----

    [Fact]
    public void The_stakes_of_the_place_raise_the_house_limit_and_the_curb_plays_for_the_usual()
    {
        var noon = new DateTime(1969, 6, 1, 12, 0, 0);
        var (_, usual) = MatchupCalculator.WagerLimits("drag", 10_000m, 10_000m, 50, noon);
        var (_, doubled) = MatchupCalculator.WagerLimits("drag", 10_000m, 10_000m, 50, noon, stakes: 2m);
        var (_, odd) = MatchupCalculator.WagerLimits("drag", 10_000m, 10_000m, 50, noon, stakes: 0m);

        Assert.Equal(usual * 2, doubled);
        Assert.Equal(usual, odd);
    }

    // ----- a mod's diners -----

    private ModSet Mods(string? dinersJson, bool baseDiners = true)
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        foreach (var name in new[] { "street.json", "diner.json" })
            _temp.File($"{ModIds.BaseId}/streets/mainstreet/{name}", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "streets", "mainstreet", name)));
        if (baseDiners)
            _temp.File($"{ModIds.BaseId}/diners/diners.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "diners", "diners.json")));
        if (dinersJson != null)
        {
            _temp.File("mymod/mod.json", """{ "id": "mymod" }""");
            _temp.File("mymod/diners/diners.json", dinersJson);
        }

        return ModSet.Load(_temp.Path);
    }

    [Fact]
    public void A_mod_may_replace_a_diner_add_one_above_the_top_and_put_one_on_the_base_games_street()
    {
        var mods = Mods("""
            { "diners": [
                { "replaces": "hiway", "street": "mainstreet", "powerFrom": 200, "stakes": 1.2 },
                { "id": "top", "powerFrom": 500, "stakes": 5 }
            ] }
            """);
        var scenes = DinerScenes.LoadAll(mods);
        var book = DinerBook.Load(mods, scenes);

        Assert.Equal(["mainstreet", "hiway", "surfside", "starlite", "mymod:top"], book.All.Select(d => d.Id));
        Assert.Equal(200, book.Get("hiway")!.PowerFrom);
        Assert.Equal("mymod", book.Get("hiway")!.ModId);
        Assert.Equal(4, book.Top!.Level);
        Assert.Equal("mymod:top", book.Of(600)!.Id);

        // "mainstreet" from a mod means the base game's street, as the mod has none of its own; the new top diner names
        // no street and is seen on the first's, without a word about it
        var mainStreet = Assert.Single(scenes);
        Assert.Equal(mainStreet.Kn5, book.SceneOf(book.Get("hiway"))!.Kn5);
        Assert.Null(book.Top.Street);
        Assert.Equal(mainStreet.Kn5, book.SceneOf(book.Top)!.Kn5);
        Assert.Empty(book.Problems);

        // A diner keeps the look it lays over the shared street; one replaced without a look, or a new one, has none
        Assert.NotNull(book.Get("starlite")!.Look?.Layout);
        Assert.Null(book.Get("hiway")!.Look);
        Assert.Null(book.Top.Look);
        Assert.Empty(book.SceneOf(book.Top)!.ShownLooks);
    }

    [Fact]
    public void A_diner_with_no_floor_is_left_out_and_odd_stakes_are_the_usual()
    {
        var mods = Mods("""
            { "diners": [
                { "id": "nowhere", "powerFrom": -10 },
                { "id": "cheap", "powerFrom": 250, "stakes": 0 }
            ] }
            """);
        var book = DinerBook.Load(mods, DinerScenes.LoadAll(mods));

        Assert.Null(book.Get("mymod:nowhere"));
        Assert.Equal(1m, book.Get("mymod:cheap")!.Stakes);
        Assert.Contains(book.Problems, p => p.Contains("nowhere") && p.Contains("left out"));
        Assert.Contains(book.Problems, p => p.Contains("stakes 0"));
    }

    [Fact]
    public void With_no_diners_listed_every_street_with_a_diner_on_it_is_a_diner_of_the_first_level()
    {
        var mods = Mods(null, baseDiners: false);
        var scenes = DinerScenes.LoadAll(mods);
        var book = DinerBook.Load(mods, scenes);

        var only = Assert.Single(book.All);
        Assert.Equal(Assert.Single(scenes).Id, only.Id);
        Assert.Equal(only.Id, only.Street);
        Assert.True(only.IsFirst);
        Assert.Equal(0, only.PowerFrom);
        Assert.False(DinerBook.Snubs(only, 50));
        Assert.Equal(scenes[0].Kn5, book.SceneOf(only)!.Kn5);
        Assert.NotEmpty(book.NameOf(only));
    }

    // ----- the looks -----

    [Fact]
    public void A_diners_look_names_the_groups_it_shows_and_goes_over_the_streets_sun_lights_and_glow()
    {
        var mods = Mods("""
            { "diners": [ { "id": "yard", "street": "mainstreet", "powerFrom": 200, "look": {
                "show": [ "Yard", " shared ", "" ],
                "sun": [ 0.1, 0.9, 0.2 ],
                "lights": { "night": { "brightness": 0.33, "color": [ 255, 150, 80 ], "ambient": 0.5, "lampColor": [ 255, 160, 60 ],
                                       "lamps": [ { "x": 1, "y": 4, "z": -10, "color": [ 255, 0, 255 ], "brightness": 1, "range": 10 } ] } },
                "glow": { "night": { "sign_roof": 2.5 } }
            } } ] }
            """);
        var scenes = DinerScenes.LoadAll(mods);
        var street = Assert.Single(scenes);
        var book = DinerBook.Load(mods, scenes);
        var diner = book.Get("mymod:yard")!;

        Assert.NotNull(diner.Look);
        var dressed = book.SceneOf(diner)!;
        Assert.Equal(["LOOK_YARD", "LOOK_SHARED"], dressed.ShownLooks);
        Assert.Equal([0.1f, 0.9f, 0.2f], dressed.Sun);

        // The night is the look's, with the street's lamps kept (recoloured) and the look's own added; the day stays the street's
        var night = dressed.LightingFor(Services.Street.StreetLight.Night);
        Assert.Equal(0.33f, night.Brightness);
        Assert.Equal([255, 150, 80], night.Color);
        Assert.Equal(street.LightingFor(Services.Street.StreetLight.Night).Lamps.Count + 1, night.Lamps.Count);
        Assert.Equal([255, 0, 255], night.Lamps[^1].Color);
        Assert.Equal(10f, night.Lamps[^1].Range);
        Assert.All(night.Lamps.SkipLast(1), lamp => Assert.Equal([255, 160, 60], lamp.Color));
        Assert.Equal(street.LightingFor(Services.Street.StreetLight.Day).Brightness, dressed.LightingFor(Services.Street.StreetLight.Day).Brightness);

        // The glow goes over the street's material by material (the test's street has no model, so no glow of its own:
        // the base game's is checked below)
        Assert.Equal(2.5f, dressed.GlowAt(Services.Street.StreetLight.Night)["sign_roof"]);
        foreach (var (material, level) in street.GlowAt(Services.Street.StreetLight.Night))
            Assert.Equal(level, dressed.GlowAt(Services.Street.StreetLight.Night)[material]);
        Assert.Equal(street.GlowAt(Services.Street.StreetLight.Dusk), dressed.GlowAt(Services.Street.StreetLight.Dusk));

        // The base game's street model has no look groups: its diners' looks are pieces laid on it (step 39)
        Assert.Empty(Assert.Single(Services.Street.StreetScenes.LoadAll(BaseMods)).ShownLooks);
        Assert.Empty(book.SceneOf(book.First)!.ShownLooks);

        // The base game's Main Street, worn by its top diner: the street's glow is under the diner's, and the street's
        // lamps are all there, its own and its layout's lamp posts' (step 39), recoloured, with the Starlite's own after them
        var baseBook = Base;
        var state = new GameState { StreetSeed = 1968 };
        var streets = Services.Street.StreetScenes.LoadAll(BaseMods);
        var kit = Services.Street.StreetKit.Load(BaseMods, []);
        DinerScene Laid(DinerScene s) => Services.Street.StreetLayouts.Laid(s, state, baseBook.Top, streets, kit);
        var baseStreet = Laid(Assert.Single(DinerScenes.LoadAll(BaseMods)));
        var top = baseBook.SceneOf(baseBook.Top, Laid)!;
        var topLook = baseBook.Top!.Look!;
        foreach (var (material, level) in baseStreet.GlowAt(Services.Street.StreetLight.Night).Where(m => !topLook.Glow[Services.Street.StreetLight.Night].ContainsKey(m.Key)))
            Assert.Equal(level, top.GlowAt(Services.Street.StreetLight.Night)[material]);
        var streetLamps = baseStreet.LightingFor(Services.Street.StreetLight.Night).Lamps.Count;
        Assert.True(streetLamps > 10);
        Assert.All(top.LightingFor(Services.Street.StreetLight.Night).Lamps.Take(streetLamps),
            l => Assert.Equal(topLook.LampTints[Services.Street.StreetLight.Night], l.Color));
        Assert.Equal(streetLamps + topLook.Lights[Services.Street.StreetLight.Night].Lamps.Count, top.LightingFor(Services.Street.StreetLight.Night).Lamps.Count);
        Assert.NotEmpty(topLook.Lights[Services.Street.StreetLight.Night].Lamps);
    }

    [Fact]
    public void Every_base_diner_lays_its_look_on_main_streets_stage_from_the_street_kit()
    {
        var book = Base;
        var scene = Assert.Single(DinerScenes.LoadAll(BaseMods));
        var streets = Services.Street.StreetScenes.LoadAll(BaseMods);
        var kn5 = AcTools.Kn5File.Kn5.FromFile(scene.Kn5, AcTools.Kn5File.SkippingTextureLoader.Instance);
        var kit = Services.Street.StreetKit.Load(BaseMods, []);

        // The stage's materials and the street kit's pieces' (the signs stand on the street as pieces, step 39)
        var materials = kn5.Materials.Keys.Concat(kit.All
                .SelectMany(p => AcTools.Kn5File.Kn5.FromFile(p.Model, AcTools.Kn5File.SkippingTextureLoader.Instance).Materials.Keys))
            .ToHashSet(StringComparer.Ordinal);
        var nodes = new List<string>();
        void Walk(AcTools.Kn5File.Kn5Node node)
        {
            nodes.Add(node.Name);
            foreach (var child in node.Children) Walk(child);
        }

        // The stage holds no look of any diner: they are all pieces now
        Walk(kn5.RootNode);
        Assert.DoesNotContain(nodes, n => n.StartsWith(Services.Street.SceneLooks.Prefix, StringComparison.Ordinal));

        // What only one diner has, by the diner
        var own = new Dictionary<string, string[]>
        {
            ["mainstreet"] = ["sign_roof", "billboard_ad"],
            ["hiway"] = ["sign_roof_hiway", "billboard_ad_hiway", "wrecker", "heavy_tow_truck", "hiway_drums", "hiway_fence"],
            ["surfside"] = ["sign_roof_surfside", "billboard_ad_surfside", "surfside_pennants", "surfside_surfboards"],
            ["starlite"] = ["sign_roof_starlite", "billboard_ad_starlite", "starlite_neon"]
        };
        Assert.Equal(own.Keys, book.All.Select(d => d.Id));
        Assert.Empty(Services.Street.StreetLayouts.UnknownWords(streets, kit, book.All));

        var state = new GameState { StreetSeed = 1968 };
        foreach (var diner in book.All)
        {
            Assert.NotNull(diner.Look?.Layout);
            var laid = Services.Street.StreetLayouts.Laid(scene, state, diner, streets, kit).Pieces.Select(p => p.Piece).ToHashSet();

            // Its roof board, billboard and lot's props stand on its street, and no other diner's
            Assert.All(own[diner.Id], piece => Assert.Contains(piece, laid));
            Assert.All(own.Where(o => o.Key != diner.Id).SelectMany(o => o.Value), piece => Assert.DoesNotContain(piece, laid));

            // Every material the diner's look makes glow is in the model or a piece of the kit
            foreach (var material in diner.Look!.Glow.Values.SelectMany(g => g.Keys).Distinct())
                Assert.True(materials.Contains(material), $"{diner.Id}: {material}");
        }

        // Each upper diner's street is its own: laid out apart from the street's, and weighted to its kind
        Assert.Equal(["mainstreet", "hiway", "surfside", "starlite"], state.Streets.Select(s => s.Diner));
        Assert.Equal(4, state.Streets.Select(s => s.Seed).Distinct().Count());
        Assert.Equal(3f, book.Get("hiway")!.Look!.Layout!.Weights["industrial"]);
        Assert.Equal(3f, book.Get("surfside")!.Look!.Layout!.Weights["beach"]);
        Assert.Equal(3f, book.Get("starlite")!.Look!.Layout!.Weights["googie"]);
    }

    [Fact]
    public void A_look_groups_names_become_node_prefixes_in_capitals()
    {
        Assert.Equal(["LOOK_HIWAY", "LOOK_SURF_SIDE"], Services.Street.SceneLooks.Prefixes(["hiway", " Surf_Side ", null, "", "HIWAY"]));
        Assert.Empty(Services.Street.SceneLooks.Prefixes(null));

        // A group is its own nodes, not another group's whose name starts the same
        Assert.True(Services.Street.SceneLooks.IsIn("LOOK_STAR", "LOOK_STAR"));
        Assert.True(Services.Street.SceneLooks.IsIn("LOOK_STAR_sign", "LOOK_STAR"));
        Assert.False(Services.Street.SceneLooks.IsIn("LOOK_STARLITE_neon", "LOOK_STAR"));
        Assert.False(Services.Street.SceneLooks.IsIn("ROAD", "LOOK_STAR"));
    }

    [Fact]
    public void A_looks_light_with_no_brightness_keeps_the_streets_light_and_only_recolours_its_lamps()
    {
        var mods = Mods("""
            { "diners": [ { "id": "amber", "street": "mainstreet", "powerFrom": 200, "look": {
                "lights": { "night": { "lampColor": [ 255, 160, 60 ] } }
            } } ] }
            """);

        // Over the base game's Main Street, with its model and lamps
        var scenes = DinerScenes.LoadAll(BaseMods);
        var street = Assert.Single(scenes);
        var book = DinerBook.Load(mods, scenes);
        var dressed = book.SceneOf(book.Get("mymod:amber"))!;

        var night = dressed.LightingFor(Services.Street.StreetLight.Night);
        var streets = street.LightingFor(Services.Street.StreetLight.Night);
        Assert.Equal(streets.Brightness, night.Brightness);
        Assert.Equal(streets.Ambient, night.Ambient);
        Assert.Equal(streets.Lamps.Count, night.Lamps.Count);
        Assert.NotEmpty(night.Lamps);
        Assert.All(night.Lamps, lamp => Assert.Equal([255, 160, 60], lamp.Color));
    }

    [Fact]
    public void The_cruise_street_wears_the_look_of_the_diner_the_player_came_out_of_on_its_street()
    {
        var book = Base;
        var street = Assert.Single(Services.Street.StreetScenes.LoadAll(BaseMods));
        var hiway = book.Get("hiway")!;

        var dressed = book.DressedStreet(street, hiway);
        Assert.Equal(hiway.Look!.Sun, dressed.Sun);
        Assert.All(dressed.Lights[Services.Street.StreetLight.Night].Lamps.Take(street.Lights[Services.Street.StreetLight.Night].Lamps.Count),
            lamp => Assert.Equal(hiway.Look.LampTints[Services.Street.StreetLight.Night], lamp.Color));

        // Out of no diner, the street as it is; a curb of it with no diner to go by wears the first diner's look, and out
        // of a diner seen on it, that diner's
        Assert.Same(street, book.DressedStreet(street, null));
        Assert.Equal("mainstreet", book.WornBy(street, null)!.Id);
        Assert.Equal("hiway", book.WornBy(street, hiway)!.Id);
    }


    [Fact]
    public void A_diners_look_layout_is_read_with_its_weights_and_a_weight_that_is_no_factor_is_told()
    {
        var mods = Mods("""
            { "diners": [
                { "id": "yard", "powerFrom": 600, "look": { "layout": {
                    "weights": { "industrial": 4, "beach": -1 },
                    "spots": [ { "at": [ 1, 2 ], "heading": 90, "take": [ "wrecker" ] }, { "heading": 0 } ] } } }
            ] }
            """);
        var book = DinerBook.Load(mods, DinerScenes.LoadAll(mods));
        var layout = book.Get("mymod:yard")!.Look!.Layout!;

        Assert.Equal(4f, layout.Weights["industrial"]);
        Assert.False(layout.Weights.ContainsKey("beach"));
        Assert.Equal(["wrecker"], Assert.Single(layout.Spots).Rules.Choice.Take);
        Assert.Contains(book.Problems, p => p.Contains("yard", StringComparison.Ordinal) && p.Contains("beach", StringComparison.Ordinal));
        Assert.Contains(book.Problems, p => p.Contains("yard", StringComparison.Ordinal) && p.Contains("spot 2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_way_to_the_diner_that_names_none_or_one_that_is_gone_leads_to_the_first()

    {
        var book = Four();

        Assert.Equal("mainstreet", book.GetOrFirst(null)!.Id);
        Assert.Equal("mainstreet", book.GetOrFirst("closed_down")!.Id);
        Assert.Equal("surfside", book.GetOrFirst("SURFSIDE")!.Id);
    }
}
