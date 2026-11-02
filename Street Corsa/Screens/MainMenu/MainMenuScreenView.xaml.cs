using System.ComponentModel;
using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace StreetCorsa.Screens.MainMenu
{
    /// <summary>
    /// Plays the main screen's changes: the menu and the cards take turns in the same place, one fading out before the
    /// other drops in. The screen starts black and the showroom fades up in it; the old picture only comes up when
    /// there is no showroom to show. Esc closes the open card. When the game opens, its title covers all of it for at
    /// least <see cref="IntroLeast"/> and until the first room fades up behind it, so the menu never stands on black.
    /// </summary>
    public partial class MainMenuScreenView : UserControl
    {
        private static readonly Duration OutDuration = new(TimeSpan.FromMilliseconds(180));
        private static readonly Duration InDuration = new(TimeSpan.FromMilliseconds(380));
        private static readonly Duration FallbackFade = new(TimeSpan.FromMilliseconds(900));
        // How far a panel travels as it comes and goes: it drops in from above and lifts away
        private const double Travel = 24;

        private static readonly TimeSpan IntroLeast = TimeSpan.FromSeconds(4);
        // A showroom slower than this does not keep the player waiting: the menu comes up and the cars fade in later
        private static readonly TimeSpan IntroMost = TimeSpan.FromSeconds(15);

        private DispatcherTimer? _introTimer;
        private bool _introStarted;
        private bool _introOver;
        private bool _introLeastPassed;
        private bool _showroomUp;
        private IntroView? _intro;

        private MainMenuScreenViewModel? _viewModel;
        private Window? _window;

        /// <summary>Counts switches, so a slow fade-out does not finish a switch that a later one replaced</summary>
        private int _switch;

        /// <summary>The corner buttons (Exit, the CSP warning) are up: hidden until the menu first shows, so they never show through the title</summary>
        private bool _cornersUp;

        public MainMenuScreenView()
        {
            InitializeComponent();

            DataContextChanged += (_, e) =>
            {
                if (IsLoaded) Attach(e.NewValue as MainMenuScreenViewModel);
            };
            Loaded += (_, _) =>
            {
                // Esc from the window: once a menu badge is clicked it is hidden, and the focus leaves this screen
                _window = Window.GetWindow(this);
                if (_window != null)
                {
                    _window.PreviewKeyDown -= OnWindowKeyDown;
                    _window.PreviewKeyDown += OnWindowKeyDown;
                }

                Attach(DataContext as MainMenuScreenViewModel);
                if (_viewModel?.ShowIntro == true && !_introStarted) StartIntro();
                else if (!_introStarted || _introOver) ShowPanel(_viewModel?.CurrentCard);
            };
            Unloaded += (_, _) =>
            {
                Attach(null);
                if (_window != null) _window.PreviewKeyDown -= OnWindowKeyDown;
                _window = null;

                // Left before the title went: it does not stay over the next screen, and the view loaded again shows its menu
                StopIntro();
            };

            // No showroom to show at all: the old picture rather than a black screen
            Showcase.Failed += (_, _) =>
            {
                ShowFallback();
                ShowroomUp();
            };
            Showcase.SceneShown += (_, _) => ShowroomUp();
        }

        private void Attach(MainMenuScreenViewModel? viewModel)
        {
            if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = viewModel;
            if (_viewModel == null) return;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Found to have nothing to show before this view was attached
            if (_viewModel.NothingToShow)
            {
                ShowFallback();
                ShowroomUp();
            }
        }

        #region Intro

        private void StartIntro()
        {
            _introStarted = true;

            // The window's title, up since the game started: it stays until the showroom is ready
            _intro = (_window as MainWindow)?.Intro;
            if (_intro == null || !_intro.Claim())
            {
                _intro = null;
                EndIntro();
                return;
            }

            _intro.MouseDown += Intro_MouseDown;

            // The least time counts from when the title came up, not from when this screen did
            var shown = DateTime.UtcNow - _intro.ShownAt;
            var least = IntroLeast - shown;
            if (least <= TimeSpan.Zero)
            {
                // The game took its time starting: the title has been up long enough, and the showroom may be too
                _introLeastPassed = true;
                if (_showroomUp)
                {
                    EndIntro();
                    return;
                }
            }

            _introTimer = new DispatcherTimer { Interval = least > TimeSpan.Zero ? least : IntroMost - IntroLeast };
            _introTimer.Tick += (_, _) =>
            {
                // The least time is up: go if the showroom is, or give it until the most
                if (_showroomUp || _introLeastPassed)
                {
                    EndIntro();
                    return;
                }

                _introLeastPassed = true;
                _introTimer.Interval = IntroMost - IntroLeast;
            };
            _introTimer.Start();
        }

        /// <summary>The first room is fading up, or there is none to wait for</summary>
        private void ShowroomUp()
        {
            _showroomUp = true;

            // Past the least time already, and only waiting for the cars
            if (_introLeastPassed) EndIntro();
        }

        private void Intro_MouseDown(object sender, MouseButtonEventArgs e) => EndIntro();

        private void EndIntro()
        {
            if (StopIntro()) ShowPanel(_viewModel?.CurrentCard);
        }

        /// <summary>The title goes, and lets go of this view (the window keeps it); false when it was not up</summary>
        private bool StopIntro()
        {
            if (!_introStarted || _introOver) return false;

            _introOver = true;
            _introTimer?.Stop();

            if (_intro != null)
            {
                _intro.MouseDown -= Intro_MouseDown;
                _intro.FadeOut();
            }
            return true;
        }

        #endregion

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            // Escape with a dialog in front is the dialog's, not the card's behind it
            if (e.Key != Key.Escape || !IsLoaded || StreetCorsa.Dialogs.ModalState.IsOpen || _viewModel?.CloseCardCommand is not { } close) return;
            if (!close.CanExecute(null)) return;

            close.Execute(null);
            e.Handled = true;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainMenuScreenViewModel.CurrentCard)) SwitchTo(_viewModel?.CurrentCard);
            if (e.PropertyName == nameof(MainMenuScreenViewModel.NothingToShow) && _viewModel?.NothingToShow == true)
            {
                ShowFallback();
                ShowroomUp();
            }
        }

        private void ShowFallback() => Fallback.BeginAnimation(OpacityProperty, new DoubleAnimation(1, FallbackFade));

        private void SwitchTo(object? card)
        {
            var id = ++_switch;
            var outgoing = CardPanel.Visibility == Visibility.Visible ? (FrameworkElement)CardPanel : MenuPanel;

            // No second click lands while the first is still playing out
            outgoing.IsHitTestVisible = false;

            var fade = new DoubleAnimation(0, OutDuration);
            fade.Completed += (_, _) =>
            {
                if (id != _switch) return;
                outgoing.Visibility = Visibility.Collapsed;
                ShowPanel(card);
            };
            outgoing.BeginAnimation(OpacityProperty, fade);
            Translate(outgoing).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-Travel / 2, OutDuration));
        }

        /// <summary>Brings up the card, or the menu when there is none</summary>
        private void ShowPanel(object? card)
        {
            CardHost.Content = card;

            FrameworkElement incoming = card != null ? CardPanel : MenuPanel;
            FrameworkElement other = card != null ? MenuPanel : CardPanel;

            other.BeginAnimation(OpacityProperty, null);
            other.Opacity = 0;
            other.Visibility = Visibility.Collapsed;

            incoming.Visibility = Visibility.Visible;
            incoming.IsHitTestVisible = true;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, InDuration) { EasingFunction = ease });

            // The corner buttons come up with the menu the first time, after the title (they stay up from then on)
            if (!_cornersUp)
            {
                _cornersUp = true;
                foreach (var corner in new UIElement[] { ExitButton, CornerPanel })
                    corner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, InDuration) { EasingFunction = ease });
            }
            Translate(incoming).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-Travel, 0, InDuration) { EasingFunction = ease });
        }

        private static TranslateTransform Translate(UIElement element)
        {
            if (element.RenderTransform is TranslateTransform { IsFrozen: false } translate) return translate;

            translate = new TranslateTransform();
            element.RenderTransform = translate;
            return translate;
        }
    }
}
