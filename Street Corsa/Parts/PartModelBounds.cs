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
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                foreach (var mesh in model.RootNode.Children.Where(n => n.NodeClass == Kn5NodeClass.Mesh))
                {
                    foreach (var vertex in mesh.Vertices)
                    {
                        var p = new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                }

                return min.X <= max.X ? (min, max) : null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A model that can't be read is as good as none, and the file is not read again
                return null;
            }
        });
    }
}
