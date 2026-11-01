using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StreetCorsa.Audio;
using StreetCorsa.Controls;
using StreetCorsa.Looks;
using StreetCorsa.Mods;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-25 audit's fixes in the sound, the controls and the looks: FMOD 1.08's structures as the header lays them
/// out, what counts as a lost device, the wear layer's DDS and PNG, the texture sizes a model or a header may claim, the
/// sound files a mod's cue may name, and the adapter the D3D9 side is made on
/// </summary>
public sealed class Audit1025ETests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ----- FMOD 1.08: every structure handed to the DLL keeps the header's size and offsets (x64) -----

    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    [Fact]
    public void The_dsp_description_is_the_1_08_headers_216_bytes_its_callbacks_where_fmod_reads_them()
    {
        Assert.Equal(216, Unsafe.SizeOf<FmodPlugins.DspDescription>());
        Assert.Equal(216, Marshal.SizeOf<FmodPlugins.DspDescription>());

        var offsets = new (string Field, int Offset)[]
        {
            ("PluginSdkVersion", 0), ("Name", 4), ("Version", 36), ("NumInputBuffers", 40), ("NumOutputBuffers", 44),
            ("Create", 48), ("Release", 56), ("Reset", 64), ("Read", 72), ("NumParameters", 96), ("ParameterDescs", 104),
            ("SetFloat", 112), ("SetBool", 128), ("SetData", 136), ("GetFloat", 144), ("GetBool", 160), ("GetData", 168)
        };
        foreach (var (field, offset) in offsets) Assert.Equal(offset, Offset<FmodPlugins.DspDescription>(field));
    }

    [Fact]
    public void A_dsp_parameter_description_is_88_bytes_its_ranges_over_one_another_at_48()
    {
        Assert.Equal(88, Unsafe.SizeOf<FmodPlugins.ParameterDesc>());

        var offsets = new (string Field, int Offset)[]
        {
            ("Type", 0), ("Name", 4), ("Label", 20), ("Description", 40), ("Minimum", 48), ("BoolDefault", 48),
            ("DataType", 48), ("Maximum", 52), ("Default", 56)
        };
        foreach (var (field, offset) in offsets) Assert.Equal(offset, Offset<FmodPlugins.ParameterDesc>(field));
    }

    [Fact]
    public void The_plugins_own_state_and_the_studio_structures_keep_their_sizes()
    {
        // The gain's two floats and a flag; the filter's two floats and two FMOD_3D_ATTRIBUTES of four vectors
        Assert.Equal(12, Unsafe.SizeOf<FmodPlugins.GainState>());
        Assert.Equal(8 + 2 * 4 * 3 * sizeof(float), Unsafe.SizeOf<FmodPlugins.FilterState>());
        Assert.Equal(8, Offset<FmodPlugins.FilterState>("Attributes"));

        // FMOD_STUDIO_PARAMETER_DESCRIPTION: the name's pointer, index, minimum, maximum, default, type
        Assert.Equal(32, Marshal.SizeOf<FmodStudio.ParameterDescription>());
        Assert.Equal(8, Offset<FmodStudio.ParameterDescription>("Index"));
        Assert.Equal(24, Offset<FmodStudio.ParameterDescription>("Type"));

        // FMOD_3D_ATTRIBUTES: position, velocity, forward, up
        Assert.Equal(12, Marshal.SizeOf<FmodStudio.Vector>());
        Assert.Equal(48, Marshal.SizeOf<FmodStudio.Attributes3D>());
        Assert.Equal(36, Offset<FmodStudio.Attributes3D>("Up"));

        // FMOD_TAG: type, data type, name, data, length, updated
        Assert.Equal(32, Marshal.SizeOf<FmodStudio.Tag>());
        Assert.Equal(8, Offset<FmodStudio.Tag>("Name"));
        Assert.Equal(16, Offset<FmodStudio.Tag>("Data"));
        Assert.Equal(24, Offset<FmodStudio.Tag>("DataLength"));

        // FMOD_CREATESOUNDEXINFO of 1.08 on x64
        Assert.Equal(216, FmodStudio.CreateSoundExInfoSize);
    }

    // ----- IsDeviceLost: the DXGI and D3D9 codes, however deep, as SlimDX or Vortice reports them -----

    /// <summary>
    /// A SlimDX exception carrying <paramref name="code"/>, as SlimDX throws for a failed call (its result code, not its
    /// HResult); made by reflection, as the tests do not build against SlimDX themselves
    /// </summary>
    private static Exception SlimDXFailure(int code)
    {
        var resultType = Type.GetType("SlimDX.Result, SlimDX", throwOnError: true)!;
        var exceptionType = Type.GetType("SlimDX.SlimDXException, SlimDX", throwOnError: true)!;
        var result = Activator.CreateInstance(resultType)!;
        resultType.GetField("m_Code", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(result, code);
        return (Exception)Activator.CreateInstance(exceptionType, result)!;
    }

    private static int ResultCodeOf(Exception slimDX)
    {
        var result = slimDX.GetType().GetProperty("ResultCode")!.GetValue(slimDX)!;
        return (int)result.GetType().GetProperty("Code")!.GetValue(result)!;
    }

    [Theory]
    [InlineData(unchecked((int)0x887A0005))] // DXGI_ERROR_DEVICE_REMOVED
    [InlineData(unchecked((int)0x887A0006))] // DXGI_ERROR_DEVICE_HUNG
    [InlineData(unchecked((int)0x887A0007))] // DXGI_ERROR_DEVICE_RESET
    [InlineData(unchecked((int)0x887A0020))] // DXGI_ERROR_DRIVER_INTERNAL_ERROR
    [InlineData(unchecked((int)0x88760868))] // D3DERR_DEVICELOST
    [InlineData(unchecked((int)0x88760870))] // D3DERR_DEVICEREMOVED
    [InlineData(unchecked((int)0x88760874))] // D3DERR_DEVICEHUNG
    public void A_lost_device_is_known_by_its_code_on_the_exception_or_deep_inside_it(int code)
    {
        // Vortice (SharpGen) and plain COM failures carry the code as their HResult
        Assert.True(D3DViewportBase.IsDeviceLost(new COMException("lost", code)));
        Assert.True(D3DViewportBase.IsDeviceLost(new InvalidOperationException("outer", new InvalidOperationException("middle", new COMException("lost", code)))));

        // SlimDX carries it as its result code
        Assert.Equal(code, ResultCodeOf(SlimDXFailure(code)));
        Assert.True(D3DViewportBase.IsDeviceLost(SlimDXFailure(code)));
        Assert.True(D3DViewportBase.IsDeviceLost(new AggregateException(SlimDXFailure(code))));
    }

    [Fact]
    public void Any_other_failure_is_no_lost_device()
    {
        Assert.False(D3DViewportBase.IsDeviceLost(new COMException("out of memory", unchecked((int)0x8007000E))));
        Assert.False(D3DViewportBase.IsDeviceLost(new InvalidOperationException("a bug")));
        Assert.False(D3DViewportBase.IsDeviceLost(SlimDXFailure(unchecked((int)0x80070057))));
        Assert.False(D3DViewportBase.IsDeviceLost(new InvalidOperationException("outer", new IOException("disk"))));
    }

    // ----- Dds.Encode: the header, every mip level where its offset says, the colours swizzled to BGRA -----

    private static byte[] Gradient(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = ((byte)x, (byte)y, (byte)(x ^ y), (byte)(255 - x));
        }
        return rgba;
    }

    [Theory]
    [InlineData(2048, 2048, 12)]
    [InlineData(256, 64, 9)]
    [InlineData(1, 1, 1)]
    public void A_dds_has_its_whole_mip_chain_after_a_128_byte_header(int width, int height, int levels)
    {
        var rgba = new byte[width * height * 4];
        var dds = Dds.Encode(rgba, width, height);

        Assert.Equal(0x20534444u, BinaryPrimitives.ReadUInt32LittleEndian(dds)); // "DDS "
        Assert.Equal(124, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(4)));
        Assert.Equal(height, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(12)));
        Assert.Equal(width, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16)));
        Assert.Equal(width * 4, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(20)));
        Assert.Equal(levels, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(28)));

        // Each level half the one before (never under 1), one after another
        long total = 0;
        for (int l = 0, w = width, h = height; l < levels; l++, w = Math.Max(1, w / 2), h = Math.Max(1, h / 2)) total += w * h * 4;
        Assert.Equal(128 + total, dds.Length);

        // The header's size is what the texel reader reads before it hands the bytes on
        Assert.Equal((width, height), TexelReader.HeaderSize(dds));
    }

    [Fact]
    public void A_dds_is_written_b_g_r_a_and_each_mip_averages_the_four_below_it()
    {
        // 4×2: the second level is 2×1, each of its texels the average of a 2×2 block of the first
        var rgba = Gradient(4, 2);
        var dds = Dds.Encode(rgba, 4, 2);

        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(rgba[i * 4 + 2], dds[128 + i * 4]);
            Assert.Equal(rgba[i * 4 + 1], dds[128 + i * 4 + 1]);
            Assert.Equal(rgba[i * 4], dds[128 + i * 4 + 2]);
            Assert.Equal(rgba[i * 4 + 3], dds[128 + i * 4 + 3]);
        }

        var second = 128 + 4 * 2 * 4;
        for (var x = 0; x < 2; x++)
        for (var k = 0; k < 4; k++)
        {
            int At(int px, int py) => dds[128 + (py * 4 + px) * 4 + k];
            var expected = (At(x * 2, 0) + At(x * 2 + 1, 0) + At(x * 2, 1) + At(x * 2 + 1, 1) + 2) / 4;
            Assert.Equal(expected, dds[second + x * 4 + k]);
        }
    }

    // ----- Png.Encode: what WPF reads back is the picture, texel for texel -----

    [Theory]
    [InlineData(37, 11)]
    [InlineData(64, 64)]
    [InlineData(1, 1)]
    public void A_png_reads_back_as_the_picture_it_was_made_of(int width, int height)
    {
        var rgba = Gradient(width, height);
        var png = Png.Encode(rgba, width, height);

        Assert.Equal((width, height), TexelReader.HeaderSize(png));

        var frame = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(width, frame.PixelWidth);
        Assert.Equal(height, frame.PixelHeight);

        // Read back straight, no premultiplying: BGRA, as WPF has it
        var bgra = new byte[width * height * 4];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(bgra, width * 4, 0);
        for (var i = 0; i < width * height; i++)
        {
            Assert.Equal(rgba[i * 4], bgra[i * 4 + 2]);
            Assert.Equal(rgba[i * 4 + 1], bgra[i * 4 + 1]);
            Assert.Equal(rgba[i * 4 + 2], bgra[i * 4]);
            Assert.Equal(rgba[i * 4 + 3], bgra[i * 4 + 3]);
        }
    }

    // ----- Textures: the size a model claims, the size a header claims, the size read back -----

    [Fact]
    public void A_models_texture_size_is_read_only_when_the_file_has_that_much_left()
    {
        Assert.True(CarPaint.TextureFits(0, 0));
        Assert.True(CarPaint.TextureFits(1000, 1000));
        Assert.False(CarPaint.TextureFits(-1, 1000));
        Assert.False(CarPaint.TextureFits(1001, 1000));
        Assert.False(CarPaint.TextureFits(int.MaxValue, long.MaxValue));
        Assert.True(CarPaint.TextureFits(CarPaint.MostTextureBytes, long.MaxValue));
    }

    [Fact]
    public void A_header_says_its_size_and_anything_else_is_left_to_the_loader()
    {
        Assert.Null(TexelReader.HeaderSize([]));
        Assert.Null(TexelReader.HeaderSize("DDS "u8));
        Assert.Null(TexelReader.HeaderSize([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])); // a JPEG

        // A DDS saying 100000×8
        var dds = new byte[128];
        "DDS "u8.CopyTo(dds);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(12), 8);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(16), 100000);
        Assert.Equal((100000, 8), TexelReader.HeaderSize(dds));
    }

    [Theory]
    [InlineData(8192, 8192, null, 4096, 4096)]
    [InlineData(8192, 2048, null, 4096, 1024)]
    [InlineData(4096, 4096, null, 4096, 4096)]
    [InlineData(1024, 512, null, 1024, 512)]
    [InlineData(1024, 512, 256, 256, 128)]
    [InlineData(8192, 8192, 8192, 4096, 4096)]
    public void A_texture_is_read_back_at_most_4096_a_side_its_shape_kept(int width, int height, int? most, int w, int h)
    {
        Assert.Equal((w, h), TexelReader.ReadSize(width, height, most));
    }

    // ----- SoundCues: a cue's file is a kind of sound, and a loop's no bigger than a song -----

    private void Base(string sfxJson)
    {
        _temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/sfx/sfx.json", sfxJson);
    }

    [Fact]
    public void A_cue_naming_a_file_that_is_no_kind_of_sound_is_refused()
    {
        _temp.File($"{ModIds.BaseId}/sfx/tune.mid", "x");
        _temp.File($"{ModIds.BaseId}/sfx/tune.xm", "x");
        _temp.File($"{ModIds.BaseId}/sfx/click.ogg", "x");
        _temp.File($"{ModIds.BaseId}/sfx/clack.WAV", "x");
        Base("""
            { "cues": {
                "midi": { "bus": "interface", "files": ["tune.mid"] },
                "tracker": { "bus": "interface", "files": ["tune.xm", "click.ogg"] },
                "wave": { "bus": "interface", "files": ["clack.WAV"] } } }
            """);

        var cues = SoundCues.Load(ModSet.Load(_temp.Path));

        Assert.Null(cues.Get("midi"));
        Assert.Equal([_temp.Combine(ModIds.BaseId, "sfx", "click.ogg")], cues.Get("tracker")!.Files.Select(Path.GetFullPath));
        Assert.NotNull(cues.Get("wave"));
        Assert.Contains(cues.Problems, p => p.Contains("tune.mid") && p.Contains("no kind of sound"));
        Assert.Contains(cues.Problems, p => p.Contains("tune.xm") && p.Contains("no kind of sound"));
    }

    [Fact]
    public void A_loop_bigger_than_the_radios_biggest_song_is_refused()
    {
        // Made that size without being written: the cue only asks how big it is
        var big = _temp.Combine(ModIds.BaseId, "sfx", "air.ogg");
        Directory.CreateDirectory(Path.GetDirectoryName(big)!);
        using (var file = File.Create(big)) file.SetLength(SoundCues.LargestLoop + 1);
        _temp.File($"{ModIds.BaseId}/sfx/hum.ogg", "x");
        Base("""
            { "cues": {
                "ambience.big": { "bus": "ambience", "loop": true, "files": ["air.ogg"] },
                "ambience.small": { "bus": "ambience", "loop": true, "files": ["hum.ogg"] } } }
            """);

        var cues = SoundCues.Load(ModSet.Load(_temp.Path));

        Assert.Null(cues.Get("ambience.big"));
        Assert.NotNull(cues.Get("ambience.small"));
        Assert.Contains(cues.Problems, p => p.Contains("air.ogg") && p.Contains("too big"));
        Assert.Equal(AudioHub.LargestSong, SoundCues.LargestLoop);
    }

    // ----- SharedTextureBridge: the D3D9 adapter is the one with the DX11 adapter's LUID -----

    [Fact]
    public void The_d3d9_adapter_is_the_one_whose_luid_the_dx11_adapter_has_and_none_when_no_one_has()
    {
        Assert.Equal(1u, SharedTextureBridge.AdapterOf(0x0000_0001_0000_ABCDL, [0x1234L, 0x0000_0001_0000_ABCDL, 0x99L]));
        Assert.Equal(0u, SharedTextureBridge.AdapterOf(0x1234L, [0x1234L]));
        Assert.Null(SharedTextureBridge.AdapterOf(0x5555L, [0x1234L, 0x99L]));
        Assert.Null(SharedTextureBridge.AdapterOf(0x5555L, []));
    }

    [Fact]
    public void The_adapter_description_is_dxgi_hs_304_bytes_its_luid_at_296()
    {
        Assert.Equal(304, Unsafe.SizeOf<SharedTextureBridge.AdapterDescription>());
        Assert.Equal(256, Offset<SharedTextureBridge.AdapterDescription>("VendorId"));
        Assert.Equal(272, Offset<SharedTextureBridge.AdapterDescription>("DedicatedVideoMemory"));
        Assert.Equal(296, Offset<SharedTextureBridge.AdapterDescription>("LuidLowPart"));
        Assert.Equal(300, Offset<SharedTextureBridge.AdapterDescription>("LuidHighPart"));
    }
}
