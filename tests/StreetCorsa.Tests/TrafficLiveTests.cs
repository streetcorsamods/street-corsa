using System.Diagnostics;
using System.Text.Json;
using StreetCorsa.Services.Race.Traffic;
using StreetCorsa.Services.Race.Traffic.Conversion;

namespace StreetCorsa.Tests;

/// <summary>
/// The traffic port against a real install, run by hand only (<c>--filter-class *TrafficLiveTests</c>, explicit):
/// SC_AC_ROOT names the AC folder, SC_TRAFFIC_OUT a folder to write into (nothing is written anywhere else). Plans
/// built from real AI lines are set beside the ones Race Explorer's script made for the same roads, and real cars are
/// converted, each's time, size and notes printed.
/// </summary>
public sealed class TrafficLiveTests
{
    /// <summary>Lines into SC_TRAFFIC_OUT\live.log (the runner keeps a passing test's output to itself)</summary>
    private sealed class Log
    {
        public void WriteLine(string line)
        {
            Directory.CreateDirectory(Out);
            File.AppendAllText(Path.Combine(Out, "live.log"), line + Environment.NewLine);
        }
    }

    private readonly Log output = new();

    private static string? AcRoot => Environment.GetEnvironmentVariable("SC_AC_ROOT");
    private static string Out => Environment.GetEnvironmentVariable("SC_TRAFFIC_OUT") ?? Path.Combine(Path.GetTempPath(), "sc_traffic_live");

