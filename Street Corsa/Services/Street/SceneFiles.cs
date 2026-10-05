using System.IO;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// What the indoor scenes' files (a warehouse's warehouse.json, a part shop's shop.json) have alike, read the one way:
    /// the model next to the file, the lamps of every hour, the glow of the self-lit glass and the pictures a look wears
    /// </summary>
    internal static class SceneFiles
    {
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
