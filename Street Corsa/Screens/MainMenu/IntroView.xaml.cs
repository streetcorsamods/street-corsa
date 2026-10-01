using System.Windows;

namespace StreetCorsa.Screens.MainMenu
{
    /// <summary>The title the game opens on, over the main screen while its cars load</summary>
    public partial class IntroView : System.Windows.Controls.UserControl
    {
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
    }
}
