using System.IO;
using System.Text;
using StreetCorsa.Helpers;

namespace StreetCorsa.Parts.Export;

/// <summary>
/// The physics data of an Assetto Corsa car as it is on disk: data.acd, or a data folder when there is none.
/// The game itself reads data.acd over a folder beside it (seen with the setup probe, 2026-10-28), so that is the
/// order here too; a car racing on changed data has its acd moved aside, and reads as the folder it races on.
/// </summary>
public sealed class AcCarData
{
    public const string DataFolder = "data";

    private readonly Dictionary<string, byte[]>? _packed;

    private AcCarData(string? dataDirectory, Dictionary<string, byte[]>? packed)
    {
        DataDirectory = dataDirectory;
        _packed = packed;
    }

    /// <summary>The unpacked data folder the car runs on; null when it runs on data.acd</summary>
    public string? DataDirectory { get; }

    public static AcCarData Open(string carDirectory)
    {
        var folder = Path.Combine(carDirectory, DataFolder);
        var acd = Path.Combine(carDirectory, AcdFile.FileName);
        if (!File.Exists(acd) && Directory.Exists(folder)) return new AcCarData(folder, null);
        if (!File.Exists(acd)) throw new FileNotFoundException($"{carDirectory} has neither a data folder nor {AcdFile.FileName}", acd);

        var packed = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, content) in AcdFile.Read(acd)) packed[name] = content;
        return new AcCarData(null, packed);
    }

    /// <returns>Text of a data file, null when the car has no such file</returns>
    public string? ReadText(string name) => ReadBytes(name) is { } bytes ? Encoding.Latin1.GetString(bytes) : null;

    /// <returns>A data file, null when the car has no such file</returns>
    /// <remarks>
    /// The name is one file of the data folder or none: a name from the car's own files (the curve engine.ini names)
    /// that is rooted, climbs out or names a share is no file of the car, so nothing outside it is ever read or reached.
    /// </remarks>
    public byte[]? ReadBytes(string name)
    {
        if (!PathNames.IsSafeSegment(name)) return null;
        if (_packed != null) return _packed.GetValueOrDefault(name);

        var path = Path.Combine(DataDirectory!, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}

/// <summary>Reads the physics data of an Assetto Corsa car, whether it ships as a data folder or packed as data.acd</summary>
public static class AcCarDataReader
{
    /// <returns>Text of a data file by name, null when the car has no such file; the shape the export reads cars by</returns>
    public static Func<string, string?> ForCar(string carDirectory)
    {
        var data = AcCarData.Open(carDirectory);
        return data.ReadText;
    }

    /// <summary>
    /// One data file of a car, the same one <see cref="ForCar"/> would give, without decoding the rest of a
    /// data.acd: for readers after a single figure of many cars (the sound harvest's rev limiters)
    /// </summary>
    /// <returns>Text of the file, null when the car has no such file</returns>
    /// <exception cref="FileNotFoundException">The car has neither a data folder nor data.acd</exception>
    public static string? ReadFile(string carDirectory, string name)
    {
        if (!PathNames.IsSafeSegment(name)) return null;
        var folder = Path.Combine(carDirectory, AcCarData.DataFolder);
        var acd = Path.Combine(carDirectory, AcdFile.FileName);
        if (!File.Exists(acd) && Directory.Exists(folder))
        {
            var path = Path.Combine(folder, name);
            return File.Exists(path) ? Encoding.Latin1.GetString(File.ReadAllBytes(path)) : null;
        }

        if (!File.Exists(acd)) throw new FileNotFoundException($"{carDirectory} has neither a data folder nor {AcdFile.FileName}", acd);

        return AcdFile.ReadEntry(acd, name) is { } bytes ? Encoding.Latin1.GetString(bytes) : null;
    }

    /// <summary>A data folder on disk (the bench's way in); a name that is not one file of it is none</summary>
    public static Func<string, string?> ForFolder(string dataDirectory) =>
        name => PathNames.IsSafeSegment(name) && File.Exists(Path.Combine(dataDirectory, name))
            ? File.ReadAllText(Path.Combine(dataDirectory, name), Encoding.Latin1)
            : null;
}
