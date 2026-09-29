using System.Windows.Input;

namespace StreetCorsa.Screens.SoundBrowser
{
    public partial class SoundBrowserScreenView : System.Windows.Controls.UserControl
    {
        public SoundBrowserScreenView()
        {
            InitializeComponent();
        }

        /// <summary>A double click plays the sound again, even with "Play when picked" off</summary>
        private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is SoundBrowserScreenViewModel vm) vm.PlaySelected();
        }
    }
}
