using StreetCorsa.Logging;
using StreetCorsa.Models.Race;
using StreetCorsa.Services.Configuration.Models;
using static StreetCorsa.Services.Configuration.IniModificationService;

namespace StreetCorsa.Services.Configuration
{
    /// <summary>
    /// Writes race.ini whole for a launch: a race in the sc_race mode (<see cref="DragRace"/>) or the player alone on
    /// a track (<see cref="FreeRun"/>), on the sections every launch shares (the sun, the rules' weather and world,
    /// the player's units). The values that go into it are checked by <see cref="IniModificationService"/>'s
    /// <c>IniId</c> and <c>IniName</c>; the service writes what comes out of here and keeps the player's own file.
    /// </summary>
    internal static class RaceIniBuilder
    {
        /// <summary>The longest a staggered start holds one car after the one ahead of it, in seconds</summary>
        public const double MaxStagger = 30;

        /// <summary>
        /// Builds the complete drag race INI file content with all required sections
        /// </summary>
        public static string DragRace(DragRaceIntent intent, IAppLogger logger)
        {
            var sb = new System.Text.StringBuilder();
            AppendCommonSections(sb, SunAngle(intent.RaceTime));

            // The format: the mode and module that run the race, its laps, its session, whether the police may come.
            // A test-and-tune is a drag race's practice, whatever the intent says.
            var tune = intent.TunePasses is { } passes ? Math.Clamp(passes, 1, MaxTunePasses) : (int?)null;
            var formats = Services.Race.RaceFormats.Current;
            var format = tune != null ? formats.First(RaceFormatFamilies.Drag) ?? formats.Of(intent.Format) : formats.Of(intent.Format);

            // The police go on the grid after the two racers, as many as the track has room for; the race mode
            // keeps them out of sight until the patrol shows up
            var police = intent.Police is { Count: > 0 } sent && format.Police && tune == null ? sent : null;

            // A field: the opponent and the rivals after it, as many as the format runs (the setting cut it already);
            // none on a test-and-tune
            List<FieldCar> field = tune != null ? [] : intent.Field.Take(Math.Max(0, format.Rivals - 1)).ToList();
            var rivals = tune != null ? 0 : 1 + field.Count;
            var firstPolice = 1 + rivals;

            // A test-and-tune is the player alone, pass after pass, in a race session that never ends at its line: the
            // race mode runs the passes and ends it. A format that runs past its laps (time attack's out lap and its
            // wait for the rival) gives AC's session more, so AC never ends it before the mode does. A drag race is one
            // run down the strip, whatever an event says: the finish is never crossed twice.
            var laps = tune != null || format.IsDrag ? 1 : Math.Clamp(intent.Laps ?? format.Laps, 1, Services.Race.RaceFormats.MaxLaps);
            var acLaps = tune != null ? TuneLaps : format.AcLaps(laps);

            // [RACE] - Player car info and track
            sb.AppendLine("[RACE]");
            sb.AppendLine("AI_LEVEL=100");
            sb.AppendLine($"CARS={1 + rivals + (police?.Count ?? 0)}");
            sb.AppendLine($"CONFIG_TRACK={IniId(intent.TrackConfig, "track layout")}");
            sb.AppendLine("DRIFT_MODE=0");
            sb.AppendLine("FIXED_SETUP=0");
            sb.AppendLine("JUMP_START_PENALTY=0");  // the race mode judges false starts; AC's penalty teleports the car
            sb.AppendLine($"MODEL={IniId(intent.PlayerCarId, "car")}");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine("PENALTIES=0");
            sb.AppendLine($"RACE_LAPS={acLaps}");
            sb.AppendLine($"SKIN={IniId(intent.PlayerSkin, "skin")}");
            sb.AppendLine($"TRACK={IniId(intent.TrackId, "track")}");
            // The CSP mode that runs the race (see ScRaceMode); a test-and-tune always runs in sc_race, which the launcher installs
            sb.AppendLine($"__CM_CUSTOM_MODE={(tune != null ? RaceModeId : Services.Race.RaceFormats.ModeIdOf(format))}");
            sb.AppendLine();

            AppendTailSections(sb);

            // [SESSION_0] - Race session. A drag race is a one-lap race too, on the strip: AC's own drag session
            // (TYPE=7) disqualifies for lanes, resets jump starts and runs matches, all by teleporting the cars,
            // and the race mode owns those calls. On ks_drag both cars start side by side, one per lane, and the
            // lap ends at the strip's finish line (checked in the game 2026-09-24).
            var isDrag = format.IsDrag;
            sb.AppendLine("[SESSION_0]");
            sb.AppendLine($"STARTING_POSITION={Math.Clamp(intent.PlayerGridPosition ?? 1, 1, 1 + rivals)}");
            sb.AppendLine(tune != null ? "NAME=Test and Tune" : $"NAME={IniName(format.Session)}");
            sb.AppendLine("TYPE=3");
            sb.AppendLine($"LAPS={acLaps}");
            sb.AppendLine("DURATION_MINUTES=0");
            sb.AppendLine("SPAWN_SET=START");
            sb.AppendLine();

            // [CAR_0] - Player
            sb.AppendLine("[CAR_0]");
            sb.AppendLine("MODEL=-");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine($"SKIN={IniId(intent.PlayerSkin, "skin")}");
            sb.AppendLine($"DRIVER_NAME={IniName(intent.PlayerName)}");
            sb.AppendLine("NATIONALITY=");
            sb.AppendLine("NATION_CODE=");
            sb.AppendLine();

            // [CAR_1] - Opponent (AI); none on a test-and-tune
            if (tune == null)
            {
                sb.AppendLine("[CAR_1]");
                sb.AppendLine($"MODEL={IniId(intent.OpponentCarId, "car")}");
                sb.AppendLine("MODEL_CONFIG=");
                sb.AppendLine($"AI_LEVEL={intent.OpponentAILevel}");
                sb.AppendLine($"AI_AGGRESSION={intent.OpponentAIAggression}");
                sb.AppendLine($"SKIN={IniId(intent.OpponentSkin, "skin")}");
                sb.AppendLine($"DRIVER_NAME={IniName(intent.OpponentName)}");
                sb.AppendLine("NATIONALITY=");
                sb.AppendLine("NATION_CODE=");
            }

            // [CAR_2]... - The rest of a field, each with its own AI
            for (var i = 0; i < field.Count; i++)
            {
                var rival = field[i];
                sb.AppendLine();
                sb.AppendLine($"[CAR_{2 + i}]");
                sb.AppendLine($"MODEL={IniId(rival.CarId, "car")}");
                sb.AppendLine("MODEL_CONFIG=");
                sb.AppendLine($"AI_LEVEL={rival.AILevel}");
                sb.AppendLine($"AI_AGGRESSION={rival.AIAggression}");
                sb.AppendLine($"SKIN={IniId(rival.Skin, "skin")}");
                sb.AppendLine($"DRIVER_NAME={IniName(rival.Name)}");
                sb.AppendLine("NATIONALITY=");
                sb.AppendLine("NATION_CODE=");
            }

            // [CAR_n]... - The police, after the racers
            for (var i = 0; i < (police?.Count ?? 0); i++)
            {
                sb.AppendLine();
                sb.AppendLine($"[CAR_{firstPolice + i}]");
                sb.AppendLine($"MODEL={IniId(police!.CarId, "car")}");
                sb.AppendLine("MODEL_CONFIG=");
                sb.AppendLine($"AI_LEVEL={PoliceAILevel}");
                sb.AppendLine($"AI_AGGRESSION={PoliceAIAggression}");
                sb.AppendLine($"SKIN={IniId(police.Skins[i], "skin")}");
                sb.AppendLine($"DRIVER_NAME={PoliceDriverName}");
                sb.AppendLine("NATIONALITY=");
                sb.AppendLine("NATION_CODE=");
            }

            // [STREET_CORSA] - What the race mode needs to know: the kind of race (a drag race has lanes and
            // timeslips), which race this is, which it writes into the result so a result is only ever applied
            // to the race it came from, and the damage each car carries into it
            sb.AppendLine();
            sb.AppendLine("[STREET_CORSA]");
            sb.AppendLine(tune != null ? $"RACE_TYPE={RaceTypes.TestAndTune}" : isDrag ? $"RACE_TYPE={RaceTypes.Drag}" : $"RACE_TYPE={RaceTypes.Road}");
            if (tune != null) sb.AppendLine($"TUNE_PASSES={tune}");

            // The format: its id (written back into the result), the module sc_race runs it with, the laps it is
            // over, and how it is won
            sb.AppendLine($"FORMAT={IniId(format.Id, "race format")}");
            sb.AppendLine($"MODULE={(tune != null ? Services.Race.RaceFormats.TuneModule : Services.Race.RaceFormats.ModuleOf(format))}");
            sb.AppendLine($"LAPS={laps}");
            if (format.ByScore) sb.AppendLine(format.HigherWins ? "SCORE=HIGHER" : "SCORE=LOWER");

            // A strip of its own marks: the five, then the trap, metres (none: the race mode's quarter mile)
            if (isDrag && format.Marks is { Count: Services.Race.RaceFormats.DragMarks } marks && format.Trap is { } trap)
                sb.AppendLine("DRAG_MARKS=" + string.Join(",", marks.Append(trap).Select(m => m.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture))));

