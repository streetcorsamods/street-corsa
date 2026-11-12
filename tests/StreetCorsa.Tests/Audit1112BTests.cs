using System.Numerics;
using StreetCorsa.Controls.Street;
using StreetCorsa.Parts;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-11-12 audit's fixes in the scenes round the garage: the boxes drawn from boxes face out only, a scene's file
/// cannot hang lamps without end, and a room lit for another hour keeps the lamps it hangs again.
/// </summary>
public class Audit1112BTests
{
    private static readonly StreetCorsa.Logging.IAppLogger Logger = StreetCorsa.Logging.AppLoggerFactory.CreateLogger("Test");

    // ---- The boxes (the lift, the stand, a part without a model) ----

    [Fact]
    public void A_box_mesh_has_two_triangles_a_side_each_facing_out()
    {
        var centre = new Vector3(1f, 2f, 3f);
        var node = PlaceholderMesh.Boxes("box", [(centre, new Vector3(0.5f, 0.2f, 1f))], 0);

        Assert.Equal(6 * 2 * 3, node.Indices.Length);
        for (var i = 0; i < node.Indices.Length; i += 3)
        {
            var (a, b, c) = (node.Vertices[node.Indices[i]], node.Vertices[node.Indices[i + 1]], node.Vertices[node.Indices[i + 2]]);
            Vector3 At(AcTools.Kn5File.Kn5Node.Vertex v) => new(v.Position.X, v.Position.Y, v.Position.Z);

            // Its front the side its normal points to (the scene builders' way, SceneMesh), and that side away from the middle
            var front = Vector3.Cross(At(b) - At(a), At(c) - At(a));
            var normal = new Vector3(a.Normal.X, a.Normal.Y, a.Normal.Z);
            Assert.True(Vector3.Dot(front, normal) > 0f);
            Assert.True(Vector3.Dot(At(a) - centre, normal) > 0f);
        }
    }

    // ---- A scene file's lamps ----

    private static IReadOnlyList<StreetLamp> NightLamps(params SceneLighting.LampEntry[] lamps) =>
        SceneLighting.Read(new() { ["night"] = new SceneLighting.LightEntry(1f, null, 1f, null, null, null, [.. lamps], null) },
            "test", Logger, lampShadows: true)[StreetLight.Night].Lamps;

    [Fact]
    public void Past_the_most_lamps_casting_shadows_the_dimmest_light_without_one()
    {
        // Brightness 1, 2, 3, 4, 5, 1, 2, ...: of the three dimmest, the first keeps its shadow
        var count = SceneLighting.MostShadowLamps + 2;
        var lamps = NightLamps([.. Enumerable.Range(0, count).Select(i => new SceneLighting.LampEntry(i, 3, 0, null, 1f + i % 5, 8))]);

        Assert.Equal(count, lamps.Count);
        Assert.Equal(SceneLighting.MostShadowLamps, lamps.Count(l => l.Shadows));
        Assert.True(lamps.Where(l => l.Shadows).Min(l => l.Brightness) >= lamps.Where(l => !l.Shadows).Max(l => l.Brightness));

        // Of lamps as bright, the first in the file keep theirs; the order of the lamps is the file's
        Assert.Equal(Enumerable.Range(0, count).Select(i => (float)i), lamps.Select(l => l.X));
        var dark = lamps.Where(l => !l.Shadows).Select(l => l.X).ToList();
        Assert.Equal([5f, 10f], dark);
    }

    [Fact]
    public void A_few_lamps_casting_shadows_all_keep_them()
    {
        var lamps = NightLamps(new SceneLighting.LampEntry(0, 3, 0, null, 1, 8), new SceneLighting.LampEntry(2, 3, 0, null, 1, 8, Shadows: false));
        Assert.Equal([true, false], lamps.Select(l => l.Shadows));
    }

    [Fact]
    public void A_lamp_out_of_the_scene_or_reaching_nowhere_is_skipped_and_one_reaching_too_far_reaches_the_most()
    {
        var lamps = NightLamps(
            new SceneLighting.LampEntry(float.NaN, 3, 0, null, 1, 8),
            new SceneLighting.LampEntry(0, 1e6f, 0, null, 1, 8),
            new SceneLighting.LampEntry(0, 3, 0, null, 1, 0),
            new SceneLighting.LampEntry(0, 3, 0, null, 1, float.PositiveInfinity),
            new SceneLighting.LampEntry(1, 3, 0, null, 1, 8),
            new SceneLighting.LampEntry(2, 3, 0, null, 1, 5000));

        Assert.Equal([1f, 2f], lamps.Select(l => l.X));
        Assert.Equal([8f, SceneLighting.MostLampRange], lamps.Select(l => l.Range));
    }

    // ---- A room lit for another hour ----

    [Fact]
    public void Lamps_hang_alike_when_each_is_where_the_other_is_as_far_and_casting_as_it_does()
    {
        var lamps = new[] { new StreetLamp(0, 3, 0, [255, 255, 255], 1f, 8f, true), new StreetLamp(4, 3, 0, [255, 255, 255], 2f, 10f) };

        // Brighter or dimmer, another colour: the same lamps
        Assert.True(SceneLights.HangAlike(lamps, [lamps[0] with { Brightness = 0.2f, Color = [200, 180, 150] }, lamps[1] with { Brightness = 5f }]));
        Assert.True(SceneLights.HangAlike([], []));

        Assert.False(SceneLights.HangAlike(lamps, [lamps[0]]));
        Assert.False(SceneLights.HangAlike(lamps, [lamps[0] with { X = 1f }, lamps[1]]));
        Assert.False(SceneLights.HangAlike(lamps, [lamps[0] with { Range = 9f }, lamps[1]]));
        Assert.False(SceneLights.HangAlike(lamps, [lamps[0] with { Shadows = false }, lamps[1]]));
        Assert.False(SceneLights.HangAlike(lamps, [lamps[0], lamps[1] with { Shadows = true }]));
    }
}
