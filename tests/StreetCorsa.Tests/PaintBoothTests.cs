using StreetCorsa.Looks;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Navigation;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Services.PaintBooth;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Tests;

/// <summary>
/// The paint booth (batch 9, idea 2): a colour of the player's own tinted over the paint's own (CarPaint), what a car
/// painted shows in the scenes and the race, the shipped booth and its script, the job's price and time in the rules
/// </summary>
public class PaintBoothTests
{
    // ----- the colour -----

    [Fact]
    public void A_colour_reads_as_hex_and_writes_back_the_same()
    {
        Assert.Equal(new PaintColour(0x1E, 0x5A, 0xA8), PaintColour.Parse("#1E5AA8"));
        Assert.Equal(new PaintColour(0x1E, 0x5A, 0xA8), PaintColour.Parse(" 1e5aa8 "));
        Assert.Equal("#1E5AA8", PaintColour.Parse("#1e5aa8")!.Value.Hex);
        Assert.Null(PaintColour.Parse(null));
        Assert.Null(PaintColour.Parse("#12345"));
        Assert.Null(PaintColour.Parse("not a colour"));
    }

    // ----- the tint -----

    [Fact]
    public void The_paints_own_colour_becomes_the_new_one_shaded_as_it_was()
    {
        var red = PaintColour.Parse("#882120")!.Value;
        var blue = PaintColour.Parse("#1E5AA8")!.Value;
        var (r, g, b) = red.Unit;

        // The paint itself: the new colour
        var (nr, ng, nb) = CarPaint.Tint(r, g, b, red, blue);
        var (br, bg, bb) = blue.Unit;
        Assert.Equal(br, nr, 0.02f);
        Assert.Equal(bg, ng, 0.02f);
        Assert.Equal(bb, nb, 0.02f);

        // Darker in a crease: darker blue, the same hue
        var (dr, dg, db) = CarPaint.Tint(r * 0.7f, g * 0.7f, b * 0.7f, red, blue);
        Assert.True(db < nb && db > dr && db > dg);
    }

    [Fact]
    public void Another_colour_on_the_texture_is_left_as_it_is()
    {
        var red = PaintColour.Parse("#882120")!.Value;
        var blue = PaintColour.Parse("#1E5AA8")!.Value;

        // The chrome's grey, a badge's gold, the black of a trim
        foreach (var (r, g, b) in new[] { (0.8f, 0.8f, 0.82f), (0.85f, 0.7f, 0.2f), (0.05f, 0.05f, 0.05f) })
            Assert.Equal((r, g, b), CarPaint.Tint(r, g, b, red, blue));
    }

    [Fact]
    public void A_paint_of_one_colour_can_be_tinted_a_two_tone_cannot()
    {
        var one = Enumerable.Range(0, 100).Select(i => (0.53f + (i % 5) * 0.01f, 0.13f, 0.12f, 1f)).ToList();
        var (colour, share) = CarPaint.Dominant(one);
        Assert.True(share > CarPaint.MinShare, $"share {share}");
        Assert.True(colour.R > 120 && colour.G < 50);

        // Half red, half white: no colour covers most of it
        var two = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (0.53f, 0.13f, 0.12f, 1f) : (0.95f, 0.95f, 0.93f, 1f)).ToList();
        Assert.True(CarPaint.Dominant(two).Share < CarPaint.MinShare);

