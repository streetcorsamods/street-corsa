using StreetCorsa.Helpers;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>
    /// A car ad in the classifieds: a dealer's car, a racer's own (<see cref="UsedCarListingViewModel.RivalAd"/>), or the
    /// player's own (<see cref="PlayerAd"/>), circled in pencil. Set as a period paper sets one: the car in bold, the ad's
    /// few words, the price, who to call. The rest (condition, mileage, history, engine) is in its tooltip, the purchase
    /// question and a look at the car; the player's own opens on the ad and the buyer who called.
    /// </summary>
    public sealed class CarAdViewModel : ObservableObject
    {
        /// <param name="look">Clicking the ad: go and see the car, or, the player's own, open it</param>
        /// <param name="playerAd">The player's own ad, when it is one</param>
        /// <param name="today">Game time now, for whether a buyer's offer still stands</param>
        public CarAdViewModel(UsedCarListingViewModel car, RelayCommand look, Models.GameState.CarSaleAd? playerAd = null, DateTime today = default)
        {
            Car = car;
            LookCommand = look;
            PlayerAd = playerAd;
            _today = today;

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

        private readonly DateTime _today;

        /// <summary>The player's own ad, when this is one: it is marked as theirs and opens on the buyer who called</summary>
        public Models.GameState.CarSaleAd? PlayerAd { get; }

        public bool IsPlayerAd => PlayerAd != null;

        /// <summary>The buyer's offer pencilled by the player's own ad ("Gary, $1,200!"); empty with nobody on the phone</summary>
        public string PencilNote => PlayerAd?.Offer is { } offer && offer.Expires >= _today
            ? GameText.Say("paper.ad.pencil.offer", ("buyer", Services.Market.CarSaleService.Capitalized(offer.BuyerName)), ("amount", new Helpers.Money(offer.Amount)))
            : string.Empty;

        public bool HasPencilNote => PencilNote.Length > 0;

        /// <summary>The offer on the player's ad changed (raised, turned down, gone): the pencil note again</summary>
        public void RefreshOffer()
        {
            OnPropertyChanged(nameof(PencilNote));
            OnPropertyChanged(nameof(HasPencilNote));
        }

        /// <summary>The car in the ad's bold run-in: "'63 CHEVROLET IMPALA"</summary>
        public string Headline { get; }

        /// <summary>Where the ad goes among the others: by make, as a paper sorts its car ads</summary>
        public string SortKey { get; }

        /// <summary>The ad's few words (<see cref="Services.Market.AdBlurbs"/>), with the space after them when there are any</summary>
        public string Blurb => string.IsNullOrEmpty(Car.Listing.Description) ? string.Empty : Car.Listing.Description + " ";

        public string PriceDisplay => Car.PriceDisplay;

        /// <summary>What it was marked at before its dealer's closing-down sale, struck through before the price; empty when not on sale</summary>
        public string WasPriceDisplay => Car.IsOnSale ? Car.WasPriceDisplay + " " : string.Empty;

        /// <summary>Who to call: the dealer, the racer selling their own, or the player</summary>
        public string SellerDisplay => IsPlayerAd
            ? GameText.Say("paper.ad.seller.player", ("player", Car.PrivateSeller ?? string.Empty))
            : Car.PrivateSeller is { } rival
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
