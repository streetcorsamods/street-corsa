using System.Globalization;
using AcTools.Kn5File;
using AcTools.Numerics;
using Newtonsoft.Json.Linq;
using StreetCorsa.Tools;
using static StreetCorsa.Tools.Kn5SceneWriter;

namespace StreetSceneBuilder;

/// <summary>
/// Builds the street scene the Cruise screen is drawn in: one KN5 a light of the day (street_day.kn5, street_dusk.kn5,
/// street_night.kn5) out of the pictures prepare_street.py made from a panorama.
///
/// The panorama is projected from the point it was taken at, the capture height above the middle of the scene, onto a
/// ground and a dome: a floor disc near the middle, which is lit and takes the cars' shadows; a ground ring out to the
/// dome; and a hemisphere standing on the ring. Seen from the capture point the three meet without a seam, and the
/// street looks as photographed; from the driver's seat, a little lower, near enough.
///
///   StreetSceneBuilder &lt;prepared folder&gt; &lt;out folder&gt; [--dome-radius 35] [--flip]
/// </summary>
internal static class Program
{
    private static readonly string[] Lights = ["day", "dusk", "night"];

    // Around the scene; enough that the curved street stays smooth
    private const int Segments = 256;
    private const int FloorRings = 48;
    private const int GroundRings = 24;
    private const int DomeRings = 64;

    private const string Usage = "StreetSceneBuilder <prepared folder> <out folder> [--dome-radius 35] [--flip]";

    private static int Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--")).ToList();
        if (positional.Count < 2)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var prepared = positional[0];
        var output = positional[1];
        if (!TryOption(args, "--dome-radius", out var domeRadiusOption))
        {
            Console.Error.WriteLine("--dome-radius takes a number of metres, e.g. --dome-radius 35");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var domeRadius = domeRadiusOption ?? 35f;
        var flip = args.Contains("--flip");

        var light = JObject.Parse(File.ReadAllText(Path.Combine(prepared, "light.json")));
        var height = light.Value<float>("captureHeight");
        var floorHalf = light.Value<float>("floorHalfSize");
        if (floorHalf >= domeRadius)
        {
            Console.Error.WriteLine($"The floor ({floorHalf} m) must end inside the dome ({domeRadius} m)");
            return 1;
        }

        Directory.CreateDirectory(output);
        var floor = FloorDisc(floorHalf);
        var ground = GroundRing(floorHalf, domeRadius, height);
        var dome = Dome(domeRadius, height);

        foreach (var name in Lights)
        {
            var kn5 = Kn5.CreateEmpty();
            kn5.RootNode.Name = "street_" + name;

            var floorTexture = AddTexture(kn5, Path.Combine(prepared, $"floor_{name}.jpg"));
            var skyTexture = AddTexture(kn5, Path.Combine(prepared, $"sky_{name}.jpg"));

            // The floor is the photo lit by the scene: mostly its own light, so it looks as shot, with enough of the
            // key light that a car's shadow darkens it. The dome is the photo and nothing else.
            var floorMaterial = AddMaterial(kn5, "street_floor", floorTexture, ambient: 0.25f, diffuse: 0.35f, emissive: 0.55f);
            var skyMaterial = AddMaterial(kn5, "street_sky", skyTexture, ambient: 0f, diffuse: 0f, emissive: 1f);

            // The street throws no shadows: the sun's are in the photo already
            AddMesh(kn5, "FLOOR", floor.Vertices, floor.Indices, floorMaterial, flip);
            AddMesh(kn5, "GROUND", ground.Vertices, ground.Indices, skyMaterial, flip);
            AddMesh(kn5, "DOME", dome.Vertices, dome.Indices, skyMaterial, flip);

            var path = Path.Combine(output, $"street_{name}.kn5");
            Kn5SceneWriter.Save(kn5, path);
            Console.WriteLine($"{path}: {new FileInfo(path).Length / 1024} KB");
        }

        return 0;
    }

