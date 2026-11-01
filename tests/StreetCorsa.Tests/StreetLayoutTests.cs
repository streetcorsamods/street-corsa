using System.Numerics;
using System.Text.Json;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// Step 39, phase 2: the streets laid out at random from a kit. A recipe's slots filled with the kit's pieces from a seed,
/// the same seed giving the same street; nothing in a clear area or on top of another; the layout kept in the save.
/// </summary>
public sealed class StreetLayoutTests
{
    private static StreetPiece Piece(string id, float width, float depth, params string[] tags) =>
        new(id, id + ".kn5", width, depth, 3f, tags.ToHashSet(StringComparer.OrdinalIgnoreCase), 1f);

    private static StreetKit Kit() => StreetKit.From(
    [
        Piece("shop_a", 12f, 10f, "shop"),
        Piece("shop_b", 8f, 14f, "shop"),
        Piece("corner", 15f, 15f, "shop", "corner_shop"),
        Piece("sedan", 1.9f, 5.2f, "curb_car"),
        Piece("pickup", 2f, 5.6f, "curb_car", "truck"),
        Piece("palm", 6f, 6f, "palm"),
        Piece("palm_card", 6f, 0.1f, "palm_card"),
        Piece("lamp", 3f, 0.4f, "lamp") with { Lamp = new Vector3(0f, 9.5f, 2.9f) }
    ]);

    private static StreetRecipe Recipe(string json)
    {
        var problems = new List<string>();
        using var document = JsonDocument.Parse(json);
        var recipe = StreetRecipe.Read(document.RootElement.Clone(), "test", problems);
        Assert.Empty(problems);
        return recipe!;
    }

    private const string Shops = """
        {
          "clear": [ { "rect": [ 20, -12, 30, 12 ] }, { "rect": [ -10, 2, 0, 7 ], "only": [ "curb_car" ] } ],
          "rows": [
            { "from": [ -100, -9 ], "to": [ 100, -9 ], "heading": 0, "take": [ "shop" ], "first": [ "corner_shop" ], "gap": [ 0.4, 2 ], "empty": 0.1 },
            { "from": [ -100, 4.5 ], "to": [ 100, 4.5 ], "heading": 90, "take": [ "curb_car" ], "align": "middle", "gap": [ 1, 4 ], "empty": 0.3 }
          ],
          "runs": [
            { "from": [ -90, 7.6 ], "to": [ 90, 7.6 ], "every": 30, "heading": 180, "take": [ "lamp" ], "nudge": 9 },
            { "from": [ -95, -7.2 ], "to": [ 95, -7.2 ], "every": 20, "heading": 0, "take": [ "palm" ], "far": [ "palm_card" ],
              "near": 40, "nearAt": [ 0, 0 ], "turn": 360, "scale": [ 0.85, 1.15 ] }
          ],
          "spots": [ { "at": [ 5, 5 ], "heading": 0, "take": [ "pickup" ] } ]
        }
        """;

    private static (Vector2 Min, Vector2 Max) BoxOf(StreetKit kit, PlacedPiece placed) =>
        StreetLayout.Box(kit.Find(placed.Piece)!, new Vector2(placed.X, placed.Z), placed.Heading, placed.Scale);

    private static bool Overlap((Vector2 Min, Vector2 Max) a, (Vector2 Min, Vector2 Max) b, float give = 0.1f) =>
        a.Min.X + give < b.Max.X && b.Min.X + give < a.Max.X && a.Min.Y + give < b.Max.Y && b.Min.Y + give < a.Max.Y;

    // ----- the generator -----

    [Fact]
    public void The_same_seed_lays_the_street_out_the_same_and_another_seed_otherwise()
    {
        var kit = Kit();
        var recipe = Recipe(Shops);

        var once = StreetLayout.Generate(recipe, kit, 1968).Select(p => p.ToString()).ToList();
        var again = StreetLayout.Generate(recipe, kit, 1968).Select(p => p.ToString()).ToList();
        var other = StreetLayout.Generate(recipe, kit, 1969).Select(p => p.ToString()).ToList();

        Assert.NotEmpty(once);
        Assert.Equal(once, again);
        Assert.NotEqual(once, other);
    }

    [Fact]
    public void A_row_starts_with_its_first_pieces_and_its_shops_stand_side_by_side_behind_the_line_never_on_each_other()
    {
        var kit = Kit();
        for (var seed = 1; seed <= 40; seed++)
        {
            var layout = StreetLayout.Generate(Recipe(Shops), kit, seed);
            var shops = layout.Where(p => kit.Find(p.Piece)!.Tags.Contains("shop")).ToList();

            // The corner shop first, unless its slot drew empty: then the row's own pieces from its start
            Assert.True(shops[0].Piece == "corner" || shops[0].X > -100f + 15f - 0.01f, $"seed {seed}: {shops[0]}");
            foreach (var shop in shops)
            {
                var (min, max) = BoxOf(kit, shop);
                Assert.Equal(-9f, max.Y, 2);
                Assert.True(min.X >= -100.01f && max.X <= 100.01f, $"seed {seed}: {shop} out of its row");
            }

            for (var i = 0; i < layout.Count; i++)
            for (var j = i + 1; j < layout.Count; j++)
            {
                // The palms and the lamps need no room of their own (a crown, an arm over the parking lane), and the spot's
                // pickup is put where it is on purpose
                if (new[] { layout[i], layout[j] }.Any(p => kit.Find(p.Piece)!.Tags.Overlaps(["palm", "palm_card", "lamp"]) || p is { Piece: "pickup", X: 5f })) continue;
                Assert.False(Overlap(BoxOf(kit, layout[i]), BoxOf(kit, layout[j])), $"seed {seed}: {layout[i]} on {layout[j]}");
            }
        }
    }

    [Fact]
    public void Nothing_stands_in_a_clear_area_it_is_kept_clear_of_but_a_spot_put_there_on_purpose()
    {
        var kit = Kit();
        var recipe = Recipe(Shops);
        for (var seed = 1; seed <= 40; seed++)
        {
            var layout = StreetLayout.Generate(recipe, kit, seed);
            foreach (var placed in layout.Where(p => !(p.Piece == "pickup" && p.X == 5f)))
            {
                // A footprint may reach into a clear area by the 0.15 m it may touch another by
                var piece = kit.Find(placed.Piece)!;
                foreach (var area in recipe.Clear.Where(a => a.Holds(piece)))
                    Assert.False(Overlap(BoxOf(kit, placed), (area.Min, area.Max), give: 0.16f), $"seed {seed}: {placed} in a clear area");
            }

            // The spot's pickup is put where it is said to go, clear area or not
            Assert.Contains(layout, p => p.Piece == "pickup" && p.X == 5f && p.Z == 5f);
        }
    }

