using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// The cars that are in content\cars right now. The catalog outlives the install: a car deleted from the
    /// install stays on its books until the next start-up scan marks it Legacy, and a car that is not there
    /// cannot be sold, raced or handed to an opponent - AC would fail to load it. Whoever picks a catalog car for
    /// something that ends up in AC asks here first: the market's pool, the opponents' cars, the event fallback.
    ///
    /// One listing of the folder per call rather than a Directory.Exists per car.
    ///
    /// A cars folder that is missing or cannot be read (a misconfigured path, an unplugged drive) is not an install
    /// with no cars: taking it for one would hand every opponent of a new game "no car", for good in that save. So a
    /// failed listing falls back to the last listing that worked in this run, and before any has, <see cref="Only"/>
    /// keeps the catalog's cars as they are - their Active status is the last start-up scan's word on what is installed.
    /// </summary>
    public static class InstalledCars
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);

        private static readonly object LastKnownLock = new();
        private static HashSet<string>? _lastKnown;

        /// <summary>
        /// Folder names (car ids) of the installed cars, race copies left out. When the folder cannot be listed: the
        /// last listing that worked, or empty if none has yet.
        /// </summary>
        public static HashSet<string> Ids()
        {
            TryIds(out var ids);
            return ids ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The installed car ids, listed now. False when the cars folder is missing or cannot be read (logged), with
        /// <paramref name="ids"/> the last listing that worked in this run, or null when there has been none.
        /// </summary>
        public static bool TryIds(out HashSet<string>? ids)
        {
            var carsPath = AppSettings.Instance.CarsPath;
            if (AcCarFolder.TryListInstalledCars(carsPath, out var folders, out var error))
            {
                ids = new HashSet<string>(folders.Select(folder => Path.GetFileName(folder)), StringComparer.OrdinalIgnoreCase);
                lock (LastKnownLock) _lastKnown = ids;
                return true;
            }

            lock (LastKnownLock) ids = _lastKnown == null ? null : new HashSet<string>(_lastKnown, StringComparer.OrdinalIgnoreCase);
            Logger.Warning("Could not list the installed cars in {Path} ({Error}); {Fallback}", carsPath, error ?? "unknown error",
                ids == null ? "going by the catalog's active cars" : "going by the last listing that worked");
            return false;
        }

        /// <summary>
        /// The cars of <paramref name="cars"/> that are installed and in the game (game.json's <c>cars</c>,
        /// <see cref="Rules.GameCars"/>), in their order. When the cars folder cannot be listed and no listing has worked
        /// yet in this run, all of <paramref name="cars"/> the game takes (logged by <see cref="TryIds"/>).
        /// </summary>
        /// <param name="notInstalled">How many were left out for not being installed (a car the game does not take is not counted)</param>
        public static List<CarDefinition> Only(IEnumerable<CarDefinition> cars, out int notInstalled)
        {
            notInstalled = 0;
            var game = Rules.GameFile.Installed.Cars;
            var taken = cars.Where(car => InGame(game, car));
            if (!TryIds(out var installed) && installed == null)
                return [.. taken];

            var kept = new List<CarDefinition>();
            foreach (var car in taken)
            {
                if (installed!.Contains(car.Id)) kept.Add(car);
                else notInstalled++;
            }

            return kept;
        }

        /// <summary>Whether the game takes the car (game.json's <c>cars</c>, a mod's cars.json)</summary>
        public static bool InGame(Rules.GameCars game, CarDefinition car) => WhyOut(game, car) == null;

        /// <summary>Why the game does not take the car; null when it does</summary>
        public static Rules.CarOut? WhyOut(Rules.GameCars game, CarDefinition car) =>
            game.WhyOut(new Rules.GameCar(car.Id, car.Brand, car.Name, car.Country, car.Year, car.Class, car.Tags));

        /// <summary>Fewer cars than this in a game, and its lots, its paper and its rivals keep showing the same ones</summary>
        public const int FewCars = 10;

        /// <summary>
        /// Tells in the log how many of the installed cars the game takes and what keeps the rest out, and the player
        /// (<see cref="Mods.ModProblems"/>) when it takes fewer than <see cref="FewCars"/>
        /// </summary>
        public static void Tell(IReadOnlyCollection<CarDefinition> active)
        {
            var game = Rules.GameFile.Installed.Cars;
            var outs = active.Select(car => WhyOut(game, car)).OfType<Rules.CarOut>().ToList();
            var taken = active.Count - outs.Count;
            var why = string.Join(", ", outs.GroupBy(o => o.Because).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"));
            Logger.Information("Cars in the game: {Taken} of {Total} in the catalog{Why}", taken, active.Count, why.Length > 0 ? $" (out: {why})" : "");
            if (taken < FewCars && active.Count > taken)
                Mods.ModProblems.Add("Cars", $"only {taken} of the {active.Count} installed AC cars are in the game: its lots, paper and rivals will keep showing the same ones");
        }
    }
}
