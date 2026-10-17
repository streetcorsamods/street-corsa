using System.Numerics;
using StreetCorsa.Parts;

namespace StreetCorsa.Tests;

/// <summary>Step 31 phase 4: the running gear's models (tools/RunningGearParts and its stand-ins) are there and placed right</summary>
public class RunningGearModelsTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private static IEnumerable<PartDefinition> RunningGearParts =>
        Catalog.Parts.Values.Where(p => p.LocalId.StartsWith("running_gear/", StringComparison.Ordinal));

    [Fact]
    public void Every_running_gear_part_but_the_ones_left_for_later_has_its_model_beside_its_pack()
    {
        Assert.All(RunningGearParts, p =>
        {
            var path = Catalog.GetModelPath(p);
            Assert.True(path != null && File.Exists(path), $"{p.Id}: no model ({p.Model ?? "none named"})");
        });

        // Each model is credited or ours: a pack with a stand-in of a download says so beside it
        foreach (var pack in new[] { "brakes_drum", "brakes_disc", "springs_leaf", "axles_gm", "axles_ford", "axles_mopar", "axles_dana", "axle_speed" })
            Assert.True(File.Exists(Path.Combine(Catalog.Root, "running_gear", pack, "CREDITS.txt")), pack);
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
}
