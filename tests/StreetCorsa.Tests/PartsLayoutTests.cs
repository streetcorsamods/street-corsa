using System.Numerics;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

public class PartsLayoutTests
{
    private static readonly CarAnchors Car = new(
        new Vector3(0.75f, 0.3f, 1.4f), new Vector3(-0.75f, 0.3f, 1.4f),
        new Vector3(0.75f, 0.3f, -1.3f), new Vector3(-0.75f, 0.3f, -1.3f));

    private static PartDefinition Block() => new()
    {
        Id = "engines/test/block",
        Slots =
        {
            new PartSlot { Id = 8, Name = "crankshaft", Position = new[] { 0f, -0.1f, 0.2f } },
            new PartSlot { Id = 14, Name = "intake", Position = new[] { 0f, 0.3f, 0.1f } },
        },
    };

    private static PartDefinition Carb() => new()
    {
        Id = "engines/modded/carb",
        Slots = { new PartSlot { Id = 1, Name = "base", Position = new[] { 0f, -0.05f, 0f } } },
    };

    [Fact]
    public void An_engine_is_drawn_at_the_size_it_was_made()
    {
        var world = CarPartsLayout.EnginePlacement(Car, Block());

        var span = Vector3.Transform(Vector3.UnitX, world) - Vector3.Transform(Vector3.Zero, world);
        Assert.Equal(1f, span.Length(), 4);
    }

    [Fact]
    public void A_part_sits_on_its_parent_slot_at_its_own_size()
    {
        var block = Block();
        var blockWorld = CarPartsLayout.EnginePlacement(Car, block);
        var carbWorld = PartAssembler.ChildWorld(block, 14, Carb(), 1, blockWorld)!.Value;

        // The carb's base lands on the block's intake pad
        var pad = Vector3.Transform(new Vector3(0f, 0.3f, 0.1f), blockWorld);
        Assert.True(Vector3.Distance(pad, Vector3.Transform(new Vector3(0f, -0.05f, 0f), carbWorld)) < 1e-5f);

        var span = Vector3.Transform(Vector3.UnitX, carbWorld) - Vector3.Transform(Vector3.Zero, carbWorld);
        Assert.Equal(1f, span.Length(), 4);
    }

    private static PartDefinition Brake(string cls) => new()
    {
        Id = "running_gear/test/" + cls,
        Class = cls,
        Slots = { new PartSlot { Id = 1, Name = "mount", Position = new[] { -0.08f, 0f, 0f } } },
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_disc_has_its_caliper_behind_the_rotor_on_both_sides(int corner)
    {
        // The front wheels are at +Z here, so behind is -Z; a disc's caliper is modelled behind the rotor, at its +Z
        var world = CarPartsLayout.CarLevelPlacement(null!, Car, Brake(PartClasses.DiscBrake), RunningGear.BrakeSlot(corner), default)!.Value;

        var back = Vector3.TransformNormal(Vector3.UnitZ, world);
        var outboard = Vector3.TransformNormal(Vector3.UnitX, world);
        Assert.True(back.Z < -0.99f);
        Assert.True(outboard.X * Car.Hub(corner).X > 0);
    }

    [Fact]
    public void A_drum_on_the_right_is_the_left_one_turned_round()
    {
        // Its wheel cylinder stays on top: turned about the vertical, not rolled over
        var world = CarPartsLayout.CarLevelPlacement(null!, Car, Brake(PartClasses.DrumBrake), RunningGear.BrakeSlot(1), default)!.Value;

        Assert.True(Vector3.TransformNormal(Vector3.UnitY, world).Y > 0.99f);
        Assert.True(Vector3.TransformNormal(Vector3.UnitX, world).X < -0.99f);
    }
}
