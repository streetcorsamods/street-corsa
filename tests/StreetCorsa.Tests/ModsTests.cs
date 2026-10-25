using StreetCorsa.Models.Career.Milestones;
using StreetCorsa.Models.Career.Victory;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Sounds;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>The Content folder: mods found and ordered, ids namespaced, entries merged and replaced on purpose</summary>
public class ModSetTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private void Mod(string folder, string? manifest) =>
        _temp.File($"{folder}/{(manifest == null ? "readme.txt" : ModSet.ManifestFileName)}", manifest ?? "not a mod");

    [Fact]
    public void The_base_game_loads_first_then_the_mods_by_order_and_id()
    {
        Mod("zeta", """{ "id": "zeta", "name": "Zeta" }""");
        Mod("alpha", """{ "id": "alpha", "order": 5 }""");
        Mod("beta", """{ "id": "beta" }""");
        Mod(ModIds.BaseId, null);

        var mods = ModSet.Load(_temp.Path);

        Assert.Equal([ModIds.BaseId, "beta", "zeta", "alpha"], mods.Mods.Select(m => m.Id));
        Assert.True(mods.Mods[0].IsBase);
        Assert.Equal("alpha", mods.Mods[3].Name); // no name: its id
        Assert.Empty(mods.Problems);
    }

    [Fact]
    public void Folders_that_are_no_mods_are_left_out_and_said()
    {
        Mod(ModIds.BaseId, null);
        Mod("no_manifest", null);
        Mod("_switched_off", """{ "id": "off" }""");
        Mod("broken", "{ not json");
        Mod("car", """{ "id": "car" }""");
        Mod("upper", """{ "id": "Upper Case" }""");
        Mod("impostor", """{ "id": "streetcorsa" }""");
        Mod("first", """{ "id": "same" }""");
        Mod("second", """{ "id": "same" }""");

        var mods = ModSet.Load(_temp.Path);

        Assert.Equal([ModIds.BaseId, "same"], mods.Mods.Select(m => m.Id));
        Assert.Equal(Path.Combine(_temp.Path, "first"), mods.Get("same")!.Folder);
        Assert.Equal(6, mods.Problems.Count);
        Assert.DoesNotContain(mods.Problems, p => p.Contains("_switched_off"));
    }

    [Fact]
    public void A_missing_base_game_is_said()
    {
        Mod("other", """{ "id": "other" }""");

        var mods = ModSet.Load(_temp.Path);

        Assert.Contains(mods.Problems, p => p.Contains("core"));
        Assert.Equal(Path.Combine(_temp.Path, ModIds.BaseId), mods.Base.Folder);
    }

    [Fact]
    public void A_mod_id_goes_before_its_ids_the_base_games_never()
    {
        Assert.Equal("mymod:engines/x", ModIds.Qualify("mymod", "engines/x"));
        Assert.Equal("engines/x", ModIds.Qualify(ModIds.BaseId, "engines/x"));
        Assert.Equal("engines/x", ModIds.Normalize("streetcorsa:engines/x"));
        Assert.Equal("other:x", ModIds.Normalize("other:x"));
        Assert.Equal("engines/x", ModIds.Local("mymod:engines/x"));
        Assert.Equal("mymod", ModIds.ModOf("mymod:engines/x"));
        Assert.Equal(ModIds.BaseId, ModIds.ModOf("engines/x"));
    }

    [Fact]
    public void A_reference_finds_the_mods_own_entry_first_then_the_base_games()
    {
        var known = new HashSet<string> { "mymod:own", "shared" };

        Assert.Equal("mymod:own", ModIds.Resolve("mymod", "own", known.Contains));
        Assert.Equal("shared", ModIds.Resolve("mymod", "shared", known.Contains));
        Assert.Equal("other:thing", ModIds.Resolve("mymod", "other:thing", known.Contains));
        Assert.Equal("own", ModIds.Resolve(ModIds.BaseId, "own", known.Contains));
    }

    [Fact]
    public void The_registry_namespaces_entries_and_takes_replacements_only_on_purpose()
    {
        var baseMod = new ModInfo { Id = ModIds.BaseId };
        var mod = new ModInfo { Id = "mymod" };
        var registry = new ContentRegistry<string>("dealer");

        Assert.Equal("downtown", registry.Add(baseMod, "downtown", null, "base downtown", "base"));
        Assert.Equal("mymod:downtown", registry.Add(mod, "downtown", null, "a second downtown", "mod"));
        Assert.Equal("downtown", registry.Add(mod, null, "streetcorsa:downtown", "downtown remade", "mod"));
        Assert.Null(registry.Add(mod, null, "nowhere", "nothing to replace", "mod"));
        Assert.Null(registry.Add(mod, "other:sneaky", null, "a borrowed namespace", "mod"));
        Assert.Null(registry.Add(mod, "  ", null, "no id", "mod"));

        Assert.Equal(["downtown remade", "a second downtown"], registry.All);
        Assert.Equal("mymod", registry.ModOf("downtown")!.Id);
        Assert.Single(registry.Replaced);
        Assert.Equal(3, registry.Problems.Count);
    }
}

