using System.Numerics;
using StreetCorsa.Parts;

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
}
