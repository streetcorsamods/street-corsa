namespace StreetCorsa.Navigation
{
    public interface IScreen
    {
        void Enter();

        /// <summary>
        /// What entering the screen waits for (a shop opened, a race run in AC), started right after <see cref="Enter"/>.
        /// A screen with work to await puts it here, never in an <c>async void Enter</c>: the navigation watches the task,
        /// so one that fails leaves the screen again for the one the player came from, as an <see cref="Enter"/> that
        /// throws does. Done at once for a screen with nothing to wait for.
        /// </summary>
        Task EnterAsync() => Task.CompletedTask;

        void Exit();

        /// <summary>
        /// The screen is shown again after a navigation away from it failed. Unlike <see cref="Enter"/> this must
        /// not start anything (a race, a visit that spends time, a save): the screen still holds its state and
        /// only undoes what <see cref="Exit"/> let go of.
        /// </summary>
        void Resume();

        /// <summary>The air of the place, a looping sound cue (<see cref="Audio.AudioHub.SetAmbience"/>); null for a quiet screen</summary>
        string? Ambience => null;

        /// <summary>
        /// The player sits in their car here: the radio is the car's, an AM set (<see cref="Audio.Radio.RadioPlayer.SetInCar"/>).
        /// The screen's view puts the radio in the car once its scene is up; the radio leaves it with the screen.
        /// </summary>
        bool InCar => false;

        /// <summary>A screen of a game in play: the window shows the player's money, the date and the time over it</summary>
        bool InGame => true;
    }
}
