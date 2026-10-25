namespace StreetCorsa.Rules;

/// <summary>
/// The rivals' lives (<c>"rivals"</c> in the rules): how many race at once, their money and cars, trading up, going
/// broke, leaving and coming back, and selling their cars on
/// </summary>
public sealed class RivalRules
{
    /// <summary>The money a new rival starts with</summary>
    public RivalMoneyRules Money { get; set; } = new();

    /// <summary>A boss: what he has and the record he comes with, where his own racer data says nothing</summary>
    public BossRules Boss { get; set; } = new();

    /// <summary>Racers on the street when a career starts</summary>
    public int BaseActiveRacers { get; set; } = 6;

    /// <summary>More racers turn up every week</summary>
    public int ActiveRacersPerWeek { get; set; } = 2;

    /// <summary>What counts as broke when there is no car for sale to measure against</summary>
    public decimal BankruptFloor { get; set; } = 200m;

    /// <summary>
    /// What a broke racer scrapes together (a loan, a pawned watch, a second job), as a share of the cheapest car
    /// for sale: a few of those and they can buy one
    /// </summary>
    public double MinCashShare { get; set; } = 0.1;

    public double MaxCashShare { get; set; } = 0.6;

    /// <summary>The same when nothing is for sale (the GameMaker version's $50–500)</summary>
    public int MinCashInjection { get; set; } = 50;

    public int MaxCashInjection { get; set; } = 500;

    /// <summary>A racer keeps a car to race and one spare; the rest go to a dealer</summary>
    public int MaxCars { get; set; } = 2;

    /// <summary>
    /// Money a racer keeps back from tuning, for the bets and the repairs: this share of what their car is worth,
    /// so it follows the prices
    /// </summary>
    public decimal TuningReserveShare { get; set; } = 0.25m;

    /// <summary>Share of the money over the reserve a racer spends on parts in a day</summary>
    public decimal TuningShare { get; set; } = 0.3m;

    /// <summary>The chance a racer on the street sells up and leaves town on a given day: one in five hundred, so most stay well over a year</summary>
    public double LeaveChance { get; set; } = 0.002;

    /// <summary>Going broke this many times, they give up instead of scraping money together once more</summary>
    public int MaxTimesBroke { get; set; } = 3;

    /// <summary>A racer sitting out this long starts thinking of giving up</summary>
    public int LaidUpDays { get; set; } = 45;

    /// <summary>The chance, each day after <see cref="LaidUpDays"/>, that a racer sitting out gives up</summary>
    public double LaidUpLeaveChance { get; set; } = 0.05;

    /// <summary>
    /// When nobody new is left to come out, a racer who left this long ago can come back to town, with fresh money
    /// </summary>
    public int ComeBackAfterDays { get; set; } = 90;

    /// <summary>The chance a racer with money to spare goes looking for a better car on a given day</summary>
    public double TradeUpChance { get; set; } = 0.05;

    /// <summary>A better car is one with this much more power: less is not worth the trouble of a sale</summary>
    public double TradeUpGain { get; set; } = 1.2;

    /// <summary>
    /// The chance a racer with money goes round a dealer's closing-down sale on a given day: far more than they look
    /// for a better car (<see cref="TradeUpChance"/>), because a bargain does not wait
    /// </summary>
    public double BargainChance { get; set; } = 0.35;

    /// <summary>At most this many cars go off the sale lots to the rivals in a day: the player still gets a look</summary>
    public int MaxBargainsPerDay { get; set; } = 2;

    /// <summary>At a sale price a smaller step up is worth taking than <see cref="TradeUpGain"/></summary>
    public double BargainTradeUpGain { get; set; } = 1.1;

    /// <summary>What a flipped car has to make in the paper over what it cost: less is not worth the bother</summary>
    public decimal FlipMargin { get; set; } = 1.05m;

    /// <summary>What a rival asks for a car in the paper, as a share of the lot price: under what a lot asks, over what a dealer pays</summary>
    public double MinAskShare { get; set; } = 0.75;

    public double MaxAskShare { get; set; } = 0.9;

    /// <summary>After this many days an ad nobody answered comes down in price, once, by <see cref="AskReduction"/></summary>
    public int AskReducedAfterDays { get; set; } = 7;

    public decimal AskReduction { get; set; } = 0.9m;

    /// <summary>At most this many racers tune their engines in a day (each is a few dozen dyno runs)</summary>
    public int MaxTunersPerDay { get; set; } = 3;

    /// <summary>The chance a racer with money to spare works on the engine on a given day: now and then, not daily</summary>
    public double TuneChance { get; set; } = 0.15;

    /// <summary>The same while a shop the tuners buy at holds its closing-down sale: the racers go after the bargains</summary>
    public double SaleTuneChance { get; set; } = 0.3;

