using StreetCorsa.ViewModels;
using StreetCorsa.Models.AC;
using StreetCorsa.Models.Race;

namespace StreetCorsa.Screens.Diner
{
    /// <summary>
    /// View model for displaying a track card in the diner screen
    /// </summary>
    public class TrackCardViewModel : ObservableObject
    {
        public TrackInfo Track { get; set; } = new();
        public TrackConfiguration? Configuration { get; set; }

        /// <summary>
        /// Display name (configuration name or track name)
        /// </summary>
        public string DisplayName => Configuration?.Name ?? Track.Name;

        /// <summary>
        /// Track identifier
        /// </summary>
        public string TrackId => Track.TrackId;

        /// <summary>
        /// Configuration folder name (null for tracks without configurations)
        /// </summary>
        public string? ConfigurationId => Configuration?.FolderName;

        /// <summary>
        /// Path to the track preview image
        /// </summary>
        public string? PreviewPath { get; set; }

        /// <summary>
        /// Whether the track has a preview image
        /// </summary>
        public bool HasPreview => !string.IsNullOrEmpty(PreviewPath);

        /// <summary>
        /// Width the preview is decoded at: the card is 110 x 90 and fills it, so a wide picture is drawn about 160 px
        /// wide; twice that keeps it sharp on a high-DPI screen
        /// </summary>
        private const int PreviewDecodeWidth = 320;

        /// <summary>The preview, decoded small and shared (<see cref="Shared.Thumbnails"/>); null without one</summary>
        public System.Windows.Media.ImageSource? PreviewImage => Shared.Thumbnails.Load(PreviewPath, PreviewDecodeWidth);

        /// <summary>
        /// The races this track takes: <see cref="RaceFormatFamilies.Drag"/> on a strip, <see cref="RaceFormatFamilies.Road"/>
        /// anywhere else
        /// </summary>
        public string Family { get; set; } = RaceFormatFamilies.Road;

        public bool IsDrag => Family == RaceFormatFamilies.Drag;

        private bool _isSelected;
        /// <summary>
        /// Whether this track is currently selected
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
