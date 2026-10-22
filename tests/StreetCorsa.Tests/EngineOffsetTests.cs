using System.Numerics;
using System.Text.Json.Nodes;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Catalog;

namespace StreetCorsa.Tests;

/// <summary>The engine moved by hand in the garage (F5 on the block): kept per car in cars.json, and everything placed by it</summary>
public class EngineOffsetTests
{
    private static readonly CarAnchors Car = new(
        new Vector3(0.75f, 0.3f, 1.4f), new Vector3(-0.75f, 0.3f, 1.4f),
        new Vector3(0.75f, 0.3f, -1.3f), new Vector3(-0.75f, 0.3f, -1.3f));

    private static PartDefinition Block() => new()
    {
        Id = "engines/test/block",
        Slots = { new PartSlot { Id = 8, Name = "crankshaft", Position = new[] { 0f, -0.1f, 0.2f } } },
    };

    [Fact]
    public void The_engine_moves_by_its_offset_in_the_cars_space()
    {
        var offset = new Vector3(0.01f, 0.12f, -0.05f);

        var placed = CarPartsLayout.EnginePlacement(Car, Block());
        var moved = CarPartsLayout.EnginePlacement(Car with { EngineOffset = offset }, Block());

        Assert.True(Vector3.Distance(placed.Translation + offset, moved.Translation) < 1e-5f);
    }

    [Fact]
    public void The_offset_is_written_into_the_cars_entry_beside_its_other_fields_and_zero_takes_it_out()
    {
        const string file = "{\n  \"_note\": \"kept\",\n  \"cars\": {\n    \"car_a\": { \"price\": 1000 }\n  }\n}\n";

        var text = BaseCarsFile.WithEngineOffset(file, "car_a", new Vector3(0, 0.1234f, -0.05f));
        var entry = ModMerge.Parse(text)!["cars"]!["car_a"]!.AsObject();

        Assert.Equal(1000, entry["price"]!.GetValue<int>());
        Assert.Equal("[0,0.123,-0.05]", entry["engine_offset"]!.ToJsonString());
        Assert.Contains("\"_note\"", text);

        var back = CarBook.Read((JsonObject)ModMerge.Parse(text)!, []).Car("car_a")!;
        Assert.Equal(new Vector3(0, 0.123f, -0.05f), back.EngineShift);

        var cleared = ModMerge.Parse(BaseCarsFile.WithEngineOffset(text, "car_a", Vector3.Zero))!["cars"]!["car_a"]!.AsObject();
        Assert.False(cleared.ContainsKey("engine_offset"));

        // Nudged back to the start leaves less than a millimetre, a minus zero among it: that is no offset either
        var residue = ModMerge.Parse(BaseCarsFile.WithEngineOffset(text, "car_a", new Vector3(-0f, 0.0004f, -1e-9f)))!["cars"]!["car_a"]!.AsObject();
        Assert.False(residue.ContainsKey("engine_offset"));
        Assert.Equal("[-0.01,0,0]", ModMerge.Parse(BaseCarsFile.WithEngineOffset(text, "car_a", new Vector3(-0.01f, -1e-9f, 0)))!["cars"]!["car_a"]!["engine_offset"]!.ToJsonString());
    }

    [Fact]
    public void An_offset_that_is_not_three_numbers_within_a_metre_is_none()
    {
        Assert.Equal(Vector3.Zero, new CarEntry { EngineOffset = [0, 1.5f, 0] }.EngineShift);
        Assert.Equal(Vector3.Zero, new CarEntry { EngineOffset = [0, 0.1f] }.EngineShift);
        Assert.Equal(Vector3.Zero, new CarEntry().EngineShift);
    }

    [Fact]
    public void The_book_takes_an_offset_for_a_car_it_gave_nothing_and_drops_it_at_zero()
    {
        var book = CarBook.Read(ModMerge.NewObject(), []);

        book.SetEngineOffset("car_b", new Vector3(0, 0.2f, 0));
        Assert.Equal(new Vector3(0, 0.2f, 0), book.Car("car_b")!.EngineShift);

        book.SetEngineOffset("car_b", Vector3.Zero);
        Assert.Null(book.Car("car_b")!.EngineOffset);
    }

    [Fact]
    public void The_parts_that_may_stand_out_follow_the_engine()
    {
        var catalog = PartsCatalog.Load(RepoPaths.PartsRoots);
        var parts = new List<PartInstance> { new("engines/test_v8/block") { ParentSlot = PartInstance.CarEngineSlot } };
        foreach (var id in new[] { "engines/test_v8/left_head", "engines/test_v8/right_head", "engines/test_v8/blower_intake_manifold",
                     "engines/generic/weiand_8_71_street_supercharger" })
        {
            var loose = new PartInstance(id);
            Workbench.Mount(parts, Workbench.FindPlaces(catalog, parts, loose)[0], loose);
        }
        var tree = PartTrees.ToInstalled(catalog, parts[0])!.Root;
        var up = new Vector3(0, 0.3f, 0);

        var at = ShownEngineParts.Of(catalog, tree, Car, EngineLayout.Front).Single(p => p.Model.Name.Contains("weiand_8_71_street_supercharger")).At;
        var raised = ShownEngineParts.Of(catalog, tree, Car with { EngineOffset = up }, EngineLayout.Front).Single(p => p.Model.Name.Contains("weiand_8_71_street_supercharger")).At;

        Assert.True(Vector3.Distance(at.Translation + up, raised.Translation) < 1e-5f);
    }
}
