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

    /// <summary>The tests' own makes and countries (Fixtures	est_makes): the core has none of its own</summary>
    private static CarBook BaseBook() => TestCars.Makes.Value;

    /// <summary>The core, with the tests' makes as its cars file, and one mod over it, its files given</summary>
    private ModSet WithMod(params (string File, string Json)[] files)
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        foreach (var file in Directory.EnumerateFiles(RepoPaths.TestMakes))
            _temp.File($"Content/streetcorsa/cars/{Path.GetFileName(file)}", File.ReadAllText(file));
        _temp.File("Content/streetcorsa/parts/part_groups.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, ContentTypes.Parts, PartGroups.FileName)));
        _temp.File("Content/mymod/mod.json", """{ "id": "mymod" }""");
        foreach (var (file, json) in files) _temp.File($"Content/mymod/{file}", json);
        return ModSet.Load(_temp.Combine("Content"));
    }

    [Fact]
    public void The_makes_and_cars_and_the_cores_groups_read_without_a_problem()
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
        
        Assert.True(sixties.WhyOut(new GameCar("a", "Chevrolet", "Camaro", null, 1969), book) == null);
        Assert.False(sixties.WhyOut(new GameCar("b", "Chevrolet", "Camaro", null, 1985), book) == null);
        Assert.False(sixties.WhyOut(new GameCar("c", "Ferrari", "275", null, 1966), book) == null);
        Assert.False(sixties.WhyOut(new GameCar("d", "Chevrolet", "Something", null, null), book) == null);
        Assert.True(sixties.WhyOut(new GameCar("e", "Whoever", "Special", "USA", 1960), book) == null);

        var withIds = new GameCars { From = 1955, To = 1975, Ids = ["ferrari_275"], Never = ["camaro_69"] };
        Assert.True(withIds.WhyOut(new GameCar("ferrari_275", "Ferrari", "275", null, 1966), book) == null);
        Assert.False(withIds.WhyOut(new GameCar("camaro_69", "Chevrolet", "Camaro", null, 1969), book) == null);

        var makes = new GameCars { Makes = ["ford", "shelby"] };
        Assert.True(makes.WhyOut(new GameCar("x", "Shelby", "GT500", null, null), book) == null);
        Assert.False(makes.WhyOut(new GameCar("x", "Dodge", "Charger", null, null), book) == null);

        var only = new GameCars { Ids = ["one"], OnlyIds = true };
        Assert.True(only.WhyOut(new GameCar("one", "Ford", "T", null, 1920), book) == null);
        Assert.False(only.WhyOut(new GameCar("two", "Ford", "T", null, 1920), book) == null);
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

    [Fact]
    public void The_filter_keeps_out_cars_by_their_class_and_tags_and_says_why()
    {
        var book = BaseBook();
        var street = new GameCars { NeverClasses = ["race"], NeverTags = ["singleseater"] };
        Assert.False(street.ByYear);
        Assert.Null(street.WhyOut(new GameCar("a", "Ford", "Falcon", null, null, "Street", ["rwd"]), book));
        Assert.Null(street.WhyOut(new GameCar("b", "Ford", "Falcon", null, null), book));
        Assert.Equal(CarOutBecause.Class, street.WhyOut(new GameCar("c", "Ford", "GT40", null, null, " RACE "), book)?.Because);
        Assert.Equal(CarOutBecause.Tag, street.WhyOut(new GameCar("d", "Lotus", "25", null, null, "vintage", [null!, "SingleSeater"]), book)?.Because);

        var only = new GameCars { Classes = ["street"], Tags = ["rwd", "fwd"] };
        Assert.Null(only.WhyOut(new GameCar("e", "Ford", "Falcon", null, null, "street", ["rwd"]), book));
        Assert.Equal(CarOutBecause.Class, only.WhyOut(new GameCar("f", "Ford", "Falcon", null, null, null, ["rwd"]), book)?.Because);
        Assert.Equal(CarOutBecause.Tag, only.WhyOut(new GameCar("g", "Audi", "Quattro", null, null, "street", ["awd"]), book)?.Because);

        var sixties = new GameCars { From = 1955, To = 1975, Countries = ["USA"] };
        Assert.Equal(CarOutBecause.NoYear, sixties.WhyOut(new GameCar("h", "Ford", "Falcon", null, null), book)?.Because);
        Assert.Equal(CarOutBecause.Year, sixties.WhyOut(new GameCar("i", "Ford", "Falcon", null, 1992), book)?.Because);
        Assert.Equal(CarOutBecause.Country, sixties.WhyOut(new GameCar("j", "Whoever", "Special", "Italy", 1966), book)?.Because);
    }

    [Fact]
    public void A_mods_cars_json_brings_a_car_in_or_leaves_it_out_below_game_jsons_own_lists()
    {
        var problems = new List<string>();
        var book = CarBook.Read(ModMerge.Parse("""
            { "cars": {
                "brought": { "in": true }, "dropped": { "in": false }, "listed": { "in": false }, "banned": { "in": true },
                "dated": { "year": 1966 }, "misdated": { "year": 66 } } }
            """)!, problems);
        Assert.Contains(problems, p => p.Contains("misdated.year"));
        Assert.Null(book.Car("misdated")!.Year);

        var game = new GameCars { From = 1955, To = 1975, Ids = ["listed"], Never = ["banned"] };
        Assert.True(game.WhyOut(new GameCar("brought", "Jaguar", "XJ220", "Great Britain", 1992), book) == null);
        Assert.Equal(CarOutBecause.Mod, game.WhyOut(new GameCar("dropped", "Ford", "Falcon", null, 1962), book)?.Because);
        Assert.True(game.WhyOut(new GameCar("listed", "Ford", "Falcon", null, null), book) == null);
        Assert.False(game.WhyOut(new GameCar("banned", "Ford", "Falcon", null, 1962), book) == null);
        // A car game.json filters nothing for is still left out by a mod
        Assert.False(new GameCars().WhyOut(new GameCar("dropped", "Ford", "Falcon", null, 1962), book) == null);

        Assert.Equal(1966, CarYear.Of("dated", "Ford Falcon 1990", "Ford", 1980, book));
        Assert.Equal(1980, CarYear.Of("undated", "Ford Falcon 1990", "Ford", 1980, book));
        Assert.Equal(1990, CarYear.Of("undated", "Ford Falcon 1990", "Ford", null, book));
        Assert.Equal(2015, CarYear.Of("ks_ford_mustang_2015", "Ford Mustang", "Ford", null, book));
    }

    [Theory]
    [InlineData("Ford Mustang 2015", 2015)]
    [InlineData("chevy_camaro_1969_zl1", 1969)]
    [InlineData("Porsche 911 (1973) RS 1974", 1974)]
    [InlineData("Toyota 2000GT", null)]
    [InlineData("Alfa Romeo 1900 C52", null)]
    [InlineData("Shelby GT500 '67", null)]
    [InlineData("Peugeot 20155", null)]
    [InlineData(null, null)]
    public void A_year_is_read_from_a_name_only_when_written_whole_on_its_own(string? name, int? year) =>
        Assert.Equal(year, CarYear.FromName(name));

    [Theory]
    [InlineData("BMW 2002 Turbo", "BMW", null)]
    [InlineData("bmw_2002_turbo", "BMW", null)]
    [InlineData("Triumph 2000", "Triumph", null)]
    [InlineData("datsun_2000_roadster", "Datsun", null)]
    [InlineData("2002 Turbo", "BMW", null)]
    [InlineData("1969 Chevrolet Camaro", "Chevrolet", 1969)]
    [InlineData("ks_ford_mustang_2015", "Ford", 2015)]
    [InlineData("chevy_camaro_1969_zl1", "Chevrolet", 1969)]
    [InlineData("Alfa Romeo Giulia 1965", "Alfa Romeo", 1965)]
    [InlineData("Ford 1969\u0663", "Ford", null)]
    public void A_number_right_after_the_make_is_the_models_not_a_year(string name, string brand, int? year) =>
        Assert.Equal(year, CarYear.FromName(System.Text.RegularExpressions.Regex.Unescape(name), brand));

    [Fact]
    public void A_ui_car_year_out_of_any_car_s_years_is_passed_over()
    {
        Assert.Equal(1969, CarYear.Of("camaro_1969", "Camaro", "Chevrolet", 69, CarBook.Empty));
        Assert.Equal(1969, CarYear.Of("camaro_1969", "Camaro", "Chevrolet", 20015, CarBook.Empty));
        Assert.Equal(1968, CarYear.Of("camaro_1969", "Camaro", "Chevrolet", 1968, CarBook.Empty));
    }

    [Fact]
    public void A_cars_year_is_worked_out_again_for_another_book_and_never_given()
    {
        var car = new CarDefinition { Id = "dated", Name = "Falcon", Brand = "Ford", UiYear = 1980 };
        var dated = CarBook.Read(ModMerge.Parse("""{ "cars": { "dated": { "year": 1966 } } }""")!, []);
        Assert.Equal(1966, car.YearIn(dated));
        Assert.Equal(1980, car.YearIn(CarBook.Empty));
        Assert.Equal(1980, car.UiYear);
        car.UiYear = 1981;
        Assert.Equal(1981, car.YearIn(CarBook.Empty));
    }

    [Fact]
    public void Only_game_jsons_ids_are_in_with_only_ids_whatever_a_cars_json_says()
    {
        var book = CarBook.Read(ModMerge.Parse("""{ "cars": { "brought": { "in": true } } }""")!, []);
        var only = new GameCars { Ids = ["one"], OnlyIds = true };
        Assert.Equal(CarOutBecause.NotListed, only.WhyOut(new GameCar("brought", "Ford", "T", null, 1920), book)?.Because);
        Assert.Null(only.WhyOut(new GameCar("one", "Ford", "T", null, 1920), book));
    }

    [Fact]
    public void An_add_on_for_any_setting_brings_no_car_in_whatever_the_game_takes()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("Content/rods/mod.json", """{ "id": "rods", "type": "setting" }""");
        _temp.File("Content/anywhere/mod.json", """{ "id": "anywhere" }""");
        _temp.File("Content/anywhere/cars/cars.json", """{ "cars": { "free": { "in": true, "year": 1992 }, "gone": { "in": false } } }""");
        _temp.File("Content/forrods/mod.json", """{ "id": "forrods", "requires": { "rods": "" } }""");
        _temp.File("Content/forrods/cars/cars.json", """{ "cars": { "kept": { "in": true } } }""");

        var book = CarBook.Load(ModSet.Load(_temp.Combine("Content"), setting: "rods", exactSetting: true));
        Assert.Null(book.Car("free")!.In);
        Assert.Equal(1992, book.Car("free")!.Year);
        Assert.False(book.Car("gone")!.In);
        Assert.True(book.Car("kept")!.In);
        Assert.Contains(book.Problems, p => p.Contains("anywhere's cars.free.in"));
    }

    [Fact]
    public void The_catalog_stores_the_year_the_cars_own_data_gives_never_one_worked_out()
    {
        using var db = new LiteDB.LiteDatabase(new MemoryStream());
        var cars = db.GetCollection<CarDefinition>("catalog_cars");
        cars.Upsert(new CarDefinition { Id = "dated", Name = "Dated", UiYear = 1969 });
        cars.Upsert(new CarDefinition { Id = "named_2015", Name = "Named" });

        var raw = db.GetCollection("catalog_cars");
        Assert.Equal(1969, raw.FindById("dated")["Year"].AsInt32);
        Assert.True(raw.FindById("named_2015")["Year"].IsNull);
        Assert.Equal(1969, cars.FindById("dated").UiYear);
        Assert.Null(cars.FindById("named_2015").UiYear);
        Assert.Equal(2015, cars.FindById("named_2015").Year);
    }

    [Fact]
    public void A_setting_that_says_nothing_of_its_cars_is_told()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("Content/silent/mod.json", """{ "id": "silent", "type": "setting" }""");
        _temp.File("Content/picky/mod.json", """{ "id": "picky", "type": "setting" }""");
        _temp.File("Content/picky/game.json", """{ "cars": { "neverClasses": [ "race" ] } }""");

        GameFile.Load(ModSet.Load(_temp.Combine("Content"), setting: "picky", exactSetting: true));
        Assert.DoesNotContain(ModProblems.All, p => p.Contains("picky's game.json gives no \"cars\""));
        GameFile.Load(ModSet.Load(_temp.Combine("Content"), setting: "silent", exactSetting: true));
        Assert.Contains(ModProblems.All, p => p.Contains("silent's game.json gives no \"cars\""));
    }

    [Fact]
    public void A_setting_is_told_of_its_own_game_json_an_add_ons_cars_not_counted()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("Content/quiet/mod.json", """{ "id": "quiet", "type": "setting" }""");
        _temp.File("Content/quiet/game.json", """{ "title": "Quiet" }""");
        _temp.File("Content/banner/mod.json", """{ "id": "banner" }""");
        _temp.File("Content/banner/game.json", """{ "cars": { "never": [ "x" ] } }""");
        _temp.File("Content/upper/mod.json", """{ "id": "upper", "type": "setting" }""");
        _temp.File("Content/upper/game.json", """{ "Cars": { "from": 1950 } }""");

        GameFile.Load(ModSet.Load(_temp.Combine("Content"), setting: "quiet", exactSetting: true));
        Assert.Contains(ModProblems.All, p => p.Contains("quiet's game.json gives no \"cars\""));
        GameFile.Load(ModSet.Load(_temp.Combine("Content"), setting: "upper", exactSetting: true));
        Assert.DoesNotContain(ModProblems.All, p => p.Contains("upper's game.json gives no \"cars\""));
    }
}

