using StreetCorsa.Helpers;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// View model for a single used car listing
    /// </summary>
    public class UsedCarListingViewModel
    {
        public UsedCarListing Listing { get; set; } = new();
        public CarDefinition CarDefinition { get; set; } = new();
        public CarProfile CarProfile { get; set; } = new();
        public string DealerName { get; set; } = string.Empty;
        public bool CanAfford { get; set; }

        /// <summary>
        /// The rival's ad this stands for, when the car is a racer's own in the paper rather than on a lot: the
        /// <see cref="Listing"/> is then made up to show it (<see cref="RivalCarAds.AsListing"/>), and the ad is what is bought
        /// </summary>
        public RivalCarAd? RivalAd { get; set; }

        /// <summary>The racer selling it, with <see cref="RivalAd"/></summary>
        public string? PrivateSeller { get; set; }

        /// <summary>How a racer selling their own car shows where a dealer's name would</summary>
        public static string PrivateSellerName(string rival) => GameText.Say("market.private_seller", ("rival", rival));

        /// <summary>Who sells it, for the purchase question</summary>
        public string SellerLine => PrivateSeller != null
            ? GameText.Say("market.seller_line.private", ("seller", PrivateSeller))
            : GameText.Say("market.seller_line.dealer", ("dealer", DealerName));

        public string DisplayName => Services.Catalog.CarNames.MakeAndModel(CarDefinition);
        public string YearDisplay => CarDefinition.Year?.ToString() ?? GameText.Say("market.unknown_year");
        public string PriceDisplay => Money.Text(Listing.Price);

        /// <summary>Marked down in its dealer's closing-down sale</summary>
        public bool IsOnSale => Listing.PriceBeforeSale is { } was && was > Listing.Price;

        /// <summary>What it was marked at before the sale</summary>
        public string WasPriceDisplay => IsOnSale ? Money.Text(Listing.PriceBeforeSale!.Value) : string.Empty;
        public string ConditionDisplay => Shared.ConditionDisplay.Of(Listing.Condition).Percent;
        public string MileageDisplay => Measure.Distance(Listing.Mileage).ToString();

        /// <summary>Who had the car and how it has raced</summary>
        public string HistoryDisplay => CarHistoryDisplay.Summary(Listing.History, forSale: true);

        /// <summary>The condition in a word, for the purchase question</summary>
        public string ConditionLabel => Shared.ConditionDisplay.Of(Listing.Condition).Label;

        /// <summary>What is under the hood, with a word on it when somebody has been at it</summary>
        public string EngineDisplay => Listing.EngineSummary == null
            ? string.Empty
            : Listing.IsModified ? GameText.Say("market.engine_modified", ("engine", Listing.EngineSummary)) : Listing.EngineSummary;
    }
}
