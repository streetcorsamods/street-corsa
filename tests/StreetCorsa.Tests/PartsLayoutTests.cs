using System.Numerics;
using StreetCorsa.Parts;

namespace StreetCorsa.Tests;

public class PartsLayoutTests
{
    private static readonly CarAnchors Car = new(
        new Vector3(0.75f, 0.3f, 1.4f), new Vector3(-0.75f, 0.3f, 1.4f),
        new Vector3(0.75f, 0.3f, -1.3f), new Vector3(-0.75f, 0.3f, -1.3f));

    private static PartDefinition Block(float scale) => new()
    {
        Id = "engines/test/block",
        ModelScale = scale,
        Slots =
        {
            new PartSlot { Id = 8, Name = "crankshaft", Position = new[] { 0f, -0.1f, 0.2f } },
            new PartSlot { Id = 14, Name = "intake", Position = new[] { 0f, 0.3f, 0.1f } },
        },
    };

    private static PartDefinition Carb(float scale) => new()
    {
        Id = "engines/modded/carb",
        ModelScale = scale,
        Slots = { new PartSlot { Id = 1, Name = "base", Position = new[] { 0f, -0.05f, 0f } } },
    };

    [Fact]
    public void A_bigger_engine_grows_around_its_crankshaft()
    {
        var crank = new Vector3(0f, -0.1f, 0f);
        var normal = CarPartsLayout.EnginePlacement(Car, Block(1f));
        var bigger = CarPartsLayout.EnginePlacement(Car, Block(1.2f));

        // The crankshaft stays put, a point a metre from it moves out to 1.2 m
        Assert.True(Vector3.Distance(Vector3.Transform(crank, normal), Vector3.Transform(crank, bigger)) < 1e-5f);
        var reach = Vector3.Transform(crank + Vector3.UnitX, bigger) - Vector3.Transform(crank, bigger);
        Assert.Equal(1.2f, reach.Length(), 4);
    }

    [Fact]
    public void A_part_on_a_scaled_one_keeps_its_own_size_and_sits_on_the_moved_slot()
    {
        var block = Block(1.2f);
        var blockWorld = CarPartsLayout.EnginePlacement(Car, block);
        var carbWorld = PartAssembler.ChildWorld(block, 14, Carb(1f), 1, blockWorld)!.Value;

        // The carb's base lands on the block's intake pad, where the bigger block has it
        var pad = Vector3.Transform(new Vector3(0f, 0.3f, 0.1f), blockWorld);
        Assert.True(Vector3.Distance(pad, Vector3.Transform(new Vector3(0f, -0.05f, 0f), carbWorld)) < 1e-5f);

        // ...and the carb is as big as it was made
        var span = Vector3.Transform(Vector3.UnitX, carbWorld) - Vector3.Transform(Vector3.Zero, carbWorld);
        Assert.Equal(1f, span.Length(), 4);
    }

    [Fact]
    public void A_scaled_part_on_a_plain_one_grows_around_its_own_slot()
    {
        var block = Block(1f);
        var blockWorld = CarPartsLayout.EnginePlacement(Car, block);
        var carbWorld = PartAssembler.ChildWorld(block, 14, Carb(1.5f), 1, blockWorld)!.Value;

        var pad = Vector3.Transform(new Vector3(0f, 0.3f, 0.1f), blockWorld);
        Assert.True(Vector3.Distance(pad, Vector3.Transform(new Vector3(0f, -0.05f, 0f), carbWorld)) < 1e-5f);
        var span = Vector3.Transform(Vector3.UnitX, carbWorld) - Vector3.Transform(Vector3.Zero, carbWorld);
        Assert.Equal(1.5f, span.Length(), 4);
    }

    [Fact]
    public void The_longest_prefix_that_fits_gives_the_scale()
    {
        var disc = new PartDefinition { Id = "brakes/stock/disc_brake_00D7" };
        var block = new PartDefinition { Id = "engines/gm/GM_327_block" };
        var modded = new PartDefinition { Id = "mods/turbo/snail", ModelScale = 1.7f };

        PartsCatalog.ApplyModelScales(new[] { disc, block, modded }, new Dictionary<string, float>
        {
            ["brakes/"] = 1.2f,
            ["brakes/stock/disc_brake"] = 1.35f,
            ["Engines/"] = 1.1f,
        });

        Assert.Equal(1.35f, disc.ModelScale);
        Assert.Equal(1.1f, block.ModelScale);
        Assert.Equal(1f, modded.ModelScale);
    }
}
