using System.Globalization;
using System.Text;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa;

/// <summary>The reports on the builds: the rated table, the snapshot, the model's inputs, one build in detail</summary>
public static partial class Program
{
    private static int Table(PartsCatalog catalog, IEnumerable<EngineBuild> builds)
    {
        Console.WriteLine($"{"build",-48} {"rated",6} {"model",6} {"error",6}  {"@rpm",5} {"Nm",5} {"@rpm",5} {"litres",6} {"CR",5}  problem");
        var errors = new List<double>();
        // Rated builds that do not run, or run far off their rating (unrated ones that do not run are known and listed only)
        var failures = new List<string>();
        foreach (var build in builds)
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build);
            if (tree == null)
            {
                Console.WriteLine($"{Short(build.Id),-48} no engine block among its parts");
                if (build.RatedPower != null) failures.Add($"{build.Id}: no engine block among its parts");
                continue;
            }

            var report = EngineEvaluator.Evaluate(catalog, tree);
            var dyno = report.Dyno;
            var error = build.RatedPower is { } rated && dyno != null ? (dyno.MaxPowerHp - rated) / rated : (double?)null;
            if (error != null && report.Problem == null) errors.Add(error.Value);
            if (build.RatedPower != null)
            {
                if (report.Problem != null || dyno == null) failures.Add($"{build.Id}: {report.Problem ?? "no dyno run"}");
                else if (!build.RunsToItsRating(dyno.MaxPowerHp)) failures.Add($"{build.Id}: {error:+0%;-0%} off its rated {build.RatedPower:0} hp");
            }

