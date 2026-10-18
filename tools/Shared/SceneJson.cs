using Newtonsoft.Json.Linq;

namespace StreetCorsa.Tools;

/// <summary>
/// The pieces of a scene's description (the .json beside its .kn5) the builders write alike: colours, the renderer's
/// light for a light of the day, a lamp, the camera, the room, the places a part stands in and the looks a scene wears.
/// The keys come in the order the shipped files have them: a rebuilt scene must diff clean against what is shipped.
/// Linked into the builders that write a description (the warehouse, the part shop, the machine shop).
/// </summary>
internal static class SceneJson
{
    public static JArray Rgb(int r, int g, int b) => new(r, g, b);

    /// <summary>A figure as the files carry it, to the millimetre</summary>
    public static float Round(float v) => MathF.Round(v, 3);

    /// <summary>A lamp the renderer lights at every hour: where, how bright against its look's colour, how far it reaches</summary>
    public static JObject Lamp(float x, float y, float z, float brightness, float range) => new()
    {
        ["x"] = x, ["y"] = y, ["z"] = z, ["brightness"] = brightness, ["range"] = range
    };

    /// <summary>The renderer's light for one light of the day: the key light, the ambient from above and below, the lamps' scale then</summary>
    public static JObject Light(float brightness, JArray color, float ambient, JArray up, JArray down, float lampScale) => new()
    {
        ["brightness"] = brightness, ["color"] = color, ["ambient"] = ambient, ["ambientUp"] = up, ["ambientDown"] = down,
        ["cubemapAmbient"] = 0f, ["lampScale"] = lampScale
    };

    public static JObject Lights(JObject day, JObject dusk, JObject night) => new() { ["day"] = day, ["dusk"] = dusk, ["night"] = night };

    /// <summary>
    /// The camera: orbiting a point (alpha radians round, beta up from level, radius metres), <paramref name="targetHeight"/>
    /// over the floor, where a scene gives the point's x and z and the lens, those too
    /// </summary>
    public static JObject Camera(float alpha, float beta, float radius, float targetHeight, float? targetX = null, float? targetZ = null, float? fov = null)
    {
        var camera = new JObject { ["alpha"] = alpha, ["beta"] = beta, ["radius"] = radius };
        if (targetX is { } x) camera["targetX"] = x;
        camera["targetHeight"] = targetHeight;
        if (targetZ is { } z) camera["targetZ"] = z;
        if (fov is { } lens) camera["fov"] = lens;
        return camera;
    }

    /// <summary>The inside the camera stays in: a room across and deep (a shop)</summary>
    public static JObject Room(float halfWidth, float halfDepth, float height) => new()
    {
        ["halfWidth"] = halfWidth, ["halfDepth"] = halfDepth, ["height"] = height
    };

    /// <summary>The inside the camera stays in: a hall along and across (the warehouse's aisle)</summary>
    public static JObject Hall(float halfLength, float halfWidth, float height) => new()
    {
        ["halfLength"] = halfLength, ["halfWidth"] = halfWidth, ["height"] = height
    };

    /// <summary>The key light's direction</summary>
    public static JArray Sun(float x, float y, float z) => new(x, y, z);

    /// <summary>
    /// A place a part stands in: the middle of its floor, how much room it has (width along its own x, depth along its
    /// own z, height), the way it faces (degrees about the vertical; 0 faces +z, towards the door), and what goes there
    /// </summary>
    public sealed record Place(float X, float Y, float Z, float Width, float Depth, float Height, float Heading, string Holds)
    {
        public JObject ToJson() => new()
        {
            ["x"] = Round(X), ["y"] = Round(Y), ["z"] = Round(Z), ["width"] = Width, ["depth"] = Depth, ["height"] = Height,
            ["heading"] = Heading, ["holds"] = Holds
        };
    }

    /// <summary>
    /// What a scene wears: the picture on each of its surfaces' materials and the colour its lamps give; with the kinds
    /// of shop it is for when the scene has looks by kind
    /// </summary>
    public static JObject Look(string id, string[]? kinds, IEnumerable<(string Surface, string Picture)> surfaces, int[] lampColor)
    {
        var look = new JObject { ["id"] = id };
        if (kinds != null) look["kinds"] = new JArray(kinds);
        var worn = new JObject();
        foreach (var (surface, picture) in surfaces) worn[surface] = picture;
        look["surfaces"] = worn;
        look["lampColor"] = new JArray(lampColor);
        return look;
    }
}
