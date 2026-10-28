using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>Who sells a car as it comes into the game: what its looks are rolled by</summary>
public enum LookSource
{
    /// <summary>A dealer's lot: a used car, cleaned up for the sale</summary>
    Dealer,

    /// <summary>A private car (a rival's, the paper's, the player's first): as its owner left it</summary>
    Private
}

/// <summary>
/// How used a car looks (<see cref="Car.Rust"/>, <see cref="Car.PaintWear"/>, <see cref="Car.GlassCracks"/>, its dirt and
/// its dents) and how it gets that way: rolled when the car comes into the game by who sells it (a dealer's a bit better,
/// a private car crappier), the rust growing a little every day, a hit at the front cracking the windscreen. A broken
/// spring lets its corner sag. The figures are the rules' (<see cref="Rules.ConditionRules"/>).
/// </summary>
public static class CarLooks
{
    private static Rules.ConditionRules Rules => StreetCorsa.Rules.RuleBook.Current.Condition;

    /// <summary>The figures of a used car's looks, each 0..1</summary>
    public readonly record struct Rolled(double Rust, double Paint, double Cracks, double Dirt, int Seed);

    /// <summary>
    /// A used car's looks, for a car in <paramref name="condition"/> (0..1) sold by <paramref name="source"/>: the worse
    /// its condition, the more rust and worn paint; a private car carries more of both, now and then a cracked windscreen,
    /// and its dirt (a dealer washes its cars)
    /// </summary>
    public static Rolled Roll(double condition, LookSource source, Random random)
    {
        var rules = Rules;
        var wear = 1 - Unit.Clamp01(condition, 1);
        var dealer = source == LookSource.Dealer;
        double Between(double low, double high) => low + (high - low) * random.NextDouble();
        var rust = wear * (dealer ? rules.DealerRust : rules.PrivateRust) * Between(rules.RustRollLow, rules.RustRollHigh);
        var paint = wear * (dealer ? rules.DealerPaintWear : rules.PrivatePaintWear) * Between(rules.PaintRollLow, rules.PaintRollHigh);
        var cracks = random.NextDouble() < wear * (dealer ? rules.DealerCrackChance : rules.PrivateCrackChance)
            ? Between(rules.CrackRollLow, rules.CrackRollHigh)
            : 0;
        var dirt = dealer ? rules.DealerDirt * random.NextDouble() : rules.PrivateDirtMin + (rules.PrivateDirtMax - rules.PrivateDirtMin) * random.NextDouble();
        return new Rolled(Math.Min(1, rust), Math.Min(1, paint), cracks, Unit.Clamp01(dirt, 0), random.Next(1, int.MaxValue));
    }

    /// <summary>Gives <paramref name="car"/> the looks <paramref name="rolled"/></summary>
    public static void Give(Car car, Rolled rolled)
    {
        car.Rust = rolled.Rust;
        car.PaintWear = rolled.Paint;
        car.GlassCracks = rolled.Cracks;
        car.BodyDirt = rolled.Dirt;
        car.LookSeed = rolled.Seed;
    }

    /// <summary>Gives <paramref name="listing"/> the looks <paramref name="rolled"/></summary>
    public static void Give(UsedCarListing listing, Rolled rolled)
    {
        listing.Rust = rolled.Rust;
        listing.PaintWear = rolled.Paint;
        listing.GlassCracks = rolled.Cracks;
        listing.BodyDirt = rolled.Dirt;
        listing.LookSeed = rolled.Seed;
    }

    /// <summary>The car's looks onto the listing it is sold through (a car relisted, a rival's ad)</summary>
    public static void CopyTo(Car car, UsedCarListing listing) =>
        Give(listing, new Rolled(car.Rust, car.PaintWear, car.GlassCracks, car.BodyDirt, SeedOf(car)));

    /// <summary>The listing's looks onto the car bought from it: the car looks as it did on the lot</summary>
    public static void CopyTo(UsedCarListing listing, Car car) =>
        Give(car, new Rolled(listing.Rust, listing.PaintWear, listing.GlassCracks, listing.BodyDirt, SeedOf(listing)));

    /// <summary>The listing's pattern: its own, or one from its id (a listing from an older save), as the lot drew it</summary>
    private static int SeedOf(UsedCarListing listing) => listing.LookSeed != 0 ? listing.LookSeed : SeedOf(listing.Id);

    /// <summary>The car's pattern: its own, or one from its id (a car from an older save)</summary>
    public static int SeedOf(Car car) => car.LookSeed != 0 ? car.LookSeed : SeedOf(car.InstanceId.ToString());

    private static int SeedOf(string id)
    {
        var hash = unchecked((int)StableHash.Of(id));
        return hash == 0 ? 1 : hash;
    }

    /// <summary>
    /// How the car looks for its wear layer, rounded to what can be seen (<see cref="CarLook.Rounded"/>): a day's rust
    /// or a hair of dirt is the same look, so a scene showing the car does not take its wear off and put it on again
    /// </summary>
    public static CarLook Of(Car car)
    {
        var body = CarCondition.Body(car);
        return new CarLook(Clean(car.Rust), Clean(car.PaintWear), Clean(car.GlassCracks), Clean(car.BodyDirt),
            body[0], body[1], body[2], body[3], SeedOf(car)).Rounded();
    }

    /// <summary>How the car on sale looks for its wear layer, rounded as <see cref="Of(Car)"/> is</summary>
    public static CarLook Of(UsedCarListing listing)
    {
        var body = listing.BodyDamageKmh is { Length: >= 4 } b ? b : new double[4];
        return new CarLook(Clean(listing.Rust), Clean(listing.PaintWear), Clean(listing.GlassCracks), Clean(listing.BodyDirt),
            Kmh(body[0]), Kmh(body[1]), Kmh(body[2]), Kmh(body[3]), SeedOf(listing)).Rounded();
    }

