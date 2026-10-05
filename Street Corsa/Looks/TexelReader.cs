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

    public TexelReader()
    {
        _drawing.Initialize();
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
            using var effect = new EffectPpBasic();
            using var output = TargetResourceTexture.Create(Format.R8G8B8A8_UNorm);
            using var resource = ShaderResourceView.FromMemory(holder.Device, bytes);
            var loaded = ((Texture2D)resource.Resource).Description;

            var (width, height) = (loaded.Width, loaded.Height);
            if (most is { } side)
            {
                var scale = Math.Min((double)side / width, (double)side / height);
                (width, height) = ((int)Math.Round(width * scale), (int)Math.Round(height * scale));
            }

            effect.Initialize(holder.Device);
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

    public void Dispose() => _drawing.Dispose();
}
