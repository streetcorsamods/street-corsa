using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Parts.Cars;

/// <summary>What a gas station charges: a litre of gas, a litre of oil, a wash, and the tow truck that brings a dry car in</summary>
public sealed record StationPrices(decimal FuelPerLitre, decimal OilPerLitre, decimal Wash, decimal Tow)
{
    /// <summary>
    /// 1970: premium about 40 cents a gallon, a quart of oil about 60 cents, a wash three dollars, a tow across town
    /// twenty-five
    /// </summary>
    public static StationPrices Default { get; } = new(0.11m, 0.65m, 3m, 25m);
}

/// <summary>
/// The gas station: where a car is filled up, gets its oil topped up and is washed (<see cref="Jobs"/>). The drive there
/// takes a little of what is in the tank; a car without that much is towed in (<see cref="NeedsTow"/>).
/// </summary>
public static class GasStation
{
    /// <summary>The jobs by id (<see cref="RepairJob.Id"/>)</summary>
    public const string FillUp = "fill_up";
    public const string TopUpOil = "top_up_oil";
    public const string Wash = "wash";

    /// <summary>
    /// How long a job takes to watch, real seconds (its game time is <see cref="RepairJob.Time"/>): the pump runs at
    /// about nine litres a second, three seconds at least and eight at most; the oil goes in in two and a half, the wash
    /// takes four
    /// </summary>
    public static double WatchSeconds(string job, double litres) => job switch
    {
        FillUp => Math.Clamp(litres / 9, 3, 8),
        TopUpOil => 2.5,
        _ => 4
    };

    /// <summary>What the drive to the station takes from the tank, litres</summary>
    public const double TripLitres = 0.8;

    /// <summary>A body this dirty is worth a wash</summary>
    public const double DirtyFrom = 0.1;

    /// <summary>A tank this short of full is worth filling</summary>
    private const double FillFromLitres = 0.5;

    /// <summary>A sump this short of full is worth topping up</summary>
    private const double TopUpFrom = 0.05;

    /// <summary>What a sump holds when its pan does not say, litres: a V8 of the day took five quarts</summary>
    public const double DefaultSumpLitres = 4.7;

    private const double LitresPerQuart = 0.946;

    /// <summary>The car has too little in the tank to make it to the station: the tow truck brings it</summary>
    public static bool NeedsTow(Car car) => CarCondition.FuelLeft(car) is { } fuel && fuel < TripLitres;

    /// <summary>The drive there, from the tank; a car towed in goes as it was</summary>
    public static void Drive(Car car)
    {
        if (CarCondition.FuelLeft(car) is { } fuel) car.FuelLitres = Math.Max(0, fuel - TripLitres);
    }

    /// <summary>What the engine's sump holds, litres: its oil pan's capacity, or a V8's of the day</summary>
    public static double SumpLitres(Car car, PartsCatalog? catalog)
    {
        var pan = catalog == null || car.Engine == null ? null
            : car.Engine.SelfAndDescendants().Select(p => catalog.Get(p.DefinitionId)).FirstOrDefault(d => d != null && PartKinds.GroupOf(d) == EngineCooling.OilPansGroup);
        return pan?.Number("capacity") is > 1 and < 20 and var capacity ? capacity : DefaultSumpLitres;
    }

    /// <summary>The oil the engine is short of, litres</summary>
    public static double OilToTopUp(Car car, PartsCatalog? catalog) =>
        car.Engine == null ? 0 : (1 - CarCondition.OilLeft(car)) * SumpLitres(car, catalog);

    /// <summary>"a quart", "two quarts": the oil short, as the attendant reads it off the dipstick</summary>
    public static string Quarts(double litres)
    {
        var quarts = Math.Max(1, (int)Math.Round(litres / LitresPerQuart));
        return quarts switch
        {
            1 => PartWords.Say("station.quarts.1"),
            2 => PartWords.Say("station.quarts.2"),
            3 => PartWords.Say("station.quarts.3"),
            4 => PartWords.Say("station.quarts.4"),
            _ => PartWords.Say("station.quarts.many", ("quarts", quarts))
        };
    }

    /// <param name="catalog">For the oil pan's size; null takes a V8's of the day</param>
    /// <param name="priceMultiplier">The save's <see cref="GameRules.PartPriceMultiplier"/>, on the whole bill</param>
    public static List<RepairJob> Jobs(Car car, PartsCatalog? catalog, StationPrices prices, double priceMultiplier = 1.0)
    {
        var multiplier = GameRules.Sane(priceMultiplier);
        var jobs = new List<RepairJob>();

        var litres = CarCondition.LitresToFill(car);
        if (litres >= FillFromLitres)
        {
            jobs.Add(new RepairJob(PartWords.Say("station.job.fill_up"), PartWords.Say("station.job.fill_up.detail", ("left", CarCondition.FuelLeft(car)), ("tank", car.FuelTankLitres)),
                PartPricing.Round((double)prices.FuelPerLitre * litres * multiplier), GameAction.GarageChore, () => car.FuelLitres = null, isChore: true, id: FillUp));
        }

        if (car.Engine != null && 1 - CarCondition.OilLeft(car) >= TopUpFrom)
        {
            var oil = OilToTopUp(car, catalog);
            var detail = PartWords.Say(CarCondition.OilLeft(car) < 0.4 ? "station.job.oil.way_down" : "station.job.oil.low", ("quarts", Quarts(oil)));
            jobs.Add(new RepairJob(PartWords.Say("station.job.oil"), char.ToUpperInvariant(detail[0]) + detail[1..],
                Math.Max(PartPricing.Round((double)prices.OilPerLitre * LitresPerQuart * multiplier), PartPricing.Round((double)prices.OilPerLitre * oil * multiplier)),
                GameAction.GarageChore, () => car.OilLevel = null, isChore: true, id: TopUpOil));
        }

        if (car.BodyDirt >= DirtyFrom)
        {
            jobs.Add(new RepairJob(PartWords.Say("station.job.wash"), PartWords.Say(car.BodyDirt >= 0.5 ? "station.job.wash.filthy" : "station.job.wash.dusty"), PartPricing.Round((double)prices.Wash * multiplier),
                GameAction.GarageChore, () => car.BodyDirt = 0, isChore: true, id: Wash));
        }

        return jobs;
    }
}
