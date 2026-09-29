using System.Collections.Concurrent;
using System.IO;
using StreetCorsa.Helpers;

namespace StreetCorsa.Parts.Scripting;

/// <summary>
/// Finds compiled script classes under a root folder laid out like the source game.
/// java.game.parts.engines.Mopar.X lives in parts\engines\Mopar\scripts\X.class, but the shared classes of
/// java.game.parts.enginepart.block live in parts\scripts\enginepart\block: the scripts folder may sit at any
/// level of the package path. Cars share the package java.game.cars while each keeps its classes in its own
/// folder, so the folder of the referring class is tried first.
/// A loaded class never changes, so one loader serves any number of VMs, on any thread.
/// </summary>
public sealed class ScriptClassLoader
{
    private const string ClassPrefix = "java.game.";
    private const string ScriptsFolder = "scripts";
    private const int MaxChain = 32;

    /// <summary>
    /// Classes mods extend that the game never shipped, made from the one their author copied: what to copy,
    /// what it extends instead, and the methods left out. Block_Inline_OHV (the Ford six's blocks) is
    /// Block_Vee_OHV with one cylinder head; its author's source sits among the SLRR notes, never compiled.
    /// </summary>
    private static readonly Dictionary<string, (string CopiedFrom, string BaseClass, string[] DroppedMethods)> StandIns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["java.game.parts.enginepart.block.block_inline.Block_Inline_OHV"] =
                ("java.game.parts.enginepart.block.block_vee.Block_Vee_OHV", "java.game.parts.enginepart.block.Block_Inline",
                    new[] { "getLeftCylinderHead", "getRightCylinderHead" })
        };

    // Class files by path, and classes by name as they were looked for (see Find): two caches, as a path and a class
    // name are two things. Neither is locked while a file is read and parsed: two threads asking for the same cold
    // class may both parse it, and the first to finish is the one both get, so a class is one object everywhere
    // (chains and statics go by the object).
    private readonly ConcurrentDictionary<string, ScriptClass?> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ScriptClass?> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _rootOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly string?[] _rootPaths;

    public ScriptClassLoader(string root) : this(new[] { root })
    {
    }

    /// <summary>A loader over several roots (each mod's scripts): a class is looked for in them in order, the first found wins</summary>
    public ScriptClassLoader(IReadOnlyList<string> roots)
    {
        Roots = roots.Count > 0 ? roots : new[] { string.Empty };
        _rootPaths = Roots.Select(r => string.IsNullOrEmpty(r) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(r))).ToArray();
    }

    /// <summary>The first root: the base game's scripts</summary>
    public string Root => Roots[0];

    public IReadOnlyList<string> Roots { get; }

    /// <summary>Every class file read so far</summary>
    public IEnumerable<string> LoadedFiles => _files.Keys.ToList();

    public ScriptClass? Load(string file)
    {
        if (_byFile.TryGetValue(file, out var cached)) return cached;

        var loaded = File.Exists(file) ? ScriptClass.Load(file) : null;
        var kept = _byFile.GetOrAdd(file, loaded);
        if (kept != null) _files.TryAdd(Path.GetFullPath(file), 0);
        return kept;
    }

    public ScriptClass? Find(string className, string? nearFolder = null)
    {
        // Class names come from the constant pools of the class files: every piece that becomes a folder or a file
        // name has to be one ("java.game.C:\x" is a rooted path, and Path.Combine would drop the scripts folder for it)
        var simpleName = className[(className.LastIndexOf('.') + 1)..];
        if (nearFolder != null && PathNames.IsSafeSegment(simpleName))
        {
            var sibling = Load(Path.Combine(nearFolder, simpleName + ".class"));
            if (sibling?.ClassName == className) return sibling;
        }

        // A class a mod's script names is the mod's own first, then the base game's, then the other mods': the
        // search, and so what is cached, depends on whose root the asking class is under
        var own = nearFolder == null ? 0 : Math.Max(0, RootOf(nearFolder));
        var key = own == 0 ? className : $"{own}|{className}";
        if (_byName.TryGetValue(key, out var cached)) return cached;

        ScriptClass? result = null;
        if (className.StartsWith(ClassPrefix, StringComparison.Ordinal)
            && className[ClassPrefix.Length..].Split('.') is var segments && segments.All(PathNames.IsSafeSegment))
        {
            var package = segments[..^1];
            var file = segments[^1] + ".class";

            foreach (var root in Roots.Skip(own).Take(1).Concat(Roots.Where((_, i) => i != own)))
            {
                for (var level = package.Length; level >= 0 && result == null; level--)
                {
                    result = Load(Path.Combine(new[] { root }
                        .Concat(package[..level]).Append(ScriptsFolder).Concat(package[level..]).Append(file).ToArray()));
                }

                if (result != null) break;
            }
        }

        if (result == null && StandIns.TryGetValue(className, out var standIn) && Find(standIn.CopiedFrom, nearFolder) is { } original)
            result = original.DerivedAs(className, standIn.BaseClass, standIn.DroppedMethods);

        return _byName.GetOrAdd(key, result);
    }

    /// <summary>The root <paramref name="folder"/> is under, by index; -1 when none. The longest wins: a root may sit inside another.</summary>
    private int RootOf(string folder) => _rootOf.GetOrAdd(folder, f =>
    {
        var full = Path.GetFullPath(f);
        int best = -1, bestLength = -1;
        for (var i = 0; i < _rootPaths.Length; i++)
        {
            if (_rootPaths[i] is not { } root) continue;

            var under = full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (under && root.Length > bestLength) (best, bestLength) = (i, root.Length);
        }

        return best;
    });

    /// <summary>The class and everything it extends, own class first</summary>
    public List<ScriptClass> Chain(ScriptClass type)
    {
        var chain = new List<ScriptClass> { type };
        for (var current = type; chain.Count < MaxChain;)
        {
            var parent = current.BaseClass == null ? null : Find(current.BaseClass, current.Folder);
            if (parent == null || chain.Contains(parent)) break;

            chain.Add(parent);
            current = parent;
        }

        return chain;
    }

    public List<ScriptClass>? Chain(string className, string? nearFolder = null) =>
        Find(className, nearFolder) is { } type ? Chain(type) : null;
}
