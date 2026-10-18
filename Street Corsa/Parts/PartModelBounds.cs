using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using AcTools.Kn5File;

namespace StreetCorsa.Parts;

/// <summary>The box a part's model fills, in the part's own space</summary>
public static class PartModelBounds
{
    // By model file: read once, the layouts ask on every build
    private static readonly ConcurrentDictionary<string, (Vector3 Min, Vector3 Max)?> Bounds = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>Null when the part has no model, or it can't be read</returns>
    public static (Vector3 Min, Vector3 Max)? Of(PartsCatalog catalog, PartDefinition part)
    {
        var path = catalog.GetModelPath(part);
        if (path == null) return null;

        return Bounds.GetOrAdd(path, file =>
        {
            try
            {
                if (!File.Exists(file)) return null;

                // Only the vertices are wanted: textures and materials are skipped
                var model = Kn5.FromFile(file, SkippingTextureLoader.Instance, SkippingMaterialLoader.Instance);
                return Kn5Bounds.Of(model.RootNode.Children.Where(n => n.NodeClass == Kn5NodeClass.Mesh).SelectMany(n => n.Vertices));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A model that can't be read is as good as none, and the file is not read again
                return null;
            }
        });
    }
}
