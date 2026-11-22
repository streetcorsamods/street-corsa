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
    /// <paramref name="lamps"/>, kept when the light hangs the same ones, taken down first else), then each material pointed at its picture (every picture a look can wear
    /// is in the model already, so this is a pointer changed, not a texture loaded) and the self-lit ones set glowing
    /// </summary>
    public static void Apply(GarageRenderer renderer, LotDressing dressing, List<DarkLightBase> lamps, IAppLogger logger)
    {
        SceneLights.Relight(renderer, dressing.Lighting, dressing.Sun, lamps);

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
    /// The scene's meshes lying wholly further than <paramref name="reach"/> from (<paramref name="x"/>, <paramref name="z"/>)
    /// on the ground hidden, the sky domes (named from <paramref name="skies"/>) and the far scenery
    /// (<see cref="Services.Street.StreetReach.FarPrefix"/>) left as they are: a scene the size of one corner hides none.
    /// Its 40 m tiles (step 37) are what this hides of a town's stage. The stand-ins for the pieces
    /// (<see cref="Services.Street.StreetReach.StandInPrefix"/>) are hidden the other way round: where the scene stands up
    /// its pieces (<see cref="Services.Street.StreetReach.AroundStand"/> of the point, <see cref="Services.Street.StreetReach.AlongWay"/>
    /// of <paramref name="ways"/>), so a block is its pieces near and its boxes further off, never both.
    /// </summary>
    public static int HideFar(GarageRenderer renderer, float x, float z, string skies, float reach = Services.Street.StreetReach.ModelReach,
        IEnumerable<IReadOnlyList<Services.Station.GroundPoint>>? ways = null)
    {
        if (renderer.ShowroomNode is not { } scene) return 0;

        var segments = (ways ?? []).SelectMany(w => w.Count == 1 ? [(w[0], w[0])] : w.Zip(w.Skip(1))).ToList();
        var hidden = 0;
        foreach (var mesh in scene.Meshes)
        {
            var name = mesh.OriginalNode.Name;
            if (skies.Length > 0 && name.StartsWith(skies, StringComparison.Ordinal)) continue;
            if (mesh.BoundingBox is not { } box || !float.IsFinite(box.Minimum.X) || !float.IsFinite(box.Maximum.X)) continue;

            if (name.StartsWith(Services.Street.StreetReach.StandInPrefix, StringComparison.Ordinal))
            {
                if (!StandsNear(box, x, z, segments)) continue;
                mesh.IsEnabled = false;
                hidden++;
                continue;
            }

            if (name.StartsWith(Services.Street.StreetReach.FarPrefix, StringComparison.Ordinal)) continue;

            // The ground's distance from the point to the box, x and z
            if (DistanceSquared(box, x, z) <= reach * reach) continue;
            mesh.IsEnabled = false;
            hidden++;
        }

        return hidden;
    }

    // The ground's distance from a point to a box, x and z, squared
    private static float DistanceSquared(SlimDX.BoundingBox box, float x, float z)
    {
        var dx = MathF.Max(0f, MathF.Max(box.Minimum.X - x, x - box.Maximum.X));
        var dz = MathF.Max(0f, MathF.Max(box.Minimum.Z - z, z - box.Maximum.Z));
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// Whether a box comes within the reach the pieces are stood up in (<see cref="Services.Street.StreetReach"/>): of the
    /// point, or of a way (each stretch tried at its points and every few metres along it)
    /// </summary>
    private static bool StandsNear(SlimDX.BoundingBox box, float x, float z, IReadOnlyList<(Services.Station.GroundPoint A, Services.Station.GroundPoint B)> ways)
    {
        const float around = Services.Street.StreetReach.AroundStand, along = Services.Street.StreetReach.AlongWay;
        if (DistanceSquared(box, x, z) <= around * around) return true;
        foreach (var (a, b) in ways)
        {
            var length = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            var steps = Math.Max(1, (int)MathF.Ceiling(length / 10f));
            for (var k = 0; k <= steps; k++)
            {
                var t = k / (float)steps;
                if (DistanceSquared(box, a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t) <= along * along) return true;
            }
        }

        return false;
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

    /// <summary>
    /// The self-lit materials of the scene (glass, a sign) and of its dressing (a street's pieces) glowing as bright as
    /// <paramref name="glows"/> says; one it has not, skipped
    /// </summary>
    public static void Glow(GarageRenderer renderer, IReadOnlyDictionary<string, float> glows)
    {
        renderer.GlowDressing(glows);
        if (glows.Count == 0 || renderer.ShowroomNode?.OriginalFile is not { } kn5) return;

        foreach (var (id, glow) in GlowIds(kn5, glows))
            renderer.UpdateMaterialPropertyC(kn5, id, "ksEmissive", glow);
    }

    /// <summary>The materials of <paramref name="kn5"/> that <paramref name="glows"/> names (by their ids in it), each with its emissive colour</summary>
    public static IEnumerable<(uint Id, Vec3 Glow)> GlowIds(AcTools.Kn5File.IKn5 kn5, IReadOnlyDictionary<string, float> glows)
    {
        if (glows.Count == 0) yield break;

        var ids = kn5.Materials.Keys.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => (uint)p.i, StringComparer.Ordinal);
        foreach (var (material, glow) in glows)
            if (ids.TryGetValue(material, out var id)) yield return (id, new Vec3(glow, glow, glow));
    }
}
