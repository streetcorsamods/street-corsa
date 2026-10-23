using System.IO.Compression;
using AcTools.Kn5File;
using StreetCorsa.Tools;

namespace RunningGearParts;

/// <summary>
/// What a surface is made of: its colour (sRGB) and how it shines, and the grain it shows (<see cref="Grain"/>, none: plain).
/// The grain's picture is laid on as GearMesh's texture coordinates go: along a lathe's profile is down the picture, so a
/// grain of rows is rings round the axle on a turned face
/// </summary>
internal sealed record Finish(string Name, byte R, byte G, byte B, float Specular, float SpecularExp, Grain Grain = Grain.None)
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
    /// <summary>A brake rotor's faces: steel turned on a lathe, its fine rings</summary>
    public static readonly Finish Machined = new("rg_machined", 112, 114, 118, 0.55f, 26f, Grain.Rings);
    /// <summary>A brake pad's lining: dark, dull, flecked</summary>
    public static readonly Finish Lining = new("rg_lining", 26, 23, 21, 0.04f, 3f, Grain.Flecks);
}

/// <summary>The grain of a finish's picture</summary>
internal enum Grain
{
    None,
    /// <summary>Rows a little lighter or darker than each other (a lathe's cut), a fine noise over them</summary>
    Rings,
    /// <summary>Light and dark flecks all over (a pressed lining)</summary>
    Flecks
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
            var data = finish.Grain == Grain.None ? Swatch(finish.R, finish.G, finish.B) : Grained(finish);
            kn5.Textures[texture] = new Kn5Texture { Name = texture, Active = true, Length = data.Length };
            kn5.TexturesData[texture] = data;
            var material = Kn5SceneWriter.AddMaterial(kn5, finish.Name, texture, 0.35f, 0.55f, finish.Specular, finish.SpecularExp);
            Kn5SceneWriter.AddMesh(kn5, $"{Name}_{finish.Name}", mesh.Vertices, mesh.Indices, material, castShadows: true);
        }

        Kn5SceneWriter.Save(kn5, path);
    }

    /// <summary>A 4 x 4 picture of one colour, as a PNG</summary>
    private static byte[] Swatch(byte r, byte g, byte b) => Png(4, (_, _) => 1f, r, g, b);

    /// <summary>A finish's grain as a 256 x 256 picture about its colour, the same every run (a fixed seed)</summary>
    private static byte[] Grained(Finish finish)
    {
        const int size = 256;
        var random = new Random(finish.Name.Length * 7919 + (int)finish.Grain);
        float Noise() => (float)(random.NextDouble() * 2 - 1);
        var row = new float[size];
        var drift = 0f;
        for (var y = 0; y < size; y++)
        {
            drift = drift * 0.8f + Noise() * 0.02f;
            row[y] = drift + Noise() * 0.035f;
        }

        var grain = new float[size, size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                grain[y, x] = finish.Grain == Grain.Rings ? 1f + row[y] + Noise() * 0.02f
                    : 1f + (random.NextDouble() < 0.08 ? Noise() * 0.6f : Noise() * 0.12f);
            }
        }

        return Png(size, (x, y) => grain[y, x], finish.R, finish.G, finish.B);
    }

    /// <summary>A square picture of a colour, each pixel's brightness scaled by <paramref name="shade"/>, as a PNG</summary>
    private static byte[] Png(int size, Func<int, int, float> shade, byte r, byte g, byte b)
    {
        static byte Scaled(byte c, float by) => (byte)Math.Clamp(MathF.Round(c * by), 0, 255);
        var raw = new byte[size * (1 + size * 3)];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var at = y * (1 + size * 3) + 1 + x * 3;
                var by = shade(x, y);
                (raw[at], raw[at + 1], raw[at + 2]) = (Scaled(r, by), Scaled(g, by), Scaled(b, by));
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(raw);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", [0, 0, (byte)(size >> 8), (byte)size, 0, 0, (byte)(size >> 8), (byte)size, 8, 2, 0, 0, 0]);
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
