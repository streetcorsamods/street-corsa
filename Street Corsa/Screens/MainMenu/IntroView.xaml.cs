using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace StreetCorsa.Screens.MainMenu
{
    /// <summary>
    /// The title the game opens on. The window puts it up the moment it shows, so the player never looks at black while
    /// the game reads its content; the main screen then takes it over (<see cref="Claim"/>) and keeps it up until its
    /// showroom is ready. A first screen that does not take it over sees it fade away.
    /// </summary>
    public partial class IntroView : System.Windows.Controls.UserControl
    {
        private bool _going;

        public IntroView()
        {
            InitializeComponent();

            // A game's own logo stands in for the lettered title
            if (Styles.ThemeImages.LoadModImage(Styles.ThemeImages.IntroLogo) is { } logo)
            {
                LogoImage.Source = logo;
                LogoImage.Visibility = Visibility.Visible;
                LogoText.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>When it came up</summary>
        public DateTime ShownAt { get; } = DateTime.UtcNow;

        /// <summary>A screen took it over: it goes when that screen says</summary>
        public bool IsClaimed { get; private set; }

        /// <summary>Takes the title over; false when it is already going</summary>
        public bool Claim()
        {
            if (_going) return false;
            IsClaimed = true;
            return true;
        }

        /// <summary>How long the title takes to fade away</summary>
        private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(900));

        /// <summary>Fades the title off what is behind it, and stops it taking the clicks at once</summary>
        public void FadeOut()
        {
            if (_going) return;
            _going = true;

            var fade = new DoubleAnimation(0, FadeDuration);
            fade.Completed += (_, _) => Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