        // A black car's grain (its texture's shading, grey) is still black
        var black = Enumerable.Range(0, 100).Select(i => (0.01f * (i % 6), 0.01f * (i % 6), 0.012f * (i % 6), 1f)).ToList();
        Assert.True(CarPaint.Dominant(black).Share > CarPaint.MinShare);
    }

    // ----- what a painted car shows -----

    [Fact]
    public void A_painted_car_shows_its_paint_and_a_race_sets_its_textures()
    {
        var blue = PaintColour.Parse("#1E5AA8");
        var shown = ShownSet.Of(null, paint: blue);
        Assert.NotNull(shown);
        Assert.Equal(blue, shown!.Paint);
        Assert.NotEqual(shown, ShownSet.Of(null, paint: PaintColour.Parse("#9E1B1B")));
        Assert.Null(ShownSet.Of(null, paint: null));

        // The parts view's ghost keeps its colour
        Assert.Equal(blue, shown.WithoutExtras()!.Paint);

        var raced = shown.WithPaintFiles([new ShownPaint(new ShownModel(Path.Combine(CarPaint.Folder, "car_1.png"), "x"), "67_skin.png")]);
        var keys = RaceStartState.ShownKeys(0, raced).ToList();
        Assert.Contains(keys, k => k.Key == "CAR_0_PAINT_1" && k.Value == Path.Combine("paint", "car_1.png"));
        Assert.Contains(keys, k => k.Key == "CAR_0_PAINT_1_TEXTURE" && k.Value == "67_skin.png");
        Assert.Contains(raced.Models, m => m.Name.StartsWith("paint"));

        // The wear layer's model kept with the paint, and the other way round
        var both = raced.WithLayer(new ShownModel("looks\\a.kn5", "y"));
        Assert.Single(both.PaintFiles);
        Assert.Equal(blue, both.Paint);
    }

    // ----- the booth -----

    [Fact]
    public void The_shipped_booth_loads_with_its_way_in_its_painter_and_its_light()
    {
        var booth = PaintBooths.Load(Path.Combine(RepoPaths.BaseContent, ContentTypes.Booths, "booth"));

        Assert.NotNull(booth);
        Assert.True(File.Exists(booth.Kn5));
        Assert.True(File.Exists(booth.PainterKn5));
        Assert.True(booth.Arrive.Count >= 2);
        Assert.Contains("arrive", booth.ScriptMarks.Paths.Keys);
        Assert.NotNull(booth.ScriptMarks.Stand(booth.StandMark, "arrive"));

        // Indoors: every hour has the booth's one light
        foreach (var light in Enum.GetValues<StreetLight>()) Assert.True(booth.LightingFor(light).Brightness > 0.5f);
        Assert.True(booth.Camera.Min.X < booth.Camera.Max.X && booth.Camera.Min.Z < booth.Camera.Max.Z);
        Assert.InRange(booth.Round.Strokes, 1, 20);
    }

    [Fact]
    public void The_car_rolls_into_the_booth_by_the_base_games_script()
    {
        var book = ScriptBook.Load(ModSet.Load(RepoPaths.Content), []);
        var script = book.For(ScriptTriggers.BoothArrive, new ScriptFacts(new DateTime(1965, 6, 1, 14, 0, 0), StreetLight.Day, false), null);

        Assert.NotNull(script);
        Assert.Contains(ScriptTriggers.BoothArrive, ScriptTriggers.All);
    }

    [Fact]
    public void The_booth_is_a_screen_of_the_flow_with_its_tile_in_the_garage()
    {
        Assert.Contains(ScreenKinds.PaintBooth, ScreenKinds.All);
        Assert.Equal("paintBooth", ScreenKinds.SystemOf(ScreenKinds.PaintBooth));
        Assert.True(SystemRules.IsName("paintBooth"));
        Assert.False(new SystemRules { PaintBooth = false }.IsOn("paintbooth"));
    }

    [Fact]
    public void The_painter_sprays_no_rusty_or_dented_body()
    {
        var car = new StreetCorsa.Models.GameState.Car("car_a");
        Assert.Empty(RepairShop.BeforePaint(car));

        // Worn paint is what the paint job is for: not in the way
        car.PaintWear = 0.8;
        Assert.Empty(RepairShop.BeforePaint(car));

        car.Rust = 0.3;
        Assert.Equal([RepairShop.Rust], RepairShop.BeforePaint(car));

        car.BodyDamageKmh = [12, 0, 0, 0];
        Assert.Equal([RepairShop.Rust, RepairShop.Body], RepairShop.BeforePaint(car));

        // The repair shop's jobs put it right
        foreach (var job in RepairShop.Jobs(car, null).Where(j => j.Id is RepairShop.Rust or RepairShop.Body)) job.Apply();
        Assert.Empty(RepairShop.BeforePaint(car));
    }

    [Fact]
    public void A_paint_job_costs_money_and_half_a_day()
    {
        var rules = new RuleBook();
        Assert.True(rules.Repairs.ColourChangeCost > 0);
        Assert.True(rules.Repairs.CustomColourCost > 0);
        Assert.Equal(240, rules.Time.Of(GameAction.PaintJob));
    }
}
