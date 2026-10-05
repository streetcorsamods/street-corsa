using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>Why the player cannot go out on the streets, in words for them, and the title to say it under</summary>
    public readonly record struct NotStreetReady(string Why, string Title);

    /// <summary>
    /// The one rule for going out on the streets (the racing hub, the diner, the strip) in the selected car: there is
    /// one, the catalog has it, it is home, and it would race there (<see cref="DrivingHome.WhyWontRun"/>, the tank
    /// counted: nothing broken, gas in it, an engine in it) and the engine starts
    /// </summary>
    public static class StreetReady
    {
        /// <summary>
        /// Why the player cannot go out now; null when they can. Everything but whether the engine starts, which takes
        /// the dyno (<see cref="WhyNotAsync"/>).
        /// </summary>
        /// <param name="listed">Whether the catalog has the car: one it lacks cannot be driven</param>
        public static NotStreetReady? WhyNot(GameState game, Func<string, bool> listed, PartsCatalog? catalog) =>
            WhyNot(PracticeRuns.CarOf(game), listed, catalog);

        /// <summary>
        /// <see cref="WhyNot(GameState, Func{string, bool}, PartsCatalog?)"/> and whether the engine starts, worked out off
        /// the caller's thread on a copy of the car
        /// </summary>
        public static async Task<NotStreetReady?> WhyNotAsync(GameState game, Func<string, bool> listed, ICarPartsService parts)
        {
            var car = PracticeRuns.CarOf(game);
            var catalog = parts.IsAvailable ? parts.Catalog : null;
            if (WhyNot(car, listed, catalog) is { } why) return why;
            if (catalog == null || car!.Engine == null) return null;

            var snapshot = car.Snapshot();
            var report = await Task.Run(() => parts.Evaluate(snapshot));
            return EngineSpecs.ProblemOf(report) is { } problem ? WontRun([EngineSpecs.WontRun(problem)]) : null;
        }

        private static NotStreetReady? WhyNot(Car? car, Func<string, bool> listed, PartsCatalog? catalog)
        {
            if (car == null) return new(GameText.Say("streets.no_car"), GameText.Say("streets.no_car.title"));
            if (!listed(car.DefinitionId)) return new(GameText.Say("streets.not_installed"), GameText.Say("streets.not_installed.title"));
            if (car.IsImpounded) return new(GameText.Say("racehub.practice.impounded"), GameText.Say("streets.impounded.title"));

            var problems = DrivingHome.WhyWontRun(car, catalog, counting: true);
            return problems.Count == 0 ? null : WontRun(problems);
        }

        private static NotStreetReady WontRun(List<string> problems) =>
            new(GameText.Say("streets.wont_run", ("problems", string.Join("; ", problems))), GameText.Say("challenge.car_wont_run.title"));
    }
}
