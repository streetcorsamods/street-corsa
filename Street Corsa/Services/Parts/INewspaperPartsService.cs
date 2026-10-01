using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// Parts away from the shops: used parts from the paper's ads, new ones by mail order, and the player's own sold to
    /// whoever takes them. Everything bought goes to the player's shelf. The shops on the map are
    /// <see cref="PartShops.IPartShopService"/>.
    /// </summary>
    public interface INewspaperPartsService
    {
        /// <summary>False when there are no converted parts: nothing to sell</summary>
        bool IsAvailable { get; }

        /// <summary>Every part that can be ordered new, by name</summary>
        IReadOnlyList<PartDefinition> Assortment { get; }

        /// <summary>What the mail order asks for the part new, its premium in</summary>
        /// <param name="priceMultiplier">The save's <see cref="GameRules.PartPriceMultiplier"/></param>
        decimal NewPrice(PartDefinition part, double priceMultiplier);

        /// <summary>What the player gets for a part of theirs, with whatever is mounted on it</summary>
        decimal TradeInPrice(PartInstance part);

        /// <summary>
        /// Takes old ads out of the paper and puts new ones in; run once a game day. The new ads are made on a
        /// worker thread (whole engines among them), the game state is only touched on the thread that called.
        /// </summary>
        Task RefreshAdsAsync(GameState gameState, DateTime currentDate);

        /// <summary>Ids of the parts that go somewhere on a car as it stands, taken slots included</summary>
        HashSet<string> FindFittingParts(Car? car);

        /// <summary>
        /// Orders the part by mail: paid now, on the shelf in <see cref="NewspaperPartsService.MailOrderDays"/> days
        /// (<see cref="Deliver"/>). Null when the player cannot pay.
        /// </summary>
        PartOrder? BuyNew(GameState gameState, PartDefinition part);

        /// <summary>The mail order that has come by <paramref name="currentDate"/>, put on the player's shelf; run each morning</summary>
        List<PartOrder> Deliver(GameState gameState, DateTime currentDate);

        /// <summary>False when the player cannot pay, or the ad has left the paper in the meantime</summary>
        bool BuyUsed(GameState gameState, PartAd ad);

        bool Sell(GameState gameState, PartInstance part);
    }
}
