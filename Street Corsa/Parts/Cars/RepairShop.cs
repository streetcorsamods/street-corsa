using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Parts.Cars;

/// <summary>One piece of work the garage can do on a car: what it costs, how long it takes, and doing it</summary>
public sealed class RepairJob
{
    public RepairJob(string id, string name, string detail, decimal cost, GameAction time, Action apply, bool isChore = false)
    {
        IsChore = isChore;
        Name = name;
        Id = id;
        Detail = detail;
        Cost = cost;
        Time = time;
        _apply = apply;
    }

    private readonly Action _apply;

    /// <summary>The job as the player reads it</summary>
    public string Name { get; }

    /// <summary>The job as the code knows it, whatever the language: <see cref="GasStation.FillUp"/>, <see cref="RepairShop.CanOfGas"/></summary>
    public string Id { get; }

    /// <summary>Filling up or a wash: looking after the car, not fixing damage</summary>
    public bool IsChore { get; }

    /// <summary>What is wrong, in a few words</summary>
    public string Detail { get; }

    public decimal Cost { get; }

    /// <summary><see cref="GameAction.GarageWorkMinor"/> or <see cref="GameAction.GarageWorkMajor"/></summary>
    public GameAction Time { get; }

    /// <summary>Does the work on the car; money and time are the caller's</summary>
    public void Apply() => _apply();
}

/// <summary>
/// Repairs: what takes damage (<c>Tear</c>, and the body) off a car. Mileage (<c>Wear</c>) is not repaired here: a
/// worn part is replaced, or rebuilt, in the parts view (<see cref="Rebuilding"/>). A job costs a share of the damaged parts' new price, as much of it as the
/// damage goes, plus the labour; the body shop charges by how hard the body was hit. Small jobs take half an hour,
/// big ones two hours. Filling up, the oil and the wash are the gas station's (<see cref="GasStation"/>); a car too dry to
/// get there gets a can of gas (<see cref="CanOfGas"/>).
/// </summary>
public static class RepairShop
{
    /// <summary>Share of a part's new price a repair of a fully wrecked part costs</summary>
    public static double PartShare => Rules.RuleBook.Current.Repairs.PartShare;

    public static decimal Labour => Rules.RuleBook.Current.Repairs.Labour;

    /// <summary>Body work, per km/h of collision the body took</summary>
    public static decimal BodyCostPerKmh => Rules.RuleBook.Current.Repairs.BodyCostPerKmh;

    /// <summary>Less damage than this on a body is a small job</summary>
    public static double SmallBodyJobKmh => Rules.RuleBook.Current.Repairs.SmallBodyJobKmh;

    /// <summary>The job that puts a can of gas in a dry car</summary>
    public const string CanOfGas = "can_of_gas";

    /// <summary>Rust cut out and the body resprayed (<see cref="Rules.RepairRules.RustCost"/>)</summary>
    public static decimal RustCost => Rules.RuleBook.Current.Repairs.RustCost;

    /// <summary>A respray for paint worn all over</summary>
    public static decimal PaintCost => Rules.RuleBook.Current.Repairs.PaintCost;

    public static decimal WindscreenCost => Rules.RuleBook.Current.Repairs.WindscreenCost;

    /// <summary>Less rust than this is not worth a body shop's while; less worn paint than this is not worth a respray</summary>
    public static double SomeRust => Rules.RuleBook.Current.Repairs.SomeRust;

    public static double SomePaintWear => Rules.RuleBook.Current.Repairs.SomePaintWear;

    /// <summary>Rust from this on is a big job</summary>
    public static double BigRustJob => Rules.RuleBook.Current.Repairs.BigRustJob;

    public const string Body = "body";
    public const string Rust = "rust";
    public const string Paint = "paint";
    public const string Windscreen = "windscreen";
    public const string Engine = "engine";
    public const string Gearbox = "gearbox";
    public const string Axle = "axle";

    /// <summary>A job for the car's looks (rust, paint, the windscreen): it does not get a car racing</summary>
    public static bool IsCosmetic(RepairJob job) => job.Id is Rust or Paint or Windscreen;

    /// <summary>The springs and shocks of a corner (<see cref="RunningGear.CornerName"/>'s index after the dot)</summary>
    public static string Corner(int corner) => $"corner.{corner}";

    /// <summary>A new tyre at a corner</summary>
    public static string Tyre(int corner) => $"tyre.{corner}";

    /// <summary>What the can holds, litres: about a gallon</summary>
    public static double CanOfGasLitres => Rules.RuleBook.Current.Repairs.CanOfGasLitres;

