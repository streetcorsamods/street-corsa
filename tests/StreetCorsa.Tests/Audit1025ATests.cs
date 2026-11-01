using LiteDB;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Race.Validation;
using StreetCorsa.Services.Storage;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-25 audit's fixes to the AC install and the race results: the overlay on the folder the settings name now,
/// a sound that never left its place, manifests that go first (or are emptied), SafeFile's temp files in a car's data,
/// race copies that do not share the files tools write in place, copies counted as leftovers, the uninstall's marker
/// check, an empty result file, and the ingestion's edges
/// </summary>
[Collection(RaceSessionTests.Name)]
public sealed class Audit1025ATests : IDisposable
{
    private const string Car = "sr_audit25_car";

    private static readonly byte[] BankBytes = [1, 2, 3, 4, 5];
    private const string OwnGuids = "{11111111-1111-1111-1111-111111111111} bank:/sr_audit25_car\r\n";

    private readonly TempDir _temp = new();
    private readonly string _cars;
    private readonly string _keep;
    private readonly string _cfg;

    public Audit1025ATests()
    {
        _cars = _temp.Combine("ac", "content", "cars");
        _keep = _temp.Combine("AcRestore");
        _cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(_cars);
        Directory.CreateDirectory(_cfg);
    }

    public void Dispose() => _temp.Dispose();

    private CarDataOverlay Overlay() => new(_cars, _keep);

    private string CarFolder(string id = Car) => Path.Combine(_cars, id);

    private string EngineIni(string id = Car) => Path.Combine(CarFolder(id), "data", "engine.ini");

    private void MakeCar(string id = Car, bool withSound = false)
    {
        _temp.File(EngineIni(id), "[HEADER]\r\nVERSION=1\r\n");
        if (!withSound) return;
        _temp.Bytes(Path.Combine(CarFolder(id), "sfx", id + ".bank"), BankBytes);
        _temp.File(Path.Combine(CarFolder(id), "sfx", "GUIDs.txt"), OwnGuids);
    }

    private CarSound DonorSound()
    {
        var bank = _temp.Bytes(Path.Combine("library", "donor", "sfx", "donor.bank"), [9, 9, 9, 9]);
        return new CarSound(bank, "{22222222-2222-2222-2222-222222222222} bank:/donor\r\n", "donor");
    }

    private static Dictionary<string, string> RaceEngine() => new() { ["engine.ini"] = "[HEADER]\r\nVERSION=2\r\n" };

    /// <summary>Holds a file open for reading without delete sharing: it can be read, not moved or deleted</summary>
    private static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    // ----- the overlay -----

    [Fact]
    public void The_overlay_works_on_the_cars_folder_the_settings_name_at_each_call()
    {
        var carsPath = _temp.Combine("old", "content", "cars");
        var overlay = new CarDataOverlay(() => carsPath, _keep);
        MakeCar();

        // Start-up asked for the folder after the overlay was made: the races go into the one picked
        carsPath = _cars;
        Assert.True(overlay.Apply(Car, RaceEngine()));

        Assert.Contains("VERSION=2", File.ReadAllText(EngineIni()));
        Assert.True(overlay.Restore(Car));
        Assert.Contains("VERSION=1", File.ReadAllText(EngineIni()));
    }

    [Fact]
    public void A_sound_whose_move_failed_is_not_told_as_lost_and_the_cars_own_stays()
    {
        MakeCar(withSound: true);
        var overlay = Overlay();
        var bank = Path.Combine(CarFolder(), "sfx", Car + ".bank");

        using (Hold(bank))
        {
            Assert.ThrowsAny<IOException>(() => overlay.Apply(Car, new Dictionary<string, string>(), DonorSound()));
        }

        var problems = new List<string>();
        Assert.True(overlay.Restore(Car, problems));

        Assert.Empty(problems);
        Assert.Equal(BankBytes, File.ReadAllBytes(bank));
        Assert.Equal(OwnGuids, File.ReadAllText(Path.Combine(CarFolder(), "sfx", "GUIDs.txt")));
    }

