using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.PartShops
{
    /// <summary>
    /// What a part shop's counter tells the player of the shop's trade with them, the same for a shop's counter and a
    /// machine shop's: the closing-down sale and its last day, what they get off as a regular; and the same as the clerk
    /// knows it (<see cref="Facts"/>)
    /// </summary>
    public static class PartShopNotice
    {
        /// <summary>Between the lines of the notice under the shop's name</summary>
        public const string Separator = "   ·   ";

        /// <summary>The lines of the notice: the sale on (today the last day, or the day it ends), the regular's discount; none for neither</summary>
        public static IReadOnlyList<string> Lines(PartShopLocation shop, DateTime date)
        {
            var lines = new List<string>();
            if (shop.Status == DealerStatus.ClosingDown && shop.ClosesOn is { } last)
            {
                var values = new TextValues { ["discount"] = Percent(DealerRules.DiscountOn(shop, date)), ["day"] = last };
                lines.Add(GameText.Installed.Get(DealerRules.DaysLeft(shop, date) == 0 ? "partshop.lot.sale_last_day" : "partshop.lot.sale", values));
            }

            var regular = PartShopRules.RegularDiscountFor(shop.PlayerSpent);
            if (regular > 0)
            {
                var key = regular >= PartShopRules.GoodCustomerDiscount ? "partshop.good_customer" : "partshop.regular";
                lines.Add(GameText.Installed.Get(key, new TextValues { ["discount"] = Percent(regular) }));
            }

            return lines;
        }

        /// <summary>The notice in one line; empty for a shop the roster no longer has</summary>
        public static string Text(PartShopLocation? shop, DateTime date) => shop == null ? string.Empty : string.Join(Separator, Lines(shop, date));

        /// <summary>The shop's trade with the player as the clerk is told it, a fact a line: the sale, the discount, what the player has spent</summary>
        public static IEnumerable<string> Facts(PartShopLocation shop, DateTime date)
        {
            if (shop.Status == DealerStatus.ClosingDown && shop.ClosesOn is { } last)
                yield return GameText.Say("chat.fact.shop_sale", ("discount", Percent(DealerRules.DiscountOn(shop, date))), ("day", last));

            var regular = PartShopRules.RegularDiscountFor(shop.PlayerSpent);
            if (regular > 0) yield return GameText.Say("chat.fact.shop_regular", ("discount", Percent(regular)));

            yield return shop.PlayerSpent > 0
                ? GameText.Say("chat.fact.shop_spent", ("spent", new Helpers.Money(shop.PlayerSpent)))
                : GameText.Say("chat.fact.shop_new_customer");
        }

        private static int Percent(decimal share) => (int)Math.Round(share * 100);
    }
}
