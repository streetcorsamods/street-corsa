using StreetCorsa.Logging;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Settings
{
    /// <summary>A text provider the player can pick</summary>
    public sealed record AiProviderChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>A talk detail the player can pick</summary>
    public sealed record TalkDetailChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// The AI section of the Settings card: the player's text provider (Ollama, an OpenAI-compatible API, Anthropic), its
    /// address, model and key, and ComfyUI for pictures, each with a Test. The ticks are saved at once; the provider, address,
    /// model and key together with the section's Save, so the game never asks one provider with another's address or model. The key is only ever kept encrypted (<see cref="AiKeys"/>), and the
    /// box never shows it back. Tests try what is typed, saved or not.
    /// </summary>
    public sealed class AiSettingsSection : ObservableObject
    {
        private readonly GameSettingsService _settingsService;
        private readonly IAiText _text;
        private readonly IAiImages _images;
        private readonly Action<string> _notSaved;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Settings");

        public AiSettingsSection(GameSettingsService settingsService, IAiText text, IAiImages images, Action<string> notSaved)
        {
            _settingsService = settingsService;
            _text = text;
            _images = images;
            _notSaved = notSaved;
            Providers = AiProviders.All.Select(id => new AiProviderChoice(id, GameText.Say(ProviderKey(id)))).ToList();
            Details = Services.Talk.TalkDetail.All.Select(id => new TalkDetailChoice(id, Services.Talk.TalkDetail.Name(id))).ToList();

            SaveCommand = new RelayCommand(OnSave);
            ForgetKeyCommand = new RelayCommand(OnForgetKey, () => HasSavedKey);
            TestCommand = new AsyncRelayCommand(OnTestAsync, () => !IsTesting, name: "Test AI");
            TestComfyCommand = new AsyncRelayCommand(OnTestComfyAsync, () => !IsTestingComfy, name: "Test ComfyUI");
            MakeModelCommand = new AsyncRelayCommand(OnMakeModelAsync, () => !IsMaking, name: "Make the Ollama model");
            Reload();
        }

        private AiSettings Ai => _settingsService.Current.Ai;

        private static string ProviderKey(string id) => id switch
        {
            AiProviders.OpenAi => "settings.ai.provider.openai",
            AiProviders.Anthropic => "settings.ai.provider.anthropic",
            _ => "settings.ai.provider.ollama"
        };

        public IReadOnlyList<AiProviderChoice> Providers { get; }

        public RelayCommand SaveCommand { get; }
        public RelayCommand ForgetKeyCommand { get; }
        public AsyncRelayCommand TestCommand { get; }
        public AsyncRelayCommand TestComfyCommand { get; }
        public AsyncRelayCommand MakeModelCommand { get; }

        /// <summary>The provider is Ollama: the game's own model can be made in it</summary>
        public bool IsOllama => _provider.Id == AiProviders.Ollama;

        private string _makeModelResult = GameText.Say("settings.ai.ollama.note");

        public string MakeModelResult
        {
            get => _makeModelResult;
            private set => SetProperty(ref _makeModelResult, value);
        }

        private bool _makeModelOk;

        public bool MakeModelOk
        {
            get => _makeModelOk;
            private set => SetProperty(ref _makeModelOk, value);
        }

        private bool _isMaking;

        public bool IsMaking
        {
            get => _isMaking;
            private set
            {
                if (SetProperty(ref _isMaking, value)) MakeModelCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// The game's model made in Ollama at the address typed (<c>ai/ollama.json</c>): from the model typed in the box when
        /// there is one (and it is not the game's own), else the file's base. Once made, its name goes in the model box, to Save.
        /// </summary>
        private async Task OnMakeModelAsync()
        {
            if (AiPrompts.Installed.Ollama is not { } model)
            {
                MakeModelOk = false;
                MakeModelResult = GameText.Say("settings.ai.ollama.none");
                return;
            }

            var from = Model.Trim().Length > 0 && !Model.Trim().Equals(model.Name, StringComparison.OrdinalIgnoreCase) ? Model.Trim() : model.From;
            IsMaking = true;
            MakeModelResult = GameText.Say("settings.ai.ollama.making", ("model", model.Name), ("from", from));
            try
            {
                var endpoint = Endpoint.Trim().Length > 0 ? Endpoint.Trim() : AiProviders.DefaultEndpoint(AiProviders.Ollama);
                var ollama = new OllamaTextProvider(AiHttp.Shared, endpoint, null, null);
                var examples = model.Examples
                    .Where(e => e.Player.Length > 0 && e.Answer.Length > 0)
                    .SelectMany(e => new[] { new AiTurn(false, e.Player), new AiTurn(true, e.Answer) })
                    .ToList();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                await ollama.CreateModelAsync(model.Name, from, model.SystemText, model.Parameters, examples, timeout.Token);

                _logger.Information("Made the Ollama model {Model} from {From} at {Endpoint}", model.Name, from, endpoint);
                Model = model.Name;
                MakeModelOk = true;
                MakeModelResult = GameText.Say("settings.ai.ollama.made", ("model", model.Name), ("from", from));
            }
            catch (Exception ex)
            {
                var reason = ex is OperationCanceledException ? GameText.Say("settings.ai.test.timeout") : ex.Message;
                _logger.Information("Could not make the Ollama model {Model} from {From}: {Error}", model.Name, from, reason);
                MakeModelOk = false;
                MakeModelResult = GameText.Say("settings.ai.ollama.failed", ("from", from), ("error", reason));
            }
            finally
            {
                IsMaking = false;
            }
        }

        /// <summary>What the settings in use hold, shown again (after a reset)</summary>
        public void Reload()
        {
            _provider = Providers.First(p => p.Id == AiProviders.Normalize(Ai.TextProvider));
            _endpoint = string.IsNullOrWhiteSpace(Ai.Endpoint) ? AiProviders.DefaultEndpoint(_provider.Id) : Ai.Endpoint;
            _model = Ai.Model ?? string.Empty;
            _comfyEndpoint = string.IsNullOrWhiteSpace(Ai.ComfyEndpoint) ? AiProviders.ComfyDefaultEndpoint : Ai.ComfyEndpoint;
            _typedKey = string.Empty;
            _testResult = _comfyResult = string.Empty;
            OnPropertyChanged(string.Empty);
            KeyCleared?.Invoke();
        }

        /// <summary>The typed key is gone (saved, or the settings reset): the password box empties</summary>
        public event Action? KeyCleared;

        /// <summary>The AI writes for the game; saved at once</summary>
        public bool Enabled
        {
            get => Ai.Enabled;
            set => SaveAtOnce(Ai.Enabled, value, v => Ai.Enabled = v);
        }

        public IReadOnlyList<TalkDetailChoice> Details { get; }

        /// <summary>How much a talk sends with each line; saved at once, as it costs nothing to change</summary>
        public TalkDetailChoice Detail
        {
            get => Details.First(d => d.Id == Services.Talk.TalkDetail.Normalize(Ai.TalkDetail));
            set
            {
                if (value == null) return;
                SaveAtOnce(Services.Talk.TalkDetail.Normalize(Ai.TalkDetail), value.Id, v => Ai.TalkDetail = v);
                OnPropertyChanged(nameof(DetailTokens));
            }
        }

        /// <summary>"Up to about 9,000 tokens with each line": the most a line sends at the detail picked, by the game's guess</summary>
        public string DetailTokens
        {
            get
            {
                var rules = Services.Talk.TalkDetail.Apply(StreetCorsa.Rules.RuleBook.Current.Ai.Talk, Ai.TalkDetail);
                var tokens = Services.Talk.TalkDetail.MostTokensALine(rules, AiPrompts.Installed, _text.ContextTokens);
                return GameText.Say("settings.ai.detail.tokens", ("tokens", (int)(Math.Round(tokens / 100.0) * 100)));
            }
        }

        /// <summary>ComfyUI paints the new rivals; saved at once</summary>
        public bool ComfyEnabled
        {
            get => Ai.ComfyEnabled;
            set => SaveAtOnce(Ai.ComfyEnabled, value, v => Ai.ComfyEnabled = v);
        }

        private AiProviderChoice _provider = null!;

        /// <summary>The provider, saved with the rest by Save. An address left at the old one's usual becomes the new one's usual.</summary>
        public AiProviderChoice Provider
        {
            get => _provider;
            set
            {
                if (value == null || value == _provider) return;
                var oldDefault = AiProviders.DefaultEndpoint(_provider.Id);
                _provider = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NeedsKey));
                OnPropertyChanged(nameof(IsOllama));
                OnPropertyChanged(nameof(ModelHint));
                if (string.IsNullOrWhiteSpace(Endpoint) || Endpoint.Trim() == oldDefault) Endpoint = AiProviders.DefaultEndpoint(value.Id);
            }
        }

        private string _endpoint = string.Empty;

        public string Endpoint
        {
            get => _endpoint;
            set => SetProperty(ref _endpoint, value ?? string.Empty);
        }

        private string _model = string.Empty;

        public string Model
        {
            get => _model;
            set => SetProperty(ref _model, value ?? string.Empty);
        }

        /// <summary>What an empty model box means for this provider</summary>
        public string ModelHint => GameText.Say(_provider.Id == AiProviders.Anthropic ? "settings.ai.model.hint.anthropic" : "settings.ai.model.hint");

        /// <summary>The provider takes a key (Ollama does not)</summary>
        public bool NeedsKey => _provider.Id != AiProviders.Ollama;

        private string _typedKey = string.Empty;

        /// <summary>A key as typed in the password box, kept here only until it is saved encrypted</summary>
        public string TypedKey
        {
            set => _typedKey = value ?? string.Empty;
        }

        public bool HasSavedKey => !string.IsNullOrEmpty(Ai.ApiKeyProtected);

        private string _comfyEndpoint = string.Empty;

        public string ComfyEndpoint
        {
            get => _comfyEndpoint;
            set => SetProperty(ref _comfyEndpoint, value ?? string.Empty);
        }

        private string _testResult = string.Empty;

        public string TestResult
        {
            get => _testResult;
            private set => SetProperty(ref _testResult, value);
        }

        private bool _testOk;

        public bool TestOk
        {
            get => _testOk;
            private set => SetProperty(ref _testOk, value);
        }

        private bool _isTesting;

        public bool IsTesting
        {
            get => _isTesting;
            private set
            {
                if (SetProperty(ref _isTesting, value)) TestCommand.RaiseCanExecuteChanged();
            }
        }

        private string _comfyResult = string.Empty;

        public string ComfyResult
        {
            get => _comfyResult;
            private set => SetProperty(ref _comfyResult, value);
        }

        private bool _comfyOk;

        public bool ComfyOk
        {
            get => _comfyOk;
            private set => SetProperty(ref _comfyOk, value);
        }

        private bool _isTestingComfy;

        public bool IsTestingComfy
        {
            get => _isTestingComfy;
            private set
            {
                if (SetProperty(ref _isTestingComfy, value)) TestComfyCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>A tick to disk at once; put back as it was when it could not be written</summary>
        private void SaveAtOnce<T>(T was, T value, Action<T> set, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
        {
            if (EqualityComparer<T>.Default.Equals(was, value)) return;
            set(value);
            if (!_settingsService.Save())
            {
                set(was);
                _notSaved(GameText.Say("settings.not_saved"));
            }

            OnPropertyChanged(property);
        }

        /// <summary>The settings as typed, for a Test or the Save</summary>
        private AiSettings Typed()
        {
            var typed = Ai.Clone();
            typed.TextProvider = _provider.Id;
            typed.Endpoint = Endpoint.Trim() == AiProviders.DefaultEndpoint(_provider.Id) || Endpoint.Trim().Length == 0 ? null : Endpoint.Trim();
            typed.Model = Model.Trim().Length == 0 ? null : Model.Trim();
            typed.ComfyEndpoint = ComfyEndpoint.Trim() == AiProviders.ComfyDefaultEndpoint || ComfyEndpoint.Trim().Length == 0 ? null : ComfyEndpoint.Trim();
            return typed;
        }

        private void OnSave()
        {
            var saved = Ai.Clone();
            var typed = Typed();
            try
            {
                if (_typedKey.Trim().Length > 0) typed.ApiKeyProtected = AiKeys.Protect(_typedKey);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                _logger.Error(ex, "The API key could not be encrypted");
                _notSaved(GameText.Say("settings.save_failed", ("error", ex.Message)));
                return;
            }

            _settingsService.Current.Ai = typed;
            if (!_settingsService.Save())
            {
                _settingsService.Current.Ai = saved;
                _notSaved(GameText.Say("settings.not_saved"));
                return;
            }

            _logger.Information("AI settings saved: {Provider} at {Endpoint}, model {Model}, key {Key}, ComfyUI {Comfy}",
                typed.TextProvider, typed.Endpoint ?? "(usual)", typed.Model ?? "(first)", typed.ApiKeyProtected == null ? "none" : "saved", typed.ComfyEnabled);
            Reload();
        }

        private void OnForgetKey()
        {
            var saved = Ai.ApiKeyProtected;
            Ai.ApiKeyProtected = null;
            if (!_settingsService.Save())
            {
                Ai.ApiKeyProtected = saved;
                _notSaved(GameText.Say("settings.not_saved"));
            }

            OnPropertyChanged(nameof(HasSavedKey));
            RelayCommand.RaiseCanExecuteChanged();
        }

        private async Task OnTestAsync()
        {
            IsTesting = true;
            TestResult = GameText.Say("settings.ai.testing");
            try
            {
                var key = _typedKey.Trim().Length > 0 ? _typedKey.Trim() : AiKeys.Unprotect(Ai.ApiKeyProtected);
                var result = await _text.TestAsync(Typed(), key);
                TestOk = result.Ok;
                TestResult = result.Message;
            }
            finally
            {
                IsTesting = false;
            }
        }

        private async Task OnTestComfyAsync()
        {
            IsTestingComfy = true;
            ComfyResult = GameText.Say("settings.ai.testing");
            try
            {
                var result = await _images.TestAsync(ComfyEndpoint.Trim());
                ComfyOk = result.Ok;
                ComfyResult = result.Message;
            }
            finally
            {
                IsTestingComfy = false;
            }
        }
    }
}
