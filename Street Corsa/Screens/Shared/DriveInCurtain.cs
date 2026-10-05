using System.Windows;
using System.Windows.Media.Animation;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// The black curtain over a screen the player drives into (<see cref="DriveInScreenViewModel"/>): it lifts as the car
    /// comes in, the radio the car's from there, and comes down once the car has gone
    /// </summary>
    public static class DriveInCurtain
    {
        private static readonly TimeSpan Up = TimeSpan.FromSeconds(1.2);
        private static readonly TimeSpan Down = TimeSpan.FromSeconds(0.9);

        /// <summary>The curtain worked by <paramref name="now"/>'s visit, no longer by <paramref name="was"/>'s</summary>
        public static void Hang(UIElement curtain, DriveInScreenViewModel? was, DriveInScreenViewModel? now)
        {
            if (was != null)
            {
                was.OpenCurtain = null;
                was.CloseCurtain = null;
            }

            if (now == null) return;
            now.OpenCurtain = () =>
            {
                curtain.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, Up));

                // The car comes in with the player in it: the radio is the car's from here
                Audio.Radio.RadioPlayer.Shared.SetInCar(true);
            };
            now.CloseCurtain = () => Controls.CurtainFade.ToAsync(curtain, 1, Down);
        }
    }
}
