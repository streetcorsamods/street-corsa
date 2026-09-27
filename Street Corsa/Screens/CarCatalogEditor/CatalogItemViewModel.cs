using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.CarCatalogEditor
{
    /// <summary>
    /// View model for a single car catalog item in the editor
    /// Wraps CarDefinition + CarProfile for display and editing
    /// </summary>
    public class CatalogItemViewModel : ObservableObject
    {
        public CarDefinition Definition { get; }
        public CarProfile Profile { get; }

        private decimal _basePrice;
        private float _dealerPrecedence;
        private EngineOptionViewModel? _stockEngine;
        private bool _stockEngineResolved;
        private bool _isDirty;
        private readonly Lazy<IReadOnlyList<EngineOptionViewModel>> _engineOptions;
        private readonly bool _hasEngineCatalog;
        private readonly Lazy<IReadOnlyList<EngineLayoutOptionViewModel>> _layoutOptions;
        private EngineLayout? _engineLayout;

        /// <param name="engineOptions">Engine builds the car could leave the factory with, best match first; empty without a parts catalog</param>
        public CatalogItemViewModel(CarDefinition definition, CarProfile profile, IReadOnlyList<EngineOptionViewModel>? engineOptions = null)
            : this(definition, profile, engineOptions is { Count: > 0 }, () => engineOptions ?? Array.Empty<EngineOptionViewModel>(), () => EngineLayout.Front)
        {
        }

        /// <param name="hasEngineCatalog">There is a parts catalog to pick a factory engine from</param>
        /// <param name="engineOptions">
        /// Makes the engine builds, best match first. Called the first time the row needs them: ranking every
        /// build for every car up front is what made the editor slow to open, and a virtualized list only
        /// shows a screenful of rows.
        /// </param>
        /// <param name="guessLayout">
        /// Where the car's data says the engine is; called off the UI thread the first time the row needs it, as it
        /// reads and decodes the car's data
        /// </param>
        public CatalogItemViewModel(CarDefinition definition, CarProfile profile, bool hasEngineCatalog, Func<IReadOnlyList<EngineOptionViewModel>> engineOptions,
            Func<EngineLayout> guessLayout)
        {
            Definition = definition;
            Profile = profile;
            _basePrice = profile.BasePrice;
            _dealerPrecedence = profile.DealerPrecedence;
            _engineLayout = profile.EngineLayout;
            _hasEngineCatalog = hasEngineCatalog;
            _engineOptions = new Lazy<IReadOnlyList<EngineOptionViewModel>>(engineOptions, LazyThreadSafetyMode.None);
            _layoutOptions = new Lazy<IReadOnlyList<EngineLayoutOptionViewModel>>(() => EngineLayoutOptionViewModel.All(guessLayout), LazyThreadSafetyMode.None);
        }

        /// <summary>The guess from the car's data first, then each layout to set by hand</summary>
        public IReadOnlyList<EngineLayoutOptionViewModel> EngineLayoutOptions => _layoutOptions.Value;

        /// <summary>
        /// Where the car carries its engine. As with <see cref="StockEngine"/>, only one of this row's own options is
        /// taken: anything else is the list recycling the row's ComboBox for another car.
        /// </summary>
        public EngineLayoutOptionViewModel? EngineLayoutOption
        {
            get => EngineLayoutOptions.FirstOrDefault(o => o.Layout == _engineLayout);
            set
            {
                if (value == null || !EngineLayoutOptions.Contains(value))
                {
                    OnPropertyChanged();
                    return;
                }

                if (value.Layout == _engineLayout) return;

                _engineLayout = value.Layout;
                IsDirty = true;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<EngineOptionViewModel> EngineOptions => _engineOptions.Value;

        /// <summary>Whether the row offers a factory engine; answered without ranking the builds</summary>
        public bool HasEngineOptions => _hasEngineCatalog;

        /// <summary>
        /// The engine build the car leaves the factory with. The player can only ever pick one of this row's own
        /// options: anything else written here comes from the list recycling a row's ComboBox for another car
        /// (the Selector pushes null, or the previous car's pick, back while its items are swapped) and is
        /// ignored, so scrolling never blanks an engine or marks a row changed.
        /// </summary>
        public EngineOptionViewModel? StockEngine
        {
            get
            {
                if (!_stockEngineResolved)
                {
                    _stockEngine = FindEngine(Profile.StockEngineBuildId);
                    _stockEngineResolved = true;
                }

                return _stockEngine;
            }
            set
            {
                if (ReferenceEquals(StockEngine, value)) return;

                if (value == null || !EngineOptions.Contains(value))
                {
                    // Tell the ComboBox again what the row really has; the binding re-reads it (WPF 4+)
                    OnPropertyChanged();
                    return;
                }

                _stockEngine = value;
                IsDirty = true;
                OnPropertyChanged();
            }
        }

        private EngineOptionViewModel? FindEngine(string? buildId) =>
            EngineOptions.FirstOrDefault(o => o.BuildId.Equals(buildId, StringComparison.OrdinalIgnoreCase));

        public string DisplayName => $"{Definition.Brand} {Definition.Name}";
        public string YearDisplay => Definition.Year?.ToString() ?? "Unknown";
        public string BrandDisplay => Definition.Brand;

        public decimal BasePrice
        {
            get => _basePrice;
            set
            {
                if (_basePrice != value)
                {
                    _basePrice = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BasePriceDisplay));
                }
            }
        }

        public string BasePriceDisplay => $"${_basePrice:N0}";

        public float DealerPrecedence
        {
            get => _dealerPrecedence;
            set
            {
                var clamped = Math.Clamp(value, 0.0f, 1.0f);
                if (_dealerPrecedence != clamped)
                {
                    _dealerPrecedence = clamped;
                    IsDirty = true;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DealerPrecedencePercent));
                }
            }
        }

        public int DealerPrecedencePercent
        {
            get => (int)(_dealerPrecedence * 100);
            set
            {
                DealerPrecedence = value / 100f;
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Applies changes to the underlying profile
        /// </summary>
        public void ApplyChanges()
        {
            Profile.BasePrice = _basePrice;
            Profile.DealerPrecedence = _dealerPrecedence;
            Profile.EngineLayout = _engineLayout;

            // Only a row whose engine was looked at can have a new one; the others keep the profile's
            if (_stockEngineResolved && _stockEngine != null && !_stockEngine.BuildId.Equals(Profile.StockEngineBuildId, StringComparison.OrdinalIgnoreCase))
            {
                Profile.StockEngineBuildId = _stockEngine.BuildId;
                Profile.StockEngineIsManual = true;
            }

            Profile.LastUpdatedDate = DateTime.Now;
            Profile.Source = ProfileDataSource.Manual;
            IsDirty = false;
        }

        /// <summary>
        /// Resets to the current profile values
        /// </summary>
        public void ResetToProfile()
        {
            _basePrice = Profile.BasePrice;
            _dealerPrecedence = Profile.DealerPrecedence;
            _engineLayout = Profile.EngineLayout;
            _stockEngineResolved = false;
            IsDirty = false;
            OnPropertyChanged(nameof(StockEngine));
            OnPropertyChanged(nameof(EngineLayoutOption));
            OnPropertyChanged(nameof(BasePrice));
            OnPropertyChanged(nameof(BasePriceDisplay));
            OnPropertyChanged(nameof(DealerPrecedence));
            OnPropertyChanged(nameof(DealerPrecedencePercent));
        }

        /// <summary>
        /// Sets values from a regenerated profile (for reset to defaults)
        /// </summary>
        public void SetFromGeneratedProfile(CarProfile generatedProfile)
        {
            _basePrice = generatedProfile.BasePrice;
            _dealerPrecedence = generatedProfile.DealerPrecedence;

            // The options come best match first: the first one is what a fresh profile would get
            _stockEngine = EngineOptions.FirstOrDefault() ?? StockEngine;
            _stockEngineResolved = true;
            OnPropertyChanged(nameof(StockEngine));

            // Back to the guess from the car's data
            _engineLayout = null;
            OnPropertyChanged(nameof(EngineLayoutOption));
            IsDirty = true;
            OnPropertyChanged(nameof(BasePrice));
            OnPropertyChanged(nameof(BasePriceDisplay));
            OnPropertyChanged(nameof(DealerPrecedence));
            OnPropertyChanged(nameof(DealerPrecedencePercent));
        }
    }

    /// <summary>An engine build on offer as a car's factory engine</summary>
    public class EngineOptionViewModel
    {
        public EngineOptionViewModel(string buildId, string label)
        {
            BuildId = buildId;
            Label = label;
        }

        public string BuildId { get; }

        /// <summary>E.g. "Chevrolet Camaro COPO 427 '69  ·  659 hp, 7.0 l"</summary>
        public string Label { get; }
    }

    /// <summary>A place for a car's engine on offer: a layout set by hand, or none to go by the guess</summary>
    public class EngineLayoutOptionViewModel : ObservableObject
    {
        private string _label;

        private EngineLayoutOptionViewModel(EngineLayout? layout, string label)
        {
            Layout = layout;
            _label = label;
        }

        /// <summary>Null for the guess from the car's data</summary>
        public EngineLayout? Layout { get; }

        /// <summary>E.g. "Guessed: Rear", "Front"; "Guessed: …" while the car's data is being read</summary>
        public string Label
        {
            get => _label;
            private set => SetProperty(ref _label, value);
        }

        /// <summary>The guess being made, for the guess; done at once for a layout set by hand</summary>
        public Task Guessing { get; private set; } = Task.CompletedTask;

        /// <summary>The guess first, its label filled in once <paramref name="guess"/> has read the car's data off the UI thread</summary>
        public static IReadOnlyList<EngineLayoutOptionViewModel> All(Func<EngineLayout> guess)
        {
            var guessed = new EngineLayoutOptionViewModel(null, "Guessed: …");
            guessed.Guessing = guessed.GuessAsync(guess);

            return new[] { guessed }
                .Concat(Enum.GetValues<EngineLayout>().Select(l => new EngineLayoutOptionViewModel(l, l.ToString())))
                .ToList();
        }

        private async Task GuessAsync(Func<EngineLayout> guess)
        {
            EngineLayout layout;
            try
            {
                layout = await Task.Run(guess);
            }
            catch (Exception)
            {
                // Data that can't be read is guessed the way the game guesses it then: in the front
                layout = EngineLayout.Front;
            }

            Label = $"Guessed: {layout}";
        }
    }
}
