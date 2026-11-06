using StreetCorsa.ViewModels;
using StreetCorsa.Models.AC;
using StreetCorsa.Models.Race;
using StreetCorsa.Screens.Shared;

namespace StreetCorsa.Screens.Diner
{
    /// <summary>
    /// A track, or one layout of it, the diner races on: the <see cref="PracticeTrack"/> the track picker shows, with the
    /// races it takes. Its name and picture are the picker card's, so the diner shows what was picked.
    /// </summary>
    public class TrackCardViewModel : ObservableObject
    {
        public TrackCardViewModel(PracticeTrack practice, string family)
        {
            Practice = practice;
            Family = family;
        }

        /// <summary>The track as the picker has it</summary>
        public PracticeTrack Practice { get; }

        public TrackInfo Track => Practice.Track;
        public TrackConfiguration? Configuration => Practice.Layout;

        /// <summary>"Name - Layout", as the picker writes it</summary>
        public string DisplayName => Practice.DisplayName;

        /// <summary>
        /// Track identifier
        /// </summary>
        public string TrackId => Track.TrackId;

        /// <summary>
        /// Configuration folder name (null for tracks without configurations)
        /// </summary>
        public string? ConfigurationId => Practice.Configuration;

        /// <summary>Raced from A to B: no race of more than one lap here (<see cref="TrackInfo.IsPointToPoint"/>)</summary>
        public bool IsPointToPoint => Track.IsPointToPoint(Configuration?.FolderName);

        /// <summary>
        /// Width the preview is decoded at: the box is 128 x 76 and fills it; twice that keeps it sharp on a high-DPI
        /// screen
        /// </summary>
        private const int PreviewDecodeWidth = 320;

        private System.Windows.Media.ImageSource? _previewImage;
        private bool _previewAsked;

        /// <summary>
        /// The preview the picker shows (<see cref="PracticeTrack.FindPreview"/>), decoded small and shared
        /// (<see cref="Shared.Thumbnails"/>), once looked up and read off the UI thread: asking for it the first time
        /// starts the reading. Null without one, and until it is read
        /// </summary>
        public System.Windows.Media.ImageSource? PreviewImage
        {
            get
            {
                if (!_previewAsked && !string.IsNullOrEmpty(Track.FolderPath))
                {
                    _previewAsked = true;
                    _ = ReadPreviewAsync();
                }

                return _previewImage;
            }
        }

        private async Task ReadPreviewAsync()
        {
            _previewImage = await Shared.Thumbnails.LoadAsync(Practice.FindPreview, PreviewDecodeWidth, TrackId);
            OnPropertyChanged(nameof(PreviewImage));
        }

        /// <summary>
        /// The races this track takes: <see cref="RaceFormatFamilies.Drag"/> on a strip, <see cref="RaceFormatFamilies.Road"/>
        /// anywhere else
        /// </summary>
        public string Family { get; }

        public bool IsDrag => Family == RaceFormatFamilies.Drag;

        /// <summary>The races it takes, as a short tag: DRAG or ROAD</summary>
        public string Kind => Services.Text.GameText.Say(IsDrag ? "diner.drag" : "diner.road");
    }
}
