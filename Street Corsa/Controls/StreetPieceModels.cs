using System.IO;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Logging;
using StreetCorsa.Looks;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Controls;

/// <summary>
/// The models of the street kit's pieces a scene is dressed with, kept for the run: the diners, the Cruise screen's change
/// of light and a seller's curb stand pieces of one kit (each diner's layout some of its own), which would otherwise be
/// read and decoded again each time (some 80 MB for a whole street). What is kept is at most the kit, as its files are; a
/// model whose file has changed since is read again. A model's glow is put back as made when it is handed out again, as
/// the scene before may have lit it (<see cref="GarageRenderer.GlowDressing"/>). A piece painted where it stands
/// (<see cref="PlacedPiece.Paint"/>) is a copy of its model sharing its nodes and pictures, its painted materials' pictures
/// painted (<see cref="StreetPaint"/>; any texture the renderer draws, made a DDS with its mips as a car's paint is,
/// <see cref="CarPaint.Repainted"/>), each painted once a run and kept with the model.
/// </summary>
internal static class StreetPieceModels
{
    private sealed record Held(IKn5 Kn5, DateTime Written, IReadOnlyDictionary<uint, Vec3> Emissive)
    {
        // The model's pictures painted, by the name each is kept under (StreetPaint.NameOf); null for one that could not be
        public Dictionary<string, byte[]?> Painted { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Held> HeldModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The model of each piece of <paramref name="pieces"/> the kit has, by the piece's id and paint; one that cannot be
    /// read is logged and left out, one that cannot be painted is logged and stands as made. Reads files and paints
    /// pictures: off the UI thread.
    /// </summary>
    public static Dictionary<(string Piece, int Paint), IKn5> Read(IEnumerable<PlacedPiece> pieces, StreetKit kit, IAppLogger logger)
    {
        lock (Gate)
        {
            var read = new Dictionary<(string, int), IKn5>(PieceAndPaint.Instance);
            var unlit = new HashSet<Held>();
            foreach (var (id, paint) in pieces.Select(p => (p.Piece, p.Paint)).Distinct(PieceAndPaint.Instance))
            {
                if (kit.Find(id) is not { } piece) continue;
                Held held;
                try
                {
                    held = HeldOf(piece.Model);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    logger.Warning("The street piece {Piece} could not be read from {Model}: {Error}", id, piece.Model, ex.Message);
                    continue;
                }

                if (unlit.Add(held)) Unlit(held);
                if (piece.Paint is not { } how || paint < 1 || paint > how.Colours.Count)
                {
                    read[(id, paint)] = held.Kn5;
                    continue;
                }

                try
                {
                    read[(id, paint)] = Painted(held, how, how.Colours[paint - 1], id, logger);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A pattern that takes too long to match, a picture the reader chokes on: the piece as made
                    logger.Warning("The street piece {Piece} could not be painted: {Error}; as made", id, ex.Message);
                    read[(id, paint)] = held.Kn5;
                }
            }

            return read;
        }
    }

    // The model of a file, read once a run and again when the file has changed
    private static Held HeldOf(string model)
    {
        var written = File.GetLastWriteTimeUtc(model);
        if (HeldModels.TryGetValue(model, out var before) && before.Written == written) return before;
        var kn5 = Kn5.FromFile(model);
        return HeldModels[model] = new Held(kn5, written, EmissiveOf(kn5));
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
                        data = model.TexturesData.TryGetValue(mapping.Texture, out var picture)
                            ? CarPaint.Repainted(picture, rgba => StreetPaint.Tint(rgba, colour, paint.Amount))
                            : null;
                        if (data == null) logger.Warning("The street piece {Piece}'s picture {Picture} could not be painted; as made", id, mapping.Texture);
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
