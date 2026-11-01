namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// An engine kit on the player's shelf: the crate's parts are loose parts on the shelf
    /// (<see cref="Racer.Parts"/>), which this names by their ids, so selling them and the save's migrations see them
    /// as any other. A crate that has lost a part (sold, put on an engine, gone with its mod) is no crate any more: what
    /// is left of it is loose on the shelf (<c>KitCrates.Tidy</c>).
    /// </summary>
    public class KitCrate
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The build the kit was made up from (<c>engine_builds</c>' id); empty for a used kit out of an engine</summary>
        public string BuildId { get; set; } = string.Empty;

        /// <summary>The kit's size (<c>engine_kits.json</c>' id): "short_block", "long_block", "complete"</summary>
        public string Size { get; set; } = string.Empty;

        /// <summary>The engine it is, as the player read it when it was bought: the build's name, a used kit's block</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What a used kit came without, by the parts' names, as its ad said; empty for a new one</summary>
        public List<string> Missing { get; set; } = [];

        /// <summary>The ids of its parts on the shelf, block first</summary>
        public List<Guid> PartIds { get; set; } = [];

        /// <summary>Bought new: its parts are as they left the factory</summary>
        public bool New { get; set; }
    }

    /// <summary>So many new kits of one build and size on a part shop's shelves</summary>
    public class ShopKit
    {
        public string BuildId { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public int Count { get; set; }

        /// <summary>One line of kits, a build in a size, as one key: "build|size"</summary>
        public static string KeyOf(string buildId, string size) => buildId + "|" + size;
    }

    /// <summary>
    /// A used engine kit for sale: an engine taken apart, its parts each with its own wear, short of what its seller kept
    /// back (<see cref="Missing"/>, as the ad says it). In the paper (<see cref="NewspaperAds.Kits"/>), from a rival who
    /// swapped or tore down an engine or anybody else, or in a junkyard's yard (<see cref="PartShopLocation.YardKits"/>).
    /// </summary>
    public class KitAd
    {
        public Guid AdId { get; set; } = Guid.NewGuid();

        /// <summary>The parts in the crate, loose, block first</summary>
        public List<PartInstance> Parts { get; set; } = [];

        /// <summary>The engine it is, as the ad has it: the block's name</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What it comes without, by the parts' names</summary>
        public List<string> Missing { get; set; } = [];

        public decimal AskingPrice { get; set; }

        public string SellerName { get; set; } = string.Empty;

        /// <summary>The rival selling it: paid when it sells, the shops' trade-in when nobody wants it. Null for anybody else.</summary>
        public string? SellerRival { get; set; }

        /// <summary>Game time</summary>
        public DateTime PostedDate { get; set; }
    }
}
