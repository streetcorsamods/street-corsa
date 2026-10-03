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
    public int TurnsKept { get; set; } = 40;

    /// <summary>
    /// Turns that have fallen out of the ones sent, at least, before they are summed up (an AI request, in the background)
    /// in a few sentences that are sent with the facts instead: a long talk is not forgotten from its start
    /// </summary>
    public int SummaryTurns { get; set; } = 6;

    /// <summary>
    /// The most the fact sheet may take (tokens, as the game guesses them), and never more than half the context window
    /// when the game sets one: past it, the least needed facts are left out first (other racers' races, the rest of the
    /// lot, old races, old memories), never who the person is, the thing talked about or the deal
    /// </summary>
    public int FactTokens { get; set; } = 5000;

    /// <summary>What a person remembers of past talks, the newest few, told them when they are talked to again</summary>
    public int MemoriesGiven { get; set; } = 10;

    /// <summary>What happened between a person and the player (a deal, a race) kept with them at most, the oldest dropped</summary>
    public int EventsKept { get; set; } = 12;

    // How a person's feeling toward the player (-2..+2, the sum of what they still remember) moves with each thing that
    // happened: it changes how they talk, never a price

    /// <summary>An offer far too low, or a buyer pushed until they walked</summary>
    public int FeelingInsulted { get; set; } = -1;

    /// <summary>The player bought from them</summary>
    public int FeelingBought { get; set; } = 1;

    /// <summary>The player sold to them</summary>
    public int FeelingSold { get; set; } = 1;

    /// <summary>The player beat them in a race</summary>
    public int FeelingBeaten { get; set; } = -1;

    /// <summary>They beat the player</summary>
    public int FeelingBeatPlayer { get; set; } = 0;

    /// <summary>The player won their car for pink slips (on top of the race lost)</summary>
    public int FeelingPinkLost { get; set; } = -2;

    /// <summary>They won the player's car (on top of the race won)</summary>
    public int FeelingPinkWon { get; set; } = 1;

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
    public double OfferHighest { get; set; } = 3;

    /// <summary>
    /// The hard floor: the most anybody ever comes down from the price a thing was first offered at (0.25: never under 75
    /// percent of it), whatever their temperament, the time on sale, and however often the player asks. Each thing's
    /// seller draws their own limit under it once (none at all for <see cref="FirmShare"/> of them), kept with the ad.
    /// </summary>
    public double MostDiscount { get; set; } = 0.25;

    /// <summary>The share of sellers who will not come down at all on a thing (and of buyers who will not go up)</summary>
    public double FirmShare { get; set; } = 0.2;

    /// <summary>
    /// The hard ceiling: the most a buyer who answered the player's ad ever comes up from what they first offered (0.12:
    /// never over 112 percent of it, nor over what the ad asks), however often the player asks. Each buyer draws their own
    /// limit under it once (none at all for <see cref="FirmShare"/> of them), kept with the offer. Asked for more than
    /// <see cref="InsultMargin"/> over their limit, or pushed past <see cref="MaxCounters"/>, they walk away.
    /// </summary>
    public double MostRaise { get; set; } = 0.12;

    /// <summary>A salesman's cars told him, the dearest first, each with its engine, miles and condition</summary>
    public int LotCarsTold { get; set; } = 30;

    /// <summary>
    /// Different parts of one kind told one by one in a car's build; a kind of more (an engine's small accessories) is
    /// told as one line, but for the parts not from the factory
    /// </summary>
    public int BuildPartsPerKind { get; set; } = 3;

    /// <summary>Other cars for sale in town told a seller or buyer, the most like the one talked about first</summary>
    public int MarketCarsTold { get; set; } = 4;

    /// <summary>What is said around town (the diner's street talk) told everybody, the newest</summary>
    public int StreetTalkTold { get; set; } = 6;

    /// <summary>Pieces in the paper told everybody, the newest</summary>
    public int NewsTold { get; set; } = 3;

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
    /// <summary>
    /// The context window asked for (tokens): a talk with its fact sheet needs more than Ollama's usual. 16k costs an 8B
    /// model about 1 GB of the card over 8k, and fits beside the game on 12 GB; past it the model spills off the GPU
    /// </summary>
    public int ContextTokens { get; set; } = 16384;

    /// <summary>
    /// How long Ollama keeps the model loaded after a request ("10m"), so the next line does not wait for it to load; not
    /// so long that it holds the GPU through the race that follows
    /// </summary>
    public string KeepAlive { get; set; } = "10m";

    /// <summary>How much the model is kept from saying the same words again (1: not at all)</summary>
    public double RepeatPenalty { get; set; } = 1.1;
}
