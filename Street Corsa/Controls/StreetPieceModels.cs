using System.IO;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Logging;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Controls;

/// <summary>
/// The models of the street kit's pieces a scene is dressed with, kept from one scene to the next: the diner, the Cruise
/// screen's change of light and a seller's curb all stand the same pieces, which would otherwise be read and decoded
/// again each time (some 80 MB for Main Street). Only the last scene's are kept; a model whose file has changed since is
/// read again. A model's glow is put back as made when it is handed out again, as the scene before may have lit it
/// (<see cref="GarageRenderer.GlowDressing"/>).
/// </summary>
internal static class StreetPieceModels
{
    private sealed record Held(IKn5 Kn5, DateTime Written, IReadOnlyDictionary<uint, Vec3> Emissive);

    private static readonly object Gate = new();
    private static Dictionary<string, Held> _held = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The model of each piece of <paramref name="ids"/> the kit has, by the piece's id; one that cannot be read is logged
    /// and left out. Reads files: off the UI thread.
    /// </summary>
    public static Dictionary<string, IKn5> Read(IEnumerable<string> ids, StreetKit kit, IAppLogger logger)
    {
        lock (Gate)
        {
            var read = new Dictionary<string, IKn5>(StringComparer.OrdinalIgnoreCase);
            var kept = new Dictionary<string, Held>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
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

                read[id] = held.Kn5;
            }

            _held = kept;
            return read;
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
}