            // A field: how many rivals (cars 1 to RIVALS), and a train's start. A race of two says nothing: the mode
            // takes one rival when it is not told.
            if (field.Count > 0)
            {
                sb.AppendLine($"RIVALS={rivals}");
                if (intent.Stagger is > 0 and var stagger)
                    sb.AppendLine($"STAGGER={Math.Min(stagger, MaxStagger).ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture)}");
            }

            // A bracket race: both dial-ins, and how the rival leaves and takes the stripe
            if (intent.Bracket is { } bracket && format.Bracket && tune == null)
            {
                var ac = System.Globalization.CultureInfo.InvariantCulture;
                sb.AppendLine($"DIAL_IN={bracket.PlayerDialIn.ToString("0.00", ac)},{bracket.OpponentDialIn.ToString("0.00", ac)}");
                sb.AppendLine($"BRACKET_RIVAL={bracket.RivalReaction.ToString("0.000", ac)},{bracket.RivalMargin.ToString("0.000", ac)}");
            }
            if (intent.ContextId is { } contextId)
            {
                sb.AppendLine($"CONTEXT_ID={contextId:D}");
            }

            // The police cars' indices, and how far round the lap the patrol shows up
            if (police != null)
            {
                sb.AppendLine($"POLICE={string.Join(",", Enumerable.Range(firstPolice, police.Count))}");
                sb.AppendLine($"POLICE_SPOT={police.SpotShare.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}");
                sb.AppendLine(police.Traps ? "POLICE_MODE=TRAPS" : "POLICE_MODE=PATROL");
            }

