using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>
    /// A used part ad in the classifieds: the part in bold, the ad's few words, the price, who sells it, and a word when
    /// it goes on the car the player has in the garage. Its condition and what comes with it are in its tooltip.
    /// </summary>
    public sealed class PartAdViewModel : ObservableObject
    {
        public PartAdViewModel(PartAd ad, PartDefinition definition, bool fits, RelayCommand look)
        {
            Ad = ad;
            Definition = definition;
            Fits = fits;
            LookCommand = look;
            Name = definition.DisplayName ?? definition.Name;
            Headline = Name.ToUpper(Locale.Culture);
        }

        public PartAd Ad { get; }
        public PartDefinition Definition { get; }

        /// <summary>The part's own name, for the look at it</summary>
        public string Name { get; }

        /// <summary>The part in the ad's bold run-in</summary>
        public string Headline { get; }

        /// <summary>The ad's few words (<see cref="Services.Market.AdBlurbs"/>), with the space after them when there are any</summary>
        public string Blurb => string.IsNullOrEmpty(Ad.Description) ? string.Empty : Ad.Description + " ";

        /// <summary>Offers the ad's words to the player's AI, as the paper first shows it</summary>
        public Action? Polish { get; init; }

        /// <summary>The AI's words came: the ad prints them</summary>
        public void BlurbRewritten() => OnPropertyChanged(nameof(Blurb));

        public string PriceDisplay => Money.Text(Ad.AskingPrice);
        public string SellerDisplay => GameText.Say("paper.ad.seller.part", ("seller", Ad.SellerName));

        /// <summary>It goes somewhere on the car selected in the garage</summary>
        public bool Fits { get; }

        /// <summary>The pencilled note that it fits, after the seller's name; empty when it does not</summary>
        public string FitsDisplay => Fits ? ". " + GameText.Say("paper.ad.fits") : string.Empty;

        /// <summary>Its kind, condition, and what comes with it</summary>
        public string Details => GameText.Say("paper.ad.part.details",
            ("kind", PartKinds.GroupName(PartKinds.GroupOf(Definition))), ("part", PartTrees.Describe(Ad.Part)));

        /// <summary>Have a look at the part (clicking the ad)</summary>
        public RelayCommand LookCommand { get; }
    }
}
