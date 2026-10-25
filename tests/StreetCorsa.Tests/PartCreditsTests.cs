using StreetCorsa.Mods;
using StreetCorsa.Parts;

namespace StreetCorsa.Tests;

/// <summary>A part whose model is other people's names them (its file's credit), and the credits screen reads them off the loaded catalog</summary>
public sealed class PartCreditsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_credit_is_read_once_and_a_part_without_one_or_that_does_not_read_adds_nothing()
    {
        _temp.File("a/one.json", """{ "id": "a/one", "credit": " \"Thing\" by Someone, CC BY 4.0 " }""");
        _temp.File("b/two.json", """{ "id": "b/two", "credit": "\"Thing\" by Someone, CC BY 4.0" }""");
        _temp.File("c/three.json", """{ "id": "c/three" }""");
        _temp.File("d/four.json", "{ not json");
        _temp.File(EngineBuild.FileName, """[ { "id": "x", "credit": "\"Build\" by Nobody" } ]""");
        var mod = new ModInfo { Id = ModIds.BaseId, Name = "Street Corsa" };

        var credits = PartsCatalog.Load([(mod, _temp.Path), (mod, Path.Combine(_temp.Path, "nowhere"))]).Credits();

        Assert.Equal(["\"Thing\" by Someone, CC BY 4.0"], credits);
    }
}
