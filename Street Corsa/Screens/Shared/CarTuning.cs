using StreetCorsa.Dialogs;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// The Tune screen for a car (<see cref="Dialogs.Tune.TuneDialogViewModel"/>), from the garage's Tune button and from
    /// the racing hub between test-and-tune runs: its rows made off the UI thread from the data the car's parts make,
    /// and the tune, once done, written, its garage time spent and the game saved.
    /// </summary>
    public sealed class CarTuning
    {
        private readonly DialogService _dialogService;
        private readonly ICarPartsService _partsService;
        private readonly RaceCarDataService? _raceCarData;
        private readonly IGameTimeService _timeService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly Action<Car>? _anyTuned;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Tune");

        /// <param name="raceCarData">What the car's parts make of its data; none (a test): the setup from the parts alone</param>
        /// <param name="anyTuned">Runs after every tune, whoever opened the screen (the garage behind the racing hub shows the car anew)</param>
        public CarTuning(DialogService dialogService, ICarPartsService partsService, RaceCarDataService? raceCarData,
            IGameTimeService timeService, IGameStateRepository gameStateRepo, Action<Car>? anyTuned = null)
        {
            _anyTuned = anyTuned;
            _dialogService = dialogService;
            _partsService = partsService;
            _raceCarData = raceCarData;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
        }

        /// <summary>Whether a car can be tuned at all: the parts catalog is loaded</summary>
        public bool IsAvailable => _partsService.IsAvailable;

        /// <summary>
        /// The Tune screen for <paramref name="car"/>, shown once its rows are made. False when it was not shown: no
        /// catalog, the rows could not be made (logged), or <paramref name="stillWanted"/> said no once they were.
        /// </summary>
        /// <param name="stillWanted">Asked once the rows are made: the screen behind may have moved on meanwhile</param>
        /// <param name="tuned">Runs after a tune is written, its time spent and the game saved</param>
        /// <param name="closed">Runs as the screen closes, done or cancelled</param>
        /// <param name="jumpQueue">Shows in front at once (<see cref="DialogService.ShowDialog(IDialog, bool)"/>)</param>
        public async Task<bool> ShowAsync(GameState gameState, Car car, string carName, Func<bool> stillWanted,
            Action? tuned = null, Action? closed = null, bool jumpQueue = false)
        {
            if (!_partsService.IsAvailable) return false;

            var catalog = _partsService.Catalog;
            var rules = Rules.RuleBook.Current.Tuning;
            IReadOnlyList<Parts.Cars.TuneRow> rows;
            try
            {
                // On a copy: the garage stays up, and the workbench may change the car's parts while the worker reads them
                var copy = car.Snapshot();
                var (setup, gone) = _raceCarData != null
                    ? await Task.Run(() => _raceCarData.TuneSetup(copy))
                    : (Parts.Cars.CarSetup.For(car.Parts, car.Setup, catalog, rules.Ranges()), new HashSet<string>());
                rows = Parts.Cars.CarTune.Rows(car, catalog, setup, gone);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "{Car}: the Tune screen could not be made", car.DefinitionId);
                return false;
            }

            if (!stillWanted()) return false;

            _dialogService.ShowDialog(new Dialogs.Tune.TuneDialogViewModel(_dialogService, car, carName, catalog, rules, rows,
                async (changed, minutes) =>
                {
                    _logger.Information("Tuned {Car}: {Changed} item(s), {Minutes:0} min", car.DefinitionId, changed, minutes);
                    try
                    {
                        await _timeService.SpendTimeAsync(gameState, (int)Math.Round(minutes));
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Could not spend the time for the tune: {Error}", ex.Message);
                    }

                    // The car's dyno figure as the rivals' is kept: the diner's matchup reads it, the save keeps it
                    if (car.Engine is { } engine)
                    {
                        try
                        {
                            var tunedEngine = Parts.Cars.PartTrees.Clone(engine, keepIds: true);
                            car.PowerHp = Services.Market.UsedCarMarketService.PowerOf(await Task.Run(() => Parts.Cars.EngineFactory.Evaluate(catalog, tunedEngine)));
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning("Could not run the tuned engine on the dyno: {Error}", ex.Message);
                        }
                    }

                    GameSaves.TrySave(_gameStateRepo, gameState, _logger, "a tune");
                    tuned?.Invoke();
                    _anyTuned?.Invoke(car);
                }, closed), jumpQueue);
            return true;
        }
    }
}
