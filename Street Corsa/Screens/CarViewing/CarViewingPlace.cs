using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Screens.CarViewing
{
    /// <summary>Where a car for sale is gone to see: on its dealer's lot, or at its seller's curb</summary>
    public enum CarViewingSpot
    {
        DealerLot,
        Curb
    }

    /// <summary>
    /// Who sells the car and where it stands: a dealer's car on that dealer's lot (<paramref name="DealerId"/>), a
    /// rival's own at the curb (<paramref name="Rival"/>, the ad that is bought), anyone else's at the curb too
    /// </summary>
    public sealed record CarViewingSeller(CarViewingSpot Spot, string? DealerId, RivalCarAds.Offer? Rival);

    /// <summary>Where the screen goes once the player has tried to buy the car</summary>
    public enum AfterPurchase
    {
        /// <summary>Bought: home, where the car now is</summary>
        Garage,

        /// <summary>Gone to somebody else meanwhile: back to the paper, nothing to look at here</summary>
        Newspaper,

        /// <summary>Not bought (short of money): still looking at it</summary>
        Stay
    }

    /// <summary>The car viewing screen's choices, kept out of the screen so the tests can hold them to it</summary>
    public static class CarViewingPlace
    {
        /// <summary>
        /// Who sells the car: a rival's ad in the paper stands for its listing by the ad's id
        /// (<see cref="RivalCarAds.AsListing"/>); a listing of a dealer the game knows is that dealer's; anything
        /// else is a private seller's
        /// </summary>
        public static CarViewingSeller Of(GameState state, UsedCarListing listing, Func<string, bool> isDealer)
        {
            var rival = RivalCarAds.Live(state).FirstOrDefault(o => string.Equals(o.Ad.AdId.ToString(), listing.Id, StringComparison.OrdinalIgnoreCase));
            if (rival != null) return new CarViewingSeller(CarViewingSpot.Curb, null, rival);

            return !string.IsNullOrWhiteSpace(listing.DealerLocation) && isDealer(listing.DealerLocation)
                ? new CarViewingSeller(CarViewingSpot.DealerLot, listing.DealerLocation, null)
                : new CarViewingSeller(CarViewingSpot.Curb, null, null);
        }

        public static AfterPurchase Next(PurchaseResult result) => result.Succeeded
            ? AfterPurchase.Garage
            : result.Outcome == PurchaseOutcome.NoLongerAvailable ? AfterPurchase.Newspaper : AfterPurchase.Stay;

        /// <summary>
        /// The street the seller lives on, and the light it is seen in: one of the streets used at this hour, the same
        /// one for the same ad every visit; a street used at no such hour in its nearest light. Null with no street.
        /// </summary>
        public static (StreetScene Street, StreetLight Light)? StreetFor(IReadOnlyList<StreetScene> streets, StreetLight light, string listingId)
        {
            if (streets.Count == 0) return null;

            var fitting = streets.Where(s => s.Times.Contains(light)).ToList();
            var pool = fitting.Count > 0 ? fitting : streets.ToList();
            var street = pool[StableIndex(listingId, pool.Count)];
            return (street, street.NearestTime(light));
        }

        /// <summary>The same number for the same text every run (string.GetHashCode is not)</summary>
        private static int StableIndex(string text, int count)
        {
            var hash = 17u;
            foreach (var c in text) hash = hash * 31u + c;
            return (int)(hash % (uint)Math.Max(1, count));
        }
    }
}
