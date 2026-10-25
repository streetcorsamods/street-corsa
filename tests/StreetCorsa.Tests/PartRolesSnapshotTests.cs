using System.Globalization;
using System.Text;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>
/// What the game reads out of every part's name, held in Fixtures/part_roles.txt for the core's parts and the tests' own: its kind in the work order,
/// its role in the cooling (radiator, water pump, fan, and whether it is a performance one), how long an oil pan holds
/// its oil, and a block's make family. A part's name is free text the player reads, so renaming parts can change what
/// they do without any other test noticing (a timing cover named "with water pump" became a water pump).
///
/// A change here is either a rename that must be put back or a new part: after checking every changed line, write the
/// file again with STREETCORSA_UPDATE_SNAPSHOTS=1.
/// </summary>
public sealed class PartRolesSnapshotTests
{
    private const string SnapshotFile = "part_roles.txt";

    private static string SnapshotPath => Path.Combine(RepoPaths.Root, "tests", "StreetCorsa.Tests", "Fixtures", SnapshotFile);

    [Fact]
    public void What_the_game_reads_in_part_names_does_not_change_unseen()
    {
        var actual = Roles(PartsCatalog.Load(RepoPaths.PartsRoots));

        if (Environment.GetEnvironmentVariable("STREETCORSA_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(SnapshotPath, actual);
            return;
        }

        var expected = File.ReadAllText(SnapshotPath).ReplaceLineEndings("\n");
        var was = Lines(expected);
        var now = Lines(actual);
        var changed = was.Keys.Union(now.Keys)
            .Where(id => was.GetValueOrDefault(id) != now.GetValueOrDefault(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => $"{id}: {was.GetValueOrDefault(id) ?? "(none)"} -> {now.GetValueOrDefault(id) ?? "(none)"}")
            .ToList();

        Assert.True(changed.Count == 0, $"{changed.Count} part(s) read differently from their names:\n" + string.Join("\n", changed));
    }

    /// <summary>One line per part the game reads anything into: id, then what it reads</summary>
    private static string Roles(PartsCatalog catalog)
    {
        var text = new StringBuilder();
        foreach (var part in catalog.Parts.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var roles = new List<string>();

            if (catalog.WorkOrder.KindOf(part) is { } kind) roles.Add($"work={kind}");

            switch (EngineCooling.KindOf(part))
            {
                case EngineCooling.Kind.Radiator:
                    roles.Add("cooling=radiator");
                    if (EngineCooling.IsPerformanceRadiator(part)) roles.Add("performance");
                    if (EngineCooling.HasElectricFan(part)) roles.Add("electric_fan");
                    break;
                case EngineCooling.Kind.WaterPump:
                    roles.Add("cooling=water_pump");
                    if (EngineCooling.IsPerformancePump(part)) roles.Add("performance");
                    break;
                case EngineCooling.Kind.Fan:
                    roles.Add("cooling=fan");
                    if (EngineCooling.IsPerformanceFan(part)) roles.Add("performance");
                    break;
            }

            if (PartKinds.GroupOf(part) == EngineCooling.OilPansGroup)
                roles.Add("sump_g=" + EngineCooling.SumpG(part).ToString(CultureInfo.InvariantCulture));

            if (PartKinds.IsBlock(part)) roles.Add("family=" + MakeFamilies.FamilyOf(part.DisplayName ?? part.Name));

            if (roles.Count > 0) text.Append(part.Id).Append(' ').AppendJoin(' ', roles).Append('\n');
        }

        return text.ToString();
    }

    private static Dictionary<string, string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "", StringComparer.Ordinal);
}