    [Fact(Explicit = true)]
    public void Plans_built_here_lie_where_the_scripts_did()
    {
        Assert.NotNull(AcRoot);
        var roads = new (string Track, string? Layout, bool KeepLeft)[]
        {
            ("rt_bannochbrae", "normal", true), ("ks_highlands", "layout_long", true), ("evo_triangle", "anticlockwise", true),
            ("tripoli", null, false), ("mecheria", null, false), ("Ain_Diab", null, false),
            ("pk_tail_of_the_dragon", "pk_tail_of_the_dragon_2way", false), ("transfagarasan", "hillclimb_race", false),
            ("amalfidrive", "costaditalia_circuit", false), ("rt_misty_loch", "normal", true), ("rt_bannochbrae", "ry_normal", true), ("rt_bannochbrae", "ry_reverse", true)
        };
        foreach (var (track, layout, keepLeft) in roads)
        {
            var folder = Path.Combine(AcRoot!, "content", "tracks", track);
            var theirs = TrafficRoad.PlanOf(folder, layout);
            var ai = AiLine.FileOf(folder, layout);
            if (ai == null)
            {
                output.WriteLine($"{track}/{layout}: not installed");
                continue;
            }
            if (theirs == null)
            {
                var alone = TrafficPlanBuilder.Build(AiLine.Read(ai), keepLeft, TrafficPlanBuilder.Edges.Of(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ai)!)!, "data")));
                output.WriteLine($"{track}/{layout} (keep {(keepLeft ? "left" : "right")}): ours {alone.Lanes.Count} lane(s), {alone.Why}; {string.Join("; ", alone.Problems)}");
                continue;
            }

            var watch = Stopwatch.StartNew();
            var line = AiLine.Read(ai);
            var edges = TrafficPlanBuilder.Edges.Of(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ai)!)!, "data"));
            var plan = TrafficPlanBuilder.Build(line, keepLeft, edges);
            var took = watch.Elapsed.TotalSeconds;

            var theirLanes = Lanes(theirs);
            var gaps = plan.Lanes.Select(lane =>
            {
                // Each of ours against the nearest of theirs, point by point, in plan view
                var best = theirLanes.Select(t => lane.Points.Average(p => Math.Sqrt(t.Min(q => (p.X - q.X) * (p.X - q.X) + (p.Z - q.Z) * (p.Z - q.Z))))).Min();
                return $"{lane.Name} {lane.Points.Count} pts, {best:0.00} m from theirs";
            });
            output.WriteLine($"{track}/{layout}: ours {plan.Lanes.Count} lane(s) ({plan.Why}) in {took:0.0} s, theirs {theirLanes.Count}; {string.Join("; ", gaps)}"
                + (plan.Problems.Count > 0 ? " | " + string.Join("; ", plan.Problems.Take(4)) : ""));
            var road = TrafficRoad.Measure(TrafficPlanBuilder.ToJson(plan));
            var theirRoad = TrafficRoad.Read(theirs);
            output.WriteLine($"    reach {road?.ReachKm:0.00} km (theirs {theirRoad?.ReachKm:0.00}): {TrafficSetup.Cars(road, 7, 1)} cars (theirs {TrafficSetup.Cars(theirRoad, 7, 1)})");
        }
    }

    /// <summary>
    /// A road made ready as a Free Drive makes it (SC_TRAFFIC_TRACK, SC_TRAFFIC_LAYOUT), with Open Drive's fleets
    /// (SC_TRAFFIC_MOD, the mod's folder): the plan and the models into SC_TRAFFIC_OUT, and what race.ini would get in
    /// prepared.json there, for scene-tools\open_drive\run_test.py --prepared
    /// </summary>
    [Fact(Explicit = true)]
    public void A_road_gets_its_traffic_ready()
    {
        Assert.NotNull(AcRoot);
        var track = Environment.GetEnvironmentVariable("SC_TRAFFIC_TRACK") ?? "rt_misty_loch";
        var layout = Environment.GetEnvironmentVariable("SC_TRAFFIC_LAYOUT") is { Length: > 0 } l ? l : null;
        var modFolder = Environment.GetEnvironmentVariable("SC_TRAFFIC_MOD") ?? @"D:\JUEGOS\STREET CORSA\mods\settings\open_drive";

        // The mods: the base and Open Drive's traffic folder alone
        var mods = Path.Combine(Out, "mods");
        if (Directory.Exists(mods)) Directory.Delete(mods, true);
        Directory.CreateDirectory(Path.Combine(mods, Mods.ModIds.BaseId));
        File.WriteAllText(Path.Combine(mods, Mods.ModIds.BaseId, "mod.json"), "{ \"id\": \"streetcorsa\" }");
        Directory.CreateDirectory(Path.Combine(mods, "open_drive", "traffic"));
        File.WriteAllText(Path.Combine(mods, "open_drive", "mod.json"), "{ \"id\": \"open_drive\" }");
        File.Copy(Path.Combine(modFolder, "traffic", "fleets.json"), Path.Combine(mods, "open_drive", "traffic", "fleets.json"));
        var set = Mods.ModSet.Load(mods);
        var fleets = TrafficFleets.Load(set);

        var watch = Stopwatch.StartNew();
        var progress = new List<string>();
        var traffic = TrafficSetup.For(AcRoot!, Path.Combine(AcRoot!, "content", "tracks", track), track, layout, 7, 1, fleets, TrafficDetail.Medium, set,
            new SyncProgress(p => progress.Add($"{p.Done}/{p.Of} {p.Making}")), store: new TrafficModelStore(Path.Combine(Out, "models")), planRoot: Path.Combine(Out, "plans"));
        output.WriteLine($"{track}/{layout}: ready in {watch.Elapsed.TotalSeconds:0.0} s: {traffic.Cars} cars, plan {traffic.Plan}, models {traffic.Models}, cast {traffic.Cast}");
        output.WriteLine("    progress: " + string.Join(", ", progress));
        File.WriteAllText(Path.Combine(Out, "prepared.json"), JsonSerializer.Serialize(new { track, layout, cars = traffic.Cars, plan = traffic.Plan, models = traffic.Models, cast = traffic.Cast }));
        Assert.NotNull(traffic.Plan);
        Assert.NotNull(traffic.Cast);
    }

    /// <summary>Progress told on the spot (Progress&lt;T&gt; posts to a context the test has not)</summary>
    private sealed class SyncProgress(Action<TrafficProgress> told) : IProgress<TrafficProgress>
    {
        public void Report(TrafficProgress value) => told(value);
    }

    private static List<List<(double X, double Z)>> Lanes(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file).TrimStart('﻿'));
        return document.RootElement.GetProperty("lanes").EnumerateArray()
            .Select(l => l.GetProperty("points").EnumerateArray().Select(p => (p[0].GetDouble(), p[2].GetDouble())).ToList()).ToList();
    }

    [Fact(Explicit = true)]
    public void Real_cars_convert()
    {
        Assert.NotNull(AcRoot);
        var cars = (Environment.GetEnvironmentVariable("SC_TRAFFIC_CARS") ?? "ks_mazda_miata ks_toyota_ae86 abarth500 ks_mazda_mx5_nd")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var folder = Path.Combine(Out, "models");
        Directory.CreateDirectory(folder);
        using var texels = new GpuTexels();
        var converter = new TrafficCarConverter(AcRoot!, texels);
        foreach (var car in cars)
        {
            var recipe = new TrafficCarRecipe { Id = car, Cars = [car] };
            var watch = Stopwatch.StartNew();
            try
            {
                var result = converter.Convert(recipe, folder, 1024);
                output.WriteLine($"{car}: {(result.Definition != null ? "made" : "NOT made: " + result.Problem)} in {watch.Elapsed.TotalSeconds:0.0} s, {result.Bytes / 1e6:0.0} MB");
                foreach (var note in result.Notes) output.WriteLine("    " + note);
            }
            catch (Exception ex)
            {
                output.WriteLine($"{car}: FAILED in {watch.Elapsed.TotalSeconds:0.0} s: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
