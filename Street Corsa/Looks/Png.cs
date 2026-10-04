using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace StreetCorsa.Looks;

/// <summary>
/// A picture written as a PNG, eight bits a channel with alpha: how the wear layer's pictures go into its model, which both
/// the game's renderer and AC read. Written on any thread (no WPF in it).
/// </summary>
internal static class Png
{
    /// <summary>The PNG of <paramref name="rgba"/>, <paramref name="width"/> by <paramref name="height"/>, rows from the top</summary>
    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        // Sized for a picture that is mostly see-through, so neither stream grows (and copies itself) much on the way
        using var output = new MemoryStream(rgba.Length / 4);
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits a channel
        header[9] = 6; // RGBA
        Chunk(output, "IHDR", header);

        // Each row behind a filter byte (0: as it is): the picture is mostly empty, which compresses well enough
        using (var raw = new MemoryStream(rgba.Length / 4))
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                var stride = width * 4;
                for (var y = 0; y < height; y++)
                {
                    zlib.WriteByte(0);
                    zlib.Write(rgba, y * stride, stride);
                }
            }
            Chunk(output, "IDAT", raw.GetBuffer().AsSpan(0, (int)raw.Length));
        }

        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> sum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sum, crc);
        output.Write(sum);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
