using AcTools.Render.Base;
using AcTools.Render.Base.TargetTextures;
using AcTools.Render.Shaders;
using SlimDX.Direct3D11;
using SlimDX.DXGI;

namespace StreetCorsa.Looks;

/// <summary>
/// Any texture AcTools draws (DDS of any kind, PNG, JPEG) as its texels, RGBA, rows from the top: drawn once on a device
/// of its own and read straight back, colour and alpha together. (AcTools' own reader goes through a PNG, which loses
/// the colour where the texture is see-through, so it took two drawings and two PNG readings.) One user at a time.
/// </summary>
internal sealed class TexelReader : IDisposable
{
    private sealed class Drawing : BaseRenderer
    {
        protected override FeatureLevel FeatureLevel => FeatureLevel.Level_10_0;
        protected override void InitializeInner() { }
        protected override void ResizeInner() { }
        protected override void OnTickOverride(float dt) { }
    }

    private readonly Drawing _drawing = new();

    // The copy's target, kept with the drawing and only resized between textures: making one (and the effect's shader,
    // which the holder keeps) for every texture read cost more than the copy
    private readonly TargetResourceTexture _output = TargetResourceTexture.Create(Format.R8G8B8A8_UNorm);

    public TexelReader()
    {
        _drawing.Initialize();
    }

    /// <summary>
    /// The size a texture is drawn at: <paramref name="most"/> a side when given, its shape kept; never a side of nought
    /// (a 1×4096 strip at most 256 rounds to 0×256, which no target can be)
    /// </summary>
    public static (int Width, int Height) SizeOf(int width, int height, int? most)
    {
        if (most is not { } side) return (width, height);
        var scale = Math.Min((double)side / width, (double)side / height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>
    /// The texture's texels, made <paramref name="most"/> a side when given (its shape kept, as AcTools makes it); throws
    /// for bytes that are no texture
    /// </summary>
    public (int Width, int Height, byte[] Rgba) Read(byte[] bytes, int? most)
    {
        var holder = _drawing.DeviceContextHolder;
        var context = holder.DeviceContext;
        Viewport[]? viewports = null;
        try
        {
            var effect = holder.GetEffect<EffectPpBasic>();
            var output = _output;
            using var resource = ShaderResourceView.FromMemory(holder.Device, bytes);
            var loaded = ((Texture2D)resource.Resource).Description;

            var (width, height) = SizeOf(loaded.Width, loaded.Height, most);
            output.Resize(holder, width, height, null);
            context.ClearRenderTargetView(output.TargetView, BaseRenderer.ColorTransparent);
            context.OutputMerger.SetTargets(output.TargetView);
            viewports = context.Rasterizer.GetViewports();
            context.Rasterizer.SetViewports(new Viewport(0, 0, width, height, 0f, 1f));

            // Copied as it is, nothing blended: the colour stays where the alpha is nought
            context.OutputMerger.BlendState = null;
            holder.QuadBuffers.Prepare(context, effect.LayoutPT);
            effect.FxInputMap.SetResource(resource);
            holder.PrepareQuad(effect.LayoutPT);
            effect.TechCopy.DrawAllPasses(context, 6);

            return (width, height, ReadBack(holder, output.Texture, width, height));
        }
        finally
        {
            if (viewports != null) context.Rasterizer.SetViewports(viewports);
        }
    }

    /// <summary>A drawn texture's texels, through a copy the CPU can read</summary>
    private static byte[] ReadBack(DeviceContextHolder holder, Texture2D texture, int width, int height)
    {
        var description = texture.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CpuAccessFlags = CpuAccessFlags.Read;
        description.OptionFlags = ResourceOptionFlags.None;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.SampleDescription = new SampleDescription(1, 0);

        using var copy = new Texture2D(holder.Device, description);
        var context = holder.DeviceContext;
        context.CopyResource(texture, copy);

        var rgba = new byte[width * height * 4];
        var box = context.MapSubresource(copy, 0, MapMode.Read, SlimDX.Direct3D11.MapFlags.None);
        try
        {
            // A row of the copy may be longer than the texels in it
            for (var y = 0; y < height; y++)
            {
                box.Data.Position = (long)y * box.RowPitch;
                box.Data.ReadExactly(rgba, y * width * 4, width * 4);
            }
        }
        finally
        {
            context.UnmapSubresource(copy, 0);
        }

        return rgba;
    }

    public void Dispose()
    {
        _output.Dispose();
        _drawing.Dispose();
    }
}
