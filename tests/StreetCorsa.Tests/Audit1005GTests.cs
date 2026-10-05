using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Tools;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-05 audit's fixes in the tools: a prop's meshes cast their shadow and face the right way, the builders'
/// command lines, and the scene builders' shared shapes. The race mode's fixes are pinned by its harness
/// (tools/sc_race_harness/test_parts.py).
/// </summary>
public class Audit1005GTests
{
    // ---- A prop's meshes: castShadows was passed into flip ----

    private static IKn5 OneTriangleProp(bool castShadows)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.Materials["prop"] = new Kn5Material { Name = "prop", ShaderName = "ksPerPixelMultiMap" };
        var vertices = new List<Kn5Node.Vertex>
        {
            new(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 1f), new Vec2(0f, 0f), new Vec3(1f, 0f, 0f)),
            new(new Vec3(1f, 0f, 0f), new Vec3(0f, 0f, 1f), new Vec2(1f, 0f), new Vec3(1f, 0f, 0f)),
            new(new Vec3(0f, 1f, 0f), new Vec3(0f, 0f, 1f), new Vec2(0f, 1f), new Vec3(1f, 0f, 0f))
        };
        var groups = new Dictionary<string, (List<Kn5Node.Vertex> Vertices, List<int> Indices)> { ["prop"] = (vertices, [0, 1, 2]) };
        ObjModel.AddMeshes(kn5, groups, new Dictionary<string, uint> { ["prop"] = 0 }, fallback: null, castShadows);
        return kn5;
    }

    [Fact]
    public void A_prop_asked_to_cast_a_shadow_casts_one_and_keeps_its_faces_the_right_way_round()
    {
        var mesh = OneTriangleProp(castShadows: true).RootNode.Children.Single();

        Assert.True(mesh.CastShadows);
        Assert.Equal(new ushort[] { 0, 1, 2 }, mesh.Indices);
    }

    [Fact]
    public void A_scene_mesh_casts_no_shadow_and_keeps_its_faces_the_right_way_round()
    {
        var mesh = OneTriangleProp(castShadows: false).RootNode.Children.Single();

        Assert.False(mesh.CastShadows);
        Assert.Equal(new ushort[] { 0, 1, 2 }, mesh.Indices);
    }

    // ---- The builders' command lines ----

    [Fact]
    public void A_number_after_an_option_is_never_taken_for_a_folder()
    {
        var options = new BuilderArgs(["--scale", "0.112", "prepared", "radio", "--mirror", "out", "--turn", "90"], "--mirror");

        Assert.Equal(["prepared", "radio", "out"], options.Positional);
        Assert.Equal(0.112f, options.Number("--scale"));
        Assert.Equal(90f, options.Number("--turn"));
        Assert.True(options.Flag("--mirror"));
        Assert.True(options.NumbersOk("usage", "--scale", "--turn"));
    }

    [Theory]
    [InlineData("7OO")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void A_mistyped_number_is_refused_not_built_as_the_default(string text)
    {
        var options = new BuilderArgs(["prepared", "street", "out.kn5", "--sky-radius", text]);

        Assert.False(options.NumbersOk("usage", "--sky-radius", "--ground-half"));
        Assert.Null(options.Number("--sky-radius"));
    }

    [Fact]
    public void A_number_option_left_out_is_no_problem()
    {
        var options = new BuilderArgs(["prepared", "street", "out.kn5", "--sky", "day=a.jpg"]);

        Assert.True(options.NumbersOk("usage", "--sky-radius", "--ground-half"));
        Assert.Null(options.Number("--ground-half"));
        Assert.Equal("day=a.jpg", options.Option("--sky"));
        Assert.Equal(["prepared", "street", "out.kn5"], options.Positional);
    }

    // ---- The scene builders' shared shapes ----

    [Fact]
    public void A_scene_mesh_faces_where_its_normal_points_and_the_other_way_when_flipped()
    {
        static Vec3 FrontOf(SceneMesh mesh)
        {
            var (a, b, c) = (mesh.Vertices[mesh.Indices[0]].Position, mesh.Vertices[mesh.Indices[1]].Position, mesh.Vertices[mesh.Indices[2]].Position);
            return Vec3.Cross(b - a, c - a);
        }

        var plain = new SceneMesh();
        var flipped = new SceneMesh(flip: true);
        foreach (var mesh in new[] { plain, flipped })
            mesh.Quad(new Vec3(0f, 0f, 0f), new Vec3(1f, 0f, 0f), new Vec3(1f, 0f, 1f), new Vec3(0f, 0f, 1f), Vec3.UnitY, _ => new Vec2(0f, 0f));

        Assert.True(Vec3.Dot(FrontOf(plain), Vec3.UnitY) > 0f);
        Assert.True(Vec3.Dot(FrontOf(flipped), Vec3.UnitY) < 0f);
    }

    [Fact]
    public void A_box_leaves_its_bottom_out_unless_asked()
    {
        var standing = new SceneMesh();
        standing.Box(new Vec3(0f, 0f, 0f), new Vec3(1f, 1f, 1f), tile: 1f);
        var whole = new SceneMesh();
        whole.Box(new Vec3(0f, 0f, 0f), new Vec3(1f, 1f, 1f), tile: 1f, bottom: true);

        Assert.Equal(5 * 6, standing.Indices.Count);
        Assert.Equal(6 * 6, whole.Indices.Count);
    }

    [Fact]
    public void An_appended_mesh_keeps_its_triangles_on_its_own_vertices()
    {
        var first = new SceneMesh();
        first.Box(new Vec3(0f, 0f, 0f), new Vec3(1f, 1f, 1f), tile: 1f);
        var second = new SceneMesh();
        second.Bar(new Vec3(0f, 0f, 0f), new Vec3(0f, 2f, 0f), size: 0.1f, tile: 1f);
        var before = first.Vertices.Count;

        first.Append(second);

        Assert.Equal(before + second.Vertices.Count, first.Vertices.Count);
        Assert.All(first.Indices.Skip(first.Indices.Count - second.Indices.Count), i => Assert.InRange(i, before, first.Vertices.Count - 1));
    }
}
