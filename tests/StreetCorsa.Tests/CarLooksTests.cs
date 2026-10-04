using System.Numerics;
using AcTools.Kn5File;
using StreetCorsa.Looks;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Scheduler.Tasks;

namespace StreetCorsa.Tests;

/// <summary>How used a car looks (docs/roadmap.md step 28, ideas 3 and 4): the rolls, rust, cracks, sag, repairs, AC and the wear layer</summary>
public sealed class CarLooksTests
{
    private static Car WithGear()
    {
        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true };
        TestCars.AddRunningGear(car, springs: true);
        return car;
    }

    [Fact]
    public void A_private_car_comes_rustier_and_dirtier_than_a_dealers_and_a_good_one_cleaner_than_a_rough_one()
    {
        double Mean(double condition, LookSource source, Func<CarLooks.Rolled, double> of)
        {
            var random = new Random(5);
            return Enumerable.Range(0, 400).Select(_ => of(CarLooks.Roll(condition, source, random))).Average();
        }

        Assert.True(Mean(0.5, LookSource.Private, r => r.Rust) > Mean(0.5, LookSource.Dealer, r => r.Rust) * 1.8);
        Assert.True(Mean(0.5, LookSource.Private, r => r.Paint) > Mean(0.5, LookSource.Dealer, r => r.Paint));
        Assert.True(Mean(0.5, LookSource.Private, r => r.Cracks) > Mean(0.5, LookSource.Dealer, r => r.Cracks));
        Assert.True(Mean(0.5, LookSource.Private, r => r.Dirt) > 0.15);
        Assert.True(Mean(0.5, LookSource.Dealer, r => r.Dirt) < 0.08);
        Assert.True(Mean(0.9, LookSource.Dealer, r => r.Rust) < Mean(0.4, LookSource.Dealer, r => r.Rust));
        // A car in perfect shape has nothing on it but what a private owner leaves (dirt)
        Assert.Equal(0, Mean(1, LookSource.Private, r => r.Rust + r.Paint + r.Cracks));
    }

    [Fact]
    public void A_car_bought_looks_as_it_did_on_the_lot_its_pattern_with_it()
    {
        var listing = new UsedCarListing { CarDefinitionId = "car_a", Condition = 0.5f };
        CarLooks.Give(listing, new CarLooks.Rolled(0.3, 0.2, 0.5, 0.1, 1234));

        var car = CarPurchaseService.CarFrom(listing, new DateTime(1969, 5, 1), "Player");

        Assert.Equal((0.3, 0.2, 0.5, 0.1, 1234), (car.Rust, car.PaintWear, car.GlassCracks, car.BodyDirt, car.LookSeed));
        Assert.Equal(CarLooks.Of(listing), CarLooks.Of(car));
    }

    [Fact]
    public void A_days_rust_is_the_same_look_so_a_scene_keeps_the_wear_it_has_on()
    {
        var car = new Car("car_a") { Rust = 0.3, PaintWear = 0.2, BodyDirt = 0.1 };
        var before = ShownSet.Of(null, look: CarLooks.Of(car));
        CarLooks.Age(car);
        Assert.NotEqual(0.3, car.Rust);

        Assert.Equal(before, ShownSet.Of(null, look: CarLooks.Of(car)));
        Assert.Equal(CarLooks.Of(car), CarLooks.Of(car).Rounded());
    }

    [Fact]
    public async Task Rust_grows_a_day_for_every_day_gone_by_since_it_last_grew()
    {
        var task = new RustTask();
        var daily = new Car("car_a") { Rust = 0.3 };
        var jumped = new Car("car_a") { Rust = 0.3 };
        var start = new DateTime(1969, 5, 1);

        var day = new GameState { Player = new Player { Cars = [daily] } };
        day.ScheduledTasks.Add(new ScheduledTaskState { TaskId = task.TaskId, LastExecuted = start });
        await task.ExecuteAsync(day, start.AddDays(1));

        var week = new GameState { Player = new Player { Cars = [jumped] } };
        week.ScheduledTasks.Add(new ScheduledTaskState { TaskId = task.TaskId, LastExecuted = start });
        await task.ExecuteAsync(week, start.AddDays(7));

        var expected = new Car("car_a") { Rust = 0.3 };
        CarLooks.Age(expected, 7);
        Assert.True(jumped.Rust > daily.Rust);
        Assert.Equal(expected.Rust, jumped.Rust, 9);

        // Never run before: one day's, not the age since the dawn of time
        var first = new Car("car_a") { Rust = 0.3 };
        await task.ExecuteAsync(new GameState { Player = new Player { Cars = [first] } }, start);
        Assert.Equal(daily.Rust, first.Rust, 9);
    }

    [Fact]
    public void Rust_grows_faster_the_more_there_is_and_not_at_all_on_a_sound_body()
    {
        var sound = new Car("car_a");
        var rusty = new Car("car_a") { Rust = 0.3 };
        var little = new Car("car_a") { Rust = 0.05 };
        CarLooks.Age(sound, 60);
        CarLooks.Age(rusty, 60);
        CarLooks.Age(little, 60);

        Assert.Equal(0, sound.Rust);
        Assert.True(rusty.Rust - 0.3 > little.Rust - 0.05);
        Assert.InRange(rusty.Rust, 0.33, 0.5);

        // Worn paint lets the water in: rust starts
        var chipped = new Car("car_a") { PaintWear = 0.8 };
        CarLooks.Age(chipped, 30);
        Assert.True(chipped.Rust > 0);
    }

    [Fact]
    public void A_hard_hit_at_the_front_cracks_the_windscreen_and_says_so()
    {
        var car = WithGear();
        var report = CarCondition.ApplyRace(car, new RaceCarCondition { BodyDamageKmh = [40, 0, 0, 0] }, 1, null);

        Assert.InRange(car.GlassCracks, 0.3, 0.6);
        Assert.Contains(report, l => l.Text.Contains("windscreen", StringComparison.OrdinalIgnoreCase));

        // A tap does not, nor does a hit elsewhere
        var tapped = WithGear();
        CarCondition.ApplyRace(tapped, new RaceCarCondition { BodyDamageKmh = [10, 60, 60, 0] }, 1, null);
        Assert.Equal(0, tapped.GlassCracks);
    }

    [Fact]
    public void A_broken_spring_sags_its_corner_and_a_bent_one_a_little_a_straight_one_not_at_all()
    {
        var car = WithGear();
        var corners = RunningGear.Mounted(car.Parts);
        corners[0].Spring!.Tear = 0;
        corners[1].Spring!.Tear = 0.3;
        corners[2].Spring!.Tear = 0.8;

        var heights = CarLooks.RideHeight(car);

        Assert.Equal(-0.05, heights[0], 6);
        Assert.Equal(-0.025, heights[1], 6);
        Assert.Equal(0, heights[2]);
        Assert.Equal(0, heights[3]);
        Assert.Equal(new CornerHeights(-0.05f, -0.025f, 0, 0), CornerHeights.Of(heights));
        Assert.Null(CornerHeights.Of(new double[4]));
    }

    [Fact]
    public void A_sagging_axle_goes_into_AC_as_a_shorter_push_rod()
    {
        const string suspensions = "[FRONT]\nROD_LENGTH=0.040\nTOE_OUT=0.001\n[REAR]\nROD_LENGTH=0.080\n";
        var start = new RaceStartState { RideHeight = [-0.05, -0.03, 0, 0] };

        var files = AcDamageData.Generate(start, name => name == "suspensions.ini" ? suspensions : null);

        var ini = new IniText(files["suspensions.ini"]);
        Assert.Equal(0.0, ini.GetNumber("FRONT", "ROD_LENGTH")!.Value, 4);
        Assert.Equal(0.08, ini.GetNumber("REAR", "ROD_LENGTH")!.Value, 4);
        Assert.Equal(0.001, ini.GetNumber("FRONT", "TOE_OUT")!.Value, 6);
        Assert.False(start.IsPristine);
    }

    [Fact]
    public void The_body_shop_takes_off_the_rust_and_paint_in_one_job_and_the_windscreen_in_another()
    {
        var car = new Car("car_a") { Rust = 0.4, PaintWear = 0.3, GlassCracks = 0.5 };
        var jobs = RepairShop.Jobs(car, null);

        var rust = Assert.Single(jobs, j => j.Id == RepairShop.Rust);
        Assert.Equal(RepairShop.Labour + Math.Round(RepairShop.RustCost * 0.4m + RepairShop.PaintCost * 0.3m), rust.Cost);
        Assert.DoesNotContain(jobs, j => j.Id == RepairShop.Paint);
        rust.Apply();
        Assert.Equal((0.0, 0.0), (car.Rust, car.PaintWear));

        var screen = Assert.Single(jobs, j => j.Id == RepairShop.Windscreen);
        screen.Apply();
        Assert.Equal(0, car.GlassCracks);

        // Worn paint without rust is a respray
        Assert.Contains(RepairShop.Jobs(new Car("car_a") { PaintWear = 0.5 }, null), j => j.Id == RepairShop.Paint);
        Assert.DoesNotContain(RepairShop.Jobs(new Car("car_a"), null), j => j.Id is RepairShop.Rust or RepairShop.Paint or RepairShop.Windscreen);
    }

    [Fact]
    public void Rust_and_worn_paint_take_off_what_a_car_is_worth()
    {
        var clean = new Car("car_a") { EngineHealth = 0.8, TransmissionHealth = 0.8, BodyCondition = 0.8, TireCondition = 0.8 };
        var rusty = new Car("car_a") { EngineHealth = 0.8, TransmissionHealth = 0.8, BodyCondition = 0.8, TireCondition = 0.8, Rust = 0.5, PaintWear = 0.5 };

        Assert.True(CarValuation.ValueOf(rusty, 10000m) < CarValuation.ValueOf(clean, 10000m) * 0.85m);
        Assert.Equal(1m, CarValuation.LooksFactor(0, 0, 0));
    }

    [Fact]
    public void The_scenes_and_the_race_see_the_look_a_clean_car_shows_nothing()
    {
        var look = new CarLook(0.2, 0.1, 0, 0.3, 10, 0, 0, 0, 7);
        var shown = ShownSet.Of(null, look: look);
        Assert.NotNull(shown);
        Assert.Equal(shown, ShownSet.Of(null, look: look with { }));
        Assert.Null(ShownSet.Of(null, look: new CarLook(0, 0, 0, 0, 0, 0, 0, 0, 7)));
        // The garage's parts view draws the car as a ghost: no wear on it
        Assert.Null(shown!.WithoutExtras());

        // A race names the layer's model; AC gets no dirt in it (it draws its own)
        var raced = shown.WithLayer(new ShownModel(Path.Combine(WearLayer.Folder, "car_a_1.kn5"), "x"));
        Assert.Contains(RaceStartState.ShownKeys(0, raced), k => k.Key == "CAR_0_LOOK" && k.Value == Path.Combine("looks", "car_a_1.kn5"));
        Assert.Contains(raced.Models, m => m.Name.StartsWith("looks"));
        Assert.Equal(0, look.WithoutDirt().Dirt);
        Assert.True(look.WithoutDirt() with { Rust = 0, Paint = 0, Front = 0 } is { IsClean: true });
    }

    /// <summary>A box of a car on four wheels, its paint all round, a windscreen at the front above the hood</summary>
    private static IKn5 BoxCar()
    {
        var kn5 = Kn5.CreateEmpty();
        var paint = PlaceholderMesh.SolidMaterial(kn5, "car_paint", 200, 30, 30, 0.5f, 0.5f, 0.3f, 30);
        var glass = PlaceholderMesh.SolidMaterial(kn5, "window_glass", 20, 20, 20, 0.5f, 0.5f, 0.3f, 30);
        var tyre = PlaceholderMesh.SolidMaterial(kn5, "tyre", 10, 10, 10, 0.5f, 0.5f, 0.1f, 10);
        kn5.RootNode.Children.Add(PlaceholderMesh.Boxes("body", [(new Vector3(0, 0.75f, 0), new Vector3(0.9f, 0.4f, 2.2f))], paint));
        kn5.RootNode.Children.Add(PlaceholderMesh.Boxes("screen", [(new Vector3(0, 1.3f, 0.6f), new Vector3(0.7f, 0.15f, 0.01f))], glass));
        foreach (var (name, x, z) in new[] { ("WHEEL_LF", 0.8f, 1.4f), ("WHEEL_RF", -0.8f, 1.4f), ("WHEEL_LR", 0.8f, -1.4f), ("WHEEL_RR", -0.8f, -1.4f) })
        {
            var wheel = Kn5Node.CreateBaseNode(name);
            wheel.Transform = Helpers.Matrices.ToMat4x4(Matrix4x4.CreateTranslation(x, 0.33f, z));
            wheel.Children.Add(PlaceholderMesh.Boxes(name + "_tyre", [(Vector3.Zero, new Vector3(0.1f, 0.33f, 0.33f))], tyre));
            kn5.RootNode.Children.Add(wheel);
        }
        return kn5;
    }

    [Fact]
    public void The_wear_layer_lies_over_the_paint_seen_from_outside_and_the_windscreen_never_the_wheels()
    {
        var shell = BodyShell.Of(BoxCar(), 512)!;

        Assert.Equal(["car_paint"], shell.Paint);
        Assert.Equal(1, shell.FrontSign);
        Assert.Equal(1, shell.LeftSign);
        Assert.Equal(4, shell.Hubs.Count);
        var body = shell.BodyMeshes.SelectMany(m => m.Vertices).ToList();
        Assert.NotEmpty(body);
        Assert.All(body, v => Assert.InRange(v.Tex.X, 0, 1));
        Assert.All(body, v => Assert.InRange(v.Tex.Y, 0, 1));
        // Stood off the paint, never on a wheel (the box's side is at x 0.9, the tyres reach 0.9 too but carry none)
        Assert.All(body, v => Assert.True(Math.Abs(v.Position.X) <= 0.9f + BodyShell.StandOff + 1e-4f));
        Assert.Contains(body, v => Math.Abs(v.Position.X - (0.9f + BodyShell.StandOff)) < 1e-4f);
        Assert.NotEmpty(shell.ScreenMeshes);
        // The box's bottom is under the car, seen by nothing from outside: no wear there
        Assert.DoesNotContain(body, v => v.Position.Y < 0.35f + BodyShell.StandOff - 0.001f && v.Normal.Y < -0.9f);
    }

    [Fact]
    public void A_race_writes_its_layer_with_PNG_pictures_once_for_a_look()
    {
        using var temp = new TempDir();
        var carFolder = temp.Combine("box_car");
        Directory.CreateDirectory(carFolder);
        BoxCar().Save(Path.Combine(carFolder, "box_car.kn5"));
        var look = new CarLook(0.4, 0.3, 0.6, 0.2, 30, 0, 0, 0, 3);

        var name = WearLayer.WriteFor(carFolder, look, temp.Combine("looks"))!;

        var layer = Kn5.FromFile(temp.Combine("looks", name));
        Assert.All(layer.TexturesData.Values, png => Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png[..4]));
        Assert.Equal(name, WearLayer.WriteFor(carFolder, look, temp.Combine("looks")));

        // The car's model changed (a mod updated): its layer is another file, not the old body's
        File.SetLastWriteTimeUtc(Path.Combine(carFolder, "box_car.kn5"), DateTime.UtcNow.AddDays(1));
        Assert.NotEqual(name, WearLayer.WriteFor(carFolder, look, temp.Combine("looks")));
    }

    [Fact]
    public void A_shell_kept_on_disk_comes_back_as_it_was_and_a_bad_file_is_no_file()
    {
        using var temp = new TempDir();
        var shell = BodyShell.Of(BoxCar(), 512)!;
        var path = temp.Combine("box.shell");

        ShellFile.Write(path, shell);
        Assert.True(ShellFile.TryRead(path, out var back));
        Assert.Equal(shell.Paint, back!.Paint);
        Assert.Equal(shell.Panels, back.Panels);
        Assert.Equal(shell.BodyMeshes.Sum(m => m.Vertices.Length), back.BodyMeshes.Sum(m => m.Vertices.Length));
        Assert.Equal(shell.BodyMeshes[0].Indices, back.BodyMeshes[0].Indices);
        Assert.Equal(shell.BodyMeshes[0].Vertices[5].Tex, back.BodyMeshes[0].Vertices[5].Tex);
        Assert.Equal(shell.Hubs, back.Hubs);

        // A model with no paint is kept as such; a file cut short is no file
        ShellFile.Write(path, null);
        Assert.True(ShellFile.TryRead(path, out var none));
        Assert.Null(none);
        ShellFile.Write(path, shell);
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..100]);
        Assert.False(ShellFile.TryRead(path, out _));

        // A file with a count that is no count (a bad int, a file of another kind with the same magic) is no file either,
        // not an array the size of the moon
        ShellFile.Write(path, shell);
        var bytes = File.ReadAllBytes(path);
        // After the magic (a length byte and 8 chars), the flag, two vectors and two signs: the hubs' count
        var hubsAt = 1 + 8 + 1 + 6 * 4 + 2 * 4;
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, hubsAt);
        File.WriteAllBytes(path, bytes);
        Assert.False(ShellFile.TryRead(path, out _));
        BitConverter.GetBytes(-5).CopyTo(bytes, hubsAt);
        File.WriteAllBytes(path, bytes);
        Assert.False(ShellFile.TryRead(path, out _));
    }

    [Fact]
    public void A_look_makes_a_layer_with_its_pictures_and_a_clean_car_none()
    {
        var car = BoxCar();
        var layer = WearLayer.For(car, new CarLook(0.4, 0.3, 0.6, 0.2, 30, 0, 0, 0, 3))!;

        Assert.Contains(WearLayer.BodyMaterial, layer.Materials.Keys);
        Assert.Contains(WearLayer.ScreenMaterial, layer.Materials.Keys);
        // A scene's pictures are DDS with their mips (nothing to decode as they load)
        Assert.All(layer.TexturesData.Values, dds => Assert.Equal("DDS "u8.ToArray(), dds[..4]));
        Assert.Equal(128 + (1024 * 1024 + 512 * 512 + 256 * 256 + 128 * 128 + 64 * 64 + 32 * 32 + 16 * 16 + 8 * 8 + 4 * 4 + 2 * 2 + 1) * 4,
            WearLayer.For(car, new CarLook(0.4, 0, 0, 0, 0, 0, 0, 0, 3), WearLayer.LotPicture)!.TexturesData.Values.Single().Length);
        Assert.All(layer.Materials.Values, m => Assert.Equal(Kn5MaterialBlendMode.AlphaBlend, m.BlendMode));
        Assert.Null(WearLayer.For(car, new CarLook(0, 0, 0, 0, 0, 0, 0, 0, 3)));
        // No cracks, no windscreen in the layer
        Assert.DoesNotContain(WearLayer.ScreenMaterial, WearLayer.For(car, new CarLook(0.4, 0, 0, 0, 0, 0, 0, 0, 3))!.Materials.Keys);
    }
}
