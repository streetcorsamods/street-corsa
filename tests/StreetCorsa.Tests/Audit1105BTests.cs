using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Models.Career;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.PaintBooth;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Police;
using StreetCorsa.Services.Simulation;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>The fixes of the 2026-11-05 audit, worker B: the world's services</summary>
public sealed class Audit1105BTests : IDisposable
{
    private readonly TempDir _temp = new();

    public Audit1105BTests() => TestLogging.SilenceLogging();

    public void Dispose() => _temp.Dispose();

    // ----- an event's racers and a crew's leader -----

    [Fact]
    public void An_event_naming_a_mods_racer_takes_that_mods_alone()
    {
        Assert.True(EventOpponentService.IsRacer("my_mod:gino", "my_mod:gino"));
        Assert.True(EventOpponentService.IsRacer("MY_MOD:Gino", "my_mod:gino"));
        Assert.False(EventOpponentService.IsRacer("my_mod:gino", "other_mod:gino"));
        Assert.False(EventOpponentService.IsRacer("my_mod:gino", "gino"));

        // Named without a mod: any racer of that id
        Assert.True(EventOpponentService.IsRacer("gino", "gino"));
        Assert.True(EventOpponentService.IsRacer("gino", "other_mod:gino"));
        Assert.False(EventOpponentService.IsRacer("gino", "other_mod:gina"));
    }

    [Fact]
    public void A_crews_leader_is_known_before_the_racers_are_read_and_another_mods_racer_never_is()
    {
        // As the mod wrote it: the racers not read yet
        var crew = new Crew { Id = "addon:night_owls", ModId = "addon", Boss = "gino" };

        Assert.True(CrewBook.IsLeader(crew, Racer("addon:gino")));
        Assert.True(CrewBook.IsLeader(crew, Racer("gino")));
        Assert.False(CrewBook.IsLeader(crew, Racer("other:gino")));

        // Written in full, and as the racers' reading leaves it
        crew.Boss = "addon:gino";
        Assert.True(CrewBook.IsLeader(crew, Racer("addon:gino")));
        Assert.False(CrewBook.IsLeader(crew, Racer("gino")));
        crew.Boss = null;
        Assert.False(CrewBook.IsLeader(crew, Racer("gino")));
    }

    private static Opponent Racer(string id) => new("Gino", 30, Gender.Male, 95, 50) { DefinitionId = id };

    // ----- the scenes held to their scale -----

    [Fact]
    public void A_shops_camera_room_and_places_are_held_to_the_floor()
    {
        _temp.File("shop/shop.kn5", "x");
        _temp.File("shop/shop.json", """
            {
              "camera": { "alpha": 100, "beta": 9, "radius": 1e30, "targetX": 0, "targetHeight": -1e30, "targetZ": 0, "fov": 50 },
              "room": { "halfWidth": 1e30, "halfDepth": 6, "height": 3.6 },
              "places": [ { "x": 0, "y": 0.5, "z": 1, "width": 1, "depth": 1, "height": 1 },
                          { "x": 1e30, "y": 0.5, "z": 1, "width": 1, "depth": 1, "height": 1 },
                          { "x": 0, "y": 0.5, "z": 1, "width": 1e30, "depth": 1, "height": 1 } ]
            }
            """);

        var scene = Assert.IsType<ShopScene>(ShopScenes.Load(_temp.Combine("shop")));

        Assert.Equal(SceneFiles.MostCameraRadius, scene.Camera.Radius);
        Assert.Equal(SceneFiles.MostCameraBeta, scene.Camera.Beta);
        Assert.Equal(-SceneFiles.MostCameraRadius, scene.Camera.TargetHeight);
        Assert.InRange(scene.Camera.Alpha, -MathF.PI, MathF.PI);
        Assert.Null(scene.Room);
        Assert.Single(scene.Places);
    }

