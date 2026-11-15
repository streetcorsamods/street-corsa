using System.Buffers.Binary;

namespace StreetCorsa.Services.Race.Traffic.Conversion
{
    /// <summary>Decodes any texture a model carries into its texels, RGBA, rows from the top, at most a size a side</summary>
    public interface ITexels
    {
        /// <summary>The texels, <paramref name="most"/> a side at most (its shape kept); null when the bytes do not decode</summary>
        (int Width, int Height, byte[] Rgba)? Read(byte[] bytes, int most);
    }

    /// <summary>
    /// A traffic model's textures made light: ported from Race Explorer's <c>kn5tex.py</c> and <c>dxt.py</c> (2026-09-06),
    /// written when the fleet's near models were 9 GB, 6.9 GB of it textures, and the game lost frames as they loaded.
    /// <list type="bullet">
    /// <item>A DDS with mip levels is capped by dropping its levels above the cap: byte-exact for the ones that stay.</item>
    /// <item>
    /// A raw texture (an uncompressed DDS, a PNG, a JPEG) over 64 KB is block-compressed, DXT1 when it is opaque and DXT5
    /// when not, with its whole mip chain: a raw 1024 px RGBA set is 5.6 MB where DXT5 is 1.4 MB, in video memory too.
    /// </item>
    /// <item>A block-compressed DDS too big and without levels is decoded at the cap and compressed again.</item>
    /// </list>
    /// Cube maps, volumes and formats it cannot size are left as they are. The encoder is the plain "range fit" (a
    /// block's two end colours are the corners of its colour box): fine for a car seen from ten metres.
    /// </summary>
    public static class TrafficTextures
    {
        /// <summary>A raw texture under this many bytes is left raw: compressing it saves nothing worth the time</summary>
        public const int LeastToCompress = 65536;

        private const uint DdsMagic = 0x20534444;
        private const uint FlagMipCount = 0x20000;
        private const uint FlagLinearSize = 0x80000;
        private const uint FlagPitch = 0x8;
        private const uint PixelFourCc = 0x4;
        private const uint PixelRgb = 0x40;
        private const uint PixelLuminance = 0x20000;
        private const uint Caps2CubeOrVolume = 0x200 | 0x200000;

        // Bytes per 4×4 block of the compressed formats cars ship, by four-character code and by DXGI format
        private static readonly Dictionary<string, int> BlockBytes = new()
        {
            ["DXT1"] = 8, ["DXT2"] = 16, ["DXT3"] = 16, ["DXT4"] = 16, ["DXT5"] = 16,
            ["ATI1"] = 8, ["BC4U"] = 8, ["BC4S"] = 8, ["ATI2"] = 16, ["BC5U"] = 16, ["BC5S"] = 16
        };
        private static readonly Dictionary<uint, int> DxgiBlockBytes = new()
        {
            [71] = 8, [72] = 8, [74] = 16, [75] = 16, [77] = 16, [78] = 16, [80] = 8, [81] = 8, [83] = 16, [84] = 16,
            [95] = 16, [96] = 16, [98] = 16, [99] = 16
        };
        private static readonly Dictionary<uint, int> DxgiBitsPerPixel = new() { [28] = 32, [29] = 32, [87] = 32, [88] = 32, [61] = 8, [56] = 16 };

        /// <summary>What was done to a texture</summary>
        public enum Change { None, Capped, Compressed }

        /// <summary>
        /// One texture through the pipeline: its levels over <paramref name="maxSide"/> dropped (or, without levels, it is
        /// decoded at the cap), then block-compressed when it is raw and worth it; the bytes as they were when nothing applies
        /// </summary>
        public static (byte[] Bytes, Change Change) Process(byte[] bytes, int maxSide, ITexels? texels)
        {
            var capped = CapLevels(bytes, maxSide);
            var change = ReferenceEquals(capped, bytes) ? Change.None : Change.Capped;

            var oversize = Size(capped) is { } size && Math.Max(size.Width, size.Height) > maxSide;
            if (texels != null && (IsRaw(capped) && capped.Length >= LeastToCompress || oversize && !IsCubeOrVolume(capped)))
            {
                if (texels.Read(capped, maxSide) is { } read)
                {
                    var encoded = EncodeDxt(read.Rgba, read.Width, read.Height);
                    if (encoded.Length < capped.Length || oversize) return (encoded, Change.Compressed);
                }
            }
            return (capped, change);
        }

