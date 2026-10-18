using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Race;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-18 tech audit's fixes in the AC, configuration, race, catalog and AI services: a race copy that cannot
/// be listed, a kept sound that is gone, the race rules' bounds, a format's mode folder that is somebody else's, the
/// mode's files compared by stamp, ComfyUI's "completed", the cars priced after the parts' warm-up, one drag race record
/// </summary>
public sealed class Audit1018DTests : IDisposable
{
    private readonly TempDir _temp = new();

    public Audit1018DTests()
    {
        TestLogging.SilenceLogging();
    }

    public void Dispose() => _temp.Dispose();

    // ---- CarDataOverlay ----

    private const string Car = "sr_test_car";

    private string Cars => _temp.Combine("ac", "content", "cars");

    private CarDataOverlay Overlay() => new(Cars, _temp.Combine("AcRestore"));

    private void MakeCarWithSound()
    {
        _temp.File(Path.Combine("ac", "content", "cars", Car, "data", "engine.ini"), "[HEADER]\nVERSION=1\n");
        _temp.Bytes(Path.Combine("ac", "content", "cars", Car, "sfx", Car + ".bank"), [1, 2, 3]);
        _temp.File(Path.Combine("ac", "content", "cars", Car, "sfx", "GUIDs.txt"), "{11111111-1111-1111-1111-111111111111} bank:/sr_test_car\r\n");
    }

    private CarSound DonorSound()
    {
        var bank = _temp.Bytes(Path.Combine("library", "donor", "sfx", "donor.bank"), [9, 9, 9, 9]);
        return new CarSound(bank, "{22222222-2222-2222-2222-222222222222} bank:/donor\r\n", "donor");
    }

    private static bool CanList(string folder)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(folder).Any();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Denies the test's own account the right to list <paramref name="folder"/>; returns what undoes it</summary>
    private static Action DenyListing(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        return () =>
        {
            var undo = info.GetAccessControl();
            undo.RemoveAccessRule(rule);
            info.SetAccessControl(undo);
        };
    }

