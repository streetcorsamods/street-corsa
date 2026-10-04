using System.IO;
using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Parts.Logic;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Parts;

/// <summary>
/// A part with its place in an assembly, relative to the assembly's root part.
/// <see cref="Source"/> is the installed part it stands for, when it stands for one.
/// </summary>
public sealed record PlacedPart(PartDefinition Part, Matrix4x4 World, InstalledPart? Source = null);

/// <summary>
/// An assembly merged into one model, with its bounds in the model's own space.
/// <see cref="Nodes"/> are the parts that made it into the model: the i-th is the node <see cref="PartAssembler.NodeName"/>(i).
/// A part without a model of its own that stands for an installed part is in it as a small box, so it can be clicked.
/// </summary>
public sealed record AssemblyModel(IKn5 Kn5, Vector3 Min, Vector3 Max, IReadOnlyList<PlacedPart> Nodes);

/// <summary>
/// Puts parts together by their slots and merges their models into a single KN5 for the renderer
/// </summary>
public static class PartAssembler
{
    private const int MaxDepth = 8;

    // Part models read this session, by path, while the file is as it was: the garage builds the same engine again
    // every time a part comes or goes. Emptied when it would hold more than this of model files, so a long session
    // through many engines keeps the ones it is on.
    private const long MaxCachedBytes = 256L * 1024 * 1024;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, (IKn5 Model, DateTime Written, long Length)> Models = new(StringComparer.OrdinalIgnoreCase);
    private static long _cachedBytes;

    /// <summary>Places the parts of a real assembly: every part goes where the slot it is mounted on puts it</summary>
    public static List<PlacedPart> Assemble(InstalledPart root, Matrix4x4 rootWorld)
    {
        var result = new List<PlacedPart>();
        Place(root, rootWorld, 0);
        return result;

        void Place(InstalledPart part, Matrix4x4 world, int depth)
        {
            result.Add(new PlacedPart(part.Definition, world, part));
            if (depth >= MaxDepth) return;

            foreach (var (slotId, child) in part.Children)
            {
                if (ChildWorld(part.Definition, slotId, child.Definition, child.OwnSlot, world) is { } childWorld)
                    Place(child, childWorld, depth + 1);
            }
        }
    }

    /// <inheritdoc cref="PartSlots.ChildWorld"/>
    public static Matrix4x4? ChildWorld(PartDefinition parent, int parentSlotId, PartDefinition child, int childSlotId, Matrix4x4 parentWorld) =>
        PartSlots.ChildWorld(parent, parentSlotId, child, childSlotId, parentWorld);

    /// <summary>Name of the node a part gets in a merged model</summary>
    public static string NodeName(int index) => $"part{index}";

    public static Matrix4x4 SlotMatrix(PartSlot slot) => PartSlots.SlotMatrix(slot);

    public static AssemblyModel BuildModel(PartsCatalog catalog, string name, IEnumerable<PlacedPart> parts)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = name;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var models = new Dictionary<string, IKn5?>(StringComparer.OrdinalIgnoreCase);
        var nodes = new List<PlacedPart>();
        var index = 0;

        foreach (var placed in parts)
        {
            var modelPath = catalog.GetModelPath(placed.Part);
            if (modelPath == null || !File.Exists(modelPath))
            {
                if (placed.Source != null) AddPlaceholder(placed);
                continue;
            }

            // A model that does not read is a part without one: a box for it, as for a part that has none
            if (!models.TryGetValue(modelPath, out var model)) models[modelPath] = model = ReadModel(modelPath);
            if (model == null)
            {
                if (placed.Source != null) AddPlaceholder(placed);
                continue;
            }

            // Two mods may each have a texture of the same name ("body.dds") and other content: the later one goes
            // in under a name of its own, and this part's materials are pointed at it. Equal content is one texture.
            var renamed = new Dictionary<string, string>();
            foreach (var (textureName, texture) in model.Textures)
            {
                if (!model.TexturesData.TryGetValue(textureName, out var data)) continue;

                var merged = TextureNameFor(kn5, textureName, data);
                if (merged != textureName) renamed[textureName] = merged;
                kn5.Textures[merged] = merged == textureName ? texture : new Kn5Texture { Name = merged, Active = texture.Active, Length = texture.Length };
                kn5.TexturesData[merged] = data;
            }

            // Materials are per placed part: the same model may appear more than once
            var materialIds = new Dictionary<uint, uint>();
            var sourceMaterialId = 0u;
            foreach (var material in model.Materials.Values)
            {
                var clone = (Kn5Material)material.Clone();
                clone.Name = $"{index}:{material.Name}";
                foreach (var mapping in clone.TextureMappings ?? [])
                {
                    if (mapping.Texture != null && renamed.TryGetValue(mapping.Texture, out var merged)) mapping.Texture = merged;
                }

                kn5.Materials[clone.Name] = clone;
                materialIds[sourceMaterialId++] = (uint)(kn5.Materials.Count - 1);
            }

            var partNode = Kn5Node.CreateBaseNode(NodeName(nodes.Count));
            partNode.Transform = ToMat4x4(placed.World);

            foreach (var mesh in model.RootNode.Children.Where(n => n.NodeClass == Kn5NodeClass.Mesh))
            {
                partNode.Children.Add(CloneMesh(mesh, materialIds.GetValueOrDefault(mesh.MaterialId)));

                foreach (var vertex in mesh.Vertices)
                {
                    var position = Vector3.Transform(new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z), placed.World);
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }
            }