    [Fact]
    public void A_sound_whose_kept_bank_is_gone_after_the_move_is_still_told_as_lost()
    {
        MakeCar(withSound: true);
        var overlay = Overlay();
        Assert.True(overlay.Apply(Car, new Dictionary<string, string>(), DonorSound()));
        File.Delete(Path.Combine(CarFolder(), "sfx", CarDataOverlay.SfxKeepFolder, Car + ".bank"));

        var problems = new List<string>();
        Assert.True(overlay.Restore(Car, problems));

        Assert.Single(problems);
    }

    [Fact]
    public void Temp_files_a_cut_short_write_left_in_a_cars_data_go_with_the_next_apply_and_restore()
    {
        MakeCar();
        var data = Path.Combine(CarFolder(), "data");
        string Litter()
        {
            var temp = _temp.File(Path.Combine(data, $".engine.ini.{Guid.NewGuid():N}.tmp"), "half");
            File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddHours(-1));
            return temp;
        }

        var beforeApply = Litter();
        var overlay = Overlay();
        Assert.True(overlay.Apply(Car, RaceEngine()));
        Assert.False(File.Exists(beforeApply));

        var beforeRestore = Litter();
        Assert.True(overlay.Restore(Car));
        Assert.False(File.Exists(beforeRestore));
        Assert.Equal(["engine.ini"], Directory.GetFiles(data).Select(Path.GetFileName));
    }

    [Fact]
    public void A_restore_whose_keep_folder_cannot_go_deletes_the_manifest_first_and_the_car_is_off_the_list()
    {
        MakeCar();
        var overlay = Overlay();
        Assert.True(overlay.Apply(Car, RaceEngine()));

        using (Hold(Path.Combine(_keep, Car, "files", "engine.ini")))
        {
            Assert.True(overlay.Restore(Car));
        }

        Assert.Contains("VERSION=1", File.ReadAllText(EngineIni()));
        Assert.False(File.Exists(Path.Combine(_keep, Car, "manifest.json")));
        Assert.False(overlay.IsApplied(Car));
        Assert.Empty(overlay.Applied);
    }

    [Fact]
    public void A_manifest_that_cannot_be_deleted_is_emptied_and_never_puts_the_old_data_back_again()
    {
        MakeCar();
        var overlay = Overlay();
        Assert.True(overlay.Apply(Car, RaceEngine()));
        var manifest = Path.Combine(_keep, Car, "manifest.json");
        File.SetAttributes(manifest, FileAttributes.ReadOnly);

        using (Hold(Path.Combine(_keep, Car, "files", "engine.ini")))
        {
            Assert.True(overlay.Restore(Car));
        }

        // Gone with its folder, or left with nothing in it: either way it names nothing to put back
        Assert.True(!File.Exists(manifest) || !File.ReadAllText(manifest).Contains("engine.ini"));
        // The player's own change after the race stays
        File.WriteAllText(EngineIni(), "[HEADER]\r\nVERSION=7\r\n");
        var problems = new List<string>();
        overlay.Restore(Car, problems);

        Assert.Empty(problems);
        Assert.Contains("VERSION=7", File.ReadAllText(EngineIni()));
        Assert.False(overlay.IsApplied(Car));
    }

    [Fact]
    public void A_cfg_manifest_that_cannot_be_deleted_is_emptied_and_never_writes_the_old_file_back_again()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "own");
        var backup = new AcConfigBackup(_cfg, _keep);
        backup.Keep(race);
        File.WriteAllText(race, "ours");
        var keepFolder = Path.Combine(_keep, AcConfigBackup.FolderName);
        var manifest = Path.Combine(keepFolder, "ini-manifest.json");
        File.SetAttributes(manifest, FileAttributes.ReadOnly);
        var kept = Directory.GetFiles(keepFolder).Single(f => f != manifest);

        using (Hold(kept))
        {
            Assert.Equal(1, backup.RestoreAll());
        }

        Assert.Equal("own", File.ReadAllText(race));
        Assert.True(!File.Exists(manifest) || !File.ReadAllText(manifest).Contains("race.ini"));

        // Content Manager writes race.ini after the restore: the emptied manifest leaves it be
        File.WriteAllText(race, "edited since");
        Assert.Equal(0, backup.RestoreAll());
        Assert.Equal("edited since", File.ReadAllText(race));
        Assert.False(backup.HasPending);
    }

    // ----- race copies -----

    private void MakeShowCar()
    {
        MakeCar(withSound: true);
        _temp.File(Path.Combine(CarFolder(), "ui", "ui_car.json"), "{\"name\":\"Own\"}");
        _temp.File(Path.Combine(CarFolder(), "skins", "red", "ui_skin.json"), "{\"skinname\":\"Red\"}");
        _temp.Bytes(Path.Combine(CarFolder(), "skins", "red", "preview.jpg"), [1, 2, 3]);
        _temp.Bytes(Path.Combine(CarFolder(), "car.kn5"), [7, 7, 7]);
    }

    [Fact]
    public void A_race_copy_has_files_of_its_own_where_tools_write_in_place()
    {
        MakeShowCar();
        var clone = Car + AcCarFolder.CloneSuffix;
        var overlay = Overlay();
        overlay.CreateClone(Car, clone, RaceEngine(), null);

        // Content Manager refreshing the copy's ui and preview while the race runs
        File.WriteAllText(Path.Combine(CarFolder(clone), "ui", "ui_car.json"), "{\"name\":\"Copy\"}");
        File.WriteAllText(Path.Combine(CarFolder(clone), "skins", "red", "ui_skin.json"), "{\"skinname\":\"Copy\"}");
        File.WriteAllBytes(Path.Combine(CarFolder(clone), "skins", "red", "preview.jpg"), [9]);

        Assert.Equal("{\"name\":\"Own\"}", File.ReadAllText(Path.Combine(CarFolder(), "ui", "ui_car.json")));
        Assert.Equal("{\"skinname\":\"Red\"}", File.ReadAllText(Path.Combine(CarFolder(), "skins", "red", "ui_skin.json")));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(CarFolder(), "skins", "red", "preview.jpg")));
        Assert.Equal([7, 7, 7], File.ReadAllBytes(Path.Combine(CarFolder(clone), "car.kn5")));

        Assert.Equal(1, overlay.RemoveClones());
        Assert.Equal([7, 7, 7], File.ReadAllBytes(Path.Combine(CarFolder(), "car.kn5")));
    }

    [Theory]
    [InlineData(@"ui\ui_car.json", true)]
    [InlineData(@"ui\badge.png", false)]
    [InlineData(@"skins\red\ui_skin.json", true)]
    [InlineData(@"skins\red\preview.jpg", true)]
    [InlineData(@"skins\red\livery.png", true)]
    [InlineData(@"skins\red\body.dds", false)]
    [InlineData(@"skins\red\sub\preview.jpg", false)]
    [InlineData(@"car.kn5", false)]
    [InlineData(@"ui\deep\ui_car.json", false)]
    public void The_files_a_copy_gets_its_own_of_are_the_ui_json_and_each_skins_ui_and_previews(string relative, bool copied) =>
        Assert.Equal(copied, CarDataOverlay.WrittenInPlace(relative));

    [Fact]
    public void A_race_copy_left_in_the_install_or_on_record_counts_as_something_to_take_away()
    {
        MakeShowCar();
        var overlay = Overlay();
        Assert.False(overlay.HasLeftovers);

        overlay.CreateClone(Car, Car + AcCarFolder.CloneSuffix, RaceEngine(), null);
        Assert.True(overlay.HasLeftovers);
        Assert.Empty(overlay.Applied);

        // Off the record, the marked folder in the install still counts
        File.Delete(Path.Combine(_keep, "clones.json"));
        Assert.True(overlay.HasLeftovers);

        Assert.Equal(1, overlay.RemoveClones());
        Assert.False(overlay.HasLeftovers);
    }

    [Fact]
    public void A_recorded_copy_in_an_install_the_settings_no_longer_name_counts_as_a_leftover()
    {
        MakeShowCar();
        Overlay().CreateClone(Car, Car + AcCarFolder.CloneSuffix, RaceEngine(), null);

        var elsewhere = new CarDataOverlay(_temp.Combine("other", "content", "cars"), _keep);

        Assert.True(elsewhere.HasLeftovers);
        Assert.Equal(1, elsewhere.RemoveClones());
        Assert.False(elsewhere.HasLeftovers);
        Assert.False(Directory.Exists(CarFolder(Car + AcCarFolder.CloneSuffix)));
    }

    // ----- the modes at uninstall -----

    [Fact]
    public void The_uninstall_leaves_a_mode_named_like_a_mods_format_alone_without_the_games_marker()
    {
        var modes = _temp.Combine("ac", "extension", "lua", "new-modes");
        _temp.File(Path.Combine(modes, "sc_race", "mode.lua"), "-- ours");
        _temp.File(Path.Combine(modes, "mymod_derby", "mode.lua"), "-- another author's");
        _temp.File(Path.Combine(modes, "mymod_rally", "mode.lua"), "-- ours, for a mod");
        _temp.File(Path.Combine(modes, "mymod_rally", ScRaceMode.Marker), "marked");

        var removed = new ScRaceMode(_temp.Combine("no-source")).Uninstall(_temp.Combine("ac"), ["mymod_derby", "mymod_rally"]);

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(Path.Combine(modes, "sc_race")));
        Assert.False(Directory.Exists(Path.Combine(modes, "mymod_rally")));
        Assert.True(File.Exists(Path.Combine(modes, "mymod_derby", "mode.lua")));
    }

    // ----- AC's cfg folder -----

    [Fact]
    public void The_cfg_reads_and_the_out_folder_are_under_one_Documents_root()
    {
        Assert.Equal(Path.Combine(AcDocuments.Root, "cfg"), IniModificationService.DefaultCfgDirectory);
        Assert.Equal(Path.Combine(AcDocuments.Root, "out", "sc_race"), RaceResultIngestionService.DefaultInboxPath);
        Assert.Throws<ArgumentException>(() => IniModificationService.ReadDefaultIniFile(@"..\race.ini"));
    }

    [Fact]
    public void A_cfg_write_leaves_the_read_only_mark_to_SafeFile_and_puts_it_back_with_the_restore()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "[RACE]\r\nTRACK=own\r\n");
        File.SetAttributes(race, FileAttributes.ReadOnly);
        var service = new IniModificationService(_cfg, _keep);

        Assert.True(service.ApplyIntent(new Services.Configuration.Models.DragRaceIntent { PlayerCarId = Car, OpponentCarId = Car, PlayerName = "Player", OpponentName = "Rival" }));
        Assert.DoesNotContain("TRACK=own", File.ReadAllText(race));

        Assert.True(service.RestoreAll() >= 1);
        Assert.Equal("[RACE]\r\nTRACK=own\r\n", File.ReadAllText(race));
        Assert.True(File.GetAttributes(race).HasFlag(FileAttributes.ReadOnly));
        File.SetAttributes(race, FileAttributes.Normal);
    }

    // ----- the results' inbox -----

    private string Inbox => _temp.Combine("inbox");
    private string Quarantine => _temp.Combine("quarantine");

    private (RaceResultIngestionService Service, GameStateRepository Repository) Ingestion(GameState state, bool? acRunning = false)
    {
        var database = new SaveDatabase(_temp.Combine("Saves"));
        var repository = new GameStateRepository(null, database);
        var sessions = new RaceSessionRepository(database);
        var service = new RaceResultIngestionService(new RaceResultValidator(), new SessionDeduplicator(sessions),
            new RaceResultProcessor(repository, sessions, new RaceFakes.FakeCareer(), new RaceFakes.FakeEvents()),
            sessions, () => state, Inbox, Quarantine, _temp.Combine("archive"), () => acRunning);
        return (service, repository);
    }

    private static (GameState State, RaceContext Context, Opponent Rival) World()
    {
        var world = RaceFakes.World(250m, saveName: "t");
        world.Context.CreatedAt = DateTime.Now.AddMinutes(-5);
        world.Context.LaunchedAt = DateTime.Now.AddMinutes(-4);
        return (world.State, world.Context, world.Rival);
    }

    [Fact]
    public async Task An_empty_result_file_is_quarantined_and_voids_its_race_rather_than_forfeit_it()
    {
        var (state, context, rival) = World();
        var empty = _temp.File(Path.Combine("inbox", Guid.NewGuid() + ".json"), "");
        var (service, repository) = Ingestion(state);
        using var _ = repository;

        var result = await service.ProcessOrphanedResultsAsync();

        Assert.Equal(1, result.FilesQuarantinedForContext);

        Assert.False(File.Exists(empty));
        Assert.True(File.Exists(Path.Combine(Quarantine, Path.GetFileName(empty))));
        Assert.False(result.ForfeitApplied);
        Assert.Null(state.PendingRace);
        Assert.Equal(5000m, rival.Money);
    }

    [Fact]
    public async Task An_unfinished_result_is_left_alone_while_it_cannot_be_told_whether_AC_runs()
    {
        var (state, context, _) = World();
        var tmp = _temp.File(Path.Combine("inbox", Guid.NewGuid() + ".json.tmp"), "{ \"metadata\": {");
        var (service, repository) = Ingestion(state, acRunning: null);
        using var _ = repository;

        await service.ProcessOrphanedResultsAsync();

        Assert.True(File.Exists(tmp));
        Assert.Same(context, state.PendingRace);
    }

    [Fact]
    public async Task A_file_whose_quarantine_move_failed_stays_in_the_inbox_and_goes_with_the_next_pass()
    {
        var state = RaceFakes.World(0m, saveName: "t").State;
        state.PendingRace = null;
        var broken = _temp.File(Path.Combine("inbox", Guid.NewGuid() + ".json"), "{ not json");
        var (service, repository) = Ingestion(state);
        using var _ = repository;

        using (Hold(broken))
        {
            await service.ProcessOrphanedResultsAsync();
        }

        Assert.True(File.Exists(broken));

        await service.ProcessOrphanedResultsAsync();

        Assert.False(File.Exists(broken));
        Assert.True(File.Exists(Path.Combine(Quarantine, Path.GetFileName(broken))));
    }

    [Fact]
    public void A_race_snapshot_puts_back_what_the_race_changed()
    {
        var (state, context, _) = World();
        var money = state.Player.Money;
        var snapshot = RaceStateSnapshot.Take(state);

        state.Player.Money = 1m;
        state.PendingRace = null;
        state.Racers.ReadyToRace.Clear();
        state.StreetTalk.Add(new StreetTalkItem());

        snapshot.Restore(state);

        Assert.Equal(money, state.Player.Money);
        Assert.Same(context, state.PendingRace);
        Assert.NotEmpty(state.Racers.ReadyToRace);
        Assert.Empty(state.StreetTalk);
    }

    // ----- the save's type names -----

    [Fact]
    public void A_save_type_name_of_the_old_assembly_is_not_resolved_any_more()
    {
        Assert.Same(typeof(Opponent), SaveTypeNames.Instance.GetType("Opponent"));
        Type? old;
        try
        {
            old = SaveTypeNames.Instance.GetType("Street_Rod_AC.Models.GameState.Opponent, Street Rod AC");
        }
        catch (LiteException)
        {
            old = null;
        }

        Assert.Null(old);
    }

    [Fact]
    public void The_bracket_estimate_takes_the_pound_from_the_one_conversion()
    {
        var et = BracketRules.EstimateEt(300, 1500);

        Assert.NotNull(et);
        Assert.InRange(et!.Value, 10, 20);
    }
}

