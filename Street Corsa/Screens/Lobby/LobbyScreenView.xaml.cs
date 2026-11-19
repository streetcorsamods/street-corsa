using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace StreetCorsa.Screens.Lobby
{
    /// <summary>
    /// The lobby's view: Escape steps the Settings card back while it is up (a dialog in front takes it first); with no
    /// car to film, or a room that will not load, the screen shows its picture
    /// </summary>
    public partial class LobbyScreenView : System.Windows.Controls.UserControl
    {
        private Window? _window;

        public LobbyScreenView()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                _window = Window.GetWindow(this);
                if (_window == null) return;
                _window.PreviewKeyDown -= OnWindowKeyDown;
                _window.PreviewKeyDown += OnWindowKeyDown;
            };
            Unloaded += (_, _) =>
            {
                if (_window != null) _window.PreviewKeyDown -= OnWindowKeyDown;
                _window = null;
            };

            Showcase.Failed += (_, _) => Fallback.Visibility = Visibility.Visible;
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || !IsLoaded || Dialogs.ModalState.IsOpen || DataContext is not LobbyScreenViewModel lobby) return;
            if (!lobby.CloseCardCommand.CanExecute(null)) return;

            lobby.CloseCardCommand.Execute(null);
            e.Handled = true;
        }
    }
}
