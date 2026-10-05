using AcTools.Kn5File;
using StreetCorsa.Tools;

namespace DinerSceneBuilder;

/// <summary>
/// The diner's street as a KN5, out of what prepare_prop.py made of compose_street.py's .blend: the OBJ (Y up, metres,
/// in the scene's own place: the road's surface at 0, the middle of Main Street in front of the diner at the origin) and
/// its pictures, drawn with AC's ksPerPixelMultiMap, the road markings cut out by their alpha, the windows' glass left out
/// so the rooms behind show. A sky dome goes round it for each light of the day (STREET_SKY_DAY, _DUSK, _NIGHT), and the
/// ground's height (ground.bin, read by the game's StationGround) next to it, so the car rides the kerbs and the lot.
///
///   DinerSceneBuilder &lt;prepared folder&gt; &lt;name&gt; &lt;out.kn5&gt; --sky day=a.jpg,dusk=b.jpg,night=c.jpg
///       [--sky-radius 700] [--ground-half 180]
///
/// The base game's Main Street (see docs/screens/diner.md; build_street.py runs it): DinerSceneBuilder &lt;prepared&gt; street
/// "Street Corsa/Content/streetcorsa/streets/mainstreet/street.kn5" --sky day=tools/StationScene/skies/sky_day.jpg,...
/// </summary>
internal static class Program
{
    private const string Usage = "DinerSceneBuilder <prepared folder> <name> <out.kn5> --sky day=a.jpg,dusk=b.jpg,night=c.jpg [--sky-radius 700] [--ground-half 180]";

    private static int Main(string[] args)
    {
        var options = new BuilderArgs(args);
        var positional = options.Positional;
        var sky = options.Option("--sky");
        if (positional.Count != 3 || sky == null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        if (!options.NumbersOk(Usage, "--sky-radius", "--ground-half")) return 1;

        var (prepared, name, output) = (positional[0], positional[1], positional[2]);
        var skyRadius = options.Number("--sky-radius") ?? 700f;
        var groundHalf = options.Number("--ground-half") ?? 180f;

        var groups = ObjModel.ReadObj(Path.Combine(prepared, name + ".obj"), scale: 1f, turn: 0f, mirror: false, recentre: false);
        Console.WriteLine($"{name}: {groups.Values.Sum(g => g.Vertices.Count)} vertices, {groups.Values.Sum(g => g.Indices.Count) / 3} triangles, {groups.Count} material group(s)");

        var scene = Kn5.CreateEmpty();
        scene.RootNode.Name = Path.GetFileNameWithoutExtension(output);
        // Lit more by the sun than a prop is, so the cars' shadows show on the street
        var materialOf = ObjModel.AddMaterials(scene, prepared, Path.Combine(prepared, "materials.json"), ksAmbient: 0.2f, ksDiffuse: 0.75f);

        // The scene is lit as a whole, like the gas station's: nothing of it stands in the light's way
        ObjModel.AddMeshes(scene, groups, materialOf, fallback: null, castShadows: false);

        // The ground under the street's own faces, before the sky goes round it; written after the model, so a build
        // that fails leaves the old ground under the old street
        var ground = Ground.Build(scene.RootNode.Children.ToList(), groundHalf);

        foreach (var entry in sky.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=', 2);
            Kn5SceneWriter.AddSky(scene, "street_sky_", parts.Length == 2 ? parts[0] : "day", parts[^1], skyRadius);
        }

        Kn5SceneWriter.Save(scene, output);
        Console.WriteLine($"{output}: {new FileInfo(output).Length / 1024 / 1024} MB, {scene.Textures.Count} textures, {scene.Materials.Count} materials, {scene.RootNode.Children.Count} meshes");

        var groundFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "ground.bin");
        ground.Write(groundFile);
        Console.WriteLine($"The ground, ±{groundHalf} m: {groundFile}");
        return 0;
    }
}