    /// <summary>
    /// A day goes by for the car: its rust grows, a little on a sound body (a chip lets the water in) and faster the more
    /// there is. Nothing for a clean, sound body.
    /// </summary>
    public static void Age(Car car, int days = 1)
    {
        var rules = Rules;
        var rust = Clean(car.Rust);
        var start = rust > 0 ? 1.0 : Clean(car.PaintWear);
        for (var d = 0; d < days && start > 0; d++)
            rust = Math.Min(1, rust + rules.RustPerDay * (rules.RustStart * start + rust));
        car.Rust = rust;
    }

    /// <summary>
    /// A race's hit at the front cracks the windscreen: a hit of <see cref="Rules.ConditionRules.CrackFromKmh"/> on the front
    /// zone (what this race added to it, not what the body carried in), across it at
    /// <see cref="Rules.ConditionRules.ShatteredKmh"/>; a crack never mends on its own. True when the screen is cracked now
    /// and was not before.
    /// </summary>
    public static bool CrackFromHit(Car car, double frontKmhBefore)
    {
        var rules = Rules;
        // The hit as the race report tells it: less than half a km/h is AC's rounding, no hit
        var hit = CarCondition.Body(car)[0] - Kmh(frontKmhBefore);
        if (hit <= HitTolerance || hit < rules.CrackFromKmh) return false;

        var was = Clean(car.GlassCracks);
        var cracks = Math.Clamp((hit - rules.CrackFromKmh) / Math.Max(1, rules.ShatteredKmh - rules.CrackFromKmh) * 0.8 + 0.2, 0, 1);
        car.GlassCracks = Math.Max(was, cracks);
        return was < 0.01;
    }

    /// <summary>
    /// How far each corner of the body sags, metres, AC's wheel order (negative: lower): a spring bent past
    /// <see cref="Rules.ConditionRules.SagFrom"/> lets its corner down, to <see cref="Rules.ConditionRules.MostSag"/> for a
    /// broken one. The damage the race starts with; how the car sits in all is <see cref="Stance(Car, PartsCatalog)"/>.
    /// </summary>
    public static double[] Sag(Car car) => Sag(car.Parts, car.HasRunningGearAssigned);

    /// <summary>How the car on sale sags, as <see cref="Sag(Car)"/>: the car bought sags the same</summary>
    public static double[] Sag(UsedCarListing listing) => Sag(listing.Parts, listing.HasRunningGearAssigned);

    private static double[] Sag(List<PartInstance> parts, bool hasRunningGear)
    {
        var heights = new double[RunningGear.Corners];
        if (!hasRunningGear) return heights;

        var rules = Rules;
        var corners = RunningGear.Mounted(parts);
        for (var i = 0; i < corners.Length && i < heights.Length; i++)
        {
            if (corners[i].Spring is not { } spring) continue;
            var tear = Unit.Clamp01(spring.Tear, 1);
            if (tear < rules.SagFrom) heights[i] = -rules.MostSag * (rules.SagFrom - tear) / rules.SagFrom;
        }
        return heights;
    }

    /// <summary>
    /// How far each corner of the body sits from where its model has it, metres, AC's wheel order (negative: lower), the
    /// one place it is worked out for the scenes: its sag (<see cref="Sag(Car)"/>), its spring's <c>drop</c> (a lowered
    /// spring, a lowering block on it: <see cref="RunningGear.Drop(PartsCatalog, PartInstance?)"/>) and the ride height the
    /// player set (<see cref="CarSetup"/>). AC lowers the car on its data the same (the running gear's and the setup's
    /// <c>ROD_LENGTH</c>, the damage's).
    /// </summary>
    public static double[] Stance(Car car, PartsCatalog catalog)
    {
        var heights = Stance(car.Parts, car.HasRunningGearAssigned, catalog);
        var setup = CarSetup.For(car.Parts, car.Setup, catalog, StreetCorsa.Rules.RuleBook.Current.Tuning.Ranges()).Values;
        for (var i = 0; i < heights.Length; i++)
            heights[i] += setup.GetValueOrDefault(RunningGear.IsFront(i) ? CarSetup.RideHeightFront : CarSetup.RideHeightRear) * MetresPerMm;
        return heights;
    }

    /// <summary>How the car on sale sits, as <see cref="Stance(Car, PartsCatalog)"/>: a listing carries no setup</summary>
    public static double[] Stance(UsedCarListing listing, PartsCatalog catalog) => Stance(listing.Parts, listing.HasRunningGearAssigned, catalog);

    private static double[] Stance(List<PartInstance> parts, bool hasRunningGear, PartsCatalog catalog)
    {
        var heights = Sag(parts, hasRunningGear);
        if (!hasRunningGear) return heights;

        var corners = RunningGear.Mounted(parts);
        for (var i = 0; i < corners.Length && i < heights.Length; i++)
            heights[i] -= RunningGear.Drop(catalog, corners[i].Spring) * MetresPerMm;
        return heights;
    }

    private const double MetresPerMm = 0.001;

    /// <summary>Less than this on a zone is no hit (the race report's own line: AC gives the damage to a tenth)</summary>
    private const double HitTolerance = 0.5;

    private static double Clean(double v) => Unit.Clamp01(v, 0);

    private static double Kmh(double v) => double.IsFinite(v) && v > 0 ? v : 0;
}