    /// <summary>
    /// A folder at <paramref name="folder"/> that cannot be looked into: the right to list it denied, or, for an account
    /// that lists anyway (an administrator's backup privilege), a link to a folder that is gone. Returns what undoes it;
    /// null when neither can be made here.
    /// </summary>
    private static Action? MakeUnlistable(string folder)
    {
        Directory.CreateDirectory(folder);
        var allow = DenyListing(folder);
        if (!CanList(folder)) return allow;
        allow();
        Directory.Delete(folder);

        try
        {
            Directory.CreateSymbolicLink(folder, Path.Combine(Path.GetDirectoryName(folder)!, "gone-" + Guid.NewGuid().ToString("N")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return CanList(folder) ? null : () => Directory.Delete(folder);
    }

    [Fact]
    public void A_copy_folder_that_cannot_be_listed_is_told_and_the_other_copies_still_go()
    {
        // Two folders named as copies: the first (listed first) cannot be looked into, the second is a copy of ours
        var blocked = Path.Combine(Cars, "aaa" + AcCarFolder.CloneSuffix);
        var ours = Path.Combine(Cars, "zzz" + AcCarFolder.CloneSuffix);
        _temp.File(Path.GetRelativePath(_temp.Path, Path.Combine(ours, AcCarFolder.CloneMarker)), "{}");
        var undo = MakeUnlistable(blocked);
        if (undo == null) Assert.Skip("no folder this account cannot list could be made here");
        try
        {
            var problems = new List<string>();
            var removed = Overlay().RemoveClones(problems);

            Assert.Equal(1, removed);
            Assert.False(Directory.Exists(ours));
            var problem = Assert.Single(problems);
            Assert.Contains(blocked, problem);
        }
        finally
        {
            undo();
        }
    }

    [Fact]
    public void A_kept_sound_that_is_gone_is_told_and_the_race_sound_stays()
    {
        MakeCarWithSound();
        var overlay = Overlay();
        Assert.True(overlay.Apply(Car, new Dictionary<string, string>(), DonorSound()));
        var keep = Path.Combine(Cars, Car, "sfx", CarDataOverlay.SfxKeepFolder);
        File.Delete(Path.Combine(keep, Car + ".bank"));
        File.Delete(Path.Combine(keep, "GUIDs.txt"));

        var problems = new List<string>();
        Assert.True(overlay.Restore(Car, problems));

        // Told once per file, the race's files left in place, and the manifest done with (not retried forever)
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains(Car + ".bank"));
        Assert.Contains(problems, p => p.Contains("GUIDs.txt"));
        Assert.Equal([9, 9, 9, 9], File.ReadAllBytes(Path.Combine(Cars, Car, "sfx", Car + ".bank")));
        Assert.False(overlay.IsApplied(Car));
        Assert.False(Directory.Exists(keep));
    }

    // ---- the race rules ----

    private RuleBook Read(string json, List<string> problems)
    {
        _temp.File("mod/rules/mine.json", json);
        var merged = ModMerge.NewObject();
        ModMerge.Into(merged, ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, problems));
        ModMerge.Into(merged, ModMerge.ContentFiles(ModSet.BaseOnly(_temp.Combine("mod")), ContentTypes.Rules, problems));
        return RuleBook.Read(merged, problems);
    }

    [Fact]
    public void A_race_figure_outside_what_AC_takes_is_the_base_games_and_told()
    {
        var problems = new List<string>();

        var book = Read("""{ "race": { "damage": -5, "tyreWear": 1000, "fuelRate": 2, "policeAILevel": 150, "policeAIAggression": 101 } }""", problems);

        // A damage below 0 would have made Math.Clamp throw at every launch
        Assert.Equal(100, book.Race.Damage);
        Assert.Equal(1, book.Race.TyreWear);
        Assert.Equal(2, book.Race.FuelRate);
        Assert.Equal(150, book.Race.PoliceAILevel);
        Assert.Equal(100, book.Race.PoliceAIAggression);
        Assert.Contains(problems, p => p.Contains("race.damage (-5) is not from 0 to 200"));
        Assert.Contains(problems, p => p.Contains("race.tyreWear (1000)"));
        Assert.Contains(problems, p => p.Contains("race.policeAIAggression (101)"));
        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void The_base_games_race_rules_pass_their_own_check()
    {
        var problems = new List<string>();
        new RaceRules().Check("race", problems);
        Assert.Empty(problems);
    }

    // ---- ScRaceMode ----

    private string ModeSource()
    {
        var folder = _temp.Combine("build", "sc_race");
        foreach (var name in new[] { "mode.lua", "format_drag.lua", "format_road.lua", "format_time_attack.lua", "car_parts.lua" })
            _temp.File(Path.Combine("build", "sc_race", name), "-- " + name);
        return folder;
    }

    private string AcRoot()
    {
        var ac = _temp.Combine("ac");
        Directory.CreateDirectory(Path.Combine(ac, "extension"));
        return ac;
    }

    private RaceFormats FormatsWithOwnMode()
    {
        _temp.File("mods/jdm/mod.json", """{ "id": "jdm" }""");
        _temp.File("mods/jdm/formats/formats.json", """{ "formats": [ { "id": "gymkhana", "family": "road", "mode": "gymkhana" } ] }""");
        _temp.File("mods/jdm/formats/gymkhana/mode.lua", "-- gymkhana");
        return RaceFormats.Load(ModSet.Load(_temp.Combine("mods")), ModeSource());
    }

    [Fact]
    public void A_formats_mode_does_not_go_into_a_folder_of_that_name_that_is_not_the_games()
    {
        var formats = FormatsWithOwnMode();
        var gymkhana = formats.Get("jdm:gymkhana")!;
        var ac = AcRoot();
        var theirs = Path.Combine(ac, "extension", "lua", "new-modes", RaceFormats.ModeIdOf(gymkhana));
        _temp.File(Path.GetRelativePath(_temp.Path, Path.Combine(theirs, "mode.lua")), "-- somebody else's mode");
        _temp.File(Path.GetRelativePath(_temp.Path, Path.Combine(theirs, "extra.lua")), "-- theirs too");

        var why = new ScRaceMode(ModeSource(), () => formats).Install(ac, gymkhana);

        Assert.NotNull(why);
        Assert.Contains(theirs, why);
        Assert.Equal("-- somebody else's mode", File.ReadAllText(Path.Combine(theirs, "mode.lua")));
        Assert.True(File.Exists(Path.Combine(theirs, "extra.lua")));
        Assert.False(File.Exists(Path.Combine(theirs, ScRaceMode.Marker)));
    }

    [Fact]
    public void A_formats_mode_goes_into_its_own_marked_folder_again_and_again()
    {
        var formats = FormatsWithOwnMode();
        var gymkhana = formats.Get("jdm:gymkhana")!;
        var ac = AcRoot();
        var mode = new ScRaceMode(ModeSource(), () => formats);
        var folder = Path.Combine(ac, "extension", "lua", "new-modes", RaceFormats.ModeIdOf(gymkhana));

        Assert.Null(mode.Install(ac, gymkhana));
        File.WriteAllText(Path.Combine(folder, "stale.lua"), "-- from an older build of the mod");
        Assert.Null(mode.Install(ac, gymkhana));

        Assert.True(File.Exists(Path.Combine(folder, ScRaceMode.Marker)));
        Assert.Equal("-- gymkhana", File.ReadAllText(Path.Combine(folder, "mode.lua")));
        Assert.False(File.Exists(Path.Combine(folder, "stale.lua")));
    }

    [Fact]
    public void An_installed_mode_file_carries_its_sources_write_time_and_an_edited_one_is_still_put_right()
    {
        var source = ModeSource();
        var stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(source, "mode.lua"), stamp);
        var ac = AcRoot();
        var installed = Path.Combine(ScRaceMode.InstalledFolder(ac), "mode.lua");

        Assert.Null(new ScRaceMode(source).Install(ac));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(installed));

        // The same length under another stamp is read and found the same: stamped, kept
        File.WriteAllText(installed, "-- mode.lua");
        File.SetLastWriteTimeUtc(installed, stamp.AddDays(1));
        Assert.Null(new ScRaceMode(source).Install(ac));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(installed));

