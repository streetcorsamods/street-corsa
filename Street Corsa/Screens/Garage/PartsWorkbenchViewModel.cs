using StreetCorsa.Looks;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;
using StreetCorsa.Helpers;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>
    /// Working on the selected car in the garage's parts view, or on the engine on the stand: what the engine comes
    /// to, the part that is clicked on and taking it off, the shelf of loose parts and putting one of them on.
    /// Parts are picked in the 3D view; this is everything around it. What may come off an engine, and go on, and
    /// in what order, is the catalog's work order (<see cref="EngineWork"/>). The lift, the rebuild offers, the show of
    /// a job being done and where a shelf part can go are the other parts of this class, a file each.
    /// </summary>
    public partial class PartsWorkbenchViewModel : ObservableObject
    {
        // The rules' time for each part, and for an engine in or out of a car (a big job) or onto the stand or off it
        private static int MinutesPerPart => Rules.RuleBook.Current.Time.PerPart;
        private static int EngineInOutMinutes => Rules.RuleBook.Current.Time.EngineInOut;
        private static int StandMinutes => Rules.RuleBook.Current.Time.Stand;
        private static int LiftMinutes => Rules.RuleBook.Current.Time.Lift;

        private readonly ICarPartsService _parts;
        private readonly Models.GameState.GameState _gameState;
        private readonly Action _save;
        private readonly Func<int, Task> _spendMinutes;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Parts);

        private Car? _car;
        private LiveTree? _live;

        // The stand's engine as last built, and what from: built again only when the stand has another engine, or
        // work was done on it, so that work on the car does not rebuild the stand in the 3D view
        private LiveTree? _standLive;
        private PartInstance? _standSource;
        private PartsCatalog? _standCatalog;
        private bool _standStale;

        // The engine on the stand when the view last looked, turned over or not: another one there comes upright
        private Guid? _lastStandEngine;
        private readonly List<(int CarSlot, LiveTree Tree)> _gearTrees = new();
        private int _evaluation;

        public PartsWorkbenchViewModel(ICarPartsService parts, Models.GameState.GameState gameState, Action save, Func<int, Task> spendMinutes)
        {
            _parts = parts;
            _gameState = gameState;
            _save = save;
            _spendMinutes = spendMinutes;

            RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedPart != null && string.IsNullOrEmpty(SelectedBlockedDisplay));
            CancelMountCommand = new RelayCommand(() => SelectedShelfItem = null, () => SelectedShelfItem != null);
            TakeApartCommand = new RelayCommand(TakeApart, () => CanTakeApart);
            PutOnStandCommand = new RelayCommand(PutOnStand, () => CanPutOnStand);
            InstallFromStandCommand = new RelayCommand(InstallFromStand, () => CanInstallFromStand);
            RebuildCommand = new RelayCommand<RebuildOfferViewModel>(Rebuild, offer => offer?.CanDo == true && !_isWorking);
            LowerCommand = new RelayCommand(() => MoveLift(LiftLayout.Below(LiftStop)), () => IsLifted && !_isWorking);
        }

        /// <summary>Raised when money or time moved, for the screen around the workbench</summary>
        public event Action? StateChanged;

        /// <summary>
        /// Parts went on or came off the car (not the engine on the stand): its engine is another one to start.
        /// Not raised for a new car: whoever set the car knows.
        /// </summary>
        public event Action? CarEngineChanged;

        /// <summary>The work is done but the game could not be saved; the screen around the workbench says so</summary>
        public event Action<Exception>? SaveFailed;

        public RelayCommand RemoveSelectedCommand { get; }
        public RelayCommand CancelMountCommand { get; }
        public RelayCommand TakeApartCommand { get; }
        public RelayCommand PutOnStandCommand { get; }
        public RelayCommand InstallFromStandCommand { get; }
        public RelayCommand<RebuildOfferViewModel> RebuildCommand { get; }

        /// <summary>The lift's Lower button: the car a stop down, from either stop (<see cref="LiftLayout.Below"/>)</summary>
        public RelayCommand LowerCommand { get; }

        #region What the 3D view shows

        private bool _isOpen;

        /// <summary>The parts view is on: the car is a shell and its parts can be worked on</summary>
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (!SetProperty(ref _isOpen, value)) return;

                if (!value)
                {
                    EndWork();
                    SelectedPart = null;
                    SelectedShelfItem = null;
                    IsStand = false;
                }

                OnPropertyChanged(nameof(IsCarView));
                OnPropertyChanged(nameof(IsStandView));
                OnPropertyChanged(nameof(CanTurnEngineOver));
            }
        }

        private bool _isStand;

        /// <summary>The engine stand is on show instead of the car: the work is on the engine bolted to it</summary>
        public bool IsStand
        {
            get => _isStand;
            private set
            {
                if (!SetProperty(ref _isStand, value)) return;

                EndWork();
                SelectedPart = null;
                SelectedShelfItem = null;
                OnPropertyChanged(nameof(IsCarView));
                OnPropertyChanged(nameof(IsStandView));
                OnPropertyChanged(nameof(CanTurnEngineOver));
                OnPropertyChanged(nameof(CanUseLift));
                Refresh();
            }
        }

        /// <summary>
        /// The buttons that put kinds of part out of sight while the car's parts are on show (the engine, the gearbox,
        /// the brakes, the springs and shocks), as the mods' part_groups.json has them; every kind in sight at first
        /// </summary>
        public IReadOnlyList<Controls.PartShowToggle> PartShows { get; } = Controls.PartShowToggle.ForInstalled();

        /// <summary>The "Parts" toggle: the car's parts are on show</summary>
        public bool IsCarView
        {
            get => _isOpen && !_isStand;
            set
            {
                if (value)
                {
                    IsStand = false;
                    IsOpen = true;
                }
                else if (!_isStand) IsOpen = false;
            }
        }

        /// <summary>The "Stand" toggle: the engine stand is on show</summary>
        public bool IsStandView
        {
            get => _isOpen && _isStand;
            set
            {
                if (value && !HasStand) return;
                if (value)
                {
                    IsStand = true;
                    IsOpen = true;
                }
                else if (_isStand) IsOpen = false;
            }
        }

        private bool _isEngineTurnedOver;

        /// <summary>
        /// The "Turn over" toggle: the engine on the stand is upside down, its crankshaft and oil pan up. Only looks,
        /// nothing is refused either way. Picking a part worked on from below turns it over by itself, one worked on
        /// from above back (<see cref="WorkOrder.SideOf"/>).
        /// </summary>
        public bool IsEngineTurnedOver
        {
            get => _isEngineTurnedOver;
            set => SetProperty(ref _isEngineTurnedOver, value && CanTurnEngineOver);
        }

        /// <summary>The stand is on show with an engine on it: there is something to turn over</summary>
        public bool CanTurnEngineOver => _isOpen && _isStand && _gameState.Player.EngineStand != null;

        /// <summary>The engine on the stand turned the side up a part is worked on from; one that does not say leaves it</summary>
        private void TurnFor(PartDefinition? part)
        {
            if (part == null || !CanTurnEngineOver || Catalog?.WorkOrder.SideOf(part) is not { } side) return;

            IsEngineTurnedOver = side == EngineSide.Below;
        }

        private PartsCatalog? _catalog;

        /// <summary>Null until the parts are loaded, and for good when there are none</summary>
        public PartsCatalog? Catalog
        {
            get => _catalog;
            private set
            {
                if (SetProperty(ref _catalog, value)) OnPropertyChanged(nameof(IsAvailable));
            }
        }

        /// <summary>There are parts to work with</summary>
        public bool IsAvailable => _catalog != null;

        /// <summary>The game has an engine stand (<see cref="Rules.SystemRules.EngineStand"/>); without, an engine pulled goes on the shelf</summary>
        public bool HasStand => Rules.RuleBook.Current.Systems.EngineStand;

        /// <summary>The Stand toggle is shown: the parts can be worked on and there is a stand</summary>
        public bool CanShowStand => IsAvailable && HasStand;

        private InstalledPart? _engine;

        public InstalledPart? Engine
        {
            get => _engine;
            private set => SetProperty(ref _engine, value);
        }

        private InstalledPart? _standEngine;

        /// <summary>
        /// The engine on the stand beside the car, shown whatever view is on; while the stand is worked on, the very
        /// tree of <see cref="Engine"/>
        /// </summary>
        public InstalledPart? StandEngine
        {
            get => _standEngine;
            private set => SetProperty(ref _standEngine, value);
        }

        private IReadOnlyList<CarPart>? _gear;

        /// <summary>What sits on the car's wheel slots, each with what is on it</summary>
        public IReadOnlyList<CarPart>? Gear
        {
            get => _gear;
            private set => SetProperty(ref _gear, value);
        }

        private System.Numerics.Vector3 _engineOffset;

        /// <summary>Where the car's engine is moved from its place by hand (cars.json)</summary>
        public System.Numerics.Vector3 EngineOffset
        {
            get => _engineOffset;
            private set => SetProperty(ref _engineOffset, value);
        }

        /// <summary>
        /// The garage's placement mode moved the engine block: the car keeps the new place (cars.json), and what it shows
        /// follows. The trees are left as they are: placement holds on to the block it moves, and a tree made anew would
        /// end it after the first step
        /// </summary>
        public void MoveEngine(System.Numerics.Vector3 offset)
        {
            if (_car == null) return;
            _parts.SetEngineOffset(_car.DefinitionId, offset);
            EngineOffset = _parts.EngineOffsetOf(_car.DefinitionId);
            if (Catalog != null) Shown = _parts.Shown(_car);
        }

        private ShownSet? _shown;

        /// <summary>What the car shows that its own model does not have: its wheels, the engine parts that stand out</summary>
        public ShownSet? Shown
        {
            get => _shown;
            private set => SetProperty(ref _shown, value);
        }

        private EngineLayout _engineLayout;

        /// <summary>Where the car carries its engine, which is where the 3D view draws it</summary>
        public EngineLayout EngineLayout
        {
            get => _engineLayout;
            private set => SetProperty(ref _engineLayout, value);
        }

        private IReadOnlyList<MountCandidate>? _candidates;

        public IReadOnlyList<MountCandidate>? Candidates
        {
            get => _candidates;
            private set => SetProperty(ref _candidates, value);
        }

        private string _placementDisplay = string.Empty;

        /// <summary>What the viewport's placement mode is doing, shown over the part card; empty when it is off</summary>
        public string PlacementDisplay
        {
            get => _placementDisplay;
            set => SetProperty(ref _placementDisplay, value);
        }

        private InstalledPart? _selectedPart;

        public InstalledPart? SelectedPart
        {
            get => _selectedPart;
            set
            {
                if (!SetProperty(ref _selectedPart, value)) return;

                TurnFor(value?.Definition);
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedName));
                OnPropertyChanged(nameof(SelectedKind));
                OnPropertyChanged(nameof(SelectedCondition));
                OnPropertyChanged(nameof(SelectedConditionDisplay));
                OnPropertyChanged(nameof(SelectedWorthDisplay));
                OnPropertyChanged(nameof(SelectedAttachedDisplay));
                OnPropertyChanged(nameof(SelectedCutsDisplay));
                OnPropertyChanged(nameof(SelectedBlockedDisplay));
                OnPropertyChanged(nameof(RemoveButtonText));
                OnPropertyChanged(nameof(CanInstallFromStand));
                UpdateRebuilds();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        #endregion

        #region Engine sheet

        public string EngineTitle => _isStand ? GameText.Say("workbench.engine_stand") : GameText.Say("workbench.engine");

        private EngineReport? _report;

        public bool HasEngine => _engine != null;

        public string EngineName =>
            _engine == null ? GameText.Say("workbench.no_engine") : (_engine.Definition.DisplayName ?? _engine.Definition.Name);

        public string PowerDisplay => _report?.Dyno is { MaxPowerHp: > 0 } dyno ? $"{Measure.Power(dyno.MaxPowerHp)} @ {dyno.MaxPowerRpm:0}" : "-";
        public string TorqueDisplay => _report?.Dyno is { MaxTorque: > 0 } dyno ? $"{Measure.Torque(dyno.MaxTorque)} @ {dyno.MaxTorqueRpm:0}" : "-";

        /// <summary>The engine's swept volume in the player's units (litres, or cubic inches) and its compression ratio</summary>
        public string DisplacementDisplay => _report?.Dyno is { Displacement: > 0 } dyno
            ? GameText.Say("workbench.displacement", ("displacement", Measure.Displacement(dyno.Displacement * 1000)), ("compression", dyno.Compression))
            : "-";

        public string GearsDisplay => _report is { GearRatios.Count: > 0 } report
            ? GameText.Say("workbench.gears", ("gears", report.GearRatios.Count), ("final", $"{_axleFinal ?? report.FinalRatio:0.00}"))
            : GameText.Say("workbench.no_transmission");

        /// <summary>The final drive of the car's rear axle (its own figure on the factory gears), null with no gears on an axle: the gearbox's</summary>
        private double? _axleFinal;

        private double? AxleFinal(PartsCatalog catalog, Car car)
        {
            if (RearAxle.Mounted(car.Parts).Gears is not { } gears || catalog.Get(gears.DefinitionId) is not { } definition) return null;
            if (_parts.FactoryAxle(car.DefinitionId)?.Gears.Id == definition.Id && _parts.Specs(car.DefinitionId) is { FinalRatio: > 0 } specs) return specs.FinalRatio;
            return RearAxle.Ratio(definition) is var ratio and > 0 ? ratio : null;
        }

        public string MassDisplay => _report == null ? "-" : Measure.Mass(_report.Mass).ToString();

        /// <summary>Why the engine does not run, in the parts' own words; empty when it does</summary>
        public string StatusDisplay => _engine == null
            ? _isStand ? GameText.Say("workbench.stand_empty") : GameText.Say("workbench.bay_empty")
            : _report == null
                ? GameText.Say("workbench.checking")
                : _report.Runs ? string.Empty : Capitalize(_report.Problem ?? GameText.Say("workbench.does_not_run"));

        #endregion

        #region Selected part

        public bool HasSelection => _selectedPart != null;
        public string SelectedName => _selectedPart?.Definition.DisplayName ?? _selectedPart?.Definition.Name ?? string.Empty;
        public string SelectedKind => _selectedPart == null ? string.Empty : PartKinds.GroupName(PartKinds.GroupOf(_selectedPart.Definition));
        public double SelectedCondition => _selectedPart?.Wear ?? 0;
        public string SelectedConditionDisplay => _selectedPart == null
            ? string.Empty
            : $"{_selectedPart.Wear * 100:0}%" + (_selectedPart.Tear < PartInstance.Undamaged ? GameText.Say("garage.part.damaged", ("left", $"{_selectedPart.Tear * 100:0}")) : string.Empty);

        public string SelectedWorthDisplay => _selectedPart != null && SavedOf(_selectedPart) is { } saved
            ? Money.Text(PartPricing.Round(PartPricing.Worth(_selectedPart.Definition, saved)))
            : string.Empty;

        /// <summary>What a machine shop has done to the picked part ("bored +.030 over · ported and polished"); empty for none</summary>
        public string SelectedCutsDisplay => _selectedPart != null && Catalog is { } catalog && SavedOf(_selectedPart) is { } saved
            ? string.Join("  ·  ", Machining.Describe(catalog, saved))
            : string.Empty;

        /// <summary>
        /// Why the picked part cannot come off now, in the work order's words: an engine's part (<see cref="EngineWork"/>) or
        /// the car's running gear (<see cref="CarWork"/>); empty when it can
        /// </summary>
        public string SelectedBlockedDisplay
        {
            get
            {
                if (_selectedPart == null || Catalog is not { } catalog || SavedOf(_selectedPart) is not { } saved) return string.Empty;

                // The engine hoist swings the engine out over the floor: not from under a car up on the lift
                if (SelectedIsEngine && !_isStand && _car?.IsLifted == true) return PartWords.Say("work.lift_hoist");
                if (CurrentEngine is { } engine && engine.SelfAndDescendants().Contains(saved)) return EngineWork.WhyNotOff(catalog, engine, saved, _isStand) ?? string.Empty;
                return !_isStand && _car != null ? CarWork.WhyNotOff(catalog, _car.Parts, saved, _car.IsLifted) ?? string.Empty : string.Empty;
            }
        }

        private bool SelectedIsEngine => _selectedPart != null && ReferenceEquals(_selectedPart, _engine);

        public string RemoveButtonText => !SelectedIsEngine
            ? GameText.Say("workbench.take_off")
            : _isStand ? GameText.Say("workbench.off_stand") : GameText.Say("workbench.pull_engine");

        public string SelectedAttachedDisplay
        {
            get
            {
                if (SelectedIsEngine)
                {
                    return _isStand || !HasStand
                        ? GameText.Say("workbench.off.to_shelf")
                        : _gameState.Player.EngineStand == null
                            ? Catalog is { } catalog && EngineWork.StandStripsDisplay(catalog) is { Length: > 0 } strips
                                ? GameText.Say("workbench.off.to_stand.strips", ("strips", strips))
                                : GameText.Say("workbench.off.to_stand")
                            : GameText.Say("workbench.off.stand_taken");
                }

                var attached = _selectedPart == null ? 0 : _selectedPart.SelfAndDescendants().Count() - 1;
                return attached switch
                {
                    0 => string.Empty,
                    1 => GameText.Say("workbench.attached.one"),
                    _ => GameText.Say("workbench.attached.other", ("count", attached))
                };
            }
        }

        #endregion

        #region Shelf

        public BulkObservableCollection<ShelfItemViewModel> Shelf { get; } = [];

        public bool HasShelfItems => Shelf.Count > 0;

        private ShelfItemViewModel? _selectedShelfItem;

        /// <summary>The loose part picked to go on the car; the 3D view shows where it fits</summary>
        public ShelfItemViewModel? SelectedShelfItem
        {
            get => _selectedShelfItem;
            set
            {
                if (!SetProperty(ref _selectedShelfItem, value)) return;

                if (value != null) SelectedPart = null;
                TurnFor(value == null ? null : Catalog?.Get(value.Part.DefinitionId));
                OnPropertyChanged(nameof(CanTakeApart));
                OnPropertyChanged(nameof(CanPutOnStand));
                OnPropertyChanged(nameof(IsShelfEngine));
                UpdateCandidates();
                UpdateRebuilds();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The picked shelf part has parts on it that can come off right there on the shelf; an engine comes apart on the stand</summary>
        public bool CanTakeApart => _selectedShelfItem is { Part.Children.Count: > 0, IsEngine: false };

        /// <summary>The picked shelf part is an engine, and the stand is free for it</summary>
        public bool CanPutOnStand => HasStand && _selectedShelfItem is { IsEngine: true } && _gameState.Player.EngineStand == null;

        public bool IsShelfEngine => HasStand && _selectedShelfItem is { IsEngine: true };

        /// <summary>The engine on the stand, picked, can go in the car: there is one, with its engine bay empty</summary>
        public bool CanInstallFromStand => _isStand && SelectedIsEngine && _car != null && _car.Engine == null && !_car.IsLifted;

        private string _shelfHint = string.Empty;

        public string ShelfHint
        {
            get => _shelfHint;
            private set => SetProperty(ref _shelfHint, value);
        }

        #endregion

        /// <summary>The car being worked on; null for an empty garage</summary>
        public async void SetCar(Car? car)
        {
            EndWork();
            LowerLeaving(_car, car);
            _car = car;
            // The last car's picks go before the lift, whose places and rules are read with a pick in hand
            SelectedPart = null;
            SelectedShelfItem = null;
            NotifyLift();

            // Where the car carries its engine, looked up alongside the parts rather than after them: guessing it may
            // read the car's data off the disk, and the car's parts should not wait for that
            var layoutLookup = car == null ? Task.FromResult(EngineLayout.Front) : Task.Run(() => LayoutOf(car));

            try
            {
                // The first call loads the catalog: keep that off the UI thread. The car and the save file
                // belong to the UI thread, so a car without parts gets them, and is saved, back on it.
                var available = await Task.Run(() => _parts.IsAvailable);
                if (available && car != null && await _parts.EnsurePartsAsync(car)) Save();

                var layout = await layoutLookup;

                if (!ReferenceEquals(_car, car)) return;

                EngineLayout = layout;
                Catalog = available ? _parts.Catalog : null;
                if (!available) IsOpen = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not get the parts of {Car}", car?.DefinitionId ?? "(none)");
            }

            Refresh();
        }

        /// <summary>The car's engine layout; in the front when even the guess fails, never the last car's</summary>
        private EngineLayout LayoutOf(Car car)
        {
            try
            {
                return _parts.EngineLayoutOf(car.DefinitionId);
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not tell where {Car} carries its engine: {Error}", car.DefinitionId, ex.Message);
                return EngineLayout.Front;
            }
        }

        public void OnPartClicked(InstalledPart? part)
        {
            if (_isWorking) return;

            if (SelectedShelfItem != null && part == null) return;

            SelectedShelfItem = null;
            SelectedPart = InCurrentTree(part);
        }

        /// <summary>
        /// The tree is made anew after every change, and the 3D view goes on showing the one before until the
        /// new model is built: a click in between brings a part of the old tree. The same part of the tree
        /// that counts now is known by its id; a part that has come off in the meantime is nothing to select.
        /// </summary>
        private InstalledPart? InCurrentTree(InstalledPart? part)
        {
            if (part == null || SavedOf(part) != null) return part;

            return part.InstanceId == Guid.Empty
                ? part
                : Trees.SelectMany(t => t.Root.SelfAndDescendants()).FirstOrDefault(p => p.InstanceId == part.InstanceId);
        }

        /// <summary>The engine being worked on: the one on the stand, or the car's</summary>
        private PartInstance? CurrentEngine => _isStand ? _gameState.Player.EngineStand : _car?.Engine;

        /// <summary>What <see cref="Workbench"/> mounts on and takes off: the car's own parts, or the stand's engine alone</summary>
        private List<PartInstance>? CurrentRoots => _isStand
            ? _gameState.Player.EngineStand is { } stand ? new List<PartInstance> { stand } : null
            : _car?.Parts;

        /// <summary>Every tree on the car: the engine and the running gear</summary>
        private IEnumerable<LiveTree> Trees => (_live == null ? Enumerable.Empty<LiveTree>() : new[] { _live }).Concat(_gearTrees.Select(g => g.Tree));

        /// <summary>The saved part behind a part on screen, whichever tree of the car it is in</summary>
        private PartInstance? SavedOf(InstalledPart part) => Trees.Select(t => t.Saved.GetValueOrDefault(part)).FirstOrDefault(p => p != null);

        public void OnCandidateClicked(MountCandidate candidate)
        {
            if (_isWorking || CurrentRoots is not { } roots || candidate.Tag is not MountPlace place || SelectedShelfItem is not { } item) return;

            // The 3D view shows the places of the part picked before until those of this one are built:
            // a place is only good for the part it was found for
            if (_candidates?.Any(c => ReferenceEquals(c, candidate)) != true) return;

            Workbench.Mount(roots, place, item.Part);
            _gameState.Player.Parts.Remove(item.Part);
            _logger.Information("Mounted {Part} on {Where}", item.Part.DefinitionId, _isStand ? "the engine on the stand" : _car?.DefinitionId);

            // A rear axle back under the car takes on what it carries that went on the car's own slots while it was off
            if (!_isStand && _car != null && place.Parent == null && place.ParentSlot == RearAxle.CarSlot && Catalog is { } catalog
                && RunningGearFactory.HangOnAxle(catalog, _car.Parts, _parts.Specs(_car.DefinitionId)?.HasLiveAxle ?? true) is var moved and > 0)
                _logger.Information("{Count} part(s) on {Car}'s own corner slots went onto its rear axle", moved, _car.DefinitionId);

            SelectedShelfItem = null;
            var engineIn = place.Parent == null && place.ParentSlot == PartInstance.CarEngineSlot;
            Finish(item.Part.SelfAndDescendants().Count(), engineIn ? EngineInOutMinutes : null);
        }

        private void RemoveSelected()
        {
            if (CurrentRoots is not { } roots || _selectedPart == null || SavedOf(_selectedPart) is not { } saved) return;
            if (!string.IsNullOrEmpty(SelectedBlockedDisplay)) return;

            if (SelectedIsEngine)
            {
                RemoveEngine(roots, saved);
                return;
            }

            if (!Workbench.Remove(roots, saved)) return;

            _gameState.Player.Parts.Add(saved);
            _logger.Information("Took {Part} off {Where}", saved.DefinitionId, _isStand ? "the engine on the stand" : _car?.DefinitionId);

            SelectedPart = null;
            Finish(saved.SelfAndDescendants().Count());
        }

        /// <summary>The engine itself: off the stand to the shelf, or out of the car, onto the stand when it is free</summary>
        private void RemoveEngine(List<PartInstance> roots, PartInstance engine)
        {
            if (_isStand)
            {
                _gameState.Player.EngineStand = null;
                _gameState.Player.Parts.Add(engine);
                _logger.Information("Took {Block} off the engine stand", engine.DefinitionId);
                SelectedPart = null;
                Finish(1, StandMinutes);
                return;
            }

            if (!Workbench.Remove(roots, engine)) return;

            var stripped = 0;
            if (HasStand && _gameState.Player.EngineStand == null && Catalog is { } catalog)
            {
                var off = EngineWork.StripForStand(catalog, engine);
                _gameState.Player.Parts.AddRange(off);
                _gameState.Player.EngineStand = engine;
                stripped = off.Count;
                _logger.Information("Pulled {Block} out of {Car} onto the engine stand ({Count} part(s) off to bolt it on)",
                    engine.DefinitionId, _car?.DefinitionId, stripped);
            }
            else
            {
                _gameState.Player.Parts.Add(engine);
                _logger.Information("Pulled {Block} out of {Car} onto the shelf", engine.DefinitionId, _car?.DefinitionId);
            }

            SelectedPart = null;
            Finish(1, EngineInOutMinutes + stripped * MinutesPerPart);
        }

        /// <summary>The engine picked on the shelf goes on the stand, what is in the way of its bellhousing face off onto the shelf</summary>
        private void PutOnStand()
        {
            if (!CanPutOnStand || _selectedShelfItem is not { } item || Catalog is not { } catalog) return;

            var shelf = _gameState.Player.Parts;
            var index = shelf.IndexOf(item.Part);
            shelf.Remove(item.Part);

            var off = EngineWork.StripForStand(catalog, item.Part);
            shelf.InsertRange(Math.Clamp(index, 0, shelf.Count), off);
            _gameState.Player.EngineStand = item.Part;
            _logger.Information("Put {Block} on the engine stand ({Count} part(s) off to bolt it on)", item.Part.DefinitionId, off.Count);

            SelectedShelfItem = null;
            IsStandView = true;
            Finish(1, StandMinutes + off.Count * MinutesPerPart);
        }

        /// <summary>The engine on the stand goes in the selected car's empty engine bay</summary>
        private void InstallFromStand()
        {
            if (!CanInstallFromStand || _car == null || _gameState.Player.EngineStand is not { } engine) return;

            _gameState.Player.EngineStand = null;
            Workbench.Mount(_car.Parts, new MountPlace(null, PartInstance.CarEngineSlot, 0), engine);
            _logger.Information("Put {Block} from the engine stand in {Car}", engine.DefinitionId, _car.DefinitionId);

            SelectedPart = null;
            IsCarView = true;
            Finish(1, EngineInOutMinutes);
        }

        /// <summary>What is mounted on the picked shelf part comes off and lies next to it, each piece with what is on it in turn</summary>
        private void TakeApart()
        {
            if (_selectedShelfItem is not { } item || item.Part.Children.Count == 0) return;

            var pieces = item.Part.Children.ToList();
            item.Part.Children.Clear();

            var shelf = _gameState.Player.Parts;
            var index = shelf.IndexOf(item.Part);
            foreach (var piece in pieces)
            {
                piece.ParentSlot = 0;
                piece.OwnSlot = 0;
                shelf.Insert(++index, piece);
            }

            _logger.Information("Took {Count} part(s) off {Part} on the shelf", pieces.Count, item.Part.DefinitionId);
            SelectedShelfItem = null;
            Finish(pieces.Count, carChanged: false);
        }

        /// <summary>After the car changed: show it, let the clock run for the work, and save it all</summary>
        /// <param name="minutes">What the job took, when it is more than a few parts</param>
        /// <param name="carChanged">The car itself was worked on, not only the stand or the shelf</param>
        /// <param name="refresh">The car's and the stand's trees are made anew; not for work on the shelf alone</param>
        private async void Finish(int partsHandled, int? minutes = null, bool? carChanged = null, bool refresh = true)
        {
            if (refresh)
            {
                // The stand's engine changed when the work was on it; one put on or taken off is another engine anyway
                if (_isStand) _standStale = true;
                Refresh();
            }

            if (carChanged ?? !_isStand)
            {
                try
                {
                    CarEngineChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "The garage could not catch up with the car's new engine");
                }
            }

            try
            {
                // Before saving: the time the work took belongs in the save, and so does the new day it may end in
                await _spendMinutes(minutes ?? MinutesPerPart * Math.Min(partsHandled, 6));
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not spend the time for the work: {Error}", ex.Message);
            }

            Save();

            // The money moved, and the day may have: what the picked part's jobs say about paying for them
            UpdateRebuilds();

            try
            {
                StateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The garage could not catch up with the work on the car");
            }
        }

        /// <summary>Nothing above this catches: a save that fails must not take the game down with it</summary>
        private void Save()
        {
            try
            {
                _save();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the game after working on the car");
                SaveFailed?.Invoke(ex);
            }
        }

        /// <summary>Brings the saved tree to life again, fills the shelf, and puts the engine on the dyno</summary>
        private async void Refresh()
        {
            // Everything in here runs on a saved tree the catalog may no longer be able to rebuild (a pack that
            // changed since the save): whatever throws is logged, and the garage stays up without the sheet
            try
            {
                var catalog = Catalog;
                _live = _isStand ? StandLive(catalog) : catalog != null && _car?.Engine is { } engine ? PartTrees.ToInstalled(catalog, engine) : null;
                Engine = _live?.Root;
                StandEngine = _isStand ? _live?.Root : StandBesideTheCar(catalog);

                // The one place the engine comes upright: the work has left the stand, which it has as soon as the stand is
                // not on show (Refresh follows every change to that), or another engine is on it, or none
                var standId = _gameState.Player.EngineStand?.InstanceId;
                if (!CanTurnEngineOver || standId != _lastStandEngine) IsEngineTurnedOver = false;
                _lastStandEngine = standId;
                OnPropertyChanged(nameof(CanTurnEngineOver));
                _report = null;

                _gearTrees.Clear();
                if (catalog != null && _car != null && !_isStand)
                {
                    foreach (var part in _car.Parts.Where(p => RunningGear.CornerOf(p.ParentSlot) >= 0 || p.ParentSlot == RearAxle.CarSlot || RunningGear.IsSwayBarSlot(p.ParentSlot)))
                    {
                        if (PartTrees.ToInstalled(catalog, part) is { } tree) _gearTrees.Add((part.ParentSlot, tree));
                    }
                }

                Gear = _gearTrees.Select(g => new CarPart(g.CarSlot, g.Tree.Root)).ToList();
                _axleFinal = catalog != null && _car != null && !_isStand ? AxleFinal(catalog, _car) : null;
                EngineOffset = _car != null ? _parts.EngineOffsetOf(_car.DefinitionId) : System.Numerics.Vector3.Zero;
                Shown = _car != null ? _parts.Shown(_car) : null;

                FillShelf();
                OnPropertyChanged(nameof(CanPutOnStand));
                OnPropertyChanged(nameof(CanInstallFromStand));
                OnPropertyChanged(nameof(SelectedBlockedDisplay));
                OnPropertyChanged(nameof(SelectedAttachedDisplay));
                RelayCommand.RaiseCanExecuteChanged();
                NotifySheet();

                var live = _live;
                if (catalog == null || live == null) return;

                var evaluation = ++_evaluation;

                // The engine on the stand is no car's: its dyno figure is nobody's
                var car = _isStand ? null : _car;

                // On a tree of its own, copied here: the one on screen, and the saved tree behind it, belong to the UI
                // thread, which may change them while the worker evaluates
                var saved = PartTrees.Clone(live.Saved[live.Root], keepIds: true);
                var report = await Task.Run(() => EngineFactory.Evaluate(catalog, saved));
                if (evaluation != _evaluation) return;

                _report = report;

                // The car's dyno figure, as the rivals' is kept: the diner's matchup reads it, and the next save keeps it
                if (car != null) car.PowerHp = Services.Market.UsedCarMarketService.PowerOf(report);
                NotifySheet();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not show the parts of {Car}", _car?.DefinitionId ?? "(none)");
            }
        }

        /// <summary>The shelf as the player's loose parts are now</summary>
        private void FillShelf()
        {
            // The list told once, not once a part: a shelf of many parts is drawn again after every job
            Shelf.ReplaceAll(Catalog is { } catalog
                ? _gameState.Player.Parts.Select(part => (Part: part, Definition: catalog.Get(part.DefinitionId)))
                    .Where(p => p.Definition != null)
                    .Select(p => new ShelfItemViewModel(p.Part, p.Definition!, catalog))
                : []);

            OnPropertyChanged(nameof(HasShelfItems));
            OnPropertyChanged(nameof(AwayDisplay));
        }

        /// <summary>The player's parts at a machine shop, a line each: what, where, back when; empty for none</summary>
        public string AwayDisplay => string.Join(Environment.NewLine, (_gameState.Player.MachineWork ?? []).Select(order =>
            GameText.Say("workbench.away", ("part", Catalog?.Get(order.Part.DefinitionId) is { } d ? d.DisplayName ?? d.Name : order.Part.DefinitionId),
                ("shop", order.ShopName), ("day", order.ReadyOn))));

        /// <summary>The stand's engine brought to life, the one built before while nothing changed on it</summary>
        private LiveTree? StandLive(PartsCatalog? catalog)
        {
            var stand = _gameState.Player.EngineStand;
            if (_standStale || !ReferenceEquals(stand, _standSource) || !ReferenceEquals(catalog, _standCatalog))
            {
                // Forgotten first: a stand that fails to build is built again next time, not taken for the one before
                _standLive = null;
                _standSource = null;
                _standLive = catalog != null && stand != null ? PartTrees.ToInstalled(catalog, stand) : null;
                _standSource = stand;
                _standCatalog = catalog;
                _standStale = false;
            }

            return _standLive;
        }

        /// <summary>The stand's engine, for the car's view: whatever goes wrong with it is the stand's, not the car's</summary>
        private InstalledPart? StandBesideTheCar(PartsCatalog? catalog)
        {
            try
            {
                return StandLive(catalog)?.Root;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not show the engine on the stand");
                return null;
            }
        }

        private void NotifySheet()
        {
            OnPropertyChanged(nameof(HasEngine));
            OnPropertyChanged(nameof(EngineTitle));
            OnPropertyChanged(nameof(EngineName));
            OnPropertyChanged(nameof(PowerDisplay));
            OnPropertyChanged(nameof(TorqueDisplay));
            OnPropertyChanged(nameof(DisplacementDisplay));
            OnPropertyChanged(nameof(GearsDisplay));
            OnPropertyChanged(nameof(MassDisplay));
            OnPropertyChanged(nameof(StatusDisplay));
        }

        private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>A loose part on the shelf, with whatever is still mounted on it</summary>
    public class ShelfItemViewModel
    {
        public ShelfItemViewModel(PartInstance part, PartDefinition definition, PartsCatalog catalog)
        {
            Part = part;
            Name = definition.DisplayName ?? definition.Name;
            Kind = PartKinds.GroupName(PartKinds.GroupOf(definition));
            IsEngine = PartKinds.IsBlock(definition);

            Detail = string.Join("  ·  ", new[] { PartTrees.Describe(part) }.Concat(Machining.Describe(catalog, part)));
            WorthDisplay = Money.Text(PartPricing.Round(PartPricing.WorthOfAssembly(catalog, part)));
        }

        public PartInstance Part { get; }
        public string Name { get; }
        public string Kind { get; }

        /// <summary>An engine block: it comes apart on the stand, not on the shelf</summary>
        public bool IsEngine { get; }

        public string Detail { get; }
        public string WorthDisplay { get; }
    }

    /// <summary>A job the picked part can have done (<see cref="RebuildOffer"/>), as its button reads</summary>
    public class RebuildOfferViewModel
    {
        public RebuildOfferViewModel(RebuildOffer offer, PartInstance part, decimal money)
        {
            Offer = offer;
            Part = part;
            Name = offer.Name;

            var values = new (string, object?)[]
            {
                ("cost", Money.Text(offer.Cost)),
                ("time", Services.Time.GameActionExtensions.DescribeMinutes(offer.Minutes)),
                ("after", Math.Round(offer.After * 100))
            };
            PriceDisplay = GameText.Say(offer.Job.IsExchange ? "workbench.rebuild.price.exchange" : "workbench.rebuild.price", values);
            WhyNot = offer.WhyNot ?? (money < offer.Cost ? GameText.Say("workbench.rebuild.cant_afford", ("cost", Money.Text(offer.Cost))) : string.Empty);
        }

        public RebuildOffer Offer { get; }

        /// <summary>The part it was offered for</summary>
        public PartInstance Part { get; }

        public string Name { get; }
        public string PriceDisplay { get; }

        /// <summary>Why it cannot be done now; empty when it can</summary>
        public string WhyNot { get; }

        public bool CanDo => WhyNot.Length == 0;
    }
}
