using System.IO;
using AcTools.Kn5File;
using AcTools.Numerics;

namespace StreetCorsa.Looks;

/// <summary>
/// A car model's <see cref="BodyShell"/> kept on disk, so it is worked out once for a model, not once a session: by the
/// model file's path, size and time, so a model changed is looked at again. A file that cannot be read is no file: the
/// shell is worked out again. A model with no paint is kept too, as such.
/// </summary>
internal static class ShellFile
{
    private const string Magic = "SCSHELL2";

    /// <summary>The model as it is, what its kept shell and its race layers are named for: its path, size and time</summary>
    public static string KeyOf(string model)
    {
        var info = new FileInfo(model);
        return $"{Path.GetFullPath(model).ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    /// <summary>The kept file for the model in <paramref name="model"/>, in <paramref name="folder"/></summary>
    public static string PathFor(string folder, string model)
    {
        var hash = 2166136261u;
        foreach (var c in KeyOf(model)) hash = (hash ^ c) * 16777619u;
        return Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(model)}_{hash:x8}.shell");
    }

    /// <summary>
    /// The most of anything a file may say it holds: a count past it (a bad int, a file of some other kind with the same
    /// magic) is a bad file, not an array of that size
    /// </summary>
    private const int MostMeshes = 100_000;

    private const int MostVertices = 65_536;

    private const int MostIndices = 3 * 65_536;

    private const int MostOthers = 10_000;

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

    /// <summary>A count read, within <paramref name="most"/>; a bad file otherwise</summary>
    private static int Count(BinaryReader reader, int most)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > most) throw new InvalidDataException($"a count of {count} in a shell file");
        return count;
    }

    /// <summary>Keeps <paramref name="shell"/> (null: the model has no paint), whole or not at all</summary>
    public static void Write(string path, BodyShell? shell)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temp)))
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
        File.Move(temp, path, overwrite: true);
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
            var vertices = new Kn5Node.Vertex[Count(r, MostVertices)];
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = new Kn5Node.Vertex(new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                    new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), new Vec2(r.ReadSingle(), r.ReadSingle()),
                    new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            var indices = new ushort[Count(r, MostIndices)];
            for (var i = 0; i < indices.Length; i++) indices[i] = r.ReadUInt16();
            meshes.Add(new BodyShell.Mesh(vertices, indices));
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
        }
    }
}
