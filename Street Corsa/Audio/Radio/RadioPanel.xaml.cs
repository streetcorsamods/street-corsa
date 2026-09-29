using System.Windows.Input;

namespace StreetCorsa.Audio.Radio
{
    public partial class RadioPanel : System.Windows.Controls.UserControl
    {
        public RadioPanel()
        {
            InitializeComponent();
            DataContext = RadioPanelViewModel.Shared;
        }

        /// <summary>A double click plays the song, and the list on from it</summary>
        private void OnSongDoubleClick(object sender, MouseButtonEventArgs e) => RadioPanelViewModel.Shared.PlaySelected();
    }
}
