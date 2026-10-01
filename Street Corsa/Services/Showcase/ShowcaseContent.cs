using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Controls.Showcase;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Showcase
{
    /// <summary>
    /// What the main screen has to show: the rooms the mods list in their <c>showcase</c> folder (scenes.json) that
    /// are there to be loaded, and the installed cars of the catalog.
    /// </summary>
    public static class ShowcaseContent
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Showcase");

        /// <summary>
        /// The scenes every mod lists, in load order; a later mod listing a scene again sets its wall radius. A scene
        /// is a room by its folder name: a mod's garage (see <see cref="FindKn5(string, IEnumerable{string}, string)"/>)
        /// or an AC showroom.
        /// </summary>
        public static IReadOnlyList<ShowcaseScene> LoadScenes(ModSet mods, string showroomsPath)
        {
            var problems = new List<string>();
            var entries = new List<SceneEntry>();
            foreach (var entry in ModJson.Entries(mods, ContentTypes.Showcase, "scenes", problems))
            {
                SceneEntry? scene;
                try
                {
                    scene = entry.As<SceneEntry>();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    problems.Add($"{entry.Source}: scene {entry.Id}: {ex.Message}; left out");
                    continue;
                }

                if (scene == null) continue;

                var earlier = entries.FindIndex(e => string.Equals(e.Id, scene.Id, StringComparison.OrdinalIgnoreCase));
                if (earlier >= 0) entries[earlier] = scene;
                else entries.Add(scene);
            }

            foreach (var problem in problems) Logger.Warning("Main screen scenes: {Problem}", problem);
            ModProblems.Add("Main screen scenes", problems);
            return Found(entries, GaragesOf(mods), showroomsPath);
        }

        /// <summary>The mods' garage folders, the last mod first: a later mod's room of the same name wins</summary>
        public static List<string> GaragesOf(ModSet mods) =>
            mods.Folders(ContentTypes.Garages).Select(f => f.Dir).Reverse().ToList();

        /// <summary>A room's model by its folder name: a mod's garage, else the AC install's showroom</summary>
        public static string? FindScene(string id) =>
            FindKn5(id, GaragesOf(ModSet.Installed), AppSettings.Instance.ShowroomsPath);

        private sealed record SceneEntry(string? Id, float WallRadius);

        private static List<ShowcaseScene> Found(IEnumerable<SceneEntry> entries, IEnumerable<string> garagesPaths, string showroomsPath)
        {
            var scenes = new List<ShowcaseScene>();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || entry.WallRadius <= 0f)
                {
                    Logger.Warning("Skipped a main screen scene with no id or no wall radius");
                    continue;
                }

                var kn5 = FindKn5(entry.Id, garagesPaths, showroomsPath);
                if (kn5 == null)
                {
                    Logger.Information("Main screen scene {Id} is not installed; left out", entry.Id);
                    continue;
                }

                scenes.Add(new ShowcaseScene(entry.Id, kn5, entry.WallRadius));
            }

            return scenes;
        }

        /// <summary>
        /// The model of a scene: the game's own garage by that id, else the AC showroom, whose model is not always
        /// spelled like its folder
        /// </summary>
        public static string? FindKn5(string id, IEnumerable<string> garagesPaths, string showroomsPath)
        {
            // The id is content (a mod's file): it has to be one folder name, not a path out of the room folders
            if (!PathNames.IsSafeSegment(id)) return null;

            foreach (var root in garagesPaths.Append(showroomsPath))
            {
                var folder = Path.Combine(root, id);
                if (!Directory.Exists(folder)) continue;

                var named = Path.Combine(folder, id + ".kn5");
                if (File.Exists(named)) return named;

                var any = Directory.EnumerateFiles(folder, "*.kn5", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (any != null) return any;
            }

            return null;
        }

        /// <summary>
        /// The installed cars of the catalog (as <see cref="Catalog.InstalledCars.Only"/> finds them), each with its
        /// folder and the name it is shown by
        /// </summary>
        public static IReadOnlyList<ShowcaseCar> Cars(IEnumerable<CarDefinition> installed, string carsPath) =>
            installed
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => new ShowcaseCar(c.Id, Path.Combine(carsPath, c.Id), Title(c), c.AvailableSkins?.ToList() ?? []))
                .ToList();

        /// <summary>The year in front of the name, unless the name already has it</summary>
        public static string Title(CarDefinition car)
        {
            var name = string.IsNullOrWhiteSpace(car.Name) ? car.Id : car.Name.Trim();
            return car.Year is { } year && !name.Contains(year.ToString()) ? $"{year} {name}" : name;
        }
    }
}
