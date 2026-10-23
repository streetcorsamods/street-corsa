using System.IO.Compression;
using AcTools.Kn5File;
using StreetCorsa.Tools;

namespace RunningGearParts;

/// <summary>What a surface is made of: its colour (sRGB) and how it shines</summary>
internal sealed record Finish(string Name, byte R, byte G, byte B, float Specular, float SpecularExp)
{
    public static readonly Finish CastIron = new("rg_cast_iron", 62, 62, 64, 0.15f, 8f);
    public static readonly Finish Steel = new("rg_steel", 120, 122, 126, 0.45f, 20f);
    public static readonly Finish Paint = new("rg_black_paint", 22, 22, 24, 0.35f, 30f);
    public static readonly Finish Rubber = new("rg_rubber", 14, 14, 14, 0.05f, 4f);
    public static readonly Finish Chrome = new("rg_chrome", 200, 202, 206, 0.9f, 60f);
    public static readonly Finish Brass = new("rg_brass", 170, 140, 70, 0.6f, 30f);
    public static readonly Finish Red = new("rg_red", 170, 24, 20, 0.5f, 30f);
    public static readonly Finish Blue = new("rg_blue", 30, 60, 150, 0.5f, 30f);
    public static readonly Finish Gold = new("rg_gold", 190, 150, 40, 0.6f, 30f);
    public static readonly Finish Aluminium = new("rg_aluminium", 196, 198, 202, 0.75f, 50f);
    public static readonly Finish GreyCast = new("rg_grey_cast", 104, 106, 110, 0.3f, 14f);
    public static readonly Finish Grey = new("rg_grey_paint", 70, 72, 74, 0.35f, 24f);
}

/// <summary>
/// One part's model as it is put together: a mesh for each finish, written as a KN5 of plain ksPerPixel materials, each
/// drawn with a small swatch of its colour
/// </summary>
internal sealed class Model(string name)
{
    private readonly Dictionary<Finish, GearMesh> _meshes = new();

    public string Name { get; } = name;

    /// <summary>The mesh of a finish, to add shapes to</summary>
    public GearMesh this[Finish finish] => _meshes.TryGetValue(finish, out var mesh) ? mesh : _meshes[finish] = new GearMesh();

    public int Triangles => _meshes.Values.Sum(m => m.Indices.Count / 3);

    /// <summary>Another model's shapes added to this one's, finish by finish, made bigger about the origin by <paramref name="scale"/></summary>
    public Model Add(Model other, float scale = 1f)
    {
        foreach (var (finish, mesh) in other._meshes)
        {
            var target = this[finish];
            var offset = target.Vertices.Count;
            target.Vertices.AddRange(mesh.Vertices.Select(v => new Kn5Node.Vertex(v.Position * scale, v.Normal, v.Tex, v.Tangent)));
            target.Indices.AddRange(mesh.Indices.Select(i => i + offset));
        }

        return this;
    }

    public void Save(string path)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = Name;
        foreach (var (finish, mesh) in _meshes.Where(m => m.Value.Indices.Count > 0).OrderBy(m => m.Key.Name, StringComparer.Ordinal))
        {
            var texture = finish.Name + ".png";
            var data = Swatch(finish.R, finish.G, finish.B);
            kn5.Textures[texture] = new Kn5Texture { Name = texture, Active = true, Length = data.Length };
            kn5.TexturesData[texture] = data;
            var material = Kn5SceneWriter.AddMaterial(kn5, finish.Name, texture, 0.35f, 0.55f, finish.Specular, finish.SpecularExp);
            Kn5SceneWriter.AddMesh(kn5, $"{Name}_{finish.Name}", mesh.Vertices, mesh.Indices, material, castShadows: true);
        }

        Kn5SceneWriter.Save(kn5, path);
    }

    /// <summary>A 4 x 4 picture of one colour, as a PNG</summary>
    private static byte[] Swatch(byte r, byte g, byte b)
    {
        const int size = 4;
        var raw = new byte[size * (1 + size * 3)];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var at = y * (1 + size * 3) + 1 + x * 3;
                (raw[at], raw[at + 1], raw[at + 2]) = (r, g, b);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(raw);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", [0, 0, 0, size, 0, 0, 0, size, 8, 2, 0, 0, 0]);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
        data.CopyTo(body, 4);
        WriteBigEndian(png, (uint)data.Length);
        png.Write(body);
        WriteBigEndian(png, Crc(body));
    }

    private static void WriteBigEndian(Stream s, uint value) => s.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static uint Crc(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }
}
