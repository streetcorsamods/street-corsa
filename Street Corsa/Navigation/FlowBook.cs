using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Rules;

namespace StreetCorsa.Navigation;

/// <summary>
/// How the screens hang together, from the mods' <c>flow/*.json</c> merged key by key in load order (the base game's
/// first, so a mod changes only what it names): the screens by id, each of a kind the program has (<see cref="ScreenKinds"/>),
/// where each one's ways out lead, the home screen, the tiles of the hub screens and the main menu's cards. Read once at
/// start-up, not kept in a save: a mod that fixes its flow fixes every career. Which systems a career has is the rules'
/// (<see cref="SystemRules"/>); a screen of a system that is off is not there, nor are the tiles and exits to it.
/// </summary>
public sealed class FlowBook
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Flow");

    private static readonly Lazy<FlowBook> InstalledBook = new(() => Load(ModSet.Installed));

    /// <summary>The flow of the mods next to the game, read once</summary>
    public static FlowBook Installed => InstalledBook.Value;

    /// <summary>
    /// The screen the game goes to when a game is started or loaded, and wherever a way out leads nowhere: a garage
    /// screen (another kind is told and the first garage taken)
    /// </summary>
    public string Home { get; set; } = "garage";

    /// <summary>The main screen: where the game opens and where it goes when a game is over</summary>
    public string Menu { get; set; } = "main_menu";

    /// <summary>The screens by id</summary>
    public Dictionary<string, FlowScreen> Screens { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tiles of the hub screens: by the hub's screen id, then by the tile's id</summary>
    public Dictionary<string, Dictionary<string, FlowTile>> Tiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The main menu's cards by id</summary>
    public Dictionary<string, FlowCard> Cards { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The screen of this id, or null</summary>
    public FlowScreen? Screen(string? id) => id != null && Screens.TryGetValue(id, out var screen) ? screen : null;

    /// <summary>The kind of the screen of this id, or null</summary>
    public string? KindOf(string? id) => Screen(id)?.Kind;

    /// <summary>Whether the screen is there and its system is on in the rules</summary>
    public bool IsOpen(string? id, SystemRules? systems = null) =>
        Screen(id) is { } screen && (systems ?? RuleBook.Current.Systems).IsOn(screen.System);

    /// <summary>
    /// Where a screen's way out leads: the screen named by its exit when that one is open, else null (the screen hides
    /// the button, or goes home). A screen with no such exit has no such way out.
    /// </summary>
    public string? Exit(string? from, string exit, SystemRules? systems = null) =>
        Screen(from) is { } screen && screen.Exits.TryGetValue(exit, out var to) && IsOpen(to, systems) ? to : null;

    /// <summary>The first screen of this kind, for code that knows only what it wants (the race's screen)</summary>
    public string? FirstOfKind(string kind) =>
        Screens.FirstOrDefault(s => string.Equals(s.Value.Kind, kind, StringComparison.OrdinalIgnoreCase)).Key;

    /// <summary>The tiles a hub screen shows, in their order: those not switched off, whose screen is open and whose systems are</summary>
    public IReadOnlyList<(string Id, FlowTile Tile)> TilesOf(string? hub, SystemRules? systems = null)
    {
        systems ??= RuleBook.Current.Systems;
        if (hub == null || !Tiles.TryGetValue(hub, out var tiles)) return [];
        return tiles
            .Where(t => !t.Value.Off && Shows(t.Value, systems))
            .OrderBy(t => t.Value.Order)
            .ThenBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .Select(t => (t.Key, t.Value))
            .ToList();
    }

    private bool Shows(FlowTile tile, SystemRules systems)
    {
        if (tile.Without.Any(systems.IsOn)) return false;
        if (!systems.IsOn(tile.System)) return false;
        // A tile that goes to a screen is there while the screen is; one that does something the hub does is always
        return Screen(tile.To) == null || IsOpen(tile.To, systems);
    }

    /// <summary>The main menu's cards in their order, those not switched off</summary>
    public IReadOnlyList<(string Id, FlowCard Card)> CardsInOrder() =>
        Cards.Where(c => !c.Value.Off)
            .OrderBy(c => c.Value.Order)
            .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .Select(c => (c.Key, c.Value))
            .ToList();

    /// <param name="problems">Given, what cannot be used is told there instead of to the player (the tests)</param>
    public static FlowBook Load(ModSet mods, List<string>? problems = null)
    {
        var told = problems ?? [];
        var merged = ModMerge.ContentFiles(mods, ContentTypes.Flow, told);
        var book = Read(merged, told);
        if (problems != null) return book;

        foreach (var problem in told) Logger.Warning("Flow: {Problem}", problem);
        ModProblems.Add("Flow", told);
        return book;
    }

    /// <summary>The flow from the mods' merged files, with what cannot be used told in <paramref name="problems"/> and left out</summary>
    public static FlowBook Read(System.Text.Json.Nodes.JsonObject merged, List<string> problems)
    {
        FlowBook book;
        try
        {
            book = merged.Deserialize<FlowBook>(RuleBook.Options) ?? new FlowBook();
            RuleBook.CheckNames(merged, typeof(FlowBook), "flow", problems);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            problems.Add($"the flow could not be read ({ex.Message}); the game has no screens to go to");
            book = new FlowBook();
        }

        return book.Checked(problems);
    }

    private FlowBook Checked(List<string> problems)
    {
        Screens = new Dictionary<string, FlowScreen>(
            (Screens ?? []).Where(s => s.Value != null), StringComparer.OrdinalIgnoreCase);

        // Screens a mod took off: the ways and the tiles to them go quietly, as they were meant to
        var off = new HashSet<string>(Screens.Where(s => s.Value.Off).Select(s => s.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var (id, screen) in Screens.ToList())
        {
            if (screen.Off)
            {
                Screens.Remove(id);
                continue;
            }

            if (!ScreenKinds.IsKind(screen.Kind))
            {
                problems.Add($"screens.{id}: \"{screen.Kind}\" is no kind of screen the game has ({string.Join(", ", ScreenKinds.All)}); left out");
                Screens.Remove(id);
                continue;
            }

            screen.System ??= ScreenKinds.SystemOf(screen.Kind);
            if (screen.EndDay && !ScreenKinds.CanEndDay(screen.Kind))
            {
                problems.Add($"screens.{id}: the day cannot be ended from a {screen.Kind} screen; endDay passed over");
                screen.EndDay = false;
            }

            if (screen.System is { Length: > 0 } system && !SystemRules.IsName(system))
            {
                problems.Add($"screens.{id}: \"{system}\" is no system ({string.Join(", ", SystemRules.Names)}); taken as none");
                screen.System = null;
            }

            screen.Exits = new Dictionary<string, string>(
                (screen.Exits ?? []).Where(e => !string.IsNullOrWhiteSpace(e.Value)), StringComparer.OrdinalIgnoreCase);
        }

        foreach (var (id, screen) in Screens)
        foreach (var (exit, to) in screen.Exits.ToList())
        {
            if (Screens.ContainsKey(to)) continue;
            if (!off.Contains(to)) problems.Add($"screens.{id}.exits.{exit}: there is no screen \"{to}\"; the way out goes home");
            screen.Exits.Remove(exit);
        }

        // Home is a garage: it is where the night is ended, a car towed to and a game started, and where the screens
        // that cannot keep the player send them; any other kind would send the player home from home, or end no day
        if (string.IsNullOrWhiteSpace(Home) || KindOf(Home) != ScreenKinds.Garage)
        {
            var garage = FirstOfKind(ScreenKinds.Garage);
            problems.Add(Screens.ContainsKey(Home ?? string.Empty)
                ? $"home \"{Home}\" is a {KindOf(Home)} screen, and home must be a garage; {(garage ?? "none")} is home"
                : $"home \"{Home}\" is no screen; {(garage ?? "none")} is home");
            Home = garage ?? "garage";
        }

        if (string.IsNullOrWhiteSpace(Menu) || KindOf(Menu) != ScreenKinds.MainMenu)
        {
            var menu = FirstOfKind(ScreenKinds.MainMenu);
            problems.Add($"menu \"{Menu}\" is no main menu screen; {(menu ?? "none")} is");
            Menu = menu ?? "main_menu";
        }

        Tiles = new Dictionary<string, Dictionary<string, FlowTile>>(
            (Tiles ?? []).Where(h => h.Value != null)
                .Select(h => KeyValuePair.Create(h.Key, new Dictionary<string, FlowTile>(
                    h.Value.Where(t => t.Value != null), StringComparer.OrdinalIgnoreCase))),
            StringComparer.OrdinalIgnoreCase);
        foreach (var (hub, tiles) in Tiles)
        {
            if (!Screens.ContainsKey(hub) && !off.Contains(hub)) problems.Add($"tiles.{hub}: there is no screen \"{hub}\"; its tiles are never shown");
            foreach (var (id, tile) in tiles.ToList())
            {
                tile.Without = (tile.Without ?? []).Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
                foreach (var name in tile.Without.Append(tile.System).OfType<string>().Where(n => n.Length > 0 && !SystemRules.IsName(n)))
                    problems.Add($"tiles.{hub}.{id}: \"{name}\" is no system; taken as none");
                tile.Without = tile.Without.Where(SystemRules.IsName).ToList();
                if (tile.System is { Length: > 0 } && !SystemRules.IsName(tile.System)) tile.System = null;
                tile.Requires = (tile.Requires ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
                foreach (var need in tile.Requires.Where(r => !TileRequirements.All.Contains(r)))
                    problems.Add($"tiles.{hub}.{id}: requires \"{need}\", which is nothing a tile can require ({string.Join(", ", TileRequirements.All)}); passed over");
                tile.Requires = tile.Requires.Where(TileRequirements.All.Contains).ToList();
                if (tile.Off) continue;

                if (string.IsNullOrWhiteSpace(tile.To))
                {
                    problems.Add($"tiles.{hub}.{id}: goes nowhere (no \"to\"); left out");
                    tiles.Remove(id);
                }
                else if (off.Contains(tile.To))
                {
                    tiles.Remove(id);
                }
                else if (!Screens.ContainsKey(tile.To) && !TileActions.IsAction(tile.To))
                {
                    problems.Add($"tiles.{hub}.{id}: \"{tile.To}\" is no screen and nothing a hub does ({string.Join(", ", TileActions.All)}); left out");
                    tiles.Remove(id);
                }
                else if (KindOf(tile.To) is { } kind && ScreenKinds.NeedsWhat(kind))
                {
                    problems.Add($"tiles.{hub}.{id}: a {kind} screen cannot be opened from a tile (it needs to be told what to show); left out");
                    tiles.Remove(id);
                }
                else if (!TileSpots.IsSpot(tile.Spot))
                {
                    problems.Add($"tiles.{hub}.{id}: \"{tile.Spot}\" is no spot ({string.Join(", ", TileSpots.All)}); put {TileSpots.BottomLeft}");
                    tile.Spot = TileSpots.BottomLeft;
                }
            }
        }

        Cards = new Dictionary<string, FlowCard>((Cards ?? []).Where(c => c.Value != null), StringComparer.OrdinalIgnoreCase);
        foreach (var (id, card) in Cards.ToList())
        {
            if (card.Off || MenuCards.IsCard(card.Card)) continue;
            problems.Add($"cards.{id}: \"{card.Card}\" is no card the main menu has ({string.Join(", ", MenuCards.All)}); left out");
            Cards.Remove(id);
        }

        return this;
    }
}

/// <summary>A screen of the flow: its kind and where its ways out lead</summary>
public sealed class FlowScreen
{
    /// <summary>The kind of screen the program shows for it (<see cref="ScreenKinds"/>)</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The system it belongs to (<see cref="SystemRules"/>): off, the screen is not there. None given: its kind's</summary>
    public string? System { get; set; }

    /// <summary>Its ways out by name ("back", "bought"), each to a screen id; one not given or not open goes home</summary>
    public Dictionary<string, string> Exits { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The day can be ended from it (the garage's End Day button; elsewhere the window's, which goes home for the night)</summary>
    public bool EndDay { get; set; }

    /// <summary>Taken out by a mod: as if it were not there</summary>
    public bool Off { get; set; }
}

/// <summary>A tile of a hub screen: a round button that goes to a screen or does one of the hub's own things</summary>
public sealed class FlowTile
{
    /// <summary>A screen id, or one of the hub's own things (<see cref="TileActions"/>)</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>The text key of its tooltip</summary>
    public string? Text { get; set; }

    /// <summary>Its image, as the theme names images ("icons/newspaper")</summary>
    public string? Icon { get; set; }

    /// <summary>Where on the screen it stands (<see cref="TileSpots"/>)</summary>
    public string Spot { get; set; } = TileSpots.BottomLeft;

    /// <summary>Its place among the tiles of its spot, from the top</summary>
    public int Order { get; set; }

    /// <summary>A large tile ("large") or the usual one</summary>
    public string? Size { get; set; }

    /// <summary>Room round its icon, in pixels; none: its size's</summary>
    public double? Padding { get; set; }

    /// <summary>Shown only while this system is on</summary>
    public string? System { get; set; }

    /// <summary>Shown only while every one of these systems is off (a mail order tile for a game with no part shops)</summary>
    public List<string> Without { get; set; } = [];

    /// <summary>What the hub must have for the tile to work (<see cref="TileRequirements"/>)</summary>
    public List<string> Requires { get; set; } = [];

    /// <summary>Taken out by a mod</summary>
    public bool Off { get; set; }

    [JsonIgnore]
    public bool IsLarge => string.Equals(Size, "large", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A card of the main menu</summary>
public sealed class FlowCard
{
    /// <summary>The card it opens (<see cref="MenuCards"/>)</summary>
    public string Card { get; set; } = string.Empty;

    /// <summary>The text key of its button; none: the card's own (<c>menu.&lt;card&gt;</c>)</summary>
    public string? Text { get; set; }

    /// <summary>Its place among the buttons, from the left</summary>
    public int Order { get; set; }

    /// <summary>Taken out by a mod</summary>
    public bool Off { get; set; }
}

/// <summary>The kinds of screen the program has: a screen of the flow is one of them</summary>
public static class ScreenKinds
{
    public const string MainMenu = "main_menu";
    public const string Garage = "garage";
    public const string CarLot = "car_lot";
    public const string RacingHub = "racing_hub";
    public const string Career = "career";
    public const string Diner = "diner";
    public const string Street = "street";
    public const string GasStation = "gas_station";
    public const string PaintBooth = "paint_booth";
    public const string Newspaper = "newspaper";
    public const string CarViewing = "car_viewing";
    public const string DealerMap = "dealer_map";
    public const string DealerLot = "dealer_lot";
    public const string PartShopsMap = "part_shops_map";
    public const string PartShop = "part_shop";
    public const string MailOrder = "mail_order";
    public const string Race = "race";
    public const string SoundBrowser = "sound_browser";
    public const string CarCatalogEditor = "car_catalog_editor";

    public static readonly IReadOnlyList<string> All =
    [
        MainMenu, Garage, CarLot, RacingHub, Career, Diner, Street, GasStation, PaintBooth, Newspaper, CarViewing, DealerMap,
        DealerLot, PartShopsMap, PartShop, MailOrder, Race, SoundBrowser, CarCatalogEditor
    ];

    public static bool IsKind(string? kind) => kind != null && All.Contains(kind);

    /// <summary>The kinds that open over the screen rather than in its place</summary>
    public static bool IsOverlay(string? kind) => kind is RacingHub or Career;

    /// <summary>The kinds that must be told what to show (a shop, a car, a race), so no tile can open them</summary>
    public static bool NeedsWhat(string? kind) => kind is CarViewing or DealerLot or PartShop or Race;

    /// <summary>The kinds the day may be ended from: a game's own screens, not the menus, a race or an overlay</summary>
    public static bool CanEndDay(string? kind) =>
        kind is not (MainMenu or Race or SoundBrowser or CarCatalogEditor or RacingHub or Career);

    /// <summary>The system a kind belongs to when its screen names none</summary>
    public static string? SystemOf(string kind) => kind switch
    {
        Career => "career",
        Diner => "diner",
        Street => "street",
        GasStation => "station",
        PaintBooth => "paintBooth",
        Newspaper => "paper",
        DealerMap or DealerLot => "carDealers",
        PartShopsMap or PartShop => "partShops",
        MailOrder => "mailOrder",
        _ => null
    };
}

/// <summary>What a hub's tile may do besides going to a screen</summary>
public static class TileActions
{
    /// <summary>The quit question: to the main menu or out of the game</summary>
    public const string Quit = "quit";

    /// <summary>The picked car's card</summary>
    public const string CarInfo = "car_info";

    /// <summary>The calendar panel</summary>
    public const string Calendar = "calendar";

    /// <summary>The radio</summary>
    public const string Radio = "radio";

    public static readonly IReadOnlyList<string> All = [Quit, CarInfo, Calendar, Radio];

    public static bool IsAction(string? to) => to != null && All.Contains(to, StringComparer.OrdinalIgnoreCase);
}

/// <summary>What a hub's tile may need to work</summary>
public static class TileRequirements
{
    /// <summary>A car picked</summary>
    public const string Car = "car";

    /// <summary>A car picked that is in the garage (not impounded)</summary>
    public const string CarHere = "car_here";

    public static readonly IReadOnlyList<string> All = [Car, CarHere];
}

/// <summary>Where a hub's tiles stand</summary>
public static class TileSpots
{
    public const string TopLeft = "top_left";
    public const string Left = "left";
    public const string BottomLeft = "bottom_left";
    public const string TopRight = "top_right";
    public const string Right = "right";
    public const string BottomRight = "bottom_right";

    public static readonly IReadOnlyList<string> All = [TopLeft, Left, BottomLeft, TopRight, Right, BottomRight];

    public static bool IsSpot(string? spot) => spot != null && All.Contains(spot);
}

/// <summary>The cards the main menu has</summary>
public static class MenuCards
{
    public const string NewGame = "new_game";
    public const string LoadGame = "load_game";
    public const string Settings = "settings";

    public static readonly IReadOnlyList<string> All = [NewGame, LoadGame, Settings];

    public static bool IsCard(string? card) => card != null && All.Contains(card);
}
