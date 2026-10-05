using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Market
{
    /// <summary>
    /// Which of the cars for sale the paper prints: mostly private people's (and the racers' own, <see cref="Opponents.RivalCarAds"/>),
    /// a few of the dealers'. A dealer puts a car or so off its lot in the paper, and what it keeps out back, not good
    /// enough for the lot (<see cref="UsedCarListing.PaperOnly"/>); the rest of its stock is seen on the lot.
    /// </summary>
    public static class PaperAds
    {
        // The pick is drawn from other numbers than the paper's order (ClassifiedOrder.Shuffled, by the id alone): by the
        // same ones, the car each lot printed would be the first it shuffles to, and every dealer's ad on the first page
        private const string PickSalt = "lot:";

        /// <summary>
        /// The ones of <paramref name="forSale"/> in the paper: every car in the paper only, and <paramref name="perLot"/>
        /// off each dealer's lot, the same ones each time the paper is opened
        /// </summary>
        public static IEnumerable<UsedCarListing> Listings(IEnumerable<UsedCarListing> forSale, int perLot)
        {
            var all = forSale.ToList();
            var offTheLots = all
                .Where(l => !l.PaperOnly)
                .GroupBy(l => l.DealerLocation, StringComparer.OrdinalIgnoreCase)
                .SelectMany(lot => lot.OrderBy(l => StableHash.Fnv(PickSalt + l.Id)).ThenBy(l => l.Id, StringComparer.Ordinal).Take(Math.Max(0, perLot)));
            return all.Where(l => l.PaperOnly).Concat(offTheLots);
        }
    }
}
