using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using StreetCorsa.Logging;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Ai
{
    /// <summary>Pictures from the player's ComfyUI. Never needed: a null means the game's own picture stays. Never throws.</summary>
    public interface IAiImages
    {
        /// <summary>The player has switched ComfyUI on, and it has not just failed</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// A picture (the image file's bytes, PNG as ComfyUI saves it) from the mods' workflow for <paramref name="use"/>, its
        /// prompt filled with <paramref name="values"/>; null when ComfyUI is off, has no such workflow, fails or takes too long
        /// </summary>
        Task<byte[]?> PaintAsync(string use, TextValues values, CancellationToken cancellationToken = default);

        /// <summary>Whether ComfyUI answers at <paramref name="endpoint"/> (its system stats)</summary>
        Task<AiTestResult> TestAsync(string? endpoint, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// ComfyUI's API: the workflow posted to /prompt with the game's values filled into the nodes the mod names, /history
    /// asked until the picture is done, the picture fetched from /view. One picture at a time.
    /// </summary>
    public sealed class ComfyImageService : IAiImages
    {
        /// <summary>How often /history is asked while a picture is made (a request each time, so not too often)</summary>
        private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);

        private readonly Func<AiSettings> _settings;
        private readonly HttpClient _http;
        private readonly Func<AiPrompts> _prompts;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Ai");
        private readonly SemaphoreSlim _one = new(1, 1);
        private readonly Random _random = new();
        private readonly AiBackoff _rest = new();

        /// <summary>The most a picture may be: a 512x512 PNG is well under a megabyte</summary>
        public const int MostPictureBytes = 16 * 1024 * 1024;

        /// <summary>The most ComfyUI's JSON answers may be (/prompt, /history, /system_stats)</summary>
        public const int MostJsonBytes = 4 * 1024 * 1024;

        public ComfyImageService(Func<AiSettings> settings, HttpClient http, Func<AiPrompts>? prompts = null)
        {
            _settings = settings;
            _http = http;
            _prompts = prompts ?? (() => AiPrompts.Installed);
        }

        private static Rules.AiRules Rules => StreetCorsa.Rules.RuleBook.Current.Ai;

        public bool IsAvailable => _settings().ComfyEnabled && !_rest.IsResting;

        private string Endpoint(string? endpoint = null)
        {
            var address = endpoint ?? _settings().ComfyEndpoint;
            return (string.IsNullOrWhiteSpace(address) ? AiProviders.ComfyDefaultEndpoint : address.Trim()).TrimEnd('/');
        }

        public async Task<byte[]?> PaintAsync(string use, TextValues values, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable) return null;
            if (_prompts().Workflow(use) is not { } workflow)
            {
                _logger.Debug("No ComfyUI workflow {Use} in the mods", use);
                return null;
            }

            try
            {
                await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, Rules.ImageTimeoutSeconds)));
                var started = DateTime.UtcNow;
                var picture = await PaintNowAsync(workflow, values, timeout.Token).ConfigureAwait(false);
                _logger.Information("ComfyUI painted {Use} in {Seconds:0} s ({Bytes} bytes)", use, (DateTime.UtcNow - started).TotalSeconds, picture.Length);
                return picture;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("ComfyUI {Use} took longer than {Seconds} s: given up", use, Rules.ImageTimeoutSeconds);
                return null;
            }
            catch (HttpRequestException ex)
            {
                if (_rest.Rest(Rules.RetryAfterFailureSeconds))
                    _logger.Warning("ComfyUI could not be reached ({Error}); tried again in {Seconds} s", ex.Message, Rules.RetryAfterFailureSeconds);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning("ComfyUI {Use} failed: {Error}", use, ex.Message);
                return null;
            }
            finally
            {
                _one.Release();
            }
        }

        /// <summary>The name ComfyUI saves the game's pictures under, in its own output folder</summary>
        public const string Prefix = "StreetCorsa";

        /// <summary>The graph with the game's values in the nodes the workflow names</summary>
        public JsonObject Graph(ComfyWorkflow workflow, TextValues values, long seed)
        {
            var graph = (JsonObject)workflow.Workflow.DeepClone();
            Set("positive", GameText.Fill(workflow.Prompt, values));
            Set("negative", GameText.Fill(workflow.Negative, values));
            Set("seed", seed);
            Set("width", workflow.Width);
            Set("height", workflow.Height);
            if (!string.IsNullOrWhiteSpace(workflow.Checkpoint)) Set("checkpoint", workflow.Checkpoint);
            Set("prefix", Prefix);
            // Every save node writes under the game's own name, whatever the graph says: a mod's graph picks no file names
            foreach (var (_, node) in graph)
            {
                if (node?["class_type"]?.ToString() is { } kind && ComfyWorkflow.SaveNodes.Contains(kind) && node["inputs"] is JsonObject inputs)
                    inputs["filename_prefix"] = Prefix;
            }

            return graph;

            void Set(string what, JsonNode? value)
            {
                if (workflow.Fill.TryGetValue(what, out var input) && graph[input.Node]?["inputs"] is JsonObject inputs)
                    inputs[input.Input] = value;
            }
        }

        private async Task<byte[]> PaintNowAsync(ComfyWorkflow workflow, TextValues values, CancellationToken cancellationToken)
        {
            var endpoint = Endpoint();
            var seed = (long)(_random.NextDouble() * 1_000_000_000_000);
            var body = new JsonObject { ["prompt"] = Graph(workflow, values, seed), ["client_id"] = Guid.NewGuid().ToString("N") };

            string queuedText;
            using (var post = new HttpRequestMessage(HttpMethod.Post, endpoint + "/prompt"))
            {
                post.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using var queued = await _http.SendAsync(post, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                queuedText = await AiHttp.ReadTextAsync(queued, MostJsonBytes, cancellationToken).ConfigureAwait(false);
                if (!queued.IsSuccessStatusCode) throw new AiProviderException($"HTTP {(int)queued.StatusCode} from /prompt: {AiProviders.Snippet(queuedText)}");
            }

            var promptId = JsonNode.Parse(queuedText)?["prompt_id"]?.ToString();
            if (string.IsNullOrEmpty(promptId)) throw new AiProviderException($"/prompt gave no prompt_id: {AiProviders.Snippet(queuedText)}");

            while (true)
            {
                await Task.Delay(PollEvery, cancellationToken).ConfigureAwait(false);
                var history = JsonNode.Parse(await GetTextAsync($"{endpoint}/history/{Uri.EscapeDataString(promptId)}", cancellationToken).ConfigureAwait(false));
                if (history?[promptId] is not JsonObject run) continue;

                if (run["status"]?["status_str"]?.ToString() == "error") throw new AiProviderException("ComfyUI reported an error running the workflow");
                if (FirstImage(run["outputs"] as JsonObject) is not { } image)
                {
                    if (run["status"]?["completed"]?.GetValue<bool>() == true) throw new AiProviderException("The workflow finished without a picture");
                    continue;
                }

                var query = $"filename={Uri.EscapeDataString(image.File)}&subfolder={Uri.EscapeDataString(image.Subfolder)}&type={Uri.EscapeDataString(image.Type)}";
                using var view = await _http.GetAsync($"{endpoint}/view?{query}", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                view.EnsureSuccessStatusCode();
                var picture = await AiHttp.ReadBytesAsync(view, MostPictureBytes, cancellationToken).ConfigureAwait(false);
                if (!IsPicture(picture)) throw new AiProviderException("/view gave something that is not a PNG or JPEG picture");
                return picture;
            }
        }

        /// <summary>A GET of ComfyUI's JSON, as text: an error status throws as <see cref="HttpClient.GetStringAsync(string)"/> does</summary>
        private async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await AiHttp.ReadTextAsync(response, MostJsonBytes, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The bytes start as a PNG or a JPEG file does: only a picture is saved as a portrait</summary>
        public static bool IsPicture(byte[]? bytes) =>
            bytes != null && (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
                              || bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]));

        /// <summary>The first picture among a run's outputs</summary>
        private static (string File, string Subfolder, string Type)? FirstImage(JsonObject? outputs)
        {
            foreach (var (_, output) in outputs ?? [])
            {
                if (output?["images"] is JsonArray { Count: > 0 } images && images[0] is JsonObject image && image["filename"]?.ToString() is { Length: > 0 } file)
                    return (file, image["subfolder"]?.ToString() ?? string.Empty, image["type"]?.ToString() ?? "output");
            }

            return null;
        }

        public async Task<AiTestResult> TestAsync(string? endpoint, CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var stats = JsonNode.Parse(await GetTextAsync(Endpoint(endpoint) + "/system_stats", timeout.Token).ConfigureAwait(false));
                var version = stats?["system"]?["comfyui_version"]?.ToString() ?? "?";
                var device = (stats?["devices"] as JsonArray)?.FirstOrDefault()?["name"]?.ToString() ?? "?";
                _rest.Clear();
                var workflow = _prompts().Workflow(AiWriter.PortraitUse);
                return new AiTestResult(true, GameText.Say("settings.ai.comfy.ok", ("version", version), ("device", device),
                    ("checkpoint", workflow?.Checkpoint ?? "?")));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var reason = ex is OperationCanceledException ? GameText.Say("settings.ai.test.timeout") : ex.Message;
                return new AiTestResult(false, GameText.Say("settings.ai.test.failed", ("error", reason)));
            }
        }
    }
}
