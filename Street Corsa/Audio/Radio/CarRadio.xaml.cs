using System.ComponentModel;
using System.Windows;
using Canvas = System.Windows.Controls.Canvas;
using System.Windows.Input;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Audio.Radio
{
    /// <summary>
    /// The radio in the car's dash, on the screens the player drives on: power on and off (where it was), the song on
    /// the dial with its needle running along, the tuning knob for the next song. The dial opens the whole panel.
    /// </summary>
    public partial class CarRadio : System.Windows.Controls.UserControl
    {
        private static readonly Brush LampOn = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x3A));
        private static readonly Brush LampOff = new SolidColorBrush(Color.FromRgb(0x4A, 0x1A, 0x10));

        private RadioPlayer Radio => RadioPlayer.Shared;

        public CarRadio()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                Radio.PropertyChanged += OnRadioChanged;
                Refresh();
            };
            Unloaded += (_, _) => Radio.PropertyChanged -= OnRadioChanged;
            NeedleTrack.SizeChanged += (_, _) => PlaceNeedle();
        }

        private void OnRadioChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

        private void Refresh()
        {
            SongText.Text = Radio.IsOn || Radio.Current != null ? Radio.NowPlaying : GameText.Say("radio.car.no_music");
            Lamp.Fill = Radio.IsPlaying ? LampOn : LampOff;
            Dial.Opacity = Radio.IsPlaying ? 1.0 : 0.55;
            PlaceNeedle();
        }

        private void PlaceNeedle()
        {
            var width = Math.Max(0, NeedleTrack.ActualWidth - Needle.Width - 16);
            Canvas.SetLeft(Needle, 8 + width * Radio.PositionFraction);
            Canvas.SetTop(Needle, 1);
        }

        private void OnPower(object sender, RoutedEventArgs e) => Radio.TogglePower();

        private void OnTune(object sender, RoutedEventArgs e) => Radio.Next();

        private void OnTuneBack(object sender, MouseButtonEventArgs e)
        {
            Radio.Previous();
            e.Handled = true;
        }

        private void OnDial(object sender, MouseButtonEventArgs e) => RadioPanelViewModel.Shared.IsOpen = true;
    }
}
