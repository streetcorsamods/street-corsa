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

        public override decimal Round(decimal price) => Haggle.RoundUp(price, StreetCorsa.Rules.RuleBook.Current.Ai.Talk.CarPriceStep);
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

        public override decimal Round(decimal price) => Haggle.RoundUp(price, price >= 100 ? StreetCorsa.Rules.RuleBook.Current.Ai.Talk.PartPriceStep : 1);
    }
}
