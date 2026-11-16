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
            if (strip) Remember(track, "test-and-tune", s => s.TestAndTuneStrip, (s, key) => s.TestAndTuneStrip = key);
            else Remember(track, "free run", s => s.FreeRunTrack, (s, key) => s.FreeRunTrack = key);
        }

        private void Remember(PracticeTrack track, string what, Func<GameSettings, string?> kept, Action<GameSettings, string> keep)
        {
            var settings = _settingsService.Current;
            if (track.Key == kept(settings)) return;

            try
            {
                keep(settings, track.Key);
                _settingsService.Save();
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not remember the {What}'s track: {Error}", what, ex.Message);
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
                    // Blown, wrecked or totaled: it goes nowhere until it is repaired
                    if (await ToldItWontGoAsync(gameState, car)) return null;

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
                    Shown = await RaceSetupBuilder.ShownForRaceAsync(_partsService.Shown(car), car)
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

        /// <summary>The format the player runs alone for the score (the hub's Free Drive); null when no mod has one</summary>
        public static Models.Race.RaceFormat? SoloFormat => RaceFormats.Current.Solo;

        /// <summary>
        /// Where a run alone of <paramref name="format"/> can go, of the roads already listed (<see cref="FreeRunTracks"/>):
        /// every road (no drag strip), and of a format run in traffic only the roads with a traffic plan or an AI line to
        /// build one from. A format run in traffic looks on the disk for every layout's: call it off the UI thread.
        /// </summary>
        public static List<PracticeTrack> SoloRoads(Models.Race.RaceFormat format, IEnumerable<PracticeTrack> roads) =>
            roads.Where(t => !t.IsStrip
                && (!format.Traffic || Services.Race.Traffic.TrafficSetup.HasPlan(t.Track.FolderPath, t.TrackId, t.Configuration))).ToList();

        /// <summary>The road of the last drive alone</summary>
        public string? RememberedSoloRoad => _settingsService.Current.SoloRoad;

        /// <summary>
        /// A run alone of <paramref name="car"/> on <paramref name="road"/> for the score of <paramref name="format"/>, set
        /// up: no opponent, nothing at stake but the car's wear, the road's traffic for a format run in traffic. Run through
        /// the race pipeline (the format's mode, a result, the run kept on the road's table); an hour goes by. Null when it
        /// will not go (the player is told why).
        /// </summary>
        /// <param name="trafficProgress">Told how getting the road's traffic ready goes, when models have to be made for it</param>
        /// <param name="cancel">Cancels getting the traffic ready: the run does not go, the models made so far are kept</param>
        /// <param name="options">A Challenge road's own laps, time, weather and traffic, and the Challenge; none: a Free Drive</param>
        public async Task<DragRaceLaunchIntent?> PrepareSoloRunAsync(GameState gameState, Car car, PracticeTrack road, Models.Race.RaceFormat format,
            IProgress<Services.Race.Traffic.TrafficProgress>? trafficProgress = null, CancellationToken cancel = default, SoloRunOptions? options = null)
        {
            _logger.Information("Drive alone ({Format}): {Car} on {Track}/{Layout}{Challenge}", format.Id, car.DefinitionId, road.TrackId, road.Configuration ?? "(none)",
                options?.ChallengeId is { } challenge ? $", road {options.ChallengeRoad + 1} of challenge {challenge}" : string.Empty);
            if (options?.ChallengeId == null) Remember(road, "drive alone", s => s.SoloRoad, (s, key) => s.SoloRoad = key);

            var inFront = options?.TellInFront ?? false;
            try
            {
                // The car first: it costs nothing, and the road's traffic can take minutes to make the first time
                if (_partsService.IsAvailable && await ToldItWontGoAsync(gameState, car, inFront)) return null;

                Models.Race.RaceTraffic? traffic = null;
                if (format.Traffic)
                {
                    var acRoot = Configuration.AppSettings.Instance.AssettoCorsaPath;
                    var density = _settingsService.Current.TrafficDensity;
                    var perLaneKm = options?.TrafficPerLaneKm ?? Rules.RuleBook.Current.Race.TrafficPerLaneKm;
                    var detail = Services.Race.Traffic.TrafficDetail.Current(_settingsService.Current.TrafficDetail);
                    var lanes = Enum.TryParse<Services.Race.Traffic.PlanLanes>(_settingsService.Current.TrafficLanes, true, out var picked)
                        ? picked : Services.Race.Traffic.PlanLanes.Auto;
                    try
                    {
                        traffic = await Task.Run(() => Services.Race.Traffic.TrafficSetup.For(acRoot, road.Track.FolderPath, road.TrackId, road.Configuration,
                            perLaneKm, density, Services.Race.Traffic.TrafficFleets.Installed, detail, Mods.ModSet.Installed, trafficProgress, cancel, lanes: lanes), cancel);
                        // Called off while the last model was being written: the work is kept, the drive does not go
                        cancel.ThrowIfCancellationRequested();
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Information("Getting the traffic ready was cancelled: no drive");
                        return null;
                    }
                    catch (Services.Race.Traffic.NoTrafficPlanException ex)
                    {
                        // Offered for its AI line, which made no plan after all: an empty road is no run in traffic
                        _logger.Warning("{Error}: no drive", ex.Message);
                        Tell(GameText.Say("garage.no_traffic", ("road", road.DisplayName)), GameText.Say("garage.launch_error.title"), inFront);
                        return null;
                    }
                    // A plan of the game's own in a folder race.ini cannot name (a comma in the player's profile's name):
                    // the mode would find none, and an empty road is no run in traffic
                    if (traffic.Plan is { } plan && !Services.Configuration.RaceIniBuilder.IniSafe(plan))
                    {
                        _logger.Warning("The traffic plan {Plan} cannot go into race.ini: no drive", plan);
                        Tell(GameText.Say("garage.no_traffic", ("road", road.DisplayName)), GameText.Say("garage.launch_error.title"), inFront);
                        return null;
                    }
                    // A road that names its traffic's size: that, at the player's density (as the road's own count takes
                    // it: held to its range, 1 when it is no figure) and the detail's share
                    if (options?.TrafficCars is { } cars)
                        traffic.Cars = Math.Clamp((int)Math.Round(cars * traffic.Density * detail.DensityFactor), Services.Race.Traffic.TrafficSetup.LeastCars,
                            Services.Race.Traffic.TrafficSetup.MostCars);
                    _logger.Information("Traffic: {Cars} car(s) at density {Density} ({Detail} detail), cast {Cast}, plan {Plan}", traffic.Cars, traffic.Density, detail.Key,
                        traffic.Cast ?? "(every model)", traffic.Plan ?? "(the track's own)");
                }

                // A Challenge road's rivals (experimental): one-off entrants, the first the opponent, the rest the field
                var rivals = (options?.Rivals ?? []).Select(r => new RaceRival
                {
                    Name = r.Name,
                    CarId = r.CarId,
                    Skin = r.Skin,
                    AI = Services.Opponents.OpponentAIAdapter.ToAssettoCorsaAI(new Opponent(r.Name, 25, Gender.Other, r.Skill, r.Aggression), gameState.Rules),
                    IsEventOnly = true
                }).ToList();
                if (rivals.Count > 0)
                    _logger.Information("Rivals on the road (experimental): {Rivals}", string.Join(", ", rivals.Select(r => $"{r.Name} ({r.CarId}, {r.AI.AILevel}/{r.AI.AIAggression})")));
                var opponent = rivals.FirstOrDefault();

                var setup = await _raceSetup.BuildAsync(new RaceEntry
                {
                    PlayerName = gameState.Player.Name,
                    PlayerCar = car,
                    OpponentName = opponent?.Name ?? string.Empty,
                    OpponentCarId = opponent?.CarId ?? string.Empty,
                    OpponentSkin = opponent?.Skin,
                    OpponentAI = opponent?.AI ?? new(),
                    IsEventOnlyOpponent = opponent != null,
                    Field = rivals.Skip(1).ToList(),
                    TrackId = road.TrackId,
                    TrackConfig = road.Configuration,
                    Format = format.Id,
                    Laps = options?.Laps,
                    DamagePercent = gameState.Rules.RaceDamagePercent,
                    RaceTime = options?.Minute is { } minute ? gameState.Date.Date.AddMinutes(minute) : gameState.Date,
                    Solo = true,
                    Traffic = traffic,
                    Weather = WeatherOf(options?.Weather),
                    ChallengeId = options?.ChallengeId,
                    ChallengeRoad = options?.ChallengeRoad ?? 0
                });

                if (setup.Intent == null)
                {
                    Tell(GameText.Say("garage.wont_run.damage", ("damage", setup.PlayerCarProblem), ("fix", WhereToSortOut(car))),
                        GameText.Say("garage.wont_run.title"), inFront);
                    return null;
                }

                return setup.Intent;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception setting up the drive alone");
                Tell(GameText.Say("garage.free_run_failed", ("error", ex.Message)), GameText.Say("garage.launch_error.title"), inFront);
                return null;
            }
        }

        /// <summary>
        /// Whether <paramref name="car"/> will not go as it stands (blown, wrecked, totaled, dry), the player told what
        /// and where it is sorted out. Its parts are judged, so it gets them first if it never had any.
        /// </summary>
        private async Task<bool> ToldItWontGoAsync(GameState gameState, Car car, bool inFront = false)
        {
            if (await _partsService.EnsurePartsAsync(car) && !string.IsNullOrEmpty(gameState.SaveName)) _gameStateRepo.Save(gameState, gameState.SaveName);

            var damage = CarCondition.WhyCannotRace(car, CarCondition.Groups(_partsService.Catalog));
            if (damage.Count == 0) return false;

            Tell(GameText.Say("garage.wont_run.damage", ("damage", string.Join("; ", damage)), ("fix", WhereToSortOut(car))),
                GameText.Say("garage.wont_run.title"), inFront);
            return true;
        }

        /// <summary>A weather Assetto Corsa has (its content\weather folder); null (the rules' world's) for one it lacks, told</summary>
        private string? WeatherOf(string? weather)
        {
            if (string.IsNullOrWhiteSpace(weather)) return null;
            var folder = System.IO.Path.Combine(Configuration.AppSettings.Instance.AssettoCorsaPath, "content", "weather", weather.Trim());
            if (System.IO.Directory.Exists(folder)) return weather.Trim();

            _logger.Warning("Assetto Corsa has no weather {Weather}: the rules' weather is driven in", weather);
            return null;
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
            var overnight = Navigation.FlowBook.InPlay("station") ? string.Empty : ".overnight";
            return damaged ? GameText.Say("garage.fix.repairs_and_gas" + overnight) : GameText.Say("garage.fix.gas" + overnight);
        }

        /// <param name="inFront">Over the dialog that is up (which stays, and comes back once this is read), not after it</param>
        private void Tell(string message, string title, bool inFront = false) =>
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title), jumpQueue: inFront);
    }

    /// <summary>
    /// A run alone set as a Challenge's road has it: its laps, time of day, weather and traffic (each null: as a Free Drive
    /// has it), and the Challenge and road it is
    /// </summary>
    public sealed record SoloRunOptions
    {
        public int? Laps { get; init; }

        /// <summary>The time of day, in minutes from midnight</summary>
        public int? Minute { get; init; }

        /// <summary>AC's weather (its folder)</summary>
        public string? Weather { get; init; }

        public double? TrafficPerLaneKm { get; init; }

        /// <summary>The traffic's size before the player's density</summary>
        public int? TrafficCars { get; init; }

        public string? ChallengeId { get; init; }

        /// <summary>Which of its roads, 0 the first</summary>
        public int ChallengeRoad { get; init; }

        /// <summary>The road's AI rivals (experimental); none: the player alone</summary>
        public IReadOnlyList<Services.Career.ChallengeRival>? Rivals { get; init; }

        /// <summary>
        /// What keeps the run from going is told over the dialog in front, which stays up (the Challenges), not after it
        /// (the racing hub, which closes as the setting up ends)
        /// </summary>
        public bool TellInFront { get; init; }
    }
}
