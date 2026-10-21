using StreetCorsa.Mods;
using StreetCorsa.Parts;

namespace StreetCorsa.Tests;

/// <summary>A part whose model is other people's names them (its file's credit), and the credits screen reads them off the part files alone</summary>
public sealed class PartCreditsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_base_games_running_gear_names_its_models_makers()
    {
        var credits = PartsCatalog.PartCredits(ModSet.BaseOnly(RepoPaths.BaseContent).Folders(ContentTypes.Parts));

        Assert.Contains(credits, c => c.Contains("Anoopbg") && c.Contains("CC BY 4.0"));
        Assert.Contains(credits, c => c.Contains("themodel_finder"));
        Assert.Contains(credits, c => c.Contains("3dsl"));
        Assert.Contains(credits, c => c.Contains("VR DESIGNER"));
        Assert.Equal(credits.Count, credits.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_credit_is_read_once_and_a_part_without_one_or_that_does_not_read_adds_nothing()
    {
        _temp.File("a/one.json", """{ "id": "a/one", "credit": " \"Thing\" by Someone, CC BY 4.0 " }""");
        _temp.File("b/two.json", """{ "id": "b/two", "credit": "\"Thing\" by Someone, CC BY 4.0" }""");
        _temp.File("c/three.json", """{ "id": "c/three" }""");
        _temp.File("d/four.json", "{ not json");
        _temp.File(EngineBuild.FileName, """[ { "id": "x", "credit": "\"Build\" by Nobody" } ]""");
        var mod = new ModInfo { Id = ModIds.BaseId, Name = "Street Corsa" };

        var credits = PartsCatalog.PartCredits([(mod, _temp.Path), (mod, Path.Combine(_temp.Path, "nowhere"))]);

        Assert.Equal(["\"Thing\" by Someone, CC BY 4.0"], credits);
    }
}