/// <summary>
/// catalog.db as one long-lived instance (the audit's PERF finding) on temp files: a repository round trip, calls inside
/// calls, a corrupt file put aside, the move from the saves folder put back when it fails. In the globals collection:
/// the catalog's instance is the process's.
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class Audit1025ACatalogTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose()
    {
        Assert.True(CatalogDatabase.TryClose(TimeSpan.FromSeconds(5)));
        _temp.Dispose();
    }

    private string DbPath => _temp.Combine("Catalog", "catalog.db");

    [Fact]
    public void The_repositories_share_one_open_catalog_and_find_what_was_written()
    {
        var cars = new ContentCatalogRepository(DbPath);
        var profiles = new CarProfileRepository(DbPath);

        cars.UpsertCar(new CarDefinition { Id = "a", Status = ContentStatus.Active });
        cars.UpsertCars([new CarDefinition { Id = "b", Status = ContentStatus.Broken }]);
        profiles.UpsertProfile(new CarProfile { CarDefinitionId = "a" });

        Assert.Equal("a", cars.GetCar("a")!.Id);
        Assert.Equal(["a"], cars.GetCarsByStatus(ContentStatus.Active).Select(c => c.Id));
        Assert.Equal(2, cars.GetAllCars().Count);
        Assert.True(profiles.ProfileExists("a"));
        Assert.Equal(1, profiles.GetProfileCount());
        Assert.True(profiles.UpdateProfile("a", p =>
        {
            // A call inside a call, on the same catalog: no wait for itself
            Assert.NotNull(cars.GetCar("a"));
            return p;
        }));

        // Closed and opened again: it was on the disk
        Assert.True(CatalogDatabase.TryClose(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, cars.GetAllCars().Count);
    }

    [Fact]
    public void Another_catalog_cannot_be_opened_inside_a_call_on_the_open_one()
    {
        var other = _temp.Combine("Other", "catalog.db");

        CatalogDatabase.Use(DbPath, _ =>
            Assert.Throws<InvalidOperationException>(() => CatalogDatabase.Use(other, _ => 0)));

        // After the call it can: the open one is closed first
        Assert.Equal(0, CatalogDatabase.Use(other, db => db.GetCollection("x").Count()));
    }

    [Fact]
    public void A_corrupt_catalog_is_put_aside_and_a_new_one_made()
    {
        _temp.Bytes(Path.Combine("Catalog", "catalog.db"), [.. Enumerable.Range(0, 8192).Select(i => (byte)(i * 31))]);

        var count = CatalogDatabase.Use(DbPath, db => db.GetCollection("catalog_cars").Count());

        Assert.Equal(0, count);
        Assert.True(File.Exists(DbPath + ".bad"));
    }

    [Fact]
    public void A_move_from_the_saves_folder_that_fails_puts_the_side_files_back()
    {
        var legacy = _temp.File(Path.Combine("Saves", "catalog.db"), "old data");
        var journal = _temp.File(Path.Combine("Saves", "catalog-log.db"), "old journal");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        using (new FileStream(legacy, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            CatalogDatabase.Migrate(legacy, DbPath);
        }

        Assert.True(File.Exists(legacy));
        Assert.Equal("old journal", File.ReadAllText(journal));
        Assert.False(File.Exists(DbPath));
        Assert.False(File.Exists(_temp.Combine("Catalog", "catalog-log.db")));

        CatalogDatabase.Migrate(legacy, DbPath);

        Assert.Equal("old data", File.ReadAllText(DbPath));
        Assert.Equal("old journal", File.ReadAllText(_temp.Combine("Catalog", "catalog-log.db")));
        Assert.False(File.Exists(legacy));
    }
}
