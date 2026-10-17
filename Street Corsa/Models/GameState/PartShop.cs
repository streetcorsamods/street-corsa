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

    /// <summary>
    /// A part left at a machine shop: the work is done and paid for as it is dropped off (the part as it comes back), and
    /// the shop brings it back to the garage on <see cref="ReadyOn"/>
    /// </summary>
    public class MachineOrder
    {
        /// <summary>The part as it comes back: cut, and with what the machinist fitted to it</summary>
        public PartInstance Part { get; set; } = new();

        /// <summary>The shop that has it (<c>partshops</c>' id) and its name when it was left there, for the note</summary>
        public string ShopId { get; set; } = string.Empty;
        public string ShopName { get; set; } = string.Empty;

        /// <summary>The job, as the player read it: "Bored +.030, oversize pistons"</summary>
        public string Work { get; set; } = string.Empty;

        public decimal Paid { get; set; }

        public DateTime LeftOn { get; set; }

        /// <summary>The morning it is back in the garage, from the first review of that day</summary>
        public DateTime ReadyOn { get; set; }

        /// <summary>It came off the engine stand: back on it if the stand is free, else on the shelf</summary>
        public bool FromStand { get; set; }

        /// <summary>
        /// The part it was made for and comes back with (a bare block's new pistons): kept there as long as that is; null
        /// for a part the player left here
        /// </summary>
        public Guid? ComesWith { get; set; }
    }
}
