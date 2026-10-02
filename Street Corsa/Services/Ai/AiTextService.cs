using System.Net.Http;
using System.Text.RegularExpressions;
using StreetCorsa.Logging;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Ai
{
    /// <summary>Who is waiting on an answer: the player (a rival about to speak) goes before the background's work</summary>
    public enum AiPriority
    {
        Background,
        Now
    }

    /// <summary>What a Test found: whether it worked, and what to tell the player (the models offered, or why not)</summary>
    public sealed record AiTestResult(bool Ok, string Message);

    /// <summary>
    /// Words from the player's AI. Never needed: every caller has the game's own words to say, and a null here means
    /// "say those". Never throws.
    /// </summary>
    public interface IAiText
    {
        /// <summary>The player has switched it on and set it up, and it has not just failed</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// The answer to the mods' prompt <paramref name="use"/> filled with <paramref name="values"/>, cleaned and cut to
        /// its length; null when the AI is off, the prompt is missing, the queue is full, or the request failed or timed out
        /// </summary>
        Task<string?> WriteAsync(string use, TextValues values, AiPriority priority = AiPriority.Background, CancellationToken cancellationToken = default);

        /// <summary>Tries <paramref name="settings"/> (not yet saved, maybe): the models the provider offers, and a short answer</summary>
        Task<AiTestResult> TestAsync(AiSettings settings, string? apiKey, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The text service: the provider the settings name (rebuilt when they change), at most
    /// <see cref="Rules.AiRules.MaxConcurrent"/> requests at once with the player's before the background's, a gap between
    /// requests, a timeout on each, and a rest after the provider could not be reached. The prompts are the mods'
    /// (<see cref="AiPrompts"/>): the persona goes as the system prompt with the player's language and units in it.
    /// </summary>
    public sealed partial class AiTextService : IAiText
    {
        private readonly Func<AiSettings> _settings;
        private readonly HttpClient _http;
        private readonly Func<AiPrompts> _prompts;
        private readonly Func<StreetCorsa.Rules.AiRules> _rules;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Ai");

        private readonly object _lock = new();
        private readonly LinkedList<TaskCompletionSource<bool>> _waitingNow = new();
        private readonly LinkedList<TaskCompletionSource<bool>> _waitingLater = new();
        private int _running;
        private DateTime _nextStart = DateTime.MinValue;
        private DateTime _restUntil = DateTime.MinValue;

        private (string Signature, IAiTextProvider Provider)? _provider;

        /// <param name="settings">The settings in use, read at every request</param>
        /// <param name="http">The game's one HTTP client (<see cref="AiHttp.Shared"/>)</param>
        /// <param name="prompts">The mods' prompts; the installed ones when not given</param>
        /// <param name="rules">The figures it works by; the game in play's when not given</param>
        public AiTextService(Func<AiSettings> settings, HttpClient http, Func<AiPrompts>? prompts = null, Func<StreetCorsa.Rules.AiRules>? rules = null)
        {
            _settings = settings;
            _http = http;
            _prompts = prompts ?? (() => AiPrompts.Installed);
            _rules = rules ?? (() => StreetCorsa.Rules.RuleBook.Current.Ai);
        }

        private StreetCorsa.Rules.AiRules Rules => _rules();

        public bool IsAvailable
        {
            get
            {
                var settings = _settings();
                if (!settings.Enabled) return false;
                if (AiProviders.Normalize(settings.TextProvider) == AiProviders.Anthropic && AiKeys.Unprotect(settings.ApiKeyProtected) == null) return false;
                lock (_lock) return DateTime.UtcNow >= _restUntil;
            }
        }

        public async Task<string?> WriteAsync(string use, TextValues values, AiPriority priority = AiPriority.Background, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable) return null;
            var prompts = _prompts();
            if (prompts.Prompt(use) is not { } prompt)
            {
                _logger.Debug("No AI prompt {Use} in the mods", use);
                return null;
            }

            if (!await EnterAsync(priority, cancellationToken).ConfigureAwait(false))
            {
                _logger.Debug("AI queue full: {Use} left to the game's own words", use);
                return null;
            }

            try
            {
                await WaitTurnAsync(cancellationToken).ConfigureAwait(false);
                if (!IsAvailable) return null;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Rules.RequestTimeoutSeconds)));

                var request = new AiTextRequest(SystemPrompt(prompts), GameText.Fill(prompt.Prompt, values), prompt.MaxTokens, prompt.Temperature);
                var started = DateTime.UtcNow;
                var answer = await Provider().CompleteAsync(request, timeout.Token).ConfigureAwait(false);
                var cleaned = Clean(answer, prompt.MaxLength, prompt.Lines);
                _logger.Debug("AI {Use} in {Ms} ms: {Text}", use, (int)(DateTime.UtcNow - started).TotalMilliseconds, cleaned);
                return cleaned.Length == 0 ? null : cleaned;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("AI {Use} timed out after {Seconds} s: the game's own words stand", use, Rules.RequestTimeoutSeconds);
                return null;
            }
            catch (HttpRequestException ex)
            {
                Rest(ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                // A provider that answered badly (a wrong model, a refused key): the game's own words, and a line in the log
                _logger.Warning("AI {Use} failed: {Error}", use, ex.Message);
                return null;
            }
            finally
            {
                Leave();
            }
        }

        /// <summary>The provider could not be reached: left alone for a while, told once</summary>
        private void Rest(string reason)
        {
            lock (_lock)
            {
                if (DateTime.UtcNow < _restUntil) return;
                _restUntil = DateTime.UtcNow.AddSeconds(Math.Max(0, Rules.RetryAfterFailureSeconds));
            }

            _logger.Warning("The AI could not be reached ({Reason}); tried again in {Seconds} s", reason, Rules.RetryAfterFailureSeconds);
        }

        /// <summary>The persona, in the player's language and units</summary>
        public static string SystemPrompt(AiPrompts prompts) => GameText.Fill(prompts.Persona, PlayerValues());

        /// <summary>What every prompt may name: the player's language and units, the game's title</summary>
        public static TextValues PlayerValues() => new()
        {
            ["language"] = GameText.Say("language.name"),
            ["units"] = GameText.Say(Helpers.Locale.Units == Helpers.UnitSystem.Metric ? "settings.units.metric" : "settings.units.imperial"),
            ["title"] = StreetCorsa.Rules.GameFile.Installed.Title
        };

        private IAiTextProvider Provider()
        {
            var settings = _settings();
            var key = AiKeys.Unprotect(settings.ApiKeyProtected);
            var signature = string.Join("|", AiProviders.Normalize(settings.TextProvider), settings.Endpoint, settings.Model, settings.ApiKeyProtected);
            lock (_lock)
            {
                if (_provider is { } made && made.Signature == signature) return made.Provider;
                var provider = AiProviders.Create(_http, settings.TextProvider, settings.Endpoint, settings.Model, key);
                _provider = (signature, provider);
                _restUntil = DateTime.MinValue;
                return provider;
            }
        }

        public async Task<AiTestResult> TestAsync(AiSettings settings, string? apiKey, CancellationToken cancellationToken = default)
        {
            var provider = AiProviders.Create(_http, settings.TextProvider, settings.Endpoint, settings.Model, apiKey);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Rules.RequestTimeoutSeconds)));
            try
            {
                var models = await provider.ModelsAsync(timeout.Token).ConfigureAwait(false);
                var model = await provider.ModelAsync(timeout.Token).ConfigureAwait(false);
                var prompts = _prompts();
                var prompt = prompts.Prompt("test");
                var answer = await provider.CompleteAsync(new AiTextRequest(SystemPrompt(prompts),
                    GameText.Fill(prompt?.Prompt ?? "Say hello in five words.", PlayerValues()), prompt?.MaxTokens ?? 40, prompt?.Temperature ?? 0.7), timeout.Token).ConfigureAwait(false);
                var said = Clean(answer, prompt?.MaxLength ?? 120, prompt?.Lines ?? 1);
                lock (_lock) _restUntil = DateTime.MinValue;
                _logger.Information("AI test: {Provider} {Model} answered \"{Answer}\"", settings.TextProvider, model, said);
                return new AiTestResult(said.Length > 0, GameText.Say(said.Length > 0 ? "settings.ai.test.ok" : "settings.ai.test.empty",
                    ("model", model), ("models", models.Count), ("answer", said)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var reason = ex is OperationCanceledException ? GameText.Say("settings.ai.test.timeout") : ex.Message;
                _logger.Information("AI test of {Provider} at {Endpoint} failed: {Error}", settings.TextProvider, settings.Endpoint, reason);
                return new AiTestResult(false, GameText.Say("settings.ai.test.failed", ("error", reason)));
            }
        }

        // ----- the queue -----

        /// <summary>A place to run: at once while fewer than the most run, else in line (the player's first); false when the line is full</summary>
        private Task<bool> EnterAsync(AiPriority priority, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_running < Math.Clamp(Rules.MaxConcurrent, 1, 4))
                {
                    _running++;
                    return Task.FromResult(true);
                }

                if (_waitingNow.Count + _waitingLater.Count >= Math.Max(1, Rules.MaxQueued)) return Task.FromResult(false);

                var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var node = (priority == AiPriority.Now ? _waitingNow : _waitingLater).AddLast(waiting);
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() =>
                    {
                        lock (_lock)
                        {
                            if (node.List == null) return;
                            node.List.Remove(node);
                        }

                        waiting.TrySetResult(false);
                    });
                }

                return waiting.Task;
            }
        }

        /// <summary>The place goes to the next in line (the player's first), or is free</summary>
        private void Leave()
        {
            TaskCompletionSource<bool>? next;
            lock (_lock)
            {
                var node = _waitingNow.First ?? _waitingLater.First;
                if (node == null)
                {
                    _running--;
                    return;
                }

                node.List!.Remove(node);
                next = node.Value;
            }

            next.TrySetResult(true);
        }

        /// <summary>At least <see cref="Rules.AiRules.MinSecondsBetween"/> since the last request started</summary>
        private Task WaitTurnAsync(CancellationToken cancellationToken)
        {
            TimeSpan wait;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var start = _nextStart > now ? _nextStart : now;
                _nextStart = start.AddSeconds(Math.Max(0, Rules.MinSecondsBetween));
                wait = start - now;
            }

            return wait > TimeSpan.Zero ? Task.Delay(wait, cancellationToken) : Task.CompletedTask;
        }

        // ----- the answer -----

        /// <summary>
        /// The words of an answer as the game shows them: a model's thinking taken out, markdown and the quotes around it
        /// gone, only its first <paramref name="lines"/> lines (0: all; line breaks stay, the paper's piece is a headline and a
        /// body), and cut to <paramref name="maxLength"/> at a sentence or a word.
        /// </summary>
        public static string Clean(string? answer, int maxLength, int lines = 0)
        {
            if (string.IsNullOrWhiteSpace(answer)) return string.Empty;

            var text = Thinking().Replace(answer, string.Empty);
            text = text.Replace("**", string.Empty).Replace("__", string.Empty).Replace("\r", string.Empty);
            var kept = text.Split('\n')
                .Select(line => line.Trim().TrimStart('#', '>', '-', '*', ' ').Trim())
                .Where(line => line.Length > 0)
                .Select(line => Unquoted(line))
                .ToList();
            if (lines > 0 && kept.Count > lines) kept = kept.Take(lines).ToList();
            text = string.Join("\n", kept).Trim();
            text = Unquoted(text);
            return Cut(text, maxLength);
        }

        private static string Unquoted(string text)
        {
            text = text.Trim();
            foreach (var (open, close) in new[] { ('"', '"'), ('“', '”'), ('\'', '\''), ('«', '»') })
            {
                if (text.Length >= 2 && text[0] == open && text[^1] == close && text.IndexOf(close, 1) == text.Length - 1)
                    return text[1..^1].Trim();
            }

            return text;
        }

        /// <summary>At most <paramref name="maxLength"/> characters: whole sentences when one ends past the half, else whole words</summary>
        public static string Cut(string text, int maxLength)
        {
            if (text.Length <= maxLength) return text;

            var cut = text[..maxLength];
            var sentence = cut.LastIndexOfAny(['.', '!', '?']);
            if (sentence >= maxLength / 2) return cut[..(sentence + 1)].Trim();

            var word = cut.LastIndexOfAny([' ', '\n']);
            return (word > 0 ? cut[..word] : cut).TrimEnd(',', ';', ':', ' ') + "...";
        }

        [GeneratedRegex(@"<think(ing)?>.*?(</think(ing)?>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex Thinking();
    }
}