        /// <summary>A DDS's size from its header, any other texture's from its own (PNG, JPEG); null when it cannot tell</summary>
        public static (int Width, int Height)? Size(byte[] bytes) =>
            IsDds(bytes) ? ((int)U32(bytes, 16), (int)U32(bytes, 12)) : Looks.TexelReader.HeaderSize(bytes);

        public static bool IsDds(byte[] bytes) => bytes.Length >= 128 && U32(bytes, 0) == DdsMagic;

        private static bool IsCubeOrVolume(byte[] bytes) => IsDds(bytes) && (U32(bytes, 112) & Caps2CubeOrVolume) != 0;

        /// <summary>
        /// Whether a texture is stored raw, the GPU's work left undone: an image file (PNG, JPEG...), or a DDS of plain RGB
        /// or luminance texels. A block-compressed DDS, a DX10 one, a cube map or a volume is not.
        /// </summary>
        public static bool IsRaw(byte[] bytes)
        {
            if (bytes.Length < 8) return false;
            if (!IsDds(bytes)) return true;
            if (IsCubeOrVolume(bytes)) return false;
            var flags = U32(bytes, 80);
            return (flags & PixelFourCc) == 0 && U32(bytes, 84) == 0 && (flags & (PixelRgb | PixelLuminance)) != 0;
        }

        /// <summary>
        /// The same DDS with every level wider or taller than <paramref name="maxSide"/> left out and its header saying so;
        /// the very bytes given when it has no levels to drop, is small enough, or is of a kind it cannot size
        /// </summary>
        public static byte[] CapLevels(byte[] bytes, int maxSide)
        {
            if (!IsDds(bytes)) return bytes;
            var flags = U32(bytes, 8);
            var height = (int)U32(bytes, 12);
            var width = (int)U32(bytes, 16);
            var levels = (int)U32(bytes, 28);
            var pixelFlags = U32(bytes, 80);
            var fourCc = System.Text.Encoding.ASCII.GetString(bytes, 84, 4);
            if (levels <= 1 || IsCubeOrVolume(bytes) || Math.Max(width, height) <= maxSide) return bytes;

            var headerBytes = 128;
            var block = 0;
            var bitsPerPixel = 0;
            if ((pixelFlags & PixelFourCc) != 0 && fourCc == "DX10")
            {
                if (bytes.Length < 148) return bytes;
                var dxgi = U32(bytes, 128);
                headerBytes = 148;
                if (DxgiBlockBytes.TryGetValue(dxgi, out var b)) block = b;
                else if (DxgiBitsPerPixel.TryGetValue(dxgi, out var bpp)) bitsPerPixel = bpp;
                else return bytes;
            }
            else if ((pixelFlags & PixelFourCc) != 0)
            {
                if (!BlockBytes.TryGetValue(fourCc, out block)) return bytes;
            }
            else if ((pixelFlags & PixelRgb) != 0)
            {
                bitsPerPixel = (int)U32(bytes, 88);
                if (bitsPerPixel is not (8 or 16 or 24 or 32)) return bytes;
            }
            else
            {
                return bytes;
            }

            // Walk the chain, measuring each level, to the first that fits
            long at = headerBytes;
            var (w, h) = (width, height);
            var skipped = 0;
            for (var i = 0; i < levels && Math.Max(w, h) > maxSide; i++)
            {
                at += LevelBytes(w, h, block, bitsPerPixel);
                (w, h) = (Math.Max(1, w / 2), Math.Max(1, h / 2));
                skipped++;
            }
            if (skipped == 0 || at >= bytes.Length) return bytes;

            var output = new byte[headerBytes + (bytes.Length - at)];
            Array.Copy(bytes, output, headerBytes);
            Array.Copy(bytes, at, output, headerBytes, bytes.Length - at);
            Put(output, 12, (uint)h);
            Put(output, 16, (uint)w);
            Put(output, 28, (uint)(levels - skipped));
            if ((flags & FlagLinearSize) != 0 || block != 0) Put(output, 20, (uint)LevelBytes(w, h, block, bitsPerPixel));
            else if ((flags & FlagPitch) != 0) Put(output, 20, (uint)(Math.Max(1, w) * bitsPerPixel / 8));
            return output;
        }

        private static long LevelBytes(int w, int h, int block, int bitsPerPixel) =>
            block != 0 ? (long)Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * block : (long)Math.Max(1, w) * Math.Max(1, h) * bitsPerPixel / 8;

