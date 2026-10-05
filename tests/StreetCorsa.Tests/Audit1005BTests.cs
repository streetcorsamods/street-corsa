using System.Net;
using System.Net.Http;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>The fixes of the 2026-10-05 audit in the AI and the talks (slice B)</summary>
public class Audit1005BTests
{
    public Audit1005BTests() => TestLogging.SilenceLogging();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyList<string> OfferWords = ["take", "give", "offer", "how about", "bucks"];

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    // ----- a huge number in a talk -----

    [Theory]
    [InlineData("I'll give you $1,000,000,000,000,000,000,000,000,000,000,000")]
    [InlineData("I'll give you $12345678901234567890123456789k")]
    [InlineData("I'll give you $79,228,162,514,264,337,593,543,950,335k")]
    public void A_number_too_long_for_a_price_is_no_offer_and_no_crash(string text)
    {
        Assert.Null(Haggle.OfferIn(text, 1500m, OfferWords, new TalkRules()));
        Assert.Empty(Haggle.SumsIn(text));
        Assert.False(Haggle.NamesUnknownSum(text, [1500m]));
    }

    [Fact]
    public void A_price_of_a_dozen_digits_still_reads()
    {
        Assert.Equal([999_999_999_999m], Haggle.SumsIn("$999,999,999,999"));
        Assert.Equal([1200m], Haggle.SumsIn("$1,200"));
    }

    // ----- the key goes only where it was typed for -----

    private static AiSettings KeyFor(string provider, string? endpoint = null) => new()
    {
        Enabled = true,
        TextProvider = provider,
        Endpoint = endpoint,
        ApiKeyProtected = AiKeys.Protect("sk-paid"),
        KeyProvider = provider,
        KeyEndpoint = endpoint
    };

    [Fact]
    public void The_key_is_given_only_to_the_provider_and_address_it_was_typed_for()
    {
        var settings = KeyFor(AiProviders.Anthropic);

        Assert.Equal("sk-paid", AiKeys.KeyFor(settings, AiProviders.Anthropic, null));
        // The usual address typed out, or with a slash, is the same place
        Assert.Equal("sk-paid", AiKeys.KeyFor(settings, AiProviders.Anthropic, "https://api.anthropic.com/"));
        Assert.Null(AiKeys.KeyFor(settings, AiProviders.Anthropic, "https://proxy.example.com"));
        Assert.Null(AiKeys.KeyFor(settings, AiProviders.OpenAi, null));
        Assert.Null(AiKeys.KeyFor(settings, AiProviders.OpenAi, "https://api.anthropic.com"));
        // Never to Ollama, whose key box is hidden
        Assert.Null(AiKeys.KeyFor(KeyFor(AiProviders.Ollama), AiProviders.Ollama, null));
    }

    [Fact]
    public void A_key_from_older_settings_is_the_saved_providers_and_never_ollamas()
    {
        var old = new AiSettings { TextProvider = AiProviders.OpenAi, Endpoint = "https://openrouter.ai/api", ApiKeyProtected = AiKeys.Protect("sk-old") };

        Assert.Equal("sk-old", AiKeys.KeyFor(old, AiProviders.OpenAi, "https://openrouter.ai/api"));
        Assert.Null(AiKeys.KeyFor(old, AiProviders.Anthropic, null));

        // Tied once Settings saves: switching the provider afterwards does not take the key along
        AiKeys.Tie(old);
        Assert.Equal(AiProviders.OpenAi, old.KeyProvider);
        Assert.Equal("https://openrouter.ai/api", old.KeyEndpoint);
        old.TextProvider = AiProviders.Ollama;
        old.Endpoint = null;
        Assert.Null(AiKeys.KeyFor(old, AiProviders.Ollama, null));

        // A key saved while Ollama was the provider (a switch before keys were tied) goes nowhere
        var switched = new AiSettings { TextProvider = AiProviders.Ollama, ApiKeyProtected = AiKeys.Protect("sk-old") };
        AiKeys.Tie(switched);
        Assert.Null(AiKeys.KeyFor(switched, AiProviders.Ollama, null));
        Assert.Null(AiKeys.KeyFor(switched, AiProviders.OpenAi, null));
    }