/// <summary>Parts, sounds, dealers and racers of several mods, as the game reads them</summary>
public class ModContentTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private ModSet Mods() => ModSet.Load(_temp.Path);

    private void WriteBaseAndMod()
    {
        _temp.File("streetcorsa/mod.json", "{}");
        _temp.File("mymod/mod.json", """{ "id": "mymod", "name": "My Mod", "version": "1.2" }""");
    }

    [Fact]
    public void A_mods_parts_get_its_namespace_and_mount_on_the_base_games_by_their_fittings()
    {
        WriteBaseAndMod();
        _temp.Parts("streetcorsa/parts/engines/gm", """
            [
                { "id": "engines/gm/block", "name": "block", "model": "block.kn5", "slots": [ { "id": 10, "takes": [ "gm:intake" ] } ] },
                { "id": "engines/gm/carb", "name": "base carb" } ]
            """);
        _temp.Parts("mymod/parts/whatever/folder", """
            [
                { "id": "engines/gm/intake", "name": "intake", "model": "intake.kn5",
                  "slots": [ { "id": 1, "fits": [ "GM:Intake" ] }, { "id": 2, "takes": [ "carb:4bbl" ] } ] },
                { "id": "engines/gm/carb", "name": "mod carb" },
                { "id": "engines/gm/better_block", "name": "block remade", "replaces": "engines/gm/block",
                  "slots": [ { "id": 10, "takes": [ "gm:intake" ] } ] } ]
            """);
        _temp.File("mymod/parts/part_aliases.json", """{ "engines/gm/old_intake": "engines/gm/intake" }""");
        _temp.File("mymod/parts/engine_builds.json", """[ { "id": "kits/hot", "name": "Hot", "parts": [ "engines/gm/block", "engines/gm/intake" ] } ]""");

        var catalog = PartsCatalog.Load(Mods().ContentOf(ContentTypes.Parts).ToList());

        var intake = catalog.Get("mymod:engines/gm/intake")!;
        Assert.Equal("mymod", intake.ModId);
        Assert.Equal("engines/gm/intake", intake.LocalId);
        Assert.Equal(_temp.Combine("mymod", "parts", "whatever", "folder", "intake.kn5"), catalog.GetModelPath(intake));

        // A fitting names no part: whatever takes it, of any mod (its case aside), takes the intake
        var block = catalog.Get("engines/gm/block")!;
        Assert.Equal("block remade", block.Name);
        Assert.True(PartsCatalog.CanMate(intake.Slots[0], block.Slots[0]));
        Assert.Contains(catalog.FindMountable(block, block.Slots[0]), m => m.Part == intake && m.Slot.Id == 1);
        Assert.False(PartsCatalog.CanMate(intake.Slots[1], block.Slots[0]));

        // One way: the block's slot takes what the intake fits, so the block never goes onto the intake
        Assert.False(PartsCatalog.CanMate(block.Slots[0], intake.Slots[0]));

        Assert.Same(intake, catalog.Get("mymod:engines/gm/old_intake"));
        Assert.Equal(["engines/gm/block", "mymod:engines/gm/intake"], catalog.EngineBuilds.Single(b => b.Id == "mymod:kits/hot").Parts);
    }

    [Fact]
    public void A_pack_json_of_before_is_not_read_and_told_once()
    {
        WriteBaseAndMod();
        _temp.File("mymod/parts/old/pack.json", """{ "id": "engines/old", "parts": [ { "id": "engines/old/intake", "class": "intake_manifold" } ] }""");
        _temp.File("mymod/parts/older/pack.json", """{ "id": "engines/older", "parts": [] }""");

        var catalog = PartsCatalog.Load(Mods().ContentOf(ContentTypes.Parts).ToList());

        Assert.Null(catalog.Get("mymod:engines/old"));
        Assert.Null(catalog.Get("mymod:engines/old/intake"));
        var told = Assert.Single(catalog.Problems, p => p.Contains("pack.json"));
        Assert.StartsWith("mymod/", told);
        Assert.Contains("2 file(s)", told);
    }

    [Fact]
    public void A_part_an_older_version_left_behind_gives_way_to_the_newer_file()
    {
        WriteBaseAndMod();
        // Version 1's file sorts after version 2's, which moved it into a folder and changed it
        var old = _temp.File("mymod/parts/cam_a.json", """{ "id": "engines/cam_a", "class": "camshaft", "name": "old" }""");
        _temp.File("mymod/parts/cams/cam_a.json", """{ "id": "engines/cam_a", "class": "camshaft", "name": "new" }""");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));

        var catalog = PartsCatalog.Load(Mods().ContentOf(ContentTypes.Parts).ToList());

        Assert.Equal("new", catalog.Get("mymod:engines/cam_a")!.Name);
        var told = Assert.Single(catalog.Problems, p => p.Contains("engines/cam_a"));
        Assert.Contains("newest file is used", told);
    }

    [Fact]
    public void A_part_of_its_own_named_pack_json_is_a_part()
    {
        WriteBaseAndMod();
        _temp.File("mymod/parts/electrical/pack.json", """{ "id": "engines/electrical/battery_pack", "class": "camshaft" }""");

        var catalog = PartsCatalog.Load(Mods().ContentOf(ContentTypes.Parts).ToList());

        Assert.NotNull(catalog.Get("mymod:engines/electrical/battery_pack"));
        Assert.DoesNotContain(catalog.Problems, p => p.Contains("older format"));
    }

    [Fact]
    public void The_catalogs_own_files_are_no_parts_but_a_part_of_their_name_further_down_is_one()
    {
        WriteBaseAndMod();
        _temp.File("mymod/parts/" + EngineBuild.FileName, """[ { "id": "kits/hot", "name": "Hot", "parts": [ "engines/rebuilds" ] } ]""");
        _temp.File("mymod/parts/" + PartRebuilds.FileName, """{ "jobs": [] }""");
        _temp.Parts("mymod/parts/kits", """[ { "id": "engines/rebuilds", "class": "camshaft" } ]""");
        _temp.File("mymod/parts/more/" + PartRebuilds.FileName, """{ "id": "engines/rebuilds_too", "class": "camshaft" }""");

        var catalog = PartsCatalog.Load(Mods().ContentOf(ContentTypes.Parts).ToList());

        Assert.Equal(["mymod:engines/rebuilds", "mymod:engines/rebuilds_too"], catalog.Parts.Keys.Where(k => k.StartsWith("mymod:")).Order(StringComparer.Ordinal));
        Assert.Equal(["mymod:engines/rebuilds"], catalog.EngineBuilds.Single(b => b.Id == "mymod:kits/hot").Parts);
        Assert.DoesNotContain(catalog.Problems, p => p.Contains("mymod"));
    }

    [Fact]
    public void A_mod_may_be_its_mod_json_and_its_part_files_with_their_models_and_banks_beside_them()
    {
        WriteBaseAndMod();
        WriteSound("mymod/sounds/v8");
        _temp.Parts("mymod/parts", """
            [
              { "id": "engines/mine/block", "make": "gm", "credit": "\"Block\" by Someone, CC BY 4.0", "class": "engine_block", "model": "block.kn5",
                "slots": [ { "id": 1, "takes": [ "mine:head" ] } ] },
              { "id": "engines/mine/head", "make": "gm", "class": "cylinder_head", "slots": [ { "id": 1, "fits": [ "mine:head" ] } ] }
            ]
            """);
        _temp.File("mymod/mod.json", """
            {
              "id": "mymod", "name": "My Mod", "version": "1.2",
              // Every section is what the file of its name would hold
              "builds": [ { "id": "factory/mine", "name": "Mine", "parts": [ "engines/mine/block", "engines/mine/head" ] } ],
              "part_aliases": { "engines/mine/old_head": "engines/mine/head" },
              "cars": { "some_car": { "engine": "mymod:factory/mine" } },
              "sounds": { "pins": { "engines/mine/block": "v8" } },
            }
            """);

        var mods = Mods();
        var catalog = PartsCatalog.Load(mods.ContentOf(ContentTypes.Parts).ToList());
        Assert.DoesNotContain(catalog.Problems, p => p.Contains("mymod"));

        var block = catalog.Get("mymod:engines/mine/block")!;
        var head = catalog.Get("mymod:engines/mine/old_head")!;
        Assert.Equal("gm", block.Make);
        Assert.Equal(_temp.Combine("mymod", "parts", "block.kn5"), catalog.GetModelPath(block));
        Assert.True(PartsCatalog.CanMate(head.Slots[0], block.Slots[0]));
        Assert.Equal(["mymod:engines/mine/block", "mymod:engines/mine/head"], catalog.EngineBuilds.Single(b => b.Id == "mymod:factory/mine").Parts);
        Assert.Equal(["\"Block\" by Someone, CC BY 4.0"], catalog.Credits());

        Assert.Equal("mymod:factory/mine", CarBook.Load(mods).Car("some_car")!.Engine);

        var library = SoundLibrary.Load(mods.ContentOf(ContentTypes.Sounds).ToList(), id => id == "mymod:engines/mine/block");
        Assert.Equal("mymod:v8", library.Overrides.Pins["mymod:engines/mine/block"]);
    }

    [Fact]
    public void A_build_of_mod_json_that_does_not_read_leaves_out_only_itself()
    {
        WriteBaseAndMod();
        _temp.Parts("mymod/parts", """[ { "id": "engines/fine/part" } ]""");
        _temp.File("mymod/mod.json", """
            { "id": "mymod",
              "builds": [ { "id": "kits/bad", "parts": 3 }, { "id": "kits/good", "name": "Good", "parts": [ "engines/fine/part" ] } ] }
            """);

        var catalog = PartsCatalog.Load(Mods().ContentOf(ContentTypes.Parts).ToList());

        Assert.NotNull(catalog.Get("mymod:engines/fine/part"));
        Assert.Contains(catalog.Problems, p => p.Contains("builds[0]"));
        Assert.Contains(catalog.EngineBuilds, b => b.Id == "mymod:kits/good");
    }

    [Fact]
    public void A_mods_cars_engines_are_its_own_builds_first_then_the_base_games()
    {
        WriteBaseAndMod();
        _temp.File("mymod/mod.json", """
            { "id": "mymod",
              "builds": [ { "id": "factory/mine", "name": "Mine", "parts": [] } ],
              "cars": { "mine_car": { "engine": "factory/mine" }, "base_car": { "engine": "factory/base" } },
              "countries": { "atlantis": { "names": [ "Atlantean" ] } },
              "words": { "noise": [ "deluxe" ] } }
            """);
        _temp.File("mymod/cars/cars.json", """{ "cars": { "file_car": { "engine": "factory/mine" } } }""");

        var book = CarBook.Load(Mods());

        Assert.Equal("mymod:factory/mine", book.Car("mine_car")!.Engine);
        Assert.Equal("mymod:factory/mine", book.Car("file_car")!.Engine);
        Assert.Equal("factory/base", book.Car("base_car")!.Engine);
        Assert.Equal("atlantis", book.Country("Atlantean"));
        Assert.DoesNotContain(book.Problems, p => p.Contains("countries") || p.Contains("words"));
    }

    [Fact]
    public void A_name_at_the_top_of_a_mod_json_that_nothing_reads_is_told()
    {
        WriteBaseAndMod();
        // "packs" among them: the parts of before, many to a list, are files of their own now
        _temp.File("mymod/mod.json", """{ "id": "mymod", "_comment": "a note", "pack": [], "packs": [], "builds": [] }""");

        var told = Assert.Single(Mods().Problems, p => p.Contains("none of its fields or sections"));
        Assert.Contains("\"pack\"", told);
        Assert.Contains("\"packs\"", told);
        Assert.DoesNotContain("_comment", told);
        Assert.DoesNotContain("\"builds\"", told);
    }

    [Fact]
    public void A_mods_sounds_get_its_namespace_and_its_pins_find_their_blocks()
    {
        WriteBaseAndMod();
        WriteSound("streetcorsa/sounds/v8");
        WriteSound("mymod/sounds/v8");
        _temp.File("mymod/sounds/sounds.json", """{ "pins": { "engines/gm/block": "v8", "engines/mine/block": "streetcorsa:v8" } }""");

        var library = SoundLibrary.Load(Mods().Folders(ContentTypes.Sounds).ToList(), id => id == "mymod:engines/mine/block");

        Assert.Equal(["v8", "mymod:v8"], library.Curated.Select(s => s.Id));
        Assert.Equal("mymod:v8", library.Overrides.Pins["engines/gm/block"]);
        Assert.Equal("v8", library.Overrides.Pins["mymod:engines/mine/block"]);
    }

    private void WriteSound(string folder)
    {
        _temp.File($"{folder}/sound.bank", "bank");
        _temp.File($"{folder}/GUIDs.txt", "{00000000-0000-0000-0000-000000000000} event:/cars/sound/engine_ext\n");
    }

    [Fact]
    public void A_mods_dealers_join_the_base_games_and_may_remake_one()
    {
        WriteBaseAndMod();
        _temp.File("streetcorsa/dealers/dealers.json", """
            { "dealers": [ { "id": "downtown", "name": "Downtown" }, { "id": "harbour", "name": "Harbour" } ],
              "showrooms": [ { "id": "hall", "capacity": 4 } ] }
            """);
        _temp.File("mymod/dealers/one.json", """{ "id": "valley", "name": "Valley Cars" }""");
        _temp.File("mymod/dealers/two.json", """[ { "replaces": "harbour", "name": "Harbour Remade" } ]""");
        _temp.File("mymod/dealers/rooms.json", """{ "showrooms": [ { "id": "hall", "capacity": 9 } ] }""");

        var dealers = new DealerCatalog(Mods());

        Assert.Equal(["downtown", "harbour", "mymod:valley"], dealers.All.Select(d => d.Id));
        Assert.Equal("Harbour Remade", dealers.Get("harbour")!.Name);
        Assert.Equal(9, dealers.GetShowroom("hall").Capacity);

        // A dealer's file is no showroom, nor a showroom list a dealer
        Assert.Equal(ShowroomSpec.Unknown("valley").Capacity, dealers.GetShowroom("valley").Capacity);
        Assert.Null(dealers.Get("hall"));
    }

    [Fact]
    public void A_bad_showroom_leaves_out_only_itself()
    {
        WriteBaseAndMod();
        _temp.File("streetcorsa/dealers/dealers.json", """
            { "dealers": [ { "id": "downtown", "name": "Downtown" } ], "showrooms": [ { "id": "hall", "capacity": 4 } ] }
            """);
        _temp.File("mymod/dealers/rooms.json", """{ "showrooms": [ { "id": "hall", "floorWidth": "20m" }, { "id": "shed", "capacity": 2 } ] }""");

        var dealers = new DealerCatalog(Mods());

        Assert.Equal(["downtown"], dealers.All.Select(d => d.Id));
        Assert.Equal(4, dealers.GetShowroom("hall").Capacity);
        Assert.Equal(2, dealers.GetShowroom("shed").Capacity);
    }

    [Fact]
    public void A_racer_with_a_boss_block_is_a_boss_of_his_mods_path_with_his_own_figures()
    {
        WriteBaseAndMod();
        _temp.File("streetcorsa/racers/racers.json", """
            { "opponents": [ { "opponentId": "big_al", "name": "Big Al", "age": 41, "gender": "Male", "skill": 100, "aggression": 65, "boss": { "path": "Crown" } } ] }
            """);
        _temp.File("mymod/racers/jim.json", """
            { "opponentId": "big_jim", "name": "Big Jim", "age": 50, "gender": "Male", "skill": 99, "aggression": 70,
              "boss": { "path": "Mopar", "money": 15000, "wins": "35" } }
            """);
        _temp.File("mymod/racers/free.json", """
            { "opponentId": "free", "name": "Free", "age": 50, "gender": "Male", "skill": 99, "aggression": 70, "boss": true }
            """);
        _temp.File("mymod/racers/crown_path.json", """
            { "opponentId": "jerry", "name": "Jerry", "age": 50, "gender": "Male", "skill": 99, "aggression": 70, "boss": { "path": "Crown" } }
            """);
        _temp.File("mymod/racers/bad.json", """
            { "opponentId": "bad", "name": "Bad Boss", "age": 50, "gender": "Male", "skill": 99, "aggression": 70, "boss": { "money": "lots" } }
            """);
        _temp.File("mymod/racers/odd.json", """
            { "opponentId": "odd", "name": "Odd Boss", "age": 50, "gender": "Male", "skill": 99, "aggression": 70, "boss": 5 }
            """);
        _temp.File("streetcorsa/victories/v.json", """{ "victories": [ { "id": "Crown", "name": "Crown" } ] }""");
        _temp.File("mymod/victories/v.json", """{ "victories": [ { "id": "Mopar", "name": "Mopar" } ] }""");


        var racers = new OpponentRepository(Mods()).LoadAllOpponents();

        var al = racers.Single(r => r.Name == "Big Al");
        Assert.True(al.IsBoss);
        Assert.Equal("Crown", al.BossPath);

        var jim = racers.Single(r => r.Name == "Big Jim");
        Assert.True(jim.IsBoss);
        Assert.Equal("mymod:Mopar", jim.BossPath);
        var figures = jim.BossRecord!.Over(new StreetCorsa.Rules.BossRules());
        Assert.Equal(15000m, figures.Money);
        Assert.Equal(35, figures.Wins);
        Assert.Equal(52, figures.Races); // the rules' where he names none

        var free = racers.Single(r => r.Name == "Free");
        Assert.True(free.IsBoss);
        Assert.Equal("", free.BossPath); // out from the start

        Assert.Equal("Crown", racers.Single(r => r.Name == "Jerry").BossPath); // his mod has no Crown path: the base game's

        // A boss block that does not read leaves him out, and the player is told
        Assert.DoesNotContain(racers, r => r.Name is "Bad Boss" or "Odd Boss");
        Assert.Contains(ModProblems.All, p => p.Contains("Bad Boss"));
        Assert.Contains(ModProblems.All, p => p.Contains("Odd Boss"));
    }

    [Fact]
    public void A_mods_racers_get_its_namespace_and_the_base_games_keep_who_they_were()
    {
        WriteBaseAndMod();
        _temp.File("streetcorsa/racers/racers.json", """
            { "opponents": [ { "opponentId": "drv_001", "name": "Eddie", "age": 30, "gender": "Male", "skill": 92, "aggression": 50,
                               "portraitPath": "drv_001.png" } ] }
            """);
        _temp.File("mymod/racers/vera.json", """
            { "opponentId": "vera", "name": "Vera", "age": 25, "gender": "Female", "skill": 95, "aggression": 80, "portraitPath": "faces/vera.png" }
            """);
        _temp.File("mymod/racers/sneaky.json", """
            { "opponentId": "sly", "name": "Sly", "age": 25, "gender": "Male", "skill": 95, "aggression": 80, "portraitPath": "../../streetcorsa/x.png" }
            """);

        _temp.File("mymod/racers/jo.json", """
            { "id": "jo", "name": "Jo", "age": 25, "gender": "Female", "skill": 95, "aggression": 80 }
            """);

        var mods = Mods();
        var racers = new OpponentRepository(mods).LoadAllOpponents();

        var eddie = racers.Single(r => r.Name == "Eddie");
        Assert.Equal("drv_001", eddie.DefinitionId);
        Assert.Equal(OpponentRepository.StableIdFor("drv_001"), eddie.OpponentId);
        Assert.Equal("streetcorsa/racers/drv_001.png", eddie.PortraitPath);

        var vera = racers.Single(r => r.Name == "Vera");
        Assert.Equal("mymod:vera", vera.DefinitionId);
        Assert.Equal("mymod/racers/faces/vera.png", vera.PortraitPath);
        Assert.Equal(_temp.Combine("mymod", "racers", "faces", "vera.png"), Portraits.Resolve(mods, vera.PortraitPath));

        // A portrait path out of the mod's racers folder is no portrait
        Assert.Null(racers.Single(r => r.Name == "Sly").PortraitPath);

        // A one-racer file may call its id "id"
        Assert.Equal("mymod:jo", racers.Single(r => r.Name == "Jo").DefinitionId);

        Assert.Equal(_temp.Combine("streetcorsa", "racers", "drv_001.png"), Portraits.Resolve(mods, eddie.PortraitPath));
        Assert.Null(Portraits.Resolve(mods, "../outside.png"));
    }

    [Fact]
    public void A_saved_portrait_follows_its_mod_to_another_folder()
    {
        _temp.File("streetcorsa/mod.json", "{}");
        _temp.File("rivals-1.0/mod.json", """{ "id": "rivals" }""");
        _temp.File("rivals-1.0/racers/vera.png", "png");
        var saved = Portraits.FromMod(Mods(), Mods().Get("rivals")!, "vera.png");

        Directory.Move(_temp.Combine("rivals-1.0"), _temp.Combine("rivals-1.1"));

        Assert.Equal("rivals/racers/vera.png", saved);
        Assert.Equal(_temp.Combine("rivals-1.1", "racers", "vera.png"), Portraits.Resolve(Mods(), saved));
        Assert.Null(Portraits.Resolve(Mods(), "gone/racers/vera.png"));
        Assert.Null(Portraits.Resolve(Mods(), "rivals/../../x.png"));
    }

    [Fact]
    public void Streets_say_when_they_are_used_and_the_clock_picks_among_those()
    {
        WriteBaseAndMod();
        WriteStreet("streetcorsa/streets/main", null, "day", "dusk", "night");
        WriteStreet("mymod/streets/alley", """[ "night" ]""", "night");
        WriteStreet("mymod/streets/broken", """[ "night" ]""");

        var streets = StreetScenes.LoadAll(Mods());

        Assert.Equal(["main", "mymod:alley"], streets.Select(s => s.Id));
        Assert.Equal([StreetLight.Night], streets[1].Times);

        var random = new Random(1);
        Assert.Equal("main", StreetScenes.Pick(streets, StreetLight.Day, random)!.Id);
        Assert.Contains(Enumerable.Range(0, 40).Select(_ => StreetScenes.Pick(streets, StreetLight.Night, random)!.Id), id => id == "mymod:alley");

        // The street the player is in stays while it fits
        Assert.Same(streets[1], StreetScenes.Pick(streets, StreetLight.Night, random, streets[1]));
        Assert.Same(streets[0], StreetScenes.Pick(streets, StreetLight.Dusk, random, streets[1]));
    }

    private void WriteStreet(string folder, string? times, params string[] models)
    {
        _temp.File($"{folder}/street.json", $$"""
            { "id": "{{Path.GetFileName(folder)}}", "player": { "x": 0, "z": 0, "heading": 0 }, "laneOffset": 3, "approachFrom": 40, "leaveTo": 60
              {{(times == null ? "" : $", \"times\": {times}")}} }
            """);
        foreach (var light in models) _temp.File($"{folder}/street_{light}.kn5", "kn5");
    }
}

