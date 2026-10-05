using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// Whether a car's body is fit to be painted: the painter sprays over no rust and no dents or scratches, so they are
    /// the repair shop's first (<see cref="RepairShop.BeforePaint"/>). The garage's Paint button says so before the car
    /// goes; the paint booth too, should it get there anyway.
    /// </summary>
    public static class PaintFirst
    {
        /// <summary>Why the car cannot be painted yet, for the player; null when it can</summary>
        public static string? WhyNot(Car car)
        {
            var needs = RepairShop.BeforePaint(car);
            if (needs.Count == 0) return null;
            var rust = needs.Contains(RepairShop.Rust);
            var body = needs.Contains(RepairShop.Body);
            return GameText.Say(rust && body ? "booth.ui.body_first.both" : rust ? "booth.ui.body_first.rust" : "booth.ui.body_first.body");
        }

        public static string Title => GameText.Say("booth.ui.body_first.title");
    }
}
