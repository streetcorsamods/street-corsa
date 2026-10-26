using System.Net;
using System.Net.Http;
using System.Text.Json;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Services.Ai;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-25 audit's AI, talk and scripting findings: a mod's null entries in its ai files, the picture's size, the
/// portraits' folder, redirects and the key, the answer caps, what goes into the log, the shows waited on, the AI's rules
/// held to their bounds, a scene's paths, work for a game no longer in play, and the clerk's and machinist's facts.
/// </summary>
public class Audit1025BTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ----- a mod's ai files with nulls in them -----

    private static AiPrompts LoadAi(TempDir temp, params (string File, string Json)[] files)
    {
        temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        foreach (var (file, json) in files) temp.File($"Content/streetcorsa/ai/{file}", json);
        return AiPrompts.Load(ModSet.Load(temp.Combine("Content")));
    }

    [Fact]
    public void Null_entries_in_a_mods_ai_files_are_left_out_and_the_rest_reads()
    {
        using var temp = new TempDir();
        var prompts = LoadAi(temp,
            ("prompts.json", """
                { "persona": "You are a racer.", "prompts": {
                    "talk": { "prompt": "Say {line}", "examples": null },
                    "news": { "prompt": "Write {headline}", "examples": [ null, { "player": null, "answer": "Sure." } ] } } }
                """),
            ("people.json", """{ "temperaments": { "calm": { "words": "Cool-headed.", "for": null } } }"""),
            ("lore.json", """{ "lore": { "gone": { "lines": null }, "town": { "lines": [ "The gas is cheap.", null ], "for": null } } }"""),
            ("ollama.json", """{ "name": "sc", "from": "llama3.1:8b", "parameters": null, "examples": null }"""),
            ("comfy_portrait.json", """{ "prompt": "{look}", "workflow": null }"""),
            ("comfy_car.json", """
                { "prompt": "{look}", "fill": { "positive": null, "seed": { "node": null, "input": "seed" } },
                  "workflow": { "1": { "inputs": { "text": "" }, "class_type": "CLIPTextEncode" } } }
                """));

        Assert.Empty(prompts.Prompt("talk")!.Examples);
        var example = Assert.Single(prompts.Prompt("news")!.Examples);
        Assert.Equal((string.Empty, "Sure."), (example.Player, example.Answer));

        // "for": null is everybody, as no "for" is
        var calm = Assert.Single(prompts.Temperaments);
        Assert.True(calm.Fits("rival", 50));
        var town = Assert.Single(prompts.Lore);
        Assert.Equal("town", town.Id);
        Assert.True(town.Fits("seller", 1960));
        Assert.Equal(["The gas is cheap."], town.Lines);

        Assert.NotNull(prompts.Ollama!.Parameters);
        Assert.Empty(prompts.Ollama.Examples);

        Assert.Null(prompts.Workflow(AiWriter.PortraitUse));
        Assert.Contains(prompts.Problems, p => p.Contains("comfy_portrait.json") && p.Contains("no \"workflow\""));
        var car = prompts.Workflow("car")!;
        Assert.Empty(car.Fill);
        Assert.Equal(2, prompts.Problems.Count(p => p.Contains("names no node or no input")));

        // The graph is made all the same, with nothing filled
        Assert.NotNull(new ComfyImageService(() => new AiSettings(), new HttpClient()).Graph(car, new TextValues(), 1));
    }

    [Theory]
    [InlineData(100_000, 30, 2048, 64)]
    [InlineData(515, 1023, 512, 1016)]
    [InlineData(512, 768, 512, 768)]
    public void A_workflows_picture_size_is_held_to_what_comfyui_and_the_portraits_can_take(int width, int height, int keptWidth, int keptHeight)
    {
        using var temp = new TempDir();
        var prompts = LoadAi(temp, ("comfy_portrait.json", $$"""
            { "prompt": "{look}", "width": {{width}}, "height": {{height}}, "fill": { "width": { "node": "1", "input": "width" } },
              "workflow": { "1": { "inputs": { "width": 0 }, "class_type": "EmptyLatentImage" } } }
            """));

        var workflow = prompts.Workflow(AiWriter.PortraitUse)!;
        Assert.Equal((keptWidth, keptHeight), (workflow.Width, workflow.Height));
        Assert.Equal(keptWidth != width || keptHeight != height, prompts.Problems.Any(p => p.Contains("a side in steps of 8")));
        Assert.Equal(keptWidth, new ComfyImageService(() => new AiSettings(), new HttpClient()).Graph(workflow, new TextValues(), 1)["1"]!["inputs"]!["width"]!.GetValue<int>());
    }

    // ----- the portraits' folder -----

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("save. . ")]
    [InlineData("a/../..")]
    public void A_saves_portraits_stay_in_a_folder_of_their_own_under_the_portraits(string saveName)
    {
        using var temp = new TempDir();
        var writer = new AiWriter(new FakeAiText((_, _) => Task.FromResult<string?>(null)), new ComfyImageService(() => new AiSettings(), new HttpClient()),
            () => null, portraitsRoot: temp.Path, rules: () => new AiRules());

        var file = writer.PortraitFile(saveName, Guid.NewGuid());
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;

        Assert.Equal(Path.GetFullPath(temp.Path), Path.GetDirectoryName(folder));
        Assert.NotEqual("CON", Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase);
        Assert.False(Path.GetFileName(folder).EndsWith('.'));
    }

    // ----- redirects, the key and the answers' caps -----

    [Fact]
    public void The_games_http_client_follows_no_redirect()
    {
        using var handler = AiHttp.NewHandler();
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task A_redirect_is_an_answer_the_game_cannot_use_and_the_key_goes_nowhere_else()
    {
        var http = new FakeHttp((_, _) =>
        {
            var moved = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            moved.Headers.Location = new Uri("http://user:secret@evil.example.com:8080/steal?key=1");
            return Task.FromResult(moved);
        });
        var provider = AiProviders.Create(http.Client(), AiProviders.Anthropic, null, "m", "sk-ant-test");

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.CompleteAsync(new AiTextRequest("s", "p", 50, 0.7), Ct));

        Assert.Single(http.Sent);
        Assert.Equal("api.anthropic.com", http.Sent[0].Uri.Host);
        Assert.Contains("http://evil.example.com:8080", error.Message);
        Assert.DoesNotContain("secret", error.Message);
        Assert.DoesNotContain("steal", error.Message);
    }

    [Fact]
    public async Task A_comfyui_that_redirects_paints_nothing_and_is_not_rested_as_out_of_reach()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://elsewhere:8188/") } }));
        var comfy = new ComfyImageService(() => new AiSettings { ComfyEnabled = true }, http.Client(), () => TestAi.Prompts);

        Assert.Null(await comfy.PaintAsync(AiWriter.PortraitUse, new TextValues(), Ct));
        Assert.False((await comfy.TestAsync(null, Ct)).Ok);
        Assert.True(comfy.IsAvailable);
    }

    [Fact]
    public async Task A_body_with_no_length_that_grows_past_the_most_is_not_read()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Unseekable(new byte[3000])) };
        Assert.Null(response.Content.Headers.ContentLength);

        await Assert.ThrowsAsync<AiProviderException>(() => AiHttp.ReadBytesAsync(response, 2048, Ct));

        using var small = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Unseekable(new byte[1000])) };
        Assert.Equal(1000, (await AiHttp.ReadBytesAsync(small, 2048, Ct)).Length);
    }

    [Fact]
    public async Task A_content_length_over_the_most_is_not_read_at_all()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) };
        response.Content.Headers.ContentLength = 1_000_000;

        await Assert.ThrowsAsync<AiProviderException>(() => AiHttp.ReadBytesAsync(response, 2048, Ct));
    }

    [Theory]
    [InlineData("12.5")]
    [InlineData("99999999999")]
    public async Task An_ollama_token_count_that_is_no_whole_number_keeps_the_answer(string count)
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json($$"""{ "message": { "content": "Hey, kid." }, "done": true, "prompt_eval_count": {{count}} }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.Ollama, null, "m", null);

        Assert.Equal("Hey, kid.", await provider.CompleteAsync(new AiTextRequest("s", "p", 50, 0.7), Ct));
    }

    [Fact]
    public async Task A_json_answer_longer_than_a_search_may_be_is_not_read()
    {
        var huge = "{ \"kind\": \"" + new string('a', AiTextService.MostJsonAnswer) + "\" }";
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json(JsonSerializer.Serialize(new { message = new { content = huge }, done = true }))));
        var service = new AiTextService(() => new AiSettings { Enabled = true, Model = "m" }, http.Client(), () => TestAi.Prompts,
            () => new AiRules { MinSecondsBetween = 0 });

        Assert.Null(await service.JsonAsync("clerk_search", new TextValues(), new System.Text.Json.Nodes.JsonObject(), Ct));
        Assert.Single(http.Sent);
    }

    [Fact]
    public async Task Comfyuis_test_waits_as_long_as_the_rules_say()
    {
        var http = new FakeHttp(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var comfy = new ComfyImageService(() => new AiSettings(), http.Client(), () => TestAi.Prompts, rules: () => new AiRules { RequestTimeoutSeconds = 0.2 });

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await comfy.TestAsync(null, Ct);

        Assert.False(result.Ok);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"took {started.Elapsed}");
    }

    // ----- what goes into the log -----

    [Theory]
    [InlineData("https://user:key@api.example.com:8443/v1?token=x", "https://api.example.com:8443")]
    [InlineData("http://localhost:11434", "http://localhost:11434")]
    [InlineData("https://api.anthropic.com/v1/messages", "https://api.anthropic.com")]
    [InlineData("not an address", "(an address that does not read)")]
    public void An_address_is_logged_as_its_scheme_host_and_port_only(string address, string logged) =>
        Assert.Equal(logged, AiProviders.PlaceOf(address));

    // ----- the player's line -----

    [Fact]
    public async Task A_pasted_line_longer_than_the_talk_box_takes_is_cut_before_it_goes_anywhere()
    {
        var ai = new FakeAiText((_, _) => Task.FromResult<string?>("Sure thing."));
        var desk = new TalkDesk(ai, (_, _) => Task.FromResult(new List<Models.Race.ProcessedRaceSession>()), id => id, prompts: () => TestAi.Prompts,
            rules: () => new TalkRules());
        var game = new GameState { SaveName = "test" };
        var car = new UsedCarListingViewModel
        {
            Listing = new UsedCarListing { CarDefinitionId = "bel_air", Price = 1500m, Mileage = 80_000, Condition = 0.7f, ListedDate = GameState.GetStartingDateTime() },
            CarDefinition = new CarDefinition { Id = "bel_air", Brand = "Chevrolet", Name = "Bel Air", UiYear = 1957 }
        };
        var talk = desk.ForCar(game, car, null, TalkPlace.Phone);

        await talk.SayAsync(new string('x', 2_000_000), Ct);

        var said = ai.Chats[^1].Turns.Last(t => !t.Mine).Text;
        Assert.Equal(Conversation.MostSaid, said.Length);
    }

    // ----- shows -----

    [Fact]
    public async Task A_show_whose_stop_never_comes_is_taken_as_over_after_the_most()
    {
        var shows = new AiShows(TimeSpan.FromMilliseconds(100));
        shows.Set(true);

        Assert.True(await shows.OverAsync(Ct));
        Assert.False(shows.Showing);

        // The lost stop when it comes changes nothing; a new show is waited for afresh
        shows.Set(false);
        Assert.False(shows.Showing);
        shows.Set(true);
        Assert.True(shows.Showing);
        shows.Set(false);
        Assert.True(await shows.OverAsync(Ct));
    }

    [Fact]
    public void A_show_is_waited_for_longer_than_the_longest_script()
    {
        Assert.True(AiShows.MostWait > TimeSpan.FromSeconds(CarScript.LongestMostSeconds));
    }

    // ----- the AI's rules -----

    [Fact]
    public void The_ais_figures_outside_their_bounds_are_the_core_and_told()
    {
        using var temp = new TempDir();
        var problems = new List<string>();
        temp.File("mod/rules/mine.json", """
            { "ai": { "maxConcurrent": 0, "requestTimeoutSeconds": -1, "newsChance": 3,
                      "talk": { "carPriceStep": 0, "feelingPinkLost": -50, "sellerMinAge": 90, "sellerMaxAge": 30,
                                "detailLight": { "factTokens": 0, "turnsKept": 12, "memoriesGiven": 4 } },
                      "ollama": { "contextTokens": -5 } } }
            """);
        var merged = ModMerge.NewObject();
        ModMerge.Into(merged, ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, problems));
        ModMerge.Into(merged, ModMerge.ContentFiles(ModSet.BaseOnly(temp.Combine("mod")), ContentTypes.Rules, problems));

        var ai = RuleBook.Read(merged, problems).Ai;
        var core = new AiRules();

        Assert.Equal(core.MaxConcurrent, ai.MaxConcurrent);
        Assert.Equal(core.RequestTimeoutSeconds, ai.RequestTimeoutSeconds);
        Assert.Equal(core.NewsChance, ai.NewsChance);
        Assert.Equal(core.Talk.CarPriceStep, ai.Talk.CarPriceStep);
        Assert.Equal(core.Talk.FeelingPinkLost, ai.Talk.FeelingPinkLost);
        Assert.Equal((core.Talk.SellerMinAge, core.Talk.SellerMaxAge), (ai.Talk.SellerMinAge, ai.Talk.SellerMaxAge));
        Assert.Equal(core.Talk.DetailLight.FactTokens, ai.Talk.DetailLight.FactTokens);
        Assert.Equal(core.Ollama.ContextTokens, ai.Ollama.ContextTokens);
        Assert.Contains(problems, p => p.StartsWith("ai.maxConcurrent (0)", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("ai.talk.sellerMinAge is over", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("ai.talk.detailLight", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("ai.ollama.contextTokens (-5)", StringComparison.Ordinal));
    }

    // ----- a scene's paths -----

    private static SceneMarks Marks(string json, List<string> problems)
    {
        using var document = JsonDocument.Parse(json);
        return SceneMarks.Read(document.RootElement, "scene.json", problems);
    }

    [Fact]
    public void A_path_of_too_many_points_or_too_long_is_left_out_and_told()
    {
        var problems = new List<string>();
        var many = string.Join(", ", Enumerable.Range(0, SceneMarks.MostPathPoints + 1).Select(i => $"[{i % 2}, {i % 3}]"));
        var marks = Marks($$"""
            { "paths": { "many": [ {{many}} ], "zigzag": [ [-9000, -9000], [9000, 9000], [-9000, 9000], [9000, -9000] ], "arrive": [ [0, 0], [0, 20] ] } }
            """, problems);

        Assert.Equal(["arrive"], marks.Paths.Keys);
        Assert.Contains(problems, p => p.Contains("\"many\"") && p.Contains("points"));
        Assert.Contains(problems, p => p.Contains("\"zigzag\"") && p.Contains("long"));
    }

    [Fact]
    public void A_scenes_path_is_curved_once_however_often_it_is_asked_for()
    {
        var problems = new List<string>();
        var marks = Marks("""{ "paths": { "arrive": [ [0, 0], [0, 20], [5, 30] ] } }""", problems);

        var once = DrivePath.Of(marks.Paths["arrive"]);
        Assert.Same(once, DrivePath.Of(marks.Paths["arrive"]));
        var stand = marks.Stand("nowhere", "arrive")!.Value;
        Assert.Equal(once.End.X, stand.X, 3);
        Assert.Equal(once.End.Z, stand.Z, 3);
        Assert.Equal(0f, marks.PathStart("arrive")!.Value.Z, 3);
    }

    // ----- work for a game no longer in play -----

    [Fact]
    public async Task Street_talk_news_and_ads_queued_for_a_game_no_longer_in_play_are_let_go()
    {
        var ai = new FakeAiText((_, values) => Task.FromResult<string?>("AI words"));
        var old = new GameState();
        var now = new GameState();
        var writer = new AiWriter(ai, new ComfyImageService(() => new AiSettings(), new HttpClient()), () => now, rules: () => new AiRules());
        var talk = new StreetTalkItem(old.Date, "Eddie bought a Chevy.");
        var piece = new NewsArticle { Headline = "Boss Beaten", Body = "The Boss lost." };

        writer.StreetTalkAdded(old, [talk]);
        writer.NewsAdded(old, [piece]);
        await writer.WhenIdle();

        Assert.Empty(ai.Asked);
        Assert.Null(talk.AiText);
        Assert.Null(piece.AiBody);

        // The game in play's own are written
        var mine = new StreetTalkItem(now.Date, "Rosa won again.");
        writer.StreetTalkAdded(now, [mine]);
        await writer.WhenIdle();
        Assert.Equal("AI words", mine.AiText);
    }

    [Fact]
    public async Task An_ad_at_the_desk_for_a_game_since_left_is_not_rewritten()
    {
        var release = new TaskCompletionSource();
        var ai = new FakeAiText(async (_, values) =>
        {
            await release.Task;
            return "AI: " + values["blurb"];
        });
        var game = new GameState();
        var writer = new AiWriter(ai, new ComfyImageService(() => new AiSettings(), new HttpClient()), () => game, rules: () => new AiRules());
        var said = new string?[2];

        writer.Improve(new PartAdFacts("Cam", "Camshafts", false, true, 0.9, false), "ad 0", better => said[0] = better);
        while (true)
        {
            lock (ai.Asked) if (ai.Asked.Count == 1) break;
            await Task.Delay(5, Ct);
        }

        writer.Improve(new PartAdFacts("Cam", "Camshafts", false, true, 0.9, false), "ad 1", better => said[1] = better);
        game = new GameState();
        release.SetResult();
        await writer.WhenIdle();

        Assert.Equal("AI: ad 0", said[0]);
        Assert.Null(said[1]);
        Assert.Single(ai.Asked);
    }

    // ----- the counters' facts -----

    [Fact]
    public void A_machinists_job_is_told_by_its_part_days_and_speed()
    {
        var search = new ClerkSearch(true, null, false, null, null, null);
        var job = ClerkPick.Job("bore", "Bore .030 over", "Ford 289 block", 85m, speed: true, days: 3);
        var lines = TalkFacts.Work(new ClerkFindings(search, 1, [job])).ToList();

        Assert.Equal(("Ford 289 block", true, 3), (job.Part, job.Speed, job.Days));
        Assert.Contains(GameText.Say("chat.fact.work.pick", ("name", job.Name), ("part", job.Part), ("price", new Helpers.Money(job.Price)),
            ("days", 3), ("speed", GameText.Say("chat.fact.work.speed")), ("dyno", string.Empty)), lines);

        // Not asked, and nothing found, say so in the machinist's words; a clerk's in the clerk's
        Assert.Equal([GameText.Say("chat.fact.work.not_asked")], TalkFacts.Work(null));
        Assert.Equal([GameText.Say("chat.fact.stock.not_asked")], TalkFacts.Stock(null));
        var none = new ClerkFindings(search, 0, []);
        Assert.Equal([GameText.Say("chat.fact.stock.nothing", ("asked", TalkFacts.Asked(search)))], TalkFacts.Stock(none));
    }

    [Fact]
    public void A_clerk_whose_picks_none_fit_says_so_once_more_at_the_end()
    {
        var search = new ClerkSearch(true, null, false, null, null, null);
        var picks = new[] { new ClerkPick("a", "Cam A", "Camshafts", 40m, false, 2), new ClerkPick("b", "Cam B", "Camshafts", 50m, false, 1) };

        Assert.Equal(GameText.Say("chat.fact.stock.none_fit"), TalkFacts.Stock(new ClerkFindings(search, 2, picks)).Last());
        Assert.NotEqual(GameText.Say("chat.fact.stock.none_fit"),
            TalkFacts.Stock(new ClerkFindings(search, 2, [picks[0] with { Fits = true }, picks[1]])).Last());
    }

    /// <summary>A body with no length known ahead, as a chunked answer is</summary>
    private sealed class Unseekable(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 512));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
