using StreetCorsa.Models.AC;

namespace StreetCorsa.Services;

/// <summary>
/// Service for discovering and loading Assetto Corsa content (tracks; the cars go through the catalog's import)
/// </summary>
public interface IAssettoCorsaContentService
{
    /// <summary>
    /// Loads all available tracks from the AC content folder
    /// </summary>
    Task<List<TrackInfo>> LoadTracksAsync();

    /// <summary>
    /// The installed tracks the game races on (game.json's tracks of the setting in play)
    /// </summary>
    List<TrackInfo> GetTracks();

    /// <summary>
    /// Gets the path to a track's preview image
    /// </summary>
    /// <param name="trackId">The track identifier</param>
    /// <returns>Full path to preview.png if it exists, null otherwise</returns>
    string? GetTrackPreviewPath(string trackId);
}
