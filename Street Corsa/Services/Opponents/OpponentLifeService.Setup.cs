using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// The rivals' setups (<see cref="RivalSetup"/>): a few racers a day set their car up the way they race, in what its
    /// parts let them move (the racer's own setup, else their crew's, else the rules'). The street talks of it when it
    /// shows: an angle kit wound out, a car lowered, power found on the dyno.
    /// </summary>
    public partial class OpponentLifeService
    {
        /// <summary>What a setup came to for one racer, worked out on a copy of the car: the rows' keys and values, and the power</summary>
        private sealed record SetUpJob(Opponent Racer, Car Car, Car Copy, IReadOnlyDictionary<string, string> Setup)
        {
            public List<(string Key, double Value)> Wanted { get; set; } = [];
            public double? PowerBefore { get; set; }
            public double? PowerAfter { get; set; }

            /// <summary>The tuned engine on the dyno, for the car's power once the setup is on it</summary>
            public StreetCorsa.Parts.Logic.EngineReport? Tuned { get; set; }

            /// <summary>The car-wide items as the car's data held them, and those it has no place for (camber on a live axle)</summary>
            public IReadOnlyDictionary<string, SetupRange> Held { get; set; } = new Dictionary<string, SetupRange>();

            public IReadOnlySet<string> Gone { get; set; } = new HashSet<string>();
        }

        /// <summary>A few racers set their cars up; the engine's specs are tried on the dyno off the UI thread</summary>
        private async Task SetUpAsync(GameState gameState, Review review, List<string> talk)
        {
            if (!_parts.IsAvailable) return;

            var rules = Rules.RuleBook.Current.Tuning;
            var crews = CrewBook.Current;
            var jobs = gameState.Racers.ReadyToRace.Values
                .Concat(gameState.Racers.Inactive.Values.Where(r => r is Opponent { IsBoss: true }))
                .OfType<Opponent>()
                .Where(r => !HasPendingRace(gameState, r))
                .Where(r => r.Cars.FirstOrDefault() is { HasPartsAssigned: true } && _random.NextDouble() < rules.Rivals.Chance)
                .Select(r => (Racer: r, Setup: rules.Rivals.Get(rules.Rivals.Named(r.Setup, crews.CrewOf(r)?.Setup, rules.Rivals.Setup))))
                .Where(r => r.Setup != null)
                .OrderBy(_ => _random.Next())
                .Take(rules.Rivals.MaxPerDay)
                .Select(r => new SetUpJob(r.Racer, r.Racer.Cars[0], r.Racer.Cars[0].Snapshot(), r.Setup!))
                .ToList();
            if (jobs.Count == 0) return;

            var catalog = _parts.Catalog;
            var ranges = rules.Ranges();
            await Task.Run(() =>
            {
                foreach (var job in jobs)
                {
                    try
                    {
                        double? PowerOf(PartInstance engine) => EngineTuner.PowerOf(catalog, engine);

                        // As the Tune screen has it: the items the car's data has no place for left out, the rest held to it
                        var (setup, gone) = _tuneSetup?.Invoke(job.Copy)
                            ?? (CarSetup.For(job.Copy.Parts, job.Copy.Setup, catalog, ranges), new HashSet<string>());
                        (job.Held, job.Gone) = (setup.Allowed, gone);
                        var rows = CarTune.Rows(job.Copy, catalog, setup, gone);
                        var wanted = RivalSetup.Wanted(job.Copy, rows, job.Setup, PowerOf);
                        job.Wanted = wanted.Select(w => (w.Row.Key, w.Row.Snap(w.Value))).ToList();
                        if (wanted.Any(w => w.Row.Tab == TuneTab.Engine) && job.Copy.Engine is { } engine)
                        {
                            job.PowerBefore = PowerOf(engine);
                            job.Tuned = CarTune.EngineWith(job.Copy, wanted) is { } tuned ? EngineFactory.Evaluate(catalog, tuned) : null;
                            job.PowerAfter = job.Tuned is { Runs: true, Dyno: { } dyno } ? dyno.MaxPowerHp : null;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Setting up {Racer}'s car failed: {Error}", job.Racer.Name, ex.Message);
                        job.Wanted = [];
                    }
                }
            });

            foreach (var job in jobs)
            {
                // Still their car, still not held for a race with the player: the day may have moved on meanwhile
                if (job.Wanted.Count == 0 || job.Racer.Cars.FirstOrDefault() != job.Car || HasPendingRace(gameState, job.Racer)) continue;

                try
                {
                    SetUp(job, catalog, ranges, review, talk);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Setting up {Racer}'s car failed: {Error}", job.Racer.Name, ex.Message);
                }
            }
        }

        /// <summary>The setup worked out on the copy written onto the car itself, row by row as the car's rows stand now</summary>
        private void SetUp(SetUpJob job, StreetCorsa.Parts.PartsCatalog catalog, IReadOnlyDictionary<string, SetupRange> ranges,
            Review review, List<string> talk)
        {
            var car = job.Car;

            // The parts' ranges as they are now, held to what the car's data took on the worker
            var now = CarSetup.For(car.Parts, car.Setup, catalog, ranges);
            var held = now.Allowed.Where(a => !job.Gone.Contains(a.Key))
                .ToDictionary(a => a.Key, a => job.Held.GetValueOrDefault(a.Key) ?? a.Value, StringComparer.Ordinal);
            var setup = new StreetCorsa.Parts.Export.SetupChoice { Allowed = held, Values = CarSetup.Values(car.Setup, held) };
            var rows = CarTune.Rows(car, catalog, setup, job.Gone).ToDictionary(r => r.Key);
            var set = job.Wanted.Where(w => rows.ContainsKey(w.Key)).Select(w => (Row: rows[w.Key], w.Value)).ToList();
            var changed = set.Where(s => !s.Row.IsLocked && Math.Abs(s.Row.Snap(s.Value) - s.Row.Value) > 1e-9).ToList();
            if (CarTune.Apply(car, set) == 0) return;

            // The power the rivals choose and race by, whichever way the setup moved it: the dyno ran on the worker
            if (job.Tuned != null && changed.Any(c => c.Row.Tab == TuneTab.Engine)) car.PowerHp = MarketPower(job.Tuned);
            var power = job.PowerBefore is { } before && job.PowerAfter is { } after && after > before ? (before, after) : ((double, double)?)null;

            _logger.Information("{Racer} set the {Car} up: {Items}", job.Racer.Name, car.DefinitionId,
                string.Join(", ", changed.Select(c => $"{c.Row.Item} {c.Row.Value:0.###} -> {c.Row.Snap(c.Value):0.###}")));

            // One line a car, the one that shows most
            bool Up(string item) => changed.Any(c => c.Row.Item == item && c.Value > c.Row.Value);
            bool Down(string item) => changed.Any(c => c.Row.Item == item && c.Value < c.Row.Value);
            if (Up(CarSetup.SteerLock))
                talk.Add(Say(review, "street.set_up.angle_kit", job.Racer, car));
            else if (Down(CarSetup.RideHeightFront) || Down(CarSetup.RideHeightRear) || Up("drop"))
                talk.Add(Say(review, "street.set_up.lowered", job.Racer, car));
            else if (power is var (hpBefore, hpAfter) && hpAfter - hpBefore >= 1)
                talk.Add(Say(review, "street.set_up.engine", job.Racer, car, new TextValues { ["hpBefore"] = hpBefore, ["hpAfter"] = hpAfter }));
        }
    }
}
