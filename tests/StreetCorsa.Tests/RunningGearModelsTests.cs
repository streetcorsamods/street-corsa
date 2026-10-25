using System.Numerics;
using StreetCorsa.Mods;
using StreetCorsa.Parts;

namespace StreetCorsa.Tests;

/// <summary>Step 31 phase 4: the running gear's models, given by models.json, and placed right</summary>
public sealed class RunningGearModelsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private static IEnumerable<PartDefinition> RunningGearParts =>
        Catalog.Parts.Values.Where(p => p.LocalId.StartsWith("running_gear/", StringComparison.Ordinal));

    [Fact]
    public void Plain_coil_springs_are_drawn_with_the_model_models_json_gives_them()
    {
        var springs = Catalog.Parts.Values.Where(p => p.Id.StartsWith("suspension/test_stock/coil_", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(springs);
        Assert.All(springs, s => Assert.EndsWith(Path.Combine("running_gear", "test_coils", "coil.kn5"), Catalog.GetModelPath(s)));
        Assert.DoesNotContain(Catalog.Problems, p => p.Contains("models.json"));
    }

    [Fact]
    public void The_right_axle_shaft_points_out_to_its_wheel_as_the_left_does()
    {
        var shaft = RunningGearParts.First(p => PartKinds.GroupOf(p) == PartKinds.AxleShafts);
        foreach (var housing in RunningGearParts.Where(p => PartKinds.GroupOf(p) == PartKinds.RearAxles))
        {
            // The shaft's flange is at +X in its own space
            var flange = new Vector3(0.28f, 0, 0);
            var left = Vector3.Transform(flange, PartSlots.ChildWorld(housing, 3, shaft, 1, Matrix4x4.Identity)!.Value);
            var right = Vector3.Transform(flange, PartSlots.ChildWorld(housing, 4, shaft, 1, Matrix4x4.Identity)!.Value);
            Assert.InRange(left.X, 0.6f, 0.7f);
            Assert.InRange(right.X, -0.7f, -0.6f);
        }
    }

    [Fact]
    public void A_housing_reaches_into_the_drums_at_the_cars_track_with_its_middle_its_own_size()
    {
        var anchors = new CarAnchors(new Vector3(0.8f, 0.3f, -1.3f), new Vector3(-0.8f, 0.3f, -1.3f), new Vector3(0.82f, 0.3f, 1.3f), new Vector3(-0.82f, 0.3f, 1.3f));
        var stretch = CarPartsLayout.AxleStretch(anchors);
        Assert.Equal(0.8f, stretch.At(0.655f), 3);
        Assert.Equal(-0.8f, stretch.At(-0.655f), 3);
        Assert.Equal(0.2f, stretch.At(0.2f));

        // A narrow car's tubes are drawn shorter, never through the middle
        var narrow = CarPartsLayout.AxleStretch(anchors with { WheelLR = new Vector3(0.6f, 0.3f, 1.3f), WheelRR = new Vector3(-0.6f, 0.3f, 1.3f) });
        Assert.InRange(narrow.At(0.655f), 0.5f, 0.6f);
        Assert.Equal(0.25f, narrow.At(0.25f));
    }

    [Fact]
    public void A_mod_gives_other_parts_models_of_its_own_by_id_or_by_the_start_of_one_and_the_parts_stay_as_they_are()
    {
        _temp.Parts("base/parts/gear", """
            [ { "id": "gear/drum_9", "name": "d", "model": "drum.kn5", "specs": { "force": 1 } },
                                       { "id": "gear/drum_11", "name": "e", "model": "drum.kn5" }, { "id": "gear/leaf", "name": "l", "model": "leaf.kn5" } ]
            """);
        _temp.File("mod/parts/models.json", """
            { "models": { "gear/drum_*": "truck/drum.kn5", "gear/leaf": "truck/leaf.kn5", "gear/nothing": "truck/leaf.kn5", "gear/drum_9x": "truck/gone.kn5" } }
            """);
        _temp.Bytes("mod/parts/truck/drum.kn5", [1]);
        _temp.Bytes("mod/parts/truck/leaf.kn5", [1]);

        var catalog = PartsCatalog.Load([
            (new ModInfo { Id = ModIds.BaseId }, Path.Combine(_temp.Path, "base", "parts")),
            (new ModInfo { Id = "truckparts" }, Path.Combine(_temp.Path, "mod", "parts"))
        ]);

        var truck = Path.Combine(_temp.Path, "mod", "parts", "truck");
        Assert.Equal(Path.Combine(truck, "drum.kn5"), catalog.GetModelPath(catalog.Get("gear/drum_9")!));
        Assert.Equal(Path.Combine(truck, "drum.kn5"), catalog.GetModelPath(catalog.Get("gear/drum_11")!));
        Assert.Equal(Path.Combine(truck, "leaf.kn5"), catalog.GetModelPath(catalog.Get("gear/leaf")!));
        Assert.Equal(1, catalog.Get("gear/drum_9")!.Number("force"));
        Assert.Contains(catalog.Problems, p => p.Contains("gear/nothing names no part"));
        Assert.Contains(catalog.Problems, p => p.Contains("no model truck/gone.kn5"));
    }
}
