using System.Globalization;
using StreetCorsa.Configuration;
using StreetCorsa.Helpers;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Services;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Content;

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
    private readonly CultureInfo _culture = Locale.Culture;

    public void Dispose()
    {
        AppSettings.Instance.AssettoCorsaPath = _acBefore;
        Locale.Culture = _culture;
        LoadingProgress.Clear();
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

    [Fact]
    public void The_install_stamp_holds_while_nothing_changes_and_moves_with_a_car_or_its_files()
    {
        var ac = Ac("ac_stamp", "kunos_car");
        var stamp = AcInstallStamp.Of(ac);
        Assert.NotNull(stamp);
        Assert.Equal(stamp, AcInstallStamp.Of(ac));

        // Its ui_car.json written again
        var ui = _temp.Combine("ac_stamp", "content", "cars", "kunos_car", "ui", "ui_car.json");
        File.SetLastWriteTimeUtc(ui, File.GetLastWriteTimeUtc(ui).AddMinutes(5));
        var edited = AcInstallStamp.Of(ac);
        Assert.NotEqual(stamp, edited);

        // A skin added
        Directory.CreateDirectory(_temp.Combine("ac_stamp", "content", "cars", "kunos_car", "skins", "red"));
        var skinned = AcInstallStamp.Of(ac);
        Assert.NotEqual(edited, skinned);

        // A car added, and a track
        Ac("ac_stamp", "mod_car");
        var more = AcInstallStamp.Of(ac);
        Assert.NotEqual(skinned, more);
        Directory.CreateDirectory(_temp.Combine("ac_stamp", "content", "tracks", "strip"));
        Assert.NotEqual(more, AcInstallStamp.Of(ac));

        // No install there: nothing to hold a look against
        Assert.Null(AcInstallStamp.Of(_temp.Combine("nowhere")));
        Assert.Null(AcInstallStamp.Of(null));
    }

    [Fact]
    public void The_install_stamp_holds_over_what_a_race_does_to_the_cars_and_moves_when_a_car_goes()
    {
        var ac = Ac("ac_race", "kunos_car", "mod_car");
        var stamp = AcInstallStamp.Of(ac);
        Assert.NotNull(stamp);

        // A race copy made beside the car, the car's folder written to (its data moved aside and back)
        _temp.File(Path.Combine("ac_race", "content", "cars", "kunos_car__sr_opponent", Parts.Export.AcCarFolder.CloneMarker), "{}");
        var folder = _temp.Combine("ac_race", "content", "cars", "kunos_car");
        Directory.SetLastWriteTimeUtc(folder, Directory.GetLastWriteTimeUtc(folder).AddMinutes(5));
        var cars = _temp.Combine("ac_race", "content", "cars");
        Directory.SetLastWriteTimeUtc(cars, Directory.GetLastWriteTimeUtc(cars).AddMinutes(5));
        Assert.Equal(stamp, AcInstallStamp.Of(ac));

        // A car taken out
        Directory.Delete(_temp.Combine("ac_race", "content", "cars", "mod_car"), true);
        Assert.NotEqual(stamp, AcInstallStamp.Of(ac));
    }

    [Fact]
    public void The_loading_bar_runs_only_while_a_step_is_under_way_and_counts_in_the_games_culture()
    {
        LoadingProgress.Clear();
        var shared = LoadingProgress.Shared;
        Assert.False(shared.IsBusy);
        Assert.False(shared.IsIndeterminate);

        LoadingProgress.Reporter.Report(LoadingStep.Named("loading.cars"));
        Assert.True(shared.IsBusy);
        Assert.True(shared.IsIndeterminate);

        Locale.Culture = CultureInfo.GetCultureInfo("en-US");
        LoadingProgress.Reporter.Report(LoadingStep.Counted(1234, 2000));
        Assert.False(shared.IsIndeterminate);
        Assert.Equal("1,234 / 2,000", shared.CountText);

        LoadingProgress.Clear();
        Assert.False(shared.IsBusy);
        Assert.False(shared.IsIndeterminate);
        Assert.Equal(string.Empty, shared.CountText);
    }
}
