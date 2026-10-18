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
    /// so a mod can dress the garage), else the AC install's showroom of that name, else AC's own showroom
    /// (<see cref="FallbackShowroomId"/>): the released game ships without the garage's model, which is not ours to give
    /// </summary>
    public string GarageShowroomKn5
    {
        get
        {
            // Found once for the garage and the AC folder it is looked for in: the mods' garages folders are walked and
            // probed with File.Exists, and this is read at every scene (GarageId at every prop)
            var (id, showrooms) = (GarageShowroomId, ShowroomsPath);
            if (_garageKn5 is { } known && known.Id == id && known.Showrooms == showrooms) return known.Kn5;

            var garages = Services.Showcase.ShowcaseContent.GaragesOf(Mods.ModSet.Installed);
            var kn5 = Services.Showcase.ShowcaseContent.FindKn5(id, garages, showrooms)
                      ?? Services.Showcase.ShowcaseContent.FindKn5(FallbackShowroomId, garages, showrooms)
                      ?? Path.Combine(showrooms, id, id + ".kn5");
            _garageKn5 = new FoundGarage(id, showrooms, kn5);
            return kn5;
        }
    }

    /// <summary>The garage model last found, and what it was found for; one object, so a read on another thread sees one answer</summary>
    private sealed record FoundGarage(string Id, string Showrooms, string Kn5);

    private volatile FoundGarage? _garageKn5;

    /// <summary>Forgets the garage model found, so the next read looks again (a mod's garage put in while the game runs)</summary>
    public void ForgetGarageKn5() => _garageKn5 = null;

    /// <summary>
    /// The garage the game is played in: the room folder <see cref="GarageShowroomKn5"/> is in, <see cref="GarageShowroomId"/>
    /// or <see cref="FallbackShowroomId"/>. What its props (props/&lt;id&gt;/prop.json's places) and its marks
    /// (garages/&lt;id&gt;/scene.json) are looked up by, so a room keeps the ones laid out for it.
    /// </summary>
    public string GarageId => Path.GetFileName(Path.GetDirectoryName(GarageShowroomKn5)) is { Length: > 0 } id ? id : GarageShowroomId;

    /// <summary>The showroom every AC install has (Kunos'), for a garage whose model is nowhere; the base game's props have places in it too</summary>
    public const string FallbackShowroomId = "showroom";

    /// <summary>
    /// The base game's part packs (docs/modding.md); the catalog reads every mod's
    /// </summary>
    public string PartsPath => Path.Combine(BaseContentPath, Mods.ContentTypes.Parts);

    /// <summary>The base game's engine sounds: a folder per sound; the library reads every mod's</summary>
    public string SoundsPath => Path.Combine(BaseContentPath, Mods.ContentTypes.Sounds);

    /// <summary>The install's master GUIDs, where Kunos cars name their sound events</summary>
    public string SfxGuidsPath => Path.Combine(AssettoCorsaPath, "content", "sfx", "GUIDs.txt");

    /// <summary>The AC folder the game uses holds Assetto Corsa (<see cref="IsInstall"/>)</summary>
    public bool IsValidInstallation() => IsInstall(AssettoCorsaPath);

    /// <summary>
    /// A folder that holds Assetto Corsa: its cars and tracks. The one test of an install, for the folder the game
    /// starts with and for the one it finds on the machine.
    /// </summary>
    public static bool IsInstall(string? folder)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(folder)
                   && Directory.Exists(Path.Combine(folder, "content", "cars"))
                   && Directory.Exists(Path.Combine(folder, "content", "tracks"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
