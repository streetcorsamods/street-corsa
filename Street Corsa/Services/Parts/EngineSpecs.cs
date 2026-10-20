using System.Globalization;
using System.IO;
using StreetCorsa.Helpers;
using StreetCorsa.Audio;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// What it takes to start a car's engine where it stands: the engine on its parts, as the dyno has it, and the
    /// sound it races with
    /// </summary>
    public static class EngineSpecs
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("EngineAudio");

        /// <summary>
        /// What keeps the engine the dyno ran (<paramref name="report"/>) from running, in words for the player; null when
        /// it runs. No report is an engine missing what it takes to run at all.
        /// </summary>
        public static string? ProblemOf(StreetCorsa.Parts.Logic.EngineReport? report) =>
            report == null ? GameText.Say("enginespecs.incomplete")
            : report.Runs ? null
            : report.Problem ?? GameText.Say("enginespecs.no_power");

        /// <summary>"the engine won't run (no spark)": the car's <paramref name="problem"/> (<see cref="ProblemOf"/>) among the others</summary>
        public static string WontRun(string? problem) => GameText.Say("garage.station.engine_wont_run", ("problem", problem));

        /// <summary>
        /// The engine of a car, worked out away from the UI thread. Null when the car has no engine to start (or
        /// the parts catalog is not there).
        /// </summary>
        public static Task<EngineSpec?> ForAsync(ICarPartsService parts, Car car, string name)
        {
            // The worker gets a copy made here, on the caller's thread: the car's own parts may be changed on the
            // workbench while the dyno runs
            var copy = car.Snapshot();
            return Task.Run(() =>
            {
                try
                {
                    return For(parts, copy, name);
                }
                catch (Exception ex)
                {
                    Logger.Warning("{Car}: the engine cannot be started in the garage: {Error}", copy.DefinitionId, ex.Message);
                    return null;
                }
            });
        }

        /// <summary>
        /// A car for sale, as it is: its own parts, or the factory's when the offer came without any (an older save)
        /// </summary>
        public static async Task<EngineSpec?> ForSaleAsync(ICarPartsService parts, UsedCarListing listing, string name)
        {
            var car = new Car(listing.CarDefinitionId)
            {
                Parts = listing.Parts,
                HasPartsAssigned = listing.Parts.Count > 0,
                HasRunningGearAssigned = true,
                GearVersion = RunningGearFactory.GearVersion,
                EngineHealth = listing.Condition
            };

            if (!car.HasPartsAssigned)
            {
                // A copy of the list: the offer is not the place to keep a factory engine made up to listen to
                car.Parts = new List<PartInstance>();
                try
                {
                    await parts.EnsurePartsAsync(car);
                }
                catch (Exception ex)
                {
                    // A lot's engine that cannot be put together is one that does not start: the lot goes on
                    Logger.Warning("{Car}: no factory engine to start on the lot: {Error}", car.DefinitionId, ex.Message);
                    return null;
                }
            }

            return await ForAsync(parts, car, name);
        }

        private static EngineSpec? For(ICarPartsService parts, Car car, string name)
        {
            var layout = parts.IsAvailable ? parts.EngineLayoutOf(car.DefinitionId) : StreetCorsa.Parts.EngineLayout.Front;
            var directory = Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId);

            // A car the game has no engine build for (no pack for its make installed, or no parts at all) starts and
            // sounds on the engine Assetto Corsa runs it on, as it races; one whose engine was taken out has nothing to start
            if (!parts.IsAvailable || (car.Engine == null && !car.HasPartsAssigned))
                return FromAcData(name, AcCarDataReader.ForCar(directory), AcCarSound.FromCar(directory, AppSettings.Instance.SfxGuidsPath), layout);
            if (car.Engine == null) return null;

            var report = parts.Evaluate(car);
            var dyno = report?.Dyno;
            var problem = ProblemOf(report);

            var sound = parts.ChooseSound(car, report) ?? AcCarSound.FromCar(directory, AppSettings.Instance.SfxGuidsPath);

            // An engine without a dyno curve does not run (its problem says why): no torque and no cylinder count are
            // made up here, the sim stands in its own figures for the cranking (EngineSim, BodyRock)
            Func<double, double> torque = dyno is { Curve.Count: > 0 }
                ? rpm => dyno.TorqueAt(Math.Min(rpm, dyno.Curve[^1].Rpm))
                : _ => 0;

            return new EngineSpec(
                name,
                report?.IdleRpm ?? 0,
                report?.LimiterRpm ?? 0,
                report?.Inputs?.Cylinders ?? 0,
                report?.Inertia ?? 0,
                torque,
                dyno?.MaxTorque ?? 0,
                sound,
                problem,
                layout);
        }

        /// <summary>
        /// The engine a car runs on in Assetto Corsa, from its own data (<paramref name="read"/>, a data file by name:
        /// engine.ini's idle, limiter and inertia, the torque curve it names, power.lut unless it says another), with
        /// the car's own sound. It runs: what AC drives, the game starts. Null when the data has no engine.ini: not a
        /// car's data. A figure missing or spoilt is left at 0, and the sim stands in its own (<see cref="EngineSim"/>).
        /// The cylinders are not in the data; 0 is unknown to the body rock.
        /// </summary>
        public static EngineSpec? FromAcData(string name, Func<string, string?> read, CarSound? sound, StreetCorsa.Parts.EngineLayout layout)
        {
            if (read(AcEngineData.EngineFile) is not { } text) return null;
            var engine = new IniText(text);

            var curveFile = engine.Get("HEADER", "POWER_CURVE") is { Length: > 0 } named && PathNames.IsSafeSegment(named) ? named : AcEngineData.DefaultPowerCurve;
            var curve = TorqueCurve(read(curveFile));

            // AC's inertia is kg m2; the spec's is the parts' unit, which the sim scales to its own figures
            var inertia = engine.GetNumber("ENGINE_DATA", "INERTIA") is > 0 and var kgm2 ? kgm2 / StreetCorsa.Parts.Logic.PartFigures.KgM2PerInertiaUnit : 0;

            return new EngineSpec(
                name,
                engine.GetNumber("ENGINE_DATA", "MINIMUM") ?? 0,
                engine.GetNumber("ENGINE_DATA", "LIMITER") ?? 0,
                0,
                inertia,
                rpm => TorqueAt(curve, rpm),
                curve.Count == 0 ? 0 : curve.Max(p => p.Torque),
                sound,
                null,
                layout);
        }

        /// <summary>A .lut's "rpm|torque" lines, in rpm order; lines that are not that are skipped</summary>
        private static List<(double Rpm, double Torque)> TorqueCurve(string? lut)
        {
            var points = new List<(double Rpm, double Torque)>();
            if (lut == null) return points;
            foreach (var raw in lut.Split('\n'))
            {
                var line = raw.Trim();
                var bar = line.IndexOf('|');
                if (bar <= 0) continue;
                if (double.TryParse(line[..bar], NumberStyles.Float, CultureInfo.InvariantCulture, out var rpm)
                    && double.TryParse(line[(bar + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var torque))
                    points.Add((rpm, torque));
            }

            return points.OrderBy(p => p.Rpm).ToList();
        }

        /// <summary>The curve read between its points, held at its ends</summary>
        private static double TorqueAt(List<(double Rpm, double Torque)> curve, double rpm)
        {
            if (curve.Count == 0) return 0;
            if (rpm <= curve[0].Rpm) return curve[0].Torque;
            if (rpm >= curve[^1].Rpm) return curve[^1].Torque;
            for (var i = 1; i < curve.Count; i++)
            {
                var (r1, t1) = curve[i];
                if (rpm > r1) continue;
                var (r0, t0) = curve[i - 1];
                return r1 == r0 ? t1 : t0 + (t1 - t0) * (rpm - r0) / (r1 - r0);
            }

            return curve[^1].Torque;
        }
    }
}