    /// <summary>How long the street keeps talking about something, and how much it remembers at once</summary>
    public int StreetTalkDays { get; set; } = 7;

    public int MaxStreetTalk { get; set; } = 40;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(BaseActiveRacers), 0, 1000, problems);
        Figures.Within(this, path, nameof(ActiveRacersPerWeek), 0, 1000, problems);
        Figures.Within(this, path, nameof(MaxCars), 1, 100, problems);
        Figures.Within(this, path, nameof(MaxBargainsPerDay), 0, 100, problems);
        Figures.Within(this, path, nameof(MaxTimesBroke), 0, 1000, problems);
        Figures.Within(this, path, nameof(MaxTunersPerDay), 0, 1000, problems);
        Figures.Within(this, path, nameof(TuneChance), 0, 1, problems);
        Figures.Within(this, path, nameof(SaleTuneChance), 0, 1, problems);
        Figures.Within(this, path, nameof(StreetTalkDays), 0, 3650, problems);
        Figures.Within(this, path, nameof(MaxStreetTalk), 0, 100_000, problems);
        Figures.Money(this, path, problems, 10_000_000, nameof(BankruptFloor), nameof(MinCashInjection), nameof(MaxCashInjection));
        Figures.Within(this, path, nameof(TuningReserveShare), 0, 1, problems);
        Figures.Within(this, path, nameof(TuningShare), 0, 1, problems);
        Figures.Within(this, path, nameof(FlipMargin), 0, 10, problems);
        Figures.Within(this, path, nameof(AskReduction), 0, 1, problems);
        Money.Check($"{path}.money", problems);
        Boss.Check($"{path}.boss", problems);
    }
}

/// <summary>What a new rival has in the bank: a base, more for skill, age and reputation, give or take a little</summary>
public sealed class RivalMoneyRules
{
    /// <summary>The base, somewhere between these</summary>
    public decimal BaseLow { get; set; } = 2000m;

    public decimal BaseHigh { get; set; } = 5000m;

    /// <summary>For each point of skill over the least (the least skilled racer counts 10, the most 20)</summary>
    public decimal PerSkill { get; set; } = 150m;

    /// <summary>For age, by band: the first band whose <c>under</c> the racer is younger than, else the last</summary>
    public RivalMoneyByAge[] ByAge { get; set; } =
    [
        new() { Under = 26, Low = 0m, High = 500m },
        new() { Under = 36, Low = 500m, High = 1500m },
        new() { Under = 46, Low = 1500m, High = 2500m },
        new() { Under = 200, Low = 2000m, High = 4000m }
    ];

    /// <summary>For each point of reputation over this</summary>
    public int ReputationFrom { get; set; } = 50;

    public decimal PerReputation { get; set; } = 40m;

    /// <summary>The total, give or take this share</summary>
    public double Spread { get; set; } = 0.15;

    /// <summary>Rounded to this, and never less than the least</summary>
    public decimal RoundTo { get; set; } = 100m;

    public decimal Least { get; set; } = 1000m;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Money(this, path, problems, 10_000_000, nameof(BaseLow), nameof(BaseHigh), nameof(PerSkill), nameof(PerReputation), nameof(Least));
        Figures.Above0(this, path, nameof(RoundTo), 1_000_000, problems);
        for (var i = 0; i < ByAge.Length; i++)
            Figures.Money(ByAge[i], $"{path}.byAge[{i}]", problems, 10_000_000, nameof(RivalMoneyByAge.Low), nameof(RivalMoneyByAge.High));
    }
}

/// <summary>An age band's money: somewhere between low and high for a racer under <see cref="Under"/></summary>
public sealed class RivalMoneyByAge
{
    public int Under { get; set; }

    public decimal Low { get; set; }

    public decimal High { get; set; }
}

/// <summary>A boss's figures; a boss's own in the racers' data go over them</summary>
public sealed class BossRules
{
    /// <summary>What a boss has in the bank</summary>
    public decimal Money { get; set; } = 20000m;

    /// <summary>His car is one of this many strongest from the factory</summary>
    public int CarChoices { get; set; } = 5;

    /// <summary>The condition his car is kept in (0..1): a show car, its looks rolled as a dealer's at that, and washed</summary>
    public double CarCondition { get; set; } = 0.95;

    /// <summary>His record when the career starts: years of wins, pink slips taken</summary>
    public int Races { get; set; } = 52;

    public int Wins { get; set; } = 48;

    public int PinkSlipsWon { get; set; } = 12;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Money(this, path, problems, (double)Parts.Cars.CarBook.MaxCarPrice, nameof(Money));
        Figures.Within(this, path, nameof(CarChoices), 1, 100, problems);
    }
}
