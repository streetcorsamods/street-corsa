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
        /// The context window the provider is asked to work in (tokens), when the game sets it (Ollama's num_ctx); null
        /// when the provider keeps its own
        /// </summary>
        int? ContextTokens { get; }

        /// <summary>
        /// The answer to the mods' prompt <paramref name="use"/> filled with <paramref name="values"/>, cleaned and cut to
        /// its length; null when the AI is off, the prompt is missing, the queue is full, or the request failed or timed out
        /// </summary>
        Task<string?> WriteAsync(string use, TextValues values, AiPriority priority = AiPriority.Background, CancellationToken cancellationToken = default);

        /// <summary>
        /// The next words of a talk: the mods' prompt <paramref name="use"/> filled with <paramref name="values"/> goes after
        /// the persona as who the AI is and what it knows, and <paramref name="turns"/> are the talk so far (the first and the
        /// last the player's). Always the player's own request. Null as for <see cref="WriteAsync"/>.
        /// </summary>
        Task<string?> ChatAsync(string use, TextValues values, IReadOnlyList<AiTurn> turns, CancellationToken cancellationToken = default);

        /// <summary>
        /// The mods' prompt <paramref name="use"/> filled with <paramref name="values"/> answered as a JSON object of
        /// <paramref name="schema"/>, as written: not cleaned or cut, for the game to read (a clerk's search). The player's
        /// own request. Null as for <see cref="WriteAsync"/>; an answer that is no such object is the caller's to throw away.
        /// </summary>
        Task<string?> JsonAsync(string use, TextValues values, System.Text.Json.Nodes.JsonObject schema, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

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
        private readonly AiBackoff _rest = new();

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
                if (AiProviders.Normalize(settings.TextProvider) == AiProviders.Anthropic && AiKeys.KeyFor(settings, settings.TextProvider, settings.Endpoint) == null)
                    return false;
                return !_rest.IsResting;
            }
        }

        public Task<string?> WriteAsync(string use, TextValues values, AiPriority priority = AiPriority.Background, CancellationToken cancellationToken = default) =>
            AskAsync(use, priority, (prompts, prompt) =>
                new AiTextRequest(SystemPrompt(prompts), GameText.Fill(prompt.Prompt, values), prompt.MaxTokens, prompt.Temperature), cancellationToken);

        public Task<string?> JsonAsync(string use, TextValues values, System.Text.Json.Nodes.JsonObject schema, CancellationToken cancellationToken = default) =>
            AskAsync(use, AiPriority.Now, (prompts, prompt) =>
                new AiTextRequest(SystemPrompt(prompts), GameText.Fill(prompt.Prompt, values), prompt.MaxTokens, prompt.Temperature, Format: schema),
                cancellationToken);

        public int? ContextTokens
        {
            get
            {
                var context = Rules.Ollama.ContextTokens;
                return AiProviders.Normalize(_settings().TextProvider) == AiProviders.Ollama && context > 0 ? context : null;
            }
        }

        public Task<string?> ChatAsync(string use, TextValues values, IReadOnlyList<AiTurn> turns, CancellationToken cancellationToken = default) =>
            turns.Count == 0 ? Task.FromResult<string?>(null) : AskAsync(use, AiPriority.Now, (prompts, prompt) =>
            {
                var system = SystemPrompt(prompts) + "\n\n" + GameText.Fill(prompt.Prompt, values);
                var fitted = Fit(turns, system, prompt.MaxTokens, ContextTokens);
                if (fitted.Count < turns.Count)
                    _logger.Debug("AI {Use}: the {Dropped} oldest turns left out to fit the context window", use, turns.Count - fitted.Count);
                var request = new AiTextRequest(system, string.Empty, prompt.MaxTokens, prompt.Temperature, fitted);
                if (ContextTokens is { } context && AiTokens.Estimate(request) + prompt.MaxTokens > context)
                    _logger.Warning("AI {Use}: about {Tokens} tokens asked in a context of {Context}: the provider will cut it", use,
                        AiTokens.Estimate(request) + prompt.MaxTokens, context);
                return request;
            }, cancellationToken);

        /// <summary>
        /// The newest of <paramref name="turns"/> that fit a context window of <paramref name="context"/> tokens beside the
        /// system prompt and room for the answer, from a player's turn on (the APIs want the talk to start with one); the
        /// last turn always goes. All of them when the window is not known.
        /// </summary>
        public static IReadOnlyList<AiTurn> Fit(IReadOnlyList<AiTurn> turns, string system, int answerTokens, int? context)
        {
            if (context is not { } window || turns.Count == 0) return turns;

            var room = window - AiTokens.Estimate(system) - AiTokens.PerMessage - answerTokens - AiTokens.Margin;
            var used = turns.Sum(AiTokens.Estimate);
            var first = 0;
            while (first < turns.Count - 1 && (used > room || turns[first].Mine))
                used -= AiTokens.Estimate(turns[first++]);
            return first == 0 ? turns : turns.Skip(first).ToList();
        }

        private async Task<string?> AskAsync(string use, AiPriority priority, Func<AiPrompts, AiPrompt, AiTextRequest> ask, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (!IsAvailable) return null;
                if (HeldForRace)
                {
                    // Nobody waits on the player's own in a race; the background's (the race's own news and talk) is asked after it
                    if (priority == AiPriority.Now)
                    {
                        _logger.Debug("AI {Use} not asked during a race: the model is off the GPU", use);
                        return null;
                    }

                    _logger.Debug("AI {Use} waits for the race to end: the model is off the GPU", use);
                    if (!await RaceOverAsync(cancellationToken).ConfigureAwait(false)) return null;
                    continue;
                }

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

                var (held, answer) = await RunAsync(use, prompts, prompt, ask, cancellationToken).ConfigureAwait(false);
                if (!held) return answer;
            }
        }

        /// <summary>
        /// A request that has its place, sent once its turn comes: held (and the place given up) when a race began while it
        /// waited in line, as sending it would load the model again
        /// </summary>
        private async Task<(bool Held, string? Answer)> RunAsync(string use, AiPrompts prompts, AiPrompt prompt,
            Func<AiPrompts, AiPrompt, AiTextRequest> ask, CancellationToken cancellationToken)
        {
            try
            {
                await WaitTurnAsync(cancellationToken).ConfigureAwait(false);
                if (!IsAvailable) return (false, null);
                if (HeldForRace) return (true, null);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Rules.RequestTimeoutSeconds)));

                var request = ask(prompts, prompt);
                var started = DateTime.UtcNow;
                var answer = await Provider().CompleteAsync(request, timeout.Token).ConfigureAwait(false);
                // A JSON answer is the game's to read, as written
                var cleaned = request.Format != null ? answer.Trim() : Clean(answer, prompt.MaxLength, prompt.Lines);
                _logger.Debug("AI {Use} in {Ms} ms: {Text}", use, (int)(DateTime.UtcNow - started).TotalMilliseconds, cleaned);
                return (false, cleaned.Length == 0 ? null : cleaned);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (false, null);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("AI {Use} timed out after {Seconds} s: the game's own words stand", use, Rules.RequestTimeoutSeconds);
                return (false, null);
            }
            catch (HttpRequestException ex)
            {
                Rest(ex.Message);
                return (false, null);
            }
            catch (Exception ex)
            {
                // A provider that answered badly (a wrong model, a refused key): the game's own words, and a line in the log
                _logger.Warning(ex, "AI {Use} failed", use);
                return (false, null);
            }
            finally
            {
                Leave();
            }
        }

        // ----- races -----

        private volatile bool _racing;

        // Done when no race runs: the background's requests held for one wait on it
        private TaskCompletionSource _raceOver = Over();

        // Whether the model was on the GPU when the race began (and was let go): only then is it loaded again after
        private Task<bool> _letGo = Task.FromResult(false);

        private static TaskCompletionSource Over()
        {
            var over = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            over.SetResult();
            return over;
        }

        /// <summary>A race runs and the local model was let go for it: nothing is asked, which would load it again</summary>
        private bool HeldForRace => _racing && Rules.Ollama.UnloadForRaces && AiProviders.Normalize(_settings().TextProvider) == AiProviders.Ollama;

        /// <summary>The race is over (true), or the wait was called off (false)</summary>
        private async Task<bool> RaceOverAsync(CancellationToken cancellationToken)
        {
            Task over;
            lock (_lock) over = _raceOver.Task;
            try
            {
                await over.WaitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// A race starts (<paramref name="racing"/>) or is over: a local model on the GPU lets go of it for the race, and is
        /// loaded again in the background after (<see cref="StreetCorsa.Rules.OllamaRules.UnloadForRaces"/>); one that was not
        /// loaded is left off. The background's requests asked meanwhile wait for the end of the race. Other providers are not
        /// touched. Never throws, never waits.
        /// </summary>
        public void SetRacing(bool racing)
        {
            OllamaTextProvider? ollama = null;
            try
            {
                if (Rules.Ollama.UnloadForRaces && _settings().Enabled) ollama = Provider() as OllamaTextProvider;
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not {What} the AI model for the race: {Error}", racing ? "let go of" : "load", ex.Message);
            }

            // The start and the end of a race may be told on different threads: the flip and what was let go at the start
            // change together, so the end always loads again after the start it ends
            TaskCompletionSource? over = null;
            Task<bool>? letGo = null;
            lock (_lock)
            {
                if (_racing == racing) return;
                _racing = racing;
                if (racing)
                {
                    _raceOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _letGo = ollama != null ? Task.Run(() => LetGoAsync(ollama)) : Task.FromResult(false);
                }
                else
                {
                    over = _raceOver;
                    letGo = _letGo;
                }
            }

            try
            {
                if (ollama != null && letGo != null) _ = LoadAgainAsync(ollama, letGo);
            }
            finally
            {
                // After the load is sent: the requests that waited go to a model already being loaded
                over?.TrySetResult();
            }
        }

        /// <summary>Lets go of the model if it is on the GPU: whether it was</summary>
        private async Task<bool> LetGoAsync(OllamaTextProvider ollama)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, Rules.RequestTimeoutSeconds)));
                bool loaded;
                try
                {
                    loaded = await ollama.IsLoadedAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (AiProviderException)
                {
                    // An Ollama that cannot say: let go of it all the same
                    loaded = true;
                }

                if (!loaded)
                {
                    _logger.Debug("The AI model was not on the GPU at the start of the race");
                    return false;
                }

                await ollama.UnloadAsync(timeout.Token).ConfigureAwait(false);
                _logger.Information("The AI model let go of the GPU for the race");
                return true;
            }
            catch (Exception ex)
            {
                // Ollama not running is nothing to let go of
                _logger.Debug("Could not let go of the AI model for the race: {Error}", ex.Message);
                return false;
            }
        }

        private async Task LoadAgainAsync(OllamaTextProvider ollama, Task<bool> letGo)
        {
            try
            {
                if (!await letGo.ConfigureAwait(false)) return;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, Rules.RequestTimeoutSeconds)));
                var started = DateTime.UtcNow;
                await ollama.LoadAsync(timeout.Token).ConfigureAwait(false);
                _logger.Information("The AI model is loaded again after the race in {Ms} ms", (int)(DateTime.UtcNow - started).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                // The first line after waits for it instead
                _logger.Debug("Could not load the AI model again after the race: {Error}", ex.Message);
            }
        }

        /// <summary>The provider could not be reached: left alone for a while, told once</summary>
        private void Rest(string reason)
        {
            if (!_rest.Rest(Rules.RetryAfterFailureSeconds)) return;
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

        /// <summary>The game's own Ollama model, taken when the player names none and Ollama has it</summary>
        private static string? GameModel => AiPrompts.Installed.Ollama?.Name;

        private IAiTextProvider Provider()
        {
            var settings = _settings();
            // The key only for the provider and address it was typed for
            var key = AiKeys.KeyFor(settings, settings.TextProvider, settings.Endpoint);
            // The rules' Ollama tuning too: a career whose mod sets other values gets a provider that sends them
            var ollama = Rules.Ollama;
            var signature = string.Join("|", AiProviders.Normalize(settings.TextProvider), settings.Endpoint, settings.Model, settings.ApiKeyProtected,
                settings.KeyProvider, settings.KeyEndpoint, ollama.ContextTokens, ollama.KeepAlive, ollama.RepeatPenalty);
            lock (_lock)
            {
                if (_provider is { } made && made.Signature == signature) return made.Provider;
                var provider = AiProviders.Create(_http, settings.TextProvider, settings.Endpoint, settings.Model, key, ollama, GameModel);
                _provider = (signature, provider);
                _rest.Clear();
                return provider;
            }
        }

        public async Task<AiTestResult> TestAsync(AiSettings settings, string? apiKey, CancellationToken cancellationToken = default)
        {
            // Sent as every other request is: a different context size would make Ollama load the model again
            var provider = AiProviders.Create(_http, settings.TextProvider, settings.Endpoint, settings.Model, apiKey, Rules.Ollama, GameModel);
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
                _rest.Clear();
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

        /// <summary>A place to run: at once while fewer than the most run, else in line (the player's first); false when the line is full (for the player's, full of the player's)</summary>
        private Task<bool> EnterAsync(AiPriority priority, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_running < Math.Clamp(Rules.MaxConcurrent, 1, 4))
                {
                    _running++;
                    return Task.FromResult(true);
                }

                // A full line drops background work only: the player's own request waits ahead of it anyway
                var waitingAhead = priority == AiPriority.Now ? _waitingNow.Count : _waitingNow.Count + _waitingLater.Count;
                if (waitingAhead >= Math.Max(1, Rules.MaxQueued)) return Task.FromResult(false);

                var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var node = (priority == AiPriority.Now ? _waitingNow : _waitingLater).AddLast(waiting);
                if (cancellationToken.CanBeCanceled)
                {
                    var registration = cancellationToken.Register(() =>
                    {
                        lock (_lock)
                        {
                            if (node.List == null) return;
                            node.List.Remove(node);
                        }

                        waiting.TrySetResult(false);
                    });
                    // A talk's token lives as long as the talk: once the place is had (or given up), the token lets go of it
                    waiting.Task.ContinueWith(_ => registration.Unregister(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
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
