using System.IO;
using System.Text;
using StreetCorsa.Configuration;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Configuration.Models;

namespace StreetCorsa.Services.Configuration
{
    /// <summary>
    /// Central service for all Assetto Corsa INI file modifications.
    /// Implements the Read -> Intent -> Apply model, with one way to write: the file's original is kept first
    /// (<see cref="AcConfigBackup"/>, under %AppData%\StreetCorsa\AcRestore), then the new text is written through
    /// <see cref="SafeFile"/> (temp file, flushed, renamed over the old one). An edited file keeps its encoding and
    /// every line but the ones changed (<see cref="IniText"/>); race.ini, which a race rewrites whole
    /// (<see cref="RaceIniBuilder"/>), is written as UTF-8 without a BOM. Everything written goes back with
    /// <see cref="RestoreAll"/> once AC has exited.
    /// </summary>
    public class IniModificationService : IIniModificationService
    {
        /// <summary>The CSP mode a race runs in: race.ini selects it by its folder name under extension\lua\new-modes</summary>
        public const string RaceModeId = "sc_race";

        /// <summary>The CSP mode a free run of a car with parts its model does not have runs in (ScRaceMode.InstallFreeRun)</summary>
        public const string FreeRunModeId = "sc_free";

        /// <summary>AC's assists file, where the damage, fuel and tyre wear rates are</summary>
        public const string AssistsFile = "assists.ini";

        /// <summary>Mechanical and body damage in a race, in percent: AC's full rate, which an easy game turns down</summary>
        public static int RaceDamage => Rules.RuleBook.Current.Race.Damage;

        /// <summary>Tyre wear in a race: 1 is AC's normal rate</summary>
        public static int RaceTyreWear => Rules.RuleBook.Current.Race.TyreWear;

        /// <summary>Fuel use in a race: 1 is AC's normal rate. The fuel a race burns stays burned (Car.FuelLitres).</summary>
        public static int RaceFuelRate => Rules.RuleBook.Current.Race.FuelRate;

        /// <summary>The most passes one test-and-tune runs</summary>
        public const int MaxTunePasses = 20;

        /// <summary>A test-and-tune's laps: more than its passes ever cross the line, so AC never ends the session</summary>
        public const int TuneLaps = 50;

        private readonly string _cfgDirectory;
        private readonly AcConfigBackup _backup;
        private readonly IAppLogger _logger;

        public IniModificationService() : this(DefaultCfgDirectory, AppSettings.AcRestorePath)
        {
        }

        /// <summary>AC's Documents\Assetto Corsa\cfg</summary>
        public static string DefaultCfgDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa", "cfg");

        /// <param name="cfgDirectory">AC's Documents\Assetto Corsa\cfg</param>
        /// <param name="restoreRoot">Where the originals wait (%AppData%\StreetCorsa\AcRestore)</param>
        public IniModificationService(string cfgDirectory, string restoreRoot)
        {
            _cfgDirectory = cfgDirectory;
            _backup = new AcConfigBackup(cfgDirectory, restoreRoot);
            _logger = AppLoggerFactory.CreateLogger("IniModification");

            // Ensure cfg directory exists
            if (!Directory.Exists(_cfgDirectory))
            {
                _logger.Warning("AC cfg directory not found: {CfgDirectory}", _cfgDirectory);
            }
            else
            {
                _logger.Information("INI Modification Service initialized. CFG directory: {CfgDirectory}", _cfgDirectory);
            }
        }

