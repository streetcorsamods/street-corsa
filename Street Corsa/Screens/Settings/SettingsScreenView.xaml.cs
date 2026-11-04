namespace StreetCorsa.Screens.Settings
{
    /// <summary>
    /// Interaction logic for SettingsScreenView.xaml
    /// </summary>
    public partial class SettingsScreenView : System.Windows.Controls.UserControl
    {
        public SettingsScreenView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // A password box's text cannot be bound (WPF keeps it out of bindings on purpose): the typed key goes to the AI
        // section as it is typed, and the box empties once the section has saved it encrypted

        private void OnAiKeyChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SettingsScreenViewModel settings) settings.Ai.TypedKey = AiKeyBox.Password;
        }

        private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is SettingsScreenViewModel old)
            {
                old.Ai.KeyCleared -= ClearKey;
                old.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is SettingsScreenViewModel now)
            {
                now.Ai.KeyCleared += ClearKey;
                now.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        // The button pressed to swap the settings and the credits goes with the screen it was on: the focus moves to the
        // button that swaps back, on the screen that came up, so Enter and Tab carry on from there
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SettingsScreenViewModel.ShowingCredits) || sender is not SettingsScreenViewModel settings) return;

            var target = settings.ShowingCredits ? CreditsBackButton : CreditsButton;
            Dispatcher.BeginInvoke(() => target.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void ClearKey() => AiKeyBox.Clear();
    }
}
