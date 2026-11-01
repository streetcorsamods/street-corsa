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

    /// <summary>The core with <see cref="Avenue"/> and its four diners, and nothing else</summary>
    private ModSet BaseMods
    {
        get
        {
            Avenue(_temp);
            return ModSet.Load(_temp.Path);
        }
    }

    private DinerBook Base => DinerBook.Load(BaseMods, DinerScenes.LoadAll(BaseMods));

    /// <summary>
    /// A made-up street with a diner on it, as a setting has one: "avenue" (street.json, the Cruise screen's, and
    /// diner.json, the diner's, sharing its sun, lights and glow; a recipe to lay it out by; a stand-in for a model), and, with <paramref name="diners"/>, four
    /// diners by level in diners.json, the first on the avenue and the three above on streets not built, so seen there:
    /// the truck stop with a sun and a lamp colour of its own, the drive-in at the top with its own lamp and glow
    /// </summary>
    internal static void Avenue(TempDir temp, bool diners = true, string mod = ModIds.BaseId, string street = "avenue")
    {
        temp.File($"{mod}/mod.json", $$"""{ "id": "{{mod}}" }""");
        temp.File($"{mod}/streets/{street}/street.json", $$"""
            { "id": "{{street}}", "title": "The Avenue", "model": "street.kn5",
              "player": { "x": 30, "z": -4, "heading": -90 }, "laneOffset": 3, "approachFrom": 45, "leaveTo": 70,
              "sun": [ 0.3, 0.6, 0.7 ],
              "layout": { "rows": [ { "from": [ 20, -9 ], "to": [ 60, -9 ], "heading": 0, "take": [ "shop" ] } ] },
              "lights": {
                "day": { "brightness": 1.5, "color": [ 220, 210, 190 ], "ambient": 2 },
                "dusk": { "brightness": 0.8, "color": [ 255, 160, 110 ], "ambient": 1 },
                "night": { "brightness": 0.2, "color": [ 150, 170, 230 ], "ambient": 0.8, "lamps": [
                  { "x": -8, "y": 3.5, "z": -13, "color": [ 255, 220, 170 ], "brightness": 1, "range": 14 },
                  { "x": 4, "y": 3.5, "z": -13, "color": [ 255, 220, 170 ], "brightness": 1, "range": 14 },
                  { "x": -20, "y": 6, "z": -10, "color": [ 255, 120, 150 ], "brightness": 0.7, "range": 10 } ] } },
              "glow": { "dusk": { "sign_roof": 0.6, "window": 0.3 }, "night": { "sign_roof": 1.1, "window": 0.6 } } }
            """);
        // A stand-in for its model, which the Cruise screen's street needs to be there; nothing here reads it
        temp.File($"{mod}/streets/{street}/street.kn5", "not a model");
        temp.File($"{mod}/streets/{street}/diner.json", $$"""
            { "id": "{{street}}", "model": "street.kn5",
              "arrive": [ [ -150, 1.5 ], [ -40, 1.5 ], [ -10, 3.5 ], [ -6, 4.5 ], [ -2, 4.5 ] ],
              "backOut": [ [ -2, 4.5 ], [ -3.6, 4.5 ] ],
              "leave": [ [ -3.6, 4.5 ], [ 10, 2 ], [ 150, 1.5 ] ],
              "lookWhileParked": 80, "lookUpWhileParked": 10,
              "bays": [ { "x": 1.5, "z": -12, "heading": 180 }, { "x": -1.5, "z": -12, "heading": 180 } ] }
            """);
        if (!diners) return;

        temp.File($"{mod}/diners/diners.json", $$"""
            { "diners": [
                { "id": "corner", "street": "{{street}}", "powerFrom": 0, "stakes": 1, "mapX": 0.3, "mapY": 0.6,
                  "look": { "show": [ "corner" ], "layout": { "spots": [ { "at": [ -2, -20 ], "heading": 0, "take": [ "sign_roof" ] } ] } } },
                { "id": "truckstop", "street": "truckstop", "powerFrom": 220, "stakes": 1.5, "mapX": 0.35, "mapY": 0.65,
                  "look": { "show": [ "truckstop" ], "sun": [ 0.2, 0.8, 0.5 ], "lights": { "night": { "lampColor": [ 255, 160, 60 ] } },
                            "layout": { "weights": { "industrial": 8 } } } },
                { "id": "beach", "street": "beach", "powerFrom": 300, "stakes": 2, "mapX": 0.2, "mapY": 0.6,
                  "look": { "show": [ "beach" ], "layout": { "weights": { "beach": 8 } } } },
                { "id": "drivein", "street": "drivein", "powerFrom": 380, "stakes": 3, "mapX": 0.25, "mapY": 0.5,
                  "look": { "show": [ "drivein" ], "glow": { "night": { "neon": 2 } },
                            "lights": { "night": { "lampColor": [ 200, 220, 255 ],
                                                   "lamps": [ { "x": -30, "y": 5, "z": -20, "color": [ 120, 255, 200 ], "brightness": 1, "range": 12 } ] } },
                            "layout": { "weights": { "googie": 8 } } } }
            ] }
            """);
    }

    /// <summary>Four diners by level, over no streets</summary
    private static DinerBook Four() => DinerBook.From(
    [
        new Diner { Id = "drivein", PowerFrom = 380, Stakes = 3 },
        new Diner { Id = "corner", PowerFrom = 0 },
        new Diner { Id = "beach", PowerFrom = 300, Stakes = 2 },
        new Diner { Id = "truckstop", PowerFrom = 220, Stakes = 1.5m }
    ]);

    private static Opponent Racer(string name, double? powerHp, bool boss = false)
    {
        var racer = new Opponent(name, 30, Gender.Male, 95, 50) { IsBoss = boss };
        racer.Cars.Add(new Car("car") { PowerHp = powerHp });
        return racer;
    }

    private static RacerOut Out(Opponent racer, string? bhp = null) =>
        new(racer, racer.Cars[0], new CarDefinition { Id = "car", Specs = bhp == null ? null : new CarSpecsData { Bhp = bhp } });

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
        Avenue(_temp, diners: false, mod: "mymod", street: "myblock");
        var mods = Mods(null);
        var scenes = DinerScenes.LoadAll(mods);
        var book = DinerBook.Load(mods, scenes);
        var mine = Assert.Single(scenes, s => s.Id.StartsWith("mymod", StringComparison.Ordinal));

        Assert.Equal("corner", book.First!.Id);
        Assert.Equal(mine.Id, book.First.Street);
        Assert.Equal(mine.Folder, book.SceneOf(book.First)!.Folder);
        Assert.Null(book.First.Look);

        // The diners whose streets are not built are seen there too; the drive-in keeps its own look
        Assert.Equal(mine.Folder, book.SceneOf(book.Get("drivein"))!.Folder);
        Assert.NotNull(book.Get("drivein")!.Look?.Layout);

        // Its layout and way in are written on the avenue: on the mod's street it comes in as that street says, and the
        // street is laid out as its own recipe says
        Assert.Equal("avenue", book.Get("drivein")!.Look!.Street);
        Assert.Equal(mine.Arrive, book.SceneOf(book.Get("drivein"))!.Arrive);
        Assert.False(book.Get("drivein")!.Look!.FitsOn(mine.Id));
    }

    [Fact]
    public void A_car_belongs_at_the_highest_diner_whose_floor_it_reaches()
    {
        var book = Four();

        Assert.Equal("corner", book.Of(150)!.Id);
        Assert.Equal("corner", book.Of(219.9)!.Id);
        Assert.Equal("truckstop", book.Of(220)!.Id);
        Assert.Equal("truckstop", book.Of(299.9)!.Id);
        Assert.Equal("beach", book.Of(300)!.Id);
        Assert.Equal("drivein", book.Of(380)!.Id);
        Assert.Equal("drivein", book.Of(1000)!.Id);

        // No figure, or no figure that is one: the beginners' diner
        Assert.Equal("corner", book.Of(null)!.Id);
        Assert.Equal("corner", book.Of(double.NaN)!.Id);
        Assert.Equal("corner", book.Of(-5)!.Id);
        Assert.Null(DinerBook.From([]).Of(300));
    }

    [Fact]
    public void The_regulars_of_a_diner_above_the_players_car_will_not_race_it_and_the_first_diner_turns_nobody_away()
    {
        var book = Four();
        var first = book.Get("corner")!;
        var truckStop = book.Get("truckstop")!;
        var top = book.Get("drivein")!;

        Assert.False(DinerBook.Snubs(first, 0));
        Assert.False(DinerBook.Snubs(first, null));
        Assert.True(DinerBook.Snubs(truckStop, 150));
        Assert.True(DinerBook.Snubs(truckStop, 219.9));
        Assert.False(DinerBook.Snubs(truckStop, 220));
        Assert.False(DinerBook.Snubs(truckStop, 400));

        // A car the game has no figure for is not turned away for what nobody has measured
        Assert.False(DinerBook.Snubs(top, null));
        Assert.False(DinerBook.Snubs(top, double.NaN));
        Assert.True(DinerBook.Snubs(top, 200));

        // The Boss takes on anybody at his table, and a rival out for a rematch wants it whatever the player drives
        Assert.True(DinerBook.Snubs(top, Racer("regular", 400), 200));
        Assert.False(DinerBook.Snubs(top, Racer("boss", 400, boss: true), 200));
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
            Out(Racer("The Boss", 100, boss: true)),
            // No dyno figure: the spec sheet's power stands in
            Out(Racer("Spec sheet", null), bhp: "350bhp"),
            Out(Racer("Unknown", null))
        };

        Assert.Equal(["Six", "Unknown"], RacersOut.AtDiner(today, book.Get("corner")!, book).Select(r => r.Opponent.Name));
        Assert.Equal(["Small block"], RacersOut.AtDiner(today, book.Get("truckstop")!, book).Select(r => r.Opponent.Name));
        Assert.Equal(["Spec sheet"], RacersOut.AtDiner(today, book.Get("beach")!, book).Select(r => r.Opponent.Name));
        Assert.Equal(["Big block", "The Boss"], RacersOut.AtDiner(today, book.Get("drivein")!, book).Select(r => r.Opponent.Name));
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
        Avenue(_temp, baseDiners);
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
                { "replaces": "truckstop", "street": "avenue", "powerFrom": 200, "stakes": 1.2 },
                { "id": "top", "powerFrom": 500, "stakes": 5 }
            ] }
            """);
        var scenes = DinerScenes.LoadAll(mods);
        var book = DinerBook.Load(mods, scenes);

        Assert.Equal(["corner", "truckstop", "beach", "drivein", "mymod:top"], book.All.Select(d => d.Id));
        Assert.Equal(200, book.Get("truckstop")!.PowerFrom);
        Assert.Equal("mymod", book.Get("truckstop")!.ModId);
        Assert.Equal(4, book.Top!.Level);
        Assert.Equal("mymod:top", book.Of(600)!.Id);

        // "avenue" from a mod means the core's street, as the mod has none of its own; the new top diner names
        // no street and is seen on the first's, without a word about it
        var avenue = Assert.Single(scenes);
        Assert.Equal(avenue.Kn5, book.SceneOf(book.Get("truckstop"))!.Kn5);
        Assert.Null(book.Top.Street);
        Assert.Equal(avenue.Kn5, book.SceneOf(book.Top)!.Kn5);
        Assert.Empty(book.Problems);

        // A diner keeps the look it lays over the shared street; one replaced without a look, or a new one, has none
        Assert.NotNull(book.Get("drivein")!.Look?.Layout);
        Assert.Null(book.Get("truckstop")!.Look);
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
            { "diners": [ { "id": "yard", "street": "avenue", "powerFrom": 200, "look": {
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

        // The glow goes over the street's material by material
        Assert.Equal(2.5f, dressed.GlowAt(Services.Street.StreetLight.Night)["sign_roof"]);
        foreach (var (material, level) in street.GlowAt(Services.Street.StreetLight.Night).Where(m => m.Key != "sign_roof"))
            Assert.Equal(level, dressed.GlowAt(Services.Street.StreetLight.Night)[material]);
        Assert.Equal(street.GlowAt(Services.Street.StreetLight.Dusk), dressed.GlowAt(Services.Street.StreetLight.Dusk));

        // The top diner seen on the avenue: the street's glow is under the diner's, and the street's lamps are all
        // there, recoloured, with the diner's own after them
        var top = book.SceneOf(book.Top)!;
        var topLook = book.Top!.Look!;
        foreach (var (material, level) in street.GlowAt(Services.Street.StreetLight.Night).Where(m => !topLook.Glow[Services.Street.StreetLight.Night].ContainsKey(m.Key)))
            Assert.Equal(level, top.GlowAt(Services.Street.StreetLight.Night)[material]);
        Assert.Equal(2f, top.GlowAt(Services.Street.StreetLight.Night)["neon"]);
        var streetLamps = street.LightingFor(Services.Street.StreetLight.Night).Lamps.Count;
        Assert.All(top.LightingFor(Services.Street.StreetLight.Night).Lamps.Take(streetLamps),
            l => Assert.Equal(topLook.LampTints[Services.Street.StreetLight.Night], l.Color));
        Assert.Equal(streetLamps + topLook.Lights[Services.Street.StreetLight.Night].Lamps.Count, top.LightingFor(Services.Street.StreetLight.Night).Lamps.Count);
        Assert.NotEmpty(topLook.Lights[Services.Street.StreetLight.Night].Lamps);
    }

    [Fact]
    public void A_look_groups_names_become_node_prefixes_in_capitals()
    {
        Assert.Equal(["LOOK_GRILL", "LOOK_SURF_SIDE"], Services.Street.SceneLooks.Prefixes(["grill", " Surf_Side ", null, "", "GRILL"]));
        Assert.Empty(Services.Street.SceneLooks.Prefixes(null));

        // A group is its own nodes, not another group's whose name starts the same
        Assert.True(Services.Street.SceneLooks.IsIn("LOOK_STAR", "LOOK_STAR"));
        Assert.True(Services.Street.SceneLooks.IsIn("LOOK_STAR_sign", "LOOK_STAR"));
        Assert.False(Services.Street.SceneLooks.IsIn("LOOK_STARBURST_neon", "LOOK_STAR"));
        Assert.False(Services.Street.SceneLooks.IsIn("ROAD", "LOOK_STAR"));
    }

    [Fact]
    public void A_looks_light_with_no_brightness_keeps_the_streets_light_and_only_recolours_its_lamps()
    {
        var mods = Mods("""
            { "diners": [ { "id": "amber", "street": "avenue", "powerFrom": 200, "look": {
                "lights": { "night": { "lampColor": [ 255, 160, 60 ] } }
            } } ] }
            """);

        // Over the avenue, with its lamps
        var scenes = DinerScenes.LoadAll(mods);
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
        var truckStop = book.Get("truckstop")!;

        var dressed = book.DressedStreet(street, truckStop);
        Assert.Equal(truckStop.Look!.Sun, dressed.Sun);
        Assert.All(dressed.Lights[Services.Street.StreetLight.Night].Lamps.Take(street.Lights[Services.Street.StreetLight.Night].Lamps.Count),
            lamp => Assert.Equal(truckStop.Look.LampTints[Services.Street.StreetLight.Night], lamp.Color));

        // Out of no diner, the street as it is; a curb of it with no diner to go by wears the first diner's look, and out
        // of a diner seen on it, that diner's
        Assert.Same(street, book.DressedStreet(street, null));
        Assert.Equal("corner", book.WornBy(street, null)!.Id);
        Assert.Equal("truckstop", book.WornBy(street, truckStop)!.Id);
    }


    [Fact]
    public void A_diner_driven_to_another_way_comes_in_parks_and_looks_as_its_look_says_and_a_way_that_goes_nowhere_is_told()
    {
        var mods = Mods("""
            { "diners": [
                { "id": "yard", "powerFrom": 600, "look": { "approach": {
                    "arrive": [ [ -37.5, -150 ], [ -40.5, -38 ] ], "backOut": [ [ -40.5, -38 ], [ -40.5, -39.6 ] ],
                    "leave": [ [ -40.5, -39.6 ], [ -37.5, 150 ] ], "lookWhileParked": 85, "lookUpWhileParked": 40 } } },
                { "id": "lost", "powerFrom": 700, "look": { "approach": { "arrive": [ [ 1, 1 ] ], "backOut": [], "leave": [] } } }
            ] }
            """);
        var scenes = DinerScenes.LoadAll(mods);
        var book = DinerBook.Load(mods, scenes);
        var street = Assert.Single(scenes);

        var yard = book.SceneOf(book.Get("mymod:yard"))!;
        Assert.Equal(new Services.Station.GroundPoint(-40.5f, -38f), yard.Arrive[^1]);
        Assert.Equal(new Services.Station.GroundPoint(-40.5f, -38f), yard.ScriptMarks.Paths["arrive"][^1]);
        Assert.Equal(-39.6f, yard.BackOut[^1].Z);
        Assert.Equal(150f, yard.Leave[^1].Z);
        Assert.Equal(85f, yard.LookWhileParked);
        Assert.Equal(15f, yard.LookUpWhileParked);

        // A way that goes nowhere is told, and the diner is driven to as its street says
        Assert.Null(book.Get("mymod:lost")!.Look!.Approach);
        Assert.Contains(book.Problems, p => p.Contains("lost", StringComparison.Ordinal) && p.Contains("approach", StringComparison.Ordinal));
        Assert.Equal(street.Arrive, book.SceneOf(book.Get("mymod:lost"))!.Arrive);

        // The diners whose looks name no way in come in as the street says
        Assert.Null(book.First!.Look!.Approach);
        Assert.Equal(street.Arrive, book.SceneOf(book.First)!.Arrive);
        Assert.All(book.All, d => Assert.True(book.SceneOf(d)!.LookWhileParked > 0, d.Id));
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
        Assert.Contains(book.Problems, p => p.Contains("diners.json", StringComparison.Ordinal) && p.Contains("yard", StringComparison.Ordinal) && p.Contains("beach", StringComparison.Ordinal));
        Assert.Contains(book.Problems, p => p.Contains("diners.json", StringComparison.Ordinal) && p.Contains("yard", StringComparison.Ordinal) && p.Contains("spot 2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_look_whose_way_in_does_not_read_or_whose_layout_is_no_object_is_told_and_the_diner_kept()
    {
        var mods = Mods("""
            { "diners": [
                { "id": "yard", "powerFrom": 600, "look": { "layout": [ 1 ],
                    "approach": { "arrive": [ 1, 2 ], "backOut": [], "leave": [] } } }
            ] }
            """);
        var scenes = DinerScenes.LoadAll(mods);
        var book = DinerBook.Load(mods, scenes);
        var yard = book.Get("mymod:yard");

        Assert.NotNull(yard);
        Assert.Null(yard.Look!.Approach);
        Assert.Null(yard.Look.Layout);
        Assert.Equal(Assert.Single(scenes).Arrive, book.SceneOf(yard)!.Arrive);
        Assert.Contains(book.Problems, p => p.Contains("diners.json", StringComparison.Ordinal) && p.Contains("approach does not read", StringComparison.Ordinal));
        Assert.Contains(book.Problems, p => p.Contains("diners.json", StringComparison.Ordinal) && p.Contains("layout is no object", StringComparison.Ordinal));
    }

    [Fact]
    public void A_saves_layouts_no_screen_lays_out_any_more_are_taken_out_and_the_rest_kept()
    {
        var book = Base;
        var streets = Services.Street.StreetScenes.LoadAll(BaseMods);
        var main = Assert.Single(streets).Id;
        var state = new GameState
        {
            Streets = book.All.Select(d => new StreetLayoutState { Street = main, Diner = d.Id })
                .Append(new StreetLayoutState { Street = main, Diner = "" })
                .Append(new StreetLayoutState { Street = main, Diner = "mymod:gone" })
                .Append(new StreetLayoutState { Street = "mymod:block", Diner = "" })
                .ToList()
        };

        // Every diner on the avenue lays its own: the street's own layout is wanted no more, nor the gone ones
        Assert.True(Services.Street.StreetLayouts.Prune(state, streets, book));
        Assert.Equal(book.All.Select(d => d.Id), state.Streets.Select(s => s.Diner));
        Assert.False(Services.Street.StreetLayouts.Prune(state, streets, book));
    }

    [Fact]
    public void A_way_to_the_diner_that_names_none_or_one_that_is_gone_leads_to_the_first()

    {
        var book = Four();

        Assert.Equal("corner", book.GetOrFirst(null)!.Id);
        Assert.Equal("corner", book.GetOrFirst("closed_down")!.Id);
        Assert.Equal("beach", book.GetOrFirst("BEACH")!.Id);
    }
}