        // Other bytes are put right, whatever the length
        File.WriteAllText(installed, "-- edited!!");
        Assert.Null(new ScRaceMode(source).Install(ac));
        Assert.Equal("-- mode.lua", File.ReadAllText(installed));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(installed));
    }

    // ---- ComfyUI ----

    [Theory]
    [InlineData("""{ "completed": true }""", true)]
    [InlineData("""{ "completed": "true" }""", true)]
    [InlineData("""{ "completed": "True" }""", true)]
    [InlineData("""{ "completed": 1 }""", true)]
    [InlineData("""{ "completed": false }""", false)]
    [InlineData("""{ "completed": "false" }""", false)]
    [InlineData("""{ "completed": "yes" }""", false)]
    [InlineData("""{ "completed": 0 }""", false)]
    [InlineData("""{ "completed": null }""", false)]
    [InlineData("""{ "completed": { } }""", false)]
    [InlineData("""{ "status_str": "success" }""", false)]
    [InlineData("""true""", false)]
    [InlineData("""null""", false)]
    public void ComfyUIs_completed_is_read_whatever_kind_of_value_it_is(string status, bool completed)
    {
        Assert.Equal(completed, ComfyImageService.Completed(JsonNode.Parse(status)));
    }

    // ---- the cars' prices ----

    /// <summary>Parts that are never up, counting how often they are asked whether they are</summary>
    private sealed class CountingParts : NullCarPartsService
    {
        public int Asked { get; private set; }

        public override bool IsAvailable
        {
            get
            {
                Asked++;
                return false;
            }
        }
    }

    /// <summary>The real parts, every car leaving the factory with the build whose id ends with what the car's name says</summary>
    private sealed class FactoryEngines : NullCarPartsService
    {
        private readonly CatalogParts _parts = new();

        public override StreetCorsa.Parts.PartsCatalog Catalog => _parts.Catalog;
        public override EngineBuildIndex Builds => _parts.Builds;
        public override bool IsAvailable => true;

        public override RatedBuild? GetStockBuild(CarDefinition car) =>
            Builds.Runnable.FirstOrDefault(b => b.Build.Id.EndsWith(car.Name, StringComparison.Ordinal));
    }

    private static CarDefinition ActiveCar(string name = "Test") => new()
    {
        Id = "car",
        Name = name,
        Brand = "Plymouth",
        ContentHash = "hash",
        Status = ContentStatus.Active,
        Specs = new CarSpecsData { Bhp = "275bhp", Weight = "1400kg" }
    };

    [Fact]
    public async Task Start_up_prices_a_new_car_by_its_body_without_asking_the_parts_and_the_warm_up_prices_it_by_them()
    {
        var catalog = new MemoryCatalog();
        catalog.UpsertCar(ActiveCar());
        var profiles = new MemoryProfiles();
        var parts = new CountingParts();
        var service = new CarProfileService(catalog, profiles, parts);

        await service.EnsureProfilesExistAsync();

        Assert.Equal(0, parts.Asked);
        Assert.Equal(2500m, profiles.Profiles["car"].BasePrice);

        await service.RepriceAsync();

        Assert.True(parts.Asked > 0);
        Assert.Equal(2500m, profiles.Profiles["car"].BasePrice);
    }

    [Fact]
    public async Task The_warm_ups_pricing_adds_the_factory_engines_parts_to_the_body()
    {
        var catalog = new MemoryCatalog();
        var car = ActiveCar("chrysler-la-340-275-hp");
        catalog.UpsertCar(car);
        var profiles = new MemoryProfiles();
        var service = new CarProfileService(catalog, profiles, new FactoryEngines());

        await service.EnsureProfilesExistAsync();
        Assert.Equal(2500m, profiles.Profiles["car"].BasePrice);

        await service.RepriceAsync();

        var priced = service.PriceNew(car);
        Assert.True(priced.Engine > 0);
        Assert.Equal(priced.BasePrice, profiles.Profiles["car"].BasePrice);
        Assert.Equal(priced.Engine, profiles.Profiles["car"].EnginePrice);
    }

    // ---- one drag race record ----

    [Fact]
    public void The_race_damage_has_one_default_the_rule_books()
    {
        var rules = RuleBook.Current.Race.Damage;
        Assert.Equal(rules, new DragRaceIntent().DamagePercent);
        Assert.Equal(rules, new RaceEntry { PlayerCar = new Car("car") }.DamagePercent);
        Assert.Equal(rules, new DragRaceLaunchIntent().Race.DamagePercent);
    }

    [Fact]
    public void A_launch_hands_race_ini_its_own_race_record_with_the_context_written_in()
    {
        var entry = new RaceEntry
        {
            PlayerName = "Bob",
            PlayerCar = new Car { DefinitionId = "car_a", SkinId = "red" },
            OpponentName = "Ann",
            OpponentCarId = "car_b",
            TrackId = "ks_drag",
            TrackConfig = "drag1000",
            Format = "drag",
            DamagePercent = 60,
            TunePasses = null
        };

        var intent = RaceSetupBuilder.BuildIntent(entry, "car_b", []);
        var configured = Assert.Single(intent.GetConfigurationIntents());

        Assert.Same(intent.Race, configured);
        var race = Assert.IsType<DragRaceIntent>(configured);
        Assert.Equal("car_a", race.PlayerCarId);
        Assert.Equal("red", race.PlayerSkin);
        Assert.Equal(60, race.DamagePercent);
        Assert.Equal(Assert.IsType<Models.Race.RaceContext>(intent.Metadata["RaceContext"]).ContextId, race.ContextId);
        Assert.Equal("car_a", intent.PlayerCarId);
        Assert.Equal("drag", intent.Format);
        Assert.Equal("ks_drag", new DragRaceIntent().TrackId);
    }
}