            // The shape each car goes in, from its earlier races: the mode puts it into AC before the green
            var leftOut = new List<string>();
            var starts = new[] { (0, intent.PlayerStart), (1, intent.OpponentStart) }.Concat(field.Select((rival, i) => (2 + i, rival.Start)));
            foreach (var (index, start) in starts)
            {
                if (start == null || (tune != null && index > 0)) continue;
                foreach (var (key, value) in start.IniKeys(index, leftOut)) sb.AppendLine($"{key}={value}");
            }
            LogLeftOut(logger, leftOut);

            // What the mode says in AC, in the player's language
            sb.AppendLine();
            foreach (var line in StreetCorsa.Services.Text.RaceText.IniLines(StreetCorsa.Services.Text.GameText.Installed)) sb.AppendLine(line);

            return sb.ToString();
        }

        /// <summary>
        /// The player alone on a track: a practice session without the sc_race mode, so nothing starts the
        /// clock or closes the game; the player leaves when they are done, and no results are read
        /// </summary>
        public static string FreeRun(FreeRunIntent intent, IAppLogger logger)
        {
            var sb = new System.Text.StringBuilder();
            AppendCommonSections(sb);

            sb.AppendLine("[RACE]");
            sb.AppendLine("AI_LEVEL=100");
            sb.AppendLine("CARS=1");
            sb.AppendLine($"CONFIG_TRACK={IniId(intent.TrackConfig, "track layout")}");
            sb.AppendLine("DRIFT_MODE=0");
            sb.AppendLine("FIXED_SETUP=0");
            sb.AppendLine("JUMP_START_PENALTY=0");
            sb.AppendLine($"MODEL={IniId(intent.CarId, "car")}");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine("PENALTIES=0");
            sb.AppendLine("RACE_LAPS=0");
            sb.AppendLine($"SKIN={IniId(intent.Skin, "skin")}");
            sb.AppendLine($"TRACK={IniId(intent.TrackId, "track")}");
            // A car with parts its model does not have runs in sc_free, which puts them on and leaves the session to AC
            var shown = intent.Shown != null;
            if (shown) sb.AppendLine($"__CM_CUSTOM_MODE={FreeRunModeId}");
            sb.AppendLine();

            AppendTailSections(sb);

            if (shown)
            {
                sb.AppendLine("[STREET_CORSA]");
                var leftOut = new List<string>();
                foreach (var (key, value) in StreetCorsa.Models.Race.RaceStartState.ShownKeys(0, intent.Shown, leftOut)) sb.AppendLine($"{key}={value}");
                LogLeftOut(logger, leftOut);
                sb.AppendLine();
            }

            sb.AppendLine("[SESSION_0]");
            sb.AppendLine("NAME=Free Run");
            sb.AppendLine("TYPE=1");
            sb.AppendLine("DURATION_MINUTES=0");
            sb.AppendLine("SPAWN_SET=PIT");
            sb.AppendLine();

            sb.AppendLine("[CAR_0]");
            sb.AppendLine("MODEL=-");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine($"SKIN={IniId(intent.Skin, "skin")}");
            sb.AppendLine($"DRIVER_NAME={IniName(intent.PlayerName)}");
            sb.AppendLine("NATIONALITY=");
            sb.AppendLine("NATION_CODE=");

            return sb.ToString();
        }

