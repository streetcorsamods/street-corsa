using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Garage;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// The player back from a trip out in the selected car (the street, the diner, the gas station): it drives into the
    /// garage under its own power when it could have driven there, or is simply in its place when it was towed or the
    /// police kept it
    /// </summary>
    public static class DrivingHome
    {
        /// <summary>
        /// The garage's arrival for the selected car; <paramref name="engine"/> is its engine when the trip worked it out
        /// already (the garage works it out otherwise). Null when no car is selected.
        /// </summary>
        public static CarArrival? Arrival(GameState game, Audio.EngineSpec? engine, PartsCatalog? catalog)
        {
            if (game.Player.Cars.FirstOrDefault(c => c.InstanceId == game.Player.SelectedCarInstanceId) is not { } car) return null;
            return new CarArrival(car.InstanceId, engine, Drives(car, catalog));
        }

        /// <summary>
        /// The car would run: home, with gas for the way, nothing broken that keeps it off the road, an engine in it when
        /// its parts are known (the gas station's rule for driving there)
        /// </summary>
        public static bool Drives(Car car, PartsCatalog? catalog)
        {
            if (car.IsImpounded || GasStation.NeedsTow(car)) return false;
            if (catalog != null && car.HasPartsAssigned && car.Engine == null) return false;
            return CarCondition.WhyCannotRace(car, catalog == null ? null : CarCondition.Groups(catalog), counting: false).Count == 0;
        }
    }
}
