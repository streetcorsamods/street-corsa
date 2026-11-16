using StreetCorsa.Helpers;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Race
{
    /// <summary>
    /// What the player is told after a race, put into words: the bracket's line, the score card, a lap time, the car's
    /// report, an event's prize and a forfeit (the race itself is applied in RaceResultProcessor.cs)
    /// </summary>
    public partial class RaceResultProcessor
    {
        /// <summary>The bracket race in a line under the slip: who won and why</summary>
        private static string? BracketNote(RaceDecision outcome, RaceContext context) => outcome.WinCondition switch
        {
            WinCondition.BracketFinish => outcome.PlayerWon ? GameText.Say("result.bracket.won")
                : GameText.Say("result.bracket.lost", ("name", context.OpponentName)),
            WinCondition.PlayerBrokeOut => GameText.Say("result.bracket.broke_out"),
            WinCondition.OpponentBrokeOut => GameText.Say("result.bracket.rival_broke_out", ("name", context.OpponentName)),
            _ => null
        };

        private static string? Join(string? first, string second) => first == null ? second : first + "\n" + second;

        /// <summary>
        /// The racers' scores after a format won on score: each one's score (a time shows as one), and who won on it.
        /// The format's name is the title.
        /// </summary>
        private static PlayerMessage ScoreCard(RaceFormat format, RaceDecision outcome, RaceContext context)
        {
            string Show(double? score) => score is not { } value ? GameText.Say("result.score.none")
                : format.ScoreIsTime ? LapTime(value)
                : value.ToString("N0", Locale.Culture);

            var lines = new List<string>
            {
                GameText.Say("result.score.line", ("name", context.PlayerName), ("score", Show(outcome.Player?.Score))),
                GameText.Say("result.score.line", ("name", context.OpponentName), ("score", Show(outcome.Opponent?.Score)))
            };
            if (outcome.Player?.LapTimesMs is { Count: > 0 } laps)
                lines.Add(GameText.Say("result.score.laps", ("laps", string.Join("  ", laps.Select(LapTime)))));
            // The format's own words when its mod has them (result.score.won.time_attack), else the plain ones
            var formats = RaceFormats.Current;
            if (outcome.WinCondition == WinCondition.ScoreFinish)
                lines.Add(outcome.PlayerWon ? formats.SayFor(format, "result.score.won")
                    : formats.SayFor(format, "result.score.lost", ("name", context.OpponentName)));

            return new PlayerMessage(formats.NameOf(format), string.Join("\n", lines));
        }

        /// <summary>
        /// A run alone for the score, on its own card under the format's name: the score, the time, the distance and the
        /// average, the crashes, and where it went on the road's table; a run with no score says it never reached the line
        /// </summary>
        /// <param name="field">Everyone on the road, when the player had rivals there (a Challenge road's, experimental):
        /// ranked by score under the run, the player among them</param>
        private static PlayerMessage SoloCard(RaceFormat format, Models.GameState.ScoredRun? run, int? place, IReadOnlyList<RaceParticipant>? field = null)
        {
            var formats = RaceFormats.Current;
            var ranked = field == null ? null : FieldScores(format, field);
            if (run == null)
                return new PlayerMessage(formats.NameOf(format), formats.SayFor(format, "result.solo.no_score") + (ranked == null ? "" : "\n\n" + ranked));

            var score = format.ScoreIsTime ? LapTime(run.Score) : run.Score.ToString("N0", Locale.Culture);
            var lines = new List<string>
            {
                formats.SayFor(format, "result.solo.score", ("score", score)),
                GameText.Say("result.solo.run", ("time", LapTime(run.TimeMs)), ("distance", Measure.Distance(run.DistanceKm)),
                    ("speed", Measure.Speed(run.AverageKmh))),
                run.Clean ? formats.SayFor(format, "result.solo.clean") : formats.SayFor(format, "result.solo.crashes", ("crashes", run.Crashes))
            };
            if (place == 1) lines.Add(formats.SayFor(format, "result.solo.best"));
            else if (place is { } onTable) lines.Add(GameText.Say("result.solo.place", ("place", onTable)));
            if (ranked != null) lines.Add("\n" + ranked);

            return new PlayerMessage(formats.NameOf(format), string.Join("\n", lines));
        }

        /// <summary>The cars on the road ranked by score, best first (a car with none last), the player's line marked</summary>
        private static string FieldScores(RaceFormat format, IReadOnlyList<RaceParticipant> field)
        {
            var ranked = (format.HigherWins ? field.OrderByDescending(p => p.Score ?? double.MinValue) : field.OrderBy(p => p.Score ?? double.MaxValue)).ToList();
            var lines = new List<string> { GameText.Say("result.solo.field") };
            for (var i = 0; i < ranked.Count; i++)
            {
                var car = ranked[i];
                var score = car.Score is not { } value ? GameText.Say("result.score.none")
                    : format.ScoreIsTime ? LapTime(value) : value.ToString("N0", Locale.Culture);
                lines.Add(GameText.Say(car.IsPlayer == true ? "result.solo.field.you" : "result.solo.field.line",
                    ("place", i + 1), ("name", car.DriverName), ("score", score)));
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// A Challenge road's run, on a card under the Challenge's name: the road it was, the medal it won (or what bronze
        /// asked), the next road or the go's total, the banked score, and what opened (a tier's cars, Challenges)
        /// </summary>
        private static PlayerMessage ChallengeCard(Career.ChallengeDefinition challenge, Career.ChallengeRunOutcome outcome,
            Career.ChallengeMedals? medals, IReadOnlyList<Career.ChallengeDefinition> opened, IReadOnlyList<Career.ShowroomCar> newCars)
        {
            static string Points(double score) => score.ToString("N0", Locale.Culture);
            var lines = new List<string>
            {
                GameText.Say("result.challenge.road", ("road", outcome.Road + 1), ("roads", challenge.Roads.Count))
            };
            lines.Add(outcome.Medal switch
            {
                Models.GameState.Medal.Gold => GameText.Say("result.challenge.gold"),
                Models.GameState.Medal.Silver => GameText.Say("result.challenge.silver", ("gold", Points(medals?.Gold ?? 0))),
                Models.GameState.Medal.Bronze => GameText.Say("result.challenge.bronze", ("silver", Points(medals?.Silver ?? 0))),
                _ => medals == null ? GameText.Say("result.challenge.no_medals") : GameText.Say("result.challenge.no_medal", ("bronze", Points(medals.Bronze)))
            });

            if (outcome.Total is { } total)
            {
                lines.Add(GameText.Say("result.challenge.done", ("total", Points(total))));
                if (outcome.NewBestTotal && !outcome.FirstFinish) lines.Add(GameText.Say("result.challenge.best_total"));
            }
            else
            {
                lines.Add(GameText.Say("result.challenge.next", ("road", outcome.Road + 2), ("roads", challenge.Roads.Count)));
            }

            if (outcome.ScoreAfter > outcome.ScoreBefore)
                lines.Add(GameText.Say("result.challenge.banked", ("score", Points(outcome.ScoreAfter)), ("gained", Points(outcome.ScoreAfter - outcome.ScoreBefore))));
            if (outcome.TierAfter > outcome.TierBefore)
            {
                lines.Add(GameText.Say("result.challenge.tier", ("tier", outcome.TierAfter)));
                if (newCars.Count > 0)
                {
                    const int named = 6;
                    var names = newCars.Take(named).Select(c => c.Definition.Name).ToList();
                    if (newCars.Count > named) names.Add(GameText.Say("result.challenge.more", ("count", newCars.Count - named)));
                    lines.Add(GameText.Say("result.challenge.new_cars", ("cars", string.Join(", ", names))));
                }
            }

            if (opened.Count > 0)
                lines.Add(GameText.Say("result.challenge.opened", ("challenges", string.Join(", ", opened.Select(c => c.Name)))));

            return new PlayerMessage(challenge.Name, string.Join("\n", lines));
        }

        /// <summary>A lap time in milliseconds as the stopwatch shows it: 1:23.456 (one that is no number as 0:00.000)</summary>
        public static string LapTime(double milliseconds)
        {
            // The validator holds the times to a day; a score shown as a time is held only to its largest score
            var shown = double.IsFinite(milliseconds) ? Math.Clamp(Math.Round(milliseconds), 0, Validation.RaceResultValidator.MaxScore) : 0;
            var total = TimeSpan.FromMilliseconds(shown);
            var decimals = Locale.Culture.NumberFormat.NumberDecimalSeparator;
            return $"{(int)total.TotalMinutes}:{total.Seconds:00}{decimals}{total.Milliseconds:000}";
        }

        /// <summary>
        /// What the player hears about their car after the session: towed home when it crashed (the player goes with
        /// it to see what the crash did), else the damage report, or the fuel and oil alone when nothing was damaged; null
        /// when there is nothing to tell
        /// </summary>
        private static PlayerMessage? CarReport(IReadOnlyList<ReportLine> lines, bool towed)
        {
            var damage = lines.Where(l => l.IsDamage).Select(l => l.Text).ToList();
            var notes = lines.Where(l => !l.IsDamage).ToList();
            var after = notes.Count == 0 ? "" : "\n\n" + string.Join("\n", notes);
            if (towed)
            {
                return new PlayerMessage(GameText.Say("result.towed.title"), (damage.Count == 0
                    ? GameText.Say("result.towed")
                    : GameText.Say("result.towed.damage", ("damage", string.Join("\n", damage)))) + after)
                {
                    TowedToGarage = true
                };
            }

            if (damage.Count > 0) return new PlayerMessage(GameText.Say("result.damage.title"), string.Join("\n", damage) + after);
            return notes.Count > 0 ? new PlayerMessage(GameText.Say(notes.All(n => n.Kind == ReportKind.Oil) ? "result.oil.title"
                : notes.Any(n => n.Kind == ReportKind.Oil) ? "result.fuel_and_oil.title" : "result.fuel.title"), string.Join("\n", notes)) : null;
        }

        /// <summary>
        /// The message telling the player what an event paid; null when it paid nothing to speak of
        /// </summary>
        private static PlayerMessage? EventRewardMessage(Models.Career.Events.EventReward reward)
        {
            var rewardParts = new List<string>();

            if (reward.Cash > 0)
                rewardParts.Add(GameText.Say("event.reward.cash", ("cash", reward.Cash)));

            if (reward.Reputation > 0)
                rewardParts.Add(GameText.Say("event.reward.reputation", ("reputation", reward.Reputation)));

            if (!string.IsNullOrEmpty(reward.SpecialItemDescription))
                rewardParts.Add(reward.SpecialItemDescription);
            else if (!string.IsNullOrEmpty(reward.SpecialItem))
                rewardParts.Add(GameText.Say("event.reward.special_item", ("item", reward.SpecialItem)));

            if (rewardParts.Count == 0)
                return null; // No rewards to show

            var message = GameText.Say("event.won", ("rewards", string.Join("\n", rewardParts.Select(r => GameText.Say("event.won.reward", ("reward", r))))));
            return new PlayerMessage(GameText.Say("event.won.title"), message);
        }

        /// <summary>
        /// What the player is told when a race with stakes came back without a result: only what actually changed
        /// hands (an event-only opponent has no purse or garage to take it)
        /// </summary>
        private static PlayerMessage ForfeitMessage(RaceContext context, StakesMoved moved)
        {
            var stakes = new List<string>();
            if (moved.Cash)
                stakes.Add(GameText.Say("result.forfeit.cash", ("wager", context.CashWager), ("name", context.OpponentName)));
            if (moved.Car)
                stakes.Add(GameText.Say("result.forfeit.pink_slip", ("name", context.OpponentName)));

            if (stakes.Count == 0)
                return new PlayerMessage(GameText.Say("result.forfeit.title"), GameText.Say("result.forfeit"));

            return new PlayerMessage(
                GameText.Say("result.forfeit.title"),
                GameText.Say("result.forfeit.stakes", ("stakes", string.Join("\n", stakes.Select(s => GameText.Say("result.forfeit.stake", ("stake", s)))))));
        }
    }
}
