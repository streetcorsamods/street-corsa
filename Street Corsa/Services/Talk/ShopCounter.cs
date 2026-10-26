using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StreetCorsa.Services.Talk
{
    /// <summary>
    /// What the player asked a part shop's clerk for, as the AI reads it out of their words (<see cref="Schema"/>): the game
    /// looks for it in the shop's own stock (<see cref="IShopCounter.FindAsync"/>), the AI never does.
    /// </summary>
    /// <param name="Asks">The player asks for parts at all (not "how's business"); any kind, price, name or want read makes it one</param>
    /// <param name="Kind">A kind of part, as the shop lists them ("Carburetors"); null: any</param>
    /// <param name="Fits">Only what goes on the player's car</param>
    /// <param name="MaxPrice">At most this much; null: any price</param>
    /// <param name="Words">A make or a name to look for in the parts' names ("Edelbrock", "Hemi"); null: none</param>
    /// <param name="Want">What matters most: <see cref="Power"/>, <see cref="Cheap"/>, or null for neither</param>
    public sealed record ClerkSearch(bool Asks, string? Kind, bool Fits, decimal? MaxPrice, string? Words, string? Want)
    {
        public const string Power = "power";
        public const string Cheap = "cheap";

        /// <summary>The kinds the AI may pick from; "any" for none</summary>
        public const string AnyKind = "any";

        /// <summary>A search for nothing in particular: the shop's best for the player's car</summary>
        public static ClerkSearch Anything { get; } = new(true, null, false, null, null, null);

        /// <summary>
        /// The JSON schema of the AI's answer, its kinds limited to <paramref name="kinds"/> (and "any"), so a model held to the
        /// schema cannot name a kind the shop does not list
        /// </summary>
        public static JsonObject Schema(IEnumerable<string> kinds) => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["asks"] = new JsonObject { ["type"] = "boolean" },
                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([AnyKind, .. kinds.Select(k => (JsonNode)k)]) },
                ["fits"] = new JsonObject { ["type"] = "boolean" },
                ["maxPrice"] = new JsonObject { ["type"] = "number" },
                ["words"] = new JsonObject { ["type"] = "string" },
                ["want"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Power, Cheap, "any") }
            },
            ["required"] = new JsonArray("asks", "kind", "fits", "maxPrice", "words", "want")
        };

        /// <summary>
        /// The search in the AI's answer: the first JSON object in it, read leniently (a kind the shop does not list, a
        /// price of 0 or less, "any" and blanks are none); null when there is no object to read
        /// </summary>
        public static ClerkSearch? Parse(string? answer, IReadOnlyCollection<string> kinds)
        {
            if (answer == null) return null;
            var start = answer.IndexOf('{');
            var end = answer.LastIndexOf('}');
            if (start < 0 || end <= start) return null;

            JsonObject? json;
            try
            {
                json = JsonNode.Parse(answer[start..(end + 1)]) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }

            if (json == null) return null;

            var kind = Text(json["kind"]);
            kind = kinds.FirstOrDefault(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));
            var want = Text(json["want"])?.ToLowerInvariant() is { } w && (w == Power || w == Cheap) ? w : null;
            var price = Number(json["maxPrice"]);
            var search = new ClerkSearch(
                Asks: Bool(json["asks"]) ?? true,
                Kind: kind,
                Fits: Bool(json["fits"]) ?? false,
                MaxPrice: price is > 0 ? Math.Round(price.Value) : null,
                Words: Text(json["words"]) is { } words && !string.Equals(words, AnyKind, StringComparison.OrdinalIgnoreCase) ? words : null,
                Want: want);

            // A small model says it is no ask while it reads a kind or a price out of it ("what about a cam?"): what it read
            // is the ask
            return search.Asks || search is { Kind: null, MaxPrice: null, Words: null, Want: null } ? search : search with { Asks = true };
        }

        private static string? Text(JsonNode? node) =>
            node?.GetValueKind() == JsonValueKind.String && node.GetValue<string>().Trim() is { Length: > 0 } text ? text : null;

        private static bool? Bool(JsonNode? node) => node?.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(node.GetValue<string>(), out var b) ? b : null,
            _ => null
        };

        private static decimal? Number(JsonNode? node) => node?.GetValueKind() switch
        {
            // A number too big for a price (1e30) is no price, as is one that will not read
            JsonValueKind.Number => node.AsValue().TryGetValue<decimal>(out var n) ? n : null,
            JsonValueKind.String => decimal.TryParse(node.GetValue<string>().Trim().TrimStart('$').Replace(",", ""), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null
        };
    }

    /// <summary>
    /// One thing the game picked out of the shop's stock for the clerk to talk about: its row's key (a part's id, a kit's
    /// build and size; the list shows it picked), what it costs, whether it goes on the player's car, and what it does on
    /// the dyno for the player's engine when it is a part that changes the power
    /// </summary>
    public sealed record ClerkPick(string Id, string Name, string Kind, decimal Price, bool Fits, int InStock, double? PowerBefore = null,
        double? PowerAfter = null)
    {
        /// <summary>
        /// A machinist's job picked for the talk: <paramref name="part"/> is the part it is done on, <paramref name="speed"/>
        /// that it is speed work, <paramref name="days"/> how long it takes (read back as <see cref="Part"/>, <see cref="Speed"/>
        /// and <see cref="Days"/>)
        /// </summary>
        public static ClerkPick Job(string id, string name, string part, decimal price, bool speed, int days, double? powerBefore = null,
            double? powerAfter = null) =>
            new(id, name, part, price, speed, days, powerBefore, powerAfter);

        /// <summary>A machinist's job: the part it is done on</summary>
        public string Part => Kind;

        /// <summary>A machinist's job: it is speed work</summary>
        public bool Speed => Fits;

        /// <summary>A machinist's job: the days it takes</summary>
        public int Days => InStock;
    }

    /// <summary>
    /// What the game found for a search: the search as it was run (a name no part has, or a fit with no car to fit, left
    /// out), how many parts in stock match it, and the few best of them; <paramref name="NoName"/> the name asked for that
    /// no part in stock has
    /// </summary>
    public sealed record ClerkFindings(ClerkSearch Search, int Matching, IReadOnlyList<ClerkPick> Picks, string? NoName = null);

    /// <summary>
    /// The part shop's counter, as a clerk's talk sees it (the screen is it): the kinds of part the shop has, what the clerk
    /// knows of the shop and of the player's car, and the search in the shop's real stock
    /// </summary>
    public interface IShopCounter
    {
        /// <summary>The kinds of part in stock, as the shop lists them</summary>
        IReadOnlyList<string> Kinds { get; }

        /// <summary>The shop as the clerk knows it, a fact a line: its name and kind, a sale on, the player as a customer, the player's car</summary>
        IEnumerable<string> Facts();

        /// <summary>The stock searched: the list shows what matches and the picks; the best few are the clerk's to talk about</summary>
        Task<ClerkFindings> FindAsync(ClerkSearch search, CancellationToken cancellationToken);

        /// <summary>The picks after a deal: at their prices now, with what is left of them; one sold out is gone</summary>
        ClerkFindings Recheck(ClerkFindings found);
    }
}
