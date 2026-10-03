using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>A request the fake HTTP handler was sent, with its body read</summary>
public sealed record SentRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body)
{
    public JsonNode? Json => Body.Length == 0 ? null : JsonNode.Parse(Body);
}

/// <summary>An HTTP server in the test: answers each request with what the test says, and keeps what it was sent</summary>
public sealed class FakeHttp(Func<SentRequest, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    public List<SentRequest> Sent { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public HttpClient Client() => new(this);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var sent = new SentRequest(request.Method, request.RequestUri!, headers, body);
        lock (Sent) Sent.Add(sent);
        return await answer(sent, cancellationToken);
    }
}

/// <summary>A text AI in the test: answers each prompt with what the test says, after a wait if it says so</summary>
public sealed class FakeAiText(Func<string, TextValues, Task<string?>> answer, bool available = true) : IAiText
{
    public List<(string Use, TextValues Values, AiPriority Priority)> Asked { get; } = [];

    public bool IsAvailable { get; set; } = available;

    public int? ContextTokens { get; set; }

    public Task<string?> WriteAsync(string use, TextValues values, AiPriority priority = AiPriority.Background, CancellationToken cancellationToken = default)
    {
        lock (Asked) Asked.Add((use, values, priority));
        return answer(use, values);
    }

    /// <summary>The talks asked, with the turns as they were then</summary>
    public List<(string Use, TextValues Values, List<AiTurn> Turns)> Chats { get; } = [];

    public Task<string?> ChatAsync(string use, TextValues values, IReadOnlyList<AiTurn> turns, CancellationToken cancellationToken = default)
    {
        lock (Chats) Chats.Add((use, values, turns.ToList()));
        return answer(use, values);
    }

    public Task<AiTestResult> TestAsync(AiSettings settings, string? apiKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiTestResult(true, "ok"));
}

