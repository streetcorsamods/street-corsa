using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.PartShops;

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
        public static double MinAskShare => Rules.RuleBook.Current.Economy.RivalPartMinAskShare;
        public static double MaxAskShare => Rules.RuleBook.Current.Economy.RivalPartMaxAskShare;

        /// <summary>
        /// The part in the paper under <paramref name="seller"/>'s name, at a price near a used shop's, never as little as
        /// a shop of <paramref name="shops"/> (or of the rules) pays for it; null when it is worth nothing to anybody
        /// </summary>
        public static PartAd? Post(GameState gameState, PartsCatalog catalog, Racer seller, PartInstance part, DateTime date, double roll,
            IEnumerable<PartShopDefinition>? shops = null)
        {
            roll = Unit.Clamp01(roll, ifNotFinite: 0);
            var asking = NewspaperPartsService.UsedAsk(catalog, part, PartPricing.WorthOfAssembly(catalog, part) * PartPricing.UsedShopFactor
                * (MinAskShare + roll * (MaxAskShare - MinAskShare)) * GameRules.Sane(gameState.Rules.PartPriceMultiplier), PartShopRules.MostPaidFactor(shops));
            if (asking <= 0) return null;


            var ad = new PartAd(part, asking, seller.Name) { SellerRival = seller.Name, PostedDate = date };
            Market.AdBlurbs.Describe(ad, catalog);
            gameState.NewspaperAds.Parts.Add(ad);
            return ad;
        }

        /// <summary>The rival who placed the ad gets <paramref name="amount"/>, when they are still around; nothing for the paper's other sellers</summary>
        public static void Pay(GameState gameState, PartAd ad, decimal amount) => Pay(gameState, ad.SellerRival, amount);

        /// <summary>The rival named <paramref name="sellerRival"/> gets <paramref name="amount"/>, when they are still around</summary>
        public static void Pay(GameState gameState, string? sellerRival, decimal amount)
        {
            if (sellerRival == null || amount <= 0) return;
            if (gameState.Racers.Find(sellerRival) is { } seller) seller.Money += amount;
        }

        /// <summary>
        /// An engine a rival took out, taken apart and in the paper under their name as a used kit (roadmap step 40): what
        /// they kept back of it they trade in, and the ad says it is missing. Null when the kit is worth nothing to anybody.
        /// </summary>
        public static KitAd? PostKit(GameState gameState, PartsCatalog catalog, Racer seller, PartInstance engine, DateTime date, Random random,
            IEnumerable<PartShopDefinition>? shops = null)
        {
            var (parts, kept) = KitWork.TearDown(catalog, engine, random, Rules.RuleBook.Current.Economy.KitMostMissing);
            var asking = NewspaperPartsService.KitAsk(catalog, parts, KitWork.WorthOf(catalog, parts) * PartPricing.UsedShopFactor
                * (MinAskShare + random.NextDouble() * (MaxAskShare - MinAskShare)) * GameRules.Sane(gameState.Rules.PartPriceMultiplier),
                PartShopRules.MostPaidFactor(shops));
            if (asking <= 0) return null;

            seller.Money += kept.Sum(p => PartPricing.TradeIn(catalog, p));
            var ad = new KitAd
            {
                Parts = parts,
                Name = KitWork.EngineNameOf(catalog, parts),
                Missing = KitWork.NamesOf(catalog, kept),
                AskingPrice = asking,
                SellerName = seller.Name,
                SellerRival = seller.Name,
                PostedDate = date
            };
            (gameState.NewspaperAds.Kits ??= []).Add(ad);
            return ad;
        }

        /// <summary>The used kits in the paper a rival could buy: everybody's but their own, none run out; copies</summary>
        public static List<KitOffer> KitOffersFor(GameState gameState, Racer buyer, PartsCatalog catalog, DateTime? date = null) =>
            (gameState.NewspaperAds.Kits ?? [])
                .Where(a => a.AskingPrice > 0 && !string.Equals(a.SellerRival, buyer.Name, StringComparison.Ordinal)
                            && !NewspaperPartsService.IsExpired(a.PostedDate, date ?? gameState.Date))
                .Select(a => new KitOffer(a.AdId, null, string.Empty, catalog.Kits.Complete?.Id ?? string.Empty, a.Name,
                    a.Parts.Select(p => PartTrees.Clone(p, keepIds: true)).ToList(), a.AskingPrice))
                .ToList();

        /// <summary>A rival bought the used kit: the ad comes down and its seller is paid. False when it was gone already.</summary>
        public static bool TakeKit(GameState gameState, Guid adId)
        {
            var kits = gameState.NewspaperAds.Kits ?? [];
            var ad = kits.FirstOrDefault(a => a.AdId == adId);
            if (ad == null || !kits.Remove(ad)) return false;
            Pay(gameState, ad.SellerRival, ad.AskingPrice);
            return true;
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