    /// <summary>A number option: null when not given, false when given without a number</summary>
    private static bool TryOption(string[] args, string name, out float? value)
    {
        value = null;
        var i = Array.IndexOf(args, name);
        if (i < 0) return true;
        if (i + 1 >= args.Length || !float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;

        value = number;
        return true;
    }

    private sealed record Mesh(List<Kn5Node.Vertex> Vertices, List<int> Indices);

    /// <summary>Where the panorama sees a direction: the same mapping as prepare_street.py</summary>
    private static float PanoramaV(float dx, float dy, float dz) =>
        0.5f - MathF.Asin(Math.Clamp(dy / MathF.Sqrt(dx * dx + dy * dy + dz * dz), -1f, 1f)) / MathF.PI;

    private static float Azimuth(int column) => MathF.PI - 2f * MathF.PI * column / Segments;

    /// <summary>
    /// A grid of rings round the middle, <see cref="Segments"/> + 1 columns so the panorama's seam gets vertices of
    /// its own on both sides. A column's azimuth is its place in the panorama: u = column / segments.
    /// </summary>
    private static Mesh Rings(int rings, Func<int, int, (Vec3 Position, Vec3 Normal, Vec2 Uv)> vertex)
    {
        var mesh = new Mesh([], []);
        for (var ring = 0; ring <= rings; ring++)
        {
            for (var column = 0; column <= Segments; column++)
            {
                var (p, n, uv) = vertex(ring, column);
                mesh.Vertices.Add(new Kn5Node.Vertex(p, n, uv, new Vec3(1f, 0f, 0f)));
            }
        }

        var stride = Segments + 1;
        for (var ring = 0; ring < rings; ring++)
        {
            for (var column = 0; column < Segments; column++)
            {
                var a = ring * stride + column;
                var b = a + 1;
                var c = a + stride;
                var d = c + 1;
                mesh.Indices.AddRange([a, b, c, b, d, c]);
            }
        }

        return mesh;
    }

    /// <summary>The floor near the middle, drawn with the picture of the ground seen from above</summary>
    private static Mesh FloorDisc(float half) => Rings(FloorRings, (ring, column) =>
    {
        // Rings closer together near the middle, where the cars are
        var t = (float)ring / FloorRings;
        var r = half * t * t;
        var a = Azimuth(column);
        var x = r * MathF.Sin(a);
        var z = r * MathF.Cos(a);
        var uv = new Vec2((x + half) / (2f * half), (half - z) / (2f * half));
        return (new Vec3(x, 0f, z), new Vec3(0f, 1f, 0f), uv);
    });

    /// <summary>The ground from the floor out to the dome, drawn with the panorama itself</summary>
    private static Mesh GroundRing(float from, float to, float height) => Rings(GroundRings, (ring, column) =>
    {
        var r = from + (to - from) * ring / GroundRings;
        var a = Azimuth(column);
        var uv = new Vec2((float)column / Segments, PanoramaV(r, -height, 0f));
        return (new Vec3(r * MathF.Sin(a), 0f, r * MathF.Cos(a)), new Vec3(0f, 1f, 0f), uv);
    });

    /// <summary>The hemisphere standing on the ground ring, facing in</summary>
    private static Mesh Dome(float radius, float height) => Rings(DomeRings, (ring, column) =>
    {
        var elevation = MathF.PI / 2f * ring / DomeRings;
        var a = Azimuth(column);
        var horizontal = radius * MathF.Cos(elevation);
        var y = radius * MathF.Sin(elevation);
        var p = new Vec3(horizontal * MathF.Sin(a), y, horizontal * MathF.Cos(a));
        var inward = new Vec3(-MathF.Cos(elevation) * MathF.Sin(a), -MathF.Sin(elevation), -MathF.Cos(elevation) * MathF.Cos(a));
        var uv = new Vec2((float)column / Segments, PanoramaV(horizontal, y - height, 0f));
        return (p, inward, uv);
    });
}