public class AiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AiPrompts BasePrompts() => AiPrompts.Load(ModSet.BaseOnly(RepoPaths.BaseContent));

    private static readonly AiTextRequest Request = new("You are a racer.", "Say hi.", 50, 0.7);

    // ----- the providers' requests and answers -----

    [Fact]
    public async Task Ollama_is_asked_on_its_chat_api_and_its_message_is_the_answer()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "message": { "role": "assistant", "content": "Hey, kid." }, "done": true }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.Ollama, null, "llama3.2:3b", null);

        var answer = await provider.CompleteAsync(Request, CancellationToken.None);

        Assert.Equal("Hey, kid.", answer);
        var sent = Assert.Single(http.Sent);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("http://localhost:11434/api/chat", sent.Uri.ToString());
        var json = sent.Json!;
        Assert.Equal("llama3.2:3b", json["model"]!.GetValue<string>());
        Assert.False(json["stream"]!.GetValue<bool>());
        Assert.Equal("system", json["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("You are a racer.", json["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal("Say hi.", json["messages"]![1]!["content"]!.GetValue<string>());
        Assert.Equal(50, json["options"]!["num_predict"]!.GetValue<int>());
        Assert.False(sent.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Ollama_without_a_model_asks_the_first_it_has()
    {
        var http = new FakeHttp((sent, _) => Task.FromResult(sent.Uri.AbsolutePath == "/api/tags"
            ? FakeHttp.Json("""{ "models": [ { "name": "llama3.2:3b" }, { "name": "qwen" } ] }""")
            : FakeHttp.Json("""{ "message": { "content": "Yo." } }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.Ollama, "http://box:11434/", null, null);

        Assert.Equal("Yo.", await provider.CompleteAsync(Request, CancellationToken.None));
        Assert.Equal("http://box:11434/api/tags", http.Sent[0].Uri.ToString());
        Assert.Equal("llama3.2:3b", http.Sent[1].Json!["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task Ollama_without_a_model_asks_the_games_own_when_it_has_it()
    {
        var http = new FakeHttp((sent, _) => Task.FromResult(sent.Uri.AbsolutePath == "/api/tags"
            ? FakeHttp.Json("""{ "models": [ { "name": "qwen3:14b" }, { "name": "StreetCorsa:latest" }, { "name": "llama3.1:8b" } ] }""")
            : FakeHttp.Json("""{ "message": { "content": "Yo." } }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.Ollama, null, null, null, gameModel: "streetcorsa");

        Assert.Equal("StreetCorsa:latest", await provider.ModelAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Ollama_without_the_games_model_asks_the_first_it_has()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "models": [ { "name": "qwen3:14b" }, { "name": "streetcorsa:v2" } ] }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.Ollama, null, null, null, gameModel: "streetcorsa");

        Assert.Equal("qwen3:14b", await provider.ModelAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_model_the_player_names_beats_the_games_own()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "models": [ { "name": "streetcorsa:latest" } ] }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.Ollama, null, "gemma3:12b", null, gameModel: "streetcorsa");

        Assert.Equal("gemma3:12b", await provider.ModelAsync(CancellationToken.None));
        Assert.Empty(http.Sent);
    }

    [Fact]
    public async Task Without_a_model_one_that_cannot_chat_is_passed_over()
    {
        var http = new FakeHttp((sent, _) => Task.FromResult(sent.Uri.AbsolutePath == "/v1/models"
            ? FakeHttp.Json("""{ "data": [ { "id": "text-embedding-nomic-embed-text-v1.5" }, { "id": "whisper-1" }, { "id": "qwen2.5-7b-instruct" } ] }""")
            : FakeHttp.Json("""{ "choices": [ { "message": { "content": "Yo." } } ] }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.OpenAi, null, null, null);

        Assert.Equal("qwen2.5-7b-instruct", await provider.ModelAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(AiProviders.Anthropic, "https://api.anthropic.com/v1", "https://api.anthropic.com/v1/messages")]
    [InlineData(AiProviders.Anthropic, "https://api.anthropic.com/v1/", "https://api.anthropic.com/v1/messages")]
    [InlineData(AiProviders.Ollama, "http://box:11434/api", "http://box:11434/api/chat")]
    [InlineData(AiProviders.Ollama, "http://box:11434/v1/", "http://box:11434/api/chat")]
    public async Task An_address_typed_with_the_apis_own_path_is_not_doubled(string kind, string endpoint, string url)
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""
            { "message": { "content": "Hi." }, "content": [ { "type": "text", "text": "Hi." } ] }
            """)));
        var provider = AiProviders.Create(http.Client(), kind, endpoint, "some-model", "key");

        Assert.Equal("Hi.", await provider.CompleteAsync(Request, CancellationToken.None));
        Assert.Equal(url, Assert.Single(http.Sent).Uri.ToString());
    }

    [Theory]
    [InlineData("http://127.0.0.1:1234/v1", "http://127.0.0.1:1234/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1/", "http://localhost:11434/v1/chat/completions")]
    public async Task An_openai_compatible_api_is_asked_on_chat_completions_with_the_key_as_a_bearer(string endpoint, string url)
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "choices": [ { "message": { "role": "assistant", "content": "Nice wheels." } } ] }""")));
        var provider = AiProviders.Create(http.Client(), AiProviders.OpenAi, endpoint, "gpt-4o-mini", "sk-test");

        Assert.Equal("Nice wheels.", await provider.CompleteAsync(Request, CancellationToken.None));
        var sent = Assert.Single(http.Sent);
        Assert.Equal(url, sent.Uri.ToString());
        Assert.Equal("Bearer sk-test", sent.Headers["Authorization"]);
        Assert.Equal("gpt-4o-mini", sent.Json!["model"]!.GetValue<string>());
        Assert.Equal(50, sent.Json!["max_tokens"]!.GetValue<int>());
        Assert.Equal("system", sent.Json!["messages"]![0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task Anthropic_is_asked_on_messages_with_its_headers_and_only_the_text_blocks_are_words()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""
            { "content": [ { "type": "thinking", "thinking": "" }, { "type": "text", "text": "Step on it." } ],
              "stop_reason": "end_turn" }
            """)));
        var provider = AiProviders.Create(http.Client(), AiProviders.Anthropic, null, null, "sk-ant-test");

        Assert.Equal("Step on it.", await provider.CompleteAsync(Request, CancellationToken.None));
        var sent = Assert.Single(http.Sent);
        Assert.Equal("https://api.anthropic.com/v1/messages", sent.Uri.ToString());
        Assert.Equal("sk-ant-test", sent.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", sent.Headers["anthropic-version"]);
        Assert.False(sent.Headers.ContainsKey("Authorization"));
        var json = sent.Json!;
        Assert.Equal(AiProviders.AnthropicDefaultModel, json["model"]!.GetValue<string>());
        Assert.Equal("You are a racer.", json["system"]!.GetValue<string>());
        Assert.Equal("user", json["messages"]![0]!["role"]!.GetValue<string>());
        Assert.True(json["max_tokens"]!.GetValue<int>() >= AnthropicTextProvider.MinMaxTokens);
        Assert.Null(json["temperature"]);
    }

    [Fact]
    public async Task An_anthropic_refusal_or_error_is_no_answer()
    {
        var refusing = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "content": [], "stop_reason": "refusal" }""")));
        await Assert.ThrowsAsync<AiProviderException>(() =>
            AiProviders.Create(refusing.Client(), AiProviders.Anthropic, null, null, "k").CompleteAsync(Request, CancellationToken.None));

        var badKey = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "type": "error", "error": { "type": "authentication_error", "message": "invalid x-api-key" } }""", HttpStatusCode.Unauthorized)));
        var error = await Assert.ThrowsAsync<AiProviderException>(() =>
            AiProviders.Create(badKey.Client(), AiProviders.Anthropic, null, null, "k").CompleteAsync(Request, CancellationToken.None));
        Assert.Contains("invalid x-api-key", error.Message);
        Assert.Contains("401", error.Message);
    }

    // ----- the key -----

    [Fact]
    public void The_key_is_kept_encrypted_and_comes_back()
    {
        var kept = AiKeys.Protect("  sk-secret-123  ");

        Assert.NotNull(kept);
        Assert.DoesNotContain("sk-secret", kept);
        Assert.DoesNotContain("sk-secret", Encoding.UTF8.GetString(Convert.FromBase64String(kept!)));
        Assert.Equal("sk-secret-123", AiKeys.Unprotect(kept));
        Assert.Null(AiKeys.Protect("   "));
        Assert.Null(AiKeys.Unprotect(null));
        Assert.Null(AiKeys.Unprotect("bm90IGEga2V5"));
        Assert.Null(AiKeys.Unprotect("not base64 at all!"));
    }

    [Fact]
    public void Settings_json_holds_the_encrypted_key_only()
    {
        var settings = new GameSettings();
        settings.Ai.ApiKeyProtected = AiKeys.Protect("sk-plain-text-key");
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings);

        Assert.DoesNotContain("sk-plain-text-key", json);
        Assert.Contains("ApiKeyProtected", json);
        Assert.False(new GameSettings().Ai.Enabled);
        Assert.False(new GameSettings().Ai.ComfyEnabled);
    }

    // ----- the text service -----

    private static AiTextService Service(FakeHttp http, AiSettings? settings = null, AiRules? rules = null) =>
        new(() => settings ?? new AiSettings { Enabled = true, Model = "m" }, http.Client(), BasePrompts,
            () => rules ?? new AiRules { MinSecondsBetween = 0 });

    [Fact]
    public async Task A_local_model_lets_go_of_the_gpu_for_a_race_is_asked_nothing_during_it_and_is_loaded_again_after()
    {
        var http = new FakeHttp((sent, _) => Task.FromResult(FakeHttp.Json(sent.Uri.AbsolutePath == "/api/ps"
            ? """{ "models": [ { "name": "m:latest", "model": "m:latest" } ] }"""
            : """{ "message": { "content": "Hey." }, "done": true }""")));
        var service = Service(http);
        List<SentRequest> Chats() { lock (http.Sent) return http.Sent.Where(s => s.Uri.AbsolutePath == "/api/chat").ToList(); }

        service.SetRacing(true);
        for (var i = 0; i < 50 && Chats().Count == 0; i++) await Task.Delay(10, Ct);
        var unload = Assert.Single(Chats()).Json!;
        Assert.Equal(0, unload["keep_alive"]!.GetValue<int>());
        Assert.Empty(unload["messages"]!.AsArray());

        // The player's own is not asked; the background's waits for the end of the race
        Assert.Null(await service.ChatAsync("talk", new TextValues { ["line"] = "Move it." }, [new AiTurn(false, "Hi.")], Ct));
        var waiting = service.WriteAsync("talk", new TextValues { ["line"] = "Move it." }, cancellationToken: Ct);
        await Task.Delay(50, Ct);
        Assert.False(waiting.IsCompleted);
        Assert.Single(Chats());

        service.SetRacing(false);
        Assert.Equal("Hey.", await waiting);
        for (var i = 0; i < 50 && Chats().Count < 3; i++) await Task.Delay(10, Ct);
        var load = Assert.Single(Chats(), s => s.Json!["messages"]!.AsArray().Count == 0 && s.Json!["keep_alive"]!.GetValueKind() == System.Text.Json.JsonValueKind.String).Json!;
        Assert.Equal(new OllamaRules().KeepAlive, load["keep_alive"]!.GetValue<string>());
        Assert.Equal(new OllamaRules().ContextTokens, load["options"]!["num_ctx"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_model_that_was_not_loaded_before_a_race_is_not_loaded_after_it()
    {
        var http = new FakeHttp((sent, _) => Task.FromResult(FakeHttp.Json(sent.Uri.AbsolutePath == "/api/ps"
            ? """{ "models": [ { "name": "other:latest" } ] }"""
            : """{ "message": { "content": "Hey." }, "done": true }""")));
        var service = Service(http);

        service.SetRacing(true);
        for (var i = 0; i < 50 && http.Sent.Count == 0; i++) await Task.Delay(10, Ct);
        await Task.Delay(30, Ct);
        service.SetRacing(false);
        await Task.Delay(100, Ct);

        Assert.Equal("/api/ps", Assert.Single(http.Sent).Uri.AbsolutePath);
    }

    [Fact]
    public async Task A_request_waiting_in_line_when_a_race_starts_is_not_sent_until_it_is_over()
    {
        var release = new TaskCompletionSource();
        var http = new FakeHttp(async (sent, _) =>
        {
            if (sent.Uri.AbsolutePath == "/api/ps") return FakeHttp.Json("""{ "models": [] }""");
            if (sent.Json!["messages"]!.AsArray().Count > 0 && sent.Body.Contains("first")) await release.Task;
            return FakeHttp.Json("""{ "message": { "content": "Hey." }, "done": true }""");
        });
        var service = Service(http);
        List<SentRequest> Chats() { lock (http.Sent) return http.Sent.Where(s => s.Uri.AbsolutePath == "/api/chat").ToList(); }

        var first = service.WriteAsync("talk", new TextValues { ["line"] = "first" }, cancellationToken: Ct);
        for (var i = 0; i < 50 && Chats().Count == 0; i++) await Task.Delay(10, Ct);
        var second = service.WriteAsync("talk", new TextValues { ["line"] = "second" }, cancellationToken: Ct);
        service.SetRacing(true);
        release.SetResult();
        Assert.Equal("Hey.", await first);
        await Task.Delay(50, Ct);
        Assert.DoesNotContain(Chats(), s => s.Body.Contains("second"));

        service.SetRacing(false);
        Assert.Equal("Hey.", await second);
        Assert.Contains(Chats(), s => s.Body.Contains("second"));
    }

    [Fact]
    public async Task A_paid_provider_is_not_touched_by_a_race_and_nothing_is_unloaded_when_the_rules_say_not_to()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "choices": [ { "message": { "content": "Hey." } } ] }""")));
        var service = Service(http, new AiSettings { Enabled = true, TextProvider = AiProviders.OpenAi, Model = "m" });
        service.SetRacing(true);
        Assert.Equal("Hey.", await service.WriteAsync("talk", new TextValues { ["line"] = "Move it." }, cancellationToken: Ct));
        Assert.Single(http.Sent);

        var local = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "message": { "content": "Hey." } }""")));
        var kept = Service(local, rules: new AiRules { MinSecondsBetween = 0, Ollama = new OllamaRules { UnloadForRaces = false } });
        kept.SetRacing(true);
        Assert.Equal("Hey.", await kept.WriteAsync("talk", new TextValues { ["line"] = "Move it." }, cancellationToken: Ct));
        Assert.Single(local.Sent);
    }

    [Fact]
    public async Task A_prompt_is_filled_with_the_games_values_and_the_persona_with_the_players_language()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "message": { "content": "\"Out of my way, rookie.\"" } }""")));

        var said = await Service(http).WriteAsync("talk", new TextValues { ["line"] = "Move it.", ["name"] = "Eddie Collins", ["nickname"] = "Flathead", ["temper"] = "hothead" }, cancellationToken: Ct);

        Assert.Equal("Out of my way, rookie.", said);
        var json = http.Sent.Single().Json!;
        var system = json["messages"]![0]!["content"]!.GetValue<string>();
        var prompt = json["messages"]![1]!["content"]!.GetValue<string>();
        Assert.Contains("English", system);
        Assert.Contains("1960s", system);
        Assert.DoesNotContain("{language}", system);
        Assert.Contains("Eddie Collins \"Flathead\"", prompt);
        Assert.Contains("\"Move it.\"", prompt);
        Assert.Contains("hothead", prompt);
    }

    [Fact]
    public async Task A_provider_that_fails_or_is_off_leaves_the_games_own_words()
    {
        var failing = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "error": "model not found" }""", HttpStatusCode.NotFound)));
        Assert.Null(await Service(failing).WriteAsync("talk", new TextValues { ["line"] = "x" }, cancellationToken: Ct));

        var unreachable = new FakeHttp((_, _) => throw new HttpRequestException("connection refused"));
        var service = Service(unreachable);
        Assert.Null(await service.WriteAsync("talk", new TextValues { ["line"] = "x" }, cancellationToken: Ct));
        // Left alone for a while after that: not asked again at once
        Assert.False(service.IsAvailable);
        Assert.Null(await service.WriteAsync("talk", new TextValues { ["line"] = "x" }, cancellationToken: Ct));
        Assert.Single(unreachable.Sent);

        var off = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Json("""{ "message": { "content": "hi" } }""")));
        Assert.Null(await Service(off, new AiSettings { Enabled = false }).WriteAsync("talk", new TextValues(), cancellationToken: Ct));
        Assert.Null(await Service(off).WriteAsync("no_such_prompt", new TextValues(), cancellationToken: Ct));
        Assert.Empty(off.Sent);

        // Anthropic without a key is not available at all
        Assert.False(Service(off, new AiSettings { Enabled = true, TextProvider = AiProviders.Anthropic }).IsAvailable);
    }

    [Fact]
    public async Task A_provider_too_slow_is_given_up()
    {
        var slow = new FakeHttp(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return FakeHttp.Json("""{ "message": { "content": "late" } }""");
        });
        var service = Service(slow, rules: new AiRules { RequestTimeoutSeconds = 0.3, MinSecondsBetween = 0 });

        var started = DateTime.UtcNow;
        Assert.Null(await service.WriteAsync("talk", new TextValues { ["line"] = "x" }, cancellationToken: Ct));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Requests_run_one_at_a_time_and_the_players_go_first()
    {
        var release = new TaskCompletionSource();
        var order = new List<string>();
        var http = new FakeHttp(async (sent, _) =>
        {
            var line = sent.Json!["messages"]![1]!["content"]!.GetValue<string>();
            lock (order) order.Add(line);
            await release.Task;
            return FakeHttp.Json("""{ "message": { "content": "ok" } }""");
        });
        var service = Service(http);

        var first = service.WriteAsync("street_talk", new TextValues { ["line"] = "first" }, cancellationToken: Ct);
        await Task.Delay(100, Ct);
        var background = service.WriteAsync("street_talk", new TextValues { ["line"] = "background" }, cancellationToken: Ct);
        var now = service.WriteAsync("street_talk", new TextValues { ["line"] = "now" }, AiPriority.Now, cancellationToken: Ct);
        await Task.Delay(100, Ct);
        Assert.Single(order);

        release.SetResult();
        await Task.WhenAll(first, background, now);

        Assert.Equal(3, order.Count);
        Assert.Contains("first", order[0]);
        Assert.Contains("now", order[1]);
        Assert.Contains("background", order[2]);
    }

    [Fact]
    public async Task A_full_queue_drops_the_background_work()
    {
        var release = new TaskCompletionSource();
        var http = new FakeHttp(async (_, _) =>
        {
            await release.Task;
            return FakeHttp.Json("""{ "message": { "content": "ok" } }""");
        });
        var service = Service(http, rules: new AiRules { MaxQueued = 1, MinSecondsBetween = 0 });

        var running = service.WriteAsync("street_talk", new TextValues { ["line"] = "a" }, cancellationToken: Ct);
        await Task.Delay(100, Ct);
        var queued = service.WriteAsync("street_talk", new TextValues { ["line"] = "b" }, cancellationToken: Ct);
        Assert.Null(await service.WriteAsync("street_talk", new TextValues { ["line"] = "c" }, cancellationToken: Ct));

        release.SetResult();
        Assert.Equal("ok", await running);
        Assert.Equal("ok", await queued);
    }

    [Fact]
    public async Task A_line_full_of_background_work_still_takes_the_players_request()
    {
        var release = new TaskCompletionSource();
        var http = new FakeHttp(async (_, _) =>
        {
            await release.Task;
            return FakeHttp.Json("""{ "message": { "content": "ok" } }""");
        });
        var service = Service(http, rules: new AiRules { MaxQueued = 1, MinSecondsBetween = 0 });

        var running = service.WriteAsync("street_talk", new TextValues { ["line"] = "a" }, cancellationToken: Ct);
        await Task.Delay(100, Ct);
        var queued = service.WriteAsync("street_talk", new TextValues { ["line"] = "b" }, cancellationToken: Ct);
        var now = service.WriteAsync("street_talk", new TextValues { ["line"] = "now" }, AiPriority.Now, cancellationToken: Ct);
        Assert.Null(await service.WriteAsync("street_talk", new TextValues { ["line"] = "c" }, AiPriority.Now, cancellationToken: Ct));

        release.SetResult();
        Assert.Equal("ok", await running);
        Assert.Equal("ok", await queued);
        Assert.Equal("ok", await now);
    }

    [Fact]
    public void A_workflows_fill_is_read_in_any_case()
    {
        var workflow = System.Text.Json.JsonSerializer.Deserialize<ComfyWorkflow>(
            """{ "fill": { "Positive": { "node": "6", "input": "text" } } }""", ModJson.Options)!;

        Assert.True(workflow.Fill.TryGetValue("positive", out var input));
        Assert.Equal("6", input.Node);
    }

    [Theory]
    [InlineData("<think>let me see</think>\n\"Beat it, kid.\"", 200, "Beat it, kid.")]
    [InlineData("**Headline:** Big Night\nThe King lost.", 200, "Headline: Big Night\nThe King lost.")]
    [InlineData("One sentence here. Another one that runs on and on and on.", 30, "One sentence here.")]
    [InlineData("words words words words words", 14, "words words...")]
    public void An_answer_is_cleaned_and_cut(string answer, int maxLength, string expected)
    {
        Assert.Equal(expected, AiTextService.Clean(answer, maxLength));
    }

    [Fact]
    public void Only_the_first_lines_of_an_answer_are_kept()
    {
        const string answer = "You think that's a real hot rod?\nOr, slightly more edgy:\nChurch clothes, or real heat?";
        Assert.Equal("You think that's a real hot rod?", AiTextService.Clean(answer, 200, lines: 1));
        Assert.Equal(3, AiTextService.Clean(answer, 200, lines: 0).Split('\n').Length);
        Assert.Equal(1, BasePrompts().Prompt("talk")!.Lines);
        Assert.Equal(0, BasePrompts().Prompt(AiWriter.NewsUse)!.Lines);
    }

    [Fact]
    public async Task The_test_tells_the_models_and_the_answer_or_why_not()
    {
        var http = new FakeHttp((sent, _) => Task.FromResult(sent.Uri.AbsolutePath == "/api/tags"
            ? FakeHttp.Json("""{ "models": [ { "name": "llama3.2:3b" } ] }""")
            : FakeHttp.Json("""{ "message": { "content": "Welcome to the diner, pal." } }""")));
        var ok = await Service(http).TestAsync(new AiSettings { TextProvider = AiProviders.Ollama }, null, cancellationToken: Ct);
        Assert.True(ok.Ok);
        Assert.Contains("llama3.2:3b", ok.Message);
        Assert.Contains("Welcome to the diner, pal.", ok.Message);

        var down = new FakeHttp((_, _) => throw new HttpRequestException("No connection could be made"));
        var failed = await Service(down).TestAsync(new AiSettings(), null, cancellationToken: Ct);
        Assert.False(failed.Ok);
        Assert.Contains("No connection could be made", failed.Message);
    }

    // ----- the mods' prompts -----

    [Fact]
    public void The_base_game_has_a_prompt_for_every_use_and_a_portrait_workflow_whose_nodes_exist()
    {
        var prompts = BasePrompts();

        Assert.Empty(prompts.Problems);
        Assert.Contains("{language}", prompts.Persona);
        foreach (var use in new[] { AiTalkService.TalkUse, AiWriter.StreetTalkUse, AiWriter.NewsUse, AiWriter.AdUse, AiWriter.PortraitLookUse, "test" })
            Assert.NotNull(prompts.Prompt(use));

        var workflow = prompts.Workflow(AiWriter.PortraitUse);
        Assert.NotNull(workflow);
        Assert.False(string.IsNullOrWhiteSpace(workflow!.Checkpoint));
        foreach (var what in new[] { "positive", "negative", "seed", "width", "height", "checkpoint" })
            Assert.True(workflow.Fill.ContainsKey(what), what);
    }

    [Fact]
    public void A_mod_changes_one_prompt_and_replaces_a_workflow_whole()
    {
        using var temp = new TempDir();
        temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        temp.File("Content/streetcorsa/ai/prompts.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "ai", "prompts.json")));
        temp.File("Content/streetcorsa/ai/comfy_portrait.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "ai", "comfy_portrait.json")));
        temp.File("Content/tokyo/mod.json", """{ "id": "tokyo" }""");
        temp.File("Content/tokyo/ai/prompts.json", """{ "persona": "Tokyo, 2003. Write in {language}.", "prompts": { "talk": { "prompt": "Say {line} like a drifter", "maxLength": 90 } } }""");
        temp.File("Content/tokyo/ai/comfy_portrait.json", """
            { "checkpoint": "anime.safetensors", "prompt": "{look}", "fill": { "positive": { "node": "1", "input": "text" }, "seed": { "node": "9", "input": "seed" } },
              "workflow": { "1": { "inputs": { "text": "" }, "class_type": "CLIPTextEncode" } } }
            """);

        var prompts = AiPrompts.Load(ModSet.Load(temp.Combine("Content")));

        Assert.StartsWith("Tokyo, 2003", prompts.Persona);
        Assert.Equal("Say {line} like a drifter", prompts.Prompt("talk")!.Prompt);
        Assert.Equal(90, prompts.Prompt("talk")!.MaxLength);
        Assert.NotNull(prompts.Prompt(AiWriter.NewsUse));
        Assert.Equal("anime.safetensors", prompts.Workflow(AiWriter.PortraitUse)!.Checkpoint);
        // The fill names a node the graph does not have: told
        Assert.Contains(prompts.Problems, p => p.Contains("node 9"));
    }

    // ----- ComfyUI -----

    [Fact]
    public async Task ComfyUI_gets_the_workflow_filled_and_the_picture_is_fetched()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var polls = 0;
        var http = new FakeHttp((sent, _) => Task.FromResult(sent.Uri.AbsolutePath switch
        {
            "/prompt" => FakeHttp.Json("""{ "prompt_id": "abc", "number": 1, "node_errors": {} }"""),
            "/history/abc" => ++polls < 2
                ? FakeHttp.Json("{}")
                : FakeHttp.Json("""{ "abc": { "status": { "completed": true, "status_str": "success" }, "outputs": { "21": { "images": [ { "filename": "StreetCorsa_00001_.png", "subfolder": "", "type": "output" } ] } } } }"""),
            "/view" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        var comfy = new ComfyImageService(() => new AiSettings { ComfyEnabled = true, ComfyEndpoint = "http://comfy:8188" }, http.Client(), BasePrompts);

        var picture = await comfy.PaintAsync(AiWriter.PortraitUse, new TextValues { ["look"] = "greased hair, leather jacket", ["sex"] = "man", ["age"] = 31 }, cancellationToken: Ct);

        Assert.Equal(png, picture);
        var graph = http.Sent[0].Json!["prompt"]!;
        Assert.Equal("http://comfy:8188/prompt", http.Sent[0].Uri.ToString());
        var positive = graph["10"]!["inputs"]!["text"]!.GetValue<string>();
        Assert.Contains("greased hair, leather jacket", positive);
        Assert.Contains("man aged 31", positive);
        Assert.Contains("modern clothing", graph["12"]!["inputs"]!["text"]!.GetValue<string>());
        Assert.Equal("juggernautXL_ragnarokBy.safetensors", graph["8"]!["inputs"]!["ckpt_name"]!.GetValue<string>());
        Assert.NotEqual(0, graph["11"]!["inputs"]!["seed"]!.GetValue<long>());
        Assert.Equal(512, graph["22"]!["inputs"]!["width"]!.GetValue<int>());
        Assert.Contains(http.Sent, s => s.Uri.AbsolutePath == "/view" && s.Uri.Query.Contains("filename=StreetCorsa_00001_.png"));
    }

    [Fact]
    public async Task ComfyUI_off_or_down_gives_no_picture()
    {
        var down = new FakeHttp((_, _) => throw new HttpRequestException("refused"));
        Assert.Null(await new ComfyImageService(() => new AiSettings(), down.Client(), BasePrompts).PaintAsync(AiWriter.PortraitUse, new TextValues(), cancellationToken: Ct));
        Assert.Empty(down.Sent);

        var on = new ComfyImageService(() => new AiSettings { ComfyEnabled = true }, down.Client(), BasePrompts);
        Assert.Null(await on.PaintAsync(AiWriter.PortraitUse, new TextValues(), cancellationToken: Ct));
        Assert.False(on.IsAvailable);
        Assert.False((await on.TestAsync(null, cancellationToken: Ct)).Ok);
    }

    // ----- what the game writes with it -----

    private static AiWriter Writer(IAiText text, GameState? game = null, IAiImages? images = null, string? portraits = null) =>
        new(text, images ?? new ComfyImageService(() => new AiSettings(), new HttpClient()), () => game, carName: id => "1957 " + id,
            portraitsRoot: portraits, rules: () => new AiRules());

    [Fact]
    public async Task Street_talk_and_news_are_written_again_and_kept_next_to_the_originals()
    {
        var ai = new FakeAiText((use, values) => Task.FromResult<string?>(use == AiWriter.NewsUse
            ? "Headline: King Falls At Midnight\nThe King lost his crown on Route 9 last night."
            : "They say " + values["line"]));
        var writer = Writer(ai);
        var game = new GameState();
        var talk = new StreetTalkItem(game.Date, "Eddie bought a Chevy.");
        var piece = new NewsArticle { Headline = "King Beaten", Body = "The King lost." };

        writer.StreetTalkAdded(game, [talk]);
        writer.NewsAdded(game, [piece]);
        await writer.WhenIdle();

        Assert.Equal("Eddie bought a Chevy.", talk.Text);
        Assert.Equal("They say Eddie bought a Chevy.", talk.AiText);
        Assert.Equal("They say Eddie bought a Chevy.", AiWriter.Said(talk));
        Assert.Equal("King Falls At Midnight", AiWriter.Headline(piece));
        Assert.Equal("The King lost his crown on Route 9 last night.", AiWriter.Body(piece));
        Assert.Equal("King Beaten", piece.Headline);

        // Written in another language: the game's own words show
        talk.AiLanguage = piece.AiLanguage = "xx";
        Assert.Equal("Eddie bought a Chevy.", AiWriter.Said(talk));
        Assert.Equal("King Beaten", AiWriter.Headline(piece));
    }

    [Fact]
    public async Task Nothing_is_written_when_the_ai_is_off_or_answers_badly()
    {
        var off = new FakeAiText((_, _) => Task.FromResult<string?>("x"), available: false);
        var game = new GameState();
        var talk = new StreetTalkItem(game.Date, "line");
        Writer(off, game).StreetTalkAdded(game, [talk]);
        Assert.Empty(off.Asked);
        Assert.Null(talk.AiText);

        // A piece without a body line is no piece
        var oneLine = new FakeAiText((_, _) => Task.FromResult<string?>("Just a headline"));
        var piece = new NewsArticle { Headline = "h", Body = "b" };
        var writer = Writer(oneLine);
        writer.NewsAdded(game, [piece]);
        await writer.WhenIdle();
        Assert.Null(piece.AiBody);
        Assert.Equal("b", AiWriter.Body(piece));
    }

    [Fact]
    public async Task An_ad_is_made_livelier_and_kept_by_its_key()
    {
        var ai = new FakeAiText((_, values) => Task.FromResult<string?>($"HOT! {values["blurb"]} {values["engine"]}"));
        var game = new GameState();
        var writer = Writer(ai, game);

        var said = await writer.ImproveAdAsync("car", "Grandma's car, church on Sundays.", new Dictionary<string, object?> { ["engine"] = "327 V8" }, key: "listing-1");
        var again = await writer.ImproveAdAsync("car", "Grandma's car, church on Sundays.", key: "listing-1");

        Assert.Equal("HOT! Grandma's car, church on Sundays. 327 V8", said);
        Assert.Equal(said, again);
        Assert.Single(ai.Asked);
        Assert.Equal(AiPriority.Background, ai.Asked[0].Priority);
        Assert.Equal("car", ai.Asked[0].Values["kind"]);
    }

    [Fact]
    public async Task Ads_go_to_the_ai_one_at_a_time_and_each_gets_its_own_words()
    {
        var atOnce = 0;
        var most = 0;
        var ai = new FakeAiText(async (_, values) =>
        {
            most = Math.Max(most, Interlocked.Increment(ref atOnce));
            await Task.Delay(5);
            Interlocked.Decrement(ref atOnce);
            return "AI: " + values["blurb"];
        });
        var writer = Writer(ai, new GameState());
        var said = new string?[20];

        for (var i = 0; i < said.Length; i++)
        {
            var n = i;
            writer.Improve(new PartAdFacts("Cam", "Camshafts", false, true, 0.9, false), "ad " + n, better => said[n] = better);
        }

        await writer.WhenIdle();

        Assert.Equal(1, most);
        for (var i = 0; i < said.Length; i++) Assert.Equal("AI: ad " + i, said[i]);
    }

    [Fact]
    public async Task A_new_face_is_painted_into_the_saves_folder_and_shown_first()
    {
        using var temp = new TempDir();
        var game = new GameState { SaveName = "My Save" };
        var racer = new Opponent("Rosa Diaz", 24, Gender.Female, 93, 60) { Nickname = "Spark" };
        var images = new FakeImages([1, 2, 3]);
        var text = new FakeAiText((_, _) => Task.FromResult<string?>("short dark curls, cat-eye glasses"));
        var writer = Writer(text, game, images, temp.Path);

        Assert.Null(writer.PortraitOf(racer));
        writer.NewFace(game, racer);
        await writer.WhenIdle();

        var file = writer.PortraitOf(racer);
        Assert.NotNull(file);
        Assert.StartsWith(Path.Combine(temp.Path, "My Save"), file);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file!));
        Assert.Equal("short dark curls, cat-eye glasses", images.Values!["look"]);
        Assert.Equal("woman", images.Values["sex"]);

        // Painted once; the King never
        writer.NewFace(game, racer);
        writer.NewFace(game, new Opponent("The King", 40, Gender.Male, 100, 50) { IsKing = true });
        await writer.WhenIdle();
        Assert.Equal(1, images.Painted);
    }

    private sealed class FakeImages(byte[] picture) : IAiImages
    {
        public int Painted;
        public TextValues? Values;

        public bool IsAvailable => true;

        public Task<byte[]?> PaintAsync(string use, TextValues values, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Painted);
            Values = values;
            return Task.FromResult<byte[]?>(picture);
        }

        public Task<AiTestResult> TestAsync(string? endpoint, CancellationToken cancellationToken = default) => Task.FromResult(new AiTestResult(true, "ok"));
    }

    [Theory]
    [InlineData("HEADLINE: Rookie Wins\nBODY: A rookie won.", "Rookie Wins", "A rookie won.")]
    [InlineData("Rookie Wins\n\nA rookie won.\nThe crowd cheered.", "Rookie Wins", "A rookie won. The crowd cheered.")]
    public void A_piece_splits_into_headline_and_body(string written, string headline, string body)
    {
        var piece = AiWriter.SplitPiece(written);
        Assert.Equal((headline, body), piece);
    }

    // ----- the rivals' talk -----

    private sealed class OwnLines : ITalkService
    {
        public bool IsAvailable => true;

        public Task<string> GetMessageAsync(TalkContext context) => Task.FromResult($"own {context.Trigger} {context.Opponent.Name}");
    }

    private static TalkContext Talk(Opponent opponent, TalkTrigger trigger = TalkTrigger.OpponentSelected) =>
        new() { Opponent = opponent, Player = new Player("Player"), Trigger = trigger };

    private static AiRules TalkRules(double wait) => new() { TalkWaitSeconds = wait };

    [Fact]
    public async Task A_rival_says_the_ais_line_when_it_comes_in_time_else_the_games_own_and_the_late_one_next_time()
    {
        var eddie = new Opponent("Eddie", 30, Gender.Male, 92, 50);
        var quick = new AiTalkService(new OwnLines(), new FakeAiText((_, v) => Task.FromResult<string?>("AI: " + v["line"])), () => TalkRules(2));
        Assert.Equal("AI: own OpponentSelected Eddie", await quick.GetMessageAsync(Talk(eddie)));

        var release = new TaskCompletionSource<string?>();
        var slowAi = new FakeAiText((_, _) => release.Task);
        var slow = new AiTalkService(new OwnLines(), slowAi, () => TalkRules(0.2));
        Assert.Equal("own OpponentSelected Eddie", await slow.GetMessageAsync(Talk(eddie)));
        release.SetResult("late but good");
        await Task.Delay(100, Ct);
        Assert.Equal("late but good", await slow.GetMessageAsync(Talk(eddie)));
    }

    [Fact]
    public async Task A_rival_with_the_ai_off_says_the_games_own_line_at_once()
    {
        var ai = new FakeAiText((_, _) => Task.FromResult<string?>("never"), available: false);
        var talk = new AiTalkService(new OwnLines(), ai, () => TalkRules(5));
        var eddie = new Opponent("Eddie", 30, Gender.Male, 92, 50);

        var said = talk.GetMessageAsync(Talk(eddie));
        Assert.True(said.IsCompleted);
        Assert.Equal("own OpponentSelected Eddie", await said);
        talk.Prepare(Talk(eddie));
        Assert.Empty(ai.Asked);
    }

    [Fact]
    public async Task A_line_prepared_ahead_is_said_at_once()
    {
        var ai = new FakeAiText((_, v) => Task.FromResult<string?>("ready: " + v["name"]));
        var talk = new AiTalkService(new OwnLines(), ai, () => TalkRules(0));
        var rosa = new Opponent("Rosa", 24, Gender.Female, 93, 80);

        talk.Prepare(Talk(rosa));
        await Task.Delay(100, Ct);

        Assert.Equal("ready: Rosa", await talk.GetMessageAsync(Talk(rosa)));
        Assert.Equal(AiPriority.Background, ai.Asked[0].Priority);
    }

    [Fact]
    public async Task A_slow_line_never_lands_over_a_newer_one()
    {
        var hold = new TaskCompletionSource<string?>();
        var ai = new FakeAiText((_, v) => (string)v["name"]! == "Eddie" ? hold.Task : Task.FromResult<string?>("AI for " + v["name"]));
        var talk = new AiTalkService(new OwnLines(), ai, () => TalkRules(5));

        var older = talk.GetMessageAsync(Talk(new Opponent("Eddie", 30, Gender.Male, 92, 50)));
        var newer = await talk.GetMessageAsync(Talk(new Opponent("Rosa", 24, Gender.Female, 93, 80)));

        Assert.Equal("AI for Rosa", newer);
        Assert.Equal("AI for Rosa", await older);
        hold.SetResult("too late");
    }

    [Fact]
    public void The_ai_rules_are_in_the_base_games_rules()
    {
        var book = RuleBook.Read(ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, []), []);
        Assert.Equal(new AiRules().TalkWaitSeconds, book.Ai.TalkWaitSeconds);
        Assert.True(book.Ai.PaintNewFaces);
    }
}
