using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// A car's horsepower wherever the game weighs one car against another: what the parts on the car make on the
    /// dyno (<see cref="Car.PowerHp"/>) when it has been on one, else the power its spec sheet states (read by
    /// <see cref="AcSpecs"/>, the one parser for ui_car.json numbers). An engine swap shows in the dyno figure, which
    /// is why that one comes first.
    /// </summary>
    public static class CarPower
    {
        /// <summary>The car's dyno figure when it is a number above 0, else its spec sheet's power; null without either</summary>
        public static double? Of(Car? car, CarDefinition? definition) => Of(car?.PowerHp, definition);

        /// <summary>A dyno figure when it is a number above 0, else the spec sheet's power; null without either</summary>
        public static double? Of(double? dynoHp, CarDefinition? definition) =>
            dynoHp is { } hp && hp > 0 && double.IsFinite(hp) ? hp : AcSpecs.ParsePower(definition?.Specs?.Bhp);
    }
}