    [Fact]
    public void A_shops_camera_looking_at_a_point_off_the_floor_is_the_default_one()
    {
        _temp.File("shop/shop.kn5", "x");
        _temp.File("shop/shop.json", """{ "camera": { "alpha": 1, "beta": 0.2, "radius": 6, "targetX": 1e30, "targetHeight": 1, "targetZ": 0 } }""");

        var scene = Assert.IsType<ShopScene>(ShopScenes.Load(_temp.Combine("shop")));

        Assert.Equal(5f, scene.Camera.Radius);
        Assert.Equal(0f, scene.Camera.TargetX);
    }

    [Fact]
    public void A_booths_camera_bound_off_the_floor_is_the_default_one()
    {
        _temp.File("booth/booth.kn5", "x");
        _temp.File("booth/painter.kn5", "x");
        _temp.File("booth/booth.json", """
            { "arrive": [ [ 0, 9 ], [ 0, 0.4 ] ],
              "round": { "wait": [ 1e30, 2 ] },
              "camera": { "min": [ -1e30, -4 ], "max": [ 2.5, 4.4 ] } }
            """);

        var booth = PaintBooths.Load(_temp.Combine("booth"));

        Assert.NotNull(booth);
        Assert.Equal(BoothCamera.Default.Min, booth.Camera.Min);
        Assert.Equal(BoothRound.Default.Wait, booth.Round.Wait);
    }

    // ----- an event's field -----

    [Fact]
    public void A_field_given_rivals_always_keeps_one_and_is_empty_only_when_given_none()
    {
        var rivals = Enumerable.Range(0, 3).Select(i => new EventOpponentResult { OpponentName = $"r{i}" }).ToList();

        foreach (var boxes in new string?[] { null, "", "x", "-4", "0", "1", "2", "3", "99" })
            Assert.NotEmpty(EventGrid.Fit(rivals, boxes));
        Assert.Empty(EventGrid.Fit([], "8"));
        Assert.Empty(EventGrid.Fit([], null));
    }

    // ----- the rivals' races -----

    [Fact]
    public void A_rivals_race_that_throws_is_passed_over_and_the_days_other_races_still_run()
    {
        var state = GameState.CreateNew("P");
        Opponent Add(string name, double hp)
        {
            var racer = new Opponent(name, 30, Gender.Male, 95, 50) { Money = 1000m, Status = RacerStatus.ReadyToRace };
            racer.Cars.Add(new Car("car_" + name) { PowerHp = hp, EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 1, TireCondition = 1 });
            state.Racers.AddRacer(racer);
            return racer;
        }

        // The two strongest race first; one's car has a history that cannot be written
        var ann = Add("Ann", 300);
        var bob = Add("Bob", 290);
        var cid = Add("Cid", 200);
        var dee = Add("Dee", 190);
        ann.Cars[0].History = null!;

        // 0.05 accepts every challenge
        var day = new RaceSimulatorService(new FakeMarket(1000m), random: new FixedRandom(0.05)).SimulateDay(state, state.Date);

        var race = Assert.Single(day.Races);
        Assert.Equal(["Cid", "Dee"], new[] { race.Racer1Name, race.Racer2Name }.Order());
        Assert.Equal(1, cid.Stats.Races);
        Assert.Equal(1, dee.Stats.Races);
        Assert.Equal(0, ann.Stats.Races);
        Assert.Equal(0, bob.Stats.Races);
    }

    // ----- the machine shop -----

