using System.IO;
using System.Text.RegularExpressions;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Catalog;

namespace StreetCorsa.Services.Police
{
    /// <summary>A police car installed in AC, and the liveries that make it one</summary>
    public sealed record PoliceCar(string CarId, IReadOnlyList<string> Skins);

    /// <summary>
    /// Finds the police car in the AC install. The game's is the 1974 Dodge Monaco Police (Stereo's mod on Overtake),
    /// which the player installs; no police car ships with AC.
    ///
    /// A livery is a police livery when its <c>ui_skin.json</c> says so: <c>"street_corsa_police": true</c>, a field
    /// of the game's own that AC and Content Manager ignore. Any car can be made a police car that way (a
    /// black-and-white skin on an installed sedan); only its marked liveries go to the police, and they never go to a
    /// lot or a rival (<see cref="CarImportService"/> leaves them out, and leaves a car that has nothing else out of the
    /// catalog). A Monaco is preferred over any other.
    ///
    /// The install is looked at once a run: the player adds cars with the game closed. Which of its police cars comes
    /// is picked each time, by the rules of the game in play.
    /// </summary>
    public static class PoliceCars
    {
        /// <summary>The field in a skin's ui_skin.json that makes it a police livery</summary>
        public const string SkinMarker = "street_corsa_police";

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Police");
        private static readonly Regex Marker = new("\"" + SkinMarker + @"""\s*:\s*true", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly object Lock = new();
        private static List<PoliceCar>? _installed;
        private static PoliceCar? _lastPicked;

        /// <summary>
        /// The install's police car the rules of the game in play like best (<c>police.preferredCars</c>), or null when
        /// there is none or the game has no police (<see cref="Rules.SystemRules.Police"/>): no police come then
        /// </summary>
        public static PoliceCar? Installed()
        {
            if (!Rules.RuleBook.Current.Systems.Police) return null;

            lock (Lock)
            {
                if (_installed == null)
                {
                    try
                    {
                        _installed = All(AppSettings.Instance.CarsPath);
                        if (_installed.Count == 0) Logger.Information("No police car installed: street races go without police");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("Could not look for a police car: {Error}", ex.Message);
                        _installed = [];
                    }
                }

                var picked = Best(_installed);
                if (picked != null && picked != _lastPicked)
                    Logger.Information("Police car: {Car} with {Count} livery(ies): {Skins}", picked.CarId, picked.Skins.Count, string.Join(", ", picked.Skins));
                _lastPicked = picked;
                return picked;
            }
        }

        /// <summary>The police car in a cars folder the rules like best, or null</summary>
        public static PoliceCar? Find(string carsPath) => Best(All(carsPath));

        /// <summary>Every police car in a cars folder, by folder name</summary>
        private static List<PoliceCar> All(string carsPath)
        {
            if (!Directory.Exists(carsPath)) return [];

            return AcCarFolder.InstalledCars(carsPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(Look).OfType<PoliceCar>().ToList();
        }

        /// <summary>The one the rules like best; the first of them when they like none better</summary>
        private static PoliceCar? Best(IEnumerable<PoliceCar> cars)
        {
            PoliceCar? best = null;
            var bestScore = 0;
            foreach (var car in cars)
            {
                var score = 1 + Preference(car.CarId);
                if (score <= bestScore) continue;
                best = car;
                bestScore = score;
            }

            return best;
        }

        /// <summary>
        /// The skin folder is a police livery: its ui_skin.json carries <see cref="SkinMarker"/>. One larger than any
        /// skin's description (<see cref="Catalog.CarImportService.MaxUiJsonBytes"/>) is not read, and is no police livery.
        /// </summary>
        public static bool IsPoliceSkin(string skinFolder)
        {
            var path = Path.Combine(skinFolder, "ui_skin.json");
            try
            {
                var file = new FileInfo(path);
                return file.Exists && file.Length <= Catalog.CarImportService.MaxUiJsonBytes && Marker.IsMatch(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Every livery the car has is a police one: it is nothing but a police car</summary>
        public static bool IsPoliceCar(string folder)
        {
            var skins = SkinFolders(folder);
            return skins.Count > 0 && skins.All(IsPoliceSkin);
        }

        /// <summary>One car folder as a police car, or null</summary>
        private static PoliceCar? Look(string folder)
        {
            var policeSkins = SkinFolders(folder).Where(IsPoliceSkin).Select(Path.GetFileName).OfType<string>().ToList();
            return policeSkins.Count == 0 ? null : new PoliceCar(Path.GetFileName(folder), policeSkins);
        }

        /// <summary>How much the rules' preferred police cars (<c>police.preferredCars</c>) rank this one up: the first word most</summary>
        private static int Preference(string id)
        {
            var preferred = Rules.RuleBook.Current.Police.PreferredCars ?? [];
            for (var i = 0; i < preferred.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(preferred[i]) && id.Contains(preferred[i], StringComparison.OrdinalIgnoreCase))
                    return 10 * (preferred.Length - i);
            }

            return 0;
        }

        private static List<string> SkinFolders(string folder)
        {
            var skinsFolder = Path.Combine(folder, "skins");
            try
            {
                return Directory.Exists(skinsFolder)
                    ? Directory.GetDirectories(skinsFolder).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()
                    : new List<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// The police a street race draws, rolled now: a road race only, with a police car installed that neither
        /// racer drives (the two share a folder otherwise), one car for each racer (<see cref="PoliceRules.CopsPerRace"/>)
        /// on a track with a pit box for each. The more known of the two racers is the one the police have heard of.
        /// Null when none come.
        /// </summary>
        public static RacePolice? Patrol(PoliceCar? car, bool isRoadRace, DateTime time, int reputation, bool isPinkSlip,
            decimal cashWager, string? pitboxes, IEnumerable<string> racingCarIds, Random random)
        {
            if (car == null || !isRoadRace) return null;
            if (racingCarIds.Any(id => string.Equals(id, car.CarId, StringComparison.OrdinalIgnoreCase))) return null;

            if (int.TryParse(pitboxes, out var boxes) && boxes > 0 && boxes < 2 + PoliceRules.CopsPerRace) return null;
            return Roll(PoliceRules.Chance(time, reputation, isPinkSlip, cashWager), PoliceRules.CopsPerRace, car, random);
        }

        /// <summary>
        /// Whether a patrol comes, and if so what it sends: <paramref name="cops"/> cars of <paramref name="car"/>, each
        /// in its own livery where there are enough. Two in three are speed traps parked round the track
        /// (<see cref="PoliceRules.TrapShare"/>); the rest a patrol turning up behind somewhere between a quarter and 60%
        /// of the way round.
        /// </summary>
        public static RacePolice? Roll(double chance, int cops, PoliceCar? car, Random random)
        {
            if (car == null || car.Skins.Count == 0 || cops <= 0 || chance <= 0) return null;
            if (random.NextDouble() >= chance) return null;

            var first = random.Next(car.Skins.Count);
            return new RacePolice
            {
                CarId = car.CarId,
                Skins = Enumerable.Range(0, cops).Select(i => car.Skins[(first + i) % car.Skins.Count]).ToList(),
                SpotShare = Math.Round(0.25 + random.NextDouble() * 0.35, 3),
                Traps = random.NextDouble() < PoliceRules.TrapShare
            };
        }
    }
}
