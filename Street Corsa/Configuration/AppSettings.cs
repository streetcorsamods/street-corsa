using System.IO;

namespace StreetCorsa.Configuration;

/// <summary>
/// Application configuration settings
/// </summary>
public class AppSettings
{
    private static AppSettings? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Singleton instance of AppSettings
    /// </summary>
    public static AppSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new AppSettings();
                }
            }
            return _instance;
        }
    }

    /// <summary>Where the game looks for Assetto Corsa when the settings name no folder</summary>
    /// <remarks>
    /// The developer's own install, under its old name. It stays until the settings name the folder there: a
    /// settings.json without <c>AssettoCorsaPath</c> runs on this default, so pointing it at Steam's folder would
    /// break that install. A new player is told at start-up where to set their folder.
    /// </remarks>
    public const string DefaultAssettoCorsaPath = @"C:\GAMES\Street Rod AC";

    private AppSettings()
    {
        AssettoCorsaPath = DefaultAssettoCorsaPath;
    }

    /// <summary>
    /// Path to the Assetto Corsa installation directory. Set from the settings when they load (see
    /// <c>GameSettingsService</c>), before anything reads the install. The car-data overlay and the catalog are
    /// built on the folder they started with, so for them a change takes effect at the next start; the engine
    /// sounds (<c>EngineAudio</c>, <c>EngineLoudness</c>) load their banks from the new folder at once.
    /// </summary>
    public string AssettoCorsaPath { get; set; }

    /// <summary>The game's own folder under %AppData%: settings, logs, the catalog, what a race changed in AC</summary>
    public static string AppDataPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreetCorsa");

    /// <summary>
    /// Where the originals of whatever a race changes in AC wait until they go back: car data and sound
    /// (<c>CarDataOverlay</c>, a folder per car) and the cfg INI files (<c>AcConfigBackup</c>)
    /// </summary>
    public static string AcRestorePath => Path.Combine(AppDataPath, "AcRestore");

    /// <summary>
    /// Path to the cars content folder
    /// </summary>
    public string CarsPath => Path.Combine(AssettoCorsaPath, "content", "cars");

    /// <summary>
    /// Path to the tracks content folder
    /// </summary>
    public string TracksPath => Path.Combine(AssettoCorsaPath, "content", "tracks");

    /// <summary>
    /// Path to the showrooms content folder
    /// </summary>
    public string ShowroomsPath => Path.Combine(AssettoCorsaPath, "content", "showroom");

    /// <summary>The base game's content: the first mod (see <see cref="Mods.ModSet"/>)</summary>
    public static string BaseContentPath => Mods.ModSet.Installed.Base.Folder;

    /// <summary>The garage the game is played from (game.json's <c>garage</c>): a mod's garage or an AC showroom of that id</summary>
    public string GarageShowroomId { get; set; } = Rules.GameFile.Installed.Garage;

    /// <summary>
    /// Full path to the garage showroom model: a mod's garage by that id if one has it (the last mod to have it wins,
    /// so a mod can dress the garage), else the AC install's showroom of that name
    /// </summary>
    public string GarageShowroomKn5
    {
        get
        {
            foreach (var (_, dir) in Mods.ModSet.Installed.Folders(Mods.ContentTypes.Garages).Reverse())
            {
                var own = Path.Combine(dir, GarageShowroomId, GarageShowroomId + ".kn5");
                if (File.Exists(own)) return own;
            }

            return Path.Combine(ShowroomsPath, GarageShowroomId, GarageShowroomId + ".kn5");
        }
    }

    /// <summary>
    /// The base game's converted part packs (see tools/convert-parts.ps1); the catalog reads every mod's
    /// </summary>
    public string PartsPath => Path.Combine(BaseContentPath, Mods.ContentTypes.Parts);

    /// <summary>The base game's engine sounds: a folder per sound; the library reads every mod's</summary>
    public string SoundsPath => Path.Combine(BaseContentPath, Mods.ContentTypes.Sounds);

    /// <summary>The install's master GUIDs, where Kunos cars name their sound events</summary>
    public string SfxGuidsPath => Path.Combine(AssettoCorsaPath, "content", "sfx", "GUIDs.txt");

    /// <summary>
    /// Validates that the AC installation path exists and is valid
    /// </summary>
    public bool IsValidInstallation()
    {
        if (string.IsNullOrEmpty(AssettoCorsaPath) || !Directory.Exists(AssettoCorsaPath))
            return false;

        return Directory.Exists(CarsPath) && Directory.Exists(TracksPath);
    }
}