            if (partNode.Children.Count > 0)
            {
                kn5.RootNode.Children.Add(partNode);
                nodes.Add(placed);
            }

            index++;
        }

        if (kn5.RootNode.Children.Count == 0)
            throw new InvalidOperationException($"No part of '{name}' has a model");

        return new AssemblyModel(kn5, min, max, nodes);

        void AddPlaceholder(PlacedPart placed)
        {
            var partNode = Kn5Node.CreateBaseNode(NodeName(nodes.Count));
            partNode.Transform = ToMat4x4(placed.World);
            partNode.Children.Add(PlaceholderMesh.Create(kn5, $"{partNode.Name}_box"));
            kn5.RootNode.Children.Add(partNode);
            nodes.Add(placed);

            var centre = placed.World.Translation;
            min = Vector3.Min(min, centre - new Vector3(PlaceholderMesh.HalfSize));
            max = Vector3.Max(max, centre + new Vector3(PlaceholderMesh.HalfSize));
        }
    }

    /// <summary>
    /// The same merged model moved by <paramref name="offset"/> (a translation): each part's node and its box, without
    /// reading or merging the models again
    /// </summary>
    public static AssemblyModel Moved(AssemblyModel model, Vector3 offset)
    {
        var move = Matrix4x4.CreateTranslation(offset);
        var nodes = model.Nodes.Select(p => p with { World = p.World * move }).ToList();
        var partNodes = model.Kn5.RootNode.Children;
        for (var i = 0; i < partNodes.Count && i < nodes.Count; i++) partNodes[i].Transform = ToMat4x4(nodes[i].World);
        return model with { Min = model.Min + offset, Max = model.Max + offset, Nodes = nodes };
    }

    /// <summary>
    /// The name a texture goes into a merged model under: its own, unless a texture of other content has it already
    /// (another mod's), then its own with a number, the extension kept
    /// </summary>
    private static string TextureNameFor(IKn5 kn5, string textureName, byte[] data)
    {
        for (var n = 0; ; n++)
        {
            var name = n == 0 ? textureName : $"{Path.GetFileNameWithoutExtension(textureName)}~{n}{Path.GetExtension(textureName)}";
            if (!kn5.TexturesData.TryGetValue(name, out var existing) || ReferenceEquals(existing, data) || existing.AsSpan().SequenceEqual(data))
                return name;
        }
    }

    /// <summary>
    /// A part's model, from the session's cache while its file is unchanged; null when it does not read (a corrupt
    /// or foreign file in a mod), as <see cref="PartModelBounds"/> takes it
    /// </summary>
    private static IKn5? ReadModel(string path)
    {
        DateTime written;
        long length;
        try
        {
            var file = new FileInfo(path);
            (written, length) = (file.LastWriteTimeUtc, file.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        lock (CacheLock)
        {
            if (Models.TryGetValue(path, out var cached) && cached.Written == written && cached.Length == length) return cached.Model;
        }

        IKn5 model;
        try
        {
            model = Kn5.FromFile(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }

        lock (CacheLock)
        {
            if (Models.Remove(path, out var old)) _cachedBytes -= old.Length;
            if (_cachedBytes + length > MaxCachedBytes)
            {
                Models.Clear();
                _cachedBytes = 0;
            }

            Models[path] = (model, written, length);
            _cachedBytes += length;
        }

        return model;
    }

    /// <summary>Forgets the part models read so far: the next assembly reads them from disk</summary>
    public static void ClearModelCache()
    {
        lock (CacheLock)
        {
            Models.Clear();
            _cachedBytes = 0;
        }
    }

    /// <summary>Geometry arrays are shared with the source, only the material binding differs</summary>
    private static Kn5Node CloneMesh(Kn5Node mesh, uint materialId) => new()
    {
        NodeClass = Kn5NodeClass.Mesh,
        Name = mesh.Name,
        Active = true,
        Children = new List<Kn5Node>(),
        CastShadows = mesh.CastShadows,
        IsVisible = mesh.IsVisible,
        IsTransparent = mesh.IsTransparent,
        IsRenderable = mesh.IsRenderable,
        Vertices = mesh.Vertices,
        Indices = mesh.Indices,
        MaterialId = materialId,
        Layer = mesh.Layer,
        LodIn = mesh.LodIn,
        LodOut = mesh.LodOut,
        BoundingSphereCenter = mesh.BoundingSphereCenter,
        BoundingSphereRadius = mesh.BoundingSphereRadius
    };

}