            Console.WriteLine($"{Short(build.Id),-48} {build.RatedPower,6:0} {dyno?.MaxPowerHp,6:0} {error,6:+0%;-0%}  {dyno?.MaxPowerRpm,5:0} " +
                              $"{dyno?.MaxTorque,5:0} {dyno?.MaxTorqueRpm,5:0} {dyno?.Displacement * 1000,6:0.00} {dyno?.Compression,5:0.0}  " +
                              $"{report.Problem}{(tree.Unplaced.Count > 0 ? $" [{tree.Unplaced.Count} unplaced]" : "")}");
        }

        if (errors.Count > 0)
            Console.WriteLine($"\n{errors.Count} rated builds that run: mean error {errors.Average():+0.0%;-0.0%}, " +
                              $"mean absolute {errors.Average(Math.Abs):0.0%}, worst {errors.MaxBy(Math.Abs):+0.0%;-0.0%}");

        if (failures.Count == 0) return 0;

        Console.WriteLine($"\n{failures.Count} rated build(s) do not run or are more than {EngineBuild.MaxRatedError:0%} off:");
        foreach (var failure in failures) Console.WriteLine("  " + failure);
        return 1;
    }

    /// <summary>
    /// Every build's whole report, one JSON object a line, to compare the part logic of two versions of the game by:
    /// what feeds the dyno, the curve, and everything the car's data is given. Written whole or not at all: a run that
    /// stops half way leaves the last snapshot, never one the comparison would read as complete
    /// </summary>
    private static int Snapshot(PartsCatalog catalog, string file)
    {
        var lines = new StringBuilder();
        foreach (var build in catalog.EngineBuilds.OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build);
            var report = tree == null ? null : EngineEvaluator.Evaluate(catalog, tree);
            var dyno = report?.Dyno;
            lines.AppendLine(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                build.Id,
                report?.Problem,
                report?.Inputs,
                report?.IdleRpm,
                report?.LimiterRpm,
                report?.RedlineRpm,
                report?.Inertia,
                report?.GearRatios,
                report?.ReverseRatio,
                report?.FinalRatio,
                report?.DriveType,
                report?.DiffLock,
                report?.Friction,
                report?.ClutchCapacity,
                report?.Turbocharged,
                report?.Mass,
                Hp = dyno?.MaxPowerHp,
                HpRpm = dyno?.MaxPowerRpm,
                Nm = dyno?.MaxTorque,
                NmRpm = dyno?.MaxTorqueRpm,
                dyno?.Displacement,
                dyno?.Compression,
                Curve = dyno?.Curve.Select(p => new[] { p.Rpm, Math.Round(p.Torque, 3) }),
                Unplaced = tree?.Unplaced.Select(p => p.Id)
            }));
        }

        Helpers.SafeFile.WriteAllText(file, lines.ToString());
        return 0;
    }

    /// <summary>What the parts' figures feed the engine model, side by side</summary>
    private static int Inputs(PartsCatalog catalog, IEnumerable<EngineBuild> builds)
    {
        // BENCH_JSON=<file>: one JSON object per build, for fitting the engine model outside. A run's file holds that
        // run's builds only (an earlier run's lines would be fitted twice), written whole at the end of the run
        var jsonFile = Environment.GetEnvironmentVariable("BENCH_JSON");
        var json = new StringBuilder();

        Console.WriteLine($"{"build",-30} {"hp",4} {"inMax",6} {"inMin",6} {"exMax",6} {"inDur",5} {"exDur",5} {"inOpn",5} {"exOpn",5} {"lead0",5} {"lead1",5} {"burn ms",7} {"Tloss",5} {"A/F",5} {"H MJ",5} {"air",6} {"fuel",6} {"maxRpm",6} {"float",6} {"cut",6} {"boost",5}");
        foreach (var build in builds)
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build);
            var i = tree == null ? null : EngineEvaluator.Evaluate(catalog, tree).Inputs;
            if (i == null) continue;

            if (!string.IsNullOrEmpty(jsonFile))
                json.Append(Newtonsoft.Json.JsonConvert.SerializeObject(new { build.Id, build.RatedPower, Inputs = i }) + Environment.NewLine);

            // A speed no part gives is none, not -1
            static string Rpm(double rpm) => rpm >= EngineDyno.LeastSpeed ? rpm.ToString("0", CultureInfo.InvariantCulture) : "-";
            double Span(double open, double close) => (close - open < 0 ? close - open + 1 : close - open) * 720;
            Console.WriteLine($"{Short(build.Id)[^Math.Min(30, Short(build.Id).Length)..],-30} {build.RatedPower,4:0} {i.IntakeMax,6:0.000} {i.IntakeMin,6:0.000} {i.ExhaustMax,6:0.000} " +
                              $"{Span(i.IntakeOpen, i.IntakeClose),5:0} {Span(i.ExhaustOpen, i.ExhaustClose),5:0} {i.IntakeOpen * 720,5:0} {i.ExhaustOpen * 720,5:0} " +
                              $"{(0.5 - i.SparkMin) * 720,5:0} {(0.5 - i.SparkMin - i.SparkInc) * 720,5:0} {i.BurnTime * 1000,7:0.000} {i.HeatLoss,5:0} {i.MixtureRatio,5:0.0} " +
                              $"{i.MixtureHeat / 1e6,5:0.00} {i.MaxAirFlow,6:0.000} {i.MaxFuelFlow,6:0.000} {Rpm(i.MaxRpm),6} {Rpm(i.FloatRpm),6} {Rpm(i.RpmLimit),6} {i.BoostMax,5:0.00}");
        }

        if (!string.IsNullOrEmpty(jsonFile)) Helpers.SafeFile.WriteAllText(jsonFile, json.ToString());
        return 0;
    }

    private static int Show(PartsCatalog catalog, string id)
    {
        var build = FindBuild(catalog.EngineBuilds, b => b.Id, id);
        if (build == null) return 1;

        var tree = PartTreeBuilder.BuildEngine(catalog, build);
        if (tree == null)
        {
            Console.WriteLine("No engine block among its parts");
            return 1;
        }

        Console.WriteLine($"\n{build.Name}  ({build.Id})");
        Print(tree.Root, 0);
        foreach (var part in tree.Unplaced) Console.WriteLine($"  UNPLACED {part.Id}");
        foreach (var missing in tree.Missing) Console.WriteLine($"  MISSING  {missing}");

        var report = EngineEvaluator.Evaluate(catalog, tree);
        Console.WriteLine($"\nRuns: {report.Runs}{(report.Problem != null ? " - " + report.Problem : "")}");
        Console.WriteLine($"Inputs: {report.Inputs}");
        Console.WriteLine($"Idle {report.IdleRpm:0} rpm, limiter {report.LimiterRpm:0} rpm, redline {report.RedlineRpm:0} rpm, inertia {report.Inertia:0.000} kg m2, " +
                          $"mass {report.Mass:0} kg, value ${tree.Root.SelfAndDescendants().Sum(p => PartPricing.NewPrice(p.Definition)):0}, friction {report.Friction:0.000000}, clutch {report.ClutchCapacity:0}{(report.Turbocharged ? ", turbo" : "")}");
        Console.WriteLine($"Gears {string.Join(" / ", report.GearRatios.Select(r => r.ToString("0.00")))}  reverse {report.ReverseRatio:0.00}  " +
                          $"final {report.FinalRatio:0.00}  drive {report.DriveType}  lock {report.DiffLock:0.00}");

        if (report.Dyno is { } dyno)
        {
            Console.WriteLine($"{dyno.Displacement * 1000:0.00} l, {dyno.Compression:0.0}:1, {dyno.MaxPowerHp:0} hp @ {dyno.MaxPowerRpm:0}, " +
                              $"{dyno.MaxTorque:0} Nm @ {dyno.MaxTorqueRpm:0}{(build.RatedPower != null ? $"  (rated {build.RatedPower:0} hp)" : "")}");
            foreach (var (rpm, torque) in dyno.Curve.Where(p => p.Rpm % 500 == 0))
                Console.WriteLine($"  {rpm,6:0} rpm {torque,6:0} Nm {dyno.PowerHpAt(rpm),6:0} hp  {new string('#', (int)(torque / 12))}");
        }

        return 0;
    }

    private static void Print(InstalledPart part, int depth)
    {
        Console.WriteLine($"  {new string(' ', depth * 2)}{(depth == 0 ? "" : $"[{part.ParentSlot}] ")}{part}");
        foreach (var child in part.Children.OrderBy(c => c.Key)) Print(child.Value, depth + 1);
    }

    private static string Short(string id) => id.Length > 48 ? id[^48..] : id;

    /// <summary>
    /// The build a command names, the same way for every command: the build of exactly that id, else the one build
    /// whose id contains it. Several that contain it is an error listing them: a short id must not bench one build
    /// and show another. Null (with the reason printed) when there is none or no single one.
    /// </summary>
    /// <param name="what">Said of the candidates when none matches, e.g. " that runs"</param>
    private static T? FindBuild<T>(IReadOnlyCollection<T> candidates, Func<T, string> idOf, string id, string what = "") where T : class
    {
        var exact = candidates.FirstOrDefault(c => idOf(c).Equals(id, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var matches = candidates.Where(c => idOf(c).Contains(id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) return matches[0];

        if (matches.Count == 0) Console.WriteLine($"No build '{id}'{what}");
        else
        {
            Console.WriteLine($"'{id}' names {matches.Count} builds{what}; give more of the id:");
            foreach (var match in matches.Take(12)) Console.WriteLine($"  {idOf(match)}");
            if (matches.Count > 12) Console.WriteLine("  ...");
        }

        return null;
    }
}
