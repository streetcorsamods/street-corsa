using System.Collections.Concurrent;
using System.IO;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Scripting
{
    /// <summary>
    /// The garage's marks for the scripts, from its garages/&lt;id&gt;/scene.json (the last mod that has one for the
    /// garage played from, as with its model): where a car comes in from, <c>door</c>, and whatever else a mod names.
    /// Without a file, the door is 7 metres behind the car's place, out of the picture.
    /// </summary>
    public static class GarageScene
    {
        public const string FileName = "scene.json";

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Garage");

        private static readonly SceneMarks Fallback = SceneMarks.None.With(new Dictionary<string, MarkSpec> { ["door"] = new(Ahead: -7f) });

        private static readonly ContentCache<ConcurrentDictionary<string, SceneMarks>> Read = new(() => new(StringComparer.OrdinalIgnoreCase));

        /// <summary>The marks of the garage <paramref name="garageId"/> in the mods next to the game, read once</summary>
        public static SceneMarks For(string garageId) => Read.Value.GetOrAdd(garageId, id => Load(ModSet.Installed, id));

        /// <summary>The marks of the garage <paramref name="garageId"/>; the fallback ones for an id that is no plain folder name</summary>
        public static SceneMarks Load(ModSet mods, string garageId)
        {
            if (!Helpers.PathNames.IsSafeSegment(garageId)) return Fallback;

            foreach (var (_, dir) in mods.Folders(ContentTypes.Garages).Reverse())
            {
                var file = Path.Combine(dir, garageId, FileName);
                if (!File.Exists(file)) continue;

                var problems = new List<string>();
                var marks = SceneMarks.ReadFile(file, problems).With(Fallback.Marks);
                foreach (var problem in problems) Logger.Warning("{Problem}", problem);
                ModProblems.Add("Garages", problems);
                return marks;
            }

            return Fallback;
        }
    }
}
