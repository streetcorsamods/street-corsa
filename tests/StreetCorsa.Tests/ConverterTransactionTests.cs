using System.Numerics;
using System.Text;
using StreetCorsa.Slrr;

namespace StreetCorsa.Tests;

/// <summary>
/// The parts converter's commit (<see cref="ConversionOutput"/>) on temp folders: a folder swapped in whole, put back
/// when the swap fails, recovered after a run killed half way, and the marker of a commit under way
/// </summary>
public sealed class ConverterTransactionTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Folder(string name, params (string File, string Content)[] files)
    {
        var folder = _temp.Combine(name);
        Directory.CreateDirectory(folder);
        foreach (var (file, content) in files) _temp.File(Path.Combine(name, file), content);
        return folder;
    }

    [Fact]
    public void A_swap_replaces_the_folder_whole_and_leaves_nothing_behind()
    {
        var target = Folder("_scripts", ("old.class", "old"), ("both.class", "old"));
        var staged = Folder("staging", ("new.class", "new"), ("both.class", "new"));

        Assert.True(ConversionOutput.SwapIn(target, staged));

        Assert.Equal(["both.class", "new.class"], Directory.GetFiles(target).Select(Path.GetFileName).Order());
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "both.class")));
        Assert.False(Directory.Exists(staged));
        Assert.False(Directory.Exists(target + ConversionOutput.OldSuffix));
    }

    [Fact]
    public void Nothing_staged_leaves_the_folder_as_it_is()
    {
        var target = Folder("_scripts", ("old.class", "old"));

        Assert.False(ConversionOutput.SwapIn(target, _temp.Combine("never_made")));

        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "old.class")));
    }

    [Fact]
    public void A_swap_that_cannot_move_the_new_folder_in_puts_the_old_one_back()
    {
        var target = Folder("_scripts", ("old.class", "old"));
        var staged = Folder("staging", ("new.class", "new"));

        // A file of the new folder held open without delete sharing (antivirus): the folder cannot be renamed
        using (new FileStream(Path.Combine(staged, "new.class"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => ConversionOutput.SwapIn(target, staged));
        }

        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "old.class")));
        Assert.False(Directory.Exists(target + ConversionOutput.OldSuffix));
        Assert.True(File.Exists(Path.Combine(staged, "new.class")));
    }

    [Fact]
    public void A_swap_killed_between_its_renames_is_undone_by_the_next_run()
    {
        // Killed after the old folder was parked, before the new one moved in: the parked one is the only copy
        var target = _temp.Combine("_scripts");
        Folder("_scripts" + ConversionOutput.OldSuffix, ("old.class", "old"));

        Assert.NotNull(ConversionOutput.RecoverSwap(target));

        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "old.class")));
        Assert.False(Directory.Exists(target + ConversionOutput.OldSuffix));
        Assert.Null(ConversionOutput.RecoverSwap(target));
    }

    [Fact]
    public void A_swap_killed_in_its_clean_up_leaves_a_parked_folder_the_next_run_deletes()
    {
        var target = Folder("_scripts", ("new.class", "new"));
        Folder("_scripts" + ConversionOutput.OldSuffix, ("old.class", "old"));

        Assert.NotNull(ConversionOutput.RecoverSwap(target));

        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "new.class")));
        Assert.False(Directory.Exists(target + ConversionOutput.OldSuffix));
    }

    [Fact]
    public void A_leftover_parked_folder_does_not_stop_the_next_swap()
    {
        var target = Folder("_scripts", ("current.class", "current"));
        Folder("_scripts" + ConversionOutput.OldSuffix, ("stale.class", "stale"));
        var staged = Folder("staging", ("new.class", "new"));

        Assert.True(ConversionOutput.SwapIn(target, staged));

        Assert.Equal(["new.class"], Directory.GetFiles(target).Select(Path.GetFileName));
        Assert.False(Directory.Exists(target + ConversionOutput.OldSuffix));
    }

    [Fact]
    public void The_commit_marker_sits_next_to_the_output_from_begin_to_end()
    {
        var output = Folder("parts", ("pack.json", "{}"));

        Assert.False(ConversionOutput.CommitWasInterrupted(output));
        ConversionOutput.BeginCommit(output);

        var marker = ConversionOutput.CommitMarker(output);
        Assert.Equal(_temp.Path, Path.GetDirectoryName(marker));
        Assert.True(ConversionOutput.CommitWasInterrupted(output));
        Assert.StartsWith(Path.GetFullPath(output), File.ReadAllText(marker));

        // A run killed now leaves the marker for the next one to see
        Assert.True(ConversionOutput.CommitWasInterrupted(output + Path.DirectorySeparatorChar));

        ConversionOutput.EndCommit(output);
        Assert.False(ConversionOutput.CommitWasInterrupted(output));
        Assert.Equal(["pack.json"], Directory.GetFiles(output).Select(Path.GetFileName));
    }

    [Fact]
    public void The_staging_folder_is_next_to_the_output_and_cleared_whole()
    {
        var output = _temp.Combine("parts");
        var staging = ConversionOutput.StagingFolder(output);
        Assert.Equal(_temp.Combine(".parts" + ConversionOutput.StagingSuffix), staging);

        _temp.File(Path.Combine(".parts" + ConversionOutput.StagingSuffix, "engines", "a.kn5"), "model");
        ConversionOutput.ClearStaging(staging, strict: true);
        Assert.False(Directory.Exists(staging));
        ConversionOutput.ClearStaging(staging, strict: true);
    }

    [Fact]
    public void Filling_in_copies_only_what_the_folder_lacks_and_keeps_the_layout()
    {
        var folder = Folder("new", ("a.class", "new a"));
        var from = Folder("old", ("a.class", "old a"), (Path.Combine("sub", "b.class"), "old b"));

        Assert.Equal(1, ConversionOutput.FillMissing(folder, from));

        Assert.Equal("new a", File.ReadAllText(Path.Combine(folder, "a.class")));
        Assert.Equal("old b", File.ReadAllText(Path.Combine(folder, "sub", "b.class")));
        Assert.Equal(0, ConversionOutput.FillMissing(folder, _temp.Combine("missing")));
    }
}