    /// <summary>
    /// The jobs the body needs before it is painted (<see cref="Rust"/>, <see cref="Body"/>: its rust cut out, its dents
    /// and scratches taken out; the painter sprays over neither), as <see cref="Jobs"/> has them; empty when none
    /// </summary>
    public static IReadOnlyList<string> BeforePaint(Car car)
    {
        var needs = new List<string>();
        if (Unit.Clamp01(car.Rust, 0) >= SomeRust) needs.Add(Rust);
        if (CarCondition.BodyTotal(car) > BodyWorth) needs.Add(Body);
        return needs;
    }

    /// <summary>Less damage than this on a body, km/h of collision, is not worth a body shop's while</summary>
    private const double BodyWorth = 0.5;

    /// <summary>A part at this or better needs nothing done</summary>
    private const double Straight = PartInstance.Undamaged;

    /// <param name="catalog">Null when there are no parts: only the body can be done</param>
    /// <param name="priceMultiplier">The save's <see cref="GameRules.PartPriceMultiplier"/>, on the whole bill</param>
    /// <param name="gas">The gas station's prices, which the can of gas is paid at; the base game's when null</param>
    public static List<RepairJob> Jobs(Car car, PartsCatalog? catalog, double priceMultiplier = 1.0, StationPrices? gas = null)
    {
        var prices = GameRules.Sane(priceMultiplier);
        var jobs = new List<RepairJob>();
        Func<string, string?>? groupOf = catalog == null ? null : CarCondition.Groups(catalog);
        void Refresh()
        {
            if (groupOf != null) CarCondition.RefreshFigures(car, groupOf);
            else car.BodyCondition = CarCondition.BodyCondition(car);
        }

        // A car too dry to make it to the gas station gets a can of gas, enough to get there: no tow truck needed
        if (GasStation.NeedsTow(car) && CarCondition.FuelLeft(car) is { } fuel)
        {
            var litres = Math.Min(CanOfGasLitres, (CarCondition.TankLitres(car) ?? CanOfGasLitres) - fuel);
            jobs.Add(new RepairJob(CanOfGas, PartWords.Say("repair.job.can_of_gas"), PartWords.Say("repair.job.can_of_gas.detail"),
                GasStation.GasPrice(gas ?? StationPrices.Default, litres, prices), GameAction.GarageChore,
                () => car.FuelLitres = fuel + litres, isChore: true));
        }

        var body = CarCondition.BodyTotal(car);
        if (body > BodyWorth)
        {
            var detail = CarCondition.IsTotaled(car) ? PartWords.Say("repair.job.body.totaled") : PartWords.Say("repair.job.body.dents", ("kmh", body));
            jobs.Add(new RepairJob(Body, PartWords.Say("repair.job.body"), detail, LabourAt(prices) + Math.Round(BodyCostPerKmh * (decimal)body * (decimal)prices),
                body < SmallBodyJobKmh ? GameAction.GarageWorkMinor : GameAction.GarageWorkMajor,
                () => { car.BodyDamageKmh = new double[CarCondition.Zones]; Refresh(); }));
        }

        // Rust cut out and the body resprayed, or the paint alone; a cracked windscreen replaced
        var rust = Unit.Clamp01(car.Rust, 0);
        var paint = Unit.Clamp01(car.PaintWear, 0);
        if (rust >= SomeRust)
        {
            jobs.Add(new RepairJob(Rust, PartWords.Say("repair.job.rust"), PartWords.Say("repair.job.rust.detail", ("rust", rust * 100), ("paint", paint * 100)),
                LabourAt(prices) + Math.Round((RustCost * (decimal)rust + PaintCost * (decimal)paint) * (decimal)prices),
                rust < BigRustJob ? GameAction.GarageWorkMinor : GameAction.GarageWorkMajor,
                () => { car.Rust = 0; car.PaintWear = 0; }));
        }
        else if (paint >= SomePaintWear)
        {
            jobs.Add(new RepairJob(Paint, PartWords.Say("repair.job.paint"), PartWords.Say("repair.job.paint.detail", ("paint", paint * 100)),
                LabourAt(prices) + Math.Round(PaintCost * (decimal)paint * (decimal)prices), GameAction.GarageWorkMinor,
                () => { car.Rust = 0; car.PaintWear = 0; }));
        }

        if (Unit.Clamp01(car.GlassCracks, 0) >= 0.01)
        {
            jobs.Add(new RepairJob(Windscreen, PartWords.Say("repair.job.windscreen"), PartWords.Say("repair.job.windscreen.detail"),
                LabourAt(prices) + PartPricing.Round((double)WindscreenCost * prices), GameAction.GarageWorkMinor, () => car.GlassCracks = 0));
        }

        if (catalog == null || groupOf == null) return jobs;

        var gearbox = CarCondition.Transmission(car, groupOf);
        if (car.Engine is { } engine)
        {
            var torn = engine.SelfAndDescendants().Where(p => !ReferenceEquals(p, gearbox) && p.Tear < Straight).ToList();
            if (torn.Count > 0)
            {
                var life = CarCondition.EngineLife(car, groupOf) / CarCondition.NewEngineLife;
                var weakest = CarCondition.FailedPart(car, groupOf);
                var failed = weakest == null ? null : catalog.Get(weakest.DefinitionId) is { } definition ? definition.DisplayName ?? definition.Name : null;
                var detail = life <= 0
                    ? failed == null ? PartWords.Say("repair.job.blown") : PartWords.Say("repair.job.engine.blown_part", ("part", failed))
                    : failed == null ? PartWords.Say("repair.job.engine.damaged", ("life", life * 100))
                    : PartWords.Say("repair.job.engine.damaged_part", ("part", failed), ("life", life * 100));
                jobs.Add(PartsJob(Engine, PartWords.Say("repair.job.engine"), detail, torn, catalog, prices, GameAction.GarageWorkMajor, Refresh));
            }
        }

        if (gearbox is { Tear: < Straight })
        {
            var detail = gearbox.Tear < CarCondition.BrokenBelow ? PartWords.Say("repair.job.gearbox.wrecked") : PartWords.Say("repair.job.gearbox.worn", ("left", gearbox.Tear * 100));
            jobs.Add(PartsJob(Gearbox, PartWords.Say("repair.job.gearbox"), detail, new[] { gearbox }, catalog, prices,
                gearbox.Tear < 0.5 ? GameAction.GarageWorkMajor : GameAction.GarageWorkMinor, Refresh));
        }

        // The rear axle's broken or hurt parts: a shaft, the gears, the carrier set right
        var axle = RearAxle.Mounted(car.Parts).All.Where(p => p.Tear < Straight).ToList();
        if (axle.Count > 0)
        {
            var broken = axle.Any(p => p.Tear < CarCondition.BrokenBelow);
            jobs.Add(PartsJob(Axle, PartWords.Say("repair.job.axle"), PartWords.Say(broken ? "repair.job.axle.broken" : "repair.job.axle.worn"), axle, catalog, prices,
                broken ? GameAction.GarageWorkMajor : GameAction.GarageWorkMinor, Refresh));
        }

        var corners = RunningGear.Mounted(car.Parts, CarCondition.Groups(catalog));
        for (var i = 0; i < corners.Length; i++)
        {
            var name = RunningGear.CornerName(i);
            var bent = new[] { corners[i].Spring, corners[i].Shock }.Where(p => p is { Tear: < Straight }).Select(p => p!).ToList();
            if (bent.Count > 0)
                jobs.Add(PartsJob(Corner(i), PartWords.Say("repair.job.corner", ("corner", name)), PartWords.Say("repair.job.corner.detail"), bent, catalog, prices, GameAction.GarageWorkMinor, Refresh));

            if (corners[i].Tyre is { Tear: < Straight } tyre && catalog.Get(tyre.DefinitionId) is { } tyreDefinition)
            {
                // A blown tyre is not patched: the same tyre, new, goes on
                jobs.Add(new RepairJob(Tyre(i), PartWords.Say("repair.job.tyre", ("corner", name)), PartWords.Say("repair.job.blown"), LabourAt(prices) + PartPricing.Round(PartPricing.NewPrice(tyreDefinition) * prices),
                    GameAction.GarageWorkMinor, () => { tyre.Tear = 1; tyre.Wear = 1; Refresh(); }));
            }
        }

        return jobs;
    }

    private static decimal LabourAt(double prices) => PartPricing.Round((double)Labour * prices);

    private static RepairJob PartsJob(string id, string name, string detail, IReadOnlyList<PartInstance> parts, PartsCatalog catalog, double prices, GameAction time, Action refresh)
    {
        var cost = parts.Sum(p => catalog.Get(p.DefinitionId) is { } definition
            ? PartPricing.NewPrice(definition) * PartShare * (1 - Unit.Clamp01(p.Tear, 1))
            : 0);
        return new RepairJob(id, name, detail, LabourAt(prices) + PartPricing.Round(cost * prices), time, () =>
        {
            foreach (var part in parts) part.Tear = 1;
            refresh();
        });
    }
}
