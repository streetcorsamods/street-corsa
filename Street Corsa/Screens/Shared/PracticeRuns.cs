using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// The player's selected car out on its own, from the racing hub: a free run on any track, a test-and-tune at a drag
    /// strip. Each is set up in two steps, so the hub can check the player is still home between them: the car checked
    /// and its data made (a car that will not go is told and stays in), then the run. The picks are remembered in the
    /// settings, across saves.
    /// </summary>
    public sealed class PracticeRuns
    {
        /// <summary>How many passes a test-and-tune runs</summary>
        public const int TestAndTunePasses = 6;

        private readonly DialogService _dialogService;
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly ICarPartsService _partsService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly IGameTimeService _timeService;
        private readonly GameSettingsService _settingsService;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly RaceCarDataService _raceCarDataService;
        private readonly RaceSetupBuilder _raceSetup;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Practice");

        public PracticeRuns(
            DialogService dialogService,
            IAssettoCorsaLauncher launcher,
            ICarPartsService partsService,
            IGameStateRepository gameStateRepo,
            IGameTimeService timeService,
            GameSettingsService settingsService,
            IAssettoCorsaContentService contentService,
            RaceCarDataService raceCarDataService,
            RaceSetupBuilder raceSetup)
        {
            _dialogService = dialogService;
            _launcher = launcher;
            _partsService = partsService;
            _gameStateRepo = gameStateRepo;
            _timeService = timeService;
            _settingsService = settingsService;
            _contentService = contentService;
            _raceCarDataService = raceCarDataService;
            _raceSetup = raceSetup;
        }

        /// <summary>Assetto Corsa is running (or being set up): nothing else goes out meanwhile</summary>
        public bool IsBusy => _launcher.IsExecutionLocked;

        /// <summary>The car the player has selected; null with none (a game with no car)</summary>
        public static Car? CarOf(GameState gameState) =>
            gameState.Player.SelectedCarInstanceId is { } id ? gameState.Player.Cars?.FirstOrDefault(c => c.InstanceId == id) : null;

        /// <summary>Why the selected car cannot go out on the streets (<see cref="StreetReady"/>); null when it can</summary>
        /// <param name="listed">Whether the catalog has the car</param>
        public Task<NotStreetReady?> WhyNotOnTheStreetsAsync(GameState gameState, Func<string, bool> listed) =>
            StreetReady.WhyNotAsync(gameState, listed, _partsService);

        /// <summary>Where a free run can go: every track of the install, one entry per layout</summary>
        public List<PracticeTrack> FreeRunTracks() => List(PracticeTracks.All, "free run");

        /// <summary>Where a test-and-tune can go: the drag strips' layouts</summary>
        public List<PracticeTrack> Strips() => List(PracticeTracks.Strips, "test-and-tune");

        private List<PracticeTrack> List(Func<IEnumerable<Models.AC.TrackInfo>, List<PracticeTrack>> pick, string what)
        {
            try
            {
                return pick(_contentService.GetTracks());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the tracks for a {What}", what);
                return [];
            }
        }

        /// <summary>The free run's track last time ("track" or "track/configuration")</summary>
        public string? RememberedFreeRun => _settingsService.Current.FreeRunTrack;

        /// <summary>The test-and-tune's strip last time</summary>
        public string? RememberedStrip => _settingsService.Current.TestAndTuneStrip;

        /// <summary>The pick, kept in the settings for next time; written only when it changed</summary>
        public void Remember(PracticeTrack track, bool strip)
        {
            var settings = _settingsService.Current;
            if (track.Key == (strip ? settings.TestAndTuneStrip : settings.FreeRunTrack)) return;

            try
            {
                if (strip) settings.TestAndTuneStrip = track.Key;
                else settings.FreeRunTrack = track.Key;
                _settingsService.Save();
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not remember the {What}'s track: {Error}", strip ? "test-and-tune" : "free run", ex.Message);
            }
        }

        /// <summary>
        /// A free run of <paramref name="car"/> on <paramref name="track"/>, on what its parts make of it, made ready: null
        /// when it will not go (the player is told why)
        /// </summary>
        public async Task<FreeRunLaunchIntent?> PrepareFreeRunAsync(GameState gameState, Car car, PracticeTrack track)
        {
            _logger.Information("Free run: {Car} on {Track}", car.DefinitionId, track.Key);
            Remember(track, strip: false);

            try
            {
                // The car races on its parts; one that will not go stays in
                RaceCarData? data = null;
                if (_partsService.IsAvailable)
                {
                    if (await _partsService.EnsurePartsAsync(car) && !string.IsNullOrEmpty(gameState.SaveName)) _gameStateRepo.Save(gameState, gameState.SaveName);

                    // Blown, wrecked or totaled: it goes nowhere until it is repaired
                    var damage = CarCondition.WhyCannotRace(car, CarCondition.Groups(_partsService.Catalog));
                    if (damage.Count > 0)
                    {
                        Tell(GameText.Say("garage.wont_run.damage", ("damage", string.Join("; ", damage)), ("fix", WhereToSortOut(car))),
                            GameText.Say("garage.wont_run.title"));
                        return null;
                    }

                    // On a copy made here: the workbench may change the car's own parts while the worker reads them
                    var snapshot = car.Snapshot();
                    data = await Task.Run(() => _raceCarDataService.Prepare(snapshot, rateCooling: false));
                }

                if (data is { CanDrive: false })
                {
                    Tell(GameText.Say("garage.wont_run.problem", ("problem", data.Problem)), GameText.Say("garage.wont_run.title"));
                    return null;
                }

                var intent = new FreeRunLaunchIntent
                {
                    CarId = car.DefinitionId,
                    SkinId = car.SkinId,
                    PlayerName = gameState.Player.Name,
                    TrackId = track.TrackId,
                    TrackConfig = track.Configuration,
                    Shown = await Looks.WearLayer.ForRaceAsync(_partsService.Shown(car), System.IO.Path.Combine(Configuration.AppSettings.Instance.CarsPath, car.DefinitionId))
                };
                if (data != null) intent.CarData.Add(data);
                return intent;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception setting up the free run");
                Tell(GameText.Say("garage.free_run_failed", ("error", ex.Message)), GameText.Say("garage.launch_error.title"));
                return null;
            }
        }

        /// <summary>
        /// The free run itself: a practice session the player ends when they have had enough. No opponent, no stake, no
        /// results; an hour goes by.
        /// </summary>
        public async Task FreeRunAsync(GameState gameState, FreeRunLaunchIntent intent)
        {
            try
            {
                var result = await _launcher.LaunchRaceAsync(intent);
                if (result.Success)
                {
                    await _timeService.SpendTimeAsync(gameState, GameAction.FreeRun);
                }
                else
                {
                    _logger.Error("Free run failed: {Error}", result.ErrorMessage);
                    Tell(GameText.Say("garage.free_run_failed", ("error", result.ErrorMessage)), GameText.Say("garage.launch_error.title"));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during the free run");
                Tell(GameText.Say("garage.free_run_failed", ("error", ex.Message)), GameText.Say("garage.launch_error.title"));
            }
        }

        /// <summary>
        /// A test-and-tune of <paramref name="car"/> at <paramref name="strip"/>, set up: pass after pass, each with its
        /// own tree and timeslip, nothing at stake but the car's wear. Run through the race pipeline (the race mode, a
        /// result, the timeslips after); an hour goes by. Null when it will not go (the player is told why).
        /// </summary>
        public async Task<DragRaceLaunchIntent?> PrepareTestAndTuneAsync(GameState gameState, Car car, PracticeTrack strip)
        {
            _logger.Information("Test-and-tune: {Car} on {Track}/{Layout}", car.DefinitionId, strip.TrackId, strip.Configuration ?? "(none)");
            Remember(strip, strip: true);

            try
            {
                var setup = await _raceSetup.BuildAsync(new RaceEntry
                {
                    PlayerName = gameState.Player.Name,
                    PlayerCar = car,
                    TrackId = strip.TrackId,
                    TrackConfig = strip.Configuration,
                    Format = Services.Race.RaceFormats.Current.First(Models.Race.RaceFormatFamilies.Drag)?.Id ?? string.Empty,
                    DamagePercent = gameState.Rules.RaceDamagePercent,
                    RaceTime = gameState.Date,
                    TunePasses = TestAndTunePasses
                });

                if (setup.Intent == null)
                {
                    Tell(GameText.Say("garage.wont_run.damage", ("damage", setup.PlayerCarProblem), ("fix", WhereToSortOut(car))),
                        GameText.Say("garage.wont_run.title"));
                    return null;
                }

                return setup.Intent;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception setting up the test-and-tune");
                Tell(GameText.Say("garage.strip_failed", ("error", ex.Message)), GameText.Say("garage.launch_error.title"));
                return null;
            }
        }

        /// <summary>
        /// Where a car that will not go is sorted out: an empty tank at the gas station (the garage only has a can of
        /// gas, to get it there), the damage with the Repairs button
        /// </summary>
        private string WhereToSortOut(Car car)
        {
            var dry = CarCondition.FuelLeft(car) is { } fuel && fuel < CarCondition.TooLittleToRaceLitres;
            if (!dry) return GameText.Say("garage.fix.repairs");

            var groupOf = _partsService.IsAvailable ? CarCondition.Groups(_partsService.Catalog) : null;
            var damaged = CarCondition.WhyCannotRace(car, groupOf, counting: false).Count > 0;
            // A game with no gas station fills the tank overnight
            var overnight = Rules.RuleBook.Current.Systems.Station ? string.Empty : ".overnight";
            return damaged ? GameText.Say("garage.fix.repairs_and_gas" + overnight) : GameText.Say("garage.fix.gas" + overnight);
        }

        private void Tell(string message, string title) =>
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
    }
}
