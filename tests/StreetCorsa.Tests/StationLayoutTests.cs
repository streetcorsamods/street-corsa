using System.Numerics;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// A gas station dressed with the street kit (station.json's "layout", 2026-11-21): the town round it laid out once a save,
/// the cars on its forecourt drawn again each game day, each piece on the station's rolling ground, the ways in and out
/// clear of cars, and the save keeping the station's layout apart from the streets'
/// </summary>
public sealed class StationLayoutTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static StreetPiece Piece(string id, float width, float depth, params string[] tags) =>
        new(id, id + ".kn5", width, depth, 3f, tags.ToHashSet(StringComparer.OrdinalIgnoreCase), 1f);

    private static StreetKit Kit() => StreetKit.From(
    [
        Piece("house", 16f, 28f, "house"),
        Piece("sedan", 1.9f, 5.2f, "curb_car"),
        Piece("pickup", 2f, 5.6f, "curb_car", "truck"),
        Piece("coupe", 1.8f, 4.8f, "curb_car"),
        Piece("lamp", 0.4f, 1.4f, "lamp") with { Lamp = new Vector3(0f, 10f, 1.2f) }
    ]);

    // A station whose way in comes along the road at z 30 and turns in to the pump at the middle, out the other way; a
    // row of houses across the road, a row of parking along the forecourt and spots on it, daily
    private const string Layout = """
        "layout": {
          "rows": [
            { "name": "houses", "from": [ -60, 40 ], "to": [ 60, 40 ], "heading": 180, "take": [ "house" ], "gap": [ 0.5, 1 ] },
            { "name": "parking", "from": [ -30, 16 ], "to": [ 30, 16 ], "heading": 90, "take": [ "curb_car" ], "align": "middle", "gap": [ 1, 3 ], "empty": 0.4, "daily": true }
          ],
          "runs": [ { "from": [ -40, 25 ], "to": [ 40, 25 ], "every": 40, "heading": 0, "take": [ "lamp" ], "clear": false } ],
          "spots": [
            { "name": "by_the_office", "at": [ 8, -4 ], "heading": 10, "take": [ "curb_car" ], "empty": 0.3, "room": true, "clear": true, "daily": true },
            { "name": "on_the_way_in", "at": [ -10, 10 ], "heading": 0, "take": [ "curb_car" ], "room": true, "clear": true, "daily": true }
          ]
        }
        """;

    private StationScene Station(string id = "crossroads", string? layout = Layout, float groundHeight = 0f)
    {
        var folder = Path.Combine(_temp.Path, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "station.json"), $$"""
            { "id": "{{id}}", "arrive": [ [ -100, 30 ], [ -20, 30 ], [ -10, 10 ], [ 0, 4 ] ], "leave": [ [ 0, 4 ], [ 20, 8 ], [ 30, 30 ], [ 100, 30 ] ],
              "lights": { "night": { "brightness": 0.2 } }
              {{(layout == null ? "" : "," + layout)}} }
            """);
        WriteGround(Path.Combine(folder, "ground.bin"), groundHeight);
        return StationScenes.Load(folder)!;
    }

    // A ground of one height over the whole station, as StationGround reads it
    private static void WriteGround(string file, float height)
    {
        using var writer = new BinaryWriter(File.Create(file));
        writer.Write("SCG1".ToCharArray());
        const int cells = 101;
        writer.Write(4f);
        writer.Write(cells);
        writer.Write(cells);
        writer.Write(-200f);
        writer.Write(-200f);
        for (var i = 0; i < cells * cells; i++) writer.Write(height);
    }

    private static bool IsCar(StreetKit kit, PlacedPiece placed) => kit.Find(placed.Piece)!.Tags.Contains("curb_car");

    private static GameState OnDay(GameState state, int day)
    {
        state.Date = new DateTime(1970, 6, 1, 15, 0, 0).AddDays(day);
        return state;
    }

    [Fact]
    public void A_stations_town_is_laid_out_once_and_kept_and_its_cars_are_the_same_all_day_and_others_another_day()
    {
        var kit = Kit();
        var station = Station();
        Assert.NotNull(station.Recipe);
        var state = OnDay(GameState.CreateNew("Player"), 0);

        var morning = StreetLayouts.Laid(station, state, kit).Pieces.Select(p => p.ToString()).ToList();
        Assert.Single(state.Streets);
        Assert.Equal(StreetLayouts.StationKey(station.Id), state.Streets[0].Street);
        Assert.Contains(morning, p => p.StartsWith("house", StringComparison.Ordinal));
        Assert.Contains(morning, p => !p.StartsWith("house", StringComparison.Ordinal) && !p.StartsWith("lamp", StringComparison.Ordinal));

        // Later the same day: the same cars where they were
        state.Date = state.Date.AddHours(5);
        Assert.Equal(morning, StreetLayouts.Laid(station, state, kit).Pieces.Select(p => p.ToString()));

        // Other days: the houses where they were, the cars not always
        var houses = morning.Where(p => p.StartsWith("house", StringComparison.Ordinal)).ToList();
        var days = Enumerable.Range(1, 6).Select(d => StreetLayouts.Laid(station, OnDay(state, d), kit).Pieces.Select(p => p.ToString()).ToList()).ToList();
        Assert.All(days, day => Assert.Equal(houses, day.Where(p => p.StartsWith("house", StringComparison.Ordinal))));
        Assert.Contains(days, day => !day.SequenceEqual(morning));
        Assert.Single(state.Streets);
    }

    [Fact]
    public void A_stations_cars_keep_off_its_ways_in_and_out_and_off_each_other()
    {
        var kit = Kit();
        var station = Station();
        var state = GameState.CreateNew("Player");
        var recipe = StreetLayouts.RecipeOf(station)!;
        for (var day = 0; day < 20; day++)
        {
            var cars = StreetLayouts.Laid(station, OnDay(state, day), kit).Pieces.Where(p => IsCar(kit, p)).ToList();
            foreach (var car in cars)
            {
                var (min, max) = StreetLayout.Box(kit.Find(car.Piece)!, new Vector2(car.X, car.Z), car.Heading, car.Scale);
                (min, max) = (min + new Vector2(0.2f), max - new Vector2(0.2f));
                Assert.DoesNotContain(recipe.Clear, a => a.Only.Contains("curb_car") && min.X < a.Max.X && a.Min.X < max.X && min.Y < a.Max.Y && a.Min.Y < max.Y);
            }

            // The spot on the way in is never taken
            Assert.DoesNotContain(cars, c => MathF.Abs(c.X + 10f) < 0.5f && MathF.Abs(c.Z - 10f) < 0.5f);
        }
    }

    [Fact]
    public void A_stations_pieces_stand_on_its_ground_and_its_lamp_posts_are_lit_at_night()
    {
        var kit = Kit();
        var station = Station(groundHeight: 2.5f);
        var laid = StreetLayouts.Laid(station, GameState.CreateNew("Player"), kit);

        Assert.NotEmpty(laid.Pieces);
        Assert.All(laid.Pieces, p => Assert.Equal(2.5f, p.Y, 3));
        var night = laid.LightingFor(StreetLight.Night).Lamps;
        Assert.Equal(laid.Pieces.Count(p => p.Piece == "lamp"), night.Count - station.LightingFor(StreetLight.Night).Lamps.Count);
        Assert.All(night.TakeLast(laid.Pieces.Count(p => p.Piece == "lamp")), lamp => Assert.Equal(12.5f, lamp.Y, 3));
    }

    [Fact]
    public void A_station_with_no_layout_is_as_it_was_and_lays_nothing_into_the_save()
    {
        var station = Station(layout: null);
        var state = GameState.CreateNew("Player");
        Assert.Same(station, StreetLayouts.Laid(station, state, Kit()));
        Assert.Empty(state.Streets);
    }

    [Fact]
    public void The_save_keeps_a_stations_layout_while_the_station_lays_one_out_and_apart_from_a_street_of_its_id()
    {
        var kit = Kit();
        var station = Station(id: "corner");
        var state = GameState.CreateNew("Player");
        StreetLayouts.Laid(station, state, kit);
        var street = new StreetScene("corner", "corner", ".", new StreetSpot(0f, 0f, 0f), 3f, 0.3f, 45f, 70f, [0.3f, 0.6f, 0.7f],
            new Dictionary<StreetLight, StreetLighting>()) { Recipe = station.Recipe };
        StreetLayouts.For(state, street, kit);
        Assert.Equal(2, state.Streets.Count);

        var book = Services.Diner.DinerBook.Load(ModSet.Load(_temp.Path), []);
        Assert.False(StreetLayouts.Prune(state, [street], book, [station]));
        Assert.Equal(2, state.Streets.Count);

        // The station gone (or laying nothing out now): its layout goes, the street's stays
        Assert.True(StreetLayouts.Prune(state, [street], book, []));
        Assert.Equal("corner", Assert.Single(state.Streets).Street);
    }

    [Fact]
    public void Street_rods_gas_station_lays_out_with_the_kit_it_ships_with()
    {
        var mods = ModSet.Load(RepoPaths.Content, setting: "streetrod");
        var problems = new List<string>();
        var kit = StreetKit.Load(mods, problems);
        var stations = StationScenes.LoadAll(mods);
        var station = Assert.Single(stations, s => s.Recipe != null);
        Assert.Empty(StreetLayouts.UnknownWords([], kit, stations: stations));

        var state = GameState.CreateNew("Player");
        var laid = StreetLayouts.Laid(station, OnDay(state, 0), kit);
        Assert.Contains(laid.Pieces, p => kit.Find(p.Piece)!.Tags.Contains("house"));
        Assert.Contains(laid.Pieces, p => kit.Find(p.Piece)!.Tags.Contains("shop"));
        var days = Enumerable.Range(0, 5).Select(d => StreetLayouts.Laid(station, OnDay(state, d), kit).Pieces.Where(p => IsCar(kit, p)).Select(p => p.ToString()).ToList()).ToList();
        Assert.All(days, cars => Assert.NotEmpty(cars));
        Assert.True(days.Distinct(new SequenceComparer()).Count() > 1, "the cars change from day to day");
    }

    private sealed class SequenceComparer : IEqualityComparer<List<string>>
    {
        public bool Equals(List<string>? a, List<string>? b) => a!.SequenceEqual(b!);

        public int GetHashCode(List<string> list) => list.Count;
    }
}
