using StreetCorsa.Mods;
using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.Career.Filters;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// Implementation of race event management
    /// </summary>
    public class RaceEventService : IRaceEventService
    {
        // The events given, or those of the content in play (read again once another plays)
        private readonly Dictionary<string, RaceEventDefinition>? _given;
        private readonly ContentCache<Dictionary<string, RaceEventDefinition>> _inPlay = new(() => Index(CareerContent.Installed));

        private Dictionary<string, RaceEventDefinition> _eventDefinitions => _given ?? _inPlay.Value;

        private static Dictionary<string, RaceEventDefinition> Index(CareerContent content) =>
            new(content.Events, StringComparer.OrdinalIgnoreCase);
        private readonly ICarFilterService _filterService;
        private readonly Func<string, CarDefinition?>? _carDefinitionLookup;
        private readonly Random _random = new();

        /// <param name="content">The events the mods define; the content in play's when null</param>
        public RaceEventService(
            ICarFilterService filterService,
            Func<string, CarDefinition?>? carDefinitionLookup = null,
            CareerContent? content = null)
        {
            _filterService = filterService;
            _carDefinitionLookup = carDefinitionLookup;
            if (content != null) _given = Index(content);
        }

        public IEnumerable<RaceEventDefinition> GetAllEventDefinitions()
        {
            return _eventDefinitions.Values;
        }

        public RaceEventDefinition? GetEventDefinition(string eventId)
        {
            return _eventDefinitions.TryGetValue(eventId, out var evt) ? evt : null;
        }

        public IEnumerable<RaceEventDefinition> GetEligibleEvents(CareerState career)
        {
            var reputation = career.GetCounter(Models.Career.Milestones.MilestoneTrigger.ReputationReached);

            foreach (var evt in _eventDefinitions.Values)
            {
                // Check reputation requirement
                if (reputation < evt.MinReputation)
                    continue;

                // Check milestone requirements
                if (evt.RequiredMilestones.Any(m => !career.CompletedMilestones.Contains(m)))
                    continue;

                // Check if one-time event already completed
                if (evt.Schedule == EventSchedule.OneTime &&
                    career.CompletedEventIds.Contains(evt.Id))
                    continue;

                yield return evt;
            }
        }

        public IEnumerable<RaceEventInstance> GetActiveEvents(CareerState career, DateTime currentTime)
        {
            // Everything the stored state holds, and the track and opponent from the definition, as GenerateEvents
            // gives them to a new instance
            return career.ActiveEvents
                .Where(e => e.IsAvailable(currentTime))
                .Select(e => new RaceEventInstance(e, GetEventDefinition(e.EventDefinitionId)));
        }

        /// <summary>How likely an eligible event is to turn up when events are generated</summary>
        private static double EventChance => Rules.RuleBook.Current.Events.EventChance;

        /// <summary>Most invitations open in the paper at once</summary>
        public static int MaxOpenEvents => Rules.RuleBook.Current.Events.MaxOpenEvents;

        public List<RaceEventInstance> GenerateEvents(CareerState career, DateTime currentTime, double pinkSlipFactor = 1.0)
        {
            var newEvents = new List<RaceEventInstance>();
            var eligibleEvents = GetEligibleEvents(career).ToList();

            // Expired events are cleared by the caller first (EventGenerationTask), not twice a day

            // The paper has room for a few at a time; the order they are looked at is shuffled, so a full paper
            // is not always the same few
            var open = career.ActiveEvents.Count(e => e.IsAvailable(currentTime));

            foreach (var eventDef in eligibleEvents.OrderBy(_ => _random.Next()))
            {
                if (open >= MaxOpenEvents)
                    break;

                // Check if this event type is already active
                var hasActive = career.ActiveEvents.Any(e =>
                    e.EventDefinitionId == eventDef.Id && e.IsAvailable(currentTime));

                if (hasActive)
                    continue;

                // Check schedule constraints
                var lastCompleted = career.ActiveEvents
                    .Where(e => e.EventDefinitionId == eventDef.Id && e.IsCompleted)
                    .OrderByDescending(e => e.CompletedAt)
                    .FirstOrDefault();

                bool shouldGenerate = eventDef.Schedule switch
                {
                    EventSchedule.OneTime => !career.CompletedEventIds.Contains(eventDef.Id),
                    EventSchedule.Daily => lastCompleted == null ||
                        (currentTime - lastCompleted.CompletedAt)?.TotalHours >= 24,
                    EventSchedule.Weekly => lastCompleted == null ||
                        (currentTime - lastCompleted.CompletedAt)?.TotalDays >= 7,
                    EventSchedule.Permanent => true,
                    _ => false
                };

                if (!shouldGenerate)
                    continue;

                // Random chance to generate (not all eligible events appear each time); a pink-slip event
                // turns up more or less often with the game's difficulty. An event with odds of its own (a rare big
                // one) goes by them.
                var chance = eventDef.Chance ?? (eventDef.IsPinkSlip ? Math.Min(0.95, EventChance * pinkSlipFactor) : EventChance);
                if (_random.NextDouble() > chance && eventDef.Schedule != EventSchedule.Permanent)
                    continue;

                var state = new RaceEventState
                {
                    EventDefinitionId = eventDef.Id,
                    AvailableFrom = currentTime,
                    ExpiresAt = eventDef.Schedule switch
                    {
                        EventSchedule.Daily => currentTime.AddHours(24),
                        EventSchedule.Weekly => currentTime.AddDays(7),
                        _ => null
                    }
                };
                var instance = new RaceEventInstance(state, eventDef);

                // Add to career state, under the instance's id: that is what the invitation hands back. The plain
                // state, never the instance: the save keeps only that.
                career.ActiveEvents.Add(state);

                newEvents.Add(instance);
                open++;
            }

            return newEvents;
        }

        public bool CanEnterEvent(string eventId, string carDefinitionId, Car? carInstance, CareerState career)
        {
            var eventDef = GetEventDefinition(eventId);
            if (eventDef == null)
                return false;

            // Check reputation
            var reputation = career.GetCounter(Models.Career.Milestones.MilestoneTrigger.ReputationReached);
            if (reputation < eventDef.MinReputation)
                return false;

            // Check milestones
            if (eventDef.RequiredMilestones.Any(m => !career.CompletedMilestones.Contains(m)))
                return false;

            // Check car filter
            if (eventDef.EntryRequirements != null && _carDefinitionLookup != null)
            {
                var carDef = _carDefinitionLookup(carDefinitionId);
                if (carDef == null)
                    return false;

                if (!_filterService.Matches(eventDef.EntryRequirements, carDef, carInstance))
                    return false;
            }

            return true;
        }

        /// <param name="eventInstanceId">The instance the player entered (<see cref="RaceEventState.InstanceId"/>).
        /// Guid.Empty only for a race from before instances were carried into it: then the first open event in
        /// its window is taken, as it always was.</param>
        /// <param name="completedAt">Game time of the race (gameState.Date), never the real clock</param>
        public EventReward? CompleteEvent(Guid eventInstanceId, bool playerWon, CareerState career, DateTime completedAt)
        {
            var eventState = eventInstanceId != Guid.Empty
                ? career.ActiveEvents.FirstOrDefault(e => e.InstanceId == eventInstanceId && !e.IsCompleted)
                // A day of slack past the expiry: completedAt is after the race's time was spent, and a race
                // started late in the evening finishes the next morning, past the midnight its event expired at
                : career.ActiveEvents.FirstOrDefault(e =>
                    !e.IsCompleted &&
                    e.AvailableFrom <= completedAt &&
                    (!e.ExpiresAt.HasValue || e.ExpiresAt.Value.AddDays(1) >= completedAt));

            if (eventState == null)
                return null;

            var eventDef = GetEventDefinition(eventState.EventDefinitionId);
            if (eventDef == null)
                return null;

            // Mark as completed. When it was is what brings a daily or weekly event back.
            eventState.IsCompleted = true;
            eventState.PlayerWon = playerWon;
            eventState.CompletedAt = completedAt;

            // Track one-time event completion
            if (eventDef.Schedule == EventSchedule.OneTime)
            {
                career.CompletedEventIds.Add(eventDef.Id);
            }

            // Return reward only if player won
            return playerWon ? eventDef.Reward : null;
        }

        /// <summary>
        /// Takes out the events that ran out unentered, and the completed ones once they have done their job:
        /// a completed daily or weekly event is kept until its schedule would bring it back (that is what
        /// GenerateEvents looks at), a completed one-time event lives on in CompletedEventIds. Without this the
        /// save's list only ever grew.
        /// </summary>
        public int CleanupExpiredEvents(CareerState career, DateTime currentTime)
        {
            var expiredCount = career.ActiveEvents.RemoveAll(e =>
                (!e.IsCompleted && e.ExpiresAt.HasValue && e.ExpiresAt.Value < currentTime)
                || (e.IsCompleted && CompletionHasRunOut(e, currentTime)));

            return expiredCount;
        }

        private bool CompletionHasRunOut(RaceEventState state, DateTime currentTime)
        {
            // Completed before CompletedAt was recorded: nothing would ever bring it back, so it only blocks
            if (!state.CompletedAt.HasValue) return true;

            var keepFor = GetEventDefinition(state.EventDefinitionId)?.Schedule switch
            {
                EventSchedule.Daily => TimeSpan.FromHours(24),
                EventSchedule.Weekly => TimeSpan.FromDays(7),
                _ => TimeSpan.Zero
            };

            return currentTime - state.CompletedAt.Value >= keepFor;
        }
    }
}
