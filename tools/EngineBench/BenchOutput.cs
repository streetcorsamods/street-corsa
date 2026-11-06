using StreetCorsa.Helpers;

namespace StreetCorsa;

/// <summary>
/// Where the bench may write a car's files (export, car, sound). It reads a car's data to make them, and never writes
/// over what it read or into an Assetto Corsa install: an output folder that is the car's own folder or inside it,
/// inside any content\cars, or at or under a folder that holds AC (its content\cars and content\tracks, the game's
/// test of an install) is refused, each also where a junction or a link on the way leads. The game's own overlay puts
/// a race's files into AC with a backup; the bench has none. Linked into the tests too.
/// </summary>
internal static class BenchOutput
{
    /// <summary>Why <paramref name="output"/> is no place for the files made from <paramref name="input"/>; null when it is one</summary>
    public static string? Refusal(string output, string input)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        var read = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        foreach (var target in new[] { full, Resolved(full) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var source in new[] { read, Resolved(read) }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase) || PathNames.IsUnder(source, target))
                    return $"{output} is the folder the car was read from, or inside it: write the files somewhere else";
            }

            for (var folder = new DirectoryInfo(target); folder != null; folder = folder.Parent)
            {
                if (folder.Parent != null && string.Equals(folder.Name, "cars", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(folder.Parent.Name, "content", StringComparison.OrdinalIgnoreCase))
                    return $"{output} is inside an Assetto Corsa content\\cars folder: the bench never writes into AC, write the files somewhere else";
                if (HoldsAc(folder.FullName))
                    return $"{output} is in an Assetto Corsa install ({folder.FullName}): the bench never writes into AC, write the files somewhere else";
            }
        }

        return null;
    }

    /// <summary>The files into <paramref name="output"/>, each whole or not at all (SafeFile), as the game writes AC's files</summary>
    public static void Write(string output, IEnumerable<(string Name, string Content)> files, System.Text.Encoding encoding)
    {
        foreach (var (name, content) in files) SafeFile.WriteAllText(Path.Combine(output, name), content, encoding);
    }

    /// <summary>
    /// A folder that holds Assetto Corsa: its content\cars and content\tracks, the rule of the game's
    /// AppSettings.IsInstall (the bench does not link the game's settings)
    /// </summary>
    internal static bool HoldsAc(string folder)
    {
        try
        {
            return Directory.Exists(Path.Combine(folder, "content", "cars")) && Directory.Exists(Path.Combine(folder, "content", "tracks"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The path with each folder on the way that is a junction or a link replaced by where it leads, folder by folder
    /// from the root; what does not exist yet is kept as written. The path itself when it cannot be followed.
    /// </summary>
    internal static string Resolved(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return path;
            var current = root;
            var parts = path[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var next = Path.Combine(current, parts[i]);
                var info = new DirectoryInfo(next);
                if (!info.Exists) return Path.TrimEndingDirectorySeparator(Path.Combine([next, .. parts[(i + 1)..]]));
                // A link to a link is followed to its end; a chain that loops or is too long throws and is left alone
                current = info.LinkTarget != null && info.ResolveLinkTarget(returnFinalTarget: true) is { } leads
                    ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(leads.FullName))
                    : next;
            }

            return Path.TrimEndingDirectorySeparator(current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }
}
