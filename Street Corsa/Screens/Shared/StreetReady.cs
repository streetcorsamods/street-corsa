using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// The one rule for going out on the streets (the diner, the strip) in the selected car: there is one, it is home, and
    /// it would race there (<see cref="DrivingHome.WhyWontRun"/>, the tank counted: nothing broken, gas in it, an engine in
    /// it) and the engine starts
    /// </summary>
    public static class StreetReady
    {
        /// <summary>
        /// Why the player cannot go out now, in words for them; null when they can. Everything but whether the engine
        /// starts, which takes the dyno (<see cref="WhyNotAsync"/>).
        /// </summary>
        /// <param name="listed">Whether the catalog has the car: one it lacks cannot be driven</param>
        public static string? WhyNot(GameState game, Func<string, bool> listed, PartsCatalog? catalog) => WhyNot(CarOf(game), listed, catalog);

        /// <summary>
        /// <see cref="WhyNot(GameState, Func{string, bool}, PartsCatalog?)"/> and whether the engine starts, worked out off
        /// the caller's thread on a copy of the car
        /// </summary>
        public static async Task<string?> WhyNotAsync(GameState game, Func<string, bool> listed, ICarPartsService parts)
        {
            var car = CarOf(game);
            var catalog = parts.IsAvailable ? parts.Catalog : null;
            if (WhyNot(car, listed, catalog) is { } why) return why;
            if (catalog == null || car!.Engine == null) return null;

            var snapshot = car.Snapshot();
            var report = await Task.Run(() => parts.Evaluate(snapshot));
            if (report is { Runs: true }) return null;

            var problem = report?.Problem ?? GameText.Say(report == null ? "enginespecs.incomplete" : "enginespecs.no_power");
            return WontRun([GameText.Say("garage.station.engine_wont_run", ("problem", problem))]);
        }

        private static Car? CarOf(GameState game) =>
            game.Player.SelectedCarInstanceId is { } id ? game.Player.Cars?.FirstOrDefault(c => c.InstanceId == id) : null;

        private static string? WhyNot(Car? car, Func<string, bool> listed, PartsCatalog? catalog)
        {
            if (car == null || !listed(car.DefinitionId)) return GameText.Say("streets.no_car");
            if (car.IsImpounded) return GameText.Say("racehub.practice.impounded");

            var problems = DrivingHome.WhyWontRun(car, catalog, counting: true);
            return problems.Count == 0 ? null : WontRun(problems);
        }

        private static string WontRun(List<string> problems) => GameText.Say("streets.wont_run", ("problems", string.Join("; ", problems)));
    }
}
