using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using StreetCorsa.Audio.Radio;
using StreetCorsa.Dialogs;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Navigation;

namespace StreetCorsa
{
    public partial class MainWindow : System.Windows.Window
    {
        public NavigationService NavigationService { get; }
        public DialogService DialogService { get; }

        // A change of screen: the screen left fades to black, then the black fades off the new one
        private static readonly TimeSpan ScreenFadeOut = TimeSpan.FromMilliseconds(260);
        private static readonly TimeSpan ScreenFadeIn = TimeSpan.FromMilliseconds(380);

        // Which change of screen is playing: one that is overtaken by another does not end the new one's
        private int _transition;

        // How long one dialog takes to fade in over another it replaces
        private static readonly TimeSpan DialogSwapFade = TimeSpan.FromMilliseconds(150);

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Navigation);

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
            NavigationService.ScreenChanging += OnScreenChanging;

            // A dialog that closed stays in the host while it fades out, and goes once the host is gone
            DialogHost.IsVisibleChanged += (_, e) =>
            {
                if (!(bool)e.NewValue && DialogService.CurrentDialog == null) DialogContent.Content = null;
            };

            // Assetto Corsa on and off: the screen fades to the loader for it and back (the events may come from any thread)
            app.Launcher.ExecutionStarted += (_, _) => Dispatcher.BeginInvoke(() => ShowRacing(true));
            app.Launcher.ExecutionEnded += (_, _) => Dispatcher.BeginInvoke(() => ShowRacing(false));

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

            // Both fade in and out; while one fades out it takes neither the mouse nor the keys
            Reveal.SetIsShown(RadioHost, radio);
            RadioHost.IsEnabled = radio && !dialog;
            Reveal.SetIsShown(DialogHost, dialog);
            DialogHost.IsEnabled = dialog;
            ScreenHost.IsEnabled = !dialog && !radio;
        }

        /// <summary>
        /// A screen is about to take another's place: a still of what is on show goes over it, on black, and fades to
        /// black; then the black fades off the new screen. The new screen is built and entered behind it meanwhile.
        /// </summary>
        private void OnScreenChanging()
        {
            if (!IsLoaded || ScreenArea.ActualWidth < 1 || ScreenArea.ActualHeight < 1) return;

            var transition = ++_transition;
            BitmapSource? still = null;
            try
            {
                still = TakeStill(ScreenArea);
            }
            catch (Exception ex)
            {
                // Without it the screen goes to black at once and fades in from there
                _logger.Warning(ex, "Could not take a still of the screen being left");
            }

            TransitionStill.Source = still;
            TransitionStill.BeginAnimation(OpacityProperty, null);
            TransitionStill.Opacity = 1;
            TransitionBlack.BeginAnimation(OpacityProperty, null);
            TransitionBlack.Opacity = 1;
            TransitionLayer.Visibility = Visibility.Visible;

            // Played once the new screen is built and laid out behind the still: building one can hold the UI thread
            // for a good half second, and a fade started now would be over before its first frame was drawn
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => PlayTransition(transition));
        }

        /// <summary>The still fades to black, then the black off the new screen; unless another change took over</summary>
        private void PlayTransition(int transition)
        {
            if (transition != _transition) return;

            TransitionStill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, ScreenFadeOut)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            });

            var fadeIn = new DoubleAnimation(1, 0, ScreenFadeIn)
            {
                BeginTime = ScreenFadeOut,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (_, _) =>
            {
                if (transition != _transition) return;
                TransitionLayer.Visibility = Visibility.Collapsed;
                TransitionStill.Source = null;
            };
            TransitionBlack.BeginAnimation(OpacityProperty, fadeIn);
        }

        /// <summary>What the element shows now, at the screen's own resolution</summary>
        private static BitmapSource TakeStill(FrameworkElement element)
        {
            var dpi = VisualTreeHelper.GetDpi(element);
            var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
            var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));

            var still = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            still.Render(element);
            still.Freeze();
            return still;
        }

        /// <summary>
        /// Assetto Corsa is running: the screen fades to a loader until it closes, and back. A race has a screen of its
        /// own for that (the race screen), which is left as it is.
        /// </summary>
        private void ShowRacing(bool racing)
        {
            if (racing && NavigationService.CurrentScreen is Screens.RaceLoading.RaceLoadingScreenViewModel) return;
            Reveal.SetIsShown(RacingLayer, racing);
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

            // One dialog straight after another (the next one waiting): the new one fades in on the card that stays up
            var dialog = DialogService.CurrentDialog;
            if (dialog != null)
            {
                var swapping = DialogHost.IsVisible && DialogContent.Content != null && !ReferenceEquals(DialogContent.Content, dialog);
                DialogContent.Content = dialog;
                if (swapping) DialogCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, DialogSwapFade));
            }

            UpdateHosts();
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
