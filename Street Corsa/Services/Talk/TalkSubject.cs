using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.Shared;

namespace StreetCorsa.Services.Talk
{
    /// <summary>
    /// Something for sale a talk is about: its key in the seller's deals, its price now (which a deal changes, so every
    /// way of buying it pays what was agreed), and how long it has been on sale
    /// </summary>
    public abstract class TalkSubject
    {
        /// <summary>"car:&lt;listing id&gt;", "rivalcar:&lt;ad id&gt;", "part:&lt;ad id&gt;"</summary>
        public abstract string Thing { get; }

        /// <summary>What the player calls it in a stage line: "1957 Chevrolet Bel Air", "Edelbrock intake"</summary>
        public abstract string Name { get; }

        public abstract decimal Price { get; set; }

        public abstract DateTime OnSaleSince { get; }

        /// <summary>The price first asked, kept on the ad itself the first time it is talked over (the hard floor's base)</summary>
        public abstract decimal? FirstAsked { get; set; }

        /// <summary>The most this seller comes down from the first price, drawn once and kept on the ad</summary>
        public abstract double? MostOff { get; set; }

        /// <summary>A price up to the seller's round figures</summary>
        public abstract decimal Round(decimal price);
    }

    /// <summary>A car in the paper or on a lot: a listing (a dealer's or a private seller's) or a rival's own ad</summary>
    public sealed class CarSubject(UsedCarListingViewModel car) : TalkSubject
    {
        public UsedCarListingViewModel Car { get; } = car;

        public override string Thing => Car.RivalAd is { } ad ? TalkPeople.RivalCarKey(ad) : TalkPeople.CarKey(Car.Listing);

        public override string Name => $"{Car.YearDisplay} {Car.DisplayName}";

        public override decimal Price
        {
            get => Car.RivalAd?.AskingPrice ?? Car.Listing.Price;
            set
            {
                // The ad the player buys from, and the listing the screens show it by
                if (Car.RivalAd is { } ad) ad.AskingPrice = value;
                Car.Listing.Price = value;
            }
        }

        public override DateTime OnSaleSince => Car.RivalAd?.PostedDate ?? Car.Listing.ListedDate;

        public override decimal? FirstAsked
        {
            get => Car.RivalAd?.FirstAsked ?? Car.Listing.FirstAsked;
            set
            {
                if (Car.RivalAd is { } ad) ad.FirstAsked = value;
                Car.Listing.FirstAsked = value;
            }
        }

        public override double? MostOff
        {
            get => Car.RivalAd?.MostOff ?? Car.Listing.MostOff;
            set
            {
                if (Car.RivalAd is { } ad) ad.MostOff = value;
                Car.Listing.MostOff = value;
            }
        }

        public override decimal Round(decimal price) => Haggle.RoundUp(price, StreetCorsa.Rules.RuleBook.Current.Ai.Talk.CarPriceStep);
    }

    /// <summary>
    /// The player's own car in the paper, talked over with the buyer who answered the ad: the price is the buyer's offer,
    /// which goes up (never down) as they are talked round, and is gone when they walk away (<see cref="Walk"/>)
    /// </summary>
    public sealed class BuyerSubject(CarSaleAd ad, UsedCarListingViewModel car) : TalkSubject
    {
        private decimal _last = ad.Offer?.Amount ?? 0m;

        public CarSaleAd Ad { get; } = ad;

        /// <summary>The car as the ad shows it: what the buyer is told about it</summary>
        public UsedCarListingViewModel Car { get; } = car;

        /// <summary>The offer talked over, while it stands</summary>
        public CarOffer? Offer { get; } = ad.Offer;

        /// <summary>The buyer walked away: the offer is out of the ad</summary>
        public bool IsGone => Offer == null || !ReferenceEquals(Ad.Offer, Offer);

        // By the offer talked over, kept: the buyer gone, the deal is still theirs
        public override string Thing { get; } = TalkPeople.BuyerKey(ad);

        public override string Name => $"{Car.YearDisplay} {Car.DisplayName}";

        /// <summary>What the buyer offers now</summary>
        public override decimal Price
        {
            get => Offer?.Amount ?? _last;
            set
            {
                _last = value;
                if (Offer != null) Offer.Amount = value;
            }
        }

        /// <summary>What the ad asks</summary>
        public decimal Asking => Ad.AskingPrice;

        public override DateTime OnSaleSince => Ad.PostedDate;

        /// <summary>What the buyer first offered (the hard ceiling's base)</summary>
        public override decimal? FirstAsked
        {
            get => Offer?.FirstAmount;
            set
            {
                if (Offer != null) Offer.FirstAmount = value;
            }
        }

        /// <summary>The most this buyer comes up from the first offer, drawn once and kept on it</summary>
        public override double? MostOff
        {
            get => Offer?.MostOver;
            set
            {
                if (Offer != null) Offer.MostOver = value;
            }
        }

        /// <summary>A buyer's figures go down to round ones</summary>
        public override decimal Round(decimal price) => Haggle.RoundDown(price, StreetCorsa.Rules.RuleBook.Current.Ai.Talk.CarPriceStep);

        /// <summary>The buyer wants no more of it: their offer leaves the ad (the next caller may come another day)</summary>
        public void Walk()
        {
            if (Offer != null && ReferenceEquals(Ad.Offer, Offer)) Ad.Offer = null;
        }
    }

    /// <summary>A part in the paper</summary>
    public sealed class PartSubject(PartAd ad, string name) : TalkSubject
    {
        public PartAd Ad { get; } = ad;

        public override string Thing => TalkPeople.PartKey(Ad);

        public override string Name { get; } = name;

        public override decimal Price
        {
            get => Ad.AskingPrice;
            set => Ad.AskingPrice = value;
        }

        public override DateTime OnSaleSince => Ad.PostedDate;

        public override decimal? FirstAsked
        {
            get => Ad.FirstAsked;
            set => Ad.FirstAsked = value;
        }

        public override double? MostOff
        {
            get => Ad.MostOff;
            set => Ad.MostOff = value;
        }

        public override decimal Round(decimal price) => Haggle.RoundUp(price, price >= 100 ? StreetCorsa.Rules.RuleBook.Current.Ai.Talk.PartPriceStep : 1);
    }
}