    [Fact]
    public void A_lamp_post_whose_spot_is_taken_goes_its_nudge_further_on_and_lights_the_road_at_its_arms_end()
    {
        var kit = Kit();
        var recipe = Recipe(Shops);
        var layout = StreetLayout.Generate(recipe, kit, 7);

        // The run's spots at -90, -60 ... 90; the one at 30 is in the clear area from 20 to 30, so it goes on to 39
        var lamps = layout.Where(p => p.Piece == "lamp").Select(p => p.X).ToList();
        Assert.Equal([-90f, -60f, -30f, 0f, 39f, 60f, 90f], lamps);

        // Facing 180 (-z), the arm reaches 2.9 m over the road from the post at z 7.6
        var lit = StreetLayout.Lamps(layout, kit, recipe.Lamp);
        Assert.Equal(7, lit.Count);
        Assert.All(lit, l => Assert.Equal(7.6f - 2.9f, l.Z, 3));
        Assert.All(lit, l => Assert.Equal(9.5f, l.Y, 3));
        Assert.Equal(recipe.Lamp.Range, lit[0].Range);
    }

    [Fact]
    public void A_palm_near_the_player_is_the_tree_and_one_further_off_its_card_scaled_to_the_trees_height()
    {
        var kit = StreetKit.From([Piece("palm", 6f, 6f, "palm") with { Height = 17f }, Piece("palm_card", 6f, 0.1f, "palm_card") with { Height = 8.5f }]);
        var recipe = Recipe("""
            { "runs": [ { "from": [ -100, 0 ], "to": [ 100, 0 ], "every": 10, "heading": 0, "take": [ "palm" ], "far": [ "palm_card" ],
                          "near": 45, "nearAt": [ 0, 0 ], "height": 17 } ] }
            """);

        var layout = StreetLayout.Generate(recipe, kit, 3);

        Assert.Equal(21, layout.Count);
        Assert.All(layout, p => Assert.Equal(MathF.Abs(p.X) < 45f ? "palm" : "palm_card", p.Piece));
        Assert.All(layout, p => Assert.Equal(p.Piece == "palm" ? 1f : 2f, p.Scale));
    }

    [Fact]
    public void A_slot_with_nothing_the_kit_has_stays_empty_and_a_first_piece_the_kit_has_not_lets_the_row_go_on()
    {
        var kit = Kit();
        var recipe = Recipe("""
            { "rows": [ { "from": [ 0, 0 ], "to": [ 30, 0 ], "heading": 0, "take": [ "shop_a" ], "first": [ "harbor_breakfast" ] } ],
              "spots": [ { "at": [ 0, 20 ], "heading": 0, "take": [ "bench" ] } ] }
            """);

        var layout = StreetLayout.Generate(recipe, kit, 1);

        Assert.Equal(["shop_a", "shop_a"], layout.Select(p => p.Piece));
        Assert.Equal(6f, layout[0].X, 3);
    }

    [Fact]
    public void A_row_turns_scales_and_takes_its_far_pieces_as_a_run_does()
    {
        var kit = StreetKit.From([Piece("sedan", 2f, 5f, "curb_car"), Piece("van", 2f, 5f, "far_car")]);
        var recipe = Recipe("""
            { "rows": [ { "from": [ -100, 0 ], "to": [ 100, 0 ], "heading": 90, "align": "middle", "take": [ "curb_car" ], "far": [ "far_car" ],
                          "near": 40, "nearAt": [ 0, 0 ], "turn": 5, "scale": [ 0.9, 1.1 ], "gap": [ 1, 1 ] } ] }
            """);

        var layout = StreetLayout.Generate(recipe, kit, 11);

        Assert.True(layout.Count > 20, $"{layout.Count} cars");
        Assert.All(layout, p => Assert.InRange(p.Scale, 0.9f, 1.1f));
        Assert.All(layout, p => Assert.InRange(p.Heading, 85f, 95f));
        Assert.Contains(layout, p => p.Heading != 90f);
        Assert.Contains(layout, p => p.Scale != 1f);

        // A slot's far choice is by where the slot starts: the vans past 40 m of the middle, the sedans nearer
        Assert.Contains(layout, p => p.Piece == "sedan");
        Assert.All(layout.Where(p => p.Piece == "sedan"), p => Assert.True(MathF.Abs(p.X) < 40f + 6f, p.ToString()));
        Assert.All(layout.Where(p => MathF.Abs(p.X) > 46f), p => Assert.Equal("van", p.Piece));
    }

    [Fact]
    public void A_sliver_of_a_piece_or_a_line_thousands_of_kilometres_long_still_lays_out_in_a_moment()
    {
        var kit = StreetKit.From([Piece("sliver", 0.000001f, 0.000001f, "house")]);
        var recipe = Recipe("""
            { "rows": [ { "from": [ 0, 0 ], "to": [ 1e9, 0 ], "heading": 0, "take": [ "house" ], "gap": [ 0, 0 ] } ],
              "runs": [ { "from": [ 0, 50 ], "to": [ 1e9, 50 ], "every": 0.2, "heading": 0, "take": [ "house" ] } ] }
            """);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var layout = StreetLayout.Generate(recipe, kit, 1);

        Assert.InRange(layout.Count, 1, 4000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"{watch.Elapsed}");

        // The kit leaves such a sliver out of a mod in the first place
        Assert.True(StreetKit.MinSize > 0.01f);
    }

