namespace StreetCorsa.Parts.Logic;

/// <summary>
/// A part as it sits in an assembly: what it is, what shape it is in, how it is tuned,
/// what it is mounted on and what is mounted on it.
/// </summary>
public sealed class InstalledPart
{
    public InstalledPart(PartDefinition definition)
    {
        Definition = definition;
    }

    public PartDefinition Definition { get; }

    /// <summary>
    /// Which physical part this is, when it stands for one somebody owns. A tree is made anew after every
    /// change; this is how the same part is known again in the next one.
    /// </summary>
    public Guid InstanceId { get; init; }

    /// <summary>1 = new, 0 = worn out (mileage)</summary>
    public double Wear { get; set; } = 1.0;

    /// <summary>1 = straight, 0 = wrecked (damage)</summary>
    public double Tear { get; set; } = 1.0;

    /// <summary>
    /// Settings changed from the part's defaults, by spec: idle_rpm, rev_limit, mixture_ratio, advance,
    /// final_drive... An element of a list takes an index: "gears[1]".
    /// </summary>
    public Dictionary<string, double> Tuning { get; } = new();

    /// <summary>What a machine shop has cut on the part, by the cut: the sizes of it gone (<c>PartInstance.Cuts</c>)</summary>
    public Dictionary<string, int> Cuts { get; } = new();

    /// <summary>What the cuts do to the engine's figures (a bore over, a head milled), after the part's own; null for none</summary>
    public PartFigureSet? CutFigures { get; set; }

    public InstalledPart? Parent { get; private set; }

    /// <summary>Slot of the parent this part sits on; 0 for the root</summary>
    public int ParentSlot { get; private set; }

    /// <summary>The part's own slot that mates with the parent's; 0 for the root</summary>
    public int OwnSlot { get; private set; }

    /// <summary>Mounted parts by the slot of this part they sit on</summary>
    public Dictionary<int, InstalledPart> Children { get; } = new();

    public void Mount(int slot, InstalledPart child, int childSlot)
    {
        Children[slot] = child;
        child.Parent = this;
        child.ParentSlot = slot;
        child.OwnSlot = childSlot;
    }

    public IEnumerable<InstalledPart> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children.Values)
        {
            foreach (var part in child.SelfAndDescendants()) yield return part;
        }
    }

    /// <summary>True when the part is of the class or one under it (<see cref="PartClasses"/>): "engine_block", "brake"</summary>
    public bool Is(string className) => PartKinds.Is(Definition, className);

    public override string ToString() => Definition.DisplayName ?? Definition.Id;
}
