using System.Collections.ObjectModel;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;
using StreetCorsa.Helpers;

namespace StreetCorsa.Dialogs.RepairShop
{
    /// <summary>
    /// The garage's repair bay for one car: what is wrong with it, what each job costs and takes, and doing them one at
    /// a time. The money and the clock are the garage's (<paramref name="perform"/>); a job the player cannot pay for
    /// cannot be started.
    /// </summary>
    public class RepairShopDialogViewModel : BaseDialogViewModel
    {
        private readonly Car _car;
        private readonly PartsCatalog? _catalog;
        private readonly double _priceMultiplier;
        private readonly Func<decimal> _money;
        private readonly Func<RepairJob, Task> _perform;
        private bool _busy;

        /// <param name="catalog">Null when there are no parts: only the body can be done</param>
        /// <param name="priceMultiplier">The save's <see cref="GameRules.PartPriceMultiplier"/></param>
        /// <param name="perform">Takes the money, does the job, runs the clock and saves</param>
        public RepairShopDialogViewModel(DialogService dialogService, Car car, string carName, PartsCatalog? catalog,
            double priceMultiplier, Func<decimal> money, Func<RepairJob, Task> perform)
            : base(dialogService)
        {
            _car = car;
            _catalog = catalog;
            _priceMultiplier = priceMultiplier;
            _money = money;
            _perform = perform;
            CarName = carName;
            CloseCommand = new RelayCommand(() => Close(), () => !_busy);
            Refresh();
        }

        public string CarName { get; }

        public ObservableCollection<RepairJobRow> Jobs { get; } = new();

        public bool HasJobs => Jobs.Count > 0;

        public string MoneyDisplay => Money.Text(_money());

        private string _status = string.Empty;

        /// <summary>Whether the car can race as it is, in a line</summary>
        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public RelayCommand CloseCommand { get; }

        private void Refresh()
        {
            Jobs.Clear();
            foreach (var job in Parts.Cars.RepairShop.Jobs(_car, _catalog, _priceMultiplier, PumpPrices()))
            {
                Jobs.Add(new RepairJobRow(job, new AsyncRelayCommand(() => Do(job), () => !_busy && _money() >= job.Cost, name: "Repair")));
            }

            var problems = CarCondition.WhyCannotRace(_car, _catalog == null ? null : CarCondition.Groups(_catalog));
            // The tank is the gas station's to fill, not the garage's
            var dry = CarCondition.FuelLeft(_car) is { } fuel && fuel < CarCondition.TooLittleToRaceLitres;
            Status = problems.Count > 0
                ? GameText.Say(dry ? "repair.not_fit.dry" : "repair.not_fit", ("problems", string.Join("; ", problems)))
                : Jobs.Count > 0 ? GameText.Say("repair.races_damaged") : GameText.Say("repair.nothing");

            OnPropertyChanged(nameof(HasJobs));
            OnPropertyChanged(nameof(MoneyDisplay));
        }

        /// <summary>
        /// What the can of gas is paid at: the pump prices of the town's gas stations taken together (the player drives
        /// to any of them), never a price of the garage's own nor the first mod's alone; the base game's with no station
        /// </summary>
        private static StationPrices? PumpPrices()
        {
            var stations = StationScenes.Installed;
            if (stations.Count == 0) return null;

            return new StationPrices(
                stations.Average(s => s.Prices.FuelPerLitre), stations.Average(s => s.Prices.OilPerLitre),
                stations.Average(s => s.Prices.Wash), stations.Average(s => s.Prices.Tow));
        }

        private async Task Do(RepairJob job)
        {
            if (_busy || _money() < job.Cost) return;

            _busy = true;
            RaiseAll();
            try
            {
                await _perform(job);
            }
            finally
            {
                _busy = false;
                Refresh();
                RaiseAll();
            }
        }

        private void RaiseAll()
        {
            RelayCommand.RaiseCanExecuteChanged();
            foreach (var row in Jobs) row.Command.RaiseCanExecuteChanged();
        }
    }
}
