using StreetCorsa.Tools;

namespace DinerSceneBuilder;

/// <summary>
/// The diner's street as a KN5, out of what prepare_prop.py made of compose_street.py's .blend (<see cref="PreparedScene"/>):
/// the OBJ (Y up, metres, in the scene's own place: the road's surface at 0, the middle of Main Street in front of the
/// diner at the origin) and its pictures, drawn with AC's ksPerPixelMultiMap, the road markings cut out by their
/// alpha, the windows' glass left out so the rooms behind show. A sky dome goes round it for each light of the day
/// (STREET_SKY_DAY, _DUSK, _NIGHT), and the ground's height (ground.bin, read by the game's StationGround) next to it,
/// so the car rides the kerbs and the lot. What stands on the street (the shops, the palms, the poles, the parked cars)
/// casts a shadow on it and on the car; the ground itself (the kit's road and sidewalks, MI_..., the lot's lines and bed,
/// the dirt and the lawns) never does.
///
///   DinerSceneBuilder &lt;prepared folder&gt; &lt;name&gt; &lt;out.kn5&gt; --sky day=a.jpg,dusk=b.jpg,night=c.jpg
///       [--sky-radius 700] [--ground-half 180]
///
/// The base game's Main Street (see docs/screens/diner.md; build_street.py runs it): DinerSceneBuilder &lt;prepared&gt; street
/// "Street Corsa/Content/streetcorsa/streets/mainstreet/street.kn5" --sky day=tools/StationScene/skies/sky_day.jpg,...
/// </summary>
internal static class Program
{
    private const string Usage = "DinerSceneBuilder <prepared folder> <name> <out.kn5> --sky day=a.jpg,dusk=b.jpg,night=c.jpg [--sky-radius 700] [--ground-half 180]";

    private static int Main(string[] args)
    {
        var options = new BuilderArgs(args);
        var positional = options.Positional;
        var sky = options.Option("--sky");
        if (positional.Count != 3 || sky == null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        if (!options.NumbersOk(Usage, "--sky-radius", "--ground-half")) return 1;

        PreparedScene.Build(positional[0], positional[1], positional[2], "street_sky_", sky,
            options.Number("--sky-radius") ?? 700f, options.Number("--ground-half") ?? 180f,
            castShadows: true, groundMaterials: "MI_,lot_,ground_dirt,house_lawn,house_concrete");
        return 0;
    }
}
