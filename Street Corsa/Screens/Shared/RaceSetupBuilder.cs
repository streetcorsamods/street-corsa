using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Race;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// Everything a race needs to know before AC starts, whoever set it up: a challenge at the diner or an
    /// event from the newspaper.
    /// </summary>
    public sealed class RaceEntry
    {
        public string PlayerName { get; init; } = string.Empty;

        /// <summary>The player's car, as owned; its folder is its definition id</summary>
        public required Car PlayerCar { get; init; }

        public string OpponentName { get; init; } = string.Empty;

        /// <summary>The folder of the opponent's car model</summary>
        public string OpponentCarId { get; init; } = string.Empty;

        public string? OpponentSkin { get; init; }

        /// <summary>The opponent's own car, when it has one on the books (a racer of the pool); null for a one-off event entrant</summary>
        public Car? OpponentCar { get; init; }

        /// <summary>How the opponent drives, from <see cref="OpponentAIAdapter"/>, the one place AC's AI values come from</summary>
        public AssettoCorsaAIParameters OpponentAI { get; init; } = new();

        public string TrackId { get; init; } = string.Empty;
        public string? TrackConfig { get; init; }
        /// <summary>The race's format id (<see cref="Services.Race.RaceFormats"/>)</summary>
        public string Format { get; init; } = string.Empty;

        /// <summary>The laps the race is over (an event's own); null: the format's</summary>
        public int? Laps { get; init; }

        public decimal CashWager { get; init; }
        public bool IsPinkSlip { get; init; }

        /// <summary>AC's damage for the race, in percent: the save's <see cref="GameRules.RaceDamagePercent"/>; by default the rules' full rate</summary>
        public int DamagePercent { get; init; } = Services.Configuration.IniModificationService.RaceDamage;

        /// <summary>The event raced for, or null for a street race</summary>
        public string? EventId { get; init; }

        /// <summary>The invitation raced for, so exactly that one is completed afterwards</summary>
        public Guid? EventInstanceId { get; init; }

        /// <summary>An event entrant who is not one of the racers of the pool: their stats are not kept</summary>
        public bool IsEventOnlyOpponent { get; init; }

        /// <summary>The police the race draws (<see cref="Services.Police.PoliceCars.Patrol"/>); null when none come</summary>
        public RacePolice? Police { get; init; }

        /// <summary>The game's time the race starts at, which AC's sun follows; null races at noon</summary>
        public DateTime? RaceTime { get; init; }

        /// <summary>A bracket race's dial-ins and how the rival drives; null for a heads-up race</summary>
        public BracketSetup? Bracket { get; init; }

        /// <summary>
        /// Test-and-tune: the player alone on the strip for this many passes, nothing at stake; null for a race. The
        /// opponent's fields stay empty.
        /// </summary>
        public int? TunePasses { get; init; }

        /// <summary>
        /// A field: the rivals after the opponent, in their order on the grid behind it. Empty for a race of two. A field
        /// races for cash only: no wager, no pink slip.
        /// </summary>
        public IReadOnlyList<RaceRival> Field { get; init; } = [];

        /// <summary>The player's place on the grid, 1 at the front; null at the front</summary>
        public int? PlayerGridPosition { get; init; }

        /// <summary>A staggered start: the seconds each car goes after the one ahead of it; null, all at the green</summary>
        public double? Stagger { get; init; }
    }

    /// <summary>One rival of a field after the opponent (<see cref="RaceEntry.Field"/>)</summary>
    public sealed class RaceRival
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>The folder of their car's model</summary>
        public string CarId { get; init; } = string.Empty;

        public string? Skin { get; init; }

        /// <summary>Their own car, when they have one on the books; null for an event's one-off entrant</summary>
        public Car? Car { get; init; }

        /// <summary>How they drive, from <see cref="OpponentAIAdapter"/></summary>
        public AssettoCorsaAIParameters AI { get; init; } = new();

        /// <summary>An event's one-off entrant: their stats are not kept</summary>
        public bool IsEventOnly { get; init; }
    }

    /// <summary>What came of putting a race together</summary>
    public sealed class RaceSetupResult
    {
        /// <summary>The race to launch; null when the player's car cannot go</summary>
        public DragRaceLaunchIntent? Intent { get; init; }

        /// <summary>Why the player's car will not run, for the player; empty when it will</summary>
        public string PlayerCarProblem { get; init; } = string.Empty;

        public bool CanRace => Intent != null;
    }

    /// <summary>
    /// Puts a race together the same way wherever it was set up: each car on what its parts make of it, an
    /// opponent in the player's model in a marked copy of the folder, the AI from <see cref="OpponentAIAdapter"/>,
    /// and a <see cref="RaceContext"/> that describes the race exactly as it is launched.
    ///
    /// The diner and the newspaper both go through here, so an event is raced on its own track with the
    /// same car data and clone handling as a challenge.
    /// </summary>
    public sealed class RaceSetupBuilder
    {
        private readonly ICarPartsService _parts;
        private readonly RaceCarDataService _raceCarData;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("RaceSetup");

        public RaceSetupBuilder(ICarPartsService parts, RaceCarDataService raceCarData)
        {
            _parts = parts;
            _raceCarData = raceCarData;
        }

        /// <summary>
        /// Both cars' data and the launch intent. The player's car that will not run makes no race; an
        /// opponent's car that will not run races as its author made it.
        /// </summary>
        public async Task<RaceSetupResult> BuildAsync(RaceEntry entry)
        {
            // A car the last races left blown, wrecked or totaled goes nowhere until it is repaired. Its parts are
            // judged, so it gets them first if it never had any.
            await EnsurePartsAsync(entry.PlayerCar);
            if (entry.OpponentCar != null) await EnsurePartsAsync(entry.OpponentCar);
            var groupOf = await PartGroupsAsync();
            var damage = CarCondition.WhyCannotRace(entry.PlayerCar, groupOf);
            if (damage.Count > 0)
            {
                return new RaceSetupResult { PlayerCarProblem = string.Join("; ", damage) };
            }

            // Each car goes in with the damage it carries. The rivals look after their cars (OpponentLifeService), and
            // one whose car can't race isn't offered a race (the diner, the events), so theirs goes in as it is too.
            var playerStart = CarCondition.StartState(entry.PlayerCar, groupOf);
            var opponentStart = entry.OpponentCar == null ? null : CarCondition.StartState(entry.OpponentCar, groupOf);

            var playerData = await PrepareCarDataAsync(entry.PlayerCar, playerStart);
            if (playerData is { CanDrive: false })
            {
                return new RaceSetupResult { PlayerCarProblem = playerData.Problem };
            }

            RaceCarData? opponentData = null;
            if (entry.OpponentCar != null)
            {
                opponentData = await PrepareCarDataAsync(entry.OpponentCar, opponentStart);
                if (opponentData is { CanDrive: false })
                {
                    _logger.Warning("{Opponent}'s car would not run ({Problem}): it races as its author made it", entry.OpponentName, opponentData.Problem);
                    opponentData = null;
                }
            }

            // Each car of a model already in the race races in a copy of its folder, so each has its own data and sound
            var models = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [entry.PlayerCar.DefinitionId] = 1 };
            var (opponentRacesAs, sharedData) = RacesAs(entry.OpponentCarId, opponentData, models);
            if (!string.Equals(opponentRacesAs, entry.OpponentCarId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Information("{Opponent} drives the same model as the player: it races in a copy, {Clone}", entry.OpponentName, opponentRacesAs);
            }

            var carData = new List<RaceCarData>();
            if (playerData != null) carData.Add(playerData);
            if (sharedData != null) carData.Add(sharedData);

            // The rest of a field, as many as the format runs, each on its own car's data the same way as the opponent:
            // the parts and the snapshots on this thread, one car after another, the data of all of them built at once
            var rivals = RacingField(entry);
            var starts = new List<RaceStartState?>();
            var building = new List<Task<RaceCarData?>>();
            foreach (var rival in rivals)
            {
                RaceStartState? start = null;
                if (rival.Car != null)
                {
                    await EnsurePartsAsync(rival.Car);
                    start = CarCondition.StartState(rival.Car, groupOf);
                }
                starts.Add(start);
                building.Add(rival.Car == null ? Task.FromResult<RaceCarData?>(null) : PrepareWithPartsAsync(rival.Car, start));
            }
            await Task.WhenAll(building);

            var field = new List<(RaceRival Rival, string RacesAs, RaceStartState? Start, RaceCarData? Data)>();
            for (var i = 0; i < rivals.Count; i++)
            {
                var rival = rivals[i];
                var data = building[i].Result;
                if (data is { CanDrive: false })
                {
                    _logger.Warning("{Rival}'s car would not run ({Problem}): it races as its author made it", rival.Name, data.Problem);
                    data = null;
                }

                var (racesAs, raceData) = RacesAs(rival.CarId, data, models);
                if (raceData != null) carData.Add(raceData);
                field.Add((rival, racesAs, starts[i], data));
            }

            // The race mode runs each engine's heat from how its car is cooled, and puts on the parts it does not have
            var intent = BuildIntent(entry, opponentRacesAs, carData, field.Select(f => f.RacesAs).ToList());
            // Both cars' wear layers at once: each is a few seconds the first time for a car
            var playerShown = ShownForRaceAsync(entry.PlayerCar);
            var opponentShown = opponentStart == null ? Task.FromResult<ShownSet?>(null) : ShownForRaceAsync(entry.OpponentCar!);
            await Task.WhenAll(playerShown, opponentShown);
            intent.Race.PlayerStart = playerStart with { Cooling = playerData?.Cooling, Shown = playerShown.Result, AxleStrength = AxleStrength(entry.PlayerCar) };
            intent.Race.OpponentStart = opponentStart == null ? null : opponentStart with { Cooling = opponentData?.Cooling, Shown = opponentShown.Result,
                AxleStrength = AxleStrength(entry.OpponentCar!) };

            // The field's cars' wear layers and paint, all at once as the two above
            var fieldShown = field.Select(f => f.Start == null ? Task.FromResult<ShownSet?>(null) : ShownForRaceAsync(f.Rival.Car!)).ToList();
            await Task.WhenAll(fieldShown);
            for (var i = 0; i < field.Count; i++)
            {
                var (rival, _, start, data) = field[i];
                intent.Race.Field[i].Start = start == null ? null : start with { Cooling = data?.Cooling, Shown = fieldShown[i].Result,
                    AxleStrength = AxleStrength(rival.Car!) };
            }

            return new RaceSetupResult { Intent = intent };
        }

        /// <summary>What the car's rear axle holds, for the race mode to break it past that; null without an axle of parts</summary>
        private double? AxleStrength(Car car)
        {
            try
            {
                return _parts.IsAvailable && car.HasAxleAssigned ? RearAxle.Weakest(_parts.Catalog, RearAxle.Mounted(car.Parts))?.Strength : null;
            }
            catch (Exception ex)
            {
                _logger.Warning("{Car}: what its rear axle holds could not be told, nothing on it breaks: {Error}", car.DefinitionId, ex.Message);
                return null;
            }
        }

        /// <summary>What the car shows in the race, its wear layer's model and its paint's textures written for it</summary>
        private Task<ShownSet?> ShownForRaceAsync(Car car) => ShownForRaceAsync(_parts.Shown(car), car);

        /// <summary><paramref name="shown"/> of <paramref name="car"/>, its wear layer's model and its paint's textures written for the race</summary>
        public static async Task<ShownSet?> ShownForRaceAsync(ShownSet? shown, Car car)
        {
            var directory = System.IO.Path.Combine(Configuration.AppSettings.Instance.CarsPath, car.DefinitionId);
            var worn = await Looks.WearLayer.ForRaceAsync(shown, directory);
            return await Looks.CarPaint.ForRaceAsync(worn, directory, car.SkinId);
        }

        /// <summary>
        /// The folder the opponent's car races in, and its data: an opponent in the player's model races in a
        /// copy of the folder under its own id, so each car has its own data and its own sound.
        /// </summary>
        public static (string RacesAs, RaceCarData? Data) RacesAs(string playerCarId, string opponentCarId, RaceCarData? opponentData) =>
            RacesAs(opponentCarId, opponentData, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [playerCarId] = 1 });

        /// <summary>
        /// The folder a rival's car races in, and its data, given the models already in the race (<paramref name="models"/>,
        /// each with how many cars of it, counted on here): the first car of a model races in its folder, every later
        /// one in a copy of its own (<see cref="AcCarFolder.CloneIdFor(string, int)"/>)
        /// </summary>
        public static (string RacesAs, RaceCarData? Data) RacesAs(string carId, RaceCarData? carData, Dictionary<string, int> models)
        {
            var before = models.GetValueOrDefault(carId);
            models[carId] = before + 1;
            if (before == 0) return (carId, carData);

            var cloneId = AcCarFolder.CloneIdFor(carId, before);
            var data = carData == null
                ? RaceCarData.AsAuthored(carId, cloneId)
                : new RaceCarData(carData.CarId, carData.Build) { CloneId = cloneId, Cooling = carData.Cooling };
            return (cloneId, data);
        }

        /// <summary>The launch intent and the race context it carries, both describing the same race</summary>
        /// <param name="fieldRacesAs">The folder each rival of <see cref="RaceEntry.Field"/> races in, in its order; none for a race of two</param>
        public static DragRaceLaunchIntent BuildIntent(RaceEntry entry, string opponentRacesAs, List<RaceCarData> carData, IReadOnlyList<string>? fieldRacesAs = null)
        {
            var field = RacingField(entry);
            var intent = new DragRaceLaunchIntent
            {
                Race = new DragRaceIntent
                {
                    PlayerCarId = entry.PlayerCar.DefinitionId,
                    PlayerSkin = entry.PlayerCar.SkinId ?? string.Empty,
                    PlayerName = entry.PlayerName,
                    OpponentCarId = opponentRacesAs,
                    OpponentSkin = entry.OpponentSkin ?? string.Empty,
                    OpponentName = entry.OpponentName,
                    OpponentAILevel = entry.OpponentAI.AILevel,
                    OpponentAIAggression = entry.OpponentAI.AIAggression,
                    TrackId = entry.TrackId,
                    TrackConfig = entry.TrackConfig,
                    Format = entry.Format,
                    Laps = entry.Laps,
                    DamagePercent = entry.DamagePercent,
                    Police = entry.Police,
                    RaceTime = entry.RaceTime,
                    Bracket = entry.Bracket,
                    TunePasses = entry.TunePasses,
                    Field = field.Select((rival, i) => new FieldCar
                    {
                        CarId = fieldRacesAs is { } racesAs && i < racesAs.Count ? racesAs[i] : rival.CarId,
                        Skin = rival.Skin ?? string.Empty,
                        Name = rival.Name,
                        AILevel = rival.AI.AILevel,
                        AIAggression = rival.AI.AIAggression
                    }).ToList(),
                    PlayerGridPosition = entry.PlayerGridPosition,
                    Stagger = entry.Stagger
                },
                PlayerCarInstanceId = entry.PlayerCar.InstanceId,
                OpponentCarInstanceId = entry.OpponentCar?.InstanceId ?? Guid.Empty,
                CashWager = entry.CashWager,
                IsPinkSlip = entry.IsPinkSlip,
                CarData = carData
            };

            intent.Metadata["RaceContext"] = new RaceContext
            {
                PlayerName = entry.PlayerName,
                OpponentName = entry.OpponentName,
                PlayerCarInstanceId = entry.PlayerCar.InstanceId,
                OpponentCarInstanceId = entry.OpponentCar?.InstanceId ?? Guid.Empty,
                OpponentCarDefinitionId = entry.OpponentCarId,
                CashWager = entry.CashWager,
                IsPinkSlip = entry.IsPinkSlip,
                TrackId = entry.TrackId,
                TrackConfig = entry.TrackConfig,
                Format = Services.Race.RaceFormats.Current.IdOf(entry.Format),
                Family = Services.Race.RaceFormats.Current.Of(entry.Format).Family,
                Laps = entry.Laps,
                EventId = entry.EventId,
                EventInstanceId = entry.EventInstanceId,
                IsEventOnlyOpponent = entry.IsEventOnlyOpponent,
                IsTestAndTune = entry.TunePasses != null,
                PlayerDialIn = entry.Bracket?.PlayerDialIn,
                OpponentDialIn = entry.Bracket?.OpponentDialIn,
                Field = field.Select(rival => new FieldEntrant
                {
                    Name = rival.Name,
                    CarInstanceId = rival.Car?.InstanceId ?? Guid.Empty,
                    CarDefinitionId = rival.CarId,
                    IsEventOnly = rival.IsEventOnly
                }).ToList()
            };

            return intent;
        }

        /// <summary>
        /// Where an event is raced: the track it names when that is installed, else one of the installed tracks
        /// that suits its format's family (a dragstrip for a drag race, anything else for a road race). A track with
        /// layouts is raced on one of them. The drag strip AC ships (ks_drag, drag1000) is the drag default when
        /// it is there. A race of more than one lap (the format's, or the event's own <paramref name="laps"/>) is only
        /// raced on a layout that comes back round, the named track's too. <paramref name="seed"/> makes the pick the
        /// same every time for one invitation. Null when nothing installed suits the event.
        /// </summary>
        public static (string TrackId, string? TrackConfig)? PickTrack(
            IEnumerable<Models.AC.TrackInfo> tracks, string format, string? wantedTrackId, int seed, int? laps = null)
        {
            var installed = tracks.ToList();
            var needsLoop = Services.Race.RaceFormats.Current.Of(format).NeedsLoop(laps);

            IEnumerable<(string TrackId, string? TrackConfig)> Layouts(Models.AC.TrackInfo track) =>
                (track.Configurations.Count == 0
                    ? new[] { (track.TrackId, (string?)null) }
                    : track.Configurations.Select(c => (track.TrackId, (string?)c.FolderName)))
                .Where(l => !needsLoop || !track.IsPointToPoint(l.Item2));

            if (!string.IsNullOrWhiteSpace(wantedTrackId))
            {
                var wanted = installed.FirstOrDefault(t => string.Equals(t.TrackId, wantedTrackId, StringComparison.OrdinalIgnoreCase));
                if (wanted != null && Layouts(wanted).Any()) return Layouts(wanted).First();
            }

            var isDrag = Services.Race.RaceFormats.Current.IsDrag(format);
            var candidates = installed
                .Where(t => (t.Type == Models.AC.TrackType.Dragstrip) == isDrag)
                .OrderBy(t => t.TrackId, StringComparer.OrdinalIgnoreCase)
                .SelectMany(Layouts)
                .ToList();
            if (candidates.Count == 0) return null;

            if (isDrag)
            {
                var defaults = new DragRaceIntent();
                var stock = candidates.FirstOrDefault(c =>
                    string.Equals(c.TrackId, defaults.TrackId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.TrackConfig, defaults.TrackConfig, StringComparison.OrdinalIgnoreCase));
                if (stock.TrackId != null) return stock;
            }

            return candidates[(int)((uint)seed % (uint)candidates.Count)];
        }

        /// <summary>
        /// The strip for a test-and-tune or a bracket race: the track asked for (<paramref name="trackId"/>) when it is
        /// installed, else the drag strip AC ships (ks_drag, drag1000) when it is there, else the longest installed
        /// dragstrip layout that runs the strip (<paramref name="onlyTo"/>, else the quarter mile:
        /// <see cref="BracketRules.RunsTheStrip"/>), else the longest there is. With <paramref name="onlyTo"/> (a bracket
        /// race, which the mode ends at the strip's finish, <see cref="BracketRules.StripMetres(string?)"/>) only a
        /// layout that runs that far will do. Null when there is none.
        /// </summary>
        public static (string TrackId, string? TrackConfig)? PickStrip(IEnumerable<Models.AC.TrackInfo> tracks,
            double? onlyTo = null, string? trackId = null)
        {
            var strip = onlyTo ?? BracketRules.QuarterMetres;
            var layouts = tracks
                .Where(t => t.Type == Models.AC.TrackType.Dragstrip)
                .SelectMany(t => t.Configurations.Count == 0
                    ? new[] { (TrackId: t.TrackId, TrackConfig: (string?)null, Quarter: BracketRules.RunsTheStrip(t, null, strip), Metres: BracketRules.LengthMetres(t, null)) }
                    : t.Configurations.Select(c => (TrackId: t.TrackId, TrackConfig: (string?)c.FolderName, Quarter: BracketRules.RunsTheStrip(t, c, strip), Metres: BracketRules.LengthMetres(t, c))))
                .Where(l => onlyTo == null || l.Quarter)
                .ToList();
            if (layouts.Count == 0) return null;

            if (!string.IsNullOrEmpty(trackId))
            {
                var asked = layouts.Where(l => string.Equals(l.TrackId, trackId, StringComparison.OrdinalIgnoreCase)).ToList();
                if (asked.Count > 0) layouts = asked;
            }

            var defaults = new DragRaceIntent();
            var stock = layouts.FirstOrDefault(l =>
                string.Equals(l.TrackId, defaults.TrackId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.TrackConfig, defaults.TrackConfig, StringComparison.OrdinalIgnoreCase));
            if (stock.TrackId != null) return (stock.TrackId, stock.TrackConfig);

            var best = layouts
                .OrderByDescending(l => l.Quarter)
                .ThenByDescending(l => l.Metres ?? 0)
                .ThenBy(l => l.TrackId, StringComparer.OrdinalIgnoreCase)
                .First();
            return (best.TrackId, best.TrackConfig);
        }

        private async Task EnsurePartsAsync(Car car)
        {
            try
            {
                await _parts.EnsurePartsAsync(car);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not give {Car} its parts: it races on its own figures", car.DefinitionId);
            }
        }

        /// <summary>The parts' groups off the catalog, loaded off the UI thread; null when there are no parts</summary>
        private async Task<Func<string, string?>?> PartGroupsAsync()
        {
            try
            {
                return await Task.Run(() => _parts.IsAvailable ? CarCondition.Groups(_parts.Catalog) : null);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The parts catalog could not be read: the cars race on their own figures");
                return null;
            }
        }

        /// <summary>The car's data as its parts make it, put together off the UI thread; null when it races as it is</summary>
        public async Task<RaceCarData?> PrepareCarDataAsync(Car car, RaceStartState? damage = null)
        {
            try
            {
                await _parts.EnsurePartsAsync(car);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not prepare the data of {Car}: it races as it is", car.DefinitionId);
                return null;
            }
            return await PrepareWithPartsAsync(car, damage);
        }

        /// <summary><see cref="PrepareCarDataAsync"/> of a car already given its parts</summary>
        private async Task<RaceCarData?> PrepareWithPartsAsync(Car car, RaceStartState? damage)
        {
            try
            {
                // On a copy made here: the car's own parts belong to the UI thread
                var snapshot = car.Snapshot();
                return await Task.Run(() => _raceCarData.Prepare(snapshot, damage));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not prepare the data of {Car}: it races as it is", car.DefinitionId);
                return null;
            }
        }

        /// <summary>
        /// The rivals of <see cref="RaceEntry.Field"/> who race: as many as the entry's format runs after the opponent,
        /// none on a test-and-tune. The launch intent and the race context are both built on these, so race.ini and the
        /// result the career reads count the same cars.
        /// </summary>
        public static IReadOnlyList<RaceRival> RacingField(RaceEntry entry) => entry.TunePasses != null ? []
            : entry.Field.Take(Math.Max(0, Services.Race.RaceFormats.Current.Of(entry.Format).Rivals - 1)).ToList();
    }
}
