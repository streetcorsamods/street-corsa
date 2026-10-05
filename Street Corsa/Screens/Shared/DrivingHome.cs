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
        private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan IdleBeforePullingAway = TimeSpan.FromSeconds(1.2);

        /// <summary>
        /// Before driving off (from the pump, out of a bay at the diner): the starter, and a moment at idle before pulling
        /// away. An engine that will not catch (or has no sound to start) is waited on a few seconds at most: the car drives
        /// off all the same. <paramref name="stillThere"/> false (the screen went) stops the waiting.
        /// </summary>
        public static async Task StartUpAsync(Audio.EngineRunner engine, Func<bool> stillThere)
        {
            if (!engine.IsRunning) engine.Toggle();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (stillThere() && engine.State != Audio.EngineState.Running && clock.Elapsed < StartWait)
                await Task.Delay(50);

            await Task.Delay(IdleBeforePullingAway);
            if (stillThere() && engine.State != Audio.EngineState.Running) engine.StartRunning();
        }

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
        /// The car would run: home, with gas for the way, and nothing that keeps it off the road (<see cref="WhyWontRun"/>)
        /// </summary>
        public static bool Drives(Car car, PartsCatalog? catalog) =>
            !car.IsImpounded && !GasStation.NeedsTow(car) && WhyWontRun(car, catalog).Count == 0;

        /// <summary>
        /// What keeps the car off the road, gas aside: anything broken, and no engine in it when its parts are known
        /// (<paramref name="catalog"/>; without it the car is judged by its overall health). The one rule for driving to
        /// the gas station and back home.
        /// </summary>
        /// <param name="counting">True counts the tank in too (<see cref="CarCondition.WhyCannotRace"/>): out on the streets
        /// the car races</param>
        public static List<string> WhyWontRun(Car car, PartsCatalog? catalog, bool counting = false)
        {
            var problems = CarCondition.WhyCannotRace(car, catalog == null ? null : CarCondition.Groups(catalog), counting);
            if (catalog != null && car.HasPartsAssigned && car.Engine == null) problems.Add(Services.Text.GameText.Say("station.ui.no_engine"));
            return problems;
        }
    }
}
