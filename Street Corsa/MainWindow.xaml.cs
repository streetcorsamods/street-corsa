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

        /// <summary>The money card over a game's screens, and the window's End Day button</summary>
        public ViewModels.GameStatusViewModel Status { get; }

        // A change of screen: the screen left fades to black, then the black fades off the new one
        private static readonly TimeSpan ScreenFadeOut = TimeSpan.FromMilliseconds(260);
        private static readonly TimeSpan ScreenFadeIn = TimeSpan.FromMilliseconds(380);

        // Which change of screen is playing: one that is overtaken by another does not end the new one's
        private int _transition;

        // How long one dialog takes to fade in over another it replaces
        private static readonly TimeSpan DialogSwapFade = TimeSpan.FromMilliseconds(150);

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Navigation);

        // The money card is read off the game this often: the money and the clock change in many places, none of which tells
        private static readonly TimeSpan StatusInterval = TimeSpan.FromMilliseconds(250);

        // The "now playing" note: out of its anchor, held, and back in
        private static readonly TimeSpan ToastIn = TimeSpan.FromSeconds(0.35);
        private static readonly TimeSpan ToastHold = TimeSpan.FromSeconds(3.7);
        private static readonly TimeSpan ToastOut = TimeSpan.FromSeconds(0.5);
        private const double ToastGap = 12;
        private const double ToastTravel = 36;

        // The toast keeps this far off the window's left edge: a long song's name is cut short instead
        private const double ToastMargin = 20;

        // The still of a screen being left is taken no bigger than this across: on a 4K screen a full-size one is a
        // software render of the whole window on every change of screen, for a picture that is on show half a second
        private const int StillMaxWidth = 1920;

        // Assetto Corsa runs (a free run): the screen behind the loader takes no keys
        private bool _racingShown;
        private bool _contentShown;

        // What had the keyboard on the screen last, to give it back when the dialogs are done. Weak: a screen left
        // behind must not stay in memory for the element of it that had the keyboard last.
        private WeakReference<IInputElement>? _screenFocus;

        public MainWindow()
        {
            InitializeComponent();
            Title = Rules.GameFile.Installed.Title;
            // The taskbar's picture: a game's own when it has one; the exe's icon itself cannot change
            if (Styles.ThemeImages.LoadModIcon() is { } icon) Icon = icon;

            var app = (App)System.Windows.Application.Current;
            NavigationService = app.NavigationService;
            DialogService = app.DialogService;
            Status = new ViewModels.GameStatusViewModel(() => app.CurrentGameState, () => NavigationService.CurrentScreen,
                () => NavigationService.CanEndDayHere);

            DataContext = this;

            ScreenHost.PreviewGotKeyboardFocus += (_, e) => _screenFocus = e.NewFocus is { } focus ? new WeakReference<IInputElement>(focus) : null;
            DialogService.PropertyChanged += OnDialogChanged;
            NavigationService.ScreenChanging += OnScreenChanging;
            NavigationService.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NavigationService.CurrentScreen)) Status.Refresh();
            };
            NavigationService.PropertyChanged += OnFirstScreen;

            // A dialog that closed stays in the host while it fades out, and goes once the host is gone
            DialogHost.IsVisibleChanged += (_, e) =>
            {
                if (!(bool)e.NewValue && DialogService.CurrentDialog == null) DialogContent.Content = null;
            };

            // What a dialog has room for: the host less the card's margin and outline
            DialogHost.SizeChanged += (_, e) =>
            {
                var (margin, outline) = (DialogFrame.Margin, DialogCard.BorderThickness);
                DialogRoom.SetWidth(DialogContent, Math.Max(0, e.NewSize.Width - margin.Left - margin.Right - outline.Left - outline.Right));
                DialogRoom.SetHeight(DialogContent, Math.Max(0, e.NewSize.Height - margin.Top - margin.Bottom - outline.Top - outline.Bottom));
            };

            // Assetto Corsa on and off: the screen fades to the loader for it and back (the events may come from any thread)
            app.Launcher.ExecutionStarted += (_, _) => Dispatcher.BeginInvoke(() => ShowRacing(true));
            app.Launcher.ExecutionEnded += (_, _) => Dispatcher.BeginInvoke(() => ShowRacing(false));
            app.Content.LoadingChanged += loading => Dispatcher.BeginInvoke(() => ShowContentLoading(loading));

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

            var status = new DispatcherTimer(StatusInterval, DispatcherPriority.Background, (_, _) => Status.Refresh(), Dispatcher);
            status.Start();
        }

        /// <summary>
        /// The first screen is up: the title goes unless that screen took it over as it loaded (the main screen does,
        /// and lets it go when its showroom is ready)
        /// </summary>
        private void OnFirstScreen(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(NavigationService.CurrentScreen) || NavigationService.CurrentScreen == null) return;

            NavigationService.PropertyChanged -= OnFirstScreen;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                if (!Intro.IsClaimed) Intro.FadeOut();
            });
        }

        /// <summary>
        /// The window's End Day, on a screen the flow lets the day be ended from: asked as the garage asks, then home for
        /// the night, where the day ends (the screen on show was built from the day that is ending)
        /// </summary>
        private void OnEndDayClick(object sender, RoutedEventArgs e)
        {
            var app = (App)System.Windows.Application.Current;
            if (app.CurrentGameState is not { } game || !NavigationService.CanEndDayHere || app.Launcher.IsExecutionLocked) return;

            var screen = NavigationService.CurrentScreen;
            Dialogs.Confirmation.EndDayQuestion.Ask(DialogService, app.GameTimeService, game, () =>
            {
                // Still on the screen it was asked on, and still the same game
                if (!ReferenceEquals(NavigationService.CurrentScreen, screen) || !ReferenceEquals(app.CurrentGameState, game)) return;
                NavigationService.GoHome(game, new Navigation.FlowArgs { EndDay = true });
            });
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
            ScreenHost.IsEnabled = !dialog && !radio && !_racingShown && !_contentShown;
            // The window's End Day button is the screen's, outside its host: it takes no click or key while the screen does not
            EndDayButton.IsEnabled = ScreenHost.IsEnabled;
        }

        /// <summary>The keyboard back to what had it on the screen, once nothing is in front of it any more</summary>
        private void RestoreScreenFocus()
        {
            if (_screenFocus?.TryGetTarget(out var focus) == true && focus is UIElement { IsVisible: true, IsEnabled: true } element
                && ScreenHost.IsAncestorOf(element))
                Keyboard.Focus(element);
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

        /// <summary>What the element shows now, at the screen's own resolution up to <see cref="StillMaxWidth"/></summary>
        private static BitmapSource TakeStill(FrameworkElement element)
        {
            var dpi = VisualTreeHelper.GetDpi(element);
            var scale = Math.Min(1, StillMaxWidth / Math.Max(1, element.ActualWidth * dpi.DpiScaleX));
            var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX * scale));
            var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY * scale));

            var still = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX * scale, dpi.PixelsPerInchY * scale, PixelFormats.Pbgra32);
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

            // The loader only takes the mouse: the keys would still press the screen's buttons behind it
            _racingShown = racing;
            UpdateHosts();
            if (!racing) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RestoreScreenFocus);
        }

        /// <summary>A game's content is being read (New Game, Load Game): the screen fades to a loader until it is ready</summary>
        private void ShowContentLoading(bool loading)
        {
            Reveal.SetIsShown(ContentLayer, loading);
            _contentShown = loading;
            UpdateHosts();
            if (!loading) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RestoreScreenFocus);
        }

        private void OnRadioPanelChanged()
        {
            UpdateHosts();
            var open = RadioPanelViewModel.Shared.IsOpen;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (open) RadioHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                else RestoreScreenFocus();
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

        /// <summary>
        /// The song's name for a few seconds, coming out of the radio's icon to its left and going back into it; on a screen
        /// without the radio, out of the money card, or the top right corner. Not while the panel shows it.
        /// </summary>
        private void ShowNowPlaying(RadioSong song)
        {
            if (RadioPanelViewModel.Shared.IsOpen) return;

            RadioToastText.Text = song.Display;

            // The anchor's left edge and middle, in the window (the middle of a corner toast once its height is known)
            var anchor = RadioToastAnchor.Current ?? (StatusCard.IsVisible ? StatusCard : null);
            var anchored = anchor != null && anchor.IsDescendantOf(this);
            System.Windows.Point edge;
            if (anchored)
            {
                var bounds = anchor!.TransformToAncestor(this).TransformBounds(new Rect(anchor.RenderSize));
                edge = new System.Windows.Point(bounds.Left, bounds.Top + bounds.Height / 2);
            }
            else
            {
                edge = new System.Windows.Point(ActualWidth - ToastMargin + ToastGap, ToastMargin);
            }

            // No wider than the room left of the anchor: a long name is cut short rather than off the window
            RadioToast.MaxWidth = Math.Max(0, edge.X - ToastGap - ToastMargin);
            RadioToast.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = RadioToast.DesiredSize;
            if (!anchored) edge.Y += size.Height / 2;

            System.Windows.Controls.Canvas.SetLeft(RadioToast, edge.X - ToastGap - size.Width);
            System.Windows.Controls.Canvas.SetTop(RadioToast, edge.Y - size.Height / 2);

            var total = ToastIn + ToastHold + ToastOut;
            RadioToast.BeginAnimation(OpacityProperty, Keys(total, 0, 1, 1, 0));
            RadioToastShift.BeginAnimation(TranslateTransform.XProperty, Keys(total, ToastTravel, 0, 0, ToastTravel));
            RadioToastScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(total, 0.4, 1, 1, 0.4));

            // From the anchor, held, back to it: eased out of it and into it
            DoubleAnimationUsingKeyFrames Keys(TimeSpan duration, double from, double shown, double held, double to)
            {
                var keys = new DoubleAnimationUsingKeyFrames { Duration = duration };
                keys.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                keys.KeyFrames.Add(new EasingDoubleKeyFrame(shown, KeyTime.FromTimeSpan(ToastIn), new CubicEase { EasingMode = EasingMode.EaseOut }));
                keys.KeyFrames.Add(new LinearDoubleKeyFrame(held, KeyTime.FromTimeSpan(ToastIn + ToastHold)));
                keys.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), new CubicEase { EasingMode = EasingMode.EaseIn }));
                return keys;
            }
        }

        /// <summary>
        /// The keyboard goes into a dialog when it opens (the screen behind is disabled, and would otherwise keep it
        /// nowhere), and back to what had it on the screen once the last dialog closes. After the layout, so the
        /// dialog's buttons are there to take it.
        /// </summary>
        private void OnDialogChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DialogService.CurrentDialog)) return;

            // A dialog closing is often money paid (a purchase, a sale): the card says so at once
            Status.Refresh();

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
                else
                {
                    RestoreScreenFocus();
                }
            });
        }
    }
}
