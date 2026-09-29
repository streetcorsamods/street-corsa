using AcTools.Kn5File;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.CarLot;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>The car lot: where the player's cars are kept, and the warehouse they are kept in</summary>
public class CarLotTests
{
    private static Car Kept(int? warehouse, int? bay) => new("car") { Warehouse = warehouse, Bay = bay };

    #region Where the cars are kept

    [Fact]
    public void Cars_without_a_place_fill_the_first_warehouse_bay_by_bay()
    {
        var cars = Enumerable.Range(0, 3).Select(_ => Kept(null, null)).ToList();

        Assert.True(CarStorage.Arrange(cars, _ => 10, null));

        Assert.All(cars, c => Assert.Equal(1, c.Warehouse));
        Assert.Equal([0, 1, 2], cars.Select(c => c.Bay!.Value));
    }

    [Fact]
    public void A_full_warehouse_spills_into_the_next_and_there_is_no_limit()
    {
        var cars = Enumerable.Range(0, 25).Select(_ => Kept(null, null)).ToList();

        CarStorage.Arrange(cars, _ => 10, null);

        Assert.Equal(3, CarStorage.WarehouseCount(cars));
        Assert.Equal(10, CarStorage.In(cars, 1).Count);
        Assert.Equal(10, CarStorage.In(cars, 2).Count);
        Assert.Equal(5, CarStorage.In(cars, 3).Count);
        Assert.Equal(cars.Count, cars.Select(c => (c.Warehouse, c.Bay)).Distinct().Count());
    }

    [Fact]
    public void A_car_keeps_its_place_and_a_new_one_takes_the_first_gap()
    {
        var kept = Kept(1, 4);
        var other = Kept(2, 0);
        var bought = Kept(null, null);
        var cars = new List<Car> { kept, other, bought };

        Assert.True(CarStorage.Arrange(cars, _ => 10, null));

        Assert.Equal((1, 4), (kept.Warehouse!.Value, kept.Bay!.Value));
        Assert.Equal((2, 0), (other.Warehouse!.Value, other.Bay!.Value));
        Assert.Equal((1, 0), (bought.Warehouse!.Value, bought.Bay!.Value));
        Assert.False(CarStorage.Arrange(cars, _ => 10, null));
    }

    [Fact]
    public void Of_two_cars_in_one_bay_the_one_had_longer_keeps_it()
    {
        // A car sold and won back comes home with the place it had, which somebody else has now
        var had = Kept(1, 0);
        var back = Kept(1, 0);
        var cars = new List<Car> { had, back };

        CarStorage.Arrange(cars, _ => 10, null);

        Assert.Equal(0, had.Bay);
        Assert.Equal(1, back.Bay);
    }

    [Fact]
    public void A_place_that_is_not_there_is_given_up()
    {
        var cars = new List<Car> { Kept(1, 12), Kept(0, 0), Kept(1, -1), Kept(3, null) };

        CarStorage.Arrange(cars, _ => 10, null);

        Assert.Equal([0, 1, 2, 3], cars.Select(c => c.Bay!.Value));
        Assert.All(cars, c => Assert.Equal(1, c.Warehouse));
    }

    [Fact]
    public void An_emptied_warehouse_between_full_ones_stays_and_is_filled_first()
    {
        var cars = new List<Car> { Kept(1, 0), Kept(3, 0), Kept(null, null) };

        CarStorage.Arrange(cars, _ => 1, null);

        Assert.Equal(3, CarStorage.WarehouseCount(cars));
        Assert.Equal(2, cars[2].Warehouse);
    }

    [Fact]
    public void When_a_car_goes_the_others_stay_where_they_are()
    {
        var cars = Enumerable.Range(0, 12).Select(_ => Kept(null, null)).ToList();
        CarStorage.Arrange(cars, _ => 10, null);
        var places = cars.ToDictionary(c => c.InstanceId, c => (c.Warehouse, c.Bay));

        // Sold from bay 3 of warehouse 1, and the first car of warehouse 2 lost in a pink slip race
        cars.RemoveAt(2);
        cars.RemoveAt(9);

        Assert.False(CarStorage.Arrange(cars, _ => 10, null));
        Assert.All(cars, c => Assert.Equal(places[c.InstanceId], (c.Warehouse, c.Bay)));
        Assert.Equal(2, CarStorage.WarehouseCount(cars));
    }

    [Fact]
    public void A_car_taken_to_the_garage_leaves_its_bay_empty_and_nobody_moves_up()
    {
        var cars = Enumerable.Range(0, 5).Select(_ => Kept(null, null)).ToList();
        CarStorage.Arrange(cars, _ => 10, null);

        // The garage's car was sold, and the game gave the player the first car in the list to drive
        Assert.True(CarStorage.Arrange(cars, _ => 10, cars[0].InstanceId));

        Assert.Null(cars[0].Bay);
        Assert.Equal([1, 2, 3, 4], cars.Skip(1).Select(c => c.Bay!.Value));
    }