    [Fact]
    public void Older_settings_json_with_no_key_provider_still_loads()
    {
        var json = """{ "Enabled": true, "TextProvider": "anthropic", "ApiKeyProtected": "abc" }""";
        var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<AiSettings>(json)!;

        Assert.Null(settings.KeyProvider);
        Assert.Null(settings.KeyEndpoint);
        Assert.Equal("abc", settings.ApiKeyProtected);
    }

    [Theory]
    [InlineData("http://192.168.1.5:1234/v1", false)]
    [InlineData("http://openrouter.ai/api", false)]
    [InlineData("https://openrouter.ai/api", true)]
    [InlineData("http://127.0.0.1:1234/v1", true)]
    [InlineData("http://localhost:1234/v1", true)]
    public async Task A_key_goes_over_plain_http_only_to_this_machine(string endpoint, bool sent)
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "choices": [ { "message": { "content": "Hi." } } ] }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.OpenAi, endpoint, "m", "sk-paid");

        await provider.CompleteAsync(new AiTextRequest("s", "p", 50, 0.7), Ct);

        Assert.Equal(sent, Assert.Single(http.Sent).Headers.ContainsKey("Authorization"));
        Assert.Equal(sent, AiProviders.KeySafeAt(AiProviders.OpenAi, endpoint));
    }

    [Fact]
    public async Task Ollama_is_never_sent_a_key()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "message": { "content": "Hi." }, "done": true }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.Ollama, null, "m", "sk-paid");

        await provider.CompleteAsync(new AiTextRequest("s", "p", 50, 0.7), Ct);

        Assert.False(Assert.Single(http.Sent).Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task The_service_does_not_take_an_anthropic_key_to_another_provider()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "choices": [ { "message": { "content": "Hi." } } ] }""")));
        var settings = KeyFor(AiProviders.Anthropic);
        settings.TextProvider = AiProviders.OpenAi;
        settings.Endpoint = "https://somewhere.example.com/v1";
        settings.Model = "m";
        var service = new AiTextService(() => settings, http.Client(), () => AiPrompts.Load(ModSet.BaseOnly(RepoPaths.BaseContent)),
            () => new AiRules { MinSecondsBetween = 0 });

        Assert.NotNull(await service.WriteAsync("talk", new TextValues { ["line"] = "Move it." }, AiPriority.Now, Ct));

        Assert.False(Assert.Single(http.Sent).Headers.ContainsKey("Authorization"));
    }

    [Theory]
    [InlineData(AiProviders.Ollama, null, true)]
    [InlineData(AiProviders.OpenAi, null, true)]
    [InlineData(AiProviders.Anthropic, null, false)]
    [InlineData(AiProviders.OpenAi, "https://openrouter.ai/api", false)]
    [InlineData(AiProviders.Ollama, "http://192.168.1.20:11434", false)]
    public void An_address_off_this_pc_is_told(string provider, string? endpoint, bool local) =>
        Assert.Equal(local, AiProviders.IsLocal(provider, endpoint));

    // ----- ComfyUI -----

    [Fact]
    public void A_workflow_with_a_node_that_is_not_comfyuis_own_is_left_out()
    {
        using var temp = new TempDir();
        temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        temp.File("Content/streetcorsa/ai/prompts.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "ai", "prompts.json")));
        temp.File("Content/streetcorsa/ai/comfy_portrait.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "ai", "comfy_portrait.json")));
        temp.File("Content/evil/mod.json", """{ "id": "evil" }""");
        temp.File("Content/evil/ai/comfy_portrait.json", """
            { "prompt": "{look}", "fill": { "positive": { "node": "1", "input": "text" } },
              "workflow": { "1": { "inputs": { "text": "" }, "class_type": "CLIPTextEncode" },
                            "2": { "inputs": { "code": "import os" }, "class_type": "RunPythonCode" } } }
            """);

        var prompts = AiPrompts.Load(ModSet.Load(temp.Combine("Content")));

        Assert.Contains(prompts.Problems, p => p.Contains("RunPythonCode"));
        // The base game's stays: every node it uses is ComfyUI's own
        Assert.Equal("juggernautXL_ragnarokBy.safetensors", prompts.Workflow(AiWriter.PortraitUse)!.Checkpoint);
    }

    [Fact]
    public void Every_node_of_the_shipped_workflows_is_allowed()
    {
        var prompts = AiPrompts.Load(ModSet.BaseOnly(RepoPaths.BaseContent));

        Assert.Empty(ComfyWorkflow.NotCore(prompts.Workflow(AiWriter.PortraitUse)!.Workflow));
        Assert.DoesNotContain(prompts.Problems, p => p.Contains("comfy"));
    }

    [Fact]
    public void Every_save_node_saves_under_the_games_name()
    {
        var workflow = new ComfyWorkflow
        {
            Workflow = System.Text.Json.Nodes.JsonNode.Parse("""
                { "1": { "inputs": { "filename_prefix": "../../evil" }, "class_type": "SaveImage" },
                  "2": { "inputs": { "filename_prefix": "C:/Windows/x" }, "class_type": "SaveImage" } }
                """)!.AsObject()
        };

        var graph = new ComfyImageService(() => new AiSettings(), new HttpClient()).Graph(workflow, new TextValues(), 1);

        Assert.Equal(ComfyImageService.Prefix, graph["1"]!["inputs"]!["filename_prefix"]!.GetValue<string>());
        Assert.Equal(ComfyImageService.Prefix, graph["2"]!["inputs"]!["filename_prefix"]!.GetValue<string>());
    }

    private static ComfyImageService Comfy(Func<HttpResponseMessage> view, out FakeHttp http)
    {
        http = new FakeHttp((sent, _) => Task.FromResult(sent.Uri.AbsolutePath switch
        {
            "/prompt" => FakeHttp.Json("""{ "prompt_id": "abc" }"""),
            "/history/abc" => FakeHttp.Json("""{ "abc": { "status": { "completed": true }, "outputs": { "21": { "images": [ { "filename": "a.png" } ] } } } }"""),
            "/view" => view(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        return new ComfyImageService(() => new AiSettings { ComfyEnabled = true }, http.Client(), () => AiPrompts.Load(ModSet.BaseOnly(RepoPaths.BaseContent)));
    }

    [Fact]
    public async Task A_picture_that_is_no_png_or_jpeg_is_not_taken()
    {
        var comfy = Comfy(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("MZ\x90\0 not a picture"u8.ToArray()) }, out _);

        Assert.Null(await comfy.PaintAsync(AiWriter.PortraitUse, new TextValues(), Ct));
        Assert.True(ComfyImageService.IsPicture(Png));
        Assert.True(ComfyImageService.IsPicture([0xFF, 0xD8, 0xFF, 0xE0, 0]));
        Assert.False(ComfyImageService.IsPicture([0x89, 0x50]));
        Assert.False(ComfyImageService.IsPicture(null));
    }

    [Fact]
    public async Task A_picture_larger_than_the_most_is_not_read()
    {
        var comfy = Comfy(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([.. Png, .. new byte[ComfyImageService.MostPictureBytes]])
        }, out _);

        Assert.Null(await comfy.PaintAsync(AiWriter.PortraitUse, new TextValues(), Ct));
    }

    [Fact]
    public async Task A_provider_answer_larger_than_the_most_is_no_answer()
    {
        var huge = new string('a', AiProviders.MostAnswerBytes + 1);
        var http = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(huge) }));
        var provider = AiProviders.Create(http.Client(), AiProviders.OpenAi, null, "m", null);

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.CompleteAsync(new AiTextRequest("s", "p", 50, 0.7), Ct));
        Assert.Contains("larger than", error.Message);
    }

    [Fact]
    public async Task A_new_face_with_no_picture_bytes_is_not_saved()
    {
        using var temp = new TempDir();
        var game = new Models.GameState.GameState { SaveName = "My Save" };
        var racer = new Models.GameState.Opponent("Rosa Diaz", 24, Models.GameState.Gender.Female, 93, 60);
        var images = new BytesImages("<html>not a picture</html>"u8.ToArray());
        var writer = new AiWriter(new FakeAiText((_, _) => Task.FromResult<string?>(null), available: false), images, () => game,
            portraitsRoot: temp.Path, rules: () => new AiRules());

        writer.NewFace(game, racer);
        await writer.WhenIdle();

        Assert.Equal(1, images.Painted);
        Assert.Null(writer.PortraitOf(racer));
    }

    private sealed class BytesImages(byte[] picture) : IAiImages
    {
        public int Painted;

        public bool IsAvailable => true;

        public Task<byte[]?> PaintAsync(string use, TextValues values, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Painted);
            return Task.FromResult<byte[]?>(picture);
        }

        public Task<AiTestResult> TestAsync(string? endpoint, CancellationToken cancellationToken = default) => Task.FromResult(new AiTestResult(true, "ok"));
    }

    // ----- one of each -----

    [Theory]
    [InlineData("", "")]
    [InlineData(" m ", "m:latest")]
    [InlineData("m:7b", "m:7b")]
    public void An_ollama_name_is_tagged_one_way(string name, string tagged) => Assert.Equal(tagged, AiProviders.Tagged(name));

    [Fact]
    public async Task Background_work_is_tracked_until_done_and_a_failure_is_only_logged()
    {
        var background = new AiBackground(AppLoggerFactory.CreateLogger("Ai"));
        var gate = new TaskCompletionSource();
        var done = false;

        background.Run(async () =>
        {
            await gate.Task;
            done = true;
        });
        background.Start(() => throw new InvalidOperationException("broken"));
        var idle = background.WhenIdle();
        Assert.False(idle.IsCompleted);

        gate.SetResult();
        await idle;
        Assert.True(done);
    }

    [Fact]
    public void A_backoff_rests_once_and_clears()
    {
        var backoff = new AiBackoff();

        Assert.False(backoff.IsResting);
        Assert.True(backoff.Rest(60));
        Assert.False(backoff.Rest(60));
        Assert.True(backoff.IsResting);
        backoff.Clear();
        Assert.False(backoff.IsResting);
        Assert.Equal(303, AiProviders.Snippet(new string('x', 400)).Length);
    }

    [Fact]
    public async Task A_cancelled_wait_in_the_ai_queue_lets_go_of_the_talks_token()
    {
        var release = new TaskCompletionSource();
        var http = new FakeHttp(async (_, _) =>
        {
            await release.Task;
            return FakeHttp.Json("""{ "message": { "content": "Hey." }, "done": true }""");
        });
        var service = new AiTextService(() => new AiSettings { Enabled = true, Model = "m" }, http.Client(),
            () => AiPrompts.Load(ModSet.BaseOnly(RepoPaths.BaseContent)), () => new AiRules { MinSecondsBetween = 0, MaxConcurrent = 1, MaxQueued = 5 });
        using var talk = new CancellationTokenSource();

        var first = service.WriteAsync("talk", new TextValues { ["line"] = "a" }, AiPriority.Now, Ct);
        var waiting = service.WriteAsync("talk", new TextValues { ["line"] = "b" }, AiPriority.Now, talk.Token);
        talk.Cancel();

        Assert.Null(await waiting);
        release.SetResult();
        Assert.Equal("Hey.", await first);
    }
}
