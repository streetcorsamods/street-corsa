using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// A saved part tree brought together with the catalog, and the way back from each part to its saved self.
/// Saved parts the catalog no longer has (a pack was removed), and a second part on a slot that is taken, are left
/// out, with what is on them.
/// </summary>
public sealed class LiveTree
{
    public LiveTree(InstalledPart root, Dictionary<InstalledPart, PartInstance> saved)
    {
        Root = root;
        Saved = saved;
    }

    public InstalledPart Root { get; }

    public IReadOnlyDictionary<InstalledPart, PartInstance> Saved { get; }

    public PartTree AsPartTree() => new(Root, Array.Empty<PartDefinition>(), Array.Empty<string>());
}

/// <summary>
/// Goes between the two shapes of a part tree: <see cref="PartInstance"/> is what a save file holds,
/// <see cref="InstalledPart"/> is what the part scripts and the 3D view work on.
/// </summary>
public static class PartTrees
{
    /// <summary>
    /// Parts mounted one on another no deeper than this: a real engine is a dozen deep (block, head, intake, carb,
    /// cleaner). A save is anyone's file, and a stack overflow cannot be caught: what hangs deeper is left out, as a
    /// part the catalog does not know is.
    /// </summary>
    public const int MaxDepth = PartInstance.MaxDepth;

    public static LiveTree? ToInstalled(PartsCatalog catalog, PartInstance root)
    {
        if (catalog.Get(root.DefinitionId) is not { } rootDefinition) return null;

        var saved = new Dictionary<InstalledPart, PartInstance>();
        var installedRoot = Create(rootDefinition, root);
        AddChildren(installedRoot, root, 0);
        return new LiveTree(installedRoot, saved);

        InstalledPart Create(PartDefinition definition, PartInstance instance)
        {
            var installed = new InstalledPart(definition) { InstanceId = instance.InstanceId, Wear = instance.Wear, Tear = instance.Tear };
            foreach (var (field, value) in instance.Tuning ?? []) installed.Tuning[field] = value;
            saved[installed] = instance;
            return installed;
        }

        void AddChildren(InstalledPart parent, PartInstance instance, int depth)
        {
            if (depth >= MaxDepth) return;

            foreach (var child in instance.Children ?? [])
            {
                if (child == null || catalog.Get(child.DefinitionId) is not { } definition) continue;

                // A second part saved on a slot that is taken would push the first out of the tree while the save
                // still has both: it is left out here, as a part the catalog does not know is, and the save's
                // own check (SavedParts) seats it elsewhere or takes it off
                if (parent.Children.ContainsKey(child.ParentSlot)) continue;

                var installed = Create(definition, child);
                parent.Mount(child.ParentSlot, installed, child.OwnSlot);
                AddChildren(installed, child, depth + 1);
            }
        }
    }

    /// <param name="parentSlot">Where the root goes: a slot of the car, or 0 for a loose part</param>
    public static PartInstance FromInstalled(InstalledPart root, int parentSlot = 0)
    {
        var instance = Copy(root, 0);
        instance.ParentSlot = parentSlot;
        instance.OwnSlot = 0;
        return instance;

        static PartInstance Copy(InstalledPart part, int depth)
        {
            var copy = new PartInstance(part.Definition.Id)
            {
                Wear = part.Wear,
                Tear = part.Tear,
                ParentSlot = part.ParentSlot,
                OwnSlot = part.OwnSlot,
                Tuning = new Dictionary<string, double>(part.Tuning)
            };
            if (depth < MaxDepth)
            {
                foreach (var child in part.Children.Values) copy.Children.Add(Copy(child, depth + 1));
            }

            return copy;
        }
    }

    /// <summary>
    /// A copy that shares nothing with the original; the parts get ids of their own. What a save left null (no tuning,
    /// no parts on it) is empty in the copy; what hangs deeper than <see cref="MaxDepth"/> is left out.
    /// </summary>
    public static PartInstance Clone(PartInstance part, bool keepIds = false) => Clone(part, keepIds, 0);

    private static PartInstance Clone(PartInstance part, bool keepIds, int depth)
    {
        var copy = new PartInstance(part.DefinitionId)
        {
            Wear = part.Wear,
            Tear = part.Tear,
            ParentSlot = part.ParentSlot,
            OwnSlot = part.OwnSlot,
            Tuning = new Dictionary<string, double>(part.Tuning ?? [])
        };
        if (keepIds) copy.InstanceId = part.InstanceId;
        if (depth >= MaxDepth) return copy;

        foreach (var child in part.Children ?? [])
        {
            if (child != null) copy.Children.Add(Clone(child, keepIds, depth + 1));
        }

        return copy;
    }

    /// <summary>A part somebody owns in a few words: its condition and what comes with it, "85%, with 3 more parts"</summary>
    public static string Describe(PartInstance part)
    {
        var attached = part.SelfAndDescendants().Count() - 1;
        return attached switch
        {
            0 => PartWords.Say("parts.describe", ("wear", part.Wear * 100)),
            1 => PartWords.Say("parts.describe.with.one", ("wear", part.Wear * 100)),
            _ => PartWords.Say("parts.describe.with.other", ("wear", part.Wear * 100), ("count", attached))
        };
    }

    /// <summary>The part another part is mounted on; null for a root or a part that is not in the tree</summary>
    public static PartInstance? FindParent(PartInstance root, PartInstance part) =>
        root.SelfAndDescendants().FirstOrDefault(p => p.Children.Contains(part));
}
