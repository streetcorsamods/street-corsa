using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.UsedParts;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>
    /// A used engine kit in the classifieds (roadmap step 40): the engine in bold, how many parts and what it is missing,
    /// the price and who sells it. Clicked, the player is asked whether to buy it.
    /// </summary>
    public sealed class KitAdViewModel
    {
        public KitAdViewModel(KitAd ad, RelayCommand buy)
        {
            Ad = ad;
            BuyCommand = buy;
            Name = PartOfferViewModel.KitName("used", ad.Name);
            Headline = GameText.Say("paper.ad.kit.headline", ("engine", ad.Name)).ToUpper(Locale.Culture);
        }

        public KitAd Ad { get; }

        /// <summary>The kit as the lists name it, for the question whether to buy it</summary>
        public string Name { get; }

        /// <summary>The engine in the ad's bold run-in</summary>
        public string Headline { get; }

        /// <summary>How many parts, how worn, what it is missing, with the space after it</summary>
        public string Blurb => PartOfferViewModel.UsedKitDetail(Ad.Parts, Ad.Missing) + ". ";

        public string PriceDisplay => Money.Text(Ad.AskingPrice);

        public string SellerDisplay => GameText.Say("paper.ad.seller.part", ("seller", Ad.SellerName));

        /// <summary>Buy it (clicking the ad), once the player says yes</summary>
        public RelayCommand BuyCommand { get; }
    }
}
