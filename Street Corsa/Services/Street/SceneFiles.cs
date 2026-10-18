using System.IO;
using System.Text.Json;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// What the scenes' files have alike, read the one way: the file itself (every scene's), and for the indoor scenes (a
    /// warehouse's warehouse.json, a part shop's shop.json) the model next to the file, the lamps of every hour, the glow
    /// of the self-lit glass and the pictures a look wears
    /// </summary>
    internal static class SceneFiles
    {
        /// <summary>
        /// The scene in <paramref name="file"/>, made by <paramref name="build"/> from what the file says (names whatever their
        /// case, comments and trailing commas allowed), its marks and paths given to <paramref name="marked"/> for a scene that
        /// has them; null when the file is missing or broken (logged, and in the mods' problems under <paramref name="area"/>)
        /// </summary>
        /// <param name="what">The kind of scene, for the log ("gas station")</param>
        internal static TScene? Load<TEntry, TScene>(string file, string area, string what, IAppLogger logger, Func<TEntry?, TScene?> build,
            Func<TScene, Scripting.SceneMarks, TScene>? marked = null) where TScene : class
        {
            try
            {
                if (!File.Exists(file)) return null;
                using var document = Scripting.SceneMarks.Parse(file);
                if (build(document.RootElement.Deserialize<TEntry>(ModJson.Options)) is not { } scene) return null;
                if (marked == null) return scene;
                var problems = new List<string>();
                scene = marked(scene, Scripting.SceneMarks.Read(document.RootElement, file, problems));
                ModProblems.Add(area, problems);
                return scene;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not read the " + what + " {Path}", file);
                ModProblems.Add(area, $"{file} could not be read ({ex.Message}); left out");
                return null;
            }
        }

        /// <summary>A scene that is left out: in the log, and in the mods' problems for the player under <paramref name="area"/></summary>
        internal static void LeftOut(IAppLogger logger, string area, string problem)
        {
            logger.Warning("{Problem}", problem);
            ModProblems.Add(area, problem);
        }

        /// <summary>
        /// The scene's model in <paramref name="folder"/> (<paramref name="model"/>, else <paramref name="fallback"/>); false,
        /// and the scene left out, when it names one outside the folder or it is not there
        /// </summary>
        /// <param name="scene">The scene as the player is told of it ("the warehouse x")</param>
        internal static bool TryModel(string folder, string? model, string fallback, string scene, IAppLogger logger, string area, out string kn5)
        {
            if (!PathNames.TryCombineUnder(folder, model ?? fallback, out kn5))
            {
                LeftOut(logger, area, $"{scene} names a model outside its folder ({model}); left out");
                return false;
            }

            if (!File.Exists(kn5))
            {
                LeftOut(logger, area, $"{scene} has no model {kn5}; left out");
                return false;
            }

            return true;
        }

        /// <summary>
        /// A scene that is shown without its picture, told in the log: its model is named outside its folder
        /// (<paramref name="kn5"/> empty) or is not there (<paramref name="hasModel"/> false). Nothing for one that has it.
        /// </summary>
        /// <param name="what">The kind of scene, for the log ("gas station")</param>
        internal static void WarnModel(IAppLogger logger, string what, string id, string model, string kn5, bool hasModel)
        {
            if (kn5.Length == 0)
                logger.Warning("The " + what + " {Id} names a model outside its folder ({Model}): it is shown without its picture", id, model);
            else if (!hasModel)
                logger.Warning("The " + what + " {Id} has no model {Path}: it is shown without its picture", id, kn5);
        }

        /// <summary>The lights of the day with the scene's lamps: white and the same at every hour, only scaled by it (a look colours them)</summary>
        internal static Dictionary<StreetLight, StreetLighting> Lights(Dictionary<string, SceneLighting.LightEntry?>? lights,
            List<SceneLighting.LampEntry?>? lamps, string id, IAppLogger logger)
        {
            var white = (lamps ?? []).Select(l => l == null ? null : l with { Color = null }).ToList();
            return SceneLighting.Read(lights, id, logger, cubemapAmbient: 0f, sharedLamps: white);
        }

        /// <summary>How bright the self-lit glass glows at each light of the day; one named for no time of day is skipped</summary>
        internal static Dictionary<StreetLight, float> Glow(Dictionary<string, float>? entries)
        {
            var glows = new Dictionary<StreetLight, float>();
            foreach (var (name, glow) in entries ?? [])
            {
                if (SceneLighting.TryParse(name, out var which)) glows[which] = glow;
            }

            return glows;
        }

        /// <summary>
        /// The surfaces of a look, material to picture. The viewport points each material at a texture of the model by this
        /// name: a name that is not a plain file name (a path out of the folder, a device) is dropped here, so no model ever
        /// asks the renderer for one.
        /// </summary>
        /// <param name="what">The kind of scene, for the log ("warehouse")</param>
        internal static Dictionary<string, string> Surfaces(Dictionary<string, string?>? surfaces, string what, string id, IAppLogger logger)
        {
            var kept = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (material, picture) in surfaces ?? [])
            {
                if (picture == null) continue;
                if (PathNames.IsSafeSegment(picture)) kept[material] = picture;
                else logger.Warning("The " + what + " {Id} dresses {Material} in \"{Picture}\", which is no picture name: it keeps its own", id, material, picture);
            }

            return kept;
        }
    }
}
