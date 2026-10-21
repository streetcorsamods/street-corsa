using AcTools.Numerics;
using AcTools.Render.Kn5SpecificForwardDark.Lights;
using StreetCorsa.Controls.Street;
using StreetCorsa.Logging;

namespace StreetCorsa.Controls;

/// <summary>What the rooms of the game's own scenes (the warehouse, the part shop) share: dressed without loading them again</summary>
internal static class SceneDressing
{
    /// <summary>
    /// Dresses the standing room as <paramref name="dressing"/> says: its light and lamps (the ones hung before,
    /// <paramref name="lamps"/>, taken down first), then each material pointed at its picture (every picture a look can wear
    /// is in the model already, so this is a pointer changed, not a texture loaded) and the self-lit ones set glowing
    /// </summary>
    public static void Apply(GarageRenderer renderer, LotDressing dressing, List<DarkLightBase> lamps, IAppLogger logger)
    {
        foreach (var lamp in lamps) renderer.RemoveLight(lamp);
        lamps.Clear();
        lamps.AddRange(SceneLights.Apply(renderer, dressing.Lighting, dressing.Sun));

        if (renderer.ShowroomNode?.OriginalFile is not { } kn5) return;

        var ids = kn5.Materials.Keys.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => (uint)p.i, StringComparer.Ordinal);
        foreach (var (material, picture) in dressing.Pictures)
        {
            if (!ids.TryGetValue(material, out var id) || kn5.Materials[material].GetMappingByName("txDiffuse") is not { } mapping)
            {
                logger.Warning("The room has no material {Material} to dress", material);
                continue;
            }

            // Only a picture the room's file holds: the renderer uploads the file's textures, so any other name
            // (a room without textures included) would leave the material with none
            if (!kn5.Textures.ContainsKey(picture))
            {
                logger.Warning("The room has no picture {Picture} for {Material}", picture, material);
                continue;
            }

            if (mapping.Texture == picture) continue;
            mapping.Texture = picture;
            renderer.RefreshMaterial(kn5, id);
        }

        Glow(renderer, dressing.Glow);
    }

    /// <summary>
    /// The scene's sky dome for the light of the day (<paramref name="wanted"/>); the others, whose names start with
    /// <paramref name="skies"/>, hidden
    /// </summary>
    public static void ShowSky(GarageRenderer renderer, string skies, string wanted)
    {
        if (renderer.ShowroomNode is not { } scene || skies.Length == 0) return;

        foreach (var mesh in scene.Meshes)
        {
            var name = mesh.OriginalNode.Name;
            if (name.StartsWith(skies, StringComparison.Ordinal)) mesh.IsEnabled = name == wanted;
        }
    }

    /// <summary>
    /// The scene's look groups (<see cref="Services.Street.SceneLooks"/>): every mesh under a node named from LOOK_ is shown
    /// only when it is in one of the groups <paramref name="shown"/> (a diner's own roof sign and props), hidden else.
    /// Nothing is touched in a scene with no such node.
    /// </summary>
    public static void ShowLooks(GarageRenderer renderer, IReadOnlyList<string> shown)
    {
        if (renderer.ShowroomNode is not { } scene) return;

        foreach (var mesh in scene.Meshes)
        {
            var name = mesh.OriginalNode.Name;
            if (name.StartsWith(Services.Street.SceneLooks.Prefix, StringComparison.Ordinal))
                mesh.IsEnabled = shown.Any(prefix => Services.Street.SceneLooks.IsIn(name, prefix));
        }
    }

    /// <summary>The self-lit materials of the scene (glass, a sign) glowing as bright as <paramref name="glows"/> says; one it has not, skipped</summary>
    public static void Glow(GarageRenderer renderer, IReadOnlyDictionary<string, float> glows)
    {
        if (glows.Count == 0 || renderer.ShowroomNode?.OriginalFile is not { } kn5) return;

        var ids = kn5.Materials.Keys.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => (uint)p.i, StringComparer.Ordinal);
        foreach (var (material, glow) in glows)
        {
            if (ids.TryGetValue(material, out var id))
                renderer.UpdateMaterialPropertyC(kn5, id, "ksEmissive", new Vec3(glow, glow, glow));
        }
    }
}
