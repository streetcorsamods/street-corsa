using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using StreetCorsa.Audio.Radio;
using StreetCorsa.Dialogs;
using StreetCorsa.Navigation;

namespace StreetCorsa
{
    public partial class MainWindow : System.Windows.Window
    {
        public NavigationService NavigationService { get; }
        public DialogService DialogService { get; }

        // What had the keyboard on the screen last, to give it back when the dialogs are done. Weak: a screen left
        // behind must not stay in memory for the element of it that had the keyboard last.
        private WeakReference<IInputElement>? _screenFocus;

        public MainWindow()
        {
            InitializeComponent();

            var app = (App)System.Windows.Application.Current;
            NavigationService = app.NavigationService;
            DialogService = app.DialogService;

            DataContext = this;

            ScreenHost.PreviewGotKeyboardFocus += (_, e) => _screenFocus = e.NewFocus is { } focus ? new WeakReference<IInputElement>(focus) : null;
            DialogService.PropertyChanged += OnDialogChanged;

            // The radio: its panel over the screen, its hotkeys wherever the focus is, a note as a song starts
            RadioPanelViewModel.Shared.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RadioPanelViewModel.IsOpen)) OnRadioPanelChanged();
            };
            RadioPlayer.Shared.SongStarted += ShowNowPlaying;
            PreviewKeyDown += OnRadioKeys;

            // After the screens have had it: the garage's part placement takes R for its own
            KeyDown += OnRadioPowerKey;
            UpdateHosts();
        }

        /// <summary>The screen takes input only with nothing in front of it; the radio's panel none while a dialog is up</summary>
        private void UpdateHosts()
        {
            var radio = RadioPanelViewModel.Shared.IsOpen;
            var dialog = DialogService.IsDialogOpen;
            RadioHost.Visibility = radio ? Visibility.Visible : Visibility.Collapsed;
            RadioHost.IsEnabled = !dialog;
            ScreenHost.IsEnabled = !dialog && !radio;
        }

        private void OnRadioPanelChanged()
        {
            UpdateHosts();
            var open = RadioPanelViewModel.Shared.IsOpen;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (open) RadioHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                else if (_screenFocus?.TryGetTarget(out var focus) == true && focus is UIElement { IsVisible: true, IsEnabled: true } element
                         && ScreenHost.IsAncestorOf(element))
                    Keyboard.Focus(element);
            });
        }

        /// <summary>
        /// F2 opens and closes the radio, Esc closes it (R, its power, is <see cref="OnRadioPowerKey"/>); the keyboard's
        /// media keys work it from anywhere. Left alone
        /// while a dialog is up, and F2 while typing in the panel's boxes is still F2.
        /// </summary>
        private void OnRadioKeys(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (DialogService.IsDialogOpen) return;

            var radio = RadioPlayer.Shared;
            switch (e.Key)
            {
                case Key.F2:
                    RadioPanelViewModel.Shared.IsOpen = !RadioPanelViewModel.Shared.IsOpen;
                    break;
                case Key.Escape when RadioPanelViewModel.Shared.IsOpen:
                    RadioPanelViewModel.Shared.IsOpen = false;
                    break;
                case Key.MediaPlayPause:
                    radio.TogglePlay();
                    break;
                case Key.MediaNextTrack:
                    radio.Next();
                    break;
                case Key.MediaPreviousTrack:
                    radio.Previous();
                    break;
                case Key.MediaStop:
                    radio.Stop();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        /// <summary>
        /// R switches the radio on and off, wherever the player is, unless they are typing, a dialog is up, or a screen
        /// took the key for itself first (the garage's part placement)
        /// </summary>
        private void OnRadioPowerKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Handled || e.Key != Key.R || Keyboard.Modifiers != ModifierKeys.None || DialogService.IsDialogOpen) return;
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;

            RadioPlayer.Shared.TogglePower();
            e.Handled = true;
        }

        /// <summary>The song's name at the bottom of the screen for a few seconds; not while the panel shows it</summary>
        private void ShowNowPlaying(RadioSong song)
        {
            if (RadioPanelViewModel.Shared.IsOpen) return;

            RadioToastText.Text = song.Display;
            var show = new DoubleAnimationUsingKeyFrames();
            show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.0))));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.8))));
            RadioToast.BeginAnimation(OpacityProperty, show);
        }

        /// <summary>
        /// The keyboard goes into a dialog when it opens (the screen behind is disabled, and would otherwise keep it
        /// nowhere), and back to what had it on the screen once the last dialog closes. After the layout, so the
        /// dialog's buttons are there to take it.
        /// </summary>
        private void OnDialogChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DialogService.CurrentDialog)) return;
            UpdateHosts();

            var dialog = DialogService.CurrentDialog;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!ReferenceEquals(dialog, DialogService.CurrentDialog)) return;

                if (dialog != null)
                {
                    DialogHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }
                else if (_screenFocus?.TryGetTarget(out var focus) == true && focus is UIElement { IsVisible: true, IsEnabled: true } element
                         && ScreenHost.IsAncestorOf(element))
                {
                    Keyboard.Focus(element);
                }
            });
        }
    }
}
