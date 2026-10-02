namespace StreetCorsa.Rules;

/// <summary>
/// The AI the player may connect (<c>"ai"</c> in the rules): how long the game waits for it, how often it is asked, and
/// what it is asked to write. The game never needs it: what it says without one is always there, and a line the AI has
/// not written in time is the game's own.
/// </summary>
public sealed class AiRules
{
    /// <summary>Seconds a rival's line waits for the AI when none was written ahead; the game's own line after that</summary>
    public double TalkWaitSeconds { get; set; } = 1.5;

    /// <summary>Seconds one request may take before it is given up (a local model loading its weights is slow the first time)</summary>
    public double RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>Seconds a ComfyUI picture may take, from the queue to the file</summary>
    public double ImageTimeoutSeconds { get; set; } = 240;

    /// <summary>Requests to the text provider at once (1 or 2: a local model does one at a time anyway)</summary>
    public int MaxConcurrent { get; set; } = 1;

    /// <summary>Seconds at least between two requests, so a busy day never floods a paid API</summary>
    public double MinSecondsBetween { get; set; } = 0.5;

    /// <summary>Requests waiting at most; past that a new background one is dropped (the game's own words stand)</summary>
    public int MaxQueued { get; set; } = 24;

    /// <summary>Seconds the AI is left alone after it could not be reached, before it is tried again</summary>
    public double RetryAfterFailureSeconds { get; set; } = 60;

    /// <summary>Lines a rival keeps written ahead per moment (greeting, track picked, pink slips...)</summary>
    public int TalkLinesAhead { get; set; } = 1;

    /// <summary>The chance a new line of the diner's street talk is told again in the AI's words</summary>
    public double StreetTalkChance { get; set; } = 1.0;

    /// <summary>The chance a new piece in the paper is written again by the AI</summary>
    public double NewsChance { get; set; } = 1.0;

    /// <summary>A new face on the street gets a portrait painted by ComfyUI, when it is on</summary>
    public bool PaintNewFaces { get; set; } = true;

    /// <summary>The AI's words kept in a save at most (dealers' takes, ads); the oldest go</summary>
    public int MaxKept { get; set; } = 400;

    /// <summary>Talking to people: what they are given to know, how long they remember, how they haggle</summary>
    public TalkRules Talk { get; set; } = new();

    /// <summary>What is sent to Ollama besides the prompt (the game's model is made in Ollama from <c>ai/ollama.json</c>)</summary>
    public OllamaRules Ollama { get; set; } = new();
}

/// <summary>
/// Talking to people (<c>"ai": { "talk": ... }</c>): the player's AI speaks for them, from what the game tells it.
/// </summary>
public sealed class TalkRules
{
    /// <summary>Turns of a talk sent with each request, the newest; the older ones are left out</summary>
    public int TurnsKept { get; set; } = 16;

    /// <summary>What a person remembers of past talks, the newest few, told them when they are talked to again</summary>
    public int MemoriesGiven { get; set; } = 4;

    /// <summary>Days a private seller remembers a talk</summary>
    public int ForgetDaysSeller { get; set; } = 14;

    /// <summary>Days a dealer's salesman remembers a talk</summary>
    public int ForgetDaysSalesman { get; set; } = 30;

    /// <summary>Days a rival remembers a talk: the street remembers longer</summary>
    public int ForgetDaysRival { get; set; } = 90;

    /// <summary>The player's races with a person told them, the newest</summary>
    public int RacesTold { get; set; } = 6;

    /// <summary>A rival's races with other rivals told them, the newest</summary>
    public int RivalRacesTold { get; set; } = 5;

    /// <summary>Races between rivals kept in the save at most, the oldest dropped</summary>
    public int RivalRacesKept { get; set; } = 300;

    /// <summary>What a salesman said about a car kept, so he does not say it again</summary>
    public int SaidKept { get; set; } = 3;

    /// <summary>Seconds a car must stay picked on the lot before the salesman is asked about it (picking quickly asks for the last)</summary>
    public double PitchDelaySeconds { get; set; } = 0.8;

    /// <summary>The most a private seller comes down from the price first asked (0.15: 15 percent)</summary>
    public double DiscountPrivate { get; set; } = 0.15;

    /// <summary>The most a salesman comes down</summary>
    public double DiscountSalesman { get; set; } = 0.12;

    /// <summary>The most a rival comes down on their own car or part</summary>
    public double DiscountRival { get; set; } = 0.08;

    /// <summary>A broke rival needs the money: the most they come down, times this</summary>
    public double BrokeRivalFactor { get; set; } = 1.5;

    /// <summary>Days on sale past which a seller comes down further</summary>
    public int LongOnSaleDays { get; set; } = 14;

    /// <summary>What more a seller comes down when the thing has been on sale that long</summary>
    public double LongOnSaleExtra { get; set; } = 0.05;

    /// <summary>An offer this far under what the seller would take is an insult: turned down flat</summary>
    public double InsultMargin { get; set; } = 0.25;

    /// <summary>Counter-offers a seller makes on one thing; after that they stand firm</summary>
    public int MaxCounters { get; set; } = 3;

    /// <summary>A car's price comes down in steps of this</summary>
    public int CarPriceStep { get; set; } = 25;

    /// <summary>A part's price of 100 or more comes down in steps of this (in whole money below that)</summary>
    public int PartPriceStep { get; set; } = 5;

    /// <summary>A number the player types is an offer only from this share of the price...</summary>
    public double OfferLowest { get; set; } = 0.2;

    /// <summary>...up to this share of it: a year or a mileage is no offer</summary>
    public double OfferHighest { get; set; } = 1.5;

    /// <summary>
    /// The hard floor: the most anybody ever comes down from the price a thing was first offered at (0.25: never under 75
    /// percent of it), whatever their temperament, the time on sale, and however often the player asks
    /// </summary>
    public double MostDiscount { get; set; } = 0.25;

    /// <summary>A salesman's cars told him, the dearest first</summary>
    public int LotCarsTold { get; set; } = 10;

    /// <summary>The share of salesmen who are women</summary>
    public double WomenSalesmen { get; set; } = 0.15;

    /// <summary>The share of private sellers who are women</summary>
    public double WomenSellers { get; set; } = 0.3;

    public int SalesmanMinAge { get; set; } = 28;

    public int SalesmanMaxAge { get; set; } = 57;

    public int SellerMinAge { get; set; } = 20;

    public int SellerMaxAge { get; set; } = 74;
}

/// <summary>What the game sends Ollama besides the prompt (<c>"ai": { "ollama": ... }</c>)</summary>
public sealed class OllamaRules
{
    /// <summary>The context window asked for (tokens): a talk with its fact sheet needs more than Ollama's usual</summary>
    public int ContextTokens { get; set; } = 8192;

    /// <summary>How long Ollama keeps the model loaded after a request ("30m"), so the next line does not wait for it to load</summary>
    public string KeepAlive { get; set; } = "30m";

    /// <summary>How much the model is kept from saying the same words again (1: not at all)</summary>
    public double RepeatPenalty { get; set; } = 1.1;
}
