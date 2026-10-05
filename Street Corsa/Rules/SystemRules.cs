namespace StreetCorsa.Rules;

/// <summary>
/// The game's systems a game may do without (<c>"systems"</c> in the rules): each is on in the base game. One that is
/// off is gone from the screens (its tiles, its buttons, the exits to it) and from the nights (its tasks), and what
/// leaned on it is done another way: with no gas station the tank and the oil are topped up overnight (and the car
/// washed); with no career screen the game is won on the rules' path, or on whichever is reached first. A career keeps
/// the switches it was started with, as all the rules.
/// The flow's screens name the system they belong to (<c>flow/*.json</c>, see <c>Navigation.FlowBook</c>).
/// </summary>
public sealed class SystemRules
{
    /// <summary>Police at the races: chases, fines, the impound</summary>
    public bool Police { get; set; } = true;

    /// <summary>The gas station: fuel, oil and the wash bought there; off, they are topped up overnight for free</summary>
    public bool Station { get; set; } = true;

    /// <summary>The newspaper: the classifieds, and the player's own ads</summary>
    public bool Paper { get; set; } = true;

    /// <summary>The car dealers' lots and their map</summary>
    public bool CarDealers { get; set; } = true;

    /// <summary>The part shops and their map</summary>
    public bool PartShops { get; set; } = true;

    /// <summary>The mail order: parts sent for, and selling off the shelf</summary>
    public bool MailOrder { get; set; } = true;

    /// <summary>Cruising the street: rivals met at the curb</summary>
    public bool Street { get; set; } = true;

    /// <summary>The diner: rivals' challenges, pink slips, the King</summary>
    public bool Diner { get; set; } = true;

    /// <summary>The engine stand: an engine pulled goes onto the shelf instead</summary>
    public bool EngineStand { get; set; } = true;

    /// <summary>Talking to people through the player's AI</summary>
    public bool Talk { get; set; } = true;

    /// <summary>The career screen: milestones and picking the victory path</summary>
    public bool Career { get; set; } = true;

    /// <summary>The paint booth: a car sprayed another colour, its mod's or the player's own</summary>
    public bool PaintBooth { get; set; } = true;

    /// <summary>
    /// With no career screen, the victory path the game is won on (a victory's id); none, or one there is not: whichever
    /// the player reaches first
    /// </summary>
    public string CareerPath { get; set; } = string.Empty;

    /// <summary>The names of the switches, as the flow's screens and tiles name them</summary>
    public static readonly IReadOnlyList<string> Names =
        ["police", "station", "paper", "carDealers", "partShops", "mailOrder", "street", "diner", "engineStand", "talk", "career", "paintBooth"];

    /// <summary>Whether a system is on; no name at all is always on, and so is one there is not (the loaders tell it)</summary>
    public bool IsOn(string? system) => system?.ToLowerInvariant() switch
    {
        null or "" => true,
        "police" => Police,
        "station" => Station,
        "paper" => Paper,
        "cardealers" => CarDealers,
        "partshops" => PartShops,
        "mailorder" => MailOrder,
        "street" => Street,
        "diner" => Diner,
        "enginestand" => EngineStand,
        "talk" => Talk,
        "career" => Career,
        "paintbooth" => PaintBooth,
        _ => true
    };

    /// <summary>Whether a name is one of the switches, whatever its case</summary>
    public static bool IsName(string? system) =>
        system != null && Names.Contains(system, StringComparer.OrdinalIgnoreCase);
}
