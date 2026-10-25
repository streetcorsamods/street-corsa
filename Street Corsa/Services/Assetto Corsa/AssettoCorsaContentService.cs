using System.IO;
using System.Linq;
using System.Text.Json;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Models.AC;

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

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
                var trackInfo = JsonSerializer.Deserialize<TrackInfo>(File.ReadAllText(jsonPathToLoad), JsonOptions);

                if (trackInfo != null)
                {
                    trackInfo.TrackId = trackId;
                    trackInfo.FolderPath = trackFolder;

                    // Load track configurations (variants)
                    LoadTrackConfigurations(trackInfo, trackFolder);

                    tracks.Add(trackInfo);
                    _logger.Debug("Loaded track: {Name} ({TrackId}) from {Folder}", trackInfo.Name, trackId, trackFolder);
                }
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

            try
            {
                var folderName = Path.GetFileName(subDir);
                var jsonContent = File.ReadAllText(variantJsonPath);
                var configData = JsonSerializer.Deserialize<TrackConfiguration>(jsonContent, JsonOptions);

                if (configData != null)
                {
                    configData.FolderName = folderName;
                    trackInfo.Configurations.Add(configData);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.Warning(ex, "Track {TrackId}: layout {File} skipped", trackInfo.TrackId, variantJsonPath);
            }
        }
    }

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