        /// <summary>
        /// RGBA texels (rows from the top) as a DXT1 DDS when every texel is opaque, DXT5 when not, with the whole mip chain.
        /// Direct3D 11 refuses a block-compressed texture whose top level is not a multiple of 4 a side, and reads the
        /// chain as half (rounded down) a level, so the top is resampled to a multiple of 4 and every level to the size
        /// the loader expects (the first port padded instead, and 33 models of the fleet lost their textures, 2026-09-06).
        /// </summary>
        public static byte[] EncodeDxt(byte[] rgba, int width, int height)
        {
            var alpha = false;
            for (var i = 3; i < rgba.Length; i += 4)
            {
                if (rgba[i] != 255)
                {
                    alpha = true;
                    break;
                }
            }

            var (w4, h4) = (Math.Max(4, width - width % 4), Math.Max(4, height - height % 4));
            var top = (w4, h4) == (width, height) ? rgba : Resize(rgba, width, height, w4, h4);

            var levels = new List<byte[]>();
            var (lw, lh) = (w4, h4);
            var level = top;
            var (pw, ph) = (w4, h4);
            while (true)
            {
                if ((lw, lh) != (pw, ph)) level = Resize(level, pw, ph, lw, lh);
                (pw, ph) = (lw, lh);
                levels.Add(EncodeLevel(level, lw, lh, alpha));
                if (lw == 1 && lh == 1) break;
                (lw, lh) = (Math.Max(1, lw / 2), Math.Max(1, lh / 2));
            }

            var total = 128 + levels.Sum(l => l.Length);
            var output = new byte[total];
            Put(output, 0, DdsMagic);
            Put(output, 4, 124);
            Put(output, 8, 0x1 | 0x2 | 0x4 | 0x1000 | FlagMipCount | FlagLinearSize);
            Put(output, 12, (uint)h4);
            Put(output, 16, (uint)w4);
            Put(output, 20, (uint)levels[0].Length);
            Put(output, 28, (uint)levels.Count);
            Put(output, 76, 32);
            Put(output, 80, PixelFourCc);
            System.Text.Encoding.ASCII.GetBytes(alpha ? "DXT5" : "DXT1").CopyTo(output, 84);
            Put(output, 108, 0x8 | 0x1000 | 0x400000);
            var at = 128;
            foreach (var l in levels)
            {
                l.CopyTo(output, at);
                at += l.Length;
            }
            return output;
        }

        /// <summary>
        /// Texels resampled to exactly <paramref name="toWidth"/> by <paramref name="toHeight"/>, each output texel the
        /// average of the source area under it (a box filter, as a mip level should be)
        /// </summary>
        public static byte[] Resize(byte[] rgba, int width, int height, int toWidth, int toHeight)
        {
            var output = new byte[toWidth * toHeight * 4];
            var sx = (double)width / toWidth;
            var sy = (double)height / toHeight;
            Span<double> sum = stackalloc double[4];
            for (var y = 0; y < toHeight; y++)
            {
                var y0 = y * sy;
                var y1 = Math.Min(height, (y + 1) * sy);
                for (var x = 0; x < toWidth; x++)
                {
                    var x0 = x * sx;
                    var x1 = Math.Min(width, (x + 1) * sx);
                    sum.Clear();
                    var weight = 0.0;
                    for (var yy = (int)y0; yy < y1; yy++)
                    {
                        var wy = Math.Min(yy + 1, y1) - Math.Max(yy, y0);
                        for (var xx = (int)x0; xx < x1; xx++)
                        {
                            var wx = Math.Min(xx + 1, x1) - Math.Max(xx, x0);
                            var wgt = wx * wy;
                            var at = (yy * width + xx) * 4;
                            for (var c = 0; c < 4; c++) sum[c] += rgba[at + c] * wgt;
                            weight += wgt;
                        }
                    }
                    var o = (y * toWidth + x) * 4;
                    for (var c = 0; c < 4; c++) output[o + c] = (byte)Math.Clamp(Math.Round(sum[c] / Math.Max(weight, 1e-9)), 0, 255);
                }
            }
            return output;
        }

        /// <summary>One level's blocks, edge texels repeated where the level is smaller than a block</summary>
        private static byte[] EncodeLevel(byte[] rgba, int width, int height, bool alpha)
        {
            var bw = (width + 3) / 4;
            var bh = (height + 3) / 4;
            var blockBytes = alpha ? 16 : 8;
            var output = new byte[bw * bh * blockBytes];
            Span<byte> block = stackalloc byte[64];
            for (var by = 0; by < bh; by++)
            for (var bx = 0; bx < bw; bx++)
            {
                for (var py = 0; py < 4; py++)
                for (var px = 0; px < 4; px++)
                {
                    var x = Math.Min(bx * 4 + px, width - 1);
                    var y = Math.Min(by * 4 + py, height - 1);
                    var from = (y * width + x) * 4;
                    var to = (py * 4 + px) * 4;
                    block[to] = rgba[from];
                    block[to + 1] = rgba[from + 1];
                    block[to + 2] = rgba[from + 2];
                    block[to + 3] = rgba[from + 3];
                }

                var at = (by * bw + bx) * blockBytes;
                if (alpha)
                {
                    AlphaBlock(block, output.AsSpan(at, 8));
                    at += 8;
                }
                ColourBlock(block, output.AsSpan(at, 8));
            }
            return output;
        }

