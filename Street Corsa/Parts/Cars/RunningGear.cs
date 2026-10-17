using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>How a brake heats and fades: Assetto Corsa's brakes.ini temperatures (<see cref="RunningGear.Heat(PartDefinition, PartDefinition?)"/>)</summary>
public sealed record BrakeHeat(double TorqueK, double CoolTransfer, double CoolSpeedFactor, string Curve);

/// <summary>What one corner of a car carries: the rim with its tyre, the brake, the spring and the shock</summary>
public sealed class CornerParts
{
    public PartInstance? Rim { get; set; }
    public PartInstance? Tyre { get; set; }
    public PartInstance? Brake { get; set; }
    public PartInstance? Spring { get; set; }
    public PartInstance? Shock { get; set; }
}

/// <summary>
/// The running gear of a car: what sits on the car's own wheel slots, numbered as the part scripts number them
/// (the chassis script reads rim 101+i, brake 111+i, shock 301+i, spring 311+i for wheel i; 0-1 front, 2-3 rear,
/// even numbers left). The tyre goes on the rim. What each part does to the car is read off its script fields
/// with the framework's own formulas.
/// </summary>
public static class RunningGear
{
    public const int Corners = 4;
    public const int TyreSlotOnRim = 2;

    private static readonly string[] CornerKeys =
        ["condition.corner.front_left", "condition.corner.front_right", "condition.corner.rear_left", "condition.corner.rear_right"];

    /// <summary>A corner as the player reads it ("front left")</summary>
    public static string CornerName(int corner) => PartWords.Say(CornerKeys[corner]);

    /// <summary>The corners in English, for the log</summary>
    public static readonly string[] CornerIds = ["front left", "front right", "rear left", "rear right"];

    /// <summary>An axle as the player reads it ("front"): the front's for corners 0-1, the rear's for 2-3</summary>
    public static string AxleName(int corner) => PartWords.Say(IsFront(corner) ? "condition.axle.front" : "condition.axle.rear");

    public static int WheelSlot(int corner) => 101 + corner;
    public static int BrakeSlot(int corner) => 111 + corner;
    public static int ShockSlot(int corner) => 301 + corner;
    public static int SpringSlot(int corner) => 311 + corner;

    public static bool IsFront(int corner) => corner < 2;

    /// <summary>The car's own slots of the sway bars (step 31), beside the rear axle's 401: one across each axle</summary>
    public const int FrontSwayBarSlot = 402;
    public const int RearSwayBarSlot = 403;

    public static int SwayBarSlot(bool front) => front ? FrontSwayBarSlot : RearSwayBarSlot;

    public static bool IsSwayBarSlot(int carSlot) => carSlot is FrontSwayBarSlot or RearSwayBarSlot;

    /// <summary>The slot of a leaf spring its traction bar goes on</summary>
    public const int TractionBarSlot = 2;

    /// <summary>Whether a car slot is one the rims go on</summary>
    public static bool IsWheelSlot(int carSlot) => carSlot >= 101 && carSlot < 101 + Corners;

    /// <summary>The rims on the car's wheel slots</summary>
    public static IEnumerable<PartInstance> Rims(List<PartInstance> carParts) => carParts.Where(p => IsWheelSlot(p.ParentSlot));

    /// <summary>The corner a car slot belongs to, -1 for slots that are not running gear</summary>
    public static int CornerOf(int carSlot) => carSlot switch
    {
        >= 101 and < 101 + Corners => carSlot - 101,
        >= 111 and < 111 + Corners => carSlot - 111,
        >= 301 and < 301 + Corners => carSlot - 301,
        >= 311 and < 311 + Corners => carSlot - 311,
        _ => -1
    };

