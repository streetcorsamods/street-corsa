using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Controls;

/// <summary>
/// The models of the street kit's pieces a scene is dressed with, kept from one scene to the next: the diner, the Cruise
/// screen's change of light and a seller's curb all stand the same pieces, which would otherwise be read and decoded
/// again each time (some 80 MB for Main Street). Only the last scene's are kept; a model whose file has changed since is
/// read again. A model's glow is put back as made when it is handed out again, as the scene before may have lit it
/// (<see cref="GarageRenderer.GlowDressing"/>). A piece painted where it stands (<see cref="PlacedPiece.Paint"/>) is a
/// copy of its model sharing its nodes and pictures, its painted materials' pictures painted (<see cref="StreetPaint"/>),
/// each painted once and kept with the model: the renderer loads the pictures of a model and its copies once
/// (<see cref="GarageRenderer.SetDressing"/>).
/// </summary>
internal static class StreetPieceModels
{
    private sealed record Held(IKn5 Kn5, DateTime Written, IReadOnlyDictionary<uint, Vec3> Emissive)
    {
        // The model's pictures painted, by the name each is kept under (StreetPaint.NameOf); null for one that could not be
        public Dictionary<string, byte[]?> Painted { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly object Gate = new();
    private static Dictionary<string, Held> _held = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The model of each piece of <paramref name="pieces"/> the kit has, by the piece's id and paint; one that cannot be
    /// read is logged and left out. Reads files and paints pictures: off the UI thread.
    /// </summary>
    public static Dictionary<(string Piece, int Paint), IKn5> Read(IEnumerable<PlacedPiece> pieces, StreetKit kit, IAppLogger logger)
    {
        lock (Gate)
        {
            var read = new Dictionary<(string, int), IKn5>(PieceAndPaint.Instance);
            var kept = new Dictionary<string, Held>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, paint) in pieces.Select(p => (p.Piece, p.Paint)).Distinct(PieceAndPaint.Instance))
            {
                if (kit.Find(id) is not { } piece) continue;
                if (!kept.TryGetValue(piece.Model, out var held))
                {
                    try
                    {
                        var written = File.GetLastWriteTimeUtc(piece.Model);
                        if (_held.TryGetValue(piece.Model, out var before) && before.Written == written)
                        {
                            Unlit(before);
                            held = before;
                        }
                        else
                        {
                            var kn5 = Kn5.FromFile(piece.Model);
                            held = new Held(kn5, written, EmissiveOf(kn5));
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        logger.Warning("The street piece {Piece} could not be read from {Model}: {Error}", id, piece.Model, ex.Message);
                        continue;
                    }

                    kept[piece.Model] = held;
                }

                read[(id, paint)] = piece.Paint is { } how && paint >= 1 && paint <= how.Colours.Count
                    ? Painted(held, how, how.Colours[paint - 1], id, logger)
                    : held.Kn5;
            }

            _held = kept;
            return read;
        }
    }

    /// <summary>
    /// A copy of the model with its materials the paint takes wearing their pictures painted: its nodes and pictures the
    /// model's own (never changed: another scene's renderer may still be loading them), its materials its own
    /// </summary>
    private static IKn5 Painted(Held held, PiecePaint paint, byte[] colour, string id, IAppLogger logger)
    {
        var model = held.Kn5;
        var copy = Kn5.CreateEmpty();
        copy.RootNode = model.RootNode;
        copy.Textures = new Dictionary<string, Kn5Texture>(model.Textures);
        copy.TexturesData = new Dictionary<string, byte[]>(model.TexturesData);
        copy.Materials = new Dictionary<string, Kn5Material>();
        foreach (var (name, material) in model.Materials)
        {
            var own = material.Clone();
            if (paint.Takes(name))
            {
                foreach (var mapping in own.TextureMappings.Where(m => m.Name == "txDiffuse"))
                {
                    var painted = StreetPaint.NameOf(mapping.Texture, colour);
                    if (!held.Painted.TryGetValue(painted, out var data))
                    {
                        data = model.TexturesData.TryGetValue(mapping.Texture, out var picture) ? PaintPicture(picture, mapping.Texture, colour, paint.Amount, id, logger) : null;
                        held.Painted[painted] = data;
                    }

                    if (data == null) continue;
                    copy.TexturesData[painted] = data;
                    copy.Textures[painted] = new Kn5Texture { Name = painted, Active = true, Length = data.Length };
                    mapping.Texture = painted;
                }
            }

            copy.Materials[name] = own;
        }

        return copy;
    }

    /// <summary>A picture painted (<see cref="StreetPaint.Tint"/>), as a JPEG when it was one, else a PNG; null when it is no picture WPF reads (a DDS)</summary>
    private static byte[]? PaintPicture(byte[] picture, string name, byte[] colour, float amount, string id, IAppLogger logger)
    {
        try
        {
            using var input = new MemoryStream(picture);
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            var source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var stride = source.PixelWidth * 4;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);
            StreetPaint.Tint(pixels, colour, amount);

            var painted = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            BitmapEncoder encoder = name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? new JpegBitmapEncoder { QualityLevel = 90 }
                : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(painted));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            logger.Warning("The street piece {Piece}'s picture {Picture} could not be painted: {Error}; as made", id, name, ex.Message);
            return null;
        }
    }

    private static IReadOnlyDictionary<uint, Vec3> EmissiveOf(IKn5 kn5)
    {
        var emissive = new Dictionary<uint, Vec3>();
        for (var id = 0u; id < kn5.Materials.Count; id++)
            if (kn5.GetMaterial(id)?.GetPropertyByName("ksEmissive") is { } property) emissive[id] = property.ValueC;
        return emissive;
    }

    private static void Unlit(Held held)
    {
        foreach (var (id, value) in held.Emissive)
            if (held.Kn5.GetMaterial(id)?.GetPropertyByName("ksEmissive") is { } property) property.ValueC = value;
    }

    // A piece's id in any case, and its paint
    private sealed class PieceAndPaint : IEqualityComparer<(string Piece, int Paint)>
    {
        public static readonly PieceAndPaint Instance = new();

        public bool Equals((string Piece, int Paint) a, (string Piece, int Paint) b) =>
            a.Paint == b.Paint && string.Equals(a.Piece, b.Piece, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Piece, int Paint) key) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Piece), key.Paint);
    }
}
