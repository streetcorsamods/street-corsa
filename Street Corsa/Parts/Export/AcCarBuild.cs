using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts.Export;

/// <summary>A car's parts, as far as the export needs them</summary>
public sealed class CarBuild
{
    /// <summary>The engine on the dyno; null for a car without one</summary>
    public EngineReport? Engine { get; init; }

    /// <summary>Kilograms of the engine the car's data was made with, for the mass</summary>
    public double? FactoryEngineMass { get; init; }

    /// <summary>What is on the wheel slots; null leaves the car's running gear as its data has it</summary>
    public CornerParts[]? RunningGear { get; init; }

    /// <summary>The sway bars on the car, front and rear; null leaves the car's [ARB] as its data has it</summary>
    public (PartInstance? Front, PartInstance? Rear)? SwayBars { get; init; }

    /// <summary>The running gear the car's data describes</summary>
    public (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear { get; init; }

    /// <summary>What is on the car's rear axle; null leaves the final drive and differential to the gearbox</summary>
    public RearAxleParts? RearAxle { get; init; }

    /// <summary>The rear axle the car's data describes; null for a car without one (its rear wheels not driven)</summary>
    public RearAxleChoice? FactoryAxle { get; init; }

    /// <summary>The damage the car goes in with; null or none leaves the data as the parts make it</summary>
    public Models.Race.RaceStartState? Damage { get; init; }

    /// <summary>The parts on the car's chassis slots (<see cref="Cars.RunningGear.Chassis"/>); null or none leaves the aero and electronics as they are</summary>
    public IReadOnlyList<PartDefinition>? Chassis { get; init; }

    /// <summary>The car's setup (<see cref="CarSetup.For"/>); null leaves the data and its setup.ini as the parts make them</summary>
    public SetupChoice? Setup { get; init; }
}

public sealed class CarBuildResult
{
    /// <summary>File name to new content, for the files of the car's data that change</summary>
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The sound the engine races with; null keeps the car's own</summary>
    public CarSound? Sound { get; set; }

    /// <summary>What keeps the car from being driven, in words for the player; empty when it can go</summary>
    public List<string> Problems { get; } = new();

    public bool CanDrive => Problems.Count == 0;
}

/// <summary>
/// Everything a car's parts change in its Assetto Corsa data: the engine and transmission
/// (<see cref="AcEngineData"/>), the running gear (<see cref="AcRunningGearData"/>), the spoiler and the electronics
/// (<see cref="AcChassisData"/>), the player's setup and the damage the
/// parts carry (<see cref="AcSetupData"/>, <see cref="AcDamageData"/>), one file set.
/// </summary>
public static class AcCarBuild
{
    private const string SuspensionsFile = "suspensions.ini";

    public static CarBuildResult Generate(PartsCatalog catalog, CarBuild build, Func<string, string?> readFile, AcEngineDataOptions? options = null)
    {
        var result = new CarBuildResult();

        if (build.Engine == null) result.Problems.Add("the car has no engine");
        else if (!build.Engine.Runs) result.Problems.Add("the engine does not run: " + (build.Engine.Problem ?? "no power"));
        else
        {
            options ??= new AcEngineDataOptions();
            options.FactoryEngineMass = build.FactoryEngineMass;
            foreach (var (name, content) in AcEngineData.Generate(build.Engine, readFile, options)) result.Files[name] = content;
        }

        if (build.RunningGear != null && build.FactoryRunningGear is { } factory)
        {
            // The running gear reads what the engine left (the mass in car.ini) before the car's own files
            string? Read(string name) => result.Files.GetValueOrDefault(name) ?? readFile(name);
            foreach (var (name, content) in AcRunningGearData.Generate(catalog, build.RunningGear, factory, Read, result.Problems, build.SwayBars)) result.Files[name] = content;
        }

        // The axle after the engine: its final drive and differential over the gearbox's, its weight over the running gear's
        if (build.RearAxle != null && build.FactoryAxle is { } axle)
        {
            string? Read(string name) => result.Files.GetValueOrDefault(name) ?? readFile(name);
            foreach (var (name, content) in AcAxleData.Generate(catalog, build.RearAxle, axle, Read, readFile, result.Problems)) result.Files[name] = content;
        }

        // The spoiler's wing and the electronics unit's aids, over the car's own aero and electronics
        if (build.Chassis is { Count: > 0 } chassis)
        {
            string? Read(string name) => result.Files.GetValueOrDefault(name) ?? readFile(name);
            foreach (var (name, content) in AcChassisData.Generate(chassis, Read)) result.Files[name] = content;
        }

        // The setup over what the parts made, under the damage; as the data the parts made can take it
        SetupChoice? setup = null;
        if (build.Setup != null && result.CanDrive)
        {
            string? Read(string name) => result.Files.GetValueOrDefault(name) ?? readFile(name);
            setup = AcSetupData.ForData(build.Setup, Read);
            foreach (var (name, content) in AcSetupData.Apply(setup, Read)) result.Files[name] = content;
        }

        // The damage goes on top of what the parts made of the files
        if (build.Damage != null && result.CanDrive)
        {
            string? Read(string name) => result.Files.GetValueOrDefault(name) ?? readFile(name);
            foreach (var (name, content) in AcDamageData.Generate(build.Damage, Read)) result.Files[name] = content;
        }

        // setup.ini's ranges around what the car races on, or AC holds the figures to the author's ranges: on the setup's
        // own steps, before the hold below, the ride height's held to the travel too
        if (setup != null && result.CanDrive)
        {
            string? Read(string name) => result.Files.GetValueOrDefault(name) ?? readFile(name);
            foreach (var (name, content) in AcSetupData.Range(setup, catalog, build, Read, readFile)) result.Files[name] = content;
        }

        // Last: the parts, the setup and the damage together move the ride height no further than the axle's travel allows
        if (result.Files.TryGetValue(SuspensionsFile, out var suspensions) && readFile(SuspensionsFile) is { } own &&
            AcRunningGearData.HeldToTravel(own, suspensions) is { } held)
            result.Files[SuspensionsFile] = held;

        return result;
    }
}
