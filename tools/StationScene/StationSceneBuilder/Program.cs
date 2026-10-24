using StreetCorsa.Tools;

namespace StationSceneBuilder;

/// <summary>
/// The gas station as a KN5, out of what prepare_prop.py made of compose_station.py's .blend (<see cref="PreparedScene"/>):
/// the forecourt and the road at the origin's height, the office, the pumps under their canopy and the rest in the
/// scene's own place, a sky dome for each light of the day (STATION_SKY_DAY, _DUSK, _NIGHT), and the ground's height
/// (ground.bin) next to it. What stands on the forecourt (the canopy, the office, the pumps) casts a shadow on it and
/// on the car; the ground itself (the materials named st_grass, st_forecourt, st_road) never does.
///
///   StationSceneBuilder &lt;prepared folder&gt; &lt;name&gt; &lt;out.kn5&gt; --sky day=a.jpg,dusk=b.jpg,night=c.jpg
///       [--sky-radius 700] [--ground-half 150]
///
/// The street rods' station (see docs/screens/gas-station.md; build_station.py runs it): StationSceneBuilder &lt;prepared&gt;
/// station "Street Corsa/Content/streetrod/stations/crossroads/station.kn5" --sky day=tools/StationScene/skies/sky_day.jpg,...
/// </summary>
internal static class Program
{
    private const string Usage = "StationSceneBuilder <prepared folder> <name> <out.kn5> --sky day=a.jpg,dusk=b.jpg,night=c.jpg [--sky-radius 700] [--ground-half 150]";

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

        PreparedScene.Build(positional[0], positional[1], positional[2], "station_sky_", sky,
            options.Number("--sky-radius") ?? 700f, options.Number("--ground-half") ?? 150f,
            castShadows: true, groundMaterials: "st_grass,st_forecourt,st_road,st_line");
        return 0;
    }
}
