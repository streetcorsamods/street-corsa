using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// The game as the installer puts it down (the core alone) has nothing to tell the player when a game starts: the "Mod
/// Problems" dialog is for mods, never for the core missing what only a mod brings. A clean install showed 35 lines of
/// it (2026-10-22): the base game's rebuild jobs for the engine's kinds, with no engine part in.
/// </summary>
[Collection(GlobalsCollection.Name)]
public class ShippedContentTests
{
    /// <summary>The core alone, as the installer and a modder's download have it: no setting, no add-on</summary>
    private static ModSet BareCore(TempDir temp) => ModSet.Load(temp.Path, alsoFrom: [RepoPaths.BaseContent]);

    [Fact]
    public void The_shipped_core_reads_without_a_problem()
    {
        using var temp = new TempDir();
        var mods = BareCore(temp);
        Assert.Null(mods.Setting);
        var parts = mods.ContentOf(ContentTypes.Parts).ToList();
        var folders = parts.Select(p => p.Dir).ToList();

        var problems = new List<string>();
        problems.AddRange(mods.Problems.Select(p => $"Mods: {p}"));
        // The catalog knows the classes of the content it reads, as the game does once a game is in play
        var classesBefore = PartClasses.Current;
        try
        {
            PartClasses.Current = PartClasses.Load(folders);
            problems.AddRange(PartsCatalog.Load(parts, mods).Problems.Select(p => $"Parts catalog: {p}"));
        }
        finally
        {
            PartClasses.Current = classesBefore;
        }
        problems.AddRange(CarBook.Load(mods).Problems.Select(p => $"Cars: {p}"));
        problems.AddRange(PartGroups.Load(folders).Problems.Select(p => $"Part groups: {p}"));
        problems.AddRange(PartClasses.Load(folders).Problems.Select(p => $"Part classes: {p}"));
        problems.AddRange(Fuels.Load(folders).Problems.Select(p => $"Fuels: {p}"));
        var kit = new List<string>();
        Services.Street.StreetKit.Load(mods, kit);
        problems.AddRange(kit.Select(p => $"Street kit: {p}"));

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void A_job_for_a_kind_no_part_is_of_is_no_problem_when_the_work_order_has_the_kind()
    {
        using var temp = new TempDir();
        var mods = BareCore(temp);
        var catalog = PartsCatalog.Load(mods.ContentOf(ContentTypes.Parts).ToList(), mods);

        Assert.DoesNotContain(catalog.Parts.Values, p => catalog.WorkOrder.KindOf(p) == "cylinder_head");
        Assert.Contains(catalog.Rebuilds.All, j => j.Kinds.Contains("cylinder_head"));
        Assert.DoesNotContain(catalog.Problems, p => p.Contains("\"cylinder_head\""));
    }
}
