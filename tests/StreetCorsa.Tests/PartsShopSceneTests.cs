using System.Numerics;
using System.Text.Json.Nodes;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// The part shop in 3D and its clerk (step 28, idea 7): the shipped room and its places, the stock stood in them, the
/// clerk's search read out of the AI's JSON, and a talk at the counter where the game searches and the AI only voices it
/// </summary>
public class PartsShopSceneTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AiPrompts BasePrompts() => AiPrompts.Load(RepoPaths.StreetRods);

    // ----- the room -----

    [Fact]
    public void The_shipped_shop_loads_with_its_places_a_look_for_every_kind_and_a_light_for_every_hour()
    {
        var scene = ShopScenes.Load(Path.Combine(RepoPaths.SettingContent, ContentTypes.ShopScenes, "shop"));

        Assert.NotNull(scene);
        Assert.True(File.Exists(scene.Kn5));
        Assert.Contains(scene.Places, p => p.Holds == ShopHolds.Engine);
        Assert.Contains(scene.Places, p => p.Holds == ShopHolds.Wheel);
        Assert.True(scene.Places.Count(p => p.Holds == ShopHolds.Shelf) >= 40);
        // Every kind but the machine shop, which has a scene of its own
        Assert.Empty(scene.Kinds);
        foreach (var kind in Enum.GetValues<PartShopKind>().Where(k => k != PartShopKind.Machine).Select(k => k.ToString().ToLowerInvariant()))
            Assert.Contains(kind, scene.LookFor(null, kind)!.Kinds);
        foreach (var light in Enum.GetValues<StreetLight>())
            Assert.True(scene.Lights.ContainsKey(light), $"no {light} light");
        Assert.True(scene.Camera.Fov > 0f);
        Assert.NotNull(scene.Room);
    }

    [Fact]
    public void A_shop_dressed_wears_its_look_its_sign_and_its_tubes_colour()
    {
        var scene = ShopScenes.Load(Path.Combine(RepoPaths.SettingContent, ContentTypes.ShopScenes, "shop"))!;
        var look = scene.LookFor(null, "junkyard")!;
        var dressing = scene.Dress(look, StreetLight.Night);

        Assert.Equal("sign_junkyard.jpg", dressing.Pictures["sh_sign"]);
        Assert.All(dressing.Lighting.Lamps, lamp => Assert.Equal(look.LampColor, lamp.Color));
        Assert.True(dressing.Glow[ShopScene.WindowMaterial] < scene.Dress(look, StreetLight.Day).Glow[ShopScene.WindowMaterial]);

        // A shop's own look by name goes before its kind's
        Assert.Equal("tire_shop", scene.LookFor("tire_shop", "speed")!.Id);
    }

    [Fact]
    public void The_machine_shop_has_a_scene_of_its_own_with_its_machines_places()
    {
        var scene = ShopScenes.Load(Path.Combine(RepoPaths.SettingContent, ContentTypes.ShopScenes, "machine_shop"));

        Assert.NotNull(scene);
        Assert.True(File.Exists(scene.Kn5));
        Assert.Equal(["machine"], scene.Kinds);
        Assert.Contains(scene.Places, p => p.Holds == PartGroups.Blocks);
        Assert.Contains(scene.Places, p => p.Holds == "crankshafts");
        Assert.Contains(scene.Places, p => p.Holds == ShopHolds.Engine);
        Assert.Equal("machine_shop", scene.LookFor(null, "machine")!.Id);
        foreach (var light in Enum.GetValues<StreetLight>())
            Assert.True(scene.Lights.ContainsKey(light), $"no {light} light");

        // The part shops keep theirs: a scene for a kind is that kind's alone
        var shop = ShopScenes.Load(Path.Combine(RepoPaths.SettingContent, ContentTypes.ShopScenes, "shop"))!;
        Assert.Same(scene, ShopScenes.Pick([shop, scene], "machine"));
        Assert.Same(shop, ShopScenes.Pick([shop, scene], "speed"));
        Assert.Same(shop, ShopScenes.Pick([shop, scene], null));
        Assert.Same(shop, ShopScenes.Pick([shop], "machine"));
    }

    [Fact]
    public void A_place_for_a_group_takes_a_part_of_it_first_and_nothing_else()
    {
        var places = new[] { Place(ShopHolds.Shelf, width: 2f), Place("crankshafts", width: 2f, depth: 0.4f), Place(ShopHolds.Shelf, width: 2f) };
        var crank = Item("crank", "crankshafts", 0.7f, 0.18f, 0.18f);
        var head = Item("head", "cylinder_heads", 0.65f, 0.22f, 0.33f);

        var standing = ShopStockLayout.Place([head, crank], places);

        Assert.Equal("crankshafts", standing.Single(s => s.Item.Key == "crank").Place.Holds);
        Assert.Equal(ShopHolds.Shelf, standing.Single(s => s.Item.Key == "head").Place.Holds);
    }

    [Fact]
    public void A_second_stand_stays_bare_when_the_first_has_an_engine()
    {
        var places = new[] { Place(ShopHolds.Engine, 1.7f, 1f, 1.3f), Place(ShopHolds.Engine, 1.7f, 1f, 1.3f), Place(ShopHolds.Shelf, 1f, 1f, 1f) };
        var engine = Item("engine", PartGroups.Blocks, 0.7f, 0.7f, 0.9f);
        var pistons = Item("pistons", "pistons", 0.5f, 0.25f, 0.5f);

        var standing = ShopStockLayout.Place([engine, pistons], places);

        Assert.Equal(ShopHolds.Shelf, standing.Single(s => s.Item.Key == "pistons").Place.Holds);
    }

    // ----- the stock in its places -----

    private static ShopPlace Place(string holds, float width = 0.85f, float depth = 0.46f, float height = 0.45f, float heading = 0f) =>
        new(1f, 0.5f, -2f, width, depth, height, heading, holds);

    private static ShopShownItem Item(string key, string group, float x, float y, float z) =>
        new(key, group, new Vector3(-x / 2f, 0.1f, -z / 2f), new Vector3(x / 2f, 0.1f + y, z / 2f));

    [Fact]
    public void An_engine_goes_on_the_stand_a_tyre_in_the_rack_and_a_small_part_on_a_shelf()
    {
        var places = new[] { Place(ShopHolds.Shelf), Place(ShopHolds.Wheel, 0.72f, 0.5f, 0.95f), Place(ShopHolds.Engine, 1f, 0.9f, 1.1f) };
        var items = new[]
        {
            Item("carb", "carburetors", 0.3f, 0.2f, 0.3f),
            Item("tyre", PartGroups.Tyres, 0.25f, 0.7f, 0.7f),
            Item("block", PartGroups.Blocks, 0.7f, 0.6f, 0.8f)
        };

        var standing = ShopStockLayout.Place(items, places).ToDictionary(s => s.Item.Key, s => s.Place.Holds);

        Assert.Equal(ShopHolds.Engine, standing["block"]);
        Assert.Equal(ShopHolds.Wheel, standing["tyre"]);
        Assert.Equal(ShopHolds.Shelf, standing["carb"]);
    }

    [Fact]
    public void A_part_too_big_for_every_place_is_left_out_and_one_that_fits_turned_is_turned()
    {
        var places = new[] { Place(ShopHolds.Shelf, 0.85f, 0.46f, 0.45f) };

        Assert.Empty(ShopStockLayout.Place([Item("exhaust", "exhaust", 2f, 0.3f, 0.3f)], places));

        // Longer front to back than the shelf is deep, but not wider than it is wide: a quarter turn
        Assert.Equal(1, ShopStockLayout.Turn(new Vector3(0.4f, 0.2f, 0.8f), places[0]));
        Assert.Equal(0, ShopStockLayout.Turn(new Vector3(0.8f, 0.2f, 0.4f), places[0]));
        Assert.Null(ShopStockLayout.Turn(new Vector3(0.4f, 0.6f, 0.4f), places[0]));
    }

    [Fact]
    public void A_part_stands_with_its_bottom_on_its_place_and_its_middle_over_the_places_middle()
    {
        var place = Place(ShopHolds.Shelf, heading: 90f);
        var item = Item("carb", "carburetors", 0.3f, 0.2f, 0.4f) with { Min = new Vector3(0.1f, 0.2f, -0.1f), Max = new Vector3(0.4f, 0.4f, 0.3f) };

        var world = ShopStockLayout.World(item, place, 0);
        var bottomMiddle = Vector3.Transform(new Vector3(0.25f, 0.2f, 0.1f), world);

        Assert.Equal(place.X, bottomMiddle.X, 3);
        Assert.Equal(place.Y, bottomMiddle.Y, 3);
        Assert.Equal(place.Z, bottomMiddle.Z, 3);
    }

    [Fact]
    public void A_shop_with_no_engine_puts_its_biggest_part_on_the_stand()
    {
        var places = new[] { Place(ShopHolds.Engine, 1f, 0.9f, 1.1f), Place(ShopHolds.Shelf), Place(ShopHolds.Shelf) };
        var standing = ShopStockLayout.Place([Item("small", "carburetors", 0.2f, 0.2f, 0.2f), Item("big", "intakes", 0.6f, 0.3f, 0.4f)], places);

        Assert.Equal("big", standing.Single(s => s.Place.Holds == ShopHolds.Engine).Item.Key);
        Assert.Equal(2, standing.Count);
    }

    // ----- the clerk's search -----

    private static readonly string[] Kinds = ["Carburetors", "Camshafts", "Tyres"];

    [Fact]
    public void The_search_is_read_out_of_the_AIs_JSON()
    {
        var search = ClerkSearch.Parse("""Sure: {"asks": true, "kind": "carburetors", "fits": true, "maxPrice": 150, "words": "Holley", "want": "cheap"}""", Kinds);

        Assert.Equal(new ClerkSearch(true, "Carburetors", true, 150m, "Holley", ClerkSearch.Cheap), search);
    }

    [Theory]
    [InlineData("""{"asks": true, "kind": "any", "fits": false, "maxPrice": 0, "words": "", "want": "any"}""")]
    [InlineData("""{"asks": true, "kind": "Blowers", "maxPrice": -5, "want": "fast"}""")]
    [InlineData("""{"asks": "true", "kind": "", "fits": "no", "maxPrice": "", "words": "any"}""")]
    [InlineData("""{"asks": true, "kind": "any", "maxPrice": 1e30}""")]
    public void A_kind_the_shop_lacks_no_limit_and_blanks_are_no_part_of_the_search(string json)
    {
        Assert.Equal(new ClerkSearch(true, null, false, null, null, null), ClerkSearch.Parse(json, Kinds));
    }

    [Fact]
    public void Anything_read_out_of_the_ask_makes_it_one_whatever_the_AI_says()
    {
        Assert.True(ClerkSearch.Parse("""{"asks": false, "kind": "Camshafts", "fits": false, "maxPrice": 0, "words": "", "want": "any"}""", Kinds)!.Asks);
        Assert.True(ClerkSearch.Parse("""{"asks": false, "kind": "any", "maxPrice": 50, "want": "cheap"}""", Kinds)!.Asks);
        Assert.False(ClerkSearch.Parse("""{"asks": false, "kind": "any", "fits": true, "maxPrice": 0, "words": "", "want": "any"}""", Kinds)!.Asks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Sorry, I can't help with that.")]
    [InlineData("{not json}")]
    public void An_answer_with_no_object_is_no_search(string? answer)
    {
        Assert.Null(ClerkSearch.Parse(answer, Kinds));
    }

    [Fact]
    public void The_schema_holds_the_AI_to_the_kinds_the_shop_has()
    {
        var kinds = ClerkSearch.Schema(Kinds)["properties"]!["kind"]!["enum"]!.AsArray().Select(k => k!.GetValue<string>()).ToList();

        Assert.Equal([ClerkSearch.AnyKind, .. Kinds], kinds);
    }

    // ----- a talk at the counter -----

    private sealed class FakeCounter : IShopCounter
    {
        public List<ClerkSearch> Searches { get; } = [];

        /// <summary>What the last search returned</summary>
        public ClerkFindings? Last { get; private set; }

        public IReadOnlyList<string> Kinds { get; } = PartsShopSceneTests.Kinds;

        public IEnumerable<string> Facts() => ["You work at Al's Speed Shop."];

        public Task<ClerkFindings> FindAsync(ClerkSearch search, CancellationToken cancellationToken)
        {
            Searches.Add(search);
            Last = new ClerkFindings(search, 7,
            [
                new ClerkPick("holley_2bbl", "Holley 2-barrel", "Carburetors", 95m, true, 1, 180, 192),
                new ClerkPick("carter_afb", "Carter AFB", "Carburetors", 140m, false, 3)
            ]);
            return Task.FromResult(Last);
        }

        public ClerkFindings Recheck(ClerkFindings found) => found with { Picks = [.. found.Picks.Where(p => p.Id != "holley_2bbl")] };
    }

    private static GameState Game()
    {
        var game = new GameState { SaveName = "test" };
        game.Player.Name = "Kid";
        return game;
    }

    [Fact]
    public async Task The_clerk_searches_the_stock_the_game_finds_and_the_AI_is_told_only_what_was_found()
    {
        var counter = new FakeCounter();
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "clerk_search"
            ? """{"asks": true, "kind": "Carburetors", "fits": true, "maxPrice": 150, "words": "", "want": "power"}"""
            : "That Holley's your carb, friend."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => new Rules.TalkRules());
        var talk = desk.ForShop(Game(), "al", null, counter);

        await talk.OpenAsync(Ct);
        var said = await talk.SayAsync("got a carb for my car, under 150?", Ct);

        Assert.Equal(PersonKind.Clerk, talk.Person.Kind);
        Assert.Equal("chat_clerk", ai.Chats[^1].Use);
        Assert.Equal("That Holley's your carb, friend.", said);

        // Asked of the AI as JSON, held to the shop's kinds; searched by the game
        Assert.Equal("clerk_search", ai.Jsons.Single().Use);
        Assert.Equal("got a carb for my car, under 150?", ai.Jsons.Single().Values["said"]);
        Assert.Equal(new ClerkSearch(true, "Carburetors", true, 150m, null, ClerkSearch.Power), counter.Searches.Single());

        // The AI is told the picks, their prices and what the dyno says, as facts
        var facts = (string)ai.Chats[^1].Values["facts"]!;
        Assert.Contains("You work at Al's Speed Shop.", facts);
        Assert.Contains("Holley 2-barrel", facts);
        Assert.Contains("from 180 hp to 192 hp", facts);
        Assert.Contains(GameText.Say("chat.fact.stock.no_fit").TrimStart(','), facts);
        Assert.Same(counter.Last, talk.Found);
        Assert.Equal(7, talk.Found!.Matching);
    }

    [Fact]
    public async Task Small_talk_at_the_counter_searches_nothing_and_a_clerk_naming_a_price_of_no_part_is_asked_again()
    {
        var counter = new FakeCounter();
        var answers = new Queue<string>(["""{"asks": true, "kind": "any", "fits": false, "maxPrice": 0, "words": "", "want": "any"}""",
            "The Holley's $60 to you.", "The Holley's $95.",
            """{"asks": false, "kind": "any", "fits": false, "maxPrice": 0, "words": "", "want": "any"}""", "Hot one today, huh?"]);
        var ai = new FakeAiText((_, _) => Task.FromResult<string?>(answers.Dequeue()));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => new Rules.TalkRules());
        var talk = desk.ForShop(Game(), "al", null, counter);

        Assert.Equal("The Holley's $95.", await talk.SayAsync("what've you got?", Ct));
        Assert.Contains(GameText.Say("chat.fact.shop_price_warning"), (string)ai.Chats[^1].Values["facts"]!);

        Assert.Equal("Hot one today, huh?", await talk.SayAsync("how's business?", Ct));
        Assert.Single(counter.Searches);
        Assert.NotNull(talk.Found);
    }

    [Fact]
    public async Task An_ask_for_what_fits_searches_what_fits_whatever_the_AI_read()
    {
        var counter = new FakeCounter();
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "clerk_search"
            ? """{"asks": false, "kind": "any", "fits": false, "maxPrice": 0, "words": "", "want": "any"}"""
            : "Sure thing."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => new Rules.TalkRules());
        var talk = desk.ForShop(Game(), "al", null, counter);

        await talk.SayAsync("anything that bolts on?", Ct);

        Assert.True(counter.Searches.Single().Fits);
    }

    [Theory]
    [InlineData("go on, tell me about the drag strip")]
    [InlineData("what goes on round here at night?")]
    public async Task Small_talk_with_go_on_in_it_is_no_ask_for_parts(string said)
    {
        var counter = new FakeCounter();
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "clerk_search"
            ? """{"asks": false, "kind": "any", "fits": false, "maxPrice": 0, "words": "", "want": "any"}"""
            : "Friday nights, out on the old airstrip."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => new Rules.TalkRules());
        var talk = desk.ForShop(Game(), "al", null, counter);

        await talk.SayAsync(said, Ct);

        Assert.Empty(counter.Searches);
    }

    [Fact]
    public async Task After_a_deal_the_clerk_talks_of_the_picks_as_they_are_now()
    {
        var counter = new FakeCounter();
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "clerk_search"
            ? """{"asks": true, "kind": "Carburetors", "fits": false, "maxPrice": 0, "words": "", "want": "any"}"""
            : "Take the Holley."));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => new Rules.TalkRules());
        var talk = desk.ForShop(Game(), "al", null, counter);
        await talk.SayAsync("got a carb?", Ct);

        // The Holley bought, the last one: the counter says what is left
        talk.StockChanged();

        Assert.Equal(["carter_afb"], talk.Found!.Picks.Select(p => p.Id));
    }

    [Fact]
    public void A_name_asked_for_that_no_part_has_is_told_to_the_clerk_and_not_said_as_the_ask()
    {
        var found = new ClerkFindings(ClerkSearch.Anything, 3, [new ClerkPick("cam", "Mopar cam", "Camshafts", 100m, true, 1)], NoName: "Edelbrock");
        var facts = TalkFacts.Stock(found).ToList();

        Assert.Contains(GameText.Say("chat.fact.stock.no_name", ("words", "Edelbrock")), facts);
        Assert.DoesNotContain(facts, f => f.Contains(GameText.Say("chat.fact.stock.ask.words", ("words", "Edelbrock"))));
    }

    [Fact]
    public void A_clerk_remembers_as_long_as_a_lots_salesman()
    {
        var rules = new Rules.TalkRules();

        Assert.Equal(rules.ForgetDaysSalesman, TalkPeople.ForgetDays(PersonKind.Clerk, rules));
    }

    [Fact]
    public void When_no_pick_fits_the_clerk_is_told_so_plainly()
    {
        var none = new ClerkFindings(ClerkSearch.Anything, 1, [new ClerkPick("cam", "Mopar cam", "Camshafts", 100m, false, 1)]);
        var one = new ClerkFindings(ClerkSearch.Anything, 1, [new ClerkPick("cam", "Mopar cam", "Camshafts", 100m, true, 1)]);

        Assert.Contains(GameText.Say("chat.fact.stock.none_fit"), TalkFacts.Stock(none));
        Assert.DoesNotContain(GameText.Say("chat.fact.stock.none_fit"), TalkFacts.Stock(one));
    }

    [Fact]
    public async Task With_no_search_read_the_clerk_still_answers_from_the_shop()
    {
        var counter = new FakeCounter();
        var ai = new FakeAiText((use, _) => Task.FromResult<string?>(use == "clerk_search" ? "I'd say carburetors." : "What are you driving?"));
        var desk = new TalkDesk(ai, prompts: BasePrompts, rules: () => new Rules.TalkRules());
        var talk = desk.ForShop(Game(), "al", null, counter);

        Assert.Equal("What are you driving?", await talk.SayAsync("carbs?", Ct));
        Assert.Empty(counter.Searches);
        Assert.Contains(GameText.Say("chat.fact.stock.not_asked"), (string)ai.Chats[^1].Values["facts"]!);
    }

    [Fact]
    public void A_clerk_is_one_per_shop_drawn_from_the_clerks_names_with_a_clerks_temperament_unless_the_shop_names_them()
    {
        var game = Game();
        var drawn = TalkPeople.Clerk(game, "al", prompts: BasePrompts());
        Assert.Same(drawn, TalkPeople.Clerk(game, "al", prompts: BasePrompts()));
        Assert.Contains(drawn.Name, GameText.Installed.Lines("names.clerks.men").Concat(GameText.Installed.Lines("names.clerks.women")));
        Assert.True(BasePrompts().TemperamentOf(drawn.Temperament)!.Fits("clerk", 50));

        var named = TalkPeople.Clerk(game, "bob", new DealerSalesman { Name = "Big Bob", Age = 61, Temperament = "old_mechanic" }, BasePrompts());
        Assert.Equal(("Big Bob", 61, "old_mechanic"), (named.Name, named.Age, named.Temperament));
    }

    [Fact]
    public void The_stock_told_says_the_ask_as_the_game_read_it()
    {
        var asked = TalkFacts.Asked(new ClerkSearch(true, "Carburetors", true, 150m, "Holley", ClerkSearch.Cheap));

        Assert.Contains("Carburetors", asked);
        Assert.Contains("Holley", asked);
        Assert.Contains(GameText.Say("chat.fact.stock.ask.cheap"), asked);
        Assert.Contains("150", asked);
        Assert.Contains(GameText.Say("chat.fact.stock.nothing", ("asked", TalkFacts.Asked(ClerkSearch.Anything))),
            TalkFacts.Stock(new ClerkFindings(ClerkSearch.Anything, 0, [])));
    }

    // ----- the AI's JSON on the wire -----

    [Theory]
    [InlineData(AiProviders.Ollama)]
    [InlineData(AiProviders.OpenAi)]
    [InlineData(AiProviders.Anthropic)]
    public async Task Ollama_and_OpenAI_hold_a_JSON_answer_to_its_schema_and_Anthropic_has_only_the_prompt(string kind)
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json(
            """{ "message": { "content": "{}" }, "choices": [ { "message": { "content": "{}" } } ], "content": [ { "type": "text", "text": "{}" } ] }""")));
        var provider = AiProviders.Create(http.Client(), kind, null, "model", "key");
        var schema = ClerkSearch.Schema(Kinds);

        await provider.CompleteAsync(new AiTextRequest("system", "prompt", 100, 0.1, Format: schema), Ct);

        var json = Assert.Single(http.Sent).Json!;
        switch (kind)
        {
            case AiProviders.Ollama:
                Assert.Equal("object", json["format"]!["type"]!.GetValue<string>());
                break;
            case AiProviders.OpenAi:
                Assert.Equal("json_schema", json["response_format"]!["type"]!.GetValue<string>());
                Assert.Equal("object", json["response_format"]!["json_schema"]!["schema"]!["type"]!.GetValue<string>());
                break;
            default:
                Assert.Null(json["format"]);
                Assert.Null(json["response_format"]);
                break;
        }
    }

    [Fact]
    public async Task A_JSON_answer_comes_back_as_written_not_cut_to_a_line()
    {
        const string written = "{\n  \"asks\": true,\n  \"kind\": \"any\"\n}";
        var body = new JsonObject { ["message"] = new JsonObject { ["content"] = written } }.ToJsonString();
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json(body)));
        var settings = new Services.Settings.AiSettings { Enabled = true, TextProvider = AiProviders.Ollama, Model = "model" };
        var text = new AiTextService(() => settings, http.Client(), BasePrompts);

        var answer = await text.JsonAsync("clerk_search", new TextValues { ["said"] = "hi", ["talk"] = "", ["kinds"] = "", ["player"] = "Kid" },
            ClerkSearch.Schema(Kinds), Ct);

        Assert.NotNull(ClerkSearch.Parse(answer, Kinds));
        Assert.Contains('\n', answer!);
        Assert.NotNull(Assert.Single(http.Sent).Json!["format"]);
    }
}
