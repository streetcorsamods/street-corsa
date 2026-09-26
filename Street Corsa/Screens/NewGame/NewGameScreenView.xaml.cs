using System.Windows;

namespace StreetCorsa.Screens.NewGame
{
    public partial class NewGameScreenView : System.Windows.Controls.UserControl
    {
        public NewGameScreenView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Auto-focus the player name TextBox
            PlayerNameTextBox.Focus();
            PlayerNameTextBox.SelectAll();
        }
    }
}
