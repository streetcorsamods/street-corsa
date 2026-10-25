using System.Text.Json.Nodes;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Screens.NewGame;

namespace StreetCorsa.Tests;

/// <summary>
/// The New Game screen's cards: a setting's picture and the figures of its difficulty it offers to adjust are the
/// setting's own (mod.json's <c>preview</c>, the rules' <c>difficulty.adjust</c>)
/// </summary>
public class NewGameCardsTests
{
    private static RuleBook Book(string json, List<string> problems) => RuleBook.Read(JsonNode.Parse(json)!.AsObject(), problems);

    [Fact]
    public void The_core_offers_every_figure_at_its_own_range()
    {
        var adjust = new RuleBook().Difficulty.Adjust;

        Assert.Equal(AdjustableFigure.Known.Select(k => k.Figure), adjust.Select(a => a.Figure));
        Assert.Equal((50.0, 200.0, 5.0), adjust[0].Range);
    }

    [Fact]
    public void A_setting_offers_its_own_figures_in_its_own_order_and_ranges()
    {
        var problems = new List<string>();
        var book = Book("""
            { "difficulty": { "adjust": [
                { "figure": "startMoneyMultiplier", "min": 50, "max": 500, "step": 10 },
                { "figure": "CarWearMultiplier" },
                { "figure": "nitrousMultiplier" },
                { "figure": "carWearMultiplier" },
                { "figure": "racePrizeMultiplier", "min": 200, "max": 100 } ] } }
            """, problems);

        var adjust = book.Difficulty.Adjust;
        Assert.Equal(["startMoneyMultiplier", "carWearMultiplier", "racePrizeMultiplier"], adjust.Select(a => a.Figure));
        Assert.Equal((50.0, 500.0, 10.0), adjust[0].Range);
        Assert.Equal((50.0, 200.0, 5.0), adjust[2].Range);
        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("nitrousMultiplier"));
        Assert.Contains(problems, p => p.Contains("named twice"));
        Assert.Contains(problems, p => p.Contains("no range"));
    }

    [Fact]
    public void A_book_with_its_figures_reads_back_from_a_save_without_a_problem()
    {
        var problems = new List<string>();
        var book = Book("""{ "difficulty": { "adjust": [ { "figure": "carPriceMultiplier", "max": 300 }, { "figure": "pinkSlipFrequency" } ] } }""", problems);

        var again = Book(book.ToJson(), problems);

        Assert.Empty(problems);
        Assert.Equal((50.0, 300.0, 5.0), again.Difficulty.Adjust[0].Range);
        Assert.Equal("pinkSlipFrequency", again.Difficulty.Adjust[1].Figure);
    }

    [Fact]
    public void The_editor_shows_the_settings_figures_and_a_figure_set_by_hand_makes_it_custom()
    {
        var problems = new List<string>();
        var presets = Book("""
            { "difficulty": { "adjust": [ { "figure": "carPriceMultiplier", "min": 100, "max": 150, "step": 10 }, { "figure": "raceSimulationEnabled" } ] } }
            """, problems).Difficulty;
        var editor = new GameRulesEditor(presets);

        Assert.Equal(2, editor.Figures.Count);
        var price = editor.Figure("carPriceMultiplier")!;
        Assert.True(price.IsSlider);
        // The slider reaches every preset's figure (Easy's 85%), so it never moves one by itself
        Assert.Equal(85, price.Min);
        Assert.Equal(150, price.Max);
        Assert.True(editor.Figure("raceSimulationEnabled")!.IsSwitch);
        Assert.Null(editor.Figure("opponentSkillModifier"));

        price.Value = 130;
        Assert.Equal(Difficulty.Custom, editor.Difficulty);
        Assert.Equal(1.3, editor.Build().CarPriceMultiplier, 3);
        Assert.Equal("130%", price.ValueText);

        price.Value = 115;
        // Hard's figure for car prices, but its other figures are not Hard's: still Custom
        Assert.Equal(Difficulty.Custom, editor.Difficulty);
        price.Value = 100;
        Assert.Equal(Difficulty.Normal, editor.Difficulty);

        editor.Figure("raceSimulationEnabled")!.IsOn = false;
        Assert.False(editor.Build().RaceSimulationEnabled);
        Assert.Equal(Difficulty.Custom, editor.Difficulty);

        editor.Pick(Difficulty.Hard);
        Assert.True(editor.Build().RaceSimulationEnabled);
        Assert.Equal(115, price.Value);
    }

    [Fact]
    public void The_slider_stops_on_the_settings_steps_and_the_presets_and_never_past_its_range()
    {
        var problems = new List<string>();
        var presets = Book("""{ "difficulty": { "adjust": [ { "figure": "carPriceMultiplier", "min": 100, "max": 150, "step": 10 } ] } }""", problems).Difficulty;
        var price = new GameRulesEditor(presets).Figure("carPriceMultiplier")!;

        // The setting's 100, 110 ... 150 and the presets' 85 and 115, not steps of 10 from 85
        Assert.Equal(new double[] { 85, 100, 110, 115, 120, 130, 140, 150 }, price.Ticks.ToArray());
        price.Value = 155;
        Assert.Equal(150, price.Value);
        price.Value = 104;
        Assert.Equal(100, price.Value);
        price.Value = 60;
        Assert.Equal(85, price.Value);
    }

    [Fact]
    public void A_range_the_game_would_not_play_is_the_cores()
    {
        var problems = new List<string>();
        var adjust = Book("""
            { "difficulty": { "adjust": [
                { "figure": "carWearMultiplier", "min": 0 },
                { "figure": "pinkSlipFrequency", "max": 4, "step": 1 },
                { "figure": "partPriceMultiplier", "step": 2.5 },
                { "figure": "opponentSkillModifier", "min": -3, "max": 3 } ] } }
            """, problems).Difficulty.Adjust;

        Assert.Equal((25.0, 200.0, 5.0), adjust[0].Range);
        Assert.Equal((0.0, 0.0, 0.0), adjust[1].Range);
        Assert.Equal((50.0, 200.0, 5.0), adjust[2].Range);
        Assert.Equal((-3.0, 3.0, 1.0), adjust[3].Range);
        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("choice or a switch"));
    }

    [Fact]
    public void A_settings_preview_is_a_file_in_its_own_folder()
    {
        using var temp = new TempDir();
        temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        temp.File("Content/named/mod.json", """{ "id": "named", "type": "setting", "preview": "art/card.png" }""");
        temp.File("Content/named/art/card.png", "x");
        temp.File("Content/found/mod.json", """{ "id": "found", "type": "setting" }""");
        temp.File("Content/found/preview.png", "x");
        temp.File("Content/outside/mod.json", """{ "id": "outside", "type": "setting", "preview": "../found/preview.png" }""");
        temp.File("Content/none/mod.json", """{ "id": "none", "type": "setting" }""");

        var settings = ModSet.Load(temp.Combine("Content"), everything: true).Settings.ToDictionary(m => m.Id);

        Assert.Equal(temp.Combine("Content", "named", "art", "card.png"), settings["named"].PreviewFile);
        Assert.Equal(temp.Combine("Content", "found", "preview.png"), settings["found"].PreviewFile);
        Assert.Null(settings["outside"].PreviewFile);
        Assert.Null(settings["none"].PreviewFile);
    }
}
