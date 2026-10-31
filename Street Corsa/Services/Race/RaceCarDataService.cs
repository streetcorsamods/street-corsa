using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.Race
{
    /// <summary>The data a car goes racing with, as its parts make it</summary>
    public sealed class RaceCarData
    {
        public RaceCarData(string carId, CarBuildResult build)
        {
            CarId = carId;
            Build = build;
        }

        /// <summary>The car's folder under content\cars</summary>
        public string CarId { get; }

        /// <summary>
        /// The id the car races under when another car of its model is in the race: it gets a copy of the folder
        /// with its own data and sound (<see cref="Parts.Export.AcCarFolder"/>); null races in the car's own folder
        /// </summary>
        public string? CloneId { get; init; }

        /// <summary>A car that races as its author made it, in a copy of its folder</summary>
        public static RaceCarData AsAuthored(string carId, string cloneId) => new(carId, new CarBuildResult()) { CloneId = cloneId };

        public CarBuildResult Build { get; }

        /// <summary>How the car's engine is cooled and oiled, for the race mode; null leaves it a factory car's</summary>
        public Models.Race.EngineCoolingRating? Cooling { get; init; }

        public bool CanDrive => Build.CanDrive;

        /// <summary>Why the car cannot go, for the player</summary>
        public string Problem => string.Join("; ", Build.Problems);
    }

    /// <summary>
    /// Turns what a car has on it into the Assetto Corsa data files for a race: the engine on the dyno, the
    /// running gear against the factory's. A car without parts (no catalog, an older save) races on the
    /// data its author gave it.
    /// </summary>
    public class RaceCarDataService
    {
        private readonly ICarPartsService _parts;
        private readonly CarDataOverlay _overlay;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarData");

        public RaceCarDataService(ICarPartsService parts, CarDataOverlay overlay)
        {
            _parts = parts;
            _overlay = overlay;
        }

        /// <param name="damage">The damage the car goes in with; null takes what its parts carry</param>
        /// <param name="rateCooling">False for a free run: only the race mode runs the heat</param>
        /// <returns>The files to race with; null when the car has no parts to speak of and races as it is</returns>
        public RaceCarData? Prepare(Car car, Models.Race.RaceStartState? damage = null, bool rateCooling = true)
        {
            if (!_parts.IsAvailable || !car.HasPartsAssigned) return null;

            // The race files are built from the car's own data. If an earlier race left it changed (a restore
            // that failed), building on that would scale the running gear twice: put it back first.
            try
            {
                _overlay.RestoreIfLeftover(car.DefinitionId);
            }
            catch (Exception ex)
            {
                _logger.Warning("{Car}: data left changed by an earlier race could not be put back, racing as it is: {Error}",
                    car.DefinitionId, ex.Message);
                return null;
            }

            // A leftover that could not go back (Assetto Corsa is running, or it is still listed as changed from a race
            // whose restore failed) is still in the car's folder: the car races as it is rather than on data built
            // from data that is not its own
            if (_overlay.IsApplied(car.DefinitionId))
            {
                _logger.Warning("{Car}: data left changed by an earlier race is still in place, racing as it is", car.DefinitionId);
                return null;
            }

            var catalog = _parts.Catalog;
            var carDirectory = Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId);
            Func<string, string?> readFile;
            try
            {
                readFile = AcCarDataReader.ForCar(carDirectory);
            }
            catch (Exception ex)
            {
                _logger.Warning("{Car}: data cannot be read, racing as it is: {Error}", car.DefinitionId, ex.Message);
                return null;
            }

            var build = Build(car, damage ?? CarCondition.StartState(car, CarCondition.Groups(catalog)),
                CarSetup.For(car.Parts, car.Setup, catalog, Rules.RuleBook.Current.Tuning.Ranges()));

            var result = AcCarBuild.Generate(catalog, build, readFile, new AcEngineDataOptions { OverRevDamage = Rules.RuleBook.Current.Condition.OverRevDamage });
            if (result.CanDrive) result.Sound = ChooseSound(car, build.Engine);
            var cooling = rateCooling ? RateCooling(car, build.Engine) : null;
            _logger.Information("{Car}: {Power:0} hp, {Files} file(s) to change{Sound}{Problems}{Cooling}", car.DefinitionId, build.Engine?.Dyno?.MaxPowerHp ?? 0,
                result.Files.Count, result.Sound == null ? "" : ", the sound of " + result.Sound.DonorId,
                result.Problems.Count == 0 ? "" : ", cannot drive: " + string.Join("; ", result.Problems),
                cooling == null ? "" : $", cooling {cooling.Cooling:0.00}, fan {cooling.Fan:0.00}, sump {cooling.OilG:0.00} g");
            return new RaceCarData(car.DefinitionId, result) { Cooling = cooling };
        }

        private CarBuild Build(Car car, Models.Race.RaceStartState? damage, SetupChoice? setup)
        {
            var catalog = _parts.Catalog;
            return new CarBuild
            {
                Engine = _parts.Evaluate(car),
                FactoryEngineMass = _parts.FactoryEngineMass(car),
                RunningGear = car.HasRunningGearAssigned ? RunningGear.Mounted(car.Parts, catalog) : null,
                SwayBars = car.HasRunningGearAssigned && car.HasSwayBarsAssigned ? RunningGear.SwayBars(car.Parts) : null,
                FactoryRunningGear = car.HasRunningGearAssigned ? _parts.FactoryRunningGear(car) : null,
                RearAxle = car.HasAxleAssigned ? RearAxle.Mounted(car.Parts) : null,
                FactoryAxle = car.HasAxleAssigned ? _parts.FactoryAxle(car.DefinitionId) : null,
                Damage = damage,
                Chassis = RunningGear.Chassis(car.Parts, catalog),
                Setup = setup
            };
        }

        /// <summary>
        /// The car's setup as the Tune screen shows it: what may move as the car's data, with its parts on, can take it
        /// (<see cref="AcSetupData.ForData"/>), and the items the data has no place for. Without the data to read (the car
        /// not installed, a race's data still in its folder) the rules' and the parts' ranges as they are. Reads files:
        /// off the UI thread.
        /// </summary>
        public (SetupChoice Setup, IReadOnlySet<string> Gone) TuneSetup(Car car)
        {
            var catalog = _parts.Catalog;
            var setup = CarSetup.For(car.Parts, car.Setup, catalog, Rules.RuleBook.Current.Tuning.Ranges());
            IReadOnlySet<string> none = new HashSet<string>();
            if (!_parts.IsAvailable || !car.HasPartsAssigned || _overlay.IsApplied(car.DefinitionId)) return (setup, none);

            try
            {
                var readFile = AcCarDataReader.ForCar(Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId));
                var made = AcCarBuild.Generate(catalog, Build(car, null, null), readFile).Files;
                string? Read(string name) => made.GetValueOrDefault(name) ?? readFile(name);
                return (AcSetupData.ForData(setup, Read), AcSetupData.NoPlaceIn(Read));
            }
            catch (Exception ex)
            {
                _logger.Warning("{Car}: data cannot be read for the Tune screen, its ranges shown as the parts give them: {Error}", car.DefinitionId, ex.Message);
                return (setup, none);
            }
        }

        // The heat is the race mode's to run: whatever goes wrong rating it, the car races cooled like a factory car
        private Models.Race.EngineCoolingRating? RateCooling(Car car, EngineReport? engine)
        {
            try
            {
                return _parts.RateCooling(car, engine);
            }
            catch (Exception ex)
            {
                _logger.Warning("{Car}: its cooling could not be rated, it races cooled like a factory car: {Error}", car.DefinitionId, ex.Message);
                return null;
            }
        }

        // The sound is the least of a build: whatever goes wrong choosing it, the car races on its parts with its own
        private CarSound? ChooseSound(Car car, EngineReport? engine)
        {
            try
            {
                return _parts.ChooseSound(car, engine);
            }
            catch (Exception ex)
            {
                _logger.Warning("{Car}: no sound could be chosen, it keeps its own: {Error}", car.DefinitionId, ex.Message);
                return null;
            }
        }
    }
}
