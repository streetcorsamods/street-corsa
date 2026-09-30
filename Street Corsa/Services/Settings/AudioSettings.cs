using StreetCorsa.Audio;

namespace StreetCorsa.Services.Settings
{
    /// <summary>
    /// How loud each kind of sound plays, 0..1 as the sliders show it (<see cref="AudioMix.Amplitude"/> turns a slider
    /// into a gain), and whether the game goes quiet behind another window. A settings.json from before sound had
    /// settings loads these defaults.
    /// </summary>
    public class AudioSettings
    {
        public double Master { get; set; } = 1.0;
        public double Engine { get; set; } = 1.0;
        public double Effects { get; set; } = 1.0;
        public double Interface { get; set; } = 1.0;
        public double Ambience { get; set; } = 1.0;

        // A little under the rest: the player's own music is mastered far louder than a click or a room tone
        public double Radio { get; set; } = 0.8;

        /// <summary>Everything fades out while another window has the focus, and back in when the game has it again</summary>
        public bool MuteInBackground { get; set; } = true;

        /// <summary>The slider of <paramref name="bus"/></summary>
        public double For(AudioBus bus) => bus switch
        {
            AudioBus.Engine => Engine,
            AudioBus.Effects => Effects,
            AudioBus.Interface => Interface,
            AudioBus.Ambience => Ambience,
            AudioBus.Radio => Radio,
            _ => 1.0
        };

        public void Set(AudioBus bus, double value)
        {
            switch (bus)
            {
                case AudioBus.Engine: Engine = value; break;
                case AudioBus.Effects: Effects = value; break;
                case AudioBus.Interface: Interface = value; break;
                case AudioBus.Ambience: Ambience = value; break;
                case AudioBus.Radio: Radio = value; break;
            }
        }

        public AudioSettings Clone() => (AudioSettings)MemberwiseClone();
    }
}
