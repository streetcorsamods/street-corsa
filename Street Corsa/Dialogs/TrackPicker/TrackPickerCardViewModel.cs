using System.Windows.Media;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Dialogs.TrackPicker
{
    /// <summary>
    /// A track's card in the picker: its preview, its name and layout, where it is, how long it is and how many pits it
    /// has. The preview is only read once the card first comes into view, off the UI thread and decoded small, so a
    /// list of hundreds of tracks scrolls smoothly.
    /// </summary>
    public sealed class TrackPickerCardViewModel : ObservableObject
    {
        // The card is about 240 px wide: twice that keeps the picture sharp on a high-DPI screen
        private const int PreviewDecodeWidth = 480;

        // A few pictures read at a time: a fast scroll through the list must not start hundreds at once
        private static readonly SemaphoreSlim Readers = new(3);

        private ImageSource? _preview;
        private bool _previewAsked;
        private bool _previewRead;
        private bool _isSelected;

        public TrackPickerCardViewModel(PracticeTrack track, bool isLast)
        {
            Track = track;
            IsLast = isLast;
        }

        public PracticeTrack Track { get; }

        public string Name => Track.Name;
        public string? LayoutName => Track.LayoutName;
        public bool HasLayout => LayoutName != null;

        /// <summary>"City, Country", whichever it says</summary>
        public string Place => string.Join(", ", new[] { Track.Track.City, Track.Track.Country }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        public bool HasPlace => Place.Length > 0;

        /// <summary>Its length in the player's units and its pits, whichever it says: "0.25 mi · 2 pits"</summary>
        public string Facts
        {
            get
            {
                var facts = new List<string>(2);
                if (Track.LengthMetres is { } metres && metres > 0) facts.Add(GameText.Say("trackpicker.length", ("length", Measure.Distance(metres / 1000))));
                if (Track.Pitboxes is { } pits) facts.Add(GameText.Say("trackpicker.pits", ("count", pits)));
                return string.Join("  ·  ", facts);
            }
        }

        public bool HasFacts => Facts.Length > 0;

        /// <summary>Its first few tags</summary>
        public string Tags => string.Join("  ·  ", (Track.Track.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Take(4));
        public bool HasTags => Tags.Length > 0;

        /// <summary>The one picked last time</summary>
        public bool IsLast { get; }

        /// <summary>The one picked now: the Go button takes it, and a click on it again too</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>The preview, once read; asking for it the first time starts the reading</summary>
        public ImageSource? Preview
        {
            get
            {
                if (!_previewAsked)
                {
                    _previewAsked = true;
                    _ = ReadPreviewAsync();
                }

                return _preview;
            }
        }

        /// <summary>Read, and there was none: the card says so instead of waiting for it</summary>
        public bool HasNoPreview => _previewRead && _preview == null;

        private async Task ReadPreviewAsync()
        {
            try
            {
                _preview = await Task.Run(async () =>
                {
                    await Readers.WaitAsync();
                    try
                    {
                        return Thumbnails.Load(Track.FindPreview(), PreviewDecodeWidth);
                    }
                    finally
                    {
                        Readers.Release();
                    }
                });
            }
            catch (Exception ex)
            {
                AppLoggerFactory.CreateLogger(LogCategory.UI).Warning("The preview of {Track} could not be read: {Error}", Track.Key, ex.Message);
            }

            _previewRead = true;
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(HasNoPreview));
        }
    }
}
