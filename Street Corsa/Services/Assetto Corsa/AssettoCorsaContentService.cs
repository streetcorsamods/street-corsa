using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Models.AC;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Services;

/// <summary>
/// Service for discovering and loading Assetto Corsa content. The cars are read by the catalog's import
/// (<see cref="Catalog.CarImportService"/>); this service reads the tracks.
/// </summary>
public class AssettoCorsaContentService : IAssettoCorsaContentService
{
    private const string UiFolder = "ui";
    private const string TrackJson = "ui_track.json";

    private readonly AppSettings _settings;
    private List<TrackInfo> _tracks = [];
    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Import);

    public AssettoCorsaContentService()
    {
        _settings = AppSettings.Instance;
    }

    public async Task<List<TrackInfo>> LoadTracksAsync()
    {
        var tracksPath = _settings.TracksPath;
        if (!Directory.Exists(tracksPath))
        {
            var error = $"Tracks directory not found: {tracksPath}";
            _logger.Error("Tracks directory not found: {TracksPath}", tracksPath);
            throw new DirectoryNotFoundException(error);
        }

        // On a worker: a modded install has hundreds of tracks, and this runs during start-up
        _tracks = await Task.Run(() => LoadTracks(tracksPath));
        return _tracks;
    }

    internal List<TrackInfo> LoadTracks(string tracksPath)
    {
        var tracks = new List<TrackInfo>();
        var trackFolders = Directory.GetDirectories(tracksPath);
        var withJson = 0;

        foreach (var trackFolder in trackFolders)
        {
            try
            {
                // Only where AC keeps it: ui\ui_track.json for the track, ui\<layout>\ui_track.json for its layouts.
                // A track with no main file is read from its first layout's.
                var jsonPathToLoad = MainTrackJson(trackFolder);
                if (jsonPathToLoad == null) continue;
                withJson++;

                var trackId = Path.GetFileName(trackFolder);
                // The lenient way the cars' ui_car.json is read: "tags": "circuit" or "pitboxes": 20 is still a track
                if (AcUiJson.TryRead(jsonPathToLoad, out var error) is not { } ui)
                {
                    _logger.Warning("Track {TrackId} skipped: {Error}", trackId, error);
                    continue;
                }

                var trackInfo = Track(ui);
                trackInfo.TrackId = trackId;
                trackInfo.FolderPath = trackFolder;

                // Load track configurations (variants)
                LoadTrackConfigurations(trackInfo, trackFolder);

                tracks.Add(trackInfo);
                _logger.Debug("Loaded track: {Name} ({TrackId}) from {Folder}", trackInfo.Name, trackId, trackFolder);
            }
            catch (Exception ex)
            {
                // Log and continue - don't fail entire load for one bad track
                _logger.Warning(ex, "Error loading track from {Folder}", trackFolder);
            }
        }

        _logger.Information("Found {Count} tracks with a ui_track.json in {TracksPath}", withJson, tracksPath);
        return tracks;
    }

    /// <summary>The track's own ui_track.json, else its first layout's; null when it has neither</summary>
    private static string? MainTrackJson(string trackFolder)
    {
        var uiFolder = Path.Combine(trackFolder, UiFolder);
        if (!Directory.Exists(uiFolder)) return null;

        var main = Path.Combine(uiFolder, TrackJson);
        if (File.Exists(main)) return main;

        return Directory.EnumerateDirectories(uiFolder)
            .Select(layout => Path.Combine(layout, TrackJson))
            .FirstOrDefault(File.Exists);
    }

    private void LoadTrackConfigurations(TrackInfo trackInfo, string trackFolder)
    {
        // Check for track configurations in the ui folder
        var uiFolder = Path.Combine(trackFolder, UiFolder);

        if (!Directory.Exists(uiFolder))
            return;

        // A layout is a subfolder with its own ui_track.json (ui/drag200/ui_track.json): the folder's name is what
        // race.ini's CONFIG_TRACK names, and AC knows no other kind. Loose ui_*.json files next to them (Content
        // Manager's leftovers, translations) are not layouts: one taken for a layout would name a CONFIG_TRACK
        // that AC cannot load, or list a layout twice.
        foreach (var subDir in Directory.GetDirectories(uiFolder))
        {
            var variantJsonPath = Path.Combine(subDir, TrackJson);
            if (!File.Exists(variantJsonPath))
                continue;

            if (AcUiJson.TryRead(variantJsonPath, out var error) is not { } ui)
            {
                _logger.Warning("Track {TrackId}: layout {File} skipped: {Error}", trackInfo.TrackId, variantJsonPath, error);
                continue;
            }

            trackInfo.Configurations.Add(new TrackConfiguration
            {
                FolderName = Path.GetFileName(subDir),
                Name = AcUiJson.GetString(ui, "name") ?? string.Empty,
                Length = AcUiJson.GetString(ui, "length") ?? string.Empty,
                Pitboxes = AcUiJson.GetString(ui, "pitboxes") ?? string.Empty,
                Run = AcUiJson.GetString(ui, "run") ?? string.Empty,
                Tags = [.. AcUiJson.GetStrings(ui, "tags")]
            });
        }
    }

    /// <summary>What a track's ui_track.json says of it; what it leaves out, or gives as no text, is empty</summary>
    private static TrackInfo Track(JObject ui) => new()
    {
        Name = AcUiJson.GetString(ui, "name") ?? string.Empty,
        Description = AcUiJson.GetString(ui, "description") ?? string.Empty,
        Tags = [.. AcUiJson.GetStrings(ui, "tags")],
        Country = AcUiJson.GetString(ui, "country") ?? string.Empty,
        City = AcUiJson.GetString(ui, "city") ?? string.Empty,
        Length = AcUiJson.GetString(ui, "length") ?? string.Empty,
        Width = AcUiJson.GetString(ui, "width") ?? string.Empty,
        Pitboxes = AcUiJson.GetString(ui, "pitboxes") ?? string.Empty,
        Run = AcUiJson.GetString(ui, "run") ?? string.Empty
    };

    /// <summary>
    /// The installed tracks the game races on: game.json's <c>tracks</c> (<see cref="Rules.GameTracks"/>) of the setting in
    /// play, read each time, as the content switches at run time
    /// </summary>
    public List<TrackInfo> GetTracks()
    {
        var game = Rules.GameFile.Installed.Tracks;
        var tracks = game.Of(_tracks, out var noneIn);
        if (noneIn && _toldNoTracks != game)
        {
            _toldNoTracks = game;
            _logger.Warning("No installed road track is one the game races on (game.json tracks); racing on every track");
        }

        return tracks;
    }

    // The tracks setting told of having no installed road track, so it is logged once
    private Rules.GameTracks? _toldNoTracks;

    public string? GetTrackPreviewPath(string trackId)
    {
        if (string.IsNullOrEmpty(_settings.TracksPath))
            return null;

        // AC stores track previews at: content/tracks/{trackId}/ui/preview.png
        var previewPath = Path.Combine(_settings.TracksPath, trackId, UiFolder, "preview.png");

        if (File.Exists(previewPath))
            return previewPath;

        // Some tracks use outline.png as fallback
        var outlinePath = Path.Combine(_settings.TracksPath, trackId, UiFolder, "outline.png");

        if (File.Exists(outlinePath))
            return outlinePath;

        return null;
    }
}
