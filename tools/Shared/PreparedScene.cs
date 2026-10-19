using AcTools.Kn5File;

namespace StreetCorsa.Tools;

/// <summary>
/// An outdoor scene the game drives into (the diner's street, the gas station) as a KN5, out of what prepare_prop.py
/// made of its Blender .blend: the OBJ (Y up, metres, in the scene's own place) and its pictures, drawn with AC's
/// ksPerPixelMultiMap, the road markings cut out by their alpha, the windows' glass left out so the rooms behind show.
/// A sky dome goes round it for each light of the day (&lt;prefix&gt;DAY, _DUSK, _NIGHT in capitals), and the ground's
/// height (ground.bin, read by the game's StationGround) next to it, so the car rides the kerbs and the lot.
/// Linked into the builders (DinerSceneBuilder, StationSceneBuilder), which keep their own command lines.
/// </summary>
internal static class PreparedScene
{
    /// <param name="prepared">prepare_prop.py's folder: the OBJ, materials.json and the pictures</param>
    /// <param name="name">The OBJ's name in it</param>
    /// <param name="output">The KN5 to write; ground.bin goes next to it</param>
    /// <param name="skyPrefix">What the sky domes' materials and meshes are named after ("street_sky_")</param>
    /// <param name="sky">The panoramas by light, "day=a.jpg,dusk=b.jpg,night=c.jpg"</param>
    /// <param name="castShadows">The scene's meshes stand in the sun's way (a canopy over the pumps), but the ground never does</param>
    /// <param name="groundMaterials">What the ground's materials are named after, left shadowless when the rest cast</param>
    public static void Build(string prepared, string name, string output, string skyPrefix, string sky, float skyRadius, float groundHalf,
        bool castShadows = false, string groundMaterials = "")
    {
        var groups = ObjModel.ReadObj(Path.Combine(prepared, name + ".obj"), scale: 1f, turn: 0f, mirror: false, recentre: false);
        Console.WriteLine($"{name}: {groups.Values.Sum(g => g.Vertices.Count)} vertices, {groups.Values.Sum(g => g.Indices.Count) / 3} triangles, {groups.Count} material group(s)");

        var scene = Kn5.CreateEmpty();
        scene.RootNode.Name = Path.GetFileNameWithoutExtension(output);
        // Lit more by the sun than a prop is, so the cars' shadows show on the street
        var materialOf = ObjModel.AddMaterials(scene, prepared, Path.Combine(prepared, "materials.json"), ksAmbient: 0.2f, ksDiffuse: 0.75f);

        if (!castShadows)
        {
            // The scene is lit as a whole: nothing of it stands in the light's way
            ObjModel.AddMeshes(scene, groups, materialOf, fallback: null, castShadows: false);
        }
        else
        {
            // The ground is lit as a whole; what stands on it casts its shadow on the ground and the car
            var ground = groups.Where(g => materialOf.TryGetValue(g.Key, out var m) && IsGround(scene, m, groundMaterials)).ToDictionary(g => g.Key, g => g.Value);
            var standing = groups.Where(g => !ground.ContainsKey(g.Key)).ToDictionary(g => g.Key, g => g.Value);
            ObjModel.AddMeshes(scene, ground, materialOf, fallback: null, castShadows: false);
            ObjModel.AddMeshes(scene, standing, materialOf, fallback: null, castShadows: true);
        }

        // The ground under the scene's own faces, before the sky goes round it; written after the model, so a build
        // that fails leaves the old ground under the old scene
        var grid = Ground.Build(scene.RootNode.Children.ToList(), groundHalf);

        foreach (var entry in sky.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=', 2);
            Kn5SceneWriter.AddSky(scene, skyPrefix, parts.Length == 2 ? parts[0] : "day", parts[^1], skyRadius);
        }

        Kn5SceneWriter.Save(scene, output);
        Console.WriteLine($"{output}: {new FileInfo(output).Length / 1024 / 1024} MB, {scene.Textures.Count} textures, {scene.Materials.Count} materials, {scene.RootNode.Children.Count} meshes");

        var groundFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "ground.bin");
        grid.Write(groundFile);
        Console.WriteLine($"The ground, ±{groundHalf} m: {groundFile}");
    }

    private static bool IsGround(IKn5 kn5, uint material, string prefixes) =>
        prefixes.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(p => kn5.Materials.Keys.ElementAt((int)material).StartsWith(p, StringComparison.Ordinal));
}
