using System.IO;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Helpers;

namespace StreetCorsa.Looks;

/// <summary>
/// A car model's <see cref="BodyShell"/> kept on disk, so it is worked out once for a model, not once a session: by the
/// model file's path, size and time and the way shells are worked out (<see cref="BodyShell.Version"/>), so a model
/// changed, or a shell an older game worked out, is looked at again. A file that cannot be read is no file: the shell is
/// worked out again. A model with no paint is kept too, as such.
/// </summary>
internal static class ShellFile
{
    private static readonly string Magic = "SCSHELL" + BodyShell.Version;

    /// <summary>
    /// The model as it is, what its kept shell and its race layers are named for: its path, size and time, and the way its
    /// shell is worked out
    /// </summary>
    public static string KeyOf(string model)
    {
        var info = new FileInfo(model);
        // The car's animations say what of its body moves (BodyShell.MoversOf): a car given new ones, or one of them
        // edited in place, is looked at again
        var animations = Path.GetDirectoryName(model) is { } car ? BodyShell.MoversStamp(car) : "";
        return $"{Path.GetFullPath(model).ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{BodyShell.Version}|{animations}";
    }

    /// <summary>The kept file for the model in <paramref name="model"/>, in <paramref name="folder"/></summary>
    public static string PathFor(string folder, string model)
    {
        return Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(model)}_{StableHash.Fnv(KeyOf(model)):x8}.shell");
    }

    /// <summary>
    /// The most of anything a file may say it holds: a count past it (a bad int, a file of some other kind with the same
    /// magic) is a bad file, not an array of that size. A mesh's indices are bound by what is left of the file, not by a
    /// count: a heavy model's mesh has many triangles to each of its (at most 65,536) corners.
    /// </summary>
    private const int MostMeshes = 100_000;

    private const int MostVertices = 65_536;

    private const int MostOthers = 10_000;

    /// <summary>A vertex's bytes in the file: position, normal, picture place, tangent</summary>
    private const int VertexBytes = 11 * sizeof(float);

    /// <summary>The kept shell: found (null for a model with no paint), or not found when there is no good file</summary>
    public static bool TryRead(string path, out BodyShell? shell)
    {
        shell = null;
        if (!File.Exists(path)) return false;
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadString() != Magic) return false;
            if (!reader.ReadBoolean()) return true;

            var min = ReadVector(reader);
            var max = ReadVector(reader);
            var frontSign = reader.ReadSingle();
            var leftSign = reader.ReadSingle();
            var hubs = Enumerable.Range(0, Count(reader, MostOthers)).Select(_ => ReadVector(reader)).ToList();
            var pictureSize = reader.ReadInt32();
            if (pictureSize <= 0 || pictureSize > 16384) return false;
            var panels = Enumerable.Range(0, Count(reader, MostOthers)).Select(_ => new BodyShell.Panel((BodyShell.Side)reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())).ToList();
            // A panel is painted row by row at its place in the picture: one outside it, or facing no side, is a bad file,
            // not an index out of range in the painter
            if (panels.Any(p => !Enum.IsDefined(p.Side) || p.X < 0 || p.Y < 0 || p.Width < 0 || p.Height < 0
                                || p.X + (long)p.Width > pictureSize || p.Y + (long)p.Height > pictureSize)) return false;
            var body = ReadMeshes(reader);
            var screen = ReadMeshes(reader);
            var screenMin = new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle());
            var screenMax = new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle());
            var ambient = reader.ReadSingle();
            var diffuse = reader.ReadSingle();
            var paint = Enumerable.Range(0, Count(reader, MostOthers)).Select(_ => reader.ReadString()).ToList();
            shell = new BodyShell
            {
                Min = min, Max = max, FrontSign = frontSign, LeftSign = leftSign, Hubs = hubs, PictureSize = pictureSize, Panels = panels,
                BodyMeshes = body, ScreenMeshes = screen, ScreenMin = screenMin, ScreenMax = screenMax, Ambient = ambient,
                Diffuse = diffuse, Paint = paint
            };
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever is wrong with the file (cut short, a bad count, unreadable), it is no file
            shell = null;
            return false;
        }
    }

    /// <summary>
    /// A count read, within <paramref name="most"/> and within what is left of the file at <paramref name="bytesEach"/>
    /// bytes each; a bad file otherwise
    /// </summary>
    private static int Count(BinaryReader reader, int most, int bytesEach = 1)
    {
        var count = reader.ReadInt32();
        var left = reader.BaseStream.Length - reader.BaseStream.Position;
        if (count < 0 || count > most || (long)count * bytesEach > left) throw new InvalidDataException($"a count of {count} in a shell file");
        return count;
    }

    /// <summary>Keeps <paramref name="shell"/> (null: the model has no paint), whole or not at all</summary>
    public static void Write(string path, BodyShell? shell)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes))
        {
            writer.Write(Magic);
            writer.Write(shell != null);
            if (shell != null)
            {
                WriteVector(writer, shell.Min);
                WriteVector(writer, shell.Max);
                writer.Write(shell.FrontSign);
                writer.Write(shell.LeftSign);
                writer.Write(shell.Hubs.Count);
                foreach (var hub in shell.Hubs) WriteVector(writer, hub);
                writer.Write(shell.PictureSize);
                writer.Write(shell.Panels.Count);
                foreach (var p in shell.Panels)
                {
                    writer.Write((int)p.Side);
                    writer.Write(p.X);
                    writer.Write(p.Y);
                    writer.Write(p.Width);
                    writer.Write(p.Height);
                    writer.Write(p.MinA);
                    writer.Write(p.MaxA);
                    writer.Write(p.MinB);
                    writer.Write(p.MaxB);
                }
                WriteMeshes(writer, shell.BodyMeshes);
                WriteMeshes(writer, shell.ScreenMeshes);
                writer.Write(shell.ScreenMin.X);
                writer.Write(shell.ScreenMin.Y);
                writer.Write(shell.ScreenMax.X);
                writer.Write(shell.ScreenMax.Y);
                writer.Write(shell.Ambient);
                writer.Write(shell.Diffuse);
                writer.Write(shell.Paint.Count);
                foreach (var name in shell.Paint) writer.Write(name);
            }
        }
        SafeFile.WriteAllBytes(path, bytes.ToArray());
    }

    private static System.Numerics.Vector3 ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    private static void WriteVector(BinaryWriter w, System.Numerics.Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static List<BodyShell.Mesh> ReadMeshes(BinaryReader r)
    {
        var meshes = new List<BodyShell.Mesh>();
        for (var m = Count(r, MostMeshes); m > 0; m--)
        {
            var vertices = new Kn5Node.Vertex[Count(r, MostVertices, VertexBytes)];
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = new Kn5Node.Vertex(new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                    new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), new Vec2(r.ReadSingle(), r.ReadSingle()),
                    new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            var indices = new ushort[Count(r, int.MaxValue, sizeof(ushort))];
            for (var i = 0; i < indices.Length; i++)
            {
                // Every corner must be one of the mesh's: the painter reads them unchecked
                indices[i] = r.ReadUInt16();
                if (indices[i] >= vertices.Length) throw new InvalidDataException($"an index of {indices[i]} in a mesh of {vertices.Length} corners in a shell file");
            }
            string? mover = null;
            var rest = default(System.Numerics.Matrix4x4);
            if (r.ReadBoolean())
            {
                mover = r.ReadString();
                for (var row = 0; row < 4; row++)
                for (var column = 0; column < 4; column++)
                    rest[row, column] = r.ReadSingle();
            }
            meshes.Add(new BodyShell.Mesh(vertices, indices, mover, rest));
        }
        return meshes;
    }

    private static void WriteMeshes(BinaryWriter w, IReadOnlyList<BodyShell.Mesh> meshes)
    {
        w.Write(meshes.Count);
        foreach (var mesh in meshes)
        {
            w.Write(mesh.Vertices.Length);
            foreach (var v in mesh.Vertices)
            {
                w.Write(v.Position.X); w.Write(v.Position.Y); w.Write(v.Position.Z);
                w.Write(v.Normal.X); w.Write(v.Normal.Y); w.Write(v.Normal.Z);
                w.Write(v.Tex.X); w.Write(v.Tex.Y);
                w.Write(v.Tangent.X); w.Write(v.Tangent.Y); w.Write(v.Tangent.Z);
            }
            w.Write(mesh.Indices.Length);
            foreach (var i in mesh.Indices) w.Write(i);
            w.Write(mesh.Mover != null);
            if (mesh.Mover == null) continue;
            w.Write(mesh.Mover);
            for (var row = 0; row < 4; row++)
            for (var column = 0; column < 4; column++)
                w.Write(mesh.Rest[row, column]);
        }
    }
}
