using StreetCorsa.Configuration;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Services.Catalog;

namespace StreetCorsa.Tests;

/// <summary>
/// Cars put into AC, or another AC folder picked, are in the next game without a restart: the catalog is read again
/// before a game starts, and an import says whether its cars changed (ContentInPlay then builds the content anew)
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class AcContentLiveTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _acBefore = AppSettings.Instance.AssettoCorsaPath;

    public void Dispose()
    {
        AppSettings.Instance.AssettoCorsaPath = _acBefore;
        _temp.Dispose();
    }

    private string Ac(string name, params string[] cars)
    {
        _temp.File(Path.Combine(name, "content", "tracks", "keep.txt"), "");
        foreach (var car in cars)
            _temp.File(Path.Combine(name, "content", "cars", car, "ui", "ui_car.json"), $$"""{ "name": "{{car}}", "year": 1969 }""");
        return _temp.Combine(name);
    }

    [Fact]
    public async Task Another_folder_or_a_car_added_reads_as_changed_and_an_unchanged_one_does_not()
    {
        var bare = Ac("ac_sc", "kunos_car");
        var modded = Ac("ac_old", "kunos_car", "mod_car");
        var catalog = new MemoryCatalog();
        var import = new CarImportService(catalog);

        AppSettings.Instance.AssettoCorsaPath = bare;
        Assert.True((await import.ImportCarsAsync()).Changed);
        Assert.False((await import.ImportCarsAsync()).Changed);

        // The modded install: its mod car comes in
        AppSettings.Instance.AssettoCorsaPath = modded;
        var switched = await import.ImportCarsAsync();
        Assert.Equal(1, switched.Imported);
        Assert.True(switched.Changed);
        Assert.Equal(ContentStatus.Active, catalog.Cars["mod_car"].Status);

        // Back to the bare one: the mod car goes Legacy
        AppSettings.Instance.AssettoCorsaPath = bare;
        var back = await import.ImportCarsAsync();
        Assert.Equal(1, back.Retired);
        Assert.True(back.Changed);
        Assert.Equal(ContentStatus.Legacy, catalog.Cars["mod_car"].Status);

        // And to the modded one again: the same car, unchanged, is Active again, and that is a change too
        AppSettings.Instance.AssettoCorsaPath = modded;
        var again = await import.ImportCarsAsync();
        Assert.Equal(1, again.Returned);
        Assert.True(again.Changed);
        Assert.Equal(ContentStatus.Active, catalog.Cars["mod_car"].Status);

        // A car dropped into the folder while the game runs
        Ac("ac_old", "new_mod_car");
        Assert.True((await import.ImportCarsAsync()).Changed);
        Assert.False((await import.ImportCarsAsync()).Changed);
    }
}
