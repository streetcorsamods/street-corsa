namespace StreetCorsa.Models.GameState
{
    public class NewspaperAds
    {
        public List<PartAd> Parts { get; set; }

        /// <summary>Used engine kits: an engine's parts in one ad, saying what is missing (roadmap step 40)</summary>
        public List<KitAd> Kits { get; set; }

        /// <summary>The player's own cars up for sale; see <see cref="Services.Market.CarSaleService"/></summary>
        public List<CarSaleAd> PlayerCars { get; set; }

        /// <summary>The rivals' spare cars up for sale; see <see cref="Services.Opponents.RivalCarAds"/></summary>
        public List<RivalCarAd> RivalCars { get; set; }

        public NewspaperAds()
        {
            Parts = [];
            Kits = [];
            PlayerCars = [];
            RivalCars = [];
        }
    }

    /// <summary>An ad's few words (<see cref="Services.Market.AdBlurbs"/>), the game's own until the player's AI rewrites them</summary>
    public interface IAdWords
    {
        /// <summary>The ad's few words; null on ads from before they had them</summary>
        string? Description { get; set; }

        /// <summary>The player's AI rewrote <see cref="Description"/>: it is not offered the ad again</summary>
        bool AiPolished { get; set; }
    }

    /// <summary>A car in the classifieds from someone's own garage (a rival's, the player's): what its seller says about the engine</summary>
    public interface ICarAdWords : IAdWords
    {
        string? EngineSummary { get; }

        bool IsModified { get; }
    }

    /// <summary>
    /// A rival's car in the classifieds. As with the player's own ads the car stays in the rival's garage until
    /// somebody buys it; the ad names the rival and the car.
    /// </summary>
    public class RivalCarAd : ICarAdWords
    {
        public Guid AdId { get; set; } = Guid.NewGuid();
        public string RivalName { get; set; } = string.Empty;
        public Guid CarInstanceId { get; set; }
        public decimal AskingPrice { get; set; }

        /// <summary>Game time</summary>
        public DateTime PostedDate { get; set; }

        /// <summary>The price came down once already</summary>
        public bool Reduced { get; set; }

        /// <summary>What the seller says about the engine, as a lot's listing does; null when they had nothing to say</summary>
        public string? EngineSummary { get; set; }

        /// <summary>Somebody has been at the engine</summary>
        public bool IsModified { get; set; }

        /// <summary>The ad's few words about the car (<see cref="Services.Market.AdBlurbs"/>); null on ads from before they had them</summary>
        public string? Description { get; set; }

        /// <summary>The player's AI rewrote <see cref="Description"/>: it is not offered the ad again</summary>
        public bool AiPolished { get; set; }

        /// <summary>
        /// The price first asked, kept the first time anybody haggles over it: the hard floor is worked out from it
        /// (<c>ai.talk.mostDiscount</c>), so asking again and again, of whoever, never brings it lower. Null until then.
        /// </summary>
        public decimal? FirstAsked { get; set; }

        /// <summary>
        /// The most this seller will come down from <see cref="FirstAsked"/> (0.1: ten percent), drawn once with it: 0 for
        /// one who will not budge, never more than <c>ai.talk.mostDiscount</c>. Kept, so asking again never draws again.
        /// </summary>
        public double? MostOff { get; set; }
    }

    /// <summary>
    /// One of the player's cars in the classifieds. The car stays in the garage (and can still race) until a
    /// buyer's offer is taken; the ad names it by its instance id.
    /// </summary>
    public class CarSaleAd : ICarAdWords
    {
        public Guid AdId { get; set; } = Guid.NewGuid();
        public Guid CarInstanceId { get; set; }
        public decimal AskingPrice { get; set; }

        /// <summary>Game time</summary>
        public DateTime PostedDate { get; set; }

        /// <summary>The buyer on the phone, if there is one; a later caller takes the place of one who gave up</summary>
        public CarOffer? Offer { get; set; }

        /// <summary>The buyer's offer while it still stands on <paramref name="date"/> (game time); null when there is none</summary>
        public CarOffer? OfferOn(DateTime date) => Offer is { } offer && offer.Expires >= date ? offer : null;

        /// <summary>What the player says about the engine in the ad, as it is in the car now; null when nothing</summary>
        public string? EngineSummary { get; set; }

        /// <summary>Somebody has been at the engine, as the ad was written</summary>
        public bool IsModified { get; set; }

        /// <summary>The ad's few words about the car (<see cref="Services.Market.AdBlurbs"/>); null on ads from before they had them</summary>
        public string? Description { get; set; }

        /// <summary>The player's AI rewrote <see cref="Description"/>: it is not offered the ad again</summary>
        public bool AiPolished { get; set; }
    }

    /// <summary>What a buyer who answered an ad would pay, and until when</summary>
    public class CarOffer
    {
        /// <summary>
        /// This offer and no other: a later one on the same ad, even from a caller of the same name, is a new deal. Null
        /// on offers from before it was kept.
        /// </summary>
        public Guid? Id { get; set; }

        public string BuyerName { get; set; } = string.Empty;

        /// <summary>The rival who called, when the buyer is one of the racers: the car then races under them</summary>
        public string? RivalName { get; set; }
        public decimal Amount { get; set; }

        /// <summary>
        /// What the car was worth the day the buyer called: a car worth less when the offer is taken (its engine or
        /// wheels taken off since) gets an offer cut to match. Null on offers from before it was kept.
        /// </summary>
        public decimal? ValueAtOffer { get; set; }

        /// <summary>Game time; after it the buyer has found another car</summary>
        public DateTime Expires { get; set; }

        /// <summary>
        /// What the buyer first offered, kept the first time the player haggles with them: the hard ceiling is worked out
        /// from it (<c>ai.talk.mostRaise</c>), so asking again and again never brings it higher. Null until then.
        /// </summary>
        public decimal? FirstAmount { get; set; }

        /// <summary>
        /// The most this buyer will come up from <see cref="FirstAmount"/> (0.1: ten percent), drawn once with it: 0 for one
        /// who will not budge, never more than <c>ai.talk.mostRaise</c>. Kept, so asking again never draws again.
        /// </summary>
        public double? MostOver { get; set; }
    }

    public class PartAd : IAdWords
    {
        public Guid AdId { get; set; }
        public PartInstance Part { get; set; }
        public decimal AskingPrice { get; set; }
        public string SellerName { get; set; }

        /// <summary>
        /// The rival selling it, when the part came off one of their cars: they are paid when it sells, and the shop's
        /// trade-in when nobody wants it. Null for the paper's other sellers.
        /// </summary>
        public string? SellerRival { get; set; }

        /// <summary>Game time; set by whoever places the ad</summary>
        public DateTime PostedDate { get; set; }
        public int DaysActive { get; set; }

        /// <summary>A junkyard's part the player has haggled over already: the owner does not come down twice</summary>
        public bool Haggled { get; set; }

        /// <summary>
        /// The ad's few words about the part (<see cref="Services.Market.AdBlurbs"/>), for the paper's ads; null on a
        /// junkyard's part and on ads from before they had them
        /// </summary>
        public string? Description { get; set; }

        /// <summary>The player's AI rewrote <see cref="Description"/>: it is not offered the ad again</summary>
        public bool AiPolished { get; set; }

        /// <summary>
        /// The price first asked, kept the first time anybody haggles over it: the hard floor is worked out from it
        /// (<c>ai.talk.mostDiscount</c>), so asking again and again, of whoever, never brings it lower. Null until then.
        /// </summary>
        public decimal? FirstAsked { get; set; }

        /// <summary>
        /// The most this seller will come down from <see cref="FirstAsked"/> (0.1: ten percent), drawn once with it: 0 for
        /// one who will not budge, never more than <c>ai.talk.mostDiscount</c>. Kept, so asking again never draws again.
        /// </summary>
        public double? MostOff { get; set; }

        public PartAd()
        {
            AdId = Guid.NewGuid();
            Part = new PartInstance();
            AskingPrice = 0m;
            SellerName = "Unknown";
            DaysActive = 0;
        }

        public PartAd(PartInstance part, decimal askingPrice, string sellerName) : this()
        {
            Part = part;
            AskingPrice = askingPrice;
            SellerName = sellerName;
        }
    }
}