        public bool ApplyIntent(ModificationIntent intent)
        {
            _logger.Information("Applying intent: {Description}", intent.Description);

            try
            {
                return intent switch
                {
                    ShowroomIntent showroomIntent => ApplyShowroomIntent(showroomIntent),
                    DragRaceIntent dragRaceIntent => ApplyDragRaceIntent(dragRaceIntent),
                    RaceConfigIntent raceIntent => ApplyRaceConfigIntent(raceIntent),
                    FreeRunIntent freeRunIntent => ApplyFreeRunIntent(freeRunIntent),
                    _ => throw new NotSupportedException($"Intent type not supported: {intent.GetType().Name}")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to apply intent: {Description}", intent.Description);
                return false;
            }
        }

        public int RestoreAll(ICollection<string>? problems = null) => _backup.RestoreAll(problems);

        public bool HasPendingRestore => _backup.HasPending;

        public IniText? ReadIniFile(string fileName)
        {
            var filePath = GetIniFilePath(fileName);
            if (!File.Exists(filePath)) return null;

            return new IniText(Decode(File.ReadAllBytes(filePath), out _));
        }

        public bool FileExists(string fileName)
        {
            var filePath = GetIniFilePath(fileName);
            return File.Exists(filePath);
        }

        public string GetIniFilePath(string fileName)
        {
            // Every cfg file the game touches is one file name in the cfg folder; no caller needs anything else
            if (!PathNames.IsSafeSegment(fileName))
                throw new ArgumentException($"'{fileName}' is not the name of a file in the cfg folder", nameof(fileName));

            return Path.Combine(_cfgDirectory, fileName);
        }

        // ===== THE ONE WAY AN INI FILE IS WRITTEN =====

        /// <summary>
        /// Changes some values of a cfg file and leaves the rest as it was: comments, order and the encoding (a BOM
        /// stays if there was one, none is added). Lines are written back with CRLF, as AC writes them.
        /// </summary>
        private void EditIni(string filePath, Action<IniText> edit)
        {
            Encoding encoding = SafeFile.Utf8NoBom;
            var hadBom = false;
            string? text = null;
            if (File.Exists(filePath))
            {
                var bytes = File.ReadAllBytes(filePath);
                hadBom = HasUtf8Bom(bytes);
                text = Decode(bytes, out encoding);
            }

            var ini = new IniText(text);
            edit(ini);

            // IniText marks every Set as a change, even one to the value already there
            var content = ini.ToString();
            if (text != null && content == new IniText(text).ToString()) return;

            WriteIni(filePath, content, encoding, hadBom);
        }

        /// <summary>The original is kept, then the file is replaced in one rename</summary>
        private void WriteIni(string filePath, string content, Encoding encoding, bool bom = false)
        {
            _backup.Keep(filePath);

            var bytes = encoding.GetBytes(content);
            if (bom) bytes = [.. Utf8Bom, .. bytes];
            if (File.Exists(filePath)) File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
            SafeFile.WriteAllBytes(filePath, bytes);
        }

        private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

        private static bool HasUtf8Bom(byte[] bytes) =>
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        /// <summary>
        /// The text of a cfg file and the encoding that writes it back byte for byte: UTF-8 when it is valid UTF-8
        /// (plain ASCII is), else Latin-1, which maps every byte to one character and back, so an ANSI file's
        /// accented names survive an edit of another key unchanged
        /// </summary>
        internal static string Decode(byte[] bytes, out Encoding encoding)
        {
            var offset = HasUtf8Bom(bytes) ? 3 : 0;
            try
            {
                var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, offset, bytes.Length - offset);
                encoding = SafeFile.Utf8NoBom;
                return text;
            }
            catch (DecoderFallbackException)
            {
                encoding = Encoding.Latin1;
                return Encoding.Latin1.GetString(bytes, offset, bytes.Length - offset);
            }
        }

        // ===== VALUES THAT GO INTO AN INI FILE =====

        /// <summary>
        /// An id (car, skin, track, layout) as it goes into a cfg file. Ids are folder names; one that could end its
        /// line, open a section or start a comment is refused rather than written, since it would change what the
        /// file says (a new [REMOTE] section, a key of ours overwritten). Null is an empty value.
        /// </summary>
        internal static string IniId(string? value, string what)
        {
            var id = value ?? string.Empty;
            if (!RaceStartState.IsIniValue(id))
                throw new ArgumentException($"The {what} id '{IniName(id)}' has characters that cannot go into an INI file");
            return id;
        }

        /// <summary>
        /// A name (a driver's) as it goes into a cfg file: free text, so what would break the line is replaced
        /// rather than refused. Line breaks and other control characters become spaces, [ ] become ( ), = becomes -,
        /// and ; (a comment to AC, which would cut the name) becomes ,
        /// </summary>
        internal static string IniName(string? value)
        {
            var name = new StringBuilder(value?.Length ?? 0);
            foreach (var c in value ?? string.Empty)
            {
                name.Append(c switch
                {
                    '[' => '(',
                    ']' => ')',
                    '=' => '-',
                    ';' => ',',
                    _ when char.IsControl(c) => ' ',
                    _ => c
                });
            }

            return name.ToString().Trim();
        }

        // ===== INTENT APPLICATION METHODS =====

        private bool ApplyShowroomIntent(ShowroomIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);
            var carId = IniId(intent.CarId, "car");
            var skinId = IniId(intent.SkinId, "skin");
            var track = IniId(intent.Track, "showroom");

            EditIni(filePath, ini =>
            {
                ini.Set("SHOWROOM", "CAR", carId);
                ini.Set("SHOWROOM", "SKIN", skinId);
                ini.Set("SHOWROOM", "SELECTED_SKIN", skinId);
                ini.Set("SHOWROOM", "TRACK", track);
            });

            _logger.Information("Applied showroom intent: Car={CarId}, Skin={SkinId}",
                intent.CarId, intent.SkinId);

            return true;
        }

