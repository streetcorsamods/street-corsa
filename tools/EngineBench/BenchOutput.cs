using StreetCorsa.Helpers;

namespace StreetCorsa;

/// <summary>
/// Where the bench may write a car's files (export, car, sound). It reads a car's data to make them, and never writes
/// over what it read or into an Assetto Corsa install: an output folder that is the car's own folder or inside it, or
/// inside any content\cars, is refused. The game's own overlay puts a race's files into AC with a backup; the bench
/// has none. Linked into the tests too.
/// </summary>
internal static class BenchOutput
{
    /// <summary>Why <paramref name="output"/> is no place for the files made from <paramref name="input"/>; null when it is one</summary>
    public static string? Refusal(string output, string input)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        var read = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        if (string.Equals(full, read, StringComparison.OrdinalIgnoreCase) || PathNames.IsUnder(read, full))
            return $"{output} is the folder the car was read from, or inside it: write the files somewhere else";

        for (var folder = new DirectoryInfo(full); folder?.Parent != null; folder = folder.Parent)
        {
            if (string.Equals(folder.Name, "cars", StringComparison.OrdinalIgnoreCase)
                && string.Equals(folder.Parent.Name, "content", StringComparison.OrdinalIgnoreCase))
                return $"{output} is inside an Assetto Corsa content\\cars folder: the bench never writes into AC, write the files somewhere else";
        }

        return null;
    }

    /// <summary>The files into <paramref name="output"/>, each whole or not at all (SafeFile), as the game writes AC's files</summary>
    public static void Write(string output, IEnumerable<(string Name, string Content)> files, System.Text.Encoding encoding)
    {
        foreach (var (name, content) in files) SafeFile.WriteAllText(Path.Combine(output, name), content, encoding);
    }
}
