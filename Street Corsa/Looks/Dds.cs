using System.Buffers.Binary;

namespace StreetCorsa.Looks;

/// <summary>
/// A picture written as an uncompressed DDS (32-bit BGRA) with its whole mip chain: what the game's own scenes load the
/// wear layer from. Nothing has to be decoded or shrunk when the renderer takes it (a PNG is decoded and its mips made as
/// it loads), so a car's wear goes on without a hitch. Bigger than a PNG: races, which keep files, take the PNG.
/// </summary>
internal static class Dds
{
    /// <summary>The DDS of <paramref name="rgba"/>, <paramref name="width"/> by <paramref name="height"/> (powers of two), rows from the top</summary>
    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        var levels = 1;
        for (int w = width, h = height; w > 1 || h > 1; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2)) levels++;

        var total = 0;
        for (int l = 0, w = width, h = height; l < levels; l++, w = Math.Max(1, w / 2), h = Math.Max(1, h / 2)) total += w * h * 4;

        var output = new byte[128 + total];
        var header = output.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x20534444); // "DDS "
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 124);
        // Caps, height, width, pitch, pixel format, mip count
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], 0x1 | 0x2 | 0x4 | 0x8 | 0x1000 | 0x20000);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], height);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], width * 4);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], levels);
        // The pixel format: 32 bits of RGB with alpha, laid out B, G, R, A
        BinaryPrimitives.WriteInt32LittleEndian(header[76..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header[80..], 0x40 | 0x1);
        BinaryPrimitives.WriteInt32LittleEndian(header[88..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header[92..], 0x00FF0000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[96..], 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(header[100..], 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(header[104..], 0xFF000000);
        // A texture with mips
        BinaryPrimitives.WriteUInt32LittleEndian(header[108..], 0x1000 | 0x8 | 0x400000);

        // The first level, swizzled; then each level the one before averaged two by two
        var at = 128;
        for (var i = 0; i < width * height; i++, at += 4)
        {
            output[at] = rgba[i * 4 + 2];
            output[at + 1] = rgba[i * 4 + 1];
            output[at + 2] = rgba[i * 4];
            output[at + 3] = rgba[i * 4 + 3];
        }

        var previous = 128;
        for (int l = 1, pw = width, ph = height; l < levels; l++)
        {
            int w = Math.Max(1, pw / 2), h = Math.Max(1, ph / 2);
            for (var y = 0; y < h; y++)
            {
                var y0 = Math.Min(ph - 1, y * 2);
                var y1 = Math.Min(ph - 1, y * 2 + 1);
                for (var x = 0; x < w; x++, at += 4)
                {
                    var x0 = Math.Min(pw - 1, x * 2);
                    var x1 = Math.Min(pw - 1, x * 2 + 1);
                    var a = previous + (y0 * pw + x0) * 4;
                    var b = previous + (y0 * pw + x1) * 4;
                    var c = previous + (y1 * pw + x0) * 4;
                    var d = previous + (y1 * pw + x1) * 4;
                    for (var k = 0; k < 4; k++)
                        output[at + k] = (byte)((output[a + k] + output[b + k] + output[c + k] + output[d + k] + 2) / 4);
                }
            }
            previous += pw * ph * 4;
            pw = w;
            ph = h;
        }

        return output;
    }
}