    /// <summary>The car slots a part of a group goes on, none for parts that do not sit on the car itself</summary>
    public static IEnumerable<int> CarSlotsFor(string group)
    {
        if (group == PartKinds.SwayBars) return [FrontSwayBarSlot, RearSwayBarSlot];
        Func<int, int>? slot = group switch
        {
            PartKinds.Rims => WheelSlot,
            PartKinds.Brakes => BrakeSlot,
            PartKinds.Springs => SpringSlot,
            PartKinds.Shocks => ShockSlot,
            _ => null
        };
        return slot == null ? Enumerable.Empty<int>() : Enumerable.Range(0, Corners).Select(slot);
    }

    /// <summary>The car slots a part goes on: its group's, on the axle it is made for (<see cref="FitsAxle"/>)</summary>
    public static IEnumerable<int> CarSlotsFor(PartDefinition part) =>
        CarSlotsFor(PartKinds.GroupOf(part)).Where(slot => FitsAxle(part, IsSwayBarSlot(slot) ? slot == FrontSwayBarSlot : IsFront(CornerOf(slot))));

    /// <summary>
    /// Whether a part goes on this axle: its <c>fits_axle</c> (<c>"front"</c> or <c>"rear"</c>: a leaf spring the rear, a
    /// torsion bar the front, a sway bar the one it was bent for); a part that says none goes on either
    /// </summary>
    public static bool FitsAxle(PartDefinition part, bool front) =>
        Text(part, "fits_axle") is not { } axle || string.Equals(axle, front ? "front" : "rear", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The suspension a part needs under it (suspensions.ini TYPE: a leaf spring an <c>AXLE</c>, a torsion bar a
    /// <c>DWB</c>), its <c>fits_suspension</c>; null for one that goes on any
    /// </summary>
    public static string? NeedsSuspension(PartDefinition part) => Text(part, "fits_suspension");

    public static bool IsRunningGear(string group) =>
        group is PartKinds.Rims or PartKinds.Tyres or PartKinds.Brakes or PartKinds.Springs or PartKinds.Shocks or PartKinds.SwayBars;

    /// <summary>The sway bars, front and rear: on the car's own slots, the rear one on the rear axle's when it is there</summary>
    public static (PartInstance? Front, PartInstance? Rear) SwayBars(List<PartInstance> carParts) =>
        (carParts.FirstOrDefault(p => p.ParentSlot == FrontSwayBarSlot),
         carParts.FirstOrDefault(p => p.ParentSlot == RearSwayBarSlot) ?? RearAxle.CornerParts(carParts).FirstOrDefault(c => c.CarSlot == RearSwayBarSlot).Part);

    /// <summary>The tyre script's own fit check: same rim diameter, rim width within what the tyre takes</summary>
    public static bool TyreFitsRim(PartDefinition tyre, PartDefinition rim)
    {
        var tyreRim = tyre.Number("wheel_radius");             // mm
        var rimRadius = rim.Number("wheel_radius") * 1000;     // m
        if (Math.Abs(tyreRim - rimRadius) > 0.5) return false;

        var width = rim.Number("rim_width");
        var (min, max) = (tyre.Number("minRimWidth"), tyre.Number("maxRimWidth"));
        return (min <= 0 || width >= min - 0.01) && (max <= 0 || width <= max + 0.01);
    }

    /// <summary>
    /// The parts on the car's wheel slots, by corner; a rear brake, spring or shock on the rear axle's housing
    /// (<see cref="RearAxle.CornerParts"/>) as if on the car's own slot it stands for (one on the car's own slot first)
    /// </summary>
    public static CornerParts[] Mounted(List<PartInstance> carParts)
    {
        var corners = Enumerable.Range(0, Corners).Select(_ => new CornerParts()).ToArray();
        foreach (var (part, carSlot) in RearAxle.CornerParts(carParts).Concat(carParts.Select(p => (p, p.ParentSlot))))
        {
            var corner = CornerOf(carSlot);
            if (corner < 0) continue;

            var slot = carSlot;
            if (IsWheelSlot(slot))
            {
                corners[corner].Rim = part;
                corners[corner].Tyre = part.Children.FirstOrDefault(c => c.ParentSlot == TyreSlotOnRim);
            }
            else if (slot == BrakeSlot(corner)) corners[corner].Brake = part;
            else if (slot == SpringSlot(corner)) corners[corner].Spring = part;
            else if (slot == ShockSlot(corner)) corners[corner].Shock = part;
        }

        return corners;
    }

    // ----- what the parts come to, in the scripts' formulas -----

    /// <summary>Brake torque at full pedal, Nm: clamp force over the calipers times pad on disc at the disc's radius, less with wear</summary>
    public static double BrakeTorque(PartDefinition brake, double wear = 1)
    {
        var calipers = Math.Max(1, brake.Number("number_of_calipers", 1));
        var torque = brake.Number("force") * (1 + (calipers - 1) * 0.333) * brake.Number("friction", 1) * brake.Number("radius");
        return (0.2 + Math.Sqrt(Math.Clamp(wear, 0, 1)) * 0.8) * torque;
    }

    /// <summary>The slots of a drum brake: its shoes and its wheel cylinder (step 31)</summary>
    public const int ShoesSlot = 2;
    public const int WheelCylinderSlot = 3;

    /// <summary>
    /// A brake's torque as it is put together: its own figures, its shoes' friction (<c>friction</c>, times the drum's)
    /// and its wheel cylinder's force (<c>force_factor</c>) when it has them, less with the lining's wear (the shoes', else
    /// the brake's own)
    /// </summary>
    public static double BrakeTorque(PartDefinition brake, PartDefinition? shoes, PartDefinition? cylinder, double wear = 1) =>
        BrakeTorque(brake, wear) * (shoes?.Number("friction", 1) ?? 1) * (cylinder?.Number("force_factor", 1) ?? 1);

    /// <summary>A mounted brake's torque, its shoes and wheel cylinder read off the parts on it</summary>
    public static double BrakeTorque(PartsCatalog catalog, PartInstance brake)
    {
        if (catalog.Get(brake.DefinitionId) is not { } definition) return 0;
        var shoes = brake.Children.FirstOrDefault(c => c.ParentSlot == ShoesSlot);
        var cylinder = brake.Children.FirstOrDefault(c => c.ParentSlot == WheelCylinderSlot);
        return BrakeTorque(definition, shoes == null ? null : catalog.Get(shoes.DefinitionId), cylinder == null ? null : catalog.Get(cylinder.DefinitionId),
            (shoes ?? brake).Wear);
    }

    /// <summary>
    /// How a brake heats and fades, as Assetto Corsa's brakes.ini [TEMPS_FRONT]/[TEMPS_REAR] has it: °C per kJ of braking
    /// (<c>torque_k</c>), cooling standing (<c>cool_transfer</c>) and with speed (<c>cool_speed_factor</c>), and the lining's
    /// torque by temperature (<c>perf_curve</c>, the shoes', else the brake's own). Null for a brake that says none: the
    /// source game's discs, which leave the car's own as they are.
    /// </summary>
    public static BrakeHeat? Heat(PartDefinition brake, PartDefinition? shoes)
    {
        var k = brake.Number("torque_k");
        var curve = Curve(shoes) ?? Curve(brake);
        if (k <= 0 || !double.IsFinite(k) || curve == null) return null;
        return new BrakeHeat(k, Math.Max(0, brake.Number("cool_transfer")), Math.Max(0, brake.Number("cool_speed_factor")), curve);
    }

    /// <summary>A mounted brake's heat, its shoes read off the part on it</summary>
    public static BrakeHeat? Heat(PartsCatalog catalog, PartInstance brake) =>
        catalog.Get(brake.DefinitionId) is { } definition
            ? Heat(definition, brake.Children.FirstOrDefault(c => c.ParentSlot == ShoesSlot) is { } shoes ? catalog.Get(shoes.DefinitionId) : null)
            : null;

    /// <summary>A part's <c>perf_curve</c>, an inline lut <c>(|0=0.9|300=1.0|)</c>; null when it has none, or not one of those</summary>
    private static string? Curve(PartDefinition? part) =>
        part?.Properties.GetValueOrDefault("perf_curve") is string curve && System.Text.RegularExpressions.Regex.IsMatch(curve.Trim(),
            @"^\(\|(-?\d+(\.\d+)?=-?\d+(\.\d+)?\|)+\)$") ? curve.Trim() : null;

    /// <summary>Wheel rate, N/m</summary>
    public static double SpringRate(PartDefinition spring) => spring.Number("force");

    /// <summary>
    /// How much a leaf spring winds up under the axle's torque (<c>wrap</c>: a multi-leaf 1, a mono-leaf more, a Super Stock
    /// less); 0 for a spring that says none (a coil, whose links hold the axle)
    /// </summary>
    public static double Wrap(PartDefinition spring) => spring.Number("wrap");

    /// <summary>What a traction bar leaves of a leaf spring's wrap (<c>wrap_factor</c>: a ladder bar a quarter); 1 for none</summary>
    public static double WrapFactor(PartDefinition? bar) => bar?.Number("wrap_factor", 1) is > 0 and var factor ? factor : 1;

    /// <summary>A mounted spring's wrap with the traction bar on it; 0 for a spring that does not wind up</summary>
    public static double Wrap(PartsCatalog catalog, PartInstance spring) =>
        catalog.Get(spring.DefinitionId) is { } definition
            ? Wrap(definition) * WrapFactor(spring.Children.FirstOrDefault(c => c.ParentSlot == TractionBarSlot) is { } bar ? catalog.Get(bar.DefinitionId) : null)
            : 0;

    /// <summary>
    /// A sway bar's roll stiffness, N/m as Assetto Corsa's [ARB] counts it (<c>arb_rate</c>); 0 for one that says none (the
    /// source game's, which do nothing in it)
    /// </summary>
    public static double SwayBarRate(PartDefinition bar) => bar.Number("arb_rate");

    /// <summary>Whether a part is a sway bar that does something: the source game's have no rate and stay off the shelves</summary>
    public static bool IsWorkingSwayBar(PartDefinition part) => PartKinds.GroupOf(part) == PartKinds.SwayBars && SwayBarRate(part) > 0;

    /// <summary>The load the spring was made for, kg on the wheel</summary>
    public static double SpringDesignLoad(PartDefinition spring) => spring.Number("designedMassOnWheel");

    /// <summary>Bump and rebound damping, N per m/s; the shock's rebound follows what is inside it</summary>
    public static (double Bump, double Rebound) Damping(PartDefinition shock)
    {
        var bump = shock.Number("damping");
        return (bump, bump * shock.Number("rebound_factor", 1));
    }

    /// <summary>Tread width in metres</summary>
    public static double TyreWidth(PartDefinition tyre) => tyre.Number("tyre_width") / 1000;

    /// <summary>Rolling radius in metres</summary>
    public static double TyreRadius(PartDefinition tyre) => tyre.Number("radius");

    /// <summary>Rim seat radius in metres</summary>
    public static double TyreRimRadius(PartDefinition tyre) => tyre.Number("wheel_radius") / 1000;

    /// <summary>Peak friction the tyre offers, a little less as it wears down to the cords</summary>
    public static double TyreGrip(PartDefinition tyre, double wear = 1) => tyre.Number("friction") * (0.85 + 0.15 * Math.Clamp(wear, 0, 1));

    public static double TyreLoadCapacity(PartDefinition tyre) => tyre.Number("loadcap");

    public static double TyreRollingResistance(PartDefinition tyre) => tyre.Number("rollres");

    /// <summary>Recommended inflation, bar</summary>
    public static double TyrePressure(PartDefinition tyre) => tyre.Number("optimal_inflation", 2);

    /// <summary>Wheel offset in metres; negative moves the wheel outwards</summary>
    public static double RimOffset(PartDefinition rim) => rim.Number("offset");

    public static double RimWidth(PartDefinition rim) => rim.Number("rim_width");

    /// <summary>A text property of a part, trimmed; null when it has none</summary>
    private static string? Text(PartDefinition part, string property) =>
        part.Properties.GetValueOrDefault(property) is string text && text.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