    [Fact]
    public void A_slot_left_empty_or_missing_its_piece_does_not_shift_the_draws_of_the_slots_after_it()
    {
        var kit = Kit();
        const string palms = """
            { "from": [ -95, -7.2 ], "to": [ 95, -7.2 ], "every": 20, "heading": 0, "take": [ "palm" ], "turn": 360, "scale": [ 0.85, 1.15 ], "empty": 0.3 }
            """;

        // A run of one spot first that the kit has a piece for, and one it has not: the palms after come out the same
        var with = StreetLayout.Generate(Recipe($$"""{ "runs": [ { "from": [ 0, 50 ], "to": [ 0, 50 ], "every": 1, "heading": 0, "take": [ "pickup" ] }, {{palms}} ] }"""), kit, 4);
        var without = StreetLayout.Generate(Recipe($$"""{ "runs": [ { "from": [ 0, 50 ], "to": [ 0, 50 ], "every": 1, "heading": 0, "take": [ "bench" ] }, {{palms}} ] }"""), kit, 4);

        Assert.Contains(with, p => p.Piece == "pickup");
        Assert.Equal(with.Where(p => p.Piece == "palm").Select(p => p.ToString()), without.Select(p => p.ToString()));
    }

    // ----- the recipe -----

    [Fact]
    public void A_word_takes_the_piece_of_its_id_and_every_piece_with_its_tag_so_a_mods_piece_named_as_a_tag_is_one_more()
    {
        var kit = StreetKit.From(Kit().All.Append(Piece("shop", 9f, 9f, "shop")).Append(Piece("palm_card_b", 6f, 0.1f, "palm")));

        var shops = new PieceChoice(["shop"]).In(kit).Select(p => p.Id).ToList();
        Assert.Equal(["corner", "shop", "shop_a", "shop_b"], shops.Order(StringComparer.Ordinal));

        // A piece of its id named as a first piece is that piece, and with it any piece tagged so
        Assert.Equal(["palm", "palm_card_b"], new PieceChoice(["palm"]).In(kit).Select(p => p.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["sedan"], new PieceChoice(["sedan"]).In(kit).Select(p => p.Id));
    }

    [Fact]
    public void A_recipes_word_that_is_no_piece_and_no_tag_is_told_as_a_problem_of_its_street()
    {
        var kit = Kit();
        var recipe = Recipe("""
            { "rows": [ { "from": [ 0, 0 ], "to": [ 30, 0 ], "heading": 0, "take": [ "shops", "shop" ], "first": [ "harbor_breakfast" ] } ],
              "runs": [ { "from": [ 0, 9 ], "to": [ 30, 9 ], "every": 10, "heading": 0, "take": [ "palm" ], "far": [ "palm_cards" ] } ],
              "spots": [ { "at": [ 0, 20 ], "heading": 0, "take": [ "sedan", "truck" ] } ] }
            """);

        Assert.Equal(["harbor_breakfast", "shops", "palm_cards"], recipe.Unknown(kit));

        var problems = StreetLayouts.UnknownWords([Street("base/test", recipe), Street("base/plain", null)], kit);
        var problem = Assert.Single(problems);
        Assert.Contains("base/test", problem);
        Assert.Contains("shops", problem);
    }

    [Fact]
    public void A_recipes_fingerprint_is_the_same_however_it_is_spaced_and_another_for_another_recipe()
    {
        var spaced = Recipe("""
            { "spots": [ { "at": [ 0, 0 ],
                           "heading": 0, "take": [ "palm" ] } ] }
            """);
        var tight = Recipe("""{"spots":[{"at":[0,0],"heading":0,"take":["palm"]}]}""");
        var other = Recipe("""{"spots":[{"at":[0,1],"heading":0,"take":["palm"]}]}""");

        Assert.NotEmpty(spaced.Fingerprint);
        Assert.Equal(spaced.Fingerprint, tight.Fingerprint);
        Assert.NotEqual(spaced.Fingerprint, other.Fingerprint);
    }

    [Fact]
    public void A_recipe_slot_the_game_cannot_use_is_told_and_left_out_and_the_rest_read()
    {
        using var document = JsonDocument.Parse("""
            { "rows": [ { "from": [ 0, 0 ], "to": [ 0, 0 ], "heading": 0, "take": [ "shop" ] },
                        { "from": [ 0, 0 ], "to": [ 10, 0 ], "heading": 0, "take": [ "shop" ] } ],
              "runs": [ { "from": [ 0, 0 ], "to": [ 10, 0 ], "every": 0, "heading": 0, "take": [ "palm" ] } ],
              "spots": [ { "at": [ 1 ], "heading": 0, "take": [ "palm" ] }, { "at": [ 1, 2 ], "heading": 0 } ],
              "clear": [ { "rect": [ 0, 0, 1 ] } ] }
            """);
        var problems = new List<string>();

        var recipe = StreetRecipe.Read(document.RootElement.Clone(), "test", problems)!;

        Assert.Single(recipe.Rows);
        Assert.Empty(recipe.Runs);
        Assert.Empty(recipe.Spots);
        Assert.Empty(recipe.Clear);
        Assert.Equal(5, problems.Count);
        Assert.Null(StreetRecipe.Read(null, "test", problems));
    }

    // ----- the save -----

    private static StreetScene Street(string id, StreetRecipe? recipe) =>
        new(id, id, ".", new StreetSpot(0f, 0f, 0f), 3f, 0.3f, 45f, 70f, [0.3f, 0.6f, 0.7f], new Dictionary<StreetLight, StreetLighting>()) { Recipe = recipe };

    [Fact]
    public void A_new_career_draws_its_streets_seed_and_a_street_is_laid_out_once_and_kept_in_the_save()
    {
        var state = GameState.CreateNew("Player");
        Assert.NotEqual(0, state.StreetSeed);

        var kit = Kit();
        var street = Street("base/avenue", Recipe(Shops));
        var first = StreetLayouts.For(state, street, kit).Select(p => p.ToString()).ToList();
        Assert.Single(state.Streets);
        Assert.Equal(StreetLayouts.SeedOf(state, street.Id), state.Streets[0].Seed);

        // A mod's pieces changed later move nothing in a street already laid out
        var fewer = StreetKit.From(kit.All.Where(p => p.Id != "palm_card"));
        Assert.Equal(first.Count(p => !p.StartsWith("palm_card", StringComparison.Ordinal)), StreetLayouts.For(state, street, fewer).Count);
        Assert.Equal(first, StreetLayouts.For(state, street, kit).Select(p => p.ToString()));

        // A recipe changed later (the street rebuilt) lays it out again, from the same seed
        var seed = state.Streets[0].Seed;
        var changed = Street("base/avenue", Recipe("""{ "spots": [ { "at": [ 0, 0 ], "heading": 0, "take": [ "palm" ] } ] }"""));
        var again = StreetLayouts.For(state, changed, kit);
        Assert.Equal(StreetLayout.Generate(changed.Recipe!, kit, seed).Select(p => p.ToString()), again.Select(p => p.ToString()));
        Assert.Equal(changed.Recipe!.Fingerprint, state.Streets[0].Recipe);
        Assert.Equal(seed, state.Streets[0].Seed);
        Assert.Single(state.Streets);

        // Each street of a save its own
        Assert.NotEqual(StreetLayouts.SeedOf(state, "base/avenue"), StreetLayouts.SeedOf(state, "base/other"));
        Assert.Empty(StreetLayouts.For(state, Street("base/plain", null), kit));
        Assert.Single(state.Streets);
    }

    [Fact]
    public void A_save_from_before_the_streets_seed_lays_its_street_out_from_its_name_the_same_every_load()
    {
        var kit = Kit();
        var street = Street("base/avenue", Recipe(Shops));
        var a = new GameState { SaveName = "Pablo's career" };
        var b = new GameState { SaveName = "Pablo's career" };
        var c = new GameState { SaveName = "Another career" };
        Assert.Equal(0, a.StreetSeed);

        var once = StreetLayouts.For(a, street, kit).Select(p => p.ToString()).ToList();

        Assert.NotEmpty(once);
        Assert.Equal(once, StreetLayouts.For(b, street, kit).Select(p => p.ToString()));
        Assert.NotEqual(once, StreetLayouts.For(c, street, kit).Select(p => p.ToString()));
    }

    [Fact]
    public void A_piece_no_mod_has_any_more_is_left_out_of_the_street_but_kept_in_the_save_for_when_its_mod_is_back()
    {
        var kit = Kit();
        var state = new GameState { StreetSeed = 5 };
        var street = Street("base/avenue", Recipe(Shops));
        var all = StreetLayouts.For(state, street, kit);
        Assert.Contains(all, p => p.Piece == "pickup");

        var without = StreetKit.From(kit.All.Where(p => p.Id != "pickup"));
        var drawn = StreetLayouts.For(state, street, without);

        Assert.DoesNotContain(drawn, p => p.Piece == "pickup");
        Assert.Equal(all.Count(p => p.Piece != "pickup"), drawn.Count);
        Assert.Contains(state.Streets[0].Pieces, p => p.Piece == "pickup");
    }

    [Fact]
    public void A_street_laid_out_while_the_kit_had_none_of_its_pieces_is_laid_out_again_once_it_has()
    {
        var state = new GameState { StreetSeed = 3 };
        var street = Street("base/avenue", Recipe(Shops));

        Assert.Empty(StreetLayouts.For(state, street, StreetKit.From([])));
        Assert.NotEmpty(StreetLayouts.For(state, street, Kit()));
        Assert.NotEmpty(state.Streets.Single().Pieces);
    }

    [Fact]
    public void A_layout_of_a_save_from_before_the_recipes_fingerprint_is_laid_out_again_once()
    {
        var state = new GameState { StreetSeed = 3 };
        var street = Street("base/avenue", Recipe(Shops));
        state.Streets.Add(new StreetLayoutState { Street = street.Id, Seed = 77, Pieces = [new PlacedPiece("sedan", 0f, 0f, 0f, 0f, 1f)] });

        var laid = StreetLayouts.For(state, street, Kit());

        Assert.Equal(StreetLayout.Generate(street.Recipe!, Kit(), 77).Select(p => p.ToString()), laid.Select(p => p.ToString()));
        Assert.Equal(street.Recipe!.Fingerprint, state.Streets.Single().Recipe);
    }

    [Fact]
    public void A_streets_layout_goes_through_the_save_and_comes_back_whole()
    {
        var state = new GameState { StreetSeed = 9 };
        StreetLayouts.For(state, Street("base/avenue", Recipe(Shops)), Kit());
        var mapper = Services.Storage.SaveMapper.Create();

        var back = mapper.ToObject<GameState>(mapper.ToDocument(state));

        Assert.Equal(9, back.StreetSeed);
        Assert.Equal(state.Streets[0].Pieces.Select(p => p.ToString()), back.Streets.Single().Pieces.Select(p => p.ToString()));
        Assert.Equal(state.Streets[0].Seed, back.Streets[0].Seed);
        Assert.Equal(state.Streets[0].Recipe, back.Streets[0].Recipe);

        // A piece's placement is worked out from its numbers, not saved
        var piece = mapper.ToDocument(state)["Streets"].AsArray[0]["Pieces"].AsArray[0].AsDocument;
        Assert.False(piece.ContainsKey(nameof(PlacedPiece.Placement)));
    }

    // ----- a diner's look laid over the street (phase 4) -----

    private const string Yard = """
        { "weights": { "truck": 50, "palm_card": 0 },
          "spots": [ { "at": [ 40, 30 ], "heading": 90, "take": [ "pickup" ], "y": 0.2 } ] }
        """;

    private static Models.Diners.Diner Diner(string id, StreetRecipe? layout) => new()
    {
        Id = id,
        Look = layout == null ? null : new Models.Diners.DinerLook([], null, new Dictionary<StreetLight, StreetLighting>(),
            new Dictionary<StreetLight, byte[]>(), new Dictionary<StreetLight, IReadOnlyDictionary<string, float>>()) { Layout = layout }
    };

    [Fact]
    public void A_recipes_weights_pick_a_tags_pieces_that_much_more_often_and_a_weight_of_0_never()
    {
        var kit = Kit();
        var plain = Recipe(Shops);
        var weighed = Recipe(Shops.Replace("\"clear\":", "\"weights\": { \"truck\": 50, \"palm_card\": 0 }, \"clear\":", StringComparison.Ordinal));

        // The pickups' share of the cars at the curb: about half as made, nearly all weighed 50 to 1
        double Pickups(StreetRecipe recipe)
        {
            var cars = Enumerable.Range(0, 20).SelectMany(seed => StreetLayout.Generate(recipe, kit, seed)).Where(p => p.Piece is "pickup" or "sedan").ToList();
            return cars.Count(p => p.Piece == "pickup") / (double)cars.Count;
        }

        Assert.InRange(Pickups(plain), 0.3, 0.7);
        Assert.True(Pickups(weighed) > 0.9, $"{Pickups(weighed):P0} pickups weighed");

        // A palm card weighed to 0 is never put: the far palms stay empty
        var cards = Enumerable.Range(0, 20).SelectMany(seed => StreetLayout.Generate(weighed, kit, seed)).ToList();
        Assert.DoesNotContain(cards, p => p.Piece == "palm_card");
        Assert.Contains(cards, p => p.Piece == "palm");
        Assert.Equal(50f, weighed.WeightOf(kit.Find("pickup")!));
        Assert.Equal(1f, weighed.WeightOf(kit.Find("sedan")!));
    }

    [Fact]
    public void A_diners_look_lays_its_spots_over_the_streets_recipe_with_its_weights_and_a_fingerprint_of_both()
    {
        var kit = Kit();
        var street = Recipe(Shops);
        var look = Recipe(Yard);
        var over = street.Over(look);

        Assert.Equal(street.Rows.Count, over.Rows.Count);
        Assert.Equal(street.Spots.Concat(look.Spots), over.Spots);
        Assert.Equal(street.Clear, over.Clear);
        Assert.Equal(street.Lamp, over.Lamp);
        Assert.Equal(50f, over.Weights["truck"]);
        Assert.NotEqual(street.Fingerprint, over.Fingerprint);
        Assert.NotEqual(look.Fingerprint, over.Fingerprint);
        Assert.Equal(over.Fingerprint, Recipe(Shops).Over(Recipe(Yard)).Fingerprint);

        // The look's spot stands where it says, at its height
        var laid = StreetLayout.Generate(over, kit, 7);
        Assert.Contains(laid, p => p is { Piece: "pickup", X: 40f, Z: 30f, Y: 0.2f, Heading: 90f });
    }

    [Fact]
    public void A_diner_with_a_look_of_its_own_sees_the_street_laid_out_for_it_apart_and_one_without_sees_the_streets()
    {
        var state = new GameState { StreetSeed = 1968 };
        var kit = Kit();
        var street = Street("base/avenue", Recipe(Shops));
        var yard = Diner("yard", Recipe(Yard));
        var plain = Diner("plain", null);

        var own = StreetLayouts.For(state, street, kit).Select(p => p.ToString()).ToList();
        var forYard = StreetLayouts.For(state, street, kit, yard).Select(p => p.ToString()).ToList();
        Assert.Equal(own, StreetLayouts.For(state, street, kit, plain).Select(p => p.ToString()));
        Assert.NotEqual(own, forYard);
        Assert.Contains(StreetLayouts.For(state, street, kit, yard), p => p is { Piece: "pickup", X: 40f, Z: 30f });


        // Kept apart in the save, each from its own seed, and the same every time it is wanted
        Assert.Equal(["", "yard"], state.Streets.Select(s => s.Diner));
        Assert.Equal(StreetLayouts.SeedOf(state, street.Id), state.Streets[0].Seed);
        Assert.Equal(StreetLayouts.SeedOf(state, street.Id, "yard"), state.Streets[1].Seed);
        Assert.NotEqual(state.Streets[0].Seed, state.Streets[1].Seed);
        Assert.Equal(forYard, StreetLayouts.For(state, street, kit, yard).Select(p => p.ToString()));
        Assert.Equal(2, state.Streets.Count);

        // The diner's scene and the Cruise screen's street are laid out alike for it
        Assert.Equal(forYard, StreetLayouts.Laid(street, state, yard, kit).Pieces.Select(p => p.ToString()));

        // Through the save and back, each layout found again by its diner
        var mapper = Services.Storage.SaveMapper.Create();
        var back = mapper.ToObject<GameState>(mapper.ToDocument(state));
        Assert.Equal(["", "yard"], back.Streets.Select(s => s.Diner));
        Assert.Equal(forYard, StreetLayouts.For(back, street, kit, yard).Select(p => p.ToString()));
        Assert.Equal(2, back.Streets.Count);

        // A word of the look's no piece is told as the diner's
        var problem = Assert.Single(StreetLayouts.UnknownWords([street], kit,
            [Diner("bad", Recipe("""{ "spots": [ { "at": [ 0, 0 ], "heading": 0, "take": [ "neon" ] } ] }"""))]));
        Assert.Contains("the diner bad's look", problem);
        Assert.Contains("neon", problem);
    }

    [Fact]
    public void A_looks_slot_of_a_streets_name_takes_its_place_and_its_drop_takes_the_streets_away()
    {
        var street = Recipe("""
            { "runs": [ { "name": "palms", "from": [ 0, 0 ], "to": [ 100, 0 ], "every": 20, "heading": 0, "take": [ "palm" ] },
                        { "name": "lamps", "from": [ 0, 8 ], "to": [ 100, 8 ], "every": 50, "heading": 0, "take": [ "lamp" ] },
                        { "from": [ 0, -8 ], "to": [ 100, -8 ], "every": 50, "heading": 0, "take": [ "lamp" ] } ],
              "spots": [ { "name": "sign", "at": [ 5, 5 ], "heading": 0, "take": [ "pickup" ] } ] }
            """);
        var look = Recipe("""
            { "drop": [ "sign" ],
              "runs": [ { "name": "palms", "from": [ 0, 0 ], "to": [ 100, 0 ], "every": 10, "heading": 0, "take": [ "palm_card" ] },
                        { "name": "extra", "from": [ 0, 20 ], "to": [ 10, 20 ], "every": 10, "heading": 0, "take": [ "palm" ] } ],
              "spots": [ { "at": [ 9, 9 ], "heading": 0, "take": [ "sedan" ] } ] }
            """);

        var over = street.Over(look);

        // The palms run is the look's, where the street's was; the lamps are the street's; the look's extra comes after
        Assert.Equal(["palms", "lamps", "", "extra"], over.Runs.Select(r => r.Rules.Name));
        Assert.Equal(10f, over.Runs[0].Every);
        Assert.Equal(["palm_card"], over.Runs[0].Rules.Choice.Take);
        Assert.Same(street.Runs[1], over.Runs[1]);

        // The street's sign is dropped; the look's own spot stands
        Assert.Equal(["sedan"], Assert.Single(over.Spots).Rules.Choice.Take);
        var laid = StreetLayout.Generate(over, Kit(), 3);
        Assert.DoesNotContain(laid, p => p.Piece == "pickup");
        Assert.Equal(11, laid.Count(p => p.Piece == "palm_card"));

        // A name the street has no slot of, two of the look's sharing one, a drop of none: told, each
        var odd = Recipe("""
            { "drop": [ "sign", "sing" ],
              "runs": [ { "name": "palms", "from": [ 0, 0 ], "to": [ 100, 0 ], "every": 10, "heading": 0, "take": [ "palm" ] },
                        { "name": "palms", "from": [ 0, 1 ], "to": [ 100, 1 ], "every": 10, "heading": 0, "take": [ "palm" ] } ],
              "spots": [ { "name": "lamps", "at": [ 9, 9 ], "heading": 0, "take": [ "sedan" ] } ] }
            """);
        Assert.Equal(["its run \"extra\" is no run of the street; added beside the street's"], street.NameProblems(look));
        var problems = street.NameProblems(odd);
        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("2 of its runs are named \"palms\"", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("its spot \"lamps\" is no spot", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("\"sing\"", StringComparison.Ordinal));
    }

    private static Services.Station.GroundPoint G(float x, float z) => new(x, z);

    [Fact]
    public void A_looks_way_in_keeps_the_curb_cars_clear_and_a_look_written_on_another_street_lays_nothing_there()
    {
        var kit = Kit();
        var street = Street("base/avenue", Recipe(Shops));
        var other = Street("mymod:block", Recipe(Shops));
        var way = new Models.Diners.DinerApproach([G(-100f, 4.5f), G(100f, 4.5f)], [G(100f, 4.5f), G(100f, 3f)], [G(100f, 3f), G(110f, 3f)], 0f, 0f);
        var yard = Diner("yard", Recipe(Yard));
        yard.Look = yard.Look! with { Street = "base/avenue", Approach = way };

        // Along its way the curb cars' row stands empty; the spots' pickups are put where they are on purpose
        var state = new GameState { StreetSeed = 7 };
        var laid = StreetLayouts.For(state, street, kit, yard);
        Assert.Contains(laid, p => p is { Piece: "pickup", X: 40f, Z: 30f });
        Assert.DoesNotContain(laid, p => kit.Find(p.Piece)!.Tags.Contains("curb_car") && p is not ({ X: 40f, Z: 30f } or { X: 5f, Z: 5f }));
        Assert.Contains(StreetLayouts.For(state, street, kit), p => kit.Find(p.Piece)!.Tags.Contains("curb_car") && p.Z == 4.5f);

        // The way moved is another recipe: the save lays the street out again
        var moved = Diner("yard", Recipe(Yard));
        moved.Look = moved.Look! with { Street = "base/avenue", Approach = way with { Arrive = [G(-100f, 0f), G(100f, 0f)] } };
        Assert.NotEqual(StreetLayouts.RecipeOf(street, yard)!.Fingerprint, StreetLayouts.RecipeOf(street, moved)!.Fingerprint);

        // A box every 4 m of a way at most, 2 m either side
        var boxes = StreetLayouts.ClearOf([G(0f, 0f), G(10f, 0f)]).ToList();
        Assert.Equal(3, boxes.Count);
        Assert.All(boxes, b => Assert.Equal((-2f, 2f), (b.Min.Y, b.Max.Y)));
        Assert.All(boxes, b => Assert.True(b.Only.SetEquals(["curb_car"])));

        // On a street it is not written on, the look lays nothing of its own: the street as it is, kept as the street's
        Assert.Same(other.Recipe, StreetLayouts.RecipeOf(other, yard));
        var elsewhere = new GameState { StreetSeed = 7 };
        Assert.Equal(StreetLayouts.For(elsewhere, other, kit).Select(p => p.ToString()), StreetLayouts.For(elsewhere, other, kit, yard).Select(p => p.ToString()));
        Assert.Equal([""], elsewhere.Streets.Select(s => s.Diner));
    }

    [Fact]
    public void A_painted_piece_stands_in_one_of_its_colours_or_as_made_and_one_with_no_paint_always_as_made()
    {
        var paint = new PiecePaint([new System.Text.RegularExpressions.Regex("(?i)wall")], [[200, 100, 50], [50, 100, 200], [240, 240, 240]], 0.8f);
        var kit = StreetKit.From(
        [
            Piece("shop_a", 12f, 10f, "shop") with { Paint = paint },
            Piece("shop_b", 8f, 14f, "shop")
        ]);
        var recipe = Recipe("""{ "rows": [ { "from": [ 0, 0 ], "to": [ 4000, 0 ], "heading": 0, "take": [ "shop" ] } ] }""");

        var laid = StreetLayout.Generate(recipe, kit, 11);
        var painted = laid.Where(p => p.Piece == "shop_a").GroupBy(p => p.Paint).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal([0, 1, 2, 3], painted.Keys.Order());
        Assert.All(painted.Values, n => Assert.True(n > 15, $"{n}"));
        Assert.All(laid.Where(p => p.Piece == "shop_b"), p => Assert.Equal(0, p.Paint));
        Assert.True(paint.Takes("midcity_Walls_FLR_1"));
        Assert.False(paint.Takes("glass"));

        // The paint goes through the save
        var state = new GameState { Streets = [new StreetLayoutState { Street = "base/test", Pieces = laid }] };
        var mapper = Services.Storage.SaveMapper.Create();
        var back = mapper.ToObject<GameState>(mapper.ToDocument(state));
        Assert.Equal(laid.Select(p => p.Paint), back.Streets[0].Pieces.Select(p => p.Paint));
    }

    [Fact]
    public void A_picture_painted_keeps_its_light_and_shade_and_its_see_through_and_takes_the_colour_by_the_amount()
    {
        // Two pixels: a light one and a dark one, of a grey stucco (red, green, blue, alpha), and one see-through
        byte[] Picture() => [200, 200, 200, 255, 100, 100, 100, 255, 10, 20, 30, 0];
        byte[] colour = [240, 120, 60];

        var whole = Picture();
        StreetPaint.Tint(whole, colour, 1f);
        // The middle tone (150) is the colour; the light pixel a third lighter, the dark one a third darker
        Assert.Equal([255, 160, 80], whole[..3]);
        Assert.Equal([160, 80, 40], whole[4..7]);
        Assert.Equal(255, whole[3]);
        Assert.Equal(0, whole[11]);

        var none = Picture();
        StreetPaint.Tint(none, colour, 0f);
        Assert.Equal(Picture(), none);

        var half = Picture();
        StreetPaint.Tint(half, colour, 0.5f);
        Assert.Equal([228, 180, 140], half[..3]);

        Assert.Equal("house_siding_0_diffuse_paint_f0783c.jpg", StreetPaint.NameOf("house_siding_0_diffuse.jpg", colour));
    }

    [Fact]
    public void A_pieces_paint_is_read_from_its_file_and_one_with_no_colour_or_a_pattern_that_is_none_is_told()
    {
        using var temp = new TempDir();
        temp.File("streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        void Piece(string id, string paint)
        {
            temp.File($"streetcorsa/streetkit/{id}/{id}.kn5", "kn5");
            temp.File($"streetcorsa/streetkit/{id}/piece.json",
                $$"""{ "footprint": [ 10, 10 ], "height": 6, "tags": [ "shop" ], "credit": "test", "paint": {{paint}} }""");
        }

        Piece("good", """{ "materials": [ "Facade", "Walls$" ], "colours": [ [ 238, 226, 196 ], [ 300, -5, 9 ] ], "amount": 0.6 }""");
        Piece("pale", """{ "materials": [ "Facade" ], "colours": [] }""");
        Piece("bad", """{ "materials": [ "[" ], "colours": [ [ 1, 2, 3 ] ] }""");
        var problems = new List<string>();
        var kit = StreetKit.Load(ModSet.Load(temp.Path), problems);

        var good = kit.Find("good")!.Paint!;
        Assert.Equal(2, good.Colours.Count);
        Assert.Equal([255, 0, 9], good.Colours[1]);
        Assert.Equal(0.6f, good.Amount);
        Assert.True(good.Takes("midcity_Midcity_Facade_FLR_1"));
        Assert.False(good.Takes("midcity_Walls_FLR_1_glass"));
        Assert.Null(kit.Find("pale")!.Paint);
        Assert.Null(kit.Find("bad")!.Paint);
        Assert.Contains(problems, p => p.Contains("pale", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("bad", StringComparison.Ordinal) && p.Contains("no pattern", StringComparison.Ordinal));
    }

    // ----- the daily slots (phase 5) -----

    private static readonly string DailyShops = Shops.Replace("\"align\": \"middle\", \"gap\": [ 1, 4 ], \"empty\": 0.3 }",
        "\"align\": \"middle\", \"gap\": [ 1, 4 ], \"empty\": 0.3, \"daily\": true }", StringComparison.Ordinal);

    private static bool IsCar(StreetKit kit, PlacedPiece placed) => kit.Find(placed.Piece)!.Tags.Contains("curb_car");

    [Fact]
    public void A_daily_slot_is_read_and_left_out_of_the_kept_layout_the_rest_of_the_street_the_same_whatever_it_holds()
    {
        var kit = Kit();
        var recipe = Recipe(DailyShops);
        Assert.NotEqual(Recipe(Shops).Fingerprint, recipe.Fingerprint);
        Assert.True(recipe.Rows[1].Rules.Daily);
        Assert.False(recipe.Rows[0].Rules.Daily);
        Assert.True(recipe.HasDaily);
        Assert.False(Recipe(Shops).HasDaily);

        // The kept street is the street without its daily row: its numbers are its own
        var without = Recipe(string.Join('\n', Shops.Split('\n').Where(l => !l.Contains("\"take\": [ \"curb_car\" ]", StringComparison.Ordinal)))
            .Replace("\"empty\": 0.1 },", "\"empty\": 0.1 }", StringComparison.Ordinal));
        Assert.Single(without.Rows);
        for (var seed = 1; seed <= 5; seed++)
        {
            var kept = StreetLayout.Generate(recipe, kit, seed);
            Assert.DoesNotContain(kept, p => p.Piece == "sedan" || p is { Piece: "pickup", Z: 4.5f });
            Assert.Equal(StreetLayout.Generate(without, kit, seed).Select(p => p.ToString()), kept.Select(p => p.ToString()));
        }
    }

    [Fact]
    public void A_days_cars_are_the_same_for_the_day_others_the_next_and_keep_off_the_street_each_other_and_the_clear_curb()
    {
        var kit = Kit();
        var recipe = Recipe(DailyShops);
        var kept = StreetLayout.Generate(recipe, kit, 1968);
        var monday = StreetLayout.Daily(recipe, kit, kept, 1).Select(p => p.ToString()).ToList();

        Assert.NotEmpty(monday);
        Assert.Equal(monday, StreetLayout.Daily(recipe, kit, kept, 1).Select(p => p.ToString()));
        Assert.NotEqual(monday, StreetLayout.Daily(recipe, kit, kept, 2).Select(p => p.ToString()));

        for (var day = 1; day <= 20; day++)
        {
            var today = StreetLayout.Daily(recipe, kit, kept, day);
            Assert.All(today, p => Assert.True(IsCar(kit, p) && p.Z == 4.5f, p.ToString()));

            // Out of the clear curb, off each other, off the shops
            foreach (var (car, n) in today.Select((c, i) => (c, i)))
            {
                Assert.False(Overlap(BoxOf(kit, car), (new Vector2(-10f, 2f), new Vector2(0f, 7f)), 0.2f), car.ToString());
                foreach (var other in today.Skip(n + 1)) Assert.False(Overlap(BoxOf(kit, car), BoxOf(kit, other)), $"{car} / {other}");
                foreach (var shop in kept.Where(p => p.Z < 0f && kit.Find(p.Piece)!.Tags.Contains("shop")))
                    Assert.False(Overlap(BoxOf(kit, car), BoxOf(kit, shop)), $"{car} / {shop}");
            }
        }
    }

    [Fact]
    public void The_days_cars_keep_off_the_pieces_the_save_keeps_not_the_street_laid_out_anew_and_not_a_palms_crown()
    {
        var kit = Kit();
        var recipe = Recipe(DailyShops);

        // A shop the save keeps in the parking lane (a mod's piece moved since): the cars keep off it, not off where the kit
        // would put the shops now
        var kept = new List<PlacedPiece> { new("shop_a", 50f, 0f, 4.5f, 0f, 1f) { Room = true } };
        var lone = new List<PlacedPiece> { new("shop_a", 50f, 0f, 4.5f, 0f, 1f) };
        for (var day = 1; day <= 20; day++)
        {
            Assert.All(StreetLayout.Daily(recipe, kit, kept, day), car => Assert.False(Overlap(BoxOf(kit, car), BoxOf(kit, kept[0])), car.ToString()));

            // Laid by a slot that keeps no room (a palm's crown over the lane), it is not kept off; nor a piece the kit has not
            Assert.Equal(StreetLayout.Daily(recipe, kit, [], day).Select(p => p.ToString()), StreetLayout.Daily(recipe, kit, lone, day).Select(p => p.ToString()));
            Assert.Equal(StreetLayout.Daily(recipe, kit, [], day).Select(p => p.ToString()),
                StreetLayout.Daily(recipe, kit, [new("gone", 50f, 0f, 4.5f, 0f, 1f) { Room = true }], day).Select(p => p.ToString()));
        }

        // What the street keeps says which of its pieces keep room: the shops do, the palms and lamps do not
        var street = StreetLayout.Generate(recipe, kit, 1968);
        Assert.All(street.Where(p => p.Piece.StartsWith("shop", StringComparison.Ordinal) || p.Piece == "corner"), p => Assert.True(p.Room));
        Assert.All(street.Where(p => p.Piece is "palm" or "palm_card" or "lamp"), p => Assert.False(p.Room));
    }

    [Fact]
    public void A_kept_row_after_a_daily_one_has_the_days_cars_keep_off_it()
    {
        var kit = Kit();

        // A look's row of trucks along the same curb, after the street's daily cars: the cars keep off the trucks
        var recipe = Recipe(DailyShops).Over(Recipe("""
            { "rows": [ { "from": [ -40, 4.5 ], "to": [ 40, 4.5 ], "heading": 90, "take": [ "truck" ], "align": "middle", "gap": [ 1, 1 ] } ] }
            """));
        for (var seed = 1; seed <= 10; seed++)
        {
            var kept = StreetLayout.Generate(recipe, kit, seed);
            var trucks = kept.Where(p => p.Z == 4.5f).ToList();
            Assert.NotEmpty(trucks);
            var today = StreetLayout.Daily(recipe, kit, kept, seed);
            Assert.NotEmpty(today);
            foreach (var car in today)
            foreach (var truck in trucks)
                Assert.False(Overlap(BoxOf(kit, car), BoxOf(kit, truck)), $"{car} / {truck}");
        }
    }

    [Fact]
    public void A_street_of_only_daily_slots_is_laid_out_once_not_again_each_time_it_is_drawn()
    {
        var kit = Kit();
        var street = Street("base/avenue", Recipe("""
            { "rows": [ { "from": [ -100, 4.5 ], "to": [ 100, 4.5 ], "heading": 90, "take": [ "curb_car" ], "align": "middle", "daily": true } ] }
            """));
        var state = new GameState { StreetSeed = 12, Date = new DateTime(1968, 6, 3, 9, 0, 0) };

        Assert.NotEmpty(StreetLayouts.For(state, street, kit));
        var saved = state.Streets.Single().Pieces;
        Assert.Empty(saved);
        Assert.NotEmpty(StreetLayouts.For(state, street, kit));
        Assert.Same(saved, state.Streets.Single().Pieces);
    }

    [Fact]
    public void A_looks_slot_in_the_place_of_a_daily_one_stays_daily_unless_it_says_otherwise()
    {
        var street = Recipe(DailyShops.Replace("\"daily\": true", "\"daily\": true, \"name\": \"curb\"", StringComparison.Ordinal));
        const string Curb = """{ "rows": [ { "name": "curb", "from": [ -100, 4.5 ], "to": [ 100, 4.5 ], "heading": 90, "take": [ "sedan" ], "align": "middle"%DAILY% } ] }""";

        var tweaked = street.Over(Recipe(Curb.Replace("%DAILY%", "", StringComparison.Ordinal)));
        Assert.Equal(new[] { "sedan" }, tweaked.Rows[1].Rules.Choice.Take);
        Assert.True(tweaked.Rows[1].Rules.Daily);
        Assert.True(tweaked.HasDaily);

        var kept = street.Over(Recipe(Curb.Replace("%DAILY%", ", \"daily\": false", StringComparison.Ordinal)));
        Assert.False(kept.Rows[1].Rules.Daily);
        Assert.False(kept.HasDaily);
    }

    [Fact]
    public void A_saves_street_keeps_its_buildings_and_draws_its_curb_cars_by_the_day_the_same_all_day()
    {
        var kit = Kit();
        var street = Street("base/avenue", Recipe(DailyShops));
        var state = new GameState { StreetSeed = 12, Date = new DateTime(1968, 6, 3, 9, 0, 0) };

        var drawn = StreetLayouts.For(state, street, kit);
        Assert.DoesNotContain(state.Streets.Single().Pieces, p => p.Piece == "sedan" || p is { Piece: "pickup", Z: 4.5f });
        Assert.Contains(drawn, p => p.Piece == "sedan" || p is { Piece: "pickup", Z: 4.5f });
        var morning = drawn.Select(p => p.ToString()).ToList();
        var seed = state.Streets[0].Seed;
        Assert.Equal(StreetLayout.Generate(street.Recipe!, kit, seed).Concat(StreetLayout.Daily(street.Recipe!, kit, StreetLayout.Generate(street.Recipe!, kit, seed),
                StreetLayouts.DaySeedOf(seed, state.Date)))
            .Select(p => p.ToString()), morning);

        // The evening of the same day: the same cars; the next day others, the buildings kept
        state.Date = new DateTime(1968, 6, 3, 21, 30, 0);
        Assert.Equal(morning, StreetLayouts.For(state, street, kit).Select(p => p.ToString()));
        state.Date = new DateTime(1968, 6, 4, 9, 0, 0);
        var tuesday = StreetLayouts.For(state, street, kit).Select(p => p.ToString()).ToList();
        Assert.NotEqual(morning, tuesday);
        var kept = state.Streets.Single().Pieces.Select(p => p.ToString()).ToList();
        Assert.Equal(kept, morning.Take(kept.Count));
        Assert.Equal(kept, tuesday.Take(kept.Count));

        // The day's seed is the layout's and the date's, and not the hour's
        Assert.Equal(StreetLayouts.DaySeedOf(seed, new DateTime(1968, 6, 4)), StreetLayouts.DaySeedOf(seed, new DateTime(1968, 6, 4, 23, 59, 0)));
        Assert.NotEqual(StreetLayouts.DaySeedOf(seed, new DateTime(1968, 6, 4)), StreetLayouts.DaySeedOf(seed + 1, new DateTime(1968, 6, 4)));
    }
}
