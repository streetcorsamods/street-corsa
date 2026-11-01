using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// What one request asks: who the AI is, what it is to write, and how long the answer may be. With
    /// <paramref name="Turns"/> it is a talk: those are sent as the messages so far (the first the player's), and
    /// <paramref name="Prompt"/> is not sent. With <paramref name="Format"/> the answer is a JSON object of that schema:
    /// asked of the providers that can be held to one (Ollama, OpenAI-compatible), and only by the prompt of the rest.
    /// </summary>
    public sealed record AiTextRequest(string System, string Prompt, int MaxTokens, double Temperature, IReadOnlyList<AiTurn>? Turns = null,
        JsonObject? Format = null)
    {
        /// <summary>The messages after the system prompt: the talk's turns, or the prompt as the one user message</summary>
        public IEnumerable<AiTurn> Messages => Turns is { Count: > 0 } turns ? turns : [new AiTurn(false, Prompt)];
    }

    /// <summary>One turn of a talk: the player's (<paramref name="Mine"/> false: the user's message) or the AI's own</summary>
    public sealed record AiTurn(bool Mine, string Text)
    {
        public string Role => Mine ? "assistant" : "user";

        public JsonObject ToJson() => new() { ["role"] = Role, ["content"] = Text };
    }

    /// <summary>A provider answered, but not with words the game can use; its message is for the log and the Test button</summary>
    public sealed class AiProviderException(string message) : Exception(message);

    /// <summary>One kind of text API</summary>
    public interface IAiTextProvider
    {
        /// <summary>The answer's text; throws when there is none (the caller falls back to the game's own words)</summary>
        Task<string> CompleteAsync(AiTextRequest request, CancellationToken cancellationToken);

        /// <summary>The models the provider offers; throws when it cannot be reached or refuses the key</summary>
        Task<IReadOnlyList<string>> ModelsAsync(CancellationToken cancellationToken);

        /// <summary>The model asked: the one set, else the provider's first or usual one</summary>
        Task<string> ModelAsync(CancellationToken cancellationToken);
    }

    /// <summary>The text providers the game speaks to, their usual addresses and models</summary>
    public static class AiProviders
    {
        /// <summary>Ollama's own API (/api/chat), local</summary>
        public const string Ollama = "ollama";

        /// <summary>Any OpenAI-compatible API (/v1/chat/completions): LM Studio, OpenRouter, OpenAI, Ollama's /v1</summary>
        public const string OpenAi = "openai";

        /// <summary>Anthropic's Messages API</summary>
        public const string Anthropic = "anthropic";

        public const string ComfyDefaultEndpoint = "http://127.0.0.1:8188";

        /// <summary>The model asked of Anthropic when the player names none</summary>
        public const string AnthropicDefaultModel = "claude-opus-5-5";

        public static IReadOnlyList<string> All { get; } = [Ollama, OpenAi, Anthropic];

        /// <summary>The most a provider's answer may be: a chat answer or a list of models is a few KB</summary>
        public const int MostAnswerBytes = 4 * 1024 * 1024;

        private static readonly StreetCorsa.Logging.IAppLogger Logger = StreetCorsa.Logging.AppLoggerFactory.CreateLogger("Ai");

        public static string DefaultEndpoint(string? provider) => Normalize(provider) switch
        {
            OpenAi => "http://127.0.0.1:1234/v1",
            Anthropic => "https://api.anthropic.com",
            _ => "http://localhost:11434"
        };

        // The kinds of model a provider lists that cannot chat: speech, pictures, embeddings, moderation
        private static readonly string[] NotChat =
            ["embed", "whisper", "tts", "dall-e", "image", "moderation", "transcribe", "audio", "speech", "rerank", "sora"];

        /// <summary>Whether a listed model looks like one that chats: the one picked when the player names none</summary>
        public static bool CanChat(string model) =>
            !NotChat.Any(word => model.Contains(word, StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether two model names are one: Ollama lists "streetcorsa:latest" for "streetcorsa"</summary>
        public static bool SameModel(string listed, string named) =>
            Tagged(listed).Equals(Tagged(named), StringComparison.OrdinalIgnoreCase);

        /// <summary>A model's name as Ollama lists it: "latest" when no tag is given (nothing stays nothing)</summary>
        internal static string Tagged(string model)
        {
            var name = model.Trim();
            return name.Length == 0 || name.Contains(':') ? name : name + ":latest";
        }

        /// <summary>The start of what a provider answered, for the log and the Test button</summary>
        public static string Snippet(string text) => text.Length <= 300 ? text.Trim() : text[..300].Trim() + "...";

        /// <summary>
        /// Where an address points, for the log and the player: its scheme, host and port only. A user name and password
        /// typed into it ("https://user:key@host"), its path and its query never go into the log.
        /// </summary>
        public static string PlaceOf(string? address) =>
            Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) ? PlaceOf(uri) : "(an address that does not read)";

        /// <inheritdoc cref="PlaceOf(string?)"/>
        public static string PlaceOf(Uri? uri) =>
            uri is { IsAbsoluteUri: true } && uri.Host.Length > 0 ? $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}"
            : uri is { IsAbsoluteUri: true } ? uri.Scheme + ":" : "another address on the same host";

        /// <summary>The address a provider is asked at: the one typed, else its usual one, without a slash at the end</summary>
        public static string Address(string? provider, string? endpoint) =>
            (string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint(provider) : endpoint.Trim()).TrimEnd('/');

        /// <summary>Two addresses of one provider are the same place (an empty one is its usual address)</summary>
        public static bool SameAddress(string? provider, string? one, string? other) =>
            Address(provider, one).Equals(Address(provider, other), StringComparison.OrdinalIgnoreCase);

        /// <summary>The address is on this machine (localhost, 127.x.x.x, ::1): what is sent there never leaves it</summary>
        public static bool IsLocal(string? provider, string? endpoint) => IsLocalAddress(Address(provider, endpoint));

        /// <summary>A full address is on this machine (localhost, 127.x.x.x, ::1): what answers there works this machine's graphics card</summary>
        public static bool IsLocalAddress(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsLoopback;

        /// <summary>
        /// A key may go to the address: over https, or to this machine. Plain http to anywhere else would put the key on the
        /// network for anyone to read, so it is not sent there.
        /// </summary>
        public static bool KeySafeAt(string? provider, string? endpoint) =>
            Uri.TryCreate(Address(provider, endpoint), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);

        /// <summary><paramref name="endpoint"/> without the given path at its end ("…/v1" typed for an API that adds its own)</summary>
        public static string Without(string endpoint, params string[] paths)
        {
            var trimmed = endpoint.TrimEnd('/');
            foreach (var path in paths)
            {
                if (trimmed.EndsWith(path, StringComparison.OrdinalIgnoreCase)) return trimmed[..^path.Length];
            }

            return trimmed;
        }

        /// <summary>A provider id the game knows; anything else is Ollama</summary>
        public static string Normalize(string? provider) =>
            All.FirstOrDefault(p => p.Equals(provider?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Ollama;

        /// <summary>The provider asked through <paramref name="http"/></summary>
        /// <param name="ollama">What else Ollama is sent (context, keep-alive); its own defaults when not given</param>
        /// <param name="gameModel">
        /// The game's own model (<c>ai/ollama.json</c>): with no <paramref name="model"/> named, Ollama takes it when it
        /// has it, before the first it lists
        /// </param>
        public static IAiTextProvider Create(HttpClient http, string? provider, string? endpoint, string? model, string? apiKey,
            StreetCorsa.Rules.OllamaRules? ollama = null, string? gameModel = null)
        {
            var kind = Normalize(provider);
            var address = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint(kind) : endpoint.Trim();
            var named = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
            if (!string.IsNullOrWhiteSpace(apiKey) && !KeySafeAt(kind, address))
            {
                Logger.Warning("The API key is not sent to {Endpoint}: plain http to another machine", PlaceOf(address));
                apiKey = null;
            }

            // Ollama takes no key (Settings has no box for one): a key never goes to it
            return kind switch
            {
                OpenAi => new OpenAiTextProvider(http, address, named, apiKey),
                Anthropic => new AnthropicTextProvider(http, address, named, apiKey),
                _ => new OllamaTextProvider(http, address, named, null, ollama, gameModel)
            };
        }
    }

    /// <summary>What every provider shares: the address, the JSON in and out, a readable error</summary>
    public abstract class AiTextProviderBase(HttpClient http, string endpoint, string? model, string? apiKey) : IAiTextProvider
    {
        protected HttpClient Http { get; } = http;
        protected string Endpoint { get; } = endpoint.TrimEnd('/');
        protected string? ApiKey { get; } = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

        private string? _model = model;

        public abstract Task<string> CompleteAsync(AiTextRequest request, CancellationToken cancellationToken);

        public abstract Task<IReadOnlyList<string>> ModelsAsync(CancellationToken cancellationToken);

        /// <summary>The model taken before any other when the player names none and the provider has it</summary>
        protected virtual string? Preferred => null;

        public virtual async Task<string> ModelAsync(CancellationToken cancellationToken)
        {
            if (_model != null) return _model;
            // The preferred one, else the first that chats: a provider lists its embedding, speech and picture models
            // alongside, and Ollama lists the newest first, so a model pulled for anything else would win
            var models = await ModelsAsync(cancellationToken).ConfigureAwait(false);
            var preferred = Preferred is { Length: > 0 } name ? models.FirstOrDefault(m => AiProviders.SameModel(m, name)) : null;
            return _model = preferred ?? models.FirstOrDefault(AiProviders.CanChat)
                ?? throw new AiProviderException("The provider has no chat model to offer");
        }

        protected virtual void Authorize(HttpRequestMessage request)
        {
            if (ApiKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }

        protected async Task<JsonNode> SendAsync(HttpMethod method, string url, JsonObject? body, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, url);
            Authorize(request);
            if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var text = await AiHttp.ReadTextAsync(response, AiProviders.MostAnswerBytes, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new AiProviderException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Snippet(ErrorOf(text) ?? text)}");

            try
            {
                return JsonNode.Parse(text) ?? throw new AiProviderException("An empty answer");
            }
            catch (System.Text.Json.JsonException)
            {
                throw new AiProviderException($"An answer that is not JSON: {Snippet(text)}");
            }
        }

        /// <summary>The message of an error answer, where the API puts one ({"error": "..."} or {"error": {"message": "..."}})</summary>
        private static string? ErrorOf(string text)
        {
            try
            {
                var error = JsonNode.Parse(text)?["error"];
                return error is JsonObject obj ? obj["message"]?.ToString() : error?.ToString();
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>The system prompt as the first message, then the request's</summary>
        protected static JsonArray WithSystem(AiTextRequest request) =>
            new([new JsonObject { ["role"] = "system", ["content"] = request.System }, .. request.Messages.Select(turn => (JsonNode)turn.ToJson())]);

        protected static string Snippet(string text) => AiProviders.Snippet(text);

        protected static string Text(JsonNode? node) =>
            node?.GetValueKind() == System.Text.Json.JsonValueKind.String ? node.GetValue<string>() : string.Empty;
    }

    /// <summary>Ollama's own API: POST /api/chat (not streamed), GET /api/tags; an address typed with /api or /v1 on it goes without</summary>
    public sealed class OllamaTextProvider(HttpClient http, string endpoint, string? model, string? apiKey, StreetCorsa.Rules.OllamaRules? tuning = null,
        string? gameModel = null)
        : AiTextProviderBase(http, AiProviders.Without(endpoint, "/api", "/v1"), model, apiKey)
    {
        private readonly StreetCorsa.Rules.OllamaRules? _tuning = tuning;

        protected override string? Preferred => gameModel;

        public override async Task<string> CompleteAsync(AiTextRequest request, CancellationToken cancellationToken)
        {
            var body = new JsonObject
            {
                ["model"] = await ModelAsync(cancellationToken).ConfigureAwait(false),
                ["stream"] = false,
                ["messages"] = WithSystem(request),
                ["options"] = Options(request)
            };
            if (_tuning is { KeepAlive.Length: > 0 }) body["keep_alive"] = _tuning.KeepAlive;
            if (request.Format != null) body["format"] = request.Format.DeepClone();
            var answer = await SendAsync(HttpMethod.Post, Endpoint + "/api/chat", body, cancellationToken).ConfigureAwait(false);
            LogTokens(request, answer);
            return Text(answer["message"]?["content"]);
        }

        private static readonly StreetCorsa.Logging.IAppLogger Logger = StreetCorsa.Logging.AppLoggerFactory.CreateLogger("Ai");

        /// <summary>Ollama's own count of the prompt against the game's guess (<see cref="AiTokens"/>); a prompt that filled the window was cut</summary>
        private void LogTokens(AiTextRequest request, JsonNode answer)
        {
            if (answer["prompt_eval_count"]?.GetValueKind() != System.Text.Json.JsonValueKind.Number) return;
            // A count that is no whole number (a proxy, another build) is no reason to throw the answer away
            if (!answer["prompt_eval_count"]!.AsValue().TryGetValue<int>(out var read)) return;
            var estimated = AiTokens.Estimate(request);
            Logger.Debug("Ollama read {Read} prompt tokens (the game guessed {Estimated}) and wrote {Wrote}", read, estimated,
                answer["eval_count"]?.ToString() ?? "?");
            if (_tuning is { ContextTokens: > 0 } tuning && read >= tuning.ContextTokens - request.MaxTokens)
                Logger.Warning("Ollama read {Read} prompt tokens in a window of {Context}: the start of the prompt was likely cut", read, tuning.ContextTokens);
        }

        /// <summary>What the model is asked to answer within: its length and freedom, and the game's tuning when given</summary>
        private JsonObject Options(AiTextRequest request)
        {
            var options = new JsonObject { ["num_predict"] = request.MaxTokens, ["temperature"] = request.Temperature };
            if (_tuning == null) return options;
            if (_tuning.ContextTokens > 0) options["num_ctx"] = _tuning.ContextTokens;
            if (_tuning.RepeatPenalty > 0) options["repeat_penalty"] = _tuning.RepeatPenalty;
            return options;
        }

        /// <summary>
        /// Loads the model onto the GPU with nothing asked of it (a chat of no messages), with the context every request is
        /// sent, so the next line does not wait for it; kept as long as the game's keep-alive
        /// </summary>
        public async Task LoadAsync(CancellationToken cancellationToken)
        {
            var body = new JsonObject
            {
                ["model"] = await ModelAsync(cancellationToken).ConfigureAwait(false),
                ["stream"] = false,
                ["messages"] = new JsonArray()
            };
            if (_tuning is { ContextTokens: > 0 } tuning) body["options"] = new JsonObject { ["num_ctx"] = tuning.ContextTokens };
            if (_tuning is { KeepAlive.Length: > 0 }) body["keep_alive"] = _tuning.KeepAlive;
            await SendAsync(HttpMethod.Post, Endpoint + "/api/chat", body, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The model is on the GPU now (GET /api/ps): asked before it is let go, so only a model that was is loaded again</summary>
        public async Task<bool> IsLoadedAsync(CancellationToken cancellationToken)
        {
            var model = AiProviders.Tagged(await ModelAsync(cancellationToken).ConfigureAwait(false));
            var answer = await SendAsync(HttpMethod.Get, Endpoint + "/api/ps", null, cancellationToken).ConfigureAwait(false);
            return (answer["models"] as JsonArray ?? []).Any(m =>
                string.Equals(AiProviders.Tagged(Text(m?["name"])), model, StringComparison.OrdinalIgnoreCase)
                || string.Equals(AiProviders.Tagged(Text(m?["model"])), model, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Lets go of the model, so its memory on the card is free (a chat of no messages kept alive for 0)</summary>
        public async Task UnloadAsync(CancellationToken cancellationToken)
        {
            var body = new JsonObject
            {
                ["model"] = await ModelAsync(cancellationToken).ConfigureAwait(false),
                ["stream"] = false,
                ["messages"] = new JsonArray(),
                ["keep_alive"] = 0
            };
            await SendAsync(HttpMethod.Post, Endpoint + "/api/chat", body, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Makes (or makes again) the model <paramref name="name"/> in Ollama from <paramref name="from"/>, with a system
        /// prompt, parameters and example messages of its own (POST /api/create, not streamed)
        /// </summary>
        public async Task CreateModelAsync(string name, string from, string system, JsonObject parameters, IReadOnlyList<AiTurn> examples,
            CancellationToken cancellationToken)
        {
            var body = new JsonObject
            {
                ["model"] = name,
                ["from"] = from,
                ["system"] = system,
                ["parameters"] = parameters.DeepClone(),
                ["stream"] = false
            };
            if (examples.Count > 0) body["messages"] = new JsonArray(examples.Select(turn => (JsonNode)turn.ToJson()).ToArray());

            var answer = await SendAsync(HttpMethod.Post, Endpoint + "/api/create", body, cancellationToken).ConfigureAwait(false);
            var status = Text(answer["status"]);
            if (status.Length > 0 && !status.Equals("success", StringComparison.OrdinalIgnoreCase))
                throw new AiProviderException($"Ollama said: {Snippet(status)}");
        }

        public override async Task<IReadOnlyList<string>> ModelsAsync(CancellationToken cancellationToken)
        {
            var answer = await SendAsync(HttpMethod.Get, Endpoint + "/api/tags", null, cancellationToken).ConfigureAwait(false);
            return (answer["models"] as JsonArray ?? []).Select(m => Text(m?["name"])).Where(n => n.Length > 0).ToList();
        }
    }

    /// <summary>
    /// An OpenAI-compatible API: POST {base}/chat/completions, GET {base}/models, where the base is the address given
    /// with "/v1" after it unless it already ends so. A key, if any, as a bearer token.
    /// </summary>
    public sealed class OpenAiTextProvider(HttpClient http, string endpoint, string? model, string? apiKey)
        : AiTextProviderBase(http, endpoint, model, apiKey)
    {
        private string Base => Endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? Endpoint : Endpoint + "/v1";

        public override async Task<string> CompleteAsync(AiTextRequest request, CancellationToken cancellationToken)
        {
            var body = new JsonObject
            {
                ["model"] = await ModelAsync(cancellationToken).ConfigureAwait(false),
                ["messages"] = WithSystem(request),
                ["max_tokens"] = request.MaxTokens,
                ["temperature"] = request.Temperature,
                ["stream"] = false
            };
            if (request.Format != null)
                body["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject { ["name"] = "answer", ["schema"] = request.Format.DeepClone() }
                };
            var answer = await SendAsync(HttpMethod.Post, Base + "/chat/completions", body, cancellationToken).ConfigureAwait(false);
            return Text(answer["choices"]?[0]?["message"]?["content"]);
        }

        public override async Task<IReadOnlyList<string>> ModelsAsync(CancellationToken cancellationToken)
        {
            var answer = await SendAsync(HttpMethod.Get, Base + "/models", null, cancellationToken).ConfigureAwait(false);
            return (answer["data"] as JsonArray ?? []).Select(m => Text(m?["id"])).Where(n => n.Length > 0).ToList();
        }
    }

    /// <summary>
    /// Anthropic's Messages API: POST /v1/messages with the key in x-api-key and the API version header, GET /v1/models.
    /// No temperature is sent (current models take none), and room is left for the model's own thinking, which counts
    /// against max_tokens; only the text blocks of the answer are words. A refusal is no answer. An address typed with
    /// /v1 on it goes without, as the paths add their own.
    /// </summary>
    public sealed class AnthropicTextProvider(HttpClient http, string endpoint, string? model, string? apiKey)
        : AiTextProviderBase(http, AiProviders.Without(endpoint, "/v1"), model ?? AiProviders.AnthropicDefaultModel, apiKey)
    {
        public const string ApiVersion = "2023-06-01";

        /// <summary>The least max_tokens asked: a short line still needs room for the thinking before it</summary>
        public const int MinMaxTokens = 2048;

        protected override void Authorize(HttpRequestMessage request)
        {
            if (ApiKey != null) request.Headers.Add("x-api-key", ApiKey);
            request.Headers.Add("anthropic-version", ApiVersion);
        }

        public override async Task<string> CompleteAsync(AiTextRequest request, CancellationToken cancellationToken)
        {
            if (ApiKey == null) throw new AiProviderException("Anthropic needs an API key");
            var body = new JsonObject
            {
                ["model"] = await ModelAsync(cancellationToken).ConfigureAwait(false),
                ["max_tokens"] = Math.Max(request.MaxTokens, MinMaxTokens),
                ["system"] = request.System,
                ["messages"] = new JsonArray(request.Messages.Select(turn => (JsonNode)turn.ToJson()).ToArray())
            };
            var answer = await SendAsync(HttpMethod.Post, Endpoint + "/v1/messages", body, cancellationToken).ConfigureAwait(false);
            if (Text(answer["stop_reason"]) == "refusal") throw new AiProviderException("The model declined to answer");

            return string.Concat((answer["content"] as JsonArray ?? [])
                .Where(block => Text(block?["type"]) == "text")
                .Select(block => Text(block?["text"])));
        }

        public override async Task<IReadOnlyList<string>> ModelsAsync(CancellationToken cancellationToken)
        {
            if (ApiKey == null) throw new AiProviderException("Anthropic needs an API key");
            var answer = await SendAsync(HttpMethod.Get, Endpoint + "/v1/models", null, cancellationToken).ConfigureAwait(false);
            return (answer["data"] as JsonArray ?? []).Select(m => Text(m?["id"])).Where(n => n.Length > 0).ToList();
        }
    }
}
