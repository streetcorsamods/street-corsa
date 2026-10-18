using Newtonsoft.Json;
using StreetCorsa.Audio;
using StreetCorsa.Configuration;
using StreetCorsa.Services.Settings;

namespace StreetCorsa.Tests;

public class AudioMixTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(0.5, 0.177827941)]
    [InlineData(0.75, 0.421696503)]
    public void A_slider_runs_over_thirty_decibels(double slider, double amplitude) =>
        Assert.Equal(amplitude, AudioMix.Amplitude(slider), 5);

    [Fact]
    public void A_louder_slider_is_never_quieter()
    {
        var last = -1f;
        for (var i = 0; i <= 100; i++)
        {
            var amplitude = AudioMix.Amplitude(i / 100.0);
            Assert.True(amplitude >= last);
            last = amplitude;
        }
    }

    [Fact]
    public void A_bus_plays_under_the_master_and_the_fade()
    {
        var settings = new AudioSettings { Master = 0.5, Radio = 1.0, Engine = 0.0 };

        Assert.Equal(0.177828f, AudioMix.Gain(settings, AudioBus.Radio, 1f), 5);
        Assert.Equal(0.088914f, AudioMix.Gain(settings, AudioBus.Radio, 0.5f), 5);
        Assert.Equal(0f, AudioMix.Gain(settings, AudioBus.Engine, 1f));
        Assert.Equal(0f, AudioMix.Gain(new AudioSettings { Master = 0 }, AudioBus.Effects, 1f));
    }

    [Fact]
    public void The_fade_gets_there_in_its_time_and_stops()
    {
        var half = TimeSpan.FromTicks(AudioMix.FadeTime.Ticks / 2);

        var fade = AudioMix.StepFade(1f, 0f, half);
        Assert.Equal(0.5f, fade, 3);
        fade = AudioMix.StepFade(fade, 0f, AudioMix.FadeTime);
        Assert.Equal(0f, fade);

        Assert.Equal(1f, AudioMix.StepFade(0.9f, 1f, TimeSpan.FromSeconds(10)));
        Assert.Equal(0.3f, AudioMix.StepFade(0.3f, 1f, TimeSpan.Zero));
        Assert.Equal(0.3f, AudioMix.StepFade(0.3f, 1f, TimeSpan.FromSeconds(-1)));
    }

    [Theory]
    [InlineData(false, true, true, 1f)]
    [InlineData(false, false, true, 0f)]
    [InlineData(true, false, true, 1f)]
    [InlineData(true, true, false, 1f)]
    [InlineData(false, false, false, 1f)]
    public void The_radio_is_heard_over_a_race_but_not_behind_other_windows_when_asked(bool racing, bool focused, bool mute, float target) =>
        Assert.Equal(target, AudioMix.RadioFadeTarget(racing, focused, mute));

    [Theory]
    [InlineData(false, true, true, 1f)]
    [InlineData(false, false, true, 0f)]
    [InlineData(false, false, false, 1f)]
    [InlineData(true, true, false, 0f)]
    [InlineData(true, false, false, 0f)]
    public void Silent_in_a_race_and_behind_other_windows_when_asked(bool racing, bool focused, bool mute, float target) =>
        Assert.Equal(target, AudioMix.FadeTarget(racing, focused, mute));
}

[Collection(GlobalsCollection.Name)]
public class AudioSettingsTests
{
    [Fact]
    public void Every_bus_has_its_own_slider()
    {
        var settings = new AudioSettings();
        var buses = Enum.GetValues<AudioBus>();
        for (var i = 0; i < buses.Length; i++) settings.Set(buses[i], i / 10.0);
        for (var i = 0; i < buses.Length; i++) Assert.Equal(i / 10.0, settings.For(buses[i]));
    }

    [Fact]
    public void A_clone_is_not_the_settings_it_came_from()
    {
        var settings = new AudioSettings { Master = 0.3 };
        var clone = settings.Clone();
        settings.Master = 0.9;
        Assert.Equal(0.3, clone.Master);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Audio\": null}")]
    [InlineData("{\"FreeRunTrack\": \"monza\"}")]
    public void Settings_from_before_sound_load_the_default_volumes(string json)
    {
        var audio = Load(json).Audio;

        Assert.NotNull(audio);
        Assert.Equal(1.0, audio.Master);
        Assert.Equal(0.8, audio.Radio);
        Assert.True(audio.MuteInBackground);
    }

    [Fact]
    public void Volumes_go_to_disk_and_come_back()
    {
        var folder = Directory.CreateTempSubdirectory("sc_audio_");
        try
        {
            var path = Path.Combine(folder.FullName, "settings.json");
            var service = new GameSettingsService(path, null, findInstall: () => null);
            service.Current.AssettoCorsaPath = AppSettings.Instance.AssettoCorsaPath;
            service.Current.Audio.Master = 0.4;
            service.Current.Audio.Set(AudioBus.Ambience, 0.25);
            service.Current.Audio.MuteInBackground = false;
            Assert.True(service.Save());

            var again = new GameSettingsService(path, null, findInstall: () => null).Current.Audio;
            Assert.Equal(0.4, again.Master);
            Assert.Equal(0.25, again.Ambience);
            Assert.Equal(1.0, again.Engine);
            Assert.False(again.MuteInBackground);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void A_partial_audio_block_keeps_the_other_defaults()
    {
        var audio = Load("{\"Audio\": {\"Master\": 0.5}}").Audio;
        Assert.Equal(0.5, audio.Master);
        Assert.Equal(0.8, audio.Radio);
        Assert.Equal(1.0, audio.Interface);
    }

    /// <summary>
    /// Through the service, as the game loads them. The file names the AC folder already in use: the service makes
    /// the folder it loads the one the whole process uses
    /// </summary>
    private static GameSettings Load(string json)
    {
        var folder = Directory.CreateTempSubdirectory("sc_audio_");
        try
        {
            var settings = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json)!;
            settings["AssettoCorsaPath"] = AppSettings.Instance.AssettoCorsaPath;
            var path = Path.Combine(folder.FullName, "settings.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(settings));
            return new GameSettingsService(path, null, findInstall: () => null).Current;
        }
        finally
        {
            folder.Delete(true);
        }
    }
}
