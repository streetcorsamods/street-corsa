using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.Tune
{
    /// <summary>
    /// The garage's Tune screen for one car (chassis tuning phase 3): a tab for each part of the car with something to
    /// set, a slider per item within what the rules and the mounted parts allow, the items a part would open greyed with
    /// the parts they need. The engine's items run the dyno as they move. Nothing is written until the tune is done:
    /// then it takes the garage's time (<see cref="CarTune.Minutes"/>) through <c>perform</c>; cancelled, the car is as
    /// it was.
    /// </summary>
    public class TuneDialogViewModel : BaseDialogViewModel
    {
        private readonly Car _car;
        private readonly PartsCatalog _catalog;
        private readonly Rules.TuningRules _rules;
        private readonly Func<int, double, Task> _perform;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Tune");
        private EngineReport? _stockEngine;
        private int _dynoRun;
        private bool _busy;

        /// <param name="rows">The car's rows (<see cref="CarTune.Rows"/>)</param>
        /// <param name="perform">Writes the tune onto the car, runs the clock and saves: the items changed and the minutes</param>
        public TuneDialogViewModel(DialogService dialogService, Car car, string carName, PartsCatalog catalog, Rules.TuningRules rules,
            IReadOnlyList<TuneRow> rows, Func<int, double, Task> perform)
            : base(dialogService)
        {
            _car = car;
            _catalog = catalog;
            _rules = rules;
            _perform = perform;
            CarName = carName;

            foreach (var tab in Enum.GetValues<TuneTab>())
            {
                var own = rows.Where(r => r.Tab == tab).Select(r => new TuneRowViewModel(r, OnRowChanged)).ToList();
                if (own.Count > 0) Tabs.Add(new TuneTabViewModel(this, tab, own));
            }

            _selected = Tabs.FirstOrDefault();
            if (_selected != null) _selected.IsSelected = true;

            CancelCommand = new RelayCommand(() => Close(), () => !_busy);
            ApplyCommand = new AsyncRelayCommand(Apply, () => !_busy && Changed > 0, name: "Tune");
            StockCommand = new RelayCommand(BackToStock, () => !_busy && Selected?.Rows.Any(r => !r.IsLocked && !r.IsStock) == true);

            if (HasEngineTab) _ = RunDyno(stock: true);
        }

        public string CarName { get; }

        public ObservableCollection<TuneTabViewModel> Tabs { get; } = new();

        public bool HasRows => Tabs.Count > 0;

        private TuneTabViewModel? _selected;

        public TuneTabViewModel? Selected
        {
            get => _selected;
            set
            {
                if (value == null || ReferenceEquals(value, _selected)) return;

                if (_selected != null) _selected.IsSelected = false;
                _selected = value;
                _selected.IsSelected = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowsDyno));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public RelayCommand CancelCommand { get; }
        public AsyncRelayCommand ApplyCommand { get; }

        /// <summary>The tab on show back to the parts' own figures</summary>
        public RelayCommand StockCommand { get; }

        private IEnumerable<TuneRowViewModel> AllRows => Tabs.SelectMany(t => t.Rows);

        /// <summary>How many items the tune changes</summary>
        public int Changed => AllRows.Count(r => r.IsChanged);

        /// <summary>The garage time the tune takes, as the Done button says it</summary>
        public string ApplyText => Changed == 0
            ? GameText.Say("tune.apply.nothing")
            : GameText.Say("tune.apply", ("time", TimeWords(CarTune.Minutes(Changed, _rules))));

        private static string TimeWords(double minutes)
        {
            var span = TimeSpan.FromMinutes(Math.Round(minutes));
            return span.TotalHours >= 1
                ? GameText.Say("tune.time.hours", ("h", (int)span.TotalHours), ("m", span.Minutes.ToString("00", CultureInfo.InvariantCulture)))
                : GameText.Say("tune.time.minutes", ("m", span.Minutes));
        }

        #region Dyno

        private bool HasEngineTab => Tabs.Any(t => t.Tab == TuneTab.Engine);

        /// <summary>The engine tab is on show: the dyno beside it</summary>
        public bool ShowsDyno => Selected?.Tab == TuneTab.Engine;

        private string _power = string.Empty;

        /// <summary>Peak power and torque as the engine's items stand</summary>
        public string Power
        {
            get => _power;
            private set => SetProperty(ref _power, value);
        }

        private string _powerChange = string.Empty;

        /// <summary>What the tune does to the peak power, against the engine as it is</summary>
        public string PowerChange
        {
            get => _powerChange;
            private set => SetProperty(ref _powerChange, value);
        }

        private PointCollection _stockCurve = new();

        /// <summary>The power curve as the engine is, on the dyno's box (<see cref="CurveWidth"/> by <see cref="CurveHeight"/>)</summary>
        public PointCollection StockCurve
        {
            get => _stockCurve;
            private set => SetProperty(ref _stockCurve, value);
        }

        private PointCollection _tunedCurve = new();

        /// <summary>The power curve as the tune has it</summary>
        public PointCollection TunedCurve
        {
            get => _tunedCurve;
            private set => SetProperty(ref _tunedCurve, value);
        }

        public const double CurveWidth = 340;
        public const double CurveHeight = 150;

        private async Task RunDyno(bool stock = false)
        {
            var run = ++_dynoRun;
            var set = Pending().ToList();
            try
            {
                // A slider dragged runs many: only the last one's figures are wanted
                if (!stock) await Task.Delay(120);
                if (run != _dynoRun) return;

                var (before, after) = await Task.Run(() =>
                {
                    var asIs = stock || _stockEngine == null ? Evaluate(_car.Engine) : _stockEngine;
                    return (asIs, Evaluate(CarTune.EngineWith(_car, set)));
                });
                if (run != _dynoRun) return;

                _stockEngine = before;
                ShowDyno(before, after);
            }
            catch (Exception ex)
            {
                _logger.Warning("The dyno could not run the tune: {Error}", ex.Message);
            }
        }

        private EngineReport? Evaluate(PartInstance? engine) => engine == null ? null : EngineFactory.Evaluate(_catalog, engine);

        private void ShowDyno(EngineReport? before, EngineReport? after)
        {
            if (after?.Dyno is not { } dyno || !after.Runs)
            {
                Power = GameText.Say("tune.dyno.not_running");
                PowerChange = string.Empty;
                TunedCurve = new PointCollection();
                return;
            }

            Power = GameText.Say("tune.dyno.peak", ("hp", Measure.Power(dyno.MaxPowerHp)), ("rpm", Math.Round(dyno.MaxPowerRpm)),
                ("nm", Measure.Torque(dyno.MaxTorque)));
            var change = before?.Dyno is { } own ? dyno.MaxPowerHp - own.MaxPowerHp : 0;
            PowerChange = Math.Abs(change) < 0.5 ? string.Empty : GameText.Say("tune.dyno.change", ("hp", change.ToString("+0;-0", Locale.Culture)));

            // Both curves on the same scale: the higher of the two peaks to the top, the further of the two limits to the right
            var topRpm = Math.Max(after.DrivenToRpm, before?.DrivenToRpm ?? 0);
            var topHp = Math.Max(dyno.MaxPowerHp, before?.Dyno?.MaxPowerHp ?? 0) * 1.08;
            StockCurve = Curve(before, topRpm, topHp);
            TunedCurve = Curve(after, topRpm, topHp);
        }

        private static PointCollection Curve(EngineReport? report, double topRpm, double topHp)
        {
            var points = new PointCollection();
            if (report?.Dyno is not { } dyno || topRpm <= 0 || topHp <= 0) return points;

            foreach (var (rpm, torque) in dyno.Curve)
            {
                if (rpm > report.DrivenToRpm) break;

                var hp = torque * rpm * Math.PI / 30.0 / DynoResult.WattsPerHp;
                points.Add(new System.Windows.Point(rpm / topRpm * CurveWidth, CurveHeight - Math.Clamp(hp / topHp, 0, 1) * CurveHeight));
            }

            points.Freeze();
            return points;
        }

        #endregion

        private IEnumerable<(TuneRow Row, double Value)> Pending() => AllRows.Where(r => !r.IsLocked).Select(r => (r.Row, r.Value));

        private void OnRowChanged(TuneRowViewModel row)
        {
            OnPropertyChanged(nameof(Changed));
            OnPropertyChanged(nameof(ApplyText));
            RelayCommand.RaiseCanExecuteChanged();
            ApplyCommand.RaiseCanExecuteChanged();
            if (row.Row.Tab == TuneTab.Engine) _ = RunDyno();
        }

        private void BackToStock()
        {
            if (Selected == null) return;

            foreach (var row in Selected.Rows.Where(r => !r.IsLocked)) row.Value = row.Row.Stock;
        }

        private async Task Apply()
        {
            if (_busy) return;

            var changed = Changed;
            if (changed == 0) return;

            _busy = true;
            RelayCommand.RaiseCanExecuteChanged();
            ApplyCommand.RaiseCanExecuteChanged();
            try
            {
                CarTune.Apply(_car, Pending());
                await _perform(changed, CarTune.Minutes(changed, _rules));
                Close();
            }
            finally
            {
                _busy = false;
                RelayCommand.RaiseCanExecuteChanged();
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>One tab of the Tune screen and its rows</summary>
    public sealed class TuneTabViewModel : ObservableObject
    {
        private readonly TuneDialogViewModel _owner;

        public TuneTabViewModel(TuneDialogViewModel owner, TuneTab tab, IReadOnlyList<TuneRowViewModel> rows)
        {
            _owner = owner;
            Tab = tab;
            Rows = rows;
            Name = GameText.Say(TabKey(tab));
        }

        public TuneTab Tab { get; }
        public string Name { get; }
        public IReadOnlyList<TuneRowViewModel> Rows { get; }

        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (!SetProperty(ref _isSelected, value) || !value) return;
                _owner.Selected = this;
            }
        }

        private static string TabKey(TuneTab tab) => tab switch
        {
            TuneTab.Alignment => "tune.tab.alignment",
            TuneTab.Suspension => "tune.tab.suspension",
            TuneTab.Engine => "tune.tab.engine",
            TuneTab.Gearing => "tune.tab.gearing",
            _ => "tune.tab.brakes_tyres"
        };
    }

    /// <summary>One slider of the Tune screen: its item, where it stands, and the figure in the player's units</summary>
    public sealed class TuneRowViewModel : ObservableObject
    {
        private readonly Action<TuneRowViewModel> _changed;

        public TuneRowViewModel(TuneRow row, Action<TuneRowViewModel> changed)
        {
            Row = row;
            _changed = changed;
            _value = row.Value;
            Name = TuneWords.Name(row);
            Detail = row.IsLocked
                ? GameText.Say("tune.needs", ("parts", string.Join(", ", row.Needs)))
                : row.Definition is { } part ? part.DisplayName ?? part.Name : string.Empty;
        }

        public TuneRow Row { get; }
        public string Name { get; }

        /// <summary>The part the item is on, or what a locked item needs</summary>
        public string Detail { get; }

        public bool IsLocked => Row.IsLocked;
        public double Minimum => Row.Range.Min;
        public double Maximum => Row.IsLocked ? Row.Range.Min + 1 : Row.Range.Max;
        public double Step => Row.Range.Step > 0 ? Row.Range.Step : (Row.Range.Max - Row.Range.Min) / 100;

        private double _value;

        public double Value
        {
            get => _value;
            set
            {
                if (IsLocked) return;

                var snapped = Row.Range.Snap(value);
                if (!SetProperty(ref _value, snapped)) return;

                OnPropertyChanged(nameof(ValueText));
                OnPropertyChanged(nameof(IsChanged));
                OnPropertyChanged(nameof(IsStock));
                _changed(this);
            }
        }

        public string ValueText => IsLocked ? GameText.Say("tune.locked") : TuneWords.Figure(Row, _value);

        /// <summary>Moved from where it was when the screen opened</summary>
        public bool IsChanged => Math.Abs(_value - Row.Value) > 1e-9;

        /// <summary>At the parts' own figure</summary>
        public bool IsStock => Math.Abs(_value - Row.Stock) < 1e-9;
    }

    /// <summary>The Tune screen's words for an item and its figure, in the player's units</summary>
    public static class TuneWords
    {
        /// <summary>An item's name: <c>tune.item.&lt;id&gt;</c>, a part's spec <c>tune.spec.&lt;id&gt;</c>, else the id made readable</summary>
        public static string Name(TuneRow row)
        {
            var key = (row.IsCarItem ? "tune.item." : "tune.spec.") + row.Item;
            if (GameText.Installed.Has(key)) return GameText.Say(key);

            var words = row.Item.Replace('_', ' ');
            return words.Length == 0 ? row.Item : char.ToUpper(words[0], Locale.Culture) + words[1..];
        }

        /// <summary>
        /// The figure as the player reads it: a car-wide item as the change from the parts' own (+0.5°, -1.0 in), a
        /// part's spec as the figure itself
        /// </summary>
        public static string Figure(TuneRow row, double value)
        {
            var imperial = Locale.Units == UnitSystem.Imperial;
            if (row.IsCarItem)
            {
                return row.Item switch
                {
                    CarSetup.CamberFront or CarSetup.CamberRear or CarSetup.SteerLock => Degrees(value, row.Item == CarSetup.SteerLock ? "+0;-0;0" : "+0.0;-0.0;0.0"),
                    CarSetup.ToeFront or CarSetup.ToeRear => Measure.ShortLength(value).ToString(imperial ? "+0.000;-0.000;0.000" : "+0.0;-0.0;0.0", null),
                    CarSetup.RideHeightFront or CarSetup.RideHeightRear => Measure.ShortLength(value).ToString(imperial ? "+0.00;-0.00;0.00" : "+0;-0;0", null),
                    CarSetup.PressureFront or CarSetup.PressureRear => Measure.Pressure(value).ToString(imperial ? "+0.0;-0.0;0.0" : "+0.00;-0.00;0.00", null),
                    CarSetup.BrakeBias => GameText.Say("tune.figure.bias", ("change", (value * 100).ToString("+0;-0;0", Locale.Culture))),
                    _ => Number(value, row.Range.Step)
                };
            }

            return row.Item switch
            {
                "drop" or "ride_height" => Measure.ShortLength(value).ToString(imperial ? "0.00" : "0", null),
                "timing" => Degrees(value, "0.#"),
                _ => Number(value, row.Range.Step)
            };
        }

        private static string Degrees(double value, string format) => Locale.UnitWords("deg", value.ToString(format, Locale.Culture));

        /// <summary>A plain figure, to as many places as its step has</summary>
        private static string Number(double value, double step)
        {
            var places = 0;
            for (var s = step; places < 4 && s > 0 && Math.Abs(s - Math.Round(s)) > 1e-9; s *= 10) places++;
            return value.ToString(places == 0 ? "N0" : "N" + places.ToString(CultureInfo.InvariantCulture), Locale.Culture);
        }
    }
}
