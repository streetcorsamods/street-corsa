namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// A part shop as the save carries it: how its business is going (the fields of <see cref="DealerLocation"/>: open,
    /// closing down, closed or waiting to open, and its trade), what is on its shelves, and what the player has spent
    /// there. Where it is, what kind of shop it is and what it deals in live in the mods' <c>partshops</c> files and are
    /// merged over the save (<c>PartShopRoster.Sync</c>).
    /// </summary>
    public class PartShopLocation : DealerLocation
    {
        /// <summary>New parts on the shelves, by part: a shop dealing in new parts</summary>
        public List<ShopShelf> Shelves { get; set; } = [];

        /// <summary>Used parts and whole engines in the yard, each at its own price: a junkyard</summary>
        public List<PartAd> Yard { get; set; } = [];

        /// <summary>The day the shelves were last filled; null for a shop never stocked</summary>
        public DateTime? RestockedOn { get; set; }

        /// <summary>What the player has spent here over the game: a regular gets a discount (<c>PartShopRules.RegularDiscount</c>)</summary>
        public decimal PlayerSpent { get; set; }
    }

    /// <summary>So many of one new part on a shop's shelves</summary>
    public class ShopShelf
    {
        public string DefinitionId { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    /// <summary>A part ordered by mail: paid for, and on its way to the player's shelf</summary>
    public class PartOrder
    {
        public PartInstance Part { get; set; } = new();

        /// <summary>What was paid, for the note that comes with it</summary>
        public decimal Paid { get; set; }

        public DateTime OrderedOn { get; set; }

        /// <summary>The morning it comes; it is on the shelf from the first review of that day</summary>
        public DateTime ArrivesOn { get; set; }
    }
}
