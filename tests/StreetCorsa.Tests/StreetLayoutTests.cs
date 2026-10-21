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
    private static ModSet BaseMods => ModSet.Load(RepoPaths.Content);

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
                var piece = kit.Find(placed.Piece)!;
                foreach (var area in recipe.Clear.Where(a => a.Holds(piece)))
                    Assert.False(Overlap(BoxOf(kit, placed), (area.Min, area.Max)), $"seed {seed}: {placed} in a clear area");
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

    // ----- the recipe -----

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
        var street = Street("base/mainstreet", Recipe(Shops));
        var first = StreetLayouts.For(state, street, kit).Select(p => p.ToString()).ToList();
        Assert.Single(state.Streets);
        Assert.Equal(StreetLayouts.SeedOf(state, street.Id), state.Streets[0].Seed);

        // A recipe changed later moves nothing in a street already laid out
        var changed = Street("base/mainstreet", Recipe("""{ "spots": [ { "at": [ 0, 0 ], "heading": 0, "take": [ "palm" ] } ] }"""));
        Assert.Equal(first, StreetLayouts.For(state, changed, kit).Select(p => p.ToString()));
        Assert.Single(state.Streets);

        // Each street of a save its own
        Assert.NotEqual(StreetLayouts.SeedOf(state, "base/mainstreet"), StreetLayouts.SeedOf(state, "base/other"));
        Assert.Empty(StreetLayouts.For(state, Street("base/plain", null), kit));
        Assert.Single(state.Streets);
    }

    [Fact]
    public void A_save_from_before_the_streets_seed_lays_its_street_out_from_its_name_the_same_every_load()
    {
        var kit = Kit();
        var street = Street("base/mainstreet", Recipe(Shops));
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
        var street = Street("base/mainstreet", Recipe(Shops));
        var all = StreetLayouts.For(state, street, kit);
        Assert.Contains(all, p => p.Piece == "pickup");

        var without = StreetKit.From(kit.All.Where(p => p.Id != "pickup"));
        var drawn = StreetLayouts.For(state, street, without);

        Assert.DoesNotContain(drawn, p => p.Piece == "pickup");
        Assert.Equal(all.Count(p => p.Piece != "pickup"), drawn.Count);
        Assert.Contains(state.Streets[0].Pieces, p => p.Piece == "pickup");
    }

    [Fact]
    public void A_streets_layout_goes_through_the_save_and_comes_back_whole()
    {
        var state = new GameState { StreetSeed = 9 };
        StreetLayouts.For(state, Street("base/mainstreet", Recipe(Shops)), Kit());
        var mapper = Services.Storage.SaveMapper.Create();

        var back = mapper.ToObject<GameState>(mapper.ToDocument(state));

        Assert.Equal(9, back.StreetSeed);
        Assert.Equal(state.Streets[0].Pieces.Select(p => p.ToString()), back.Streets.Single().Pieces.Select(p => p.ToString()));
        Assert.Equal(state.Streets[0].Seed, back.Streets[0].Seed);
    }

    // ----- the base game -----

    [Fact]
    public void The_base_games_kit_loads_whole_each_piece_with_its_model_tags_and_credit()
    {
        var problems = new List<string>();
        var kit = StreetKit.Load(BaseMods, problems);

        Assert.Empty(problems);
        Assert.True(kit.Count >= 60, $"{kit.Count} pieces");
        Assert.All(kit.All, p => Assert.True(File.Exists(p.Model), p.Id));
        Assert.All(kit.All, p => Assert.NotEmpty(p.Tags));
        Assert.All(kit.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Credit), p.Id));
        Assert.All(kit.Tagged(["lamp"]), p => Assert.NotNull(p.Lamp));
        Assert.Contains(kit.Credits, c => c.Contains("CC BY 4.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Main_street_is_laid_out_from_its_recipe_every_slot_taking_something_the_kit_has_the_curb_clear_where_the_car_parks()
    {
        var problems = new List<string>();
        var kit = StreetKit.Load(BaseMods, problems);
        var street = StreetScenes.LoadAll(BaseMods).Single(s => s.Id.EndsWith("mainstreet", StringComparison.Ordinal));
        var recipe = street.Recipe!;

        var choices = recipe.Rows.SelectMany(r => r.First.Append(r.Rules.Choice)).Concat(recipe.Runs.Select(r => r.Rules.Choice))
            .Concat(recipe.Spots.Select(s => s.Rules.Choice)).Concat(recipe.Runs.Select(r => r.Rules.Far).OfType<PieceChoice>());
        Assert.All(choices, c => Assert.NotEmpty(c.In(kit)));

        for (var seed = 1; seed <= 10; seed++)
        {
            var layout = StreetLayout.Generate(recipe, kit, seed);
            Assert.InRange(layout.Count, 150, 400);
            Assert.InRange(layout.Count(p => kit.Find(p.Piece)!.Tags.Contains("house")), 20, 60);
            Assert.InRange(layout.Count(p => kit.Find(p.Piece)!.Tags.Contains("curb_car")), 10, 80);

            // Nothing at the curb where the player's car parks, nor where it comes in and goes out (diner.json's paths)
            var parked = (new Vector2(-24f, 2.5f), new Vector2(14f, 6.8f));
            foreach (var car in layout.Where(p => kit.Find(p.Piece)!.Tags.Contains("curb_car")))
                Assert.False(Overlap(BoxOf(kit, car), parked), $"seed {seed}: {car}");
        }
    }
}
