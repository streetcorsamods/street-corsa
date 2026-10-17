using System.Numerics;
using AcTools.Kn5File;
using StreetCorsa.Parts;

namespace StreetCorsa.Looks;

/// <summary>
/// A part as the years left it: every material of its model swapped for caked oil and road dirt with rust in it, matt
/// but for an oily sheen. Drawn over the clean part while a job of the parts view's "Work on it" runs, fading as the
/// work goes (<see cref="Controls.GarageRenderer.SetWork"/>), so the player sees what the money bought.
/// </summary>
internal static class PartGrime
{
    private const string Texture = "sc_grime.dds";
    private const int Size = 256;

    private static readonly Lazy<byte[]> Picture = new(Paint);

    /// <summary>How much grime a part in this condition carries, 0 clean to <see cref="MostGrime"/></summary>
    public static double Of(double wear) => MostGrime * Math.Clamp(1 - (double.IsFinite(wear) ? wear : 1), 0, 1);

    /// <summary>A worn-out part is not black all over: a little of its own colour shows through</summary>
    public const double MostGrime = 0.85;

    /// <summary>How far out from the part its grime is drawn, metres: over the part's own surface whatever the depth buffer makes of two at one place</summary>
    private const float Skin = 0.0015f;

    /// <summary>
    /// The grime's model of the parts: a skin's width bigger round the middle of them all, so it is drawn over the clean
    /// part rather than fighting it for the same depth. Built once, its parts under a node that grows them.
    /// </summary>
    public static AssemblyModel Model(PartsCatalog catalog, IReadOnlyList<PlacedPart> parts)
    {
        var model = PartAssembler.BuildModel(catalog, "work_grime", parts);
        Dirty(model.Kn5);

        var size = model.Max - model.Min;
        var largest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (!(largest > 0.01f)) return model;

        var centre = (model.Min + model.Max) / 2f;
        var scale = 1f + 2f * Skin / largest;
        var g = Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(centre);
        var grown = Kn5Node.CreateBaseNode("work_grime_skin");
        grown.Transform = new AcTools.Numerics.Mat4x4(g.M11, g.M12, g.M13, g.M14, g.M21, g.M22, g.M23, g.M24, g.M31, g.M32, g.M33, g.M34, g.M41, g.M42, g.M43, g.M44);
        grown.Children.AddRange(model.Kn5.RootNode.Children);
        model.Kn5.RootNode.Children.Clear();
        model.Kn5.RootNode.Children.Add(grown);
        return model;
    }

    /// <summary>
    /// Makes every material of the model grime, in place: give it a model of its own, built for this. Its own textures
    /// go, nothing samples them any more.
    /// </summary>
    public static void Dirty(IKn5 kn5)
    {
        var picture = Picture.Value;
        kn5.Textures.Clear();
        kn5.TexturesData.Clear();
        kn5.Textures[Texture] = new Kn5Texture { Name = Texture, Active = true, Length = picture.Length };
        kn5.TexturesData[Texture] = picture;

        foreach (var material in kn5.Materials.Values)
        {
            material.ShaderName = "ksPerPixel";
            material.BlendMode = Kn5MaterialBlendMode.Opaque;
            material.ShaderProperties =
            [
                new Kn5Material.ShaderProperty { Name = "ksAmbient", ValueA = 0.35f },
                new Kn5Material.ShaderProperty { Name = "ksDiffuse", ValueA = 0.6f },
                new Kn5Material.ShaderProperty { Name = "ksSpecular", ValueA = 0.12f },
                new Kn5Material.ShaderProperty { Name = "ksSpecularEXP", ValueA = 18f },
                new Kn5Material.ShaderProperty { Name = "ksEmissive", ValueC = new AcTools.Numerics.Vec3(0, 0, 0) },
                new Kn5Material.ShaderProperty { Name = "ksAlphaRef", ValueA = 0 }
            ];
            material.TextureMappings = [new Kn5Material.TextureMapping { Name = "txDiffuse", Texture = Texture, Slot = 0 }];
        }
    }

    /// <summary>Black oil and brown dirt in blotches, rust coming through in spots, tiling</summary>
    private static byte[] Paint()
    {
        var rgba = new byte[Size * Size * 4];
        var blotches = new Noise(1958);
        var specks = new Noise(4471);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                // Round a torus, so the picture tiles however a part's UVs run over it
                var u = x / (float)Size * MathF.Tau;
                var v = y / (float)Size * MathF.Tau;
                var p = new Vector3(MathF.Cos(u) * 1.6f, MathF.Sin(u) * 1.6f, MathF.Cos(v) * 1.6f + MathF.Sin(v) * 2.3f);
                var oil = Math.Clamp(blotches.Fbm(p * 0.7f) * 0.7f + 0.55f, 0, 1);
                var rust = Math.Clamp((specks.Fbm(p * 3.1f, 3) - 0.45f) * 2f, 0, 0.5f);

                // Road dirt to oil black, mostly black; a little rust coming through
                var r = Lerp(Lerp(0x4A, 0x17, oil), 0x6A, rust);
                var g = Lerp(Lerp(0x40, 0x15, oil), 0x36, rust);
                var b = Lerp(Lerp(0x33, 0x12, oil), 0x1C, rust);
                var i = (y * Size + x) * 4;
                rgba[i] = (byte)r;
                rgba[i + 1] = (byte)g;
                rgba[i + 2] = (byte)b;
                rgba[i + 3] = 255;
            }
        }

        return Dds.Encode(rgba, Size, Size);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
