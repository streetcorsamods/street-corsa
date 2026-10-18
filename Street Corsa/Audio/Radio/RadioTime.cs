namespace StreetCorsa.Audio.Radio;

/// <summary>How the radio writes a time: a song's length, where it has got to</summary>
public static class RadioTime
{
    /// <summary>"3:07" for a song, "1:02:30" past an hour (a long mix), "0:00" at the start</summary>
    public static string Format(uint ms)
    {
        var time = TimeSpan.FromMilliseconds(ms);
        return time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
    }
}
