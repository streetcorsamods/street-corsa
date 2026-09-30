using StreetCorsa.Audio;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Settings
{
    /// <summary>One volume on the Settings card: the master one (<see cref="Bus"/> null) or a bus's</summary>
    public class VolumeSlider : ObservableObject
    {
        private readonly Action<VolumeSlider> _changed;
        private double _value;

        public VolumeSlider(string label, AudioBus? bus, double value, Action<VolumeSlider> changed)
        {
            Label = label;
            Bus = bus;
            _value = Math.Clamp(value, 0.0, 1.0);
            _changed = changed;
        }

        public string Label { get; }

        public AudioBus? Bus { get; }

        /// <summary>0..1, as the settings keep it</summary>
        public double Value
        {
            get => _value;
            set
            {
                if (!SetProperty(ref _value, Math.Clamp(value, 0.0, 1.0))) return;
                OnPropertyChanged(nameof(Percent));
                _changed(this);
            }
        }

        public string Percent => $"{Math.Round(_value * 100):0}%";
    }
}
