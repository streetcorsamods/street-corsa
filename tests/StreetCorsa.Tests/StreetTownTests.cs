using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Diner;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// Street Rod's town (docs/street-rod-town.md, phase 2): a street that is a town is laid out once, every diner's look over
/// it together, each at its own place.
/// </summary>
public sealed class StreetTownTests
{
    [Fact]
    public void A_town_is_laid_out_once_for_every_diner_each_looks_spots_and_way_at_its_own_place()
    {
        var kit = StreetLayoutTests.Kit();
        var town = StreetLayoutTests.Recipe(StreetLayoutTests.Shops) with { Town = true };
        var street = StreetLayoutTests.Street("base/town", town);
        var way = new Models.Diners.DinerApproach([StreetLayoutTests.G(-100f, 4.5f), StreetLayoutTests.G(100f, 4.5f)], [StreetLayoutTests.G(100f, 4.5f), StreetLayoutTests.G(100f, 3f)], [StreetLayoutTests.G(100f, 3f), StreetLayoutTests.G(110f, 3f)], 0f, 0f);
        var yard = StreetLayoutTests.Diner("yard", StreetLayoutTests.Recipe(StreetLayoutTests.Yard));
        yard.Look = yard.Look! with { Street = "base/town", Approach = way };
        var beach = StreetLayoutTests.Diner("beach", StreetLayoutTests.Recipe("""{ "spots": [ { "at": [ -60, 30 ], "heading": 0, "take": [ "sedan" ] } ] }"""));
        beach.Look = beach.Look! with { Street = "base/town" };
        var plain = StreetLayoutTests.Diner("plain", null);
        var elsewhere = StreetLayoutTests.Diner("elsewhere", StreetLayoutTests.Recipe("""{ "spots": [ { "at": [ 0, 60 ], "heading": 0, "take": [ "sedan" ] } ] }"""));
        elsewhere.Look = elsewhere.Look! with { Street = "base/avenue" };
        var diners = new[] { plain, yard, beach, elsewhere };

        // One recipe whoever asks: the street's with both looks written on it, the yard's way kept clear
        var recipe = StreetLayouts.RecipeOf(street, yard, diners)!;
        Assert.True(recipe.Town);
        Assert.Equal(recipe.Fingerprint, StreetLayouts.RecipeOf(street, beach, diners)!.Fingerprint);
        Assert.Equal(recipe.Fingerprint, StreetLayouts.RecipeOf(street, null, diners)!.Fingerprint);
        Assert.Equal(StreetLayoutTests.Recipe(StreetLayoutTests.Shops).Spots.Count + 2, recipe.Spots.Count);
        Assert.True(recipe.Clear.Count > town.Clear.Count);

        // Laid out once, kept as the street's, the same town at every diner
        var state = new GameState { StreetSeed = 7 };
        var atYard = StreetLayouts.For(state, street, kit, yard, diners).Select(p => p.ToString()).ToList();
        Assert.Equal(atYard, StreetLayouts.For(state, street, kit, beach, diners).Select(p => p.ToString()));
        Assert.Equal(atYard, StreetLayouts.For(state, street, kit, null, diners).Select(p => p.ToString()));
        Assert.Equal([""], state.Streets.Select(s => s.Diner));
        Assert.Equal(StreetLayouts.SeedOf(state, street.Id), state.Streets[0].Seed);
        var laid = StreetLayouts.For(state, street, kit, plain, diners);
        Assert.Contains(laid, p => p is { Piece: "pickup", X: 40f, Z: 30f });
        Assert.Contains(laid, p => p is { Piece: "sedan", X: -60f, Z: 30f });
        Assert.DoesNotContain(laid, p => p is { X: 0f, Z: 60f });

        // A save's layouts of the town for a diner (of before it was a town) go; the town's stays
        state.Streets.Add(new StreetLayoutState { Street = street.Id, Diner = "yard", Seed = 1 });
        Assert.True(StreetLayouts.Prune(state, [street], DinerBook.From(diners)));
        Assert.Equal([""], state.Streets.Select(s => s.Diner));

        // Read from the layout's file
        Assert.True(StreetLayoutTests.Recipe("""{ "town": true, "spots": [] }""").Town);
        Assert.False(StreetLayoutTests.Recipe(StreetLayoutTests.Shops).Town);
        Assert.True(StreetLayoutTests.Recipe("""{ "town": true }""").Over(StreetLayoutTests.Recipe(StreetLayoutTests.Yard)).Town);
    }
}
