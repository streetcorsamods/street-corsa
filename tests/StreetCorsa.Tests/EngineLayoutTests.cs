using System.IO;
using System.Numerics;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Export;
using StreetCorsa.Screens.CarCatalogEditor;

namespace StreetCorsa.Tests;

public class EngineLayoutTests
{
    // Facing +Z, front axle at 1.4, rear axle at -1.3, hubs 0.3 m up
    private static readonly CarAnchors Car = new(
        new Vector3(0.75f, 0.3f, 1.4f), new Vector3(-0.75f, 0.3f, 1.4f),
        new Vector3(0.75f, 0.3f, -1.3f), new Vector3(-0.75f, 0.3f, -1.3f));

    private static readonly (Vector3 Min, Vector3 Max) BlockBounds = (new Vector3(-0.3f, -0.3f, -0.35f), new Vector3(0.3f, 0.3f, 0.35f));

    private static PartDefinition Block() => new()
    {
        Id = "engines/test/block",
        Slots = { new PartSlot { Id = 8, Name = "crankshaft", Position = new[] { 0f, -0.1f, 0.2f } } },
    };

    /// <summary>Which way the block's front points in the car: part models have their front towards -Z</summary>
    private static Vector3 FrontOf(Matrix4x4 world) => Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, world));

    private static Vector3 RearFaceOf(Matrix4x4 world) => Vector3.Transform(new Vector3(0f, -0.1f, BlockBounds.Max.Z), world);

    [Fact]
    public void A_front_engine_sits_behind_the_front_axle_facing_forward()
    {
        var world = CarPartsLayout.EnginePlacement(Car, Block(), EngineLayout.Front, BlockBounds);

        Assert.True(Vector3.Distance(FrontOf(world), Vector3.UnitZ) < 1e-5f);
        Assert.Equal(1.4f - 0.18f, Vector3.Transform(Vector3.Zero, world).Z, 4);
    }

    [Fact]
    public void A_mid_engine_faces_forward_with_its_gearbox_towards_the_rear_axle()
    {
        var world = CarPartsLayout.EnginePlacement(Car, Block(), EngineLayout.Mid, BlockBounds);

        Assert.True(Vector3.Distance(FrontOf(world), Vector3.UnitZ) < 1e-5f);

        // Rear face, where the gearbox bolts on, ahead of the rear axle; the whole block between the axles
        var rearFace = RearFaceOf(world);
        Assert.Equal(-1.3f + 0.4f, rearFace.Z, 4);
        Assert.InRange(Vector3.Transform(Vector3.Zero, world).Z, -1.3f, 1.4f);
    }

    [Fact]
    public void A_rear_engine_is_turned_around_behind_the_rear_axle()
    {
        var world = CarPartsLayout.EnginePlacement(Car, Block(), EngineLayout.Rear, BlockBounds);

        Assert.True(Vector3.Distance(FrontOf(world), -Vector3.UnitZ) < 1e-5f);

        // Its rear face, and the gearbox on it, towards the rear axle; the block itself behind it
        Assert.Equal(-1.3f - 0.05f, RearFaceOf(world).Z, 4);
        Assert.True(Vector3.Transform(Vector3.Zero, world).Z < -1.3f);
    }

    [Theory]
    [InlineData(EngineLayout.Front)]
    [InlineData(EngineLayout.Mid)]
    [InlineData(EngineLayout.Rear)]
    public void Every_layout_keeps_the_crankshaft_on_the_centre_line_above_the_hubs_at_full_size(EngineLayout layout)
    {
        var world = CarPartsLayout.EnginePlacement(Car, Block(), layout, BlockBounds);

        var crank = Vector3.Transform(new Vector3(0f, -0.1f, 0f), world);
        Assert.Equal(0f, crank.X, 4);
        Assert.Equal(0.3f + 0.06f, crank.Y, 4);

        var span = Vector3.Transform(Vector3.UnitX, world) - Vector3.Transform(Vector3.Zero, world);
        Assert.Equal(1f, span.Length(), 4);
    }

    [Fact]
    public void A_block_that_cannot_be_measured_is_placed_as_one_of_the_usual_length()
    {
        var measured = CarPartsLayout.EnginePlacement(Car, Block(), EngineLayout.Rear, (new Vector3(-0.3f), new Vector3(0.3f)));
        var unmeasured = CarPartsLayout.EnginePlacement(Car, Block(), EngineLayout.Rear);

        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.Zero, measured), Vector3.Transform(Vector3.Zero, unmeasured)) < 1e-5f);
    }

    [Theory]
    [InlineData(EngineLayout.Front)]
    [InlineData(EngineLayout.Rear)]
    public void A_loose_brake_goes_on_its_own_hub_not_in_the_engine_place(EngineLayout layout)
    {
        var catalog = PartsCatalog.Empty(Path.GetTempPath());
        var brake = new PartDefinition { Id = "brakes/test/disc", Slots = { new PartSlot { Id = 1, Position = new[] { 0f, 0f, 0f } } } };

        // One place per corner, each on its own hub, whatever the engine layout
        var places = Enumerable.Range(0, 4)
            .Select(corner => CarPartsLayout.CarLevelPlacement(catalog, Car, brake, Parts.Cars.RunningGear.BrakeSlot(corner), layout)!.Value)
            .Select(world => Vector3.Transform(Vector3.Zero, world))
            .ToList();

        for (var corner = 0; corner < 4; corner++)
        {
            Assert.True(Vector3.Distance(places[corner], Car.Hub(corner)) < 0.1f, $"corner {corner} at {places[corner]}");
        }

        // What goes on the car and is no running gear is the engine
        var engine = CarPartsLayout.CarLevelPlacement(catalog, Car, Block(), 1, layout)!.Value;
        Assert.Equal(CarPartsLayout.EnginePlacement(catalog, Car, Block(), layout), engine);
    }

    [Theory]
    [InlineData(0.38, "RWD", EngineLayout.Rear)]   // Corvair
    [InlineData(0.46, "RWD", EngineLayout.Front)]  // the lightest nose of the installed front-engined cars
    [InlineData(0.55, "RWD", EngineLayout.Front)]
    [InlineData(0.38, "FWD", EngineLayout.FrontTransverse)]  // nearly every front-wheel-drive car has its engine across
    [InlineData(0.40, "AWD", EngineLayout.Rear)]
    [InlineData(0.40, null, EngineLayout.Rear)]
    public void The_layout_is_guessed_from_the_weight_and_the_driven_wheels(double frontShare, string? traction, EngineLayout expected)
    {
        Assert.Equal(expected, EngineLayouts.Guess(new AcCarSpecs { FrontWeightShare = frontShare, Traction = traction }));
    }

    [Fact]
    public void An_engine_across_the_car_has_its_crankshaft_side_to_side_and_the_block_and_gearbox_about_the_middle()
    {
        var front = CarPartsLayout.EnginePlacement(Car, Block(), EngineLayout.FrontTransverse, BlockBounds);
        var across = FrontOf(front);
        Assert.True(Math.Abs(across.Z) < 1e-5f && Math.Abs(Math.Abs(across.X) - 1) < 1e-5f, $"the block's front points {across}");

        // The crankshaft a little ahead of the front axle, the block's middle off the centre line towards its front
        var crank = Vector3.Transform(new Vector3(0f, -0.1f, 0f), front);
        Assert.Equal(1.4f + 0.08f, crank.Z, 4);
        Assert.Equal(0.3f + 0.06f, crank.Y, 4);
        Assert.Equal(0.15f, Vector3.Dot(crank, across), 4);

        var mid = CarPartsLayout.EnginePlacement(Car, Block(), EngineLayout.MidTransverse, BlockBounds);
        Assert.Equal(-1.3f + 0.25f, Vector3.Transform(new Vector3(0f, -0.1f, 0f), mid).Z, 4);
        Assert.True(Vector3.Distance(FrontOf(mid), across) < 1e-5f);
    }

    [Theory]
    [InlineData("front", EngineLayout.Front)]
    [InlineData("Rear", EngineLayout.Rear)]
    [InlineData("front_transverse", EngineLayout.FrontTransverse)]
    [InlineData("MidTransverse", EngineLayout.MidTransverse)]
    [InlineData("sideways", null)]
    [InlineData("1", null)]
    [InlineData("", null)]
    [InlineData("mid, rear", null)]
    [InlineData("front, mid", null)]
    public void A_layout_is_read_by_its_name(string name, EngineLayout? expected)
    {
        Assert.Equal(expected, EngineLayouts.Parse(name));
        if (expected is { } layout) Assert.Equal(layout, EngineLayouts.Parse(layout.Name()));
    }

    [Fact]
    public void Only_the_front_layouts_are_carried_by_the_nose_and_only_the_transverse_ones_lie_across()
    {
        Assert.Equal(new[] { EngineLayout.Front, EngineLayout.FrontTransverse }, Enum.GetValues<EngineLayout>().Where(l => l.IsFront()));
        Assert.Equal(new[] { EngineLayout.FrontTransverse, EngineLayout.MidTransverse }, Enum.GetValues<EngineLayout>().Where(l => l.IsTransverse()));
        Assert.Equal("front_transverse", EngineLayout.FrontTransverse.Name());
    }

    [Fact]
    public void A_car_without_data_has_its_engine_in_the_front()
    {
        Assert.Equal(EngineLayout.Front, EngineLayouts.Guess(null));
    }

    [Fact]
    public void The_driven_wheels_are_read_from_the_drivetrain()
    {
        var files = new Dictionary<string, string>
        {
            ["suspensions.ini"] = "[BASIC]\nCG_LOCATION=0.38\n",
            ["drivetrain.ini"] = "[TRACTION]\nTYPE=rwd ; rear\n",
        };

        var specs = AcCarSpecs.Read(name => files.GetValueOrDefault(name));

        Assert.Equal("RWD", specs.Traction);
        Assert.Equal(EngineLayout.Rear, EngineLayouts.Guess(specs));
        Assert.Null(AcCarSpecs.Read(_ => null).Traction);
    }

    private static CatalogItemViewModel Row(CarProfile profile, EngineLayout guess = EngineLayout.Rear) =>
        new(new CarDefinition { Id = "chevrolet_corvair_69" }, profile, false, Array.Empty<EngineOptionViewModel>, () => guess);

    [Fact]
    public async Task The_catalog_editor_offers_the_guess_first_and_saves_a_layout_picked_by_hand()
    {
        var profile = new CarProfile { CarDefinitionId = "chevrolet_corvair_69" };
        var row = Row(profile);

        await row.EngineLayoutOption!.Guessing;
        Assert.Equal("Guessed: Rear", row.EngineLayoutOption.Label);
        Assert.Null(row.EngineLayoutOption.Layout);

        row.EngineLayoutOption = row.EngineLayoutOptions.Single(o => o.Layout == EngineLayout.Mid);
        Assert.True(row.IsDirty);

        row.ApplyChanges();
        Assert.Equal(EngineLayout.Mid, profile.EngineLayout);
    }

    [Fact]
    public async Task The_guess_does_not_hold_up_the_row_and_a_failed_one_is_the_front()
    {
        // The data read the guess makes: the row's options are there before it is done
        using var dataRead = new ManualResetEventSlim();
        var row = new CatalogItemViewModel(new CarDefinition { Id = "x" }, new CarProfile(), false, Array.Empty<EngineOptionViewModel>, () =>
        {
            dataRead.Wait(TimeSpan.FromSeconds(10));
            return EngineLayout.Rear;
        });

        var guess = row.EngineLayoutOptions[0];
        Assert.Equal("Guessed: …", guess.Label);
        Assert.False(guess.Guessing.IsCompleted);

        dataRead.Set();
        await guess.Guessing;
        Assert.Equal("Guessed: Rear", guess.Label);

        var failing = new CatalogItemViewModel(new CarDefinition { Id = "y" }, new CarProfile(), false, Array.Empty<EngineOptionViewModel>,
            () => throw new IOException("data.acd is locked"));
        await failing.EngineLayoutOptions[0].Guessing;
        Assert.Equal("Guessed: Front", failing.EngineLayoutOptions[0].Label);
    }

    [Fact]
    public void A_layout_from_another_row_is_ignored_and_a_reset_goes_back_to_the_guess()
    {
        var profile = new CarProfile { CarDefinitionId = "chevrolet_corvair_69", EngineLayout = EngineLayout.Rear };
        var row = Row(profile);
        var other = Row(new CarProfile());

        // The list recycling the row's ComboBox for another car
        row.EngineLayoutOption = other.EngineLayoutOptions[1];
        Assert.Equal(EngineLayout.Rear, row.EngineLayoutOption!.Layout);
        Assert.False(row.IsDirty);

        row.SetFromGeneratedProfile(new CarProfile());
        row.ApplyChanges();
        Assert.Null(profile.EngineLayout);
    }
}
