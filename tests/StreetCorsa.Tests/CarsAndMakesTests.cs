using StreetCorsa.Models.Career.Filters;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Services.Catalog;

namespace StreetCorsa.Tests;

/// <summary>
/// Cars and makes as data (E6): the makes and countries of makes.json, what cars.json gives a car, game.json's filter of
/// the installed cars, the part groups of part_groups.json, the catalog editor's cars.json, the stand-in classes. Some
/// set the book and the groups in use, which every other test reads: run on their own.
/// </summary>
[Collection(GlobalsCollection.Name)]
public class CarsAndMakesTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CarBook BaseBook() => CarBook.Load(ModSet.BaseOnly(RepoPaths.BaseContent));

    /// <summary>The base game and one mod over it, its files given</summary>
    private ModSet WithMod(params (string File, string Json)[] files)
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoPaths.BaseContent, ContentTypes.Cars)))
            _temp.File($"Content/streetcorsa/cars/{Path.GetFileName(file)}", File.ReadAllText(file));
        _temp.File("Content/streetcorsa/parts/part_groups.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, ContentTypes.Parts, PartGroups.FileName)));
        _temp.File("Content/mymod/mod.json", """{ "id": "mymod" }""");
        foreach (var (file, json) in files) _temp.File($"Content/mymod/{file}", json);
        return ModSet.Load(_temp.Combine("Content"));
    }

    [Fact]
    public void The_base_games_makes_cars_and_groups_read_without_a_problem()
    {
        Assert.Empty(BaseBook().Problems);
        Assert.Empty(PartGroups.Load([Path.Combine(RepoPaths.BaseContent, ContentTypes.Parts)]).Problems);
        Assert.NotEmpty(BaseBook().Cars);
    }

    [Theory]
    [InlineData("Pontiac", "GTO 1969", "pontiac", "gm")]
    [InlineData("AC Legends", "ACL TA '70 Chevrolet Camaro Z28", "chevrolet", "gm")]
    [InlineData("Shelby", "Cobra 427", "shelby", "ford")]
    [InlineData("Aston Martin", "DB5", "aston_martin", "")]
    [InlineData("Alfa Romeo", "Giulia", "alfa_romeo", "")]
    [InlineData("Batmobile", "The Batmobile", null, "")]
    public void A_cars_make_is_told_by_its_brand_else_its_name(string brand, string name, string? make, string family)
    {
        var book = BaseBook();
        Assert.Equal(make, book.MakeOf("x", brand, name));
        Assert.Equal(family, book.FamilyOfCar("x", brand, name));
    }

    [Fact]
    public void A_make_without_a_family_names_no_engine_and_the_next_word_tells()
    {
        // Jeep has no family: the word after it does
        Assert.Equal("ford", BaseBook().FamilyOf("Jeep with a Ford 289"));
        Assert.Equal("", BaseBook().FamilyOf("Toyota 2000GT"));
    }

    [Fact]
    public void Countries_know_their_names_and_regions()
    {
        var book = BaseBook();
        Assert.Equal("UK", book.Country("Great Britain"));
        Assert.True(book.IsIn("West Germany", "European"));
        Assert.True(book.IsIn("USA", "American"));
        Assert.False(book.IsIn("Japan", "Europe"));

        var filter = new OriginFilter("European");
        Assert.True(filter.Matches(new CarDefinition { Id = "x", Brand = "Ferrari", Name = "250 GTO" }));
        Assert.False(filter.Matches(new CarDefinition { Id = "x", Brand = "Ford", Name = "Mustang" }));
        Assert.True(new OriginFilter("USA").Matches(new CarDefinition { Id = "x", Brand = "Whoever", Name = "Thing", Country = "United States" }));
    }

    [Fact]
    public void A_mod_changes_one_figure_of_a_make_and_adds_a_make_and_a_car()
    {
        var book = CarBook.Load(WithMod(("cars/mine.json", """
            {
              "makes": {
                "ford": { "common": 0.4 },
                "datsun": { "family": "nissan", "country": "Japan", "words": [ "datsun" ] }
              },
              "cars": { "datsun_240z": { "make": "datsun", "layout": "front" } }
            }
            """)));

        Assert.Empty(book.Problems);
        Assert.Equal(0.4f, book.Make("ford")!.Common);
        Assert.Contains("mustang", book.Make("ford")!.Words);
        Assert.Equal("nissan", book.FamilyOf("Datsun 240Z"));
        Assert.Equal("datsun", book.MakeOf("datsun_240z", "Nissan", "Fairlady"));
        Assert.Equal(EngineLayout.Front, book.Car("datsun_240z")!.EngineLayout);

        // The make given gives the family too, whatever the brand says
        Assert.Equal("nissan", book.FamilyOfCar("datsun_240z", "Nissan", "Fairlady"));
    }

    [Fact]
    public void A_car_given_a_make_without_a_family_has_none()
    {
        var book = CarBook.Load(WithMod(("cars/mine.json", """{ "cars": { "x": { "make": "toyota" } } }""")));
        Assert.Equal("toyota", book.MakeOf("x", "Chevrolet", "Camaro"));
        Assert.Equal("", book.FamilyOfCar("x", "Chevrolet", "Camaro"));
    }

    [Fact]
    public void A_mods_null_list_is_none_and_a_price_is_told_and_not_taken()
    {
        var book = CarBook.Load(WithMod(("cars/mine.json", """
            {
              "makes": { "jeep": { "words": null }, "ford": { "price": 1e30 } },
              "countries": { "USA": { "names": null, "regions": null } },
              "cars": { "a": { "price": 1e30 } },
              "words": { "aliases": null, "noise": null }
            }
            """)));

        Assert.Empty(book.Make("jeep")!.Words);
        Assert.Contains(book.Problems, p => p.Contains("makes.ford.price is no longer read"));
        Assert.Contains(book.Problems, p => p.Contains("cars.a.price is no longer read"));
        Assert.False(book.IsIn("USA", "Europe"));
        Assert.Contains("camaro", book.Tokens("Chevrolet Camaro"));
    }

    [Fact]
    public void What_a_mod_gets_wrong_is_told_and_left_out()
    {
        var book = CarBook.Load(WithMod(("cars/mine.json", """
            {
              "makes": { "datsun": { "words": [ "ford" ] } },
              "cars": { "a": { "make": "nobody", "layout": "sideways", "common": 3 } },
              "trucks": { }
            }
            """)));

        Assert.Contains(book.Problems, p => p.Contains("\"ford\" is a word of both"));
        Assert.Contains(book.Problems, p => p.Contains("cars.a.make"));
        Assert.Contains(book.Problems, p => p.Contains("cars.a.layout"));
        Assert.Contains(book.Problems, p => p.Contains("cars.a.common"));
        Assert.Contains(book.Problems, p => p.Contains("\"trucks\""));
        var car = book.Car("a")!;
        Assert.Null(car.Make);
        Assert.Null(car.EngineLayout);
        Assert.Null(car.Common);
    }

    [Fact]
    public void A_suggested_factory_engine_follows_the_builds_installed_now_and_one_picked_by_hand_stays()
    {
        static RatedBuild Rated(string id) => new(new EngineBuild { Id = id, Name = id }, "block", "ford", 200, 300, 5, 200);
        var v8 = Rated("ford_v8:kits/ford-302-v8-engine-kit");

        // Suggested the six while no V8 mod was installed: the V8 takes its place once one is
        Assert.True(Services.Parts.CarPartsService.TakesSuggestion(new CarProfile { StockEngineBuildId = "rated/ford-221-132-hp" }, v8));
        Assert.True(Services.Parts.CarPartsService.TakesSuggestion(new CarProfile(), v8));

        Assert.False(Services.Parts.CarPartsService.TakesSuggestion(new CarProfile { StockEngineBuildId = "FORD_V8:kits/ford-302-v8-engine-kit" }, v8));
        Assert.False(Services.Parts.CarPartsService.TakesSuggestion(new CarProfile { StockEngineBuildId = "rated/ford-221-132-hp", StockEngineIsManual = true }, v8));
        Assert.False(Services.Parts.CarPartsService.TakesSuggestion(new CarProfile { StockEngineBuildId = "gone/build" }, null));
    }

    [Fact]
    public void A_profile_carries_what_cars_json_gives_its_car_and_the_stored_one_is_left_alone()
    {
        var before = CarBook.Current;
        try
        {
            CarBook.Current = CarBook.Load(WithMod(("cars/mine.json", """{ "cars": { "c": { "engine": "b1", "common": 0.9, "layout": "rear" } } }""")));
            var stored = new CarProfile { CarDefinitionId = "c", BasePrice = 20000, DealerPrecedence = 0.4f };
            var given = CarProfileRepository.Given(stored)!;

            // The price is the game's, always
            Assert.Equal(20000m, given.BasePrice);
            Assert.Equal(0.9f, given.DealerPrecedence);
            Assert.Equal("b1", given.StockEngineBuildId);
            Assert.True(given.StockEngineIsManual);
            Assert.Equal(EngineLayout.Rear, given.EngineLayout);

            var other = new CarProfile { CarDefinitionId = "other", BasePrice = 20000 };
            Assert.Equal(20000m, CarProfileRepository.Given(other)!.BasePrice);
        }
        finally
        {
            CarBook.Current = before;
        }
    }

    [Fact]
    public void The_game_takes_the_cars_its_filter_lets_in()
    {
        var book = BaseBook();
        var sixties = new GameCars { From = 1955, To = 1975, Countries = ["American"] };
        Assert.True(sixties.Everything == false && new GameCars().Everything);

        Assert.True(sixties.Allows("a", "Chevrolet", "Camaro", null, 1969, book));
        Assert.False(sixties.Allows("b", "Chevrolet", "Camaro", null, 1985, book));
        Assert.False(sixties.Allows("c", "Ferrari", "275", null, 1966, book));
        Assert.False(sixties.Allows("d", "Chevrolet", "Something", null, null, book));
        Assert.True(sixties.Allows("e", "Whoever", "Special", "USA", 1960, book));

        var withIds = new GameCars { From = 1955, To = 1975, Ids = ["ferrari_275"], Never = ["camaro_69"] };
        Assert.True(withIds.Allows("ferrari_275", "Ferrari", "275", null, 1966, book));
        Assert.False(withIds.Allows("camaro_69", "Chevrolet", "Camaro", null, 1969, book));

        var makes = new GameCars { Makes = ["ford", "shelby"] };
        Assert.True(makes.Allows("x", "Shelby", "GT500", null, null, book));
        Assert.False(makes.Allows("x", "Dodge", "Charger", null, null, book));

        var only = new GameCars { Ids = ["one"], OnlyIds = true };
        Assert.True(only.Allows("one", "Ford", "T", null, 1920, book));
        Assert.False(only.Allows("two", "Ford", "T", null, 1920, book));
    }

    [Fact]
    public void A_mods_group_goes_first_and_a_changed_group_keeps_its_place()
    {
        var mods = WithMod(("parts/part_groups.json", """
            {
              "groups": [
                { "id": "intercoolers", "classes": [ "intercooler" ], "tune": 4, "speed_shop": true },
                { "id": "air_filters", "tune": 9 }
              ]
            }
            """));
        var groups = PartGroups.Load(mods.Folders(ContentTypes.Parts).Select(f => f.Dir));

        Assert.Empty(groups.Problems);
        Assert.Equal("intercoolers", groups.All[0].Id);
        Assert.Equal("intercoolers", groups.GroupOf("intercooler"));
        Assert.Equal(9, groups.TunePriority(PartGroups.AirFilters));
        Assert.True(groups.IsBoltOn(PartGroups.AirFilters));
        Assert.Equal(PartGroups.AirFilters, groups.GroupOf(PartClasses.AirCleaner));
        Assert.Equal(PartGroups.Blocks, groups.GroupOf(PartClasses.EngineBlock));
        Assert.Equal(PartGroups.Accessories, groups.GroupOf("neon"));
    }

    [Fact]
    public void The_parts_views_shows_take_their_kinds_the_rest_of_the_engine_and_a_mod_adds_or_changes_one()
    {
        var base_ = PartGroups.Load([Path.Combine(RepoPaths.BaseContent, ContentTypes.Parts)]);
        Assert.Equal(["engine", "drivetrain", "brakes", "suspension", "wheels"], base_.Shows.Select(s => s.Id));
        Assert.Equal("wheels", base_.ShowOf(PartGroups.Rims, inEngine: false)!.Id);
        Assert.Equal("drivetrain", base_.ShowOf(PartGroups.Transmissions, inEngine: true)!.Id);
        Assert.Equal("engine", base_.ShowOf("flywheels", inEngine: true)!.Id);
        Assert.Equal("engine", base_.ShowOf(PartGroups.CylinderHeads, inEngine: true)!.Id);
        Assert.Equal("engine", base_.ShowOf(PartGroups.Accessories, inEngine: true)!.Id);
        Assert.Equal("brakes", base_.ShowOf(PartGroups.Brakes, inEngine: false)!.Id);
        Assert.Equal("suspension", base_.ShowOf(PartGroups.Shocks, inEngine: false)!.Id);
        Assert.Null(base_.ShowOf(PartGroups.Accessories, inEngine: false));

        var mods = WithMod(("parts/part_groups.json", """
            {
              "groups": [ { "id": "differentials", "classes": [ "differential" ] } ],
              "shows": [ { "id": "brakes", "icon": "icons/brake_drum" }, { "id": "body", "icon": "icons/car", "groups": [ "panels" ] } ]
            }
            """));
        var groups = PartGroups.Load(mods.Folders(ContentTypes.Parts).Select(f => f.Dir));
        Assert.Empty(groups.Problems);
        Assert.Equal(["engine", "drivetrain", "brakes", "suspension", "wheels", "body"], groups.Shows.Select(s => s.Id));
        Assert.Equal("icons/brake_drum", groups.ShowOf(PartGroups.Brakes, inEngine: false)!.Icon);
        Assert.Equal("drivetrain", groups.ShowOf("differentials", inEngine: false)!.Id);
    }

    [Fact]
    public void A_group_reads_as_its_words_else_its_name_else_its_id()
    {
        var before = PartGroups.Current;
        try
        {
            PartGroups.Current = PartGroups.Load(WithMod(("parts/part_groups.json", """
                { "groups": [ { "id": "intercoolers", "name": "Intercoolers", "classes": [ "intercooler" ] }, { "id": "wings", "classes": [ "Wing" ] } ] }
                """)).Folders(ContentTypes.Parts).Select(f => f.Dir));

            Assert.Equal("Engine blocks", PartKinds.GroupName(PartGroups.Blocks));
            Assert.Equal("Intercoolers", PartKinds.GroupName("intercoolers"));
            Assert.Equal("wings", PartKinds.GroupName("wings"));
        }
        finally
        {
            PartGroups.Current = before;
        }
    }

    [Fact]
    public void The_editor_sets_a_cars_fields_and_keeps_the_notes_and_the_rest()
    {
        var text = """
            {
              "_comment": [ "notes" ],
              "cars": {
                "b": { "engine": "e1", "price": 100, "common": 0.5 },
                "a": { "layout": "mid" }
              }
            }
            """;
        var written = BaseCarsFile.With(text, new Dictionary<string, CarFields>
        {
            ["b"] = new(null, null, 0.75f),
            ["a"] = new(null, null, null),
            ["c"] = new("e2", EngineLayout.Rear, null)
        });

        var file = ModMerge.Parse(written)!;
        Assert.Equal("notes", file["_comment"]![0]!.GetValue<string>());
        var cars = file["cars"]!.AsObject();
        Assert.False(cars.ContainsKey("a"));
        // An older editor's price goes: prices are the game's
        Assert.False(cars["b"]!.AsObject().ContainsKey("price"));
        Assert.Equal(0.75, cars["b"]!["common"]!.GetValue<double>());
        Assert.False(cars["b"]!.AsObject().ContainsKey("engine"));
        Assert.Equal("rear", cars["c"]!["layout"]!.GetValue<string>());
        Assert.True(written.IndexOf("\"b\"", StringComparison.Ordinal) < written.IndexOf("\"c\"", StringComparison.Ordinal));
    }

    [Fact]
    public void The_editor_never_writes_over_a_cars_json_that_does_not_read()
    {
        var fields = new Dictionary<string, CarFields> { ["a"] = new("e1", null, null) };
        Assert.Throws<InvalidDataException>(() => BaseCarsFile.With("""{ "cars": { "b": { "price": 1 } "c": { } } }""", fields));
        Assert.Throws<InvalidDataException>(() => BaseCarsFile.With("""{ "cars": { "b": { }, "B": { } } }""", fields));
        Assert.Contains("\"a\"", BaseCarsFile.With(null, fields));
    }

    [Fact]
    public void A_make_game_json_names_that_is_none_is_told()
    {
        GameFile.Load(WithMod(("game.json", """{ "cars": { "makes": [ "ford", "datsunx" ] } }""")));
        Assert.Contains(ModProblems.All, p => p.Contains("datsunx"));
    }
}

