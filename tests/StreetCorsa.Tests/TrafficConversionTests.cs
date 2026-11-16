using System.Buffers.Binary;
using StreetCorsa.Mods;
using StreetCorsa.Services.Race.Traffic;
using StreetCorsa.Services.Race.Traffic.Conversion;

namespace StreetCorsa.Tests;

/// <summary>
/// The traffic of a run on public roads, in the core: plans built from a road's AI line, the plan looked for in its order,
/// the traffic cars as recipes over the player's own cars, the cast, and the converter's texture work
/// </summary>
public sealed class TrafficConversionTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A loop of a road round a circle, counter-clockwise seen from above, its sides as given</summary>
    private static AiLine Circle(double radius, double left, double right, int points = 600, Func<int, double>? widthAt = null)
    {
        var list = new List<AiLine.Point>();
        for (var i = 0; i < points; i++)
        {
            var a = 2 * Math.PI * i / points;
            var extra = widthAt?.Invoke(i) ?? 0;
            list.Add(new AiLine.Point(radius * Math.Cos(a), 0, radius * Math.Sin(a), left + extra, right + extra, 0, 1, 0));
        }
        return AiLine.Of(list);
    }

    /// <summary>The bytes of an AI line as AC writes one (version 7), with nothing after the extras</summary>
    private static byte[] Bytes(AiLine line)
    {
        var n = line.Points.Count;
        var bytes = new byte[16 + n * 20 + 4 + n * 72];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 7);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), n);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16 + n * 20), n);
        for (var i = 0; i < n; i++)
        {
            var p = line.Points[i];
            var at = 16 + i * 20;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), (float)p.X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 4), (float)p.Y);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 8), (float)p.Z);
            var extra = 16 + n * 20 + 4 + i * 72;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(extra + 5 * 4), (float)p.SideLeft);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(extra + 6 * 4), (float)p.SideRight);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(extra + 9 * 4), (float)p.NormalX);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(extra + 10 * 4), (float)p.NormalY);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(extra + 11 * 4), (float)p.NormalZ);
        }
        return bytes;
    }

    [Fact]
    public void An_ai_line_reads_its_points_sides_and_normals()
    {
        var line = AiLine.Parse(Bytes(Circle(100, 3, 4, 50)));
        Assert.Equal(50, line.Points.Count);
        Assert.Equal(100, line.Points[0].X, 3);
        Assert.Equal(3, line.Points[7].SideLeft, 3);
        Assert.Equal(4, line.Points[7].SideRight, 3);
        Assert.Equal(1, line.Points[7].NormalY, 3);

        var wrong = Bytes(Circle(100, 3, 4, 50));
        wrong[0] = 6;
        Assert.Throws<InvalidDataException>(() => AiLine.Parse(wrong));
        Assert.Throws<InvalidDataException>(() => AiLine.Parse(new byte[10]));
    }

    [Fact]
    public void A_road_with_real_widths_gets_a_two_way_plan_on_its_driving_side()
    {
        // Real widths vary along the road; a flat one is a placeholder
        var line = Circle(300, 4, 4, 900, i => 0.5 * Math.Sin(i / 30.0));

        var right = TrafficPlanBuilder.Build(line, keepLeft: false);
        Assert.True(right.TwoWay, right.Why);
        Assert.True(right.Loop);
        Assert.Empty(right.Problems);
        Assert.Equal(2, right.Lanes.Count);

        // In AC's world (right = forward × up), right of a line going from +X towards +Z is towards the circle's centre:
        // keeping right, the lane with the racing direction is the inner one, 2 m in (a quarter of the road's 8 m)
        var withFlow = right.Lanes.Single(l => l.WithFlow);
        var against = right.Lanes.Single(l => !l.WithFlow);
        static double Radius(TrafficPlanBuilder.Vector p) => Math.Sqrt(p.X * p.X + p.Z * p.Z);
        Assert.All(withFlow.Points, p => Assert.True(Radius(p) < 300));
        Assert.All(against.Points, p => Assert.True(Radius(p) > 300));
        Assert.Equal(298, withFlow.Points.Average(Radius), 0);

        // Keeping left, it is the other way round
        var left = TrafficPlanBuilder.Build(line, keepLeft: true);
        Assert.All(left.Lanes.Single(l => l.WithFlow).Points, p => Assert.True(Radius(p) > 300));

        // Points 10 m apart, as the shipped plans
        var gaps = withFlow.Points.Zip(withFlow.Points.Skip(1), (a, b) => a.Distance(b)).ToList();
        Assert.All(gaps, g => Assert.InRange(g, 9.5, 10.5));

        // The JSON CSP's traffic tool reads
        var json = System.Text.Json.JsonDocument.Parse(TrafficPlanBuilder.ToJson(right)).RootElement;
        Assert.Equal(2, json.GetProperty("lanes").GetArrayLength());
        Assert.True(json.GetProperty("lanes")[0].GetProperty("loop").GetBoolean());
        Assert.Equal(4, json.GetProperty("rules").GetProperty("laneRoles").GetArrayLength());
        var road = TrafficRoad.Measure(TrafficPlanBuilder.ToJson(right));
        Assert.NotNull(road);
        Assert.Equal(2, road.Lanes);
    }

    [Fact]
    public void Flat_placeholder_widths_or_a_road_too_narrow_for_two_get_one_lane_on_the_line()
    {
        var flat = TrafficPlanBuilder.Build(Circle(300, 5, 5), keepLeft: false);
        Assert.False(flat.TwoWay);
        Assert.Single(flat.Lanes);
        Assert.Contains("placeholder", flat.Why);

        // 1.6 m of road with sides that vary: the lanes would be 0.8 m apart, under a car's width
        var narrow = TrafficPlanBuilder.Build(Circle(300, 0.8, 0.8, 900, i => 0.05 * Math.Sin(i / 30.0)), keepLeft: false);
        Assert.False(narrow.TwoWay);
        Assert.Single(narrow.Lanes);
        Assert.Contains("too narrow", narrow.Why);

        // A road of two lanes that pinches to 3 m for a stretch: still two-way, the lanes held a car's width apart there
        var pinch = TrafficPlanBuilder.Build(Circle(300, 4, 4, 900, i => i is > 100 and < 160 ? -2.5 : 0.3 * Math.Sin(i / 30.0)), keepLeft: false);
        Assert.True(pinch.TwoWay, pinch.Why + string.Join("; ", pinch.Problems));
    }

    [Fact]
    public void A_road_that_passes_over_itself_is_still_two_way()
    {
        // A figure of eight whose crossing is a bridge, 20 m up: seen from above the lanes meet there, in 3D they are apart
        // (Bannochbrae's ry_normal fell to one lane that way, 2026-11-15)
        var list = new List<AiLine.Point>();
        const int n = 2000;
        for (var i = 0; i < n; i++)
        {
            var t = 2 * Math.PI * i / n;
            var wobble = 0.4 * Math.Sin(i / 25.0);
            list.Add(new AiLine.Point(400 * Math.Cos(t), 10 * Math.Sin(t), 400 * Math.Sin(t) * Math.Cos(t), 4 + wobble, 4 + wobble, 0, 1, 0));
        }
        var plan = TrafficPlanBuilder.Build(AiLine.Of(list), keepLeft: true);
        Assert.True(plan.TwoWay, plan.Why);
        Assert.Empty(plan.Problems);
    }

    [Fact]
    public void The_player_can_have_two_way_always_or_one_lane()
    {
        // Flat placeholder widths: one lane by default, two-way when the player says so (what the checks found told)
        var flat = Circle(300, 5, 5);
        Assert.False(TrafficPlanBuilder.Build(flat, false).TwoWay);
        var forced = TrafficPlanBuilder.Build(flat, false, lanes: PlanLanes.TwoWay);
        Assert.True(forced.TwoWay);
        Assert.Equal(2, forced.Lanes.Count);
        Assert.Contains("player's choice", forced.Why);
        Assert.Contains(forced.Problems, p => p.Contains("placeholder"));

        // A road fit for two: one lane on the line when the player says so
        var single = TrafficPlanBuilder.Build(Circle(300, 4, 4, 900, i => 0.5 * Math.Sin(i / 30.0)), false, lanes: PlanLanes.Single);
        Assert.False(single.TwoWay);
        Assert.Single(single.Lanes);
    }

    [Fact]
    public void A_built_plan_is_built_again_when_the_player_changes_the_lanes()
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        var mods = ModSet.Load(_temp.Path);
        var track = _temp.Combine("tracks", "test_road");
        var ai = _temp.Combine("tracks", "test_road", "ai", AiLine.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(ai)!);
        File.WriteAllBytes(ai, Bytes(Circle(300, 4, 4, 900, i => 0.5 * Math.Sin(i / 30.0))));
        var built = _temp.Combine("built");

        Assert.Equal(2, TrafficRoad.Read(TrafficPlans.For(track, "test_road", null, false, mods, built)!.File)!.Lanes);
        Assert.Equal(1, TrafficRoad.Read(TrafficPlans.For(track, "test_road", null, false, mods, built, PlanLanes.Single)!.File)!.Lanes);
        Assert.Equal(2, TrafficRoad.Read(TrafficPlans.For(track, "test_road", null, false, mods, built, PlanLanes.TwoWay)!.File)!.Lanes);
    }

    [Fact]
    public void A_two_way_plan_keeps_off_the_kerbs_the_track_ships()
    {
        // The real road is narrower on its outer side than the AI line says: its outer edge 2.6 m out, not 4. The
        // outer lane (against the racing direction, keeping right) was put 2 m out, 0.6 m from that kerb
        var line = Circle(300, 4, 4, 900, i => 0.3 * Math.Sin(i / 30.0));
        var ring = (double r) => Enumerable.Range(0, 900).Select(i => 2 * Math.PI * i / 900).Select(a => (r * Math.Cos(a), r * Math.Sin(a))).ToList();
        var plan = TrafficPlanBuilder.Build(line, keepLeft: false, TrafficPlanBuilder.Edges.From(left: ring(302.6), right: ring(296)));
        Assert.True(plan.TwoWay, plan.Why + string.Join("; ", plan.Problems));
        // Pulled in to keep its metre from the kerb
        Assert.All(plan.Lanes.Single(l => !l.WithFlow).Points, p => Assert.True(Math.Sqrt(p.X * p.X + p.Z * p.Z) <= 301.6 + 0.02));
    }

    [Fact]
    public void A_roads_plan_is_its_own_else_a_mods_else_one_built_and_kept()
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("roads/mod.json", """{ "id": "roads" }""");
        var mods = ModSet.Load(_temp.Path);
        var built = _temp.Combine("built");

        // No plan and no AI line: no traffic
        var track = _temp.Combine("tracks", "test_road");
        Directory.CreateDirectory(track);
        Assert.False(TrafficPlans.CanHave(track, "test_road", null, mods));
        Assert.Null(TrafficPlans.For(track, "test_road", null, false, mods, built));

        // An AI line: a plan built from it, kept, and found again without building
        var ai = _temp.Combine("tracks", "test_road", "ai", AiLine.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(ai)!);
        File.WriteAllBytes(ai, Bytes(Circle(300, 4, 4, 900, i => 0.5 * Math.Sin(i / 30.0))));
        Assert.True(TrafficPlans.CanHave(track, "test_road", null, mods));
        var plan = TrafficPlans.For(track, "test_road", null, false, mods, built);
        Assert.NotNull(plan);
        Assert.Equal(PlanSource.Built, plan.Source);
        Assert.Equal(plan.File, plan.ForRace);
        Assert.StartsWith(built, plan.File);
        Assert.Equal(2, TrafficRoad.Read(plan.File)!.Lanes);
        var written = File.GetLastWriteTimeUtc(plan.File);
        Assert.Equal(written, File.GetLastWriteTimeUtc(TrafficPlans.For(track, "test_road", null, false, mods, built)!.File));
        // The other driving side is another plan
        Assert.NotEqual(File.ReadAllText(plan.File), File.ReadAllText(TrafficPlans.For(track, "test_road", null, true, mods, built)!.File));

        // A mod's plan goes before one built
        _temp.File("roads/traffic/plans/test_road/traffic.json", """{ "lanes": [] }""");
        mods = ModSet.Load(_temp.Path);
        Assert.Equal(PlanSource.Mod, TrafficPlans.For(track, "test_road", null, false, mods, built)!.Source);

        // The track's own goes before all, and race.ini names none: the mode finds it itself
        _temp.File("tracks/test_road/data/traffic.json", """{ "lanes": [] }""");
        var own = TrafficPlans.For(track, "test_road", null, false, mods, built)!;
        Assert.Equal(PlanSource.Track, own.Source);
        Assert.Null(own.ForRace);
    }

    [Fact]
    public void Traffic_cars_are_recipes_over_the_players_cars_and_fleets_say_their_side()
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("roads/mod.json", """{ "id": "roads" }""");
        _temp.File("roads/traffic/fleets.json", """
            {
              "base": "kunos",
              "fleets": {
                "uk": { "keep": "left", "models": [ { "id": "escort", "weight": 1 } ] },
                "italy": [ { "id": "giulia", "weight": 1 } ],
                "kunos": [ { "id": "miata", "weight": 1 } ]
              },
              "tracks": { "test_road": "uk", "strada": "italy" },
              "cars": {
                "escort": { "name": "Ford Escort", "cars": [ "lm_escort", "ks_ford_escort_mk1" ], "maxSpeed": 85, "mass": 870, "paint": false,
                            "lights": { "headlights": [ "LIGHT_F" ] }, "nearFull": true, "skins": 3, "color": "modern" },
                "miata": { "car": "ks_mazda_miata" },
                "bad car": { "car": "x" },
                "nothing": { "name": "no car named" }
              }
            }
            """);
        var fleets = TrafficFleets.Load(ModSet.Load(_temp.Path));

        Assert.True(fleets.KeepsLeft("test_road", null));
        Assert.False(fleets.KeepsLeft("strada", null));
        Assert.Equal("uk", fleets.FleetOf("test_road", "any"));
        Assert.Equal("kunos", fleets.Base);
        Assert.Equal(["miata"], fleets.BaseModels.Select(m => m.Id));

        var escort = fleets.Cars["escort"];
        Assert.Equal(["lm_escort", "ks_ford_escort_mk1"], escort.Cars);
        Assert.Equal(85, escort.MaxSpeed);
        Assert.Equal(870, escort.Mass);
        Assert.Empty(escort.Paint!);
        Assert.Equal(["LIGHT_F"], escort.Lights!["headlights"]);
        Assert.True(escort.NearFull);
        Assert.Equal(3, escort.Skins);
        Assert.Equal("modern", escort.Color);
        var miata = fleets.Cars["miata"];
        Assert.Equal(["ks_mazda_miata"], miata.Cars);
        Assert.Null(miata.Paint);
        Assert.Equal(90, miata.MaxSpeed);
        Assert.False(fleets.Cars.ContainsKey("bad car"));
        Assert.False(fleets.Cars.ContainsKey("nothing"));
        Assert.Contains(fleets.Problems, p => p.Contains("names no AC car"));
    }

    [Fact]
    public void A_fleets_model_is_an_id_that_stays_in_its_folder_and_on_its_ini_line()
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("roads/mod.json", """{ "id": "roads" }""");
        _temp.File("roads/traffic/fleets.json", """
            { "fleets": { "mixed": [ { "id": "escort" }, { "id": "..\\..\\out" }, { "id": "a/b" }, { "id": "x.y" }, { "id": "a=b" },
                                     { "id": "[x]" }, { "id": "two words" }, { "id": "a:b" } ] } }
            """);
        var fleets = TrafficFleets.Load(ModSet.Load(_temp.Path));

        Assert.Equal(["escort"], fleets.Fleets["mixed"].Select(m => m.Id));
        Assert.Equal(7, fleets.Problems.Count(p => p.Contains("not an id")));
    }

    [Fact]
    public void A_roads_cast_takes_what_can_be_made_fills_in_from_the_base_fleet_and_keeps_to_the_detail()
    {
        var ac = _temp.Combine("ac");
        void Car(string id) => _temp.File($"ac/content/cars/{id}/{id}.kn5", "");
        Car("ks_ford_escort_mk1");
        Car("ks_mazda_miata");
        Car("ks_toyota_ae86");
        var store = new TrafficModelStore(_temp.Combine("models"));
        var tool = _temp.Combine("ac", "extension", "lua", "tools", "csp-traffic-tool", "data");
        _temp.File("ac/extension/lua/tools/csp-traffic-tool/data/own_truck.json", """{ "main": "own_truck.kn5", "lod": "own_truck_lod.kn5", "collider": "own_truck_collider.kn5" }""");

        TrafficCarRecipe R(string id, params string[] cars) => new() { Id = id, Cars = cars };
        var fleets = TrafficFleets.From(
            new()
            {
                ["uk"] = [new("escort", 1), new("cortina", 1), new("own_truck", 0.5)],
                ["big"] = [new("escort", 1), new("miata", 0.9), new("ae86", 0.8)],
                ["kunos"] = [new("miata", 1), new("ae86", 0.5)]
            },
            new() { ["test_road"] = "uk", ["big_road"] = "big" },
            [R("escort", "lm_escort", "ks_ford_escort_mk1"), R("cortina", "lm_cortina"), R("miata", "ks_mazda_miata"), R("ae86", "ks_toyota_ae86")],
            baseFleet: "kunos");
        var converter = new TrafficCarConverter(ac, new NoTexels());

        var cast = TrafficSetup.CastFor("test_road", null, fleets, converter, store, tool, TrafficDetail.High);
        // The escort from the Kunos car (the first of its cars installed), the truck the player has; the cortina's car
        // is not installed; two are too few, so the base fleet fills in
        Assert.Equal(["escort", "own_truck", "miata", "ae86"], cast.Models.Select(m => m.Id));
        Assert.Equal(["escort", "miata", "ae86"], cast.ToMake.Select(m => m.Model.Id));
        Assert.EndsWith("ks_ford_escort_mk1", cast.ToMake[0].Car);
        Assert.Equal(["own_truck"], cast.Adopt);
        Assert.Contains(cast.Left, l => l.StartsWith("cortina"));

        // Enough of its own: no base fleet; and the detail's limit, the heaviest-weighted kept
        var big = TrafficSetup.CastFor("big_road", null, fleets, converter, store, tool, TrafficDetail.High);
        Assert.Equal(["escort", "miata", "ae86"], big.Models.Select(m => m.Id));
        var capped = TrafficSetup.CastFor("big_road", null, fleets, converter, store, tool, TrafficDetail.Low with { MostModels = 2 });
        Assert.Equal(["escort", "miata"], capped.Models.Select(m => m.Id));
        Assert.Contains(capped.Left, l => l.StartsWith("ae86"));
    }

    [Fact]
    public void A_model_that_cannot_be_made_takes_none_of_the_details_places()
    {
        var ac = _temp.Combine("ac");
        void Car(string id) => _temp.File($"ac/content/cars/{id}/{id}.kn5", "");
        Car("car_a");
        Car("car_b");
        Car("car_c");
        Car("car_d");
        var store = new TrafficModelStore(_temp.Combine("models"));
        var tool = _temp.Combine("tool");
        TrafficCarRecipe R(string id) => new() { Id = id, Cars = ["car_" + id] };
        var fleets = TrafficFleets.From(
            new() { ["road"] = [new("a", 1), new("b", 0.9), new("c", 0.8)], ["base"] = [new("d", 1)] },
            new() { ["test_road"] = "road" }, [R("a"), R("b"), R("c"), R("d")], baseFleet: "base");
        var converter = new TrafficCarConverter(ac, new NoTexels());
        var two = TrafficDetail.Low with { MostModels = 2 };

        Assert.Equal(["a", "b"], TrafficSetup.CastFor("test_road", null, fleets, converter, store, tool, two).Models.Select(m => m.Id));

        // Found wanting on this drive: the next of the fleet has its place
        Assert.Equal(["b", "c"], TrafficSetup.CastFor("test_road", null, fleets, converter, store, tool, two,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" }).Models.Select(m => m.Id));

        // Would not convert before, the car as it was then: remembered, left out and said, never tried again
        var carA = Path.Combine(ac, "content", "cars", "car_a");
        store.MarkFailed("a", carA, TrafficCarConverter.ModelsOf(carA), two.TextureSide, R("a"), "no usable LOD C or LOD D");
        var cast = TrafficSetup.CastFor("test_road", null, fleets, converter, store, tool, two);
        Assert.Equal(["b", "c"], cast.Models.Select(m => m.Id));
        Assert.DoesNotContain(cast.ToMake, m => m.Model.Id == "a");
        Assert.Contains(cast.Left, l => l.StartsWith("a:") && l.Contains("no usable LOD"));

        // Too few of the road's own left: the base fleet fills in
        store.MarkFailed("b", Path.Combine(ac, "content", "cars", "car_b"), TrafficCarConverter.ModelsOf(Path.Combine(ac, "content", "cars", "car_b")),
            TrafficDetail.High.TextureSide, R("b"), "encrypted");
        store.MarkFailed("a", carA, TrafficCarConverter.ModelsOf(carA), TrafficDetail.High.TextureSide, R("a"), "no LODs");
        Assert.Equal(["c", "d"], TrafficSetup.CastFor("test_road", null, fleets, converter, store, tool, TrafficDetail.High).Models.Select(m => m.Id));

        // The car changed (LODs made for it): worth another try
        File.WriteAllText(Path.Combine(carA, "car_a_lod_c.kn5"), "lod");
        Assert.Null(store.WhyFailed("a", carA, TrafficCarConverter.ModelsOf(carA), TrafficDetail.High.TextureSide, R("a")));
        Assert.Contains(TrafficSetup.CastFor("test_road", null, fleets, converter, store, tool, TrafficDetail.High).ToMake, m => m.Model.Id == "a");
        // And so is a recipe that changed
        Assert.Null(store.WhyFailed("b", Path.Combine(ac, "content", "cars", "car_b"), TrafficCarConverter.ModelsOf(Path.Combine(ac, "content", "cars", "car_b")),
            TrafficDetail.High.TextureSide, R("b") with { NearFull = true }));
    }

    [Fact]
    public void A_model_is_made_again_when_its_recipe_changes()
    {
        var car = _temp.Combine("ac", "content", "cars", "car_a");
        _temp.File("ac/content/cars/car_a/car_a.kn5", "");
        var models = TrafficCarConverter.ModelsOf(car);
        var recipe = new TrafficCarRecipe { Id = "a", Cars = ["car_a"], MaxSpeed = 80, Lights = new Dictionary<string, IReadOnlyList<string>> { ["headlights"] = ["L"] } };
        var store = new TrafficModelStore(_temp.Combine("models"));
        _temp.File("models/a.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            main = "a.kn5",
            source = new { car = "car_a", converter = TrafficCarConverter.Version, maxTexture = 512, fingerprint = TrafficCarConverter.Fingerprint(car, models), recipe = recipe.Stamp() }
        }));

        Assert.True(store.IsFresh("a", car, models, 512, recipe));
        // Its name is not in the model
        Assert.True(store.IsFresh("a", car, models, 512, recipe with { Name = "Another name", Year = 1970 }));
        Assert.False(store.IsFresh("a", car, models, 1024, recipe));
        Assert.False(store.IsFresh("a", car, models, 512, recipe with { MaxSpeed = 95 }));
        Assert.False(store.IsFresh("a", car, models, 512, recipe with { Paint = [] }));
        Assert.False(store.IsFresh("a", car, models, 512, recipe with { Lights = new Dictionary<string, IReadOnlyList<string>> { ["headlights"] = ["L", "R"] } }));
        Assert.False(store.IsFresh("a", car, models, 512, recipe with { NearFull = true }));
        Assert.False(store.IsFresh("a", car, models, 512, recipe with { Skins = 0 }));

        // A model made before the recipe was kept with it is made again, once
        _temp.File("models/b.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            source = new { maxTexture = 512, fingerprint = TrafficCarConverter.Fingerprint(car, models) }
        }));
        Assert.False(store.IsFresh("b", car, models, 512, recipe));
    }

    [Fact]
    public void A_cars_models_are_told_by_its_lods_ini_else_by_their_names()
    {
        void Files(string car, params string[] names)
        {
            foreach (var name in names) _temp.File($"cars/{car}/{name}", "");
        }

        Files("a", "collider.kn5", "mazda_miata.kn5", "mazda_miata_lod_B.kn5", "mazda_miata_lod_C.kn5", "mazda_miata_lod_D.kn5");
        var a = TrafficCarConverter.ModelsOf(_temp.Combine("cars", "a"), _ => null);
        Assert.Equal(("mazda_miata.kn5", "mazda_miata_lod_B.kn5", "mazda_miata_lod_C.kn5", "mazda_miata_lod_D.kn5", "collider.kn5"), (a.Full, a.LodB, a.LodC, a.LodD, a.Collider));

        // The 500's own: <full>_B
        Files("b", "collider.kn5", "abarth500.kn5", "abarth500_B.kn5", "abarth500_C.kn5", "abarth500_D.kn5");
        var b = TrafficCarConverter.ModelsOf(_temp.Combine("cars", "b"), _ => null);
        Assert.Equal(("abarth500.kn5", "abarth500_B.kn5", "abarth500_C.kn5", "abarth500_D.kn5"), (b.Full, b.LodB, b.LodC, b.LodD));

        // lods.ini says, as AC goes by it: the MX-5 ND's full model is its lod_a
        Files("c", "collider.kn5", "mazda_mx5_lod_a.kn5", "mazda_mx5_lod_b.kn5", "mazda_mx5_lod_c.kn5", "mazda_mx5_lod_d.kn5");
        var lods = "[LOD_0]\nFILE=mazda_mx5_lod_a.kn5\n[LOD_1]\nFILE=mazda_mx5_lod_b.kn5\n[LOD_2]\nFILE=mazda_mx5_lod_c.kn5\n[LOD_3]\nFILE=mazda_mx5_lod_d.kn5\n";
        var c = TrafficCarConverter.ModelsOf(_temp.Combine("cars", "c"), name => name == "lods.ini" ? lods : null);
        Assert.Equal(("mazda_mx5_lod_a.kn5", "mazda_mx5_lod_b.kn5", "mazda_mx5_lod_c.kn5", "mazda_mx5_lod_d.kn5"), (c.Full, c.LodB, c.LodC, c.LodD));
    }

    /// <summary>A DXT1 DDS of <paramref name="side"/> px square with its whole chain, each level's blocks filled with its level number</summary>
    private static byte[] Dxt1(int side)
    {
        var levels = new List<byte[]>();
        for (int s = side, l = 0; ; s = Math.Max(1, s / 2), l++)
        {
            var bytes = new byte[Math.Max(1, (s + 3) / 4) * Math.Max(1, (s + 3) / 4) * 8];
            Array.Fill(bytes, (byte)l);
            levels.Add(bytes);
            if (s == 1) break;
        }
        var dds = new byte[128 + levels.Sum(l => l.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(dds, 0x20534444);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(8), 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(12), (uint)side);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(16), (uint)side);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(28), (uint)levels.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(80), 0x4);
        "DXT1"u8.CopyTo(dds.AsSpan(84));
        var at = 128;
        foreach (var l in levels)
        {
            l.CopyTo(dds, at);
            at += l.Length;
        }
        return dds;
    }

    [Fact]
    public void A_textures_levels_over_the_cap_are_dropped_byte_for_byte()
    {
        var dds = Dxt1(2048);
        var capped = TrafficTextures.CapLevels(dds, 1024);
        Assert.Equal((1024, 1024), TrafficTextures.Size(capped));
        Assert.Equal(11u, BinaryPrimitives.ReadUInt32LittleEndian(capped.AsSpan(28)));
        Assert.Equal(1024 / 4 * (1024 / 4) * 8u, BinaryPrimitives.ReadUInt32LittleEndian(capped.AsSpan(20)));
        // The first level kept is the second of the source, as it was
        Assert.Equal(1, capped[128]);
        Assert.Equal(dds.Length - 2048 / 4 * (2048 / 4) * 8, capped.Length);

        // Small enough, or no levels: the very bytes
        Assert.Same(dds, TrafficTextures.CapLevels(dds, 4096));
        Assert.False(TrafficTextures.IsRaw(dds));
        var (same, change) = TrafficTextures.Process(dds, 4096, new NoTexels());
        Assert.Same(dds, same);
        Assert.Equal(TrafficTextures.Change.None, change);
    }

    [Fact]
    public void Raw_texels_are_block_compressed_opaque_as_dxt1_and_see_through_as_dxt5()
    {
        var rgba = new byte[64 * 64 * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = 200;
            rgba[i + 1] = 40;
            rgba[i + 2] = 40;
            rgba[i + 3] = 255;
        }
        var opaque = TrafficTextures.EncodeDxt(rgba, 64, 64);
        Assert.Equal("DXT1", System.Text.Encoding.ASCII.GetString(opaque, 84, 4));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(opaque.AsSpan(28)));
        Assert.Equal(128 + (256 + 64 + 16 + 4 + 1 + 1 + 1) * 8, opaque.Length);
        // One colour: both ends the same 565 colour, every index 0
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(opaque.AsSpan(128));
        Assert.Equal(((200 >> 3) << 11) | ((40 >> 2) << 5) | (40 >> 3), c0);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(opaque.AsSpan(132)));

        rgba[3] = 0;
        var clear = TrafficTextures.EncodeDxt(rgba, 64, 64);
        Assert.Equal("DXT5", System.Text.Encoding.ASCII.GetString(clear, 84, 4));

        // A side that is not a multiple of 4 is resampled to one
        var odd = TrafficTextures.EncodeDxt(new byte[30 * 18 * 4], 30, 18);
        Assert.Equal((28, 16), TrafficTextures.Size(odd));
    }

    [Fact]
    public void A_raw_texture_goes_through_the_decoder_and_a_small_one_stays()
    {
        // A PNG (any image file) is raw; the decoder gives its texels at the cap
        // A 4 MB one: as raw as a mod's 1024 px body map
        var png = new byte[4 << 20];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        Assert.True(TrafficTextures.IsRaw(png));
        var texels = new SolidTexels();
        var (bytes, change) = TrafficTextures.Process(png, 512, texels);
        Assert.Equal(TrafficTextures.Change.Compressed, change);
        Assert.Equal(512, texels.Asked);
        Assert.True(TrafficTextures.IsDds(bytes));

        var small = png[..1000];
        Assert.Same(small, TrafficTextures.Process(small, 512, texels).Bytes);
    }

    [Fact]
    public void Tints_keep_the_shading_and_palettes_are_the_same_for_the_same_model()
    {
        var grey = new byte[] { 100, 100, 100, 255, 50, 50, 50, 255 };
        var tinted = TrafficPaint.Tint(grey, 100, new System.Numerics.Vector3(200, 0, 0));
        Assert.Equal(200, tinted[0]);
        Assert.Equal(100, tinted[4]);
        Assert.Equal(0, tinted[1]);
        Assert.Equal(255, tinted[3]);

        var pool = Enumerable.Range(0, 100).Select(i => new System.Numerics.Vector3(i * 2, 255 - i * 2, i)).ToList();
        var a = TrafficPaint.Palette(pool, 8, "escort");
        Assert.Equal(8, a.Count);
        Assert.Equal(a, TrafficPaint.Palette(pool, 8, "escort"));
        Assert.Equal(8, a.Distinct().Count());
    }

    [Fact]
    public void Traffic_detail_follows_the_cards_memory_unless_the_player_says()
    {
        Assert.Equal(TrafficDetail.Low, TrafficDetail.ForVideoMemory(2L << 30));
        Assert.Equal(TrafficDetail.Medium, TrafficDetail.ForVideoMemory(4L << 30));
        Assert.Equal(TrafficDetail.High, TrafficDetail.ForVideoMemory(8L << 30));
        Assert.Equal(TrafficDetail.Medium, TrafficDetail.ForVideoMemory(0));
        Assert.Equal(TrafficDetail.Low, TrafficDetail.Current("low"));
        Assert.Equal(TrafficDetail.High, TrafficDetail.Of("HIGH"));
        Assert.Null(TrafficDetail.Of("ultra"));
    }

    [Fact]
    public void A_model_the_player_has_in_the_traffic_tool_is_linked_into_the_games_folder_and_the_tool_left_as_it_is()
    {
        var tool = _temp.Combine("tool");
        _temp.File("tool/truck.json", """{ "main": "truck.kn5", "lod": "truck_lod.kn5", "collider": "truck_collider.kn5" }""");
        _temp.File("tool/truck.kn5", "near");
        _temp.File("tool/truck_lod.kn5", "far");
        _temp.File("tool/truck_collider.kn5", "hull");
        _temp.File("tool/truck_skins/red.dds", "red");
        _temp.File("tool/half.json", """{ "main": "half.kn5", "lod": "half_lod.kn5", "collider": "half_collider.kn5" }""");
        var before = Directory.EnumerateFiles(tool, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);

        var store = new TrafficModelStore(_temp.Combine("models"));
        Assert.True(store.Adopt("truck", tool));
        Assert.False(store.Adopt("half", tool));
        Assert.True(store.Has("truck"));
        Assert.Equal("far", File.ReadAllText(Path.Combine(store.Root, "truck_lod.kn5")));
        Assert.Equal("red", File.ReadAllText(Path.Combine(store.Root, "truck_skins", "red.dds")));
        Assert.Equal(before, Directory.EnumerateFiles(tool, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText));

        var listed = Assert.Single(store.List());
        Assert.Equal("truck", listed.Id);
        Assert.False(listed.Made);
        store.Delete("truck");
        Assert.False(store.Has("truck"));
        Assert.Empty(Directory.EnumerateFiles(store.Root, "*", SearchOption.AllDirectories));
        // The tool's own still there
        Assert.True(File.Exists(Path.Combine(tool, "truck_lod.kn5")));
    }

    private sealed class NoTexels : ITexels
    {
        public (int Width, int Height, byte[] Rgba)? Read(byte[] bytes, int most) => null;
    }

    private sealed class SolidTexels : ITexels
    {
        public int Asked { get; private set; }

        public (int Width, int Height, byte[] Rgba)? Read(byte[] bytes, int most)
        {
            Asked = most;
            var rgba = new byte[most * most * 4];
            Array.Fill(rgba, (byte)128);
            return (most, most, rgba);
        }
    }

    [Fact]
    public void A_definition_that_names_a_file_outside_its_folder_is_not_brought_in_and_nothing_outside_is_touched()
    {
        var tool = _temp.Combine("tool");
        var mine = _temp.File("mine/keep.kn5", "the player's own");
        _temp.File("tool/rooted.json", $$"""{ "main": {{System.Text.Json.JsonSerializer.Serialize(mine)}}, "lod": "rooted_lod.kn5", "collider": "rooted_collider.kn5" }""");
        _temp.File("tool/rooted_lod.kn5", "far");
        _temp.File("tool/rooted_collider.kn5", "hull");
        _temp.File("tool/up.json", """{ "main": "..\\mine\\keep.kn5", "lod": "rooted_lod.kn5", "collider": "rooted_collider.kn5" }""");

        var store = new TrafficModelStore(_temp.Combine("models"));
        Assert.False(store.Adopt("rooted", tool));
        Assert.False(store.Adopt("up", tool));
        Assert.False(store.Adopt("..\\tool\\rooted", tool));
        Assert.False(store.Has("rooted"));
        Assert.Equal("the player's own", File.ReadAllText(mine));

        // One that got into the folder some other way: deleting it deletes nothing it names outside
        _temp.File("models/sneaked.json", $$"""{ "main": {{System.Text.Json.JsonSerializer.Serialize(mine)}} }""");
        store.Delete("sneaked");
        Assert.False(store.Has("sneaked"));
        Assert.Equal("the player's own", File.ReadAllText(mine));

        Assert.True(TrafficModelStore.IsInside(tool, "a.kn5"));
        Assert.True(TrafficModelStore.IsInside(tool, "sub\\a.kn5"));
        Assert.False(TrafficModelStore.IsInside(tool, "..\\tool2\\a.kn5"));
        Assert.False(TrafficModelStore.IsInside(tool, "C:\\a.kn5"));
    }

    [Fact]
    public void A_model_of_the_tools_is_brought_in_once_not_drive_after_drive()
    {
        var tool = _temp.Combine("tool");
        _temp.File("tool/truck.json", """{ "main": "truck.kn5", "lod": "truck_lod.kn5", "collider": "truck_collider.kn5" }""");
        _temp.File("tool/truck.kn5", "near");
        _temp.File("tool/truck_lod.kn5", "far");
        _temp.File("tool/truck_collider.kn5", "hull");
        _temp.File("tool/truck_skins/red.dds", "red");
        var store = new TrafficModelStore(_temp.Combine("models"));
        Assert.True(store.Adopt("truck", tool));

        // Held open, as the mode holds them: bringing it in again would have to delete them, and does not try
        using (new FileStream(Path.Combine(store.Root, "truck.kn5"), FileMode.Open, FileAccess.Read, FileShare.Read))
        using (new FileStream(Path.Combine(store.Root, "truck_skins", "red.dds"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(store.Adopt("truck", tool));

        // The tool's changed: that file, and the definition, are brought in again
        File.WriteAllText(Path.Combine(tool, "truck_lod.kn5"), "a new far model");
        Assert.True(store.Adopt("truck", tool));
        Assert.Equal("a new far model", File.ReadAllText(Path.Combine(store.Root, "truck_lod.kn5")));
    }

    [Fact]
    public void A_weight_too_small_to_write_is_never_written_as_none()
    {
        Assert.Equal("a:1 b:0.5 c:0.01 d:0.01", TrafficFleets.Spec([new("a", 1), new("b", 0.5), new("c", 0.004), new("d", 0.0000001)]));
    }

    [Fact]
    public void A_traffic_car_made_from_nothing_that_is_a_car_folder_is_told()
    {
        _temp.File("content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("content/streetcorsa/traffic/fleets.json", """
            { "cars": { "escort": { "cars": [ "..\\..\\elsewhere", "C:\\cars\\escort", "ks_ford_escort_mk1" ] },
                        "nowhere": { "car": "../up" } } }
            """);

        var fleets = TrafficFleets.Load(ModSet.Load(_temp.Combine("content")));

        Assert.Equal(["ks_ford_escort_mk1"], fleets.Cars["escort"].Cars);
        Assert.False(fleets.Cars.ContainsKey("nowhere"));
        Assert.Equal(3, fleets.Problems.Count(p => p.Contains("no car folder's name")));

        // And a recipe made by hand finds no car outside content\cars either
        _temp.File("elsewhere/a.kn5", "");
        _temp.File("ac/content/cars/real/a.kn5", "");
        var converter = new TrafficCarConverter(_temp.Combine("ac"), new NoTexels());
        Assert.Null(converter.SourceOf(new TrafficCarRecipe { Id = "x", Cars = ["..\\..\\..\\elsewhere", _temp.Combine("elsewhere")] }));
        Assert.Equal(_temp.Combine("ac", "content", "cars", "real"),
            converter.SourceOf(new TrafficCarRecipe { Id = "x", Cars = ["..\\..\\..\\elsewhere", "real"] }));
    }

    [Fact]
    public void A_fleets_model_whose_cars_are_not_installed_is_still_cast_from_the_tools_folder()
    {
        var ac = _temp.Combine("ac");
        var tool = _temp.Combine("ac", "extension", "lua", "tools", "csp-traffic-tool", "data");
        _temp.File("ac/extension/lua/tools/csp-traffic-tool/data/escort.json", """{ "main": "escort.kn5", "lod": "escort_lod.kn5", "collider": "escort_collider.kn5" }""");
        var store = new TrafficModelStore(_temp.Combine("models"));
        var fleets = TrafficFleets.From(new() { ["uk"] = [new("escort", 1), new("cortina", 1)] }, new() { ["test_road"] = "uk" },
            [new TrafficCarRecipe { Id = "escort", Cars = ["lm_escort"] }, new TrafficCarRecipe { Id = "cortina", Cars = ["lm_cortina"] }]);

        var cast = TrafficSetup.CastFor("test_road", null, fleets, new TrafficCarConverter(ac, new NoTexels()), store, tool, TrafficDetail.High);

        Assert.Equal(["escort"], cast.Models.Select(m => m.Id));
        Assert.Equal(["escort"], cast.Adopt);
        Assert.Empty(cast.ToMake);
        Assert.Contains(cast.Left, l => l.StartsWith("cortina:"));
    }

    [Fact]
    public void A_cars_full_model_by_name_is_its_biggest_as_the_encrypted_check_takes_it()
    {
        _temp.File("cars/kit/collider.kn5", "");
        _temp.File("cars/kit/a_driver.kn5", "small");
        _temp.File("cars/kit/car.kn5", new string('x', 500));
        _temp.File("cars/kit/car_lod_C.kn5", new string('x', 900));

        var models = TrafficCarConverter.ModelsOf(_temp.Combine("cars", "kit"), _ => null);

        Assert.Equal("car.kn5", models.Full);
        Assert.Equal("car_lod_C.kn5", models.LodC);
        Assert.Equal(_temp.Combine("cars", "kit", "car.kn5"), StreetCorsa.Services.Catalog.CarModelFiles.MainModel(_temp.Combine("cars", "kit")), ignoreCase: true);
    }

    [Fact]
    public void A_cars_own_name_is_not_read_as_a_part_of_it()
    {
        static bool Excluded(string name, params string?[] car) =>
            TrafficPaint.IsNotPaintWord(TrafficGeometry.Plain(name, TrafficGeometry.CarWords(TrafficPaint.IsNotPaintWord, car)));

        // "under" in a Thunderbird, "seat" in a SEAT, "disc" in a Discovery, "light" in a Lightweight
        Assert.False(Excluded("thunderbird_body", "ford_thunderbird_57", "Ford Thunderbird"));
        Assert.False(Excluded("seat600_body", "seat_600", "SEAT 600"));
        Assert.False(Excluded("Discovery_paint", "lr_discovery", null));
        Assert.False(Excluded("etype_lightweight_body", "jaguar_etype", "Jaguar E-Type Lightweight"));
        // The parts themselves are still told, in those cars too
        Assert.True(Excluded("seat600_leather", "seat_600", "SEAT 600"));
        Assert.True(Excluded("thunderbird_glass", "ford_thunderbird_57", null));
        Assert.True(Excluded("underbody", "ford_escort", null));
        Assert.True(Excluded("headlight_L", "seat_600", null));
        // Only a word that would be taken for a part is a car word: the make and the body are left in the names
        Assert.Equal(["thunderbird"], TrafficGeometry.CarWords(TrafficPaint.IsNotPaintWord, "ford_thunderbird_57", "Ford Thunderbird body"));
    }

    [Fact]
    public void A_road_from_one_place_to_another_has_open_edges()
    {
        // A hairpin road: out along z = 0, round, back along z = 100. Closed, each edge would run a piece from its
        // finish straight back to its start, across the middle of the map
        List<(double X, double Z)> Side(double offset)
        {
            var points = new List<(double X, double Z)>();
            for (var x = 0.0; x <= 400; x += 10) points.Add((x, 0 - offset));
            for (var a = -80; a <= 80; a += 10) points.Add((400 + (50 + offset) * Math.Cos(a * Math.PI / 180), 50 + (50 + offset) * Math.Sin(a * Math.PI / 180)));
            for (var x = 400.0; x >= 0; x -= 10) points.Add((x, 100 + offset));
            return points;
        }

        var edges = TrafficPlanBuilder.Edges.From(left: Side(-4), right: Side(4));
        Assert.False(edges.Left.Closed);
        Assert.False(edges.Right.Closed);
        // Between the start and the finish there is no edge: nothing within a grid cell
        Assert.Equal(TrafficPlanBuilder.EdgeLine.Cell, edges.Right.Distance(0, 50), 6);
        Assert.Equal(TrafficPlanBuilder.EdgeLine.Cell, edges.Left.Distance(0, 50), 6);

        // A loop's edge still closes, whether or not its last point repeats its first
        List<(double X, double Z)> Ring(double radius) =>
            Enumerable.Range(0, 120).Select(i => (radius * Math.Cos(2 * Math.PI * i / 120), radius * Math.Sin(2 * Math.PI * i / 120))).ToList();
        Assert.True(TrafficPlanBuilder.Edges.From(left: Ring(104), right: Ring(96)).Left.Closed);
    }

    [Fact]
    public void The_nearest_point_found_in_the_grid_is_the_one_going_through_them_all_finds()
    {
        var random = new Random(11);
        var points = Enumerable.Range(0, 3000)
            .Select(_ => new TrafficPlanBuilder.Vector(random.NextDouble() * 4000 - 2000, random.NextDouble() * 60, random.NextDouble() * 4000 - 2000)).ToList();
        // Two at one place: the first of the list is the nearest
        points.Add(points[5]);
        var index = new TrafficPlanBuilder.PointIndex(points);

        for (var i = 0; i < 400; i++)
        {
            // Among them, at one of them, and far off them all
            var p = i == 0 ? points[5]
                : i % 10 == 0 ? new TrafficPlanBuilder.Vector(random.NextDouble() * 40000 - 20000, 0, random.NextDouble() * 40000 - 20000)
                : new TrafficPlanBuilder.Vector(random.NextDouble() * 4400 - 2200, random.NextDouble() * 60, random.NextDouble() * 4400 - 2200);
            var (best, distance) = (0, double.MaxValue);
            for (var j = 0; j < points.Count; j++)
            {
                if (p.DistanceSquared(points[j]) < distance) (best, distance) = (j, p.DistanceSquared(points[j]));
            }
            Assert.Equal((best, distance), index.Nearest(p));
        }
    }

    [Fact]
    public void A_road_whose_ai_line_makes_no_plan_is_not_driven_as_a_run_in_traffic()
    {
        _temp.File("track/ai/fast_lane.ai", "not an AI line");
        var road = _temp.Combine("track");
        Assert.True(TrafficPlans.CanHave(road, "track", null, ModSet.Load(_temp.Combine("no_mods"))));

        Assert.Throws<NoTrafficPlanException>(() => TrafficSetup.For(_temp.Combine("ac"), road, "track", null, 7, 1,
            TrafficFleets.From(new(), new()), TrafficDetail.Medium, ModSet.Load(_temp.Combine("no_mods")),
            store: new TrafficModelStore(_temp.Combine("models")), planRoot: _temp.Combine("plans")));
    }
}