/// <summary>
/// The converter's SLRR readers against files a mod can hand them: whole ones read, cut short or with counts past the
/// end an <see cref="InvalidDataException"/> for that file, never an index out of range or gigabytes allocated
/// </summary>
public sealed class SlrrReaderBoundsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Save(string name, byte[] data) => _temp.Bytes(name, data);

    // ---- RPK ----

    private sealed record RpkEntry(int SuperId, int TypeId, string Alias, string Payload);

    /// <summary>An rpk: 16-byte header, 64-byte externals, the entry table's 16-byte header, 23-byte entries, payloads</summary>
    private static byte[] Rpk(string[] externals, int? declaredEntries, params RpkEntry[] entries)
    {
        using var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write("RPAK"u8);
        w.Write(0);
        w.Write(externals.Length);
        w.Write(0);
        for (var i = 0; i < externals.Length; i++)
        {
            var record = new byte[64];
            BitConverter.GetBytes((ushort)(i + 1)).CopyTo(record, 2);
            Encoding.Latin1.GetBytes(externals[i]).CopyTo(record, 4);
            w.Write(record);
        }

        w.Write(0);
        w.Write(declaredEntries ?? entries.Length);
        w.Write(0L);

        var payloadAt = (int)stream.Length + entries.Sum(e => 23 + e.Alias.Length);
        foreach (var entry in entries)
        {
            w.Write(entry.SuperId);
            w.Write(entry.TypeId);
            w.Write(new byte[6]);
            w.Write(payloadAt);
            w.Write(entry.Payload.Length);
            w.Write((byte)entry.Alias.Length);
            w.Write(Encoding.Latin1.GetBytes(entry.Alias));
            payloadAt += entry.Payload.Length;
        }

        foreach (var entry in entries) w.Write(Encoding.Latin1.GetBytes(entry.Payload));
        return stream.ToArray();
    }

    private static readonly RpkEntry Render = new(0x10, 0x20, "engine_block", "mesh 0x00000021\ntexture 0x00000022\n");

    [Fact]
    public void A_whole_rpk_reads_its_externals_and_entries()
    {
        var rpk = SlrrRpk.Load(Save("a.rpk", Rpk(["parts\\engines\\other.rpk"], null, Render)), "parts\\a.rpk");

        Assert.Equal("parts\\engines\\other.rpk", rpk.Externals[1]);
        var entry = rpk.Entries[0x20];
        Assert.Equal("engine_block", entry.Alias);
        Assert.Equal(0x10, entry.SuperId);
        Assert.Equal([0x21], entry.Ids("mesh"));
        Assert.Equal("0x00000022", entry.FirstValue("texture"));
    }

    [Fact]
    public void An_rpk_that_declares_more_entries_than_it_holds_reads_the_ones_there_are()
    {
        var rpk = SlrrRpk.Load(Save("a.rpk", Rpk([], 1000, Render)), "a.rpk");

        Assert.Equal([0x20], rpk.Entries.Keys);
    }

    [Fact]
    public void An_entry_whose_payload_lies_past_the_end_is_read_empty()
    {
        var data = Rpk([], null, Render);
        var cut = data[..^Render.Payload.Length];

        var rpk = SlrrRpk.Load(Save("a.rpk", cut), "a.rpk");

        Assert.Empty(rpk.Entries[0x20].Lines);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void An_externals_count_past_the_end_is_an_error_about_the_file(int count)
    {
        var data = Rpk(["x.rpk"], null, Render);
        BitConverter.GetBytes(count).CopyTo(data, 8);

        Assert.Throws<InvalidDataException>(() => SlrrRpk.Load(Save("a.rpk", data), "a.rpk"));
    }

    [Fact]
    public void An_rpk_cut_short_is_an_error_about_the_file()
    {
        var data = Rpk(["x.rpk"], null, Render);

        Assert.Throws<InvalidDataException>(() => SlrrRpk.Load(Save("a.rpk", data[..40]), "a.rpk"));
        Assert.Throws<InvalidDataException>(() => SlrrRpk.Load(Save("b.rpk", data[..10]), "b.rpk"));
        Assert.Throws<InvalidDataException>(() => SlrrRpk.Load(Save("c.rpk", "NOPE0000000000000000"u8.ToArray()), "c.rpk"));
    }

    // ---- SCX ----

    private const int V3MaterialSize = 84;

    /// <summary>A version 3 mesh of one material: the material block, the vertices (64 bytes each), the triangles</summary>
    private static byte[] ScxV3(Vector3[] positions, int[] indices, int? vertexCount = null, int? triangleCount = null)
    {
        using var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write("INVO"u8);
        w.Write(3);

        var material = new byte[V3MaterialSize];
        BitConverter.GetBytes(V3MaterialSize).CopyTo(material, 0);
        BitConverter.GetBytes(0.5f).CopyTo(material, 4);
        BitConverter.GetBytes(0.25f).CopyTo(material, 8);
        BitConverter.GetBytes(1f).CopyTo(material, 12);
        BitConverter.GetBytes(32f).CopyTo(material, 36);
        BitConverter.GetBytes((short)2).CopyTo(material, 0x2C);
        BitConverter.GetBytes((short)-1).CopyTo(material, 0x2C + 6);
        Encoding.Latin1.GetBytes("chrome").CopyTo(material, V3MaterialSize - 32);
        w.Write(material);

        w.Write(vertexCount ?? positions.Length);
        foreach (var p in positions)
        {
            var vertex = new byte[64];
            BitConverter.GetBytes(p.X).CopyTo(vertex, 0);
            BitConverter.GetBytes(p.Y).CopyTo(vertex, 4);
            BitConverter.GetBytes(p.Z).CopyTo(vertex, 8);
            BitConverter.GetBytes(1f).CopyTo(vertex, 16);
            w.Write(vertex);
        }

        w.Write(triangleCount ?? indices.Length / 3);
        foreach (var index in indices) w.Write(index);
        return stream.ToArray();
    }

    private static readonly Vector3[] Triangle = [new(0, 0, 0), new(100, 0, 0), new(0, 250, 0)];

    [Fact]
    public void A_whole_version_3_mesh_reads_in_metres_with_its_material()
    {
        var mesh = SlrrMesh.Load(Save("a.scx", ScxV3(Triangle, [0, 1, 2])));

        var sub = Assert.Single(mesh.SubMeshes);
        Assert.Equal("chrome", sub.MaterialName);
        Assert.Equal(2, sub.DiffuseTextureIndex);
        Assert.Equal(-1, sub.ReflectionTextureIndex);
        Assert.Equal(new Vector3(0.5f, 0.25f, 1f), sub.DiffuseColor);
        Assert.Equal([0, 1, 2], sub.Indices);
        Assert.Equal(new Vector3(0f, 2.5f, 0f), sub.Vertices[2].Position);
        Assert.Equal(Vector3.UnitY, sub.Vertices[0].Normal);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-5)]
    [InlineData(4)]
    public void A_vertex_count_past_the_end_is_an_error_about_the_file(int count)
    {
        Assert.Throws<InvalidDataException>(() => SlrrMesh.Load(Save("a.scx", ScxV3(Triangle, [0, 1, 2], vertexCount: count))));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(2)]
    public void A_triangle_count_past_the_end_is_an_error_about_the_file(int count)
    {
        Assert.Throws<InvalidDataException>(() => SlrrMesh.Load(Save("a.scx", ScxV3(Triangle, [0, 1, 2], triangleCount: count))));
    }

    [Fact]
    public void A_mesh_cut_short_is_an_error_about_the_file()
    {
        var data = ScxV3(Triangle, [0, 1, 2]);

        // Inside the material block, before the vertex count, inside the vertices
        foreach (var length in new[] { 40, 8 + V3MaterialSize + 2, 8 + V3MaterialSize + 4 + 70 })
            Assert.Throws<InvalidDataException>(() => SlrrMesh.Load(Save($"cut{length}.scx", data[..length])));

        Assert.Throws<InvalidDataException>(() => SlrrMesh.Load(Save("short.scx", data[..6])));
    }

    [Fact]
    public void An_unknown_version_is_an_error_about_the_file()
    {
        var data = ScxV3(Triangle, [0, 1, 2]);
        BitConverter.GetBytes(7).CopyTo(data, 4);

        Assert.Throws<InvalidDataException>(() => SlrrMesh.Load(Save("a.scx", data)));
    }

    /// <summary>A version 4 mesh: the chunk table, then a material chunk (no entries) and an index chunk</summary>
    private static byte[] ScxV4(ushort[] indices, int? chunkCount = null, int? indexCount = null, int? indexChunkOffset = null)
    {
        const int materialAt = 12 + 2 * 8;
        const int materialSize = 20;
        const int indicesAt = materialAt + materialSize;
        var indexSize = 12 + indices.Length * 2;

        using var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write("INVO"u8);
        w.Write(4);
        w.Write(chunkCount ?? 2);
        w.Write(0);
        w.Write(materialAt);
        w.Write(5);
        w.Write(indexChunkOffset ?? indicesAt);

        w.Write(0);
        w.Write(materialSize);
        w.Write(new byte[materialSize - 8]);

        w.Write(5);
        w.Write(indexSize);
        w.Write(indexCount ?? indices.Length);
        foreach (var index in indices) w.Write(index);
        return stream.ToArray();
    }

    [Fact]
    public void A_whole_version_4_mesh_reads_its_indices()
    {
        var sub = Assert.Single(SlrrMesh.Load(Save("a.scx", ScxV4([0, 1, 2]))).SubMeshes);

        Assert.Equal([0, 1, 2], sub.Indices);
    }

    [Theory]
    [InlineData(int.MaxValue, null, null)]
    [InlineData(-1, null, null)]
    [InlineData(null, 1_000_000, null)]
    [InlineData(null, -1, null)]
    [InlineData(null, null, 1_000_000)]
    [InlineData(null, null, -8)]
    public void Version_4_counts_and_offsets_past_the_end_are_an_error_about_the_file(int? chunks, int? indexCount, int? indexOffset)
    {
        var data = ScxV4([0, 1, 2], chunks, indexCount, indexOffset);

        Assert.Throws<InvalidDataException>(() => SlrrMesh.Load(Save("a.scx", data)));
    }
}
