using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>A car's condition as a screen shows it: a percentage and a word</summary>
    public sealed record ConditionText(string Percent, string Label)
    {
        /// <summary>"Good condition (82%)", or just what is wrong with a car that will not run</summary>
        public string Sentence => Percent == Label ? Label : GameText.Say("market.condition.sentence", ("label", Label), ("percent", Percent));
    }

    /// <summary>
    /// One way to show a car's condition on every screen, on the market's rule: what is worst about the car when
    /// it will not run (totaled, a blown engine), otherwise its one overall condition (<see cref="CarValuation.ConditionOf"/>).
    /// A used-car listing's condition is that same figure, so an ad and the car bought from it read the same.
    /// </summary>
    public static class ConditionDisplay
    {
        public static ConditionText Of(Car car)
        {
            if (CarCondition.IsTotaled(car)) return new ConditionText(GameText.Say("market.condition.totaled"), GameText.Say("market.condition.totaled"));
            if (car.EngineHealth <= 0) return new ConditionText(GameText.Say("market.condition.blown"), GameText.Say("market.condition.blown"));
            return Of(CarValuation.ConditionOf(car));
        }

        /// <summary>An overall condition, 0 to 1</summary>
        public static ConditionText Of(double condition)
        {
            var clamped = Helpers.Unit.Clamp01(condition, ifNotFinite: 0);
            return new ConditionText($"{(int)(clamped * 100)}%", LabelFor(clamped));
        }

        /// <summary>What is in the tank: "Full" until a race says otherwise, then "12 of 60 l" ("1.4 of 60 l" when too low to race on)</summary>
        public static string Fuel(Car car) =>
            CarCondition.FuelLeft(car) is { } left && CarCondition.TankLitres(car) is { } tank
                ? GameText.Say(left < 10 ? "market.fuel.low" : "market.fuel", ("left", left), ("tank", tank))
                : GameText.Say("market.fuel.full");

        public static string LabelFor(double condition) => condition switch
        {
            >= 0.9 => GameText.Say("market.condition.excellent"),
            >= 0.75 => GameText.Say("market.condition.good"),
            >= 0.6 => GameText.Say("market.condition.fair"),
            >= 0.4 => GameText.Say("market.condition.poor"),
            _ => GameText.Say("market.condition.very_poor")
        };
    }
}
