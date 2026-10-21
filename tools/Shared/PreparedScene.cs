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
    /// <summary>
    /// An object of the Blender scene named so (grass, a tree) goes into the KN5 under a node of its own name, pivoted
    /// at the bottom middle of its box, and the game leans it in the wind (GarageRenderer.Wind's SwayPrefix is this).
    /// It casts no shadow: the scene's shadows are drawn again only as the car moves, so a leaning bush's would stand
    /// still (compose_station.py lays a shade on the land under each)
    /// </summary>
    public const string SwayPrefix = "SWAY_";

    /// <summary>
    /// An object named so (LOOK_HIWAY_truck) goes into the KN5 under a node of its own name too, and the game shows it
    /// only for the scene that names its group (SceneLooks: a diner's roof sign and props on a street several diners
    /// share). It casts its shadow as the rest does, and is no part of the ground.
    /// </summary>
    public const string LookPrefix = "LOOK_";

    private static bool IsApart(string name) =>
        name.StartsWith(SwayPrefix, StringComparison.Ordinal) || name.StartsWith(LookPrefix, StringComparison.Ordinal);

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
        // What the wind moves (grass, a tree's crown) is kept apart under a node of its own, which the game leans
        var groups = ObjModel.ReadObj(Path.Combine(prepared, name + ".obj"), scale: 1f, turn: 0f, mirror: false, recentre: false,
            apart: IsApart);
        Console.WriteLine($"{name}: {groups.Values.Sum(g => g.Vertices.Count)} vertices, {groups.Values.Sum(g => g.Indices.Count) / 3} triangles, {groups.Count} group(s), "
            + $"{groups.Keys.Count(k => ObjModel.ObjectOf(k)?.StartsWith(SwayPrefix, StringComparison.Ordinal) == true)} of them swaying in the wind, "
            + $"{groups.Keys.Count(k => ObjModel.ObjectOf(k)?.StartsWith(LookPrefix, StringComparison.Ordinal) == true)} in a look");

        var scene = Kn5.CreateEmpty();
        scene.RootNode.Name = Path.GetFileNameWithoutExtension(output);
        // Lit more by the sun than a prop is, so the cars' shadows show on the street
        var materialOf = ObjModel.AddMaterials(scene, prepared, Path.Combine(prepared, "materials.json"), ksAmbient: 0.2f, ksDiffuse: 0.75f);

        // The ground is lit as a whole; what stands on it casts its shadow on the ground and the car (when the scene
        // casts at all). A cut-out (a tree on a card, a bush's leaves) casts none: the renderer's shadow pass draws
        // every triangle whole, so a card's shadow would be its square; nor does anything the wind leans, whose shadow
        // would stand still (compose_station.py lays a shade on the land under each tree and bush instead)
        ObjModel.AddMeshes(scene, groups, materialOf, fallback: null, castShadows: key =>
            castShadows && (ObjModel.ObjectOf(key) is not { } own || own.StartsWith(LookPrefix, StringComparison.Ordinal))
            && materialOf.TryGetValue(ObjModel.MaterialOf(key), out var m)
            && !IsGround(scene, m, groundMaterials) && !IsCutOut(scene, m), tile: Tile);

        // The ground under the scene's own faces (what sways in the wind stands on it, and is not it), before the sky
        // goes round it; written after the model, so a build that fails leaves the old ground under the old scene
        var grid = Ground.Build(MeshesUnder(scene.RootNode).ToList(), groundHalf);

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

    /// <summary>
    /// The squares a scene's triangles are put in, metres a side (ObjModel.AddMeshes' tile):
    /// the renderer draws only those on the screen, and only those near the car into its shadows
    /// </summary>
    public const float Tile = 40f;

    /// <summary>The mesh nodes under a node, the tiles' (the ground is taken from those standing still: not what sways, not a look's)</summary>
    private static IEnumerable<Kn5Node> MeshesUnder(Kn5Node node) =>
        node.Children.SelectMany(c => c.NodeClass == Kn5NodeClass.Mesh ? [c] : IsApart(c.Name) ? [] : MeshesUnder(c));

    private static bool IsCutOut(IKn5 kn5, uint material) =>
        kn5.Materials.Values.ElementAt((int)material) is { } m && (m.AlphaTested || m.BlendMode == Kn5MaterialBlendMode.AlphaBlend);

    private static bool IsGround(IKn5 kn5, uint material, string prefixes) =>
        prefixes.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(p => kn5.Materials.Keys.ElementAt((int)material).StartsWith(p, StringComparison.Ordinal));
}