        /// <summary>A block's colour: its two ends the corners of its colour box in 565, every texel the nearest of four</summary>
        private static void ColourBlock(ReadOnlySpan<byte> block, Span<byte> output)
        {
            int rMin = 255, gMin = 255, bMin = 255, rMax = 0, gMax = 0, bMax = 0;
            for (var i = 0; i < 16; i++)
            {
                int r = block[i * 4], g = block[i * 4 + 1], b = block[i * 4 + 2];
                rMin = Math.Min(rMin, r); gMin = Math.Min(gMin, g); bMin = Math.Min(bMin, b);
                rMax = Math.Max(rMax, r); gMax = Math.Max(gMax, g); bMax = Math.Max(bMax, b);
            }

            var c0 = Quantise(rMax, gMax, bMax);
            var c1 = Quantise(rMin, gMin, bMin);
            if (c0 < c1) (c0, c1) = (c1, c0);

            Span<int> palette = stackalloc int[12];
            Dequantise(c0, palette[..3]);
            Dequantise(c1, palette.Slice(3, 3));
            for (var c = 0; c < 3; c++)
            {
                palette[6 + c] = (2 * palette[c] + palette[3 + c]) / 3;
                palette[9 + c] = (palette[c] + 2 * palette[3 + c]) / 3;
            }

            uint indices = 0;
            if (c0 != c1)
            {
                for (var i = 0; i < 16; i++)
                {
                    var best = 0;
                    var bestDistance = int.MaxValue;
                    for (var p = 0; p < 4; p++)
                    {
                        var dr = block[i * 4] - palette[p * 3];
                        var dg = block[i * 4 + 1] - palette[p * 3 + 1];
                        var db = block[i * 4 + 2] - palette[p * 3 + 2];
                        var d = dr * dr + dg * dg + db * db;
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = p;
                        }
                    }
                    indices |= (uint)best << (2 * i);
                }
            }

            BinaryPrimitives.WriteUInt16LittleEndian(output, c0);
            BinaryPrimitives.WriteUInt16LittleEndian(output[2..], c1);
            BinaryPrimitives.WriteUInt32LittleEndian(output[4..], indices);
        }

        /// <summary>A DXT5 block's alpha: its ends the least and most, six steps between, every texel the nearest of eight</summary>
        private static void AlphaBlock(ReadOnlySpan<byte> block, Span<byte> output)
        {
            int a0 = 0, a1 = 255;
            for (var i = 0; i < 16; i++)
            {
                a0 = Math.Max(a0, block[i * 4 + 3]);
                a1 = Math.Min(a1, block[i * 4 + 3]);
            }

            Span<int> palette = stackalloc int[8];
            palette[0] = a0;
            palette[1] = a1;
            for (var i = 1; i <= 6; i++) palette[1 + i] = ((7 - i) * a0 + i * a1) / 7;

            ulong bits = 0;
            if (a0 != a1)
            {
                for (var i = 0; i < 16; i++)
                {
                    var best = 0;
                    var bestDistance = int.MaxValue;
                    for (var p = 0; p < 8; p++)
                    {
                        var d = Math.Abs(block[i * 4 + 3] - palette[p]);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = p;
                        }
                    }
                    bits |= (ulong)best << (3 * i);
                }
            }

            output[0] = (byte)a0;
            output[1] = (byte)a1;
            for (var k = 0; k < 6; k++) output[2 + k] = (byte)(bits >> (8 * k));
        }

        private static ushort Quantise(int r, int g, int b) => (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

        private static void Dequantise(ushort q, Span<int> rgb)
        {
            int r = (q >> 11) & 31, g = (q >> 5) & 63, b = q & 31;
            rgb[0] = (r * 255 + 15) / 31;
            rgb[1] = (g * 255 + 31) / 63;
            rgb[2] = (b * 255 + 15) / 31;
        }

        private static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));

        private static void Put(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    }
}
