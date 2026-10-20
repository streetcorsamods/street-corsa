using StreetCorsa.Mods;
using StreetCorsa.Parts;

namespace StreetCorsa.Tests;

/// <summary>A pack whose models are other people's names them (pack.json's credit), and the credits screen reads them off the packs alone</summary>
public sealed class PackCreditsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_base_games_running_gear_names_its_models_makers()
    {
        var credits = PartsCatalog.PackCredits(ModSet.BaseOnly(RepoPaths.BaseContent).Folders(ContentTypes.Parts));

        Assert.Contains(credits, c => c.Contains("Anoopbg") && c.Contains("CC BY 4.0"));
        Assert.Contains(credits, c => c.Contains("themodel_finder"));
        Assert.Contains(credits, c => c.Contains("3dsl"));
        Assert.Contains(credits, c => c.Contains("VR DESIGNER"));
        Assert.Equal(credits.Count, credits.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_credit_is_read_once_and_a_pack_without_one_or_that_does_not_read_adds_nothing()
    {
        _temp.File("a/pack.json", """{ "id": "a", "credit": " \"Thing\" by Someone, CC BY 4.0 ", "parts": [] }""");
        _temp.File("b/pack.json", """{ "id": "b", "credit": "\"Thing\" by Someone, CC BY 4.0", "parts": [] }""");
        _temp.File("c/pack.json", """{ "id": "c", "parts": [] }""");
        _temp.File("d/pack.json", "{ not json");
        var mod = new ModInfo { Id = ModIds.BaseId, Name = "Street Corsa" };

        var credits = PartsCatalog.PackCredits([(mod, _temp.Path), (mod, Path.Combine(_temp.Path, "nowhere"))]);

        Assert.Equal(["\"Thing\" by Someone, CC BY 4.0"], credits);
    }
}
