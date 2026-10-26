using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts;
using StreetCorsa.ViewModels;
using StreetCorsa.Helpers;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Screens.CarCatalogEditor
{
    /// <summary>
    /// View model for a single car catalog item in the editor
    /// Wraps CarDefinition + CarProfile for display and editing. What is set by hand (how common the car is, the factory
    /// engine, the layout) is saved into the game's own cars.json (<see cref="ToFields"/>); what is not is left to the
    /// game to work out. The price is the game's, always: the car's body and its parts (<see cref="Parts.Cars.CarPricing"/>).
    /// </summary>
    public class CatalogItemViewModel : ObservableObject
    {
        public CarDefinition Definition { get; }
        public CarProfile Profile { get; }

        private float _dealerPrecedence;
        private EngineOptionViewModel? _stockEngine;
        private bool _stockEngineResolved;
        private bool _isDirty;
        private readonly Lazy<IReadOnlyList<EngineOptionViewModel>> _engineOptions;
        private readonly bool _hasEngineCatalog;
        private readonly Lazy<IReadOnlyList<EngineLayoutOptionViewModel>> _layoutOptions;
        private EngineLayout? _engineLayout;
        private bool _commonGiven;
        private bool _engineGiven;
        private bool _layoutGiven;
        private bool _engineReset;

        // The factory rear axle: what is picked by hand (empty: all guessed), what the game's own file gave
        private readonly AxlePicks? _axle;
        // What cars.json gives, kept as it is when there are no pickers for it (no parts catalog)
        private readonly Parts.Cars.RunningGearPicks? _gearGiven;

        /// <param name="engineOptions">Engine builds the car could leave the factory with, best match first; empty without a parts catalog</param>
        public CatalogItemViewModel(CarDefinition definition, CarProfile profile, IReadOnlyList<EngineOptionViewModel>? engineOptions = null)
            : this(definition, profile, engineOptions is { Count: > 0 }, () => engineOptions ?? Array.Empty<EngineOptionViewModel>(), () => EngineLayout.Front, null)
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
        /// <param name="given">
        /// What the game's own cars.json gives the car: those fields are set by hand already. Not the add-ons' (a field
        /// only an add-on gives is the add-on's, and is never copied into the game's file)
        /// </param>
        public CatalogItemViewModel(CarDefinition definition, CarProfile profile, bool hasEngineCatalog, Func<IReadOnlyList<EngineOptionViewModel>> engineOptions,
            Func<EngineLayout> guessLayout, Parts.Cars.CarEntry? given = null, AxlePicks? axle = null)
        {
            _axle = axle;
            _gearGiven = given?.RunningGear;
            if (_axle != null) _axle.Changed += () => IsDirty = true;
            Definition = definition;
            Profile = profile;
            _dealerPrecedence = profile.DealerPrecedence;
            _engineLayout = profile.EngineLayout;
            _commonGiven = given?.Common != null;
            _engineGiven = given?.Engine is { Length: > 0 };
            _layoutGiven = given?.EngineLayout != null;
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
                _layoutGiven = true;
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
                _engineGiven = true;
                IsDirty = true;
                OnPropertyChanged();
            }
        }

        private EngineOptionViewModel? FindEngine(string? buildId) =>
            EngineOptions.FirstOrDefault(o => o.BuildId.Equals(buildId, StringComparison.OrdinalIgnoreCase));

        /// <summary>What keeps the car out of the game played (<see cref="Services.Catalog.InstalledCars.WhyOut"/>); null when it is in</summary>
        public Rules.CarOut? Out { get; init; }

        public bool IsInGame => Out == null;

        public string InGameDisplay => Out == null ? "In this game" : $"Out: {Out}";

        public string DisplayName => $"{Definition.Brand} {Definition.Name}";
        public string YearDisplay => Definition.Year?.ToString() ?? "Unknown";
        public string BrandDisplay => Definition.Brand;

        /// <summary>What the car costs new, as the game worked it out at its start (a new factory engine shows from the next)</summary>
        public decimal BasePrice => Profile.BasePrice;

        public string BasePriceDisplay => Money.Text(Profile.BasePrice);

        /// <summary>What the price is made of: the body, the factory engine's parts, the running gear's</summary>
        public string PriceBreakdown =>
            $"Body {Money.Text(Profile.BodyPrice)}, engine {Money.Text(Profile.EnginePrice)}, running gear {Money.Text(Profile.RunningGearPrice)}";

        public float DealerPrecedence
        {
            get => _dealerPrecedence;
            set
            {
                var clamped = Math.Clamp(value, 0.0f, 1.0f);
                if (_dealerPrecedence != clamped)
                {
                    _dealerPrecedence = clamped;
                    _commonGiven = true;
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
        /// The engine a reset gave the car (the best match), for catalog.db once the car's hand-picked one is out of
        /// cars.json: the one stored there is otherwise kept, as an engine that is still in the catalog. Null when the
        /// engine was not reset; "" for none.
        /// </summary>
        public string? ResetEngine => _engineReset && !_engineGiven ? PendingEngine ?? string.Empty : null;

        /// <summary>The engine shown: the one picked, else the profile's</summary>
        private string? PendingEngine => _stockEngineResolved && _stockEngine != null ? _stockEngine.BuildId : Profile.StockEngineBuildId;

        /// <summary>
        /// Applies changes to the profile shown (not to catalog.db: what is set by hand goes into cars.json). Called
        /// once they are saved: the row is clean after it.
        /// </summary>
        public void ApplyChanges()
        {
            Profile.DealerPrecedence = _dealerPrecedence;
            Profile.EngineLayout = _engineLayout;

            // Only a row whose engine was looked at can have a new one; the others keep the profile's
            if (_stockEngineResolved && _stockEngine != null && !_stockEngine.BuildId.Equals(Profile.StockEngineBuildId, StringComparison.OrdinalIgnoreCase))
            {
                Profile.StockEngineBuildId = _stockEngine.BuildId;
            }

            Profile.StockEngineIsManual = _engineGiven;
            Profile.LastUpdatedDate = DateTime.Now;
            _engineReset = false;
            IsDirty = false;
        }

        /// <summary>
        /// What is set by hand for the car, for cars.json: how common it is, the engine, the layout. Read before
        /// <see cref="ApplyChanges"/>: what is shown, saved or not.
        /// </summary>
        public Services.Catalog.CarFields ToFields() => new(
            _engineGiven ? PendingEngine : null,
            _layoutGiven ? _engineLayout : null,
            _commonGiven ? _dealerPrecedence : null,
            _axle != null ? _axle.Picks : _gearGiven);

        /// <summary>The factory rear axle's pickers; null without a parts catalog</summary>
        public AxlePicks? Axle => _axle;

        public bool HasAxleOptions => _axle != null;

        /// <summary>
        /// Resets to the current profile values
        /// </summary>
        public void ResetToProfile()
        {
            _dealerPrecedence = Profile.DealerPrecedence;
            _engineLayout = Profile.EngineLayout;
            _stockEngineResolved = false;
            _engineReset = false;
            _axle?.Reset();
            IsDirty = false;
            OnPropertyChanged(nameof(StockEngine));
            OnPropertyChanged(nameof(EngineLayoutOption));
            OnPropertyChanged(nameof(DealerPrecedence));
            OnPropertyChanged(nameof(DealerPrecedencePercent));
        }

        /// <summary>
        /// Sets values from a regenerated profile (for reset to defaults)
        /// </summary>
        public void SetFromGeneratedProfile(CarProfile generatedProfile)
        {
            _dealerPrecedence = generatedProfile.DealerPrecedence;
            _commonGiven = false;
            _engineGiven = false;
            _engineReset = true;

            // The options come best match first: the first one is what a fresh profile would get
            _stockEngine = EngineOptions.FirstOrDefault() ?? StockEngine;
            _stockEngineResolved = true;
            OnPropertyChanged(nameof(StockEngine));

            // Back to the guess from the car's data
            _engineLayout = null;
            _layoutGiven = false;
            _axle?.Clear();
            OnPropertyChanged(nameof(EngineLayoutOption));
            IsDirty = true;
            OnPropertyChanged(nameof(DealerPrecedence));
            OnPropertyChanged(nameof(DealerPrecedencePercent));
        }
    }

    /// <summary>A choice in one of the axle's pickers: a part (or a ratio) set by hand, or none to go by the guess</summary>
    public sealed record PickOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// A car's factory running gear in the editor: the rear axle's housing, carrier and ratio, and each axle's brakes and
    /// springs, each guessed unless picked. The carriers and the ratios on offer are those that go in the housing as it
    /// stands (picked or guessed); a pick that no longer goes in is let go. The brakes and springs on offer are those made
    /// for that axle. The guess is worked out again with what is picked (<c>axleFor</c>, <c>gearFor</c>), so "Guessed:"
    /// names what the car would get.
    /// </summary>
    public sealed class AxlePicks : ObservableObject
    {
        private readonly PartsCatalog _catalog;
        private readonly Func<Parts.Cars.RunningGearPicks?, Parts.Cars.RearAxleChoice?> _axleFor;
        private readonly Func<Parts.Cars.RunningGearPicks?, (Parts.Cars.RunningGearFactory.AxleParts Front, Parts.Cars.RunningGearFactory.AxleParts Rear)?> _gearFor;
        private readonly Parts.Cars.RunningGearPicks _given;
        private Parts.Cars.RunningGearPicks _picks;
        private IReadOnlyList<PickOption>? _housings, _carriers, _ratios, _frontBrakes, _rearBrakes, _frontSprings, _rearSprings;

        // The guesses for what is picked now, made once: every option list and the housing read them, and a pick
        // refreshes seven lists at once
        private bool _choiceGuessed, _gearGuessed;
        private Parts.Cars.RearAxleChoice? _choice;
        private (Parts.Cars.RunningGearFactory.AxleParts Front, Parts.Cars.RunningGearFactory.AxleParts Rear)? _gear;

        public AxlePicks(PartsCatalog catalog, Func<Parts.Cars.RunningGearPicks?, Parts.Cars.RearAxleChoice?> axleFor, Parts.Cars.RunningGearPicks? given,
            Func<Parts.Cars.RunningGearPicks?, (Parts.Cars.RunningGearFactory.AxleParts Front, Parts.Cars.RunningGearFactory.AxleParts Rear)?>? gearFor = null)
        {
            _catalog = catalog;
            _axleFor = axleFor;
            _gearFor = gearFor ?? (_ => null);
            _given = given?.Copy() ?? new Parts.Cars.RunningGearPicks();
            _picks = _given.Copy();
        }

        /// <summary>Picked by hand; raised by the pickers, for the row to be saved</summary>
        public event Action? Changed;

        /// <summary>What is picked, for cars.json; null with nothing picked</summary>
        public Parts.Cars.RunningGearPicks? Picks => _picks.IsEmpty ? null : _picks.Copy();

        public IReadOnlyList<PickOption> HousingOptions => _housings ??= Options(Choice()?.Housing,
            _catalog.Parts.Values.Where(p => PartKinds.GroupOf(p) == PartKinds.RearAxles).OrderBy(p => p.DisplayName ?? p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(p => new PickOption(p.Id, p.DisplayName ?? p.Id)));

        public IReadOnlyList<PickOption> CarrierOptions => _carriers ??= Options(Choice()?.Carrier, HousingPart() is { } housing
            ? Mountable(housing, Parts.Cars.RearAxle.CarrierSlot, PartKinds.Differentials).Select(p => new PickOption(p.Id, p.DisplayName ?? p.Id))
            : []);

        public IReadOnlyList<PickOption> RatioOptions => _ratios ??= Options(Choice()?.Gears, Carriers()
            .SelectMany(c => Mountable(c, Parts.Cars.RearAxle.GearsSlot, PartKinds.RingPinions))
            .Select(Parts.Cars.RearAxle.Ratio).Where(r => r > 0).Distinct().Order()
            .Select(r => new PickOption(r.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), r.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))));

        public PickOption? Housing
        {
            get => Selected(HousingOptions, _picks.Axle);
            set => Pick(value, HousingOptions, id => _picks.Axle = id);
        }

        public PickOption? Carrier
        {
            get => Selected(CarrierOptions, _picks.Diff);
            set => Pick(value, CarrierOptions, id => _picks.Diff = id);
        }

        public PickOption? Ratio
        {
            get => Selected(RatioOptions, _picks.Ratio?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            set => Pick(value, RatioOptions, id => _picks.Ratio = id == null ? null : double.Parse(id, System.Globalization.CultureInfo.InvariantCulture));
        }

        public IReadOnlyList<PickOption> FrontBrakeOptions => _frontBrakes ??= Options(Gear()?.Front.Brake, ForAxle(PartKinds.Brakes, true));
        public IReadOnlyList<PickOption> RearBrakeOptions => _rearBrakes ??= Options(Gear()?.Rear.Brake, ForAxle(PartKinds.Brakes, false));
        public IReadOnlyList<PickOption> FrontSpringOptions => _frontSprings ??= Options(Gear()?.Front.Spring, ForAxle(PartKinds.Springs, true));
        public IReadOnlyList<PickOption> RearSpringOptions => _rearSprings ??= Options(Gear()?.Rear.Spring, ForAxle(PartKinds.Springs, false));

        public PickOption? FrontBrake
        {
            get => Selected(FrontBrakeOptions, _picks.BrakesFront);
            set => Pick(value, FrontBrakeOptions, id => _picks.BrakesFront = id);
        }

        public PickOption? RearBrake
        {
            get => Selected(RearBrakeOptions, _picks.BrakesRear);
            set => Pick(value, RearBrakeOptions, id => _picks.BrakesRear = id);
        }

        public PickOption? FrontSpring
        {
            get => Selected(FrontSpringOptions, _picks.SpringsFront);
            set => Pick(value, FrontSpringOptions, id => _picks.SpringsFront = id);
        }

        public PickOption? RearSpring
        {
            get => Selected(RearSpringOptions, _picks.SpringsRear);
            set => Pick(value, RearSpringOptions, id => _picks.SpringsRear = id);
        }

        /// <summary>Back to what the game's own file gives</summary>
        public void Reset()
        {
            _picks = _given.Copy();
            Forget();
            Refresh();
        }

        /// <summary>Everything guessed</summary>
        public void Clear()
        {
            _picks = new Parts.Cars.RunningGearPicks();
            Forget();
            Refresh();
            Changed?.Invoke();
        }

        private void Pick(PickOption? value, IReadOnlyList<PickOption> options, Action<string?> set)
        {
            // A ComboBox recycled for another row pushes what is not this row's: told again what the row has
            if (value == null || !options.Contains(value))
            {
                Refresh();
                return;
            }

            set(value.Id);
            Forget();

            // What no longer goes in with it is let go
            var housing = HousingPart();
            if (_picks.Diff != null && (housing == null || Mountable(housing, Parts.Cars.RearAxle.CarrierSlot, PartKinds.Differentials).All(c => c.Id != _picks.Diff)))
            {
                _picks.Diff = null;
                Forget();
            }

            Refresh();
            Changed?.Invoke();
        }

        /// <summary>The picks changed: the guesses are made again when next asked</summary>
        private void Forget() => _choiceGuessed = _gearGuessed = false;

        private void Refresh()
        {
            _housings = _carriers = _ratios = _frontBrakes = _rearBrakes = _frontSprings = _rearSprings = null;
            foreach (var name in new[] { nameof(HousingOptions), nameof(CarrierOptions), nameof(RatioOptions), nameof(FrontBrakeOptions), nameof(RearBrakeOptions),
                         nameof(FrontSpringOptions), nameof(RearSpringOptions), nameof(Housing), nameof(Carrier), nameof(Ratio), nameof(FrontBrake), nameof(RearBrake),
                         nameof(FrontSpring), nameof(RearSpring) })
                OnPropertyChanged(name);
        }

        private Parts.Cars.RearAxleChoice? Choice()
        {
            if (!_choiceGuessed)
            {
                _choice = _axleFor(_picks.IsEmpty ? null : _picks);
                _choiceGuessed = true;
            }

            return _choice;
        }

        /// <summary>What the car gets on its axles with what is picked: the guess of the brakes and springs not picked</summary>
        private (Parts.Cars.RunningGearFactory.AxleParts Front, Parts.Cars.RunningGearFactory.AxleParts Rear)? Gear()
        {
            if (!_gearGuessed)
            {
                _gear = _gearFor(_picks.IsEmpty ? null : _picks);
                _gearGuessed = true;
            }

            return _gear;
        }

        /// <summary>
        /// The parts of a shelf made for an axle: those whose <c>came_in</c> names this axle or none, and that fit it
        /// (<see cref="Parts.Cars.RunningGear.FitsAxle"/>: a leaf spring the rear, a torsion bar the front)
        /// </summary>
        private IEnumerable<PickOption> ForAxle(string group, bool front) =>
            _catalog.Parts.Values
                .Where(p => p.IsRealPart && PartKinds.GroupOf(p) == group && Parts.Cars.RunningGear.FitsAxle(p, front)
                            && (p.CameIn?.Axle is not { Length: > 0 } axle || axle.Equals(front ? "front" : "rear", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p.DisplayName ?? p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(p => new PickOption(p.Id, p.DisplayName ?? p.Id));

        private PartDefinition? HousingPart() => _picks.Axle is { } id && _catalog.Get(id) is { } picked ? picked : Choice()?.Housing;

        private IEnumerable<PartDefinition> Carriers() => _picks.Diff is { } id && _catalog.Get(id) is { } picked
            ? [picked]
            : HousingPart() is { } housing ? Mountable(housing, Parts.Cars.RearAxle.CarrierSlot, PartKinds.Differentials) : [];

        private IEnumerable<PartDefinition> Mountable(PartDefinition parent, int slotId, string group) =>
            _catalog.MountableOf(parent, slotId, group).OrderBy(p => p.DisplayName ?? p.Id, StringComparer.OrdinalIgnoreCase);

        /// <summary>The guess first ("Guessed: what the car gets"), then every pick</summary>
        private static IReadOnlyList<PickOption> Options(PartDefinition? guessed, IEnumerable<PickOption> picks)
        {
            var guess = guessed == null ? "Guessed" : "Guessed: " + (PartKinds.GroupOf(guessed) == PartKinds.RingPinions
                ? Parts.Cars.RearAxle.Ratio(guessed).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                : guessed.DisplayName ?? guessed.Id);
            return [new PickOption(null, guess), .. picks];
        }

        private static PickOption? Selected(IReadOnlyList<PickOption> options, string? id) =>
            options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
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
            var guessed = new EngineLayoutOptionViewModel(null, GameText.Say("layout.guessing"));
            guessed.Guessing = guessed.GuessAsync(guess);

            return new[] { guessed }
                .Concat(Enum.GetValues<EngineLayout>().Select(l => new EngineLayoutOptionViewModel(l, l.Label())))
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

            Label = GameText.Say("layout.guessed", ("layout", layout.Label()));
        }
    }
}
