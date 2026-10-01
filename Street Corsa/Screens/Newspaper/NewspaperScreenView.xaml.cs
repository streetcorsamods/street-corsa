using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace StreetCorsa.Screens.Newspaper
{
    public partial class NewspaperScreenView : System.Windows.Controls.UserControl
    {
        // How near the end of a section the next page of ads is put out: before the player gets there
        private const double MoreAdsAhead = 600;

        public NewspaperScreenView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not NewspaperScreenViewModel viewModel) return;

            if (viewModel.SkipEnterAnimation)
            {
                RootGrid.Opacity = 1;
                BackButton.Opacity = 1;
                Sheet.Opacity = 1;
                return;
            }

            RootGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)));
            var later = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(0.3) };
            BackButton.BeginAnimation(OpacityProperty, later);
            Sheet.BeginAnimation(OpacityProperty, later);
        }

        /// <summary>
        /// Scrolled near the end of the section, or a page too short to scroll yet: the next page of ads. Putting it out
        /// changes the extent, which comes back here, until the ads fill the page or run out.
        /// </summary>
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer { IsVisible: true } || DataContext is not NewspaperScreenViewModel viewModel) return;

            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - MoreAdsAhead) viewModel.ShowMore();
        }
    }
}