/// <summary>The career and the words as data: the base game's read cleanly, and a mod adds to them</summary>
public class ModCareerAndTextTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>The core of the source tree alone: no mod the developer has put next to it</summary>
    private static ModSet BaseGame() => ModSet.CoreOf(RepoPaths.Content);

    [Fact]
    public void The_cores_content_reads_without_a_problem_and_has_no_career_of_its_own()
    {
        var mods = BaseGame();
        Assert.Empty(mods.Problems);

        // The events, milestones and paths are a setting's
        var career = CareerContent.Load(mods);
        Assert.Empty(career.Problems);
        Assert.Empty(career.Events);
        Assert.Empty(career.Milestones);
        Assert.Empty(career.Victories);

        Assert.Empty(GameText.Load(mods).Problems);
    }

    [Fact]
    public void Events_read_their_car_filters()
    {
        _temp.File("streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("streetcorsa/formats/formats.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "formats", "formats.json")));
        _temp.File("streetcorsa/milestones/m.json", """{ "milestones": [ { "id": "street_cred", "trigger": "ReputationReached", "targetValue": 25 } ] }""");
        _temp.File("streetcorsa/events/e.json", """
            { "events": [
              { "id": "ford_fanatics", "name": "Fords", "format": "drag", "entryRequirements": { "type": "brand", "brand": "Ford" } },
              { "id": "sixties_showdown", "name": "Sixties", "format": "circuit", "entryRequirements": { "type": "years", "startYear": 1960, "endYear": 1969 } },
              { "id": "muscle_madness", "name": "Muscle", "format": "drag", "requiredMilestones": [ "street_cred" ],
                "entryRequirements": { "type": "all", "filters": [ { "type": "origin", "origin": "USA" }, { "type": "power", "minHP": 300 } ] },
                "reward": { "cash": 750, "specialItem": "rare_camshaft" } },
              { "id": "high_stakes", "name": "High Stakes", "format": "drag", "isPinkSlip": true }
            ] }
            """);
        var events = CareerContent.Load(ModSet.Load(_temp.Path)).Events;

        Assert.Equal("Ford only", events["ford_fanatics"].EntryRequirements!.DisplayDescription);
        Assert.Contains("1960", events["sixties_showdown"].EntryRequirements!.DisplayDescription);
        Assert.Equal(["street_cred"], events["muscle_madness"].RequiredMilestones);
        Assert.Equal("rare_camshaft", events["muscle_madness"].Reward.SpecialItem);
        Assert.True(events["high_stakes"].IsPinkSlip);
    }

    [Fact]
    public void A_mods_event_asks_for_its_own_milestone_or_the_base_games()
    {
        _temp.File("streetcorsa/milestones/m.json", """{ "milestones": [ { "id": "street_cred", "trigger": "ReputationReached", "targetValue": 25 } ] }""");
        _temp.File("mymod/mod.json", """{ "id": "mymod" }""");
        _temp.File("mymod/milestones/m.json", """{ "id": "own", "trigger": "TotalWins", "targetValue": 3, "unlocks": [ "victory:Path", "victory:King" ] }""");
        _temp.File("mymod/victories/v.json", """{ "id": "Path", "name": "A Path", "goal": [ { "type": "mostWins" } ] }""");
        _temp.File("mymod/events/e.json", """
            [ { "id": "meet", "name": "Meet", "raceType": "Circuit", "requiredMilestones": [ "own", "street_cred", "nowhere" ],
                "entryRequirements": { "type": "any", "filters": [ { "type": "brand", "brand": "Ford" }, { "type": "brand", "brand": "Mercury" } ] },
                "reward": { "cash": 100, "specialItem": "part:engines/x/cam" } },
              { "id": "odd", "entryRequirements": { "type": "colour" } } ]
            """);

        var career = CareerContent.Load(ModSet.Load(_temp.Path), Services.Race.RaceFormats.Installed);

        Assert.Equal(["victory:mymod:Path", "victory:King"], career.Milestones["mymod:own"].Unlocks);
        var meet = career.Events["mymod:meet"];
        Assert.Equal(["mymod:own", "street_cred", "nowhere"], meet.RequiredMilestones);
        Assert.Equal("part:mymod:engines/x/cam", meet.Reward.SpecialItem);
        Assert.False(career.Events.ContainsKey("mymod:odd"));
        Assert.Contains(career.Problems, p => p.Contains("nowhere"));
        Assert.Contains(career.Problems, p => p.Contains("colour"));
    }

    [Fact]
    public void A_mod_adds_lines_to_a_pool_or_rewrites_it()
    {
        _temp.File("streetcorsa/text/t.json", """{ "lines": { "talk.a": ["one"], "talk.b": "two" } }""");
        _temp.File("mymod/mod.json", """{ "id": "mymod" }""");
        _temp.File("mymod/text/t.json", """{ "lines": { "talk.a": ["three"] }, "replace": { "talk.b": ["four", "five"] } }""");

        var text = GameText.Load(ModSet.Load(_temp.Path));

        Assert.Equal(["one", "three"], text.Lines("talk.a"));
        Assert.Equal(["four", "five"], text.Lines("talk.b"));
        Assert.Empty(text.Lines("talk.none"));
        Assert.Equal(string.Empty, text.Pick("talk.none", new Random(0)));
    }

    [Fact]
    public void Placeholders_are_filled_with_their_formats_and_a_typo_shows()
    {
        var values = new TextValues { ["name"] = "Vera", ["price"] = 12500m };

        // Written in the game's culture (US English for the base game), whatever the machine is set to
        Assert.Equal("Vera wants $12,500 for it, {nmae}.", GameText.Fill("{name} wants ${price:N0} for it, {nmae}.", values));
    }

    [Fact]
    public void A_save_played_with_a_mod_that_is_gone_says_so_and_loses_what_needs_it()
    {
        var state = new GameState
        {
            Mods = [new ModRef { Id = ModIds.BaseId }, new ModRef { Id = "gone", Name = "Gone Mod", Version = "2" }],
            UsedCarMarket =
            [
                new UsedCarListing { DealerLocation = "downtown" },
                new UsedCarListing { DealerLocation = "gone:lot" },
                new UsedCarListing { DealerLocation = "" }
            ]
        };
        state.UsedCarMarket.Add(new UsedCarListing { DealerLocation = "here:lot" });
        state.DealerLocations = [new DealerLocation { Id = "downtown" }, new DealerLocation { Id = "gone:lot" }];
        state.Career.ActiveEvents.Add(new RaceEventState { EventDefinitionId = "ford_fanatics" });
        state.Career.ActiveEvents.Add(new RaceEventState { EventDefinitionId = "gone:meet" });
        state.Career.ActiveEvents.Add(new RaceEventState { EventDefinitionId = "here:left_out" });
        state.Career.ActiveEvents.Add(new RaceEventState { EventDefinitionId = "retired_event" });

        _temp.File("streetcorsa/readme.txt", "");
        _temp.File("here/mod.json", """{ "id": "here" }""");
        var mods = ModSet.Load(_temp.Path);

        Assert.Equal(["Gone Mod 2"], ModsInSave.Missing(state, mods).Select(m => m.ToString()));
        Assert.True(ModsInSave.Tidy(state, CareerContent.Load(BaseGame()), mods));

        // Only what a missing mod made goes: what an installed mod left out waits for it
        Assert.Equal(["downtown", "", "here:lot"], state.UsedCarMarket.Select(l => l.DealerLocation));
        Assert.Equal(["downtown"], state.DealerLocations.Select(d => d.Id));
        Assert.Equal(["ford_fanatics", "here:left_out", "retired_event"], state.Career.ActiveEvents.Select(e => e.EventDefinitionId));
        Assert.False(ModsInSave.Tidy(state, CareerContent.Load(BaseGame()), mods));
    }

    [Fact]
    public void A_save_played_with_a_newer_mod_says_which_and_what_is_installed()
    {
        var state = new GameState
        {
            Mods =
            [
                new ModRef { Id = "older", Name = "Older Mod", Version = "1.1" },
                new ModRef { Id = "same", Name = "Same Mod", Version = "1.0" },
                new ModRef { Id = "newer", Name = "Newer Mod", Version = "1.0" },
                new ModRef { Id = "unread", Name = "Unread Mod", Version = "beta" },
                new ModRef { Id = "gone", Name = "Gone Mod", Version = "2" }
            ]
        };
        _temp.File("streetcorsa/readme.txt", "");
        _temp.File("older/mod.json", """{ "id": "older", "version": "1.0.9" }""");
        _temp.File("same/mod.json", """{ "id": "same", "version": "1" }""");
        _temp.File("newer/mod.json", """{ "id": "newer", "version": 1.2 }""");
        _temp.File("unread/mod.json", """{ "id": "unread", "version": "0.1" }""");
        var mods = ModSet.Load(_temp.Path);

        var older = ModsInSave.Older(state, mods);

        Assert.Equal([("older", "1.0.9")], older.Select(m => (m.Saved.Id, m.Installed)));
        var warning = ModsInSave.Warning(ModsInSave.Missing(state, mods), older);
        Assert.Contains("Gone Mod 2", warning);
        Assert.Contains("Older Mod: played with 1.1, installed 1.0.9", warning);
        Assert.EndsWith("Load it anyway?", warning);
    }

    [Fact]
    public void Nulls_in_a_mods_career_leave_out_nothing_but_what_they_break()
    {
        _temp.File("streetcorsa/readme.txt", "");
        _temp.File("mymod/mod.json", """{ "id": "mymod" }""");
        _temp.File("mymod/victories/v.json", """
            [ { "id": "Open", "name": null, "unlock": null, "goal": [ null, { "type": "mostWins", "step": null } ], "progress": null } ]
            """);
        _temp.File("mymod/milestones/m.json", """[ { "id": "m", "trigger": "TotalWins", "targetValue": 1, "unlocks": null } ]""");
        _temp.File("mymod/events/e.json", """
            [ { "id": "e", "raceType": "Circuit", "requiredMilestones": null, "reward": null },
              { "id": "f", "raceType": "Circuit", "requiredMilestones": [ null, "m" ] } ]
            """);

        var career = CareerContent.Load(ModSet.Load(_temp.Path), Services.Race.RaceFormats.Installed);

        var open = Assert.Single(career.Victories);
        Assert.Empty(open.Unlock);
        Assert.Single(open.Goal);
        Assert.Empty(career.Milestones["mymod:m"].Unlocks);
        Assert.Empty(career.Events["mymod:e"].RequiredMilestones);
        Assert.Equal(0m, career.Events["mymod:e"].Reward.Cash);
        Assert.Equal(["mymod:m"], career.Events["mymod:f"].RequiredMilestones);

        var progress = new DataVictory(open).GetProgress(new GameState().Career);
        Assert.Equal(0f, progress.Percentage);
    }

    [Fact]
    public void A_locked_paths_goals_are_still_to_do()
    {
        var path = new DataVictory(new VictoryDefinition
        {
            Id = "P",
            Unlock = [new VictoryStep { Counter = MilestoneTrigger.TotalWins, AtLeast = 10, Step = "Win {target} ({current}, {TotalWins:N0})" }],
            Goal = [new VictoryStep { Type = VictoryStep.MostWinsType, Step = "Most wins" }],
            Progress = new VictoryTexts { Locked = "{TotalWins}/10 and {defeated}/{total}" }
        });
        var career = new GameState().Career;
        career.SetCounter(MilestoneTrigger.TotalWins, 5);
        career.PlayerHasMostWins = true;

        var progress = path.GetProgress(career);

        Assert.Empty(progress.CompletedSteps);
        Assert.Equal(["Win 10 (5, 5)", "(Locked) Most wins"], progress.RemainingSteps);
        Assert.Equal(25f, progress.Percentage);
        Assert.Equal("5/10 and 0/0", progress.ProgressDescription);
    }
}
