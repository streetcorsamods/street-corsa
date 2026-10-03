using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Market
{
    /// <summary>What came of bringing the car just bought into the garage</summary>
    public enum SwitchOutcome
    {
        Switched,
        /// <summary>In the garage, but the game could not be saved</summary>
        SaveFailed,
        /// <summary>The car is no longer the player's: nothing changed</summary>
        NotOwned
    }

    /// <summary>Buying a car, wherever it was found</summary>
    public interface ICarPurchaseService
    {
        /// <summary>
        /// Checks the money and the listing, moves the car into the player's garage, spends the time it takes
        /// and saves. The caller shows the result.
        /// </summary>
        Task<PurchaseResult> PurchaseAsync(
            Models.GameState.GameState gameState, UsedCarListing listing, CarDefinition carDef);

        /// <summary>The same for a rival's car out of the paper (<see cref="RivalCarAd"/>), at the ad's price as it stands now</summary>
        Task<PurchaseResult> PurchaseFromRivalAsync(Models.GameState.GameState gameState, RivalCarAd ad, CarDefinition carDef);

        /// <summary>
        /// Brings the car just bought into the garage, the car that stood there going to the car lot in its stead, and saves.
        /// </summary>
        SwitchOutcome SwitchTo(Models.GameState.GameState gameState, Car car);
    }
}