        /// <summary>Everything before [RACE] that every launch shares, the sun where <paramref name="sunAngle"/> puts it</summary>
        private static void AppendCommonSections(System.Text.StringBuilder sb, double sunAngle = NoonSunAngle)
        {

            // [BENCHMARK]
            sb.AppendLine("[BENCHMARK]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine();

            // [DYNAMIC_TRACK]
            sb.AppendLine("[DYNAMIC_TRACK]");
            sb.AppendLine("LAP_GAIN=1");
            sb.AppendLine("RANDOMNESS=0");
            sb.AppendLine("SESSION_START=100");
            sb.AppendLine("SESSION_TRANSFER=100");
            sb.AppendLine("PRESET=5");
            sb.AppendLine();

            // [GHOST_CAR]
            sb.AppendLine("[GHOST_CAR]");
            sb.AppendLine("ENABLED=0");
            sb.AppendLine("FILE=");
            sb.AppendLine("LOAD=0");
            sb.AppendLine("PLAYING=0");
            sb.AppendLine("RECORDING=0");
            sb.AppendLine("SECONDS_ADVANTAGE=0");
            sb.AppendLine();

            // [GROOVE]
            sb.AppendLine("[GROOVE]");
            sb.AppendLine("VIRTUAL_LAPS=10");
            sb.AppendLine("MAX_LAPS=30");
            sb.AppendLine("STARTING_LAPS=0");
            sb.AppendLine();

            // [HEADER]
            sb.AppendLine("[HEADER]");
            sb.AppendLine("VERSION=1");
            sb.AppendLine("__CM_FEATURE_SET=2");
            sb.AppendLine();

            // [LAP_INVALIDATOR]
            sb.AppendLine("[LAP_INVALIDATOR]");
            sb.AppendLine("ALLOWED_TYRES_OUT=-1");
            sb.AppendLine();

            // [LIGHTING]
            sb.AppendLine("[LIGHTING]");
            sb.AppendLine("CLOUD_SPEED=0.200");
            sb.AppendLine($"SUN_ANGLE={sunAngle.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine("TIME_MULT=1.0");
            var world = Rules.RuleBook.Current.World;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            sb.AppendLine($"__CM_WEATHER_TYPE={world.WeatherType.ToString(culture)}");
            var baseWorld = new Rules.WorldRules();
            sb.AppendLine($"__TRACK_GEOTAG_LONG={Degrees(world.Longitude, 180, baseWorld.Longitude).ToString("0.###############", culture)}");
            sb.AppendLine($"__TRACK_TIMEZONE_BASE_OFFSET={world.TimeZoneOffset.ToString(culture)}");
            sb.AppendLine("__TRACK_TIMEZONE_DTS=0");
            sb.AppendLine($"__TRACK_TIMEZONE_OFFSET={world.TimeZoneOffset.ToString(culture)}");
            sb.AppendLine($"__TRACK_GEOTAG_LAT={Degrees(world.Latitude, 90, baseWorld.Latitude).ToString("0.###############", culture)}");
            sb.AppendLine("__CM_WEATHER_CONTROLLER=base");
            sb.AppendLine();

            // [OPTIONS]
            sb.AppendLine("[OPTIONS]");
            // AC's own read-outs in the player's units
            sb.AppendLine(Helpers.Locale.Units == Helpers.UnitSystem.Imperial ? "USE_MPH=1" : "USE_MPH=0");
            sb.AppendLine();

        }

        /// <summary>A car's names that could not go into race.ini (<see cref="RaceStartState.ShownKeys"/>), told: the mode does without them</summary>
        private static void LogLeftOut(IAppLogger logger, List<string> leftOut)
        {
            foreach (var name in leftOut) logger.Warning("Left out of race.ini, its name cannot go into it: {Name}", IniName(name));
        }

        /// <summary>A latitude or longitude of the rules' world, the core's when it is no number or past <paramref name="most"/> either way</summary>
        private static double Degrees(double value, double most, double baseFigure) =>
            double.IsFinite(value) && Math.Abs(value) <= most ? value : baseFigure;

        /// <summary>The sections between [RACE] and the sessions that every launch shares</summary>
        private static void AppendTailSections(System.Text.StringBuilder sb)
        {
            // [REMOTE]
            sb.AppendLine("[REMOTE]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine("GUID=");
            sb.AppendLine("NAME=");
            sb.AppendLine("PASSWORD=");
            sb.AppendLine("REQUESTED_CAR=");
            sb.AppendLine("SERVER_IP=");
            sb.AppendLine("SERVER_PORT=");
            sb.AppendLine("TEAM=");
            sb.AppendLine();

            // [REPLAY]
            sb.AppendLine("[REPLAY]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine("FILENAME=");
            sb.AppendLine();

            // [RESTART]
            sb.AppendLine("[RESTART]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine();

            // [TEMPERATURE], [WEATHER], [WIND]: the rules' world
            var world = Rules.RuleBook.Current.World;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            sb.AppendLine("[TEMPERATURE]");
            sb.AppendLine($"AMBIENT={world.AmbientC.ToString(culture)}");
            sb.AppendLine($"ROAD={world.RoadC.ToString(culture)}");
            sb.AppendLine();

            sb.AppendLine("[WEATHER]");
            sb.AppendLine($"NAME={WeatherName(world.Weather)}");
            sb.AppendLine();

            sb.AppendLine("[WIND]");
            sb.AppendLine("DIRECTION_DEG=-1");
            sb.AppendLine($"SPEED_KMH_MAX={Math.Max(world.WindMinKmh, world.WindMaxKmh).ToString(culture)}");
            sb.AppendLine($"SPEED_KMH_MIN={Math.Max(0, world.WindMinKmh).ToString(culture)}");
            sb.AppendLine();

            // [__PREVIEW_GENERATION]
            sb.AppendLine("[__PREVIEW_GENERATION]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine();

        }
    }
}
