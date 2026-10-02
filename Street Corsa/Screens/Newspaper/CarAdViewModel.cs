using StreetCorsa.Helpers;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>
    /// A car ad in the classifieds: a dealer's car, or a racer's own (<see cref="UsedCarListingViewModel.RivalAd"/>). Set
    /// as a period paper sets one: the car in bold, the ad's few words, the price, who to call. The rest (condition,
    /// mileage, history, engine) is in its tooltip, the purchase question and a look at the car.
    /// </summary>
    public sealed class CarAdViewModel : ObservableObject
    {
        public CarAdViewModel(UsedCarListingViewModel car, RelayCommand look)
        {
            Car = car;
            LookCommand = look;

            var definition = car.CarDefinition;
            var name = ModelOf(definition);
            Headline = (definition.Year is { } year
                    ? GameText.Say("paper.ad.car.headline", ("yy", (year % 100).ToString("00", Locale.Culture)), ("year", year), ("car", name))
                    : GameText.Say("paper.ad.car.headline.no_year", ("car", name)))
                .ToUpper(Locale.Culture);
            SortKey = $"{name} {definition.Year}";
        }

        /// <summary>
        /// Make and model with the trim, the year left out (the headline has it in front): the catalog's names often
        /// carry the make and the year already ("Chevrolet Camaro 1969 Z/28" is a Chevrolet Camaro Z/28)
        /// </summary>
        public static string ModelOf(Models.Catalog.CarDefinition definition)
        {
            var name = Services.Catalog.CarNames.MakeAndModel(definition);
            if (definition.Year is not { } year) return name;

            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w != year.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
            return words.Count > 0 ? string.Join(' ', words) : name;
        }

        public UsedCarListingViewModel Car { get; }

        /// <summary>The car in the ad's bold run-in: "'63 CHEVROLET IMPALA"</summary>
        public string Headline { get; }

        /// <summary>Where the ad goes among the others: by make, as a paper sorts its car ads</summary>
        public string SortKey { get; }

        /// <summary>The ad's few words (<see cref="Services.Market.AdBlurbs"/>), with the space after them when there are any</summary>
        public string Blurb => string.IsNullOrEmpty(Car.Listing.Description) ? string.Empty : Car.Listing.Description + " ";

        /// <summary>Offers the ad's words to the player's AI, as the paper first shows it (<see cref="Services.Market.AdBlurbs"/>)</summary>
        public Action? Polish { get; init; }

        /// <summary>The AI's words came: the ad prints them</summary>
        public void BlurbRewritten() => OnPropertyChanged(nameof(Blurb));

        public string PriceDisplay => Car.PriceDisplay;

        /// <summary>What it was marked at before its dealer's closing-down sale, struck through before the price; empty when not on sale</summary>
        public string WasPriceDisplay => Car.IsOnSale ? Car.WasPriceDisplay + " " : string.Empty;

        /// <summary>Who to call: the dealer, or the racer selling their own</summary>
        public string SellerDisplay => Car.PrivateSeller is { } rival
            ? GameText.Say("paper.ad.seller.private", ("rival", rival))
            : GameText.Say("paper.ad.seller.dealer", ("dealer", Car.DealerName));

        /// <summary>What the ad does not print: condition, mileage, history, engine</summary>
        public string Details
        {
            get
            {
                var details = GameText.Say("paper.ad.car.details",
                    ("condition", GameText.Say("market.condition.sentence", ("label", Car.ConditionLabel), ("percent", Car.ConditionDisplay))),
                    ("mileage", Car.MileageDisplay),
                    ("history", Car.HistoryDisplay));
                return Car.EngineDisplay.Length == 0 ? details : GameText.Say("paper.ad.car.details.engine", ("details", details), ("engine", Car.EngineDisplay));
            }
        }

        /// <summary>Go and see the car (clicking the ad)</summary>
        public RelayCommand LookCommand { get; }
    }
}
