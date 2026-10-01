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
        private DateTime _restUntil = DateTime.MinValue;

        public ComfyImageService(Func<AiSettings> settings, HttpClient http, Func<AiPrompts>? prompts = null)
        {
            _settings = settings;
            _http = http;
            _prompts = prompts ?? (() => AiPrompts.Installed);
        }

        private static Rules.AiRules Rules => StreetCorsa.Rules.RuleBook.Current.Ai;

        public bool IsAvailable => _settings().ComfyEnabled && DateTime.UtcNow >= _restUntil;

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
                _restUntil = DateTime.UtcNow.AddSeconds(Math.Max(0, Rules.RetryAfterFailureSeconds));
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
            Set("prefix", "StreetCorsa");
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

            using var queued = await _http.PostAsync(endpoint + "/prompt",
                new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
            var queuedText = await queued.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!queued.IsSuccessStatusCode) throw new AiProviderException($"HTTP {(int)queued.StatusCode} from /prompt: {Snippet(queuedText)}");

            var promptId = JsonNode.Parse(queuedText)?["prompt_id"]?.ToString();
            if (string.IsNullOrEmpty(promptId)) throw new AiProviderException($"/prompt gave no prompt_id: {Snippet(queuedText)}");

            while (true)
            {
                await Task.Delay(PollEvery, cancellationToken).ConfigureAwait(false);
                var history = JsonNode.Parse(await _http.GetStringAsync($"{endpoint}/history/{Uri.EscapeDataString(promptId)}", cancellationToken).ConfigureAwait(false));
                if (history?[promptId] is not JsonObject run) continue;

                if (run["status"]?["status_str"]?.ToString() == "error") throw new AiProviderException("ComfyUI reported an error running the workflow");
                if (FirstImage(run["outputs"] as JsonObject) is not { } image)
                {
                    if (run["status"]?["completed"]?.GetValue<bool>() == true) throw new AiProviderException("The workflow finished without a picture");
                    continue;
                }

                var query = $"filename={Uri.EscapeDataString(image.File)}&subfolder={Uri.EscapeDataString(image.Subfolder)}&type={Uri.EscapeDataString(image.Type)}";
                return await _http.GetByteArrayAsync($"{endpoint}/view?{query}", cancellationToken).ConfigureAwait(false);
            }
        }

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
                var stats = JsonNode.Parse(await _http.GetStringAsync(Endpoint(endpoint) + "/system_stats", timeout.Token).ConfigureAwait(false));
                var version = stats?["system"]?["comfyui_version"]?.ToString() ?? "?";
                var device = (stats?["devices"] as JsonArray)?.FirstOrDefault()?["name"]?.ToString() ?? "?";
                _restUntil = DateTime.MinValue;
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

        private static string Snippet(string text) => text.Length <= 300 ? text.Trim() : text[..300].Trim() + "...";
    }
}
