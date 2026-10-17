using System.Numerics;
using StreetCorsa.Mods;
using StreetCorsa.Services.Garage;

namespace StreetCorsa.Tests;

public class GaragePropsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Showroom(string garage) => Path.Combine(_temp.Path, "showrooms", garage, garage + ".kn5");

    private void BaseRadio()
    {
        _temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/props/radio/radio.kn5", "x");
        _temp.File($"{ModIds.BaseId}/props/radio/prop.json", """
            { "name": "Radio", "model": "radio.kn5", "places": { "garage": { "position": [2.62, 1.29, -8.6], "heading": 67 } } }
            """);
    }

    [Fact]
    public void A_prop_stands_where_its_garage_puts_it()
    {
        BaseRadio();

        var radio = GarageProps.Find(ModSet.Load(_temp.Path), "radio", Showroom("garage"))!;

        Assert.Equal("Radio", radio.Name);
        Assert.EndsWith("radio.kn5", radio.Model);
        Assert.Equal(new Vector3(2.62f, 1.29f, -8.6f), radio.Position);

        // Its own origin goes to the spot; a point a metre along its X turns with the heading
        var origin = Vector3.Transform(Vector3.Zero, radio.Placement);
        Assert.Equal(radio.Position.X, origin.X, 4);
        Assert.Equal(radio.Position.Z, origin.Z, 4);
        var ahead = Vector3.Transform(Vector3.UnitX, radio.Placement) - origin;
        Assert.Equal(1f, ahead.Length(), 4);
        Assert.Equal(0f, ahead.Y, 4);
    }

    [Fact]
    public void Another_garage_without_a_place_for_it_has_none()
    {
        BaseRadio();
        Assert.Null(GarageProps.Find(ModSet.Load(_temp.Path), "radio", Showroom("barn")));
        Assert.Null(GarageProps.Find(ModSet.Load(_temp.Path), "radio", null));
        Assert.Null(GarageProps.Find(ModSet.Load(_temp.Path), "jukebox", Showroom("garage")));
    }

    [Fact]
    public void A_mod_moves_the_radio_or_takes_it_out()
    {
        BaseRadio();
        _temp.File("moved/mod.json", """{ "id": "moved" }""");
        _temp.File("moved/props/radio/philco.kn5", "x");
        _temp.File("moved/props/radio/prop.json", """
            { "model": "philco.kn5", "places": { "garage": { "position": [1, 0.9, 2], "heading": 180 } } }
            """);

        var radio = GarageProps.Find(ModSet.Load(_temp.Path), "radio", Showroom("garage"))!;
        Assert.EndsWith("philco.kn5", radio.Model);
        Assert.Equal(new Vector3(1f, 0.9f, 2f), radio.Position);
        Assert.Equal("radio", radio.Name);

        _temp.File("moved/props/radio/prop.json", """{ "model": "philco.kn5", "places": { } }""");
        Assert.Null(GarageProps.Find(ModSet.Load(_temp.Path), "radio", Showroom("garage")));
    }

    [Fact]
    public void A_prop_without_its_model_or_with_bad_json_is_said_and_skipped()
    {
        BaseRadio();
        _temp.File("broken/mod.json", """{ "id": "broken" }""");
        _temp.File("broken/props/radio/prop.json", """{ "model": "gone.kn5", "places": { "garage": { "position": [0, 0, 0] } } }""");
        _temp.File("worse/mod.json", """{ "id": "worse" }""");
        _temp.File("worse/props/radio/prop.json", "{ not json");

        var problems = new List<string>();
        var radio = GarageProps.Find(ModSet.Load(_temp.Path), "radio", Showroom("garage"), problems);

        // The base game's still stands
        Assert.NotNull(radio);
        Assert.EndsWith("radio.kn5", radio.Model);
        Assert.Contains(problems, p => p.Contains("gone.kn5"));
        Assert.Contains(problems, p => p.Contains("could not be read"));
    }

    [Fact]
    public void The_published_radio_mod_puts_its_radio_in_the_garage_and_credits_its_maker()
    {
        var folder = Path.Combine(RepoPaths.Content, "bushradio", ContentTypes.Props, "radio");
        Assert.True(File.Exists(Path.Combine(folder, "radio.kn5")));
        Assert.True(File.Exists(Path.Combine(folder, "CREDITS.txt")));
        Assert.True(File.Exists(Path.Combine(RepoPaths.Content, "bushradio", ModSet.ManifestFileName)));

        // Whichever radio mod wins on this machine (a private one may), it stands in the garage and names its maker
        var radio = GarageProps.Find(ModSet.Load(RepoPaths.Content), "radio", Path.Combine("x", "garage", "garage.kn5"));
        Assert.NotNull(radio);
        Assert.InRange(radio.Position.Y, 0.5f, 2f);
        Assert.False(string.IsNullOrWhiteSpace(radio.Credit));
    }

    [Fact]
    public void A_prop_names_its_maker()
    {
        BaseRadio();
        _temp.File("credited/mod.json", """{ "id": "credited" }""");
        _temp.File("credited/props/radio/radio.kn5", "x");
        _temp.File("credited/props/radio/prop.json", """
            { "credit": "\"Radio\" by Someone, CC BY 4.0", "places": { "garage": { "position": [0, 1, 0] } } }
            """);

        Assert.Equal("\"Radio\" by Someone, CC BY 4.0", GarageProps.Find(ModSet.Load(_temp.Path), "radio", Showroom("garage"))!.Credit);
    }

    [Fact]
    public void A_prop_the_game_draws_itself_needs_no_model_but_a_named_one_is_used()
    {
        _temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/props/workbench/prop.json", """
            { "name": "Workbench", "places": { "garage": { "position": [3, 0, -1.75], "heading": 90 } } }
            """);

        var bench = GarageProps.Find(ModSet.Load(_temp.Path), "workbench", Showroom("garage"), builtIn: true)!;
        Assert.Equal("", bench.Model);
        Assert.Equal(new Vector3(3, 0, -1.75f), bench.Position);
        Assert.Equal(90, bench.Heading);

        // Not a prop the game draws: a missing model is a problem, and no prop
        var problems = new List<string>();
        Assert.Null(GarageProps.Find(ModSet.Load(_temp.Path), "workbench", Showroom("garage"), problems));
        Assert.Single(problems);

        // A mod's own model takes the game's place
        _temp.File($"{ModIds.BaseId}/props/workbench/bench.kn5", "x");
        _temp.File($"{ModIds.BaseId}/props/workbench/prop.json", """
            { "model": "bench.kn5", "places": { "garage": { "position": [3, 0, -1.75] } } }
            """);
        Assert.EndsWith("bench.kn5", GarageProps.Find(ModSet.Load(_temp.Path), "workbench", Showroom("garage"), builtIn: true)!.Model);
    }

    [Fact]
    public void The_base_games_workbench_stands_in_its_garage()
    {
        var bench = GarageProps.Find(ModSet.Load(RepoPaths.Content), StreetCorsa.Parts.WorkbenchLayout.PropId,
            Path.Combine(RepoPaths.BaseContent, "garages", "garage", "garage.kn5"), builtIn: true);

        Assert.NotNull(bench);
        Assert.Equal("", bench.Model);
    }
}
