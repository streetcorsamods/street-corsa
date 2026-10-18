using StreetCorsa.Audio;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// The sound of a job being shown done, step by step (<see cref="Garage.RebuildShow"/>'s steps): under each step the
    /// cue of the work loops, and the one named for its start plays once. One show at a time: <see cref="Begin"/> gives a
    /// run the show checks itself by (<see cref="IsCurrent"/>), so a show ended by another, or by the player leaving,
    /// stops at its next step and leaves the sound to the new one. Only the sound: the captions and the rest are the
    /// screen's.
    /// </summary>
    public sealed class JobShowPlayer
    {
        private int _run;
        private PlayingSound? _loop;

        /// <summary>A show starts: its run, which <see cref="IsCurrent"/> answers for until the next show or <see cref="Cancel"/></summary>
        public int Begin() => ++_run;

        /// <summary>The show of <paramref name="run"/> is the one on</summary>
        public bool IsCurrent(int run) => run == _run;

        /// <summary>A step starts: the work's sound so far fades, <paramref name="sound"/> loops, and <paramref name="start"/> plays once when given</summary>
        public void Step(string sound, string? start, float fadeSeconds = 0.3f)
        {
            AudioHub.Shared.StopLoop(_loop, fadeSeconds);
            _loop = AudioHub.Shared.PlayLoop(sound, 0.3f);
            if (!string.IsNullOrWhiteSpace(start)) Sfx.Play(start);
        }

        /// <summary>The work's sound fades out</summary>
        public void Stop(float fadeSeconds = 0.3f)
        {
            AudioHub.Shared.StopLoop(_loop, fadeSeconds);
            _loop = null;
        }

        /// <summary>The show on ends at once, its sound and all: the next step it reaches is not shown</summary>
        public void Cancel(float fadeSeconds = 0.3f)
        {
            _run++;
            Stop(fadeSeconds);
        }
    }
}