    [Fact]
    public void A_machine_job_that_fails_part_way_leaves_the_part_as_it_was_and_charges_nothing()
    {
        var parts = new CatalogParts();
        var service = new PartShopService(new FakePartShops(new PartShopDefinition { Id = "gardena", Name = "Gardena Machine", Region = "Gardena", Kind = "machine" }),
            parts, new NewspaperPartsService(parts));
        var state = new GameState { Date = new DateTime(1965, 6, 1, 10, 0, 0) };
        PartShopRoster.Sync(state, service.Catalog);
        state.Player.Money = 100_000;
        var shop = PartShopRoster.Find(state, "gardena")!;

        var engine = TestCars.WithRealEngine(parts.Catalog).Engine!;
        foreach (var part in engine.SelfAndDescendants()) part.Wear = 0.4;
        // The pistons' figures spoil once the block is bored: the job throws after the cut is on it, swapping them
        var pistons = engine.SelfAndDescendants().Where(p => parts.Catalog.Get(p.DefinitionId) is { } d && parts.Catalog.WorkOrder.KindOf(d) == "pistons").ToList();
        Assert.NotEmpty(pistons);
        foreach (var piston in pistons) piston.Tuning = new SpoiltOnceBored(engine);
        state.Player.EngineStand = engine;
        var tree = engine.SelfAndDescendants().Select(p => (p.InstanceId, p.DefinitionId, p.Wear, p.Tear)).ToList();

        var piece = service.Machine.Pieces(state, shop).Single(p => p.FromStand);
        var bore = service.Machine.Offers(state, shop, piece).First(w => w.Offer.Job.Id == "bore" && w.Offer.CanDo && ReferenceEquals(w.Part, engine));

        Assert.Throws<InvalidOperationException>(() => service.Machine.Order(state, shop, bore, new Random(1)));

        Assert.Equal(0, engine.CutOf("bore"));
        Assert.Equal(tree, engine.SelfAndDescendants().Select(p => (p.InstanceId, p.DefinitionId, p.Wear, p.Tear)).ToList());
        Assert.Same(engine, state.Player.EngineStand);
        Assert.Equal(100_000, state.Player.Money);
        Assert.Equal(0, shop.PlayerSpent);
        Assert.Empty(state.Player.MachineWork);
    }

    /// <summary>A part's tuning that cannot be copied once <paramref name="block"/> is bored</summary>
    private sealed class SpoiltOnceBored(PartInstance block) : Dictionary<string, double>, IEnumerable<KeyValuePair<string, double>>
    {
        IEnumerator<KeyValuePair<string, double>> IEnumerable<KeyValuePair<string, double>>.GetEnumerator() =>
            block.CutOf("bore") > 0 ? throw new InvalidOperationException("the pistons' figures are spoilt") : GetEnumerator();
    }
}

/// <summary>The install read again: what was kept of its cars is forgotten (sets the AC folder in use)</summary>
[Collection(GlobalsCollection.Name)]
public sealed class Audit1105BGlobalsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _acBefore = AppSettings.Instance.AssettoCorsaPath;

    public Audit1105BGlobalsTests() => TestLogging.SilenceLogging();

    public void Dispose()
    {
        AppSettings.Instance.AssettoCorsaPath = _acBefore;
        PoliceCars.Forget();
        _temp.Dispose();
    }

    [Fact]
    public void A_cars_data_is_read_again_once_the_install_changed()
    {
        AppSettings.Instance.AssettoCorsaPath = _temp.Combine("ac");
        var drivetrain = _temp.File("ac/content/cars/coupe/data/drivetrain.ini", "[TRACTION]\nTYPE=RWD\n");
        var parts = new CarPartsService(new MemoryCatalog(), new MemoryProfiles(), _ => false);
        Assert.Equal("RWD", parts.Specs("coupe")?.Traction);

        File.WriteAllText(drivetrain, "[TRACTION]\nTYPE=FWD\n");
        Assert.Equal("RWD", parts.Specs("coupe")?.Traction);

        parts.ForgetInstall();
        Assert.Equal("FWD", parts.Specs("coupe")?.Traction);
    }

    [Fact]
    public void A_police_car_installed_while_the_game_runs_comes_once_the_install_changed()
    {
        AppSettings.Instance.AssettoCorsaPath = _temp.Combine("ac");
        _temp.File("ac/content/cars/sedan/skins/black/ui_skin.json", """{ "skinname": "Black" }""");
        Assert.Null(PoliceCars.Installed());

        _temp.File("ac/content/cars/cruiser/skins/black/ui_skin.json", $$"""{ "skinname": "Black", "{{PoliceCars.SkinMarker}}": true }""");
        Assert.Null(PoliceCars.Installed());

        new CarPartsService(new MemoryCatalog(), new MemoryProfiles(), _ => false).ForgetInstall();
        Assert.Equal("cruiser", PoliceCars.Installed()?.CarId);
    }
}
