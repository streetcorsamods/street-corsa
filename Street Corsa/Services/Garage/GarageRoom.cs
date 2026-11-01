using System.Collections.Concurrent;
using System.IO;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Services.Garage;

/// <summary>The inside of a garage the camera stays in: its walls across (x) and along the car (z), and how high it may go</summary>
public readonly record struct GarageWalls(float MinX, float MaxX, float MinZ, float MaxZ, float Height);

/// <summary>
/// A garage's room as its garages/&lt;id&gt;/scene.json gives it, next to its marks (<see cref="Scripting.GarageScene"/>):
/// the walls the camera stays inside, and the light for each light of the day, the lamps of every hour and the glow of
/// its self-lit glass. A garage that gives no lights (an Assetto Corsa showroom) is lit as the renderer lights it; one
/// that gives no room has the camera's own limits.
/// </summary>
public sealed record GarageRoom(
    string Id,
    GarageWalls? Walls,
    float[] Sun,
    IReadOnlyDictionary<StreetLight, StreetLighting> Lights,
    IReadOnlyDictionary<StreetLight, IReadOnlyDictionary<string, float>> Glow)
{
    /// <summary>The sky domes' meshes in a garage's model, GARAGE_SKY_DAY and so on: the one for the hour is shown</summary>
    public const string SkyPrefix = "GARAGE_SKY_";

    /// <summary>A garage that says nothing of its room</summary>
    public static GarageRoom None(string id) => new(id, null, [0.25f, 1f, 0.35f], new Dictionary<StreetLight, StreetLighting>(),
        new Dictionary<StreetLight, IReadOnlyDictionary<string, float>>());

    /// <summary>The garage lights itself: its file gives the light of the day</summary>
    public bool IsLit => Lights.Count > 0;

    public static string SkyNode(StreetLight light) => SkyPrefix + light.ToString().ToUpperInvariant();

    /// <summary>The garage at <paramref name="light"/>: its light, its sun and its glass's glow</summary>
    public GarageDressing Dress(StreetLight light) =>
        new(SceneLighting.For(Lights, light), Sun, SceneLighting.GlowAt(Glow, light), SkyNode(light));
}

/// <summary>How a garage is lit at a light of the day: what <see cref="GarageRoom.Dress"/> gives the viewport</summary>
public sealed record GarageDressing(StreetLighting Lighting, float[] Sun, IReadOnlyDictionary<string, float> Glow, string Sky);

public static class GarageRooms
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Garage");

    private sealed record WallsEntry(float? MinX, float? MaxX, float? MinZ, float? MaxZ, float? Height);

    private sealed record SceneEntry(WallsEntry? Room, float[]? Sun, Dictionary<string, SceneLighting.LightEntry?>? Lights,
        List<SceneLighting.LampEntry?>? Lamps, Dictionary<string, Dictionary<string, float>?>? Glow);

    private static readonly ContentCache<ConcurrentDictionary<string, GarageRoom>> Read = new(() => new(StringComparer.OrdinalIgnoreCase));

    /// <summary>The room of the garage <paramref name="garageId"/> in the mods next to the game, read once</summary>
    public static GarageRoom For(string garageId) => Read.Value.GetOrAdd(garageId, id => Load(ModSet.Installed, id));

    /// <summary>
    /// The room of the garage <paramref name="garageId"/>: the last mod's scene.json for it, as its marks are read; a
    /// garage without one, or with one that cannot be read (logged), says nothing of its room; nor does an id that is no
    /// plain folder name (the settings' file is anyone's to edit)
    /// </summary>
    public static GarageRoom Load(ModSet mods, string garageId)
    {
        if (!Helpers.PathNames.IsSafeSegment(garageId)) return GarageRoom.None(garageId);

        foreach (var (_, dir) in mods.Folders(ContentTypes.Garages).Reverse())
        {
            var file = Path.Combine(dir, garageId, Scripting.GarageScene.FileName);
            if (!File.Exists(file)) continue;
            return SceneFiles.Load<SceneEntry, GarageRoom>(file, "Garages", "garage", Logger, entry => Build(garageId, entry)) ?? GarageRoom.None(garageId);
        }

        return GarageRoom.None(garageId);
    }

    private static GarageRoom Build(string id, SceneEntry? entry)
    {
        if (entry == null) return GarageRoom.None(id);

        var walls = entry.Room is { MinX: { } minX, MaxX: { } maxX, MinZ: { } minZ, MaxZ: { } maxZ, Height: { } height }
                    && float.IsFinite(minX) && float.IsFinite(maxX) && float.IsFinite(minZ) && float.IsFinite(maxZ) && float.IsFinite(height)
                    && minX < 0f && maxX > 0f && minZ < 0f && maxZ > 0f && height > 0f
            ? new GarageWalls(minX, maxX, minZ, maxZ, height)
            : (GarageWalls?)null;
        if (entry.Room != null && walls == null)
            SceneFiles.LeftOut(Logger, "Garages", $"the garage {id} has a room that is not round the car's place (minX, maxX, minZ, maxZ either side of 0, a height); the camera keeps its own limits");

        // A garage's lamps are white and the same at every hour, only scaled by it, as a warehouse's
        var lights = SceneFiles.Lights(entry.Lights, entry.Lamps, id, Logger);
        var sun = entry.Sun is { Length: 3 } s && s.All(float.IsFinite) ? s : [0.25f, 1f, 0.35f];
        return new GarageRoom(id, walls, sun, lights, SceneLighting.MaterialGlows(entry.Glow));
    }
}
