using System.Text.Json.Nodes;
using AcTools.Kn5File;

namespace StreetCorsa.Tools;

/// <summary>
/// A street's kit (step 39, the game's streetkit content type) out of what prepare_prop.py made of a .blend holding
/// each piece once at the origin, as an object named PIECE_&lt;id&gt;: a KN5 a piece, &lt;out&gt;\&lt;id&gt;\&lt;id&gt;.kn5,
/// with only the materials and pictures that piece is made of, lit as the scene it stands in is
/// (<see cref="PreparedScene"/>). The pieces cast their shadows, but not their cut-outs (a card's would be its square)
/// nor their ground (a house's lawn and drive). Triangles of no piece are told and left out.
/// </summary>
internal static class PreparedKit
{
    /// <summary>What the .blend's objects that are pieces are named after</summary>
    public const string PiecePrefix = "PIECE_";

    /// <param name="prepared">prepare_prop.py's folder: the OBJ, materials.json and the pictures</param>
    /// <param name="name">The OBJ's name in it</param>
    /// <param name="output">The kit's folder: a folder a piece goes in it, its KN5 in that</param>
    /// <param name="groundMaterials">What the ground's materials are named after (a house's lawn), left shadowless</param>
    /// <returns>The pieces written, by id</returns>
    public static List<string> Build(string prepared, string name, string output, string groundMaterials)
    {
        var groups = ObjModel.ReadObj(Path.Combine(prepared, name + ".obj"), scale: 1f, turn: 0f, mirror: false, recentre: false,
            apart: o => o.StartsWith(PiecePrefix, StringComparison.Ordinal));
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(prepared, "materials.json")))!.AsObject();
        var materials = manifest["materials"]!.AsArray();
        var aliases = manifest["aliases"]!.AsObject();

        var loose = groups.Where(g => ObjModel.ObjectOf(g.Key) == null).Sum(g => g.Value.Indices.Count) / 3;
        if (loose > 0) Console.WriteLine($"{loose} triangles of no piece, left out");

        var written = new List<string>();
        var scratch = Path.Combine(prepared, "_piece_materials.json");
        foreach (var byPiece in groups.Where(g => ObjModel.ObjectOf(g.Key) != null).GroupBy(g => ObjModel.ObjectOf(g.Key)!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var id = byPiece.Key[PiecePrefix.Length..];
            var own = byPiece.ToDictionary(g => ObjModel.MaterialOf(g.Key), g => g.Value);

            // The manifest of this piece's materials alone, so its file holds no picture of another's
            var used = own.Keys.Where(k => aliases[k] is JsonValue).ToDictionary(k => k, k => aliases[k]!.GetValue<string>());
            var names = used.Values.ToHashSet(StringComparer.Ordinal);
            var piece = new JsonObject
            {
                ["materials"] = new JsonArray(materials.Where(m => names.Contains(m!["name"]!.GetValue<string>())).Select(m => m!.DeepClone()).ToArray()),
                ["aliases"] = new JsonObject(used.Select(u => KeyValuePair.Create(u.Key, (JsonNode?)JsonValue.Create(u.Value))))
            };
            File.WriteAllText(scratch, piece.ToJsonString());

            var kn5 = Kn5.CreateEmpty();
            kn5.RootNode.Name = id;
            var materialOf = ObjModel.AddMaterials(kn5, prepared, scratch, ksAmbient: 0.2f, ksDiffuse: 0.75f);
            ObjModel.AddMeshes(kn5, own, materialOf, fallback: null, castShadows: key =>
                materialOf.TryGetValue(ObjModel.MaterialOf(key), out var m) && !PreparedScene.IsGround(kn5, m, groundMaterials) && !PreparedScene.IsCutOut(kn5, m));

            var path = Path.Combine(output, id, id + ".kn5");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Kn5SceneWriter.Save(kn5, path);
            Console.WriteLine($"{id}: {own.Values.Sum(g => g.Indices.Count) / 3} triangles, {kn5.Textures.Count} textures, {new FileInfo(path).Length / 1024} KB");
            written.Add(id);
        }

        File.Delete(scratch);
        return written;
    }
}