        private bool ApplyRaceConfigIntent(RaceConfigIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);
            var carId = IniId(intent.CarId, "car");
            var skinId = IniId(intent.SkinId, "skin");
            var trackId = IniId(intent.TrackId, "track");
            var trackConfig = IniId(intent.TrackConfig, "track layout");

            EditIni(filePath, ini =>
            {
                // Configure [RACE] section
                ini.Set("RACE", "MODEL", carId);
                ini.Set("RACE", "SKIN", skinId);
                ini.Set("RACE", "TRACK", trackId);
                ini.Set("RACE", "CONFIG_TRACK", trackConfig);

                // Configure [SESSION_0] section
                ini.Set("SESSION_0", "NAME", "Quick Race");
            });

            _logger.Information("Applied race config intent: Car={CarId}, Skin={SkinId}, Track={TrackId}, Config={TrackConfig}",
                intent.CarId, intent.SkinId, intent.TrackId, intent.TrackConfig ?? "(none)");

            return true;
        }

        private bool ApplyDragRaceIntent(DragRaceIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);

            _logger.Information("Applying drag race intent (generating INI from code)");

            // Build the race.ini content from scratch
            var content = RaceIniBuilder.DragRace(intent, _logger);

            // Write to race.ini; the user's own is kept until AC exits
            WriteIni(filePath, content, SafeFile.Utf8NoBom);

            ApplyRaceDamage(intent.DamagePercent);

            _logger.Information("Applied drag race intent: Player={PlayerName} ({PlayerCarId}), Opponent={OpponentName} ({OpponentCarId}), AI={AILevel}/{AIAggression}, Context={ContextId}",
                intent.PlayerName, intent.PlayerCarId, intent.OpponentName, intent.OpponentCarId, intent.OpponentAILevel, intent.OpponentAIAggression,
                intent.ContextId?.ToString("D") ?? "(none)");

            return true;
        }

        /// <summary>
        /// A race is run with Assetto Corsa's damage, tyre wear and fuel use on, whatever the player last picked in AC:
        /// the engine can blow, the body takes what it hits and the tank runs down. The rest of the assists (and visual
        /// damage) stay the player's. Kept and put back like race.ini.
        /// </summary>
        private void ApplyRaceDamage(int damagePercent)
        {
            // The rules' most is held to 0 or more by RaceRules.Check; held again here, since Clamp throws on a most below its least
            var damage = Math.Clamp(damagePercent, 0, Math.Max(0, RaceDamage));
            EditIni(GetIniFilePath(AssistsFile), ini =>
            {
                ini.Set("ASSISTS", "DAMAGE", damage.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ini.Set("ASSISTS", "TYRE_WEAR", RaceTyreWear.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ini.Set("ASSISTS", "FUEL_RATE", RaceFuelRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            });
            _logger.Information("Race assists: DAMAGE={Damage}, TYRE_WEAR={TyreWear}, FUEL_RATE={FuelRate}", damage, RaceTyreWear, RaceFuelRate);
        }

        private bool ApplyFreeRunIntent(FreeRunIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);
            WriteIni(filePath, RaceIniBuilder.FreeRun(intent, _logger), SafeFile.Utf8NoBom);

            _logger.Information("Applied free run intent: Car={CarId}, Track={TrackId}, Config={TrackConfig}",
                intent.CarId, intent.TrackId, intent.TrackConfig ?? "(none)");
            return true;
        }

        /// <summary>How the police drive: flat out, and not shy of contact (the race mode takes them to 150% once they are out)</summary>
        public static int PoliceAILevel => Rules.RuleBook.Current.Race.PoliceAILevel;
        public static int PoliceAIAggression => Rules.RuleBook.Current.Race.PoliceAIAggression;
        public const string PoliceDriverName = "Police";

        /// <summary>Noon, which a launch without a time of day races at</summary>
        public const double NoonSunAngle = -16.0;

        /// <summary>
        /// AC's sun for a time of the game ([LIGHTING] SUN_ANGLE), in the season the street screens are lit by: see
        /// <see cref="SeasonalSun"/>
        /// </summary>
        public static double SunAngle(DateTime? time) => time is { } t ? SeasonalSun.AcSunAngle(t) : NoonSunAngle;

        /// <summary>AC's weather the rules name, AC's clear sky when they name none or one that cannot go into race.ini as it is</summary>
        internal static string WeatherName(string? weather)
        {
            const string clear = "4_mid_clear";
            if (string.IsNullOrWhiteSpace(weather)) return clear;

            var name = weather.Trim();
            if (RaceStartState.IsIniValue(name)) return name;

            AppLoggerFactory.CreateLogger("IniModification").Warning("The rules' weather '{Weather}' cannot go into race.ini; {Clear} is raced in", IniName(name), clear);
            return clear;
        }

    }
}
