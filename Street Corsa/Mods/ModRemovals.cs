namespace StreetCorsa.Mods;

/// <summary>
/// The mods' removals of one kind of content (their mod.json's <c>removes</c>), applied in load order while that content
/// is read: <see cref="Before"/> each mod's entries, <see cref="Rest"/> at the end. So a mod takes out what the mods
/// before it have, and what it adds itself under the same name stays, as does what a later mod adds.
///
/// Plain BCL on purpose, like the rest of Mods.
/// </summary>
public sealed class ModRemovals
{
    private readonly ModSet _mods;
    private readonly string _type;
    private readonly Func<string, ModInfo, bool> _remove;
    private readonly string _what;
    private readonly List<string> _problems;
    private int _next;

    internal ModRemovals(ModSet mods, string type, Func<string, ModInfo, bool> remove, string what, List<string> problems)
    {
        _mods = mods;
        _type = type;
        _remove = remove;
        _what = what;
        _problems = problems;
    }

    /// <summary>The removals of every mod up to <paramref name="mod"/>, its own included: call before its entries are read</summary>
    public void Before(ModInfo mod)
    {
        var index = -1;
        for (var i = _next; i < _mods.Mods.Count; i++)
        {
            if (ReferenceEquals(_mods.Mods[i], mod)) index = i;
        }

        ApplyUpTo(index);
    }

    /// <summary>The removals of the mods not yet passed (those with no entries of this kind)</summary>
    public void Rest() => ApplyUpTo(_mods.Mods.Count - 1);

    private void ApplyUpTo(int last)
    {
        for (; _next <= last; _next++)
        {
            var by = _mods.Mods[_next];
            foreach (var id in ModSet.RemovesOf(by, _type))
            {
                if (!_remove(id, by)) _problems.Add($"{by.Id}/{ModSet.ManifestFileName}: removes {_what} {id}, which no mod before it has");
            }
        }
    }
}
