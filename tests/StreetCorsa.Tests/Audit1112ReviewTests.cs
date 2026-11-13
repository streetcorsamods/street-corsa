using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Screens.RaceLoading;
using StreetCorsa.Services;
using StreetCorsa.Services.Simulation;

namespace StreetCorsa.Tests;

/// <summary>
/// The review of the 2026-11-12 audit's branch: a simulated crash's cracked windscreen kept on the real car, an earlier
/// race's crash tow paid for its own car, and what a delete under _replaced leaves when its folder cannot be renamed
/// </summary>
public sealed class Audit1112ReviewTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Content => _temp.Combine("Content");

    private static readonly Func<Car, bool> Runs = _ => true;

    // ---- a rival's race wear ----

    [Fact]
    public void A_simulated_race_takes_the_windscreen_the_copy_cracked()
    {
        var car = new Car("car");
        var worn = car.Snapshot();
        worn.GlassCracks = 0.6;

        RaceSimulatorService.TakeWear(car, worn);

        Assert.Equal(0.6, car.GlassCracks);
    }

    // ---- an earlier race's tow ----

    private static PlayerMessage EarlierCrash(Car car) =>
        new PlayerMessage("Crash", "Towed home") { TowedToGarage = true } with { EarlierTowOf = car.InstanceId };

    [Fact]
    public void An_earlier_races_crash_is_towed_and_paid_for_its_own_car_not_this_ones()
    {
        var earlier = new Car();
        var mine = new Car();
        var messages = AssettoCorsaLauncher.AsEarlier([EarlierCrash(earlier), new PlayerMessage("Prize", "A camshaft")]);

        var tows = RaceLoadingScreenViewModel.TowsAfterRace([earlier, mine], mine.InstanceId, null, messages, Runs);

        Assert.Equal(-1, tows.Crash);
        var (index, towed) = Assert.Single(tows.Earlier);
        Assert.Equal(0, index);
        Assert.Same(earlier, towed);
    }

    [Fact]
    public void An_earlier_races_tow_is_paid_when_this_race_never_ran()
    {
        var earlier = new Car();
        var messages = AssettoCorsaLauncher.AsEarlier([EarlierCrash(earlier)]);

        var tows = RaceLoadingScreenViewModel.TowsAfterRace([earlier], raced: null, null, messages, Runs);

        Assert.Same(earlier, Assert.Single(tows.Earlier).Car);
    }

    [Fact]
    public void An_earlier_races_car_the_police_took_or_sold_since_is_not_towed()
    {
        var impounded = new Car { ImpoundedUntil = new DateTime(1968, 6, 1) };
        var sold = new Car();
        var messages = AssettoCorsaLauncher.AsEarlier([EarlierCrash(impounded), EarlierCrash(sold)]);

        var tows = RaceLoadingScreenViewModel.TowsAfterRace([impounded], raced: null, null, messages, Runs);

        Assert.Empty(tows.Earlier);
    }

    [Fact]
    public void An_earlier_races_car_is_towed_once()
    {
        var earlier = new Car();
        var messages = AssettoCorsaLauncher.AsEarlier([EarlierCrash(earlier), EarlierCrash(earlier)]);

        var tows = RaceLoadingScreenViewModel.TowsAfterRace([earlier], raced: null, null, messages, Runs);

        Assert.Single(tows.Earlier);
    }

    // ---- what deletes under _replaced leave ----

    [Fact]
    public void A_folder_that_cannot_be_renamed_is_marked_beside_it_and_swept_once_let_go()
    {
        var aside = Path.Combine(Content, ModImport.ReplacedFolder);
        var old = Path.Combine(aside, "old");
        _temp.File("Content/_replaced/old/held.txt", "x");
        _temp.File("Content/_replaced/kept/mod.json", "{}");

        // A file held open without delete sharing: the folder can be neither renamed nor deleted whole
        using (File.Open(Path.Combine(old, "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            ModImport.DeleteAside(old);
        Assert.True(Directory.Exists(old));
        Assert.True(File.Exists(old + ModImport.GoneSuffix));

        ModImport.SweepReplaced(aside);

        Assert.False(Directory.Exists(old));
        Assert.False(File.Exists(old + ModImport.GoneSuffix));
        Assert.True(File.Exists(Path.Combine(aside, "kept", "mod.json")));
    }

    [Fact]
    public void A_mark_an_older_build_left_beside_its_folder_is_swept_with_it()
    {
        var aside = Path.Combine(Content, ModImport.ReplacedFolder);
        _temp.File("Content/_replaced/older/file.txt", "x");
        _temp.File("Content/_replaced/older" + ModImport.GoneSuffix, "");
        _temp.File("Content/_replaced/lone" + ModImport.GoneSuffix, "");

        ModImport.SweepReplaced(aside);

        Assert.False(Directory.Exists(aside));
    }
}