    [Fact]
    public void A_new_car_takes_the_gap_a_car_left()
    {
        var cars = Enumerable.Range(0, 4).Select(_ => Kept(null, null)).ToList();
        CarStorage.Arrange(cars, _ => 10, null);
        cars.RemoveAt(1);

        var bought = Kept(null, null);
        cars.Add(bought);
        CarStorage.Arrange(cars, _ => 10, null);

        Assert.Equal(1, bought.Bay);
        Assert.Equal([0, 2, 3], cars.Take(3).Select(c => c.Bay!.Value));
    }

    [Fact]
    public void The_car_in_the_garage_is_in_no_warehouse()
    {
        var driven = Kept(1, 0);
        var parked = Kept(1, 1);
        var cars = new List<Car> { driven, parked };

        Assert.True(CarStorage.Arrange(cars, _ => 10, driven.InstanceId));

        Assert.Null(driven.Warehouse);
        Assert.Null(driven.Bay);
        Assert.Equal((1, 1), (parked.Warehouse!.Value, parked.Bay!.Value));
        Assert.Equal([parked], CarStorage.In(cars, 1));
        Assert.False(CarStorage.Arrange(cars, _ => 10, driven.InstanceId));
    }

    [Fact]
    public void Taking_a_car_out_parks_the_one_from_the_garage_in_its_bay()
    {
        var driven = Kept(null, null);
        var taken = Kept(2, 7);

        CarStorage.TakeOut(taken, driven);

        Assert.Equal((2, 7), (driven.Warehouse!.Value, driven.Bay!.Value));
        Assert.Null(taken.Warehouse);
        Assert.Null(taken.Bay);

        // With nothing in the garage, the bay is simply left
        var alone = Kept(1, 3);
        CarStorage.TakeOut(alone, null);
        Assert.Null(alone.Warehouse);
    }

    [Fact]
    public void A_car_that_changes_hands_comes_back_without_its_old_place()
    {
        // Lost from warehouse 5 long ago; the player now has one warehouse and buys it back
        var here = Kept(1, 0);
        var back = Kept(5, 3);
        back.ClearPlace();

        Assert.True(CarStorage.Arrange([here, back], _ => 10, null));

        Assert.Equal((1, 1), (back.Warehouse!.Value, back.Bay!.Value));
        Assert.Equal(1, CarStorage.WarehouseCount([here, back]));
    }

    [Fact]
    public void An_impounded_car_keeps_its_bay_but_is_not_standing_in_it()
    {
        var parked = Kept(1, 0);
        var impounded = new Car("car") { Warehouse = 1, Bay = 1, ImpoundedUntil = new DateTime(1969, 6, 1) };
        var newcomer = Kept(null, null);

        Assert.True(CarStorage.Arrange([parked, impounded, newcomer], _ => 10, null));

        // Its bay waits for it: the newcomer takes the next one
        Assert.Equal((1, 1), (impounded.Warehouse!.Value, impounded.Bay!.Value));
        Assert.Equal((1, 2), (newcomer.Warehouse!.Value, newcomer.Bay!.Value));
        Assert.Equal([parked, newcomer], CarStorage.In([parked, impounded, newcomer], 1));

        impounded.ImpoundedUntil = null;
        Assert.Equal([parked, impounded, newcomer], CarStorage.In([parked, impounded, newcomer], 1));
    }

    [Fact]
    public void A_player_without_cars_still_has_a_warehouse()
    {
        Assert.Equal(1, CarStorage.WarehouseCount([]));
        Assert.False(CarStorage.Arrange([], _ => 10, null));
    }

    #endregion

    #region The warehouse

