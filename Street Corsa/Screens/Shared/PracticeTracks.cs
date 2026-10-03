using System.Globalization;
using System.IO;
using StreetCorsa.Models.AC;
using StreetCorsa.Services.Race;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// A track, or one layout of it, the player can take a car to on their own: a free run goes to any, a test-and-tune
    /// to a drag strip
    /// </summary>
    public sealed class PracticeTrack
    {
        public PracticeTrack(TrackInfo track, TrackConfiguration? layout)
        {
            Track = track;
            Layout = layout;
            Configuration = string.IsNullOrEmpty(layout?.FolderName) ? null : layout.FolderName;
        }

        public TrackInfo Track { get; }
        public TrackConfiguration? Layout { get; }

        public string TrackId => Track.TrackId;

        /// <summary>The layout's folder, what race.ini's CONFIG_TRACK names; null for a track with none</summary>
        public string? Configuration { get; }

        /// <summary>The track's own name (its folder's when ui_track.json gives none)</summary>
        public string Name => string.IsNullOrWhiteSpace(Track.Name) ? Track.TrackId : Track.Name;

        /// <summary>The layout's name; null for a track with none</summary>
        public string? LayoutName => Layout == null ? null : string.IsNullOrWhiteSpace(Layout.Name) ? Layout.FolderName : Layout.Name;

        /// <summary>"Name - Layout", as the free run's list wrote it</summary>
        public string DisplayName => LayoutName == null ? Name : $"{Name} - {LayoutName}";

        /// <summary>"track" or "track/configuration", as the settings remember it</summary>
        public string Key => Configuration == null ? TrackId : $"{TrackId}/{Configuration}";

        /// <summary>A drag strip: where a test-and-tune runs</summary>
        public bool IsStrip => Track.Type == TrackType.Dragstrip;

        /// <summary>How long it is in metres, the layout's or else the track's; null when neither says</summary>
        public double? LengthMetres => BracketRules.LengthMetres(Track, Layout);

        /// <summary>How many cars it has pits for, the layout's or else the track's; null when neither says</summary>
        public int? Pitboxes
        {
            get
            {
                var text = string.IsNullOrWhiteSpace(Layout?.Pitboxes) ? Track.Pitboxes : Layout.Pitboxes;
                return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0 ? count : null;
            }
        }

        /// <summary>
        /// Whether every word of <paramref name="query"/> is in its name, layout, city, country, tags or folder, ignoring
        /// case; an empty search matches every track
        /// </summary>
        public bool Matches(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;

            var haystack = string.Join('\n', new[] { Name, LayoutName, Track.City, Track.Country, TrackId, Configuration }
                .Concat(Track.Tags ?? []).Where(s => !string.IsNullOrWhiteSpace(s)));
            return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .All(word => haystack.Contains(word, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>
        /// The picture to show of it, off the disk: the layout's preview, else the track's, else the outlines the same
        /// way; null when it has none. Reads the disk: call it off the UI thread.
        /// </summary>
        public string? FindPreview()
        {
            if (string.IsNullOrEmpty(Track.FolderPath)) return null;

            var ui = Path.Combine(Track.FolderPath, "ui");
            var candidates = Configuration == null
                ? new[] { Path.Combine(ui, "preview.png"), Path.Combine(ui, "outline.png") }
                : new[]
                {
                    Path.Combine(ui, Configuration, "preview.png"), Path.Combine(ui, "preview.png"),
                    Path.Combine(ui, Configuration, "outline.png"), Path.Combine(ui, "outline.png")
                };
            return candidates.FirstOrDefault(File.Exists);
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>The tracks of the install as the free run and the test-and-tune offer them</summary>
    public static class PracticeTracks
    {
        /// <summary>Every track of the install, one entry per layout, by name</summary>
        public static List<PracticeTrack> All(IEnumerable<TrackInfo> tracks) =>
            tracks
                .SelectMany(t => t.Configurations.Count == 0
                    ? new[] { new PracticeTrack(t, null) }
                    : t.Configurations.Select(c => new PracticeTrack(t, c)))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.LayoutName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>The drag strips' layouts, the ones <see cref="RaceSetupBuilder.PickStrip"/> picks from</summary>
        public static List<PracticeTrack> Strips(IEnumerable<TrackInfo> tracks) => All(tracks).Where(t => t.IsStrip).ToList();

        /// <summary>The entry the settings remember by its key; null when it is no longer installed</summary>
        public static PracticeTrack? Find(IEnumerable<PracticeTrack> tracks, string? key) =>
            string.IsNullOrEmpty(key) ? null : tracks.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>Where a free run goes unless the player picks another: the last one, else the first that is no drag strip</summary>
        public static PracticeTrack? FreeRunDefault(IReadOnlyList<PracticeTrack> tracks, string? remembered) =>
            Find(tracks, remembered)
            ?? tracks.FirstOrDefault(t => !t.Key.Contains("drag", StringComparison.OrdinalIgnoreCase))
            ?? tracks.FirstOrDefault();

        /// <summary>Which strip a test-and-tune goes to unless the player picks another: the last one, else the one a race would pick</summary>
        public static PracticeTrack? StripDefault(IReadOnlyList<PracticeTrack> strips, string? remembered)
        {
            if (Find(strips, remembered) is { } last) return last;
            if (RaceSetupBuilder.PickStrip(strips.Select(s => s.Track).Distinct()) is not { } picked) return strips.FirstOrDefault();

            return strips.FirstOrDefault(s => s.TrackId == picked.TrackId && s.Configuration == picked.TrackConfig) ?? strips.FirstOrDefault();
        }

        /// <summary>
        /// The tracks a search finds, in the list's order; with no search, <paramref name="first"/> (the one picked last)
        /// leads, the rest after it
        /// </summary>
        public static List<PracticeTrack> Filter(IEnumerable<PracticeTrack> tracks, string? query, PracticeTrack? first = null)
        {
            var found = tracks.Where(t => t.Matches(query)).ToList();
            if (first != null && string.IsNullOrWhiteSpace(query) && found.Remove(first)) found.Insert(0, first);
            return found;
        }
    }
}
