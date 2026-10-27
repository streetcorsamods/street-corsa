using StreetCorsa.Helpers;
using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Race
{
    /// <summary>
    /// A field: the player and several rivals in one race. It is settled as a race of two with each rival, by the same
    /// rules as one (a crash, a breakdown, the police, the line or the score): each rival's record, car, reputation and
    /// what they make of the player come of their own race with the player. The player's record takes the field once:
    /// a win when they beat every rival, else a loss. A field races for cash only; an event's purse is paid by place.
    /// </summary>
    public partial class RaceResultProcessor
    {
        /// <summary>One rival of a field and the race of two the player had with them</summary>
        private sealed record Duel(FieldEntrant Rival, RaceContext Context, RaceDecision Decision);

        private List<PlayerMessage> ProcessField(RaceResultJson result, RaceContext context, GameState gameState, string saveName, DateTime startedAt,
            RaceFormat format)
        {
            var player = FieldPlayer(result.Participants, context);
            var duels = Duels(result, context, format, player);
            var outcome = FieldOutcome(result, player, duels);
            _logger.Information("Field of {Count}: the player placed {Place} ({Condition}, {Fate})",
                1 + duels.Count, outcome.PlayerPlace, outcome.WinCondition, outcome.PlayerFate?.ToString() ?? "at the line");

            var processedSession = CreateProcessedSession(result, outcome, context, startedAt, gameState.Date);
            processedSession.Field = context.Field.Select(f => f.Name).ToList();
            processedSession.PlayerPlace = outcome.PlayerPlace;
            processedSession.AheadOfPlayer = duels.Where(d => d.Decision.IsDecided && !d.Decision.PlayerWon).Select(d => d.Rival.Name).ToList();

            var messages = new List<PlayerMessage>();
            ApplyAndSave(gameState, saveName, processedSession, () =>
            {
                var groupOf = PartGroups();
                var (damage, _) = ApplyPlayerCarUpdates(gameState, player, context, groupOf);
                foreach (var duel in duels) ApplyRivalCarUpdates(gameState, duel.Decision.Opponent!, duel.Context, groupOf);
                ApplyField(gameState, outcome, duels, context, messages);

                var towed = player.Crash.Crashed && outcome.Pursuit?.PlayerBusted != true;
                if (CarReport(damage, towed) is { } report) messages.Add(report);
            });

            messages.Insert(0, FieldCard(format, outcome, context));
            _logger.Information("Race result processed successfully");
            return messages;
        }

        /// <summary>
        /// The player's entry in a field's result: the one marked as the player, else car 0, else by name, else the
        /// first (the ingestion has held the file to the race's count of racers)
        /// </summary>
        private static RaceParticipant FieldPlayer(List<RaceParticipant> participants, RaceContext context) =>
            participants.FirstOrDefault(p => p.IsPlayer == true)
            ?? participants.FirstOrDefault(p => p.IsPlayer == null && p.CarIndex == 0)
            ?? participants.FirstOrDefault(p => p.DriverName == context.PlayerName)
            ?? participants[0];

        /// <summary>
        /// The race of two the player had with each rival: the rival's entry by its car (car 1 the opponent, then the field
        /// in its order), else by name, decided by the rules of a race of two on the pair and that rival's police chase
        /// </summary>
        private List<Duel> Duels(RaceResultJson result, RaceContext context, RaceFormat format, RaceParticipant player)
        {
            var others = result.Participants.Where(p => !ReferenceEquals(p, player)).ToList();
            var duels = new List<Duel>();
            var index = 0;
            foreach (var rival in context.Rivals())
            {
                index++;
                var carIndex = index;
                var entry = others.FirstOrDefault(p => p.CarIndex == carIndex && p.IsPlayer != true)
                    ?? others.FirstOrDefault(p => p.DriverName == rival.Name)
                    ?? others.ElementAtOrDefault(index - 1);
                if (entry == null)
                {
                    _logger.Warning("The field's result has no entry for {Rival} (car {Index}): left out", rival.Name, carIndex);
                    continue;
                }

                var pair = new RaceResultJson
                {
                    Metadata = result.Metadata,
                    Session = result.Session,
                    Participants = [player, entry],
                    Pursuit = result.Pursuit?.For(carIndex)
                };
                var duelContext = context.Against(rival);
                duels.Add(new Duel(rival, duelContext, DetermineOutcome(pair, duelContext, format)));
            }

            return duels;
        }

        /// <summary>
        /// The field as the player's race: the places as the mode gave them, the player's own, a win when every rival
        /// was beaten. A false start is no contest for anyone; a field where no rival's race was decided is not decided.
        /// </summary>
        private static RaceDecision FieldOutcome(RaceResultJson result, RaceParticipant player, List<Duel> duels)
        {
            var places = result.Participants
                .OrderBy(p => p.Performance.FinalPosition ?? int.MaxValue)
                .ThenBy(p => p.CarIndex ?? int.MaxValue)
                .ToList();
            var outcome = new RaceDecision
            {
                Player = player,
                Pursuit = result.Pursuit,
                Places = places,
                PlayerPlace = Math.Clamp(player.Performance.FinalPosition ?? places.IndexOf(player) + 1, 1, places.Count)
            };

            var decided = duels.Where(d => d.Decision.IsDecided).ToList();
            if (duels.Count > 0 && duels.All(d => d.Decision.WinCondition == WinCondition.FalseStart))
            {
                outcome.WinCondition = WinCondition.FalseStart;
                outcome.PlayerFate = WinCondition.FalseStart;
                return outcome;
            }

            if (decided.Count == 0)
            {
                // Nobody's race with the player came to anything: as a race of two would say it, the first rival's way
                outcome.WinCondition = duels.FirstOrDefault()?.Decision.WinCondition ?? WinCondition.Inconclusive;
                outcome.PlayerFate = outcome.WinCondition;
                return outcome;
            }

            outcome.WinCondition = WinCondition.FieldFinish;
            outcome.PlayerWon = duels.All(d => d.Decision.IsDecided && d.Decision.PlayerWon);

            // The player's place holds to their own races: first only when every rival was beaten, else behind every
            // rival who beat them (a player out of it, put back or wrecked, is not paid the place a lap time or the pit
            // lane gave them in the mode's order). The card shows them where they placed.
            var lost = duels.Count(d => d.Decision.IsDecided && !d.Decision.PlayerWon);
            outcome.PlayerPlace = outcome.PlayerWon ? 1 : Math.Clamp(Math.Max(outcome.PlayerPlace ?? 1, 1 + lost), 2, places.Count);
            places.Remove(player);
            places.Insert(Math.Min(outcome.PlayerPlace.Value - 1, places.Count), player);

            // Who the player's race is against in the record: the winner when it wasn't them, the runner-up when it was
            outcome.Opponent = places.FirstOrDefault(p => !ReferenceEquals(p, player));

            // The player out of it the same way against everyone (a breakdown, the police, put back): what they are told
            var fates = decided.Select(d => d.Decision.WinCondition).Distinct().ToList();
            if (fates.Count == 1 && fates[0] is WinCondition.PlayerCrashed or WinCondition.PlayerBrokeDown or WinCondition.PlayerBusted
                    or WinCondition.PlayerAbandoned or WinCondition.PlayerDisqualified)
                outcome.PlayerFate = fates[0];
            return outcome;
        }

        /// <summary>Everything a settled field changes but the cars' wear: the records, the police, the career, the purse, the paper</summary>
        private void ApplyField(GameState gameState, RaceDecision outcome, List<Duel> duels, RaceContext context, List<PlayerMessage> messages)
        {
            // The paper's story is the player's race with the winner, or with the first rival when the player won
            var winner = outcome.Opponent?.DriverName;
            var story = (outcome.PlayerWon ? null : duels.FirstOrDefault(d => d.Rival.Name == winner)) ?? duels.FirstOrDefault();
            var before = new StandingBefore(
                gameState.Player.Stats.Reputation,
                story == null ? 0 : FindRacer(gameState, story.Rival.Name)?.Stats.Reputation ?? 0,
                gameState.Player.Stats.Wins == 0);

            // The records: the player's once, each rival's of their own race with the player
            if (outcome.IsDecided)
            {
                ApplyPlayerStats(gameState, outcome, context);
                gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == context.PlayerCarInstanceId)?.History.RecordRace(outcome.PlayerWon, pinkSlip: false);
            }
            foreach (var duel in duels)
            {
                ApplyRivalStats(gameState, duel.Decision, duel.Context);
                RecordRivalCarHistory(gameState, duel.Decision, duel.Context);
            }

            // The police: the player once, each rival they caught
            if (outcome.Pursuit is { Started: true } pursuit)
            {
                var lines = new List<string>();
                PlayerPursuit(gameState, outcome, context, pursuit, lines);
                foreach (var duel in duels)
                {
                    if (duel.Decision.Pursuit is { } theirs) RivalPursuit(gameState, duel.Decision, duel.Context, theirs, lines, inField: true);
                }
                PlayerEscape(gameState, pursuit, lines);
                PursuitMessage(pursuit, lines, messages);
            }

            ApplyReputationUpdates(gameState, outcome, context);
            foreach (var duel in duels) ApplyRivalReputation(gameState, duel.Decision, duel.Context);

            // What the player hears of their own race, said against the winner
            if (outcome.PlayerFate is { } fate)
                TellHowItWent(gameState, fate, outcome, story?.Context ?? context, messages);

            // The career: a win of the field counts as a win; each rival beaten is beaten, the boss only if he finished behind
            UpdateMilestoneCounters(gameState, outcome, context, recordDefeated: false);
            foreach (var duel in duels.Where(d => d.Decision.IsDecided && d.Decision.PlayerWon)) RecordDefeated(gameState, duel.Context);

            if (context.EventId != null)
                ProcessFieldEvent(gameState, outcome, context, messages);

            var progress = _careerProgressService.CheckProgressAfterRace(gameState);
            messages.AddRange(progress.PlayerMessages);

            foreach (var duel in duels) UpdateOpponentStatus(gameState, duel.Context);

            if (story != null) WriteUp(gameState, story.Decision, story.Context, new StakesMoved(false, false), default, before);

            if (gameState.PendingRace == null || gameState.PendingRace.ContextId == context.ContextId)
                gameState.PendingRace = null;
        }

        /// <summary>
        /// A field's event: completed, the player's place kept, and the purse paid by place (<see cref="EventReward.ForPlace"/>):
        /// the pot is the event's prize, as this game's difficulty pays it, and every car's entry. The special item is the
        /// winner's. A race that was no contest pays nothing.
        /// </summary>
        private void ProcessFieldEvent(GameState gameState, RaceDecision outcome, RaceContext context, List<PlayerMessage> messages)
        {
            // Only an event still open pays: one gone or done while AC ran is nothing to race for, as for a race of two
            var instance = context.EventInstanceId ?? Guid.Empty;
            var state = gameState.Career.ActiveEvents.FirstOrDefault(e => e.EventDefinitionId == context.EventId && !e.IsCompleted
                && (instance == Guid.Empty || e.InstanceId == instance));
            if (state == null)
            {
                _logger.Warning("Could not find active event {EventId} for completion", context.EventId);
                return;
            }

            var definition = _raceEventService.GetEventDefinition(context.EventId!);
            _raceEventService.CompleteEvent(instance, outcome.PlayerWon, gameState.Career, gameState.Date);
            if (definition == null || !state.IsCompleted) return;
            state.PlayerPlace = outcome.IsDecided ? outcome.PlayerPlace : null;

            // The entry is paid whatever came of it, the race was run: it went into the pot
            if (definition.Entry > 0)
            {
                gameState.Player.Money -= definition.Entry;
                gameState.Player.Stats.TotalLosses += definition.Entry;
                _logger.Information("Event {EventId}: the player's ${Entry} entry", context.EventId, definition.Entry);
            }
            if (!outcome.IsDecided || outcome.PlayerPlace is not { } place) return;

            var prize = definition.Reward.ScaledBy(gameState.Rules.RacePrizeMultiplier);
            var pot = prize.Cash + definition.Entry * (1 + context.RivalCount);
            var reward = prize.ForPlace(place, pot);
            _logger.Information("Event {EventId}: place {Place} of {Count} takes ${Cash} of ${Pot}, {Reputation} reputation",
                context.EventId, place, 1 + context.RivalCount, reward.Cash, pot, reward.Reputation);

            if (reward.Cash > 0)
            {
                gameState.Player.Money += reward.Cash;
                gameState.Player.Stats.TotalEarnings += reward.Cash;
                gameState.Career.IncrementCounter(Models.Career.Milestones.MilestoneTrigger.MoneyEarned, (int)reward.Cash);
            }
            if (reward.Reputation != 0)
                gameState.Player.Stats.EventReputationBonus = Math.Max(0, gameState.Player.Stats.EventReputationBonus + reward.Reputation);
            if (place == 1 && !string.IsNullOrEmpty(prize.SpecialItem))
            {
                reward.SpecialItem = prize.SpecialItem;
                reward.SpecialItemDescription = GiveSpecialItem(gameState, context, prize.SpecialItem);
            }

            if (place == 1)
            {
                if (EventRewardMessage(reward) is { } won) messages.Add(won);
            }
            else if (reward.Cash > 0 || reward.Reputation != 0)
            {
                var lines = new List<string>();
                if (reward.Cash > 0) lines.Add(GameText.Say("event.reward.cash", ("cash", reward.Cash)));
                if (reward.Reputation != 0)
                    lines.Add(GameText.Say(reward.Reputation > 0 ? "event.reward.reputation" : "event.reward.reputation_lost", ("reputation", reward.Reputation)));
                messages.Add(new PlayerMessage(GameText.Say("event.placed.title"),
                    GameText.Say("event.placed", ("place", Ordinal(place)), ("rewards", string.Join("\n", lines.Select(r => GameText.Say("event.won.reward", ("reward", r))))))));
            }
        }

        /// <summary>
        /// The field's result card: the player's place, then every racer in the order they placed (a score where the
        /// format is won on one, out of it where they were: wrecked, broken down, disqualified, gone early or busted)
        /// </summary>
        private static PlayerMessage FieldCard(RaceFormat format, RaceDecision outcome, RaceContext context)
        {
            string Show(double? score) => score is not { } value ? GameText.Say("result.score.none")
                : format.ScoreIsTime ? LapTime(value)
                : value.ToString("N0", Locale.Culture);

            var lines = new List<string>();
            for (var i = 0; i < outcome.Places.Count; i++)
            {
                var racer = outcome.Places[i];
                var name = ReferenceEquals(racer, outcome.Player) ? context.PlayerName : racer.DriverName;
                var key = Out(racer) ? "result.field.line.out" : format.ByScore ? "result.field.line.score" : "result.field.line";
                lines.Add(GameText.Say(key, ("place", i + 1), ("name", name), ("score", Show(racer.Score))));
            }

            var count = outcome.Places.Count;
            var title = !outcome.IsDecided ? GameText.Say("result.field.no_contest.title")
                : outcome.PlayerWon ? GameText.Say("result.field.won.title")
                : GameText.Say("result.field.title", ("place", Ordinal(outcome.PlayerPlace ?? count)), ("count", count));
            return new PlayerMessage(title, string.Join("\n", lines));

            bool Out(RaceParticipant p) => p.Crash.Crashed || p.BrokeDown == true || p.Disqualified == true || p.FalseStart == true
                || (ReferenceEquals(p, outcome.Player) ? outcome.Pursuit?.PlayerBusted == true
                    : p.CarIndex is { } car && outcome.Pursuit?.For(car).RivalBusted == true);
        }

        /// <summary>A place as the text says it: 1st, 2nd, 3rd (the text's <c>place.&lt;n&gt;</c>, else <c>place.other</c>)</summary>
        private static string Ordinal(int place) =>
            GameText.Installed.Has($"place.{place}") ? GameText.Say($"place.{place}") : GameText.Say("place.other", ("place", place));
    }
}