    [Fact]
    public void The_shipped_warehouse_loads_with_its_bays_lights_and_looks()
    {
        var scene = Assert.Single(WarehouseScenes.LoadAll(ModSet.Load(RepoPaths.Content)));

        Assert.Equal("warehouse", scene.Id);
        Assert.Equal(10, scene.Capacity);
        Assert.True(File.Exists(scene.Kn5));
        Assert.Equal(Enum.GetValues<StreetLight>().Length, scene.Lights.Count);
        Assert.All(scene.Lights.Values, l => Assert.NotEmpty(l.Lamps));
        Assert.True(scene.Looks.Count >= 2);
        Assert.True(scene.Camera.Radius is > 5f and < 14f);

        // The camera's view down the aisle is inside the walls and under the roof it is kept in
        var room = Assert.NotNull(scene.Room);
        Assert.True(scene.Camera.Radius * MathF.Cos(scene.Camera.Beta) < room.HalfLength);
        Assert.True(scene.Camera.TargetHeight + scene.Camera.Radius * MathF.Sin(scene.Camera.Beta) < room.Height);

        // No two cars stand in each other: bays a car's width and more apart, all inside the walls
        foreach (var a in scene.Bays)
        {
            Assert.InRange(Math.Abs(a.X), 0f, room.HalfLength - 2f);
            Assert.InRange(Math.Abs(a.Z), 0f, room.HalfWidth - 2f);
            foreach (var b in scene.Bays.Where(b => b != a))
                Assert.True(MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z)) > 4f);
        }
    }

    [Fact]
    public void Every_picture_a_warehouse_can_wear_is_in_its_model()
    {
        var scene = Assert.Single(WarehouseScenes.LoadAll(ModSet.Load(RepoPaths.Content)));
        var kn5 = Kn5.FromFile(scene.Kn5);

        for (var number = 1; number <= 100; number++)
        {
            var dressing = scene.Dress(number, number - 1, StreetLight.Day);
            foreach (var (material, picture) in dressing.Pictures)
            {
                Assert.True(kn5.Materials.ContainsKey(material), material);
                Assert.True(kn5.Textures.ContainsKey(picture), picture);
            }

            foreach (var material in dressing.Glow.Keys)
                Assert.True(kn5.Materials.ContainsKey(material), material);
        }
    }

    [Fact]
    public void Each_warehouse_wears_the_next_look_and_its_own_number()
    {
        var scene = Assert.Single(WarehouseScenes.LoadAll(ModSet.Load(RepoPaths.Content)));

        var first = scene.Dress(1, 0, StreetLight.Night);
        var second = scene.Dress(2, 1, StreetLight.Night);
        var twelfth = scene.Dress(12, 11, StreetLight.Night);

        Assert.NotEqual(first.Pictures["wh_wall"], second.Pictures["wh_wall"]);
        Assert.Equal("sign_digit_0.jpg", first.Pictures["wh_digit_tens"]);
        Assert.Equal("sign_digit_1.jpg", first.Pictures["wh_digit_units"]);
        Assert.Equal("sign_digit_1.jpg", twelfth.Pictures["wh_digit_tens"]);
        Assert.Equal("sign_digit_2.jpg", twelfth.Pictures["wh_digit_units"]);

        // The lamps take the look's colour; the skylights are dark at night
        Assert.All(first.Lighting.Lamps, l => Assert.Equal(scene.Looks[0].LampColor, l.Color));
        Assert.True(first.Glow[WarehouseScene.SkylightMaterial] < scene.Dress(1, 0, StreetLight.Day).Glow[WarehouseScene.SkylightMaterial]);
    }

    [Fact]
    public void A_warehouse_without_bays_or_a_model_is_left_out()
    {
        using var dir = new TempDir();
        dir.File("nobays/warehouse.json", """{ "id": "nobays", "bays": [] }""");
        dir.File("nomodel/warehouse.json", """{ "id": "nomodel", "bays": [ { "x": 0, "z": 0, "heading": 0 } ] }""");

        Assert.Null(WarehouseScenes.Load(dir.Combine("nobays")));
        Assert.Null(WarehouseScenes.Load(dir.Combine("nomodel")));
        Assert.Null(WarehouseScenes.Load(dir.Combine("missing")));
    }

    [Fact]
    public void A_mod_warehouse_with_empty_entries_and_a_bad_sign_picture_loads_without_them()
    {
        using var dir = new TempDir();
        dir.File("broken/warehouse.kn5", "");
        dir.File("broken/warehouse.json", """
            {
              "bays": [ null, { "x": 1, "z": 2, "heading": 90 } ],
              "lamps": [ null ],
              "lights": { "day": null, "night": { "brightness": 0.2 } },
              "looks": [ null, { "surfaces": { "wh_wall": null, "wh_floor": "floor.jpg" } } ],
              "digits": { "tens": "wh_digit_tens", "units": "wh_digit_units", "picture": "digit_{0}_{1}.jpg" }
            }
            """);

        var scene = Assert.IsType<WarehouseScene>(WarehouseScenes.Load(dir.Combine("broken")));

        Assert.Equal([new WarehouseBay(1f, 2f, 90f)], scene.Bays);
        Assert.Equal([StreetLight.Night], scene.Lights.Keys);
        Assert.Equal("floor.jpg", Assert.Single(Assert.Single(scene.Looks).Surfaces).Value);
        Assert.Null(scene.DigitPicture);
        Assert.Null(scene.Room);

        // Dressed for any number without the sign: the picture that would not format is gone
        var dressing = scene.Dress(12, 0, StreetLight.Day);
        Assert.False(dressing.Pictures.ContainsKey("wh_digit_tens"));
    }

    #endregion
}
