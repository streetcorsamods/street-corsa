using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// Service for selecting opponents for race events.
    /// Prefers pool opponents when available, falls back to special event opponents.
    /// </summary>
    public class EventOpponentService : IEventOpponentService
    {
        private readonly ICarFilterService _filterService;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly Func<Car, bool> _canRace;
        private readonly Random _random;

        /// <param name="canRace">Whether a rival's car can race today; a racer whose car is laid up sits the event out</param>
        /// <param name="random">The dice; a test seeds its own</param>
        public EventOpponentService(
            ICarFilterService filterService,
            IContentCatalogRepository catalogRepository,
            Func<Car, bool>? canRace = null,
            Random? random = null)
        {
            _random = random ?? new Random();
            _filterService = filterService;
            _catalogRepository = catalogRepository;
            _canRace = canRace ?? (_ => true);
        }

        public EventOpponentResult? GetOpponentForEvent(
            RaceEventDefinition eventDef,
            GameState gameState)
        {
            // The catalog's cars that are in the install, read once for the whole choice rather than once per
            // opponent: only these can go to AC
            var installed = InstalledCars.Only(_catalogRepository.GetCarsByStatus(ContentStatus.Active), out _)
                .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

            if (eventDef.Field is { Max: > 1 } field)
                return GetField(eventDef, field, gameState, installed);

            // Try pool opponents first (unless exclusive)
            if (!eventDef.ExclusiveOpponents)
            {
                var poolOpponent = FindEligiblePoolOpponent(eventDef, gameState, installed);
                if (poolOpponent != null)
                    return poolOpponent;
            }

            // A pink slip changes hands between two garages: only a racer of the pool has one to take the
            // player's car into, and a car of their own to lose
            if (eventDef.IsPinkSlip)
                return null;

            // Fall back to special opponents: those whose car is installed, as for everybody else, or AC is handed a car it does not have
            var specials = (eventDef.SpecialOpponents ?? [])
                .Where(s => s != null && !string.IsNullOrEmpty(s.CarDefinitionId) && installed.ContainsKey(s.CarDefinitionId))
                .ToList();
            if (specials.Count > 0)
                return SpecialResult(specials[_random.Next(specials.Count)]);

            // No opponent available - generate a fallback
            return GenerateFallbackOpponent(eventDef, installed);
        }

        private EventOpponentResult? FindEligiblePoolOpponent(
            RaceEventDefinition eventDef,
            GameState gameState,
            Dictionary<string, CarDefinition> installed)
        {
            var candidates = PoolCandidates(eventDef, gameState, installed);
            if (candidates.Count == 0)
                return null;

            // Pick random eligible opponent
            var (selectedOpponent, opponentCar) = candidates[_random.Next(candidates.Count)];
            return PoolResult(selectedOpponent, opponentCar);
        }

        /// <summary>
        /// The racers of the pool who could race the event: ready, not a boss (but for a crew's leader when its field asks
        /// for him, <paramref name="field"/>), in an installed car that suits it and can race today
        /// </summary>
        private List<(Opponent Opponent, Car Car)> PoolCandidates(RaceEventDefinition eventDef, GameState gameState, Dictionary<string, CarDefinition> installed,
            EventField? field = null)
        {
            var crew = field is { From: EventFieldFrom.Crew, Boss: true } ? Opponents.CrewBook.Current.Get(field.Crew) : null;
            var candidates = new List<(Opponent, Car)>();
            foreach (var racer in gameState.Racers.ReadyToRace.Values)
            {
                if (racer is not Opponent opponent) continue;
                if (opponent.IsBoss && !Opponents.CrewBook.IsLeader(crew, opponent)) continue;
                var car = opponent.Cars.FirstOrDefault();
                if (car == null || !_canRace(car) || !installed.TryGetValue(car.DefinitionId, out var carDef)) continue;
                if (eventDef.EntryRequirements != null && !_filterService.Matches(eventDef.EntryRequirements, carDef, car)) continue;
                candidates.Add((opponent, car));
            }

            return candidates;
        }

        /// <summary>A racer named by an event: their id, with the mod's name or without it</summary>
        private static bool IsRacer(string named, string definitionId) =>
            string.Equals(named, definitionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Mods.ModIds.Local(named), Mods.ModIds.Local(definitionId), StringComparison.OrdinalIgnoreCase);

        private static EventOpponentResult PoolResult(Opponent opponent, Car car) => new()
        {
            IsPoolOpponent = true,
            PoolOpponent = opponent,
            PoolOpponentCar = car,
            OpponentName = opponent.Name,
            OpponentNickname = opponent.Nickname,
            CarDefinitionId = car.DefinitionId,
            CarSkin = car.SkinId ?? "default",
            Skill = opponent.Skill,
            Aggression = opponent.Aggression,
            Reputation = opponent.Stats.Reputation
        };

        private static EventOpponentResult SpecialResult(EventOpponent special) => new()
        {
            IsPoolOpponent = false,
            SpecialOpponent = special,
            OpponentName = special.Name,
            OpponentNickname = special.Nickname,
            CarDefinitionId = special.CarDefinitionId,
            CarSkin = special.CarSkin ?? "default",
            Skill = special.Skill,
            Aggression = special.Aggression
        };

        /// <summary>
        /// A field for the event: as many rivals as it rolls between its least and most, nobody twice. Its special
        /// entrants (installed) first, who join either way, up to the field's most; then the racers it names
        /// (<see cref="EventFieldFrom.Racers"/>), the crew's (<see cref="EventFieldFrom.Crew"/>: its members, and its
        /// leader only when asked for, <see cref="EventField.Boss"/>, and then first) or the pool's, in a random order, unless
        /// the event takes its own entrants only; a field from the pool is then filled out with one-off entrants, a field of
        /// named racers or of a crew only when it asks (<see cref="EventField.Fill"/>), else it is those who are there. Null
        /// when not even one rival can be found (nothing installed; nobody of the crew out tonight).
        /// </summary>
        private EventOpponentResult? GetField(RaceEventDefinition eventDef, EventField field, GameState gameState, Dictionary<string, CarDefinition> installed)
        {
            var specials = (eventDef.SpecialOpponents ?? [])
                .Where(s => s != null && !string.IsNullOrEmpty(s.CarDefinitionId) && installed.ContainsKey(s.CarDefinitionId))
                .ToList();
            var wanted = Math.Max(_random.Next(field.Min, field.Max + 1), Math.Min(specials.Count, field.Max));
            var picked = new List<EventOpponentResult>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { gameState.Player.Name };

            void Add(EventOpponentResult rival)
            {
                if (picked.Count < wanted && names.Add(rival.OpponentName)) picked.Add(rival);
            }

            foreach (var special in specials.OrderBy(_ => _random.Next())) Add(SpecialResult(special));

            if (!eventDef.ExclusiveOpponents)
            {
                // Who the field is drawn from, in a random order: the pool, the racers named, or the crew's members, its
                // leader only when asked for and then first (a crew night of three is him and two)
                var pool = PoolCandidates(eventDef, gameState, installed, field);
                var crew = field.From == EventFieldFrom.Crew ? Opponents.CrewBook.Current.Get(field.Crew) : null;
                IEnumerable<(Opponent Opponent, Car Car)> ordered = field.From switch
                {
                    EventFieldFrom.Racers => pool.Where(c => field.Racers.Any(id => IsRacer(id, c.Opponent.DefinitionId))).OrderBy(_ => _random.Next()),
                    EventFieldFrom.Crew when crew == null => [],
                    EventFieldFrom.Crew => pool
                        .Where(c => Opponents.CrewBook.Rides(crew, c.Opponent) && (field.Boss || !Opponents.CrewBook.IsLeader(crew, c.Opponent)))
                        .OrderBy(c => Opponents.CrewBook.IsLeader(crew, c.Opponent) ? 0 : 1).ThenBy(_ => _random.Next()),
                    _ => pool.OrderBy(_ => _random.Next())
                };
                foreach (var (opponent, car) in ordered) Add(PoolResult(opponent, car));
            }

            // The rest of a grid from the pool (or one asked to be filled out) made up; a name the dice gave twice is rolled
            // again, a few times at most
            var fills = field.From == EventFieldFrom.Pool || field.Fill;
            for (var tries = 0; fills && picked.Count < wanted && tries < wanted * 4; tries++)
            {
                if (GenerateFallbackOpponent(eventDef, installed) is not { } made) break;
                Add(made);
            }

            if (picked.Count == 0) return null;
            var first = picked[0];
            first.Field = picked.Skip(1).ToList();
            return first;
        }

        /// <summary>
        /// A basic opponent when none are available, so events can always be entered. The car is an installed
        /// one from the catalog, one that meets the event's requirements when there is such a car. Null only
        /// when nothing at all is installed.
        /// </summary>
        private EventOpponentResult? GenerateFallbackOpponent(RaceEventDefinition eventDef, Dictionary<string, CarDefinition> installed)
        {
            var cars = installed.Values.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToList();
            if (cars.Count == 0)
                return null;

            var fitting = eventDef.EntryRequirements == null
                ? cars
                : cars.Where(c => _filterService.Matches(eventDef.EntryRequirements, c)).ToList();
            var car = (fitting.Count > 0 ? fitting : cars)[_random.Next(fitting.Count > 0 ? fitting.Count : cars.Count)];
            var skin = car.AvailableSkins is { Count: > 0 } skins ? skins[_random.Next(skins.Count)] : "default";

            // Rolled once: the opponent that races and the one the result is about are the same one
            var opponent = new EventOpponent
            {
                Name = GameText.Installed.Pick("names.event_racers", _random),
                Nickname = GameText.Installed.Pick("names.event_nicknames", _random),
                Skill = _random.Next(Opponent.MinSkill, Opponent.MaxSkill + 1),
                Aggression = 40 + _random.Next(30),
                CarDefinitionId = car.Id,
                CarSkin = skin
            };

            return new EventOpponentResult
            {
                IsPoolOpponent = false,
                SpecialOpponent = opponent,
                OpponentName = opponent.Name,
                OpponentNickname = opponent.Nickname,
                CarDefinitionId = opponent.CarDefinitionId,
                CarSkin = skin,
                Skill = opponent.Skill,
                Aggression = opponent.Aggression
            };
        }
    }
}
