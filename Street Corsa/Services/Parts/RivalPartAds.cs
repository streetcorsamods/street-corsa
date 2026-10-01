using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// The rivals' parts in the used parts ads (<see cref="PartAd.SellerRival"/>): what a rival takes off an engine goes
    /// into the paper under their name instead of back to a shop. They are paid when it sells, to the player or to
    /// another rival, and get the shop's trade-in when the ad runs out with nobody buying.
    /// </summary>
    public static class RivalPartAds
    {
        /// <summary>What a rival asks for a part, as a share of what a used parts shop asks: around the same, give or take</summary>
        public const double MinAskShare = 0.85;
        public const double MaxAskShare = 1.1;

        /// <summary>
        /// The part in the paper under <paramref name="seller"/>'s name, at a price near a used shop's; null when it
        /// is worth nothing to anybody
        /// </summary>
        public static PartAd? Post(GameState gameState, PartsCatalog catalog, Racer seller, PartInstance part, DateTime date, double roll)
        {
            roll = Unit.Clamp01(roll, ifNotFinite: 0);
            var asking = PartPricing.Round(PartPricing.WorthOfAssembly(catalog, part) * PartPricing.UsedShopFactor
                                           * (MinAskShare + roll * (MaxAskShare - MinAskShare)) * GameRules.Sane(gameState.Rules.PartPriceMultiplier));
            if (asking <= 0) return null;

            var ad = new PartAd(part, asking, seller.Name) { SellerRival = seller.Name, PostedDate = date };
            Market.AdBlurbs.Describe(ad, catalog);
            gameState.NewspaperAds.Parts.Add(ad);
            return ad;
        }

        /// <summary>The rival who placed the ad gets <paramref name="amount"/>, when they are still around; nothing for the paper's other sellers</summary>
        public static void Pay(GameState gameState, PartAd ad, decimal amount)
        {
            if (ad.SellerRival == null || amount <= 0) return;
            if (gameState.Racers.Find(ad.SellerRival) is { } seller) seller.Money += amount;
        }

        /// <summary>
        /// The paper's parts a rival could buy for their engine: everybody's ads but their own, and none that ran out
        /// and waits for the shops to take it (<see cref="NewspaperPartsService.RefreshAdsAsync"/> pays its seller the trade-in)
        /// </summary>
        /// <param name="date">The day the rival reads the paper; the save's date when null</param>
        public static List<UsedPartOffer> OffersFor(GameState gameState, Racer buyer, DateTime? date = null) =>
            gameState.NewspaperAds.Parts
                .Where(a => a.AskingPrice > 0 && !string.Equals(a.SellerRival, buyer.Name, StringComparison.Ordinal)
                            && !NewspaperPartsService.IsExpired(a, date ?? gameState.Date))
                .Select(a => new UsedPartOffer(a.AdId, a.Part, a.AskingPrice))
                .ToList();

        /// <summary>A rival bought the part: the ad comes down and its seller is paid. False when it was gone already.</summary>
        public static bool Take(GameState gameState, Guid adId)
        {
            var ad = gameState.NewspaperAds.Parts.FirstOrDefault(a => a.AdId == adId);
            if (ad == null || !gameState.NewspaperAds.Parts.Remove(ad)) return false;
            Pay(gameState, ad, ad.AskingPrice);
            return true;
        }
    }
}
