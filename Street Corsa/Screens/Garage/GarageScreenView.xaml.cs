using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>
    /// Interaction logic for GarageScreenView.xaml
    /// </summary>
    public partial class GarageScreenView : System.Windows.Controls.UserControl
    {
        private static readonly TimeSpan FadeInDuration = TimeSpan.FromSeconds(0.6);
        private static readonly TimeSpan QuickFadeInDuration = TimeSpan.FromSeconds(0.25);

        // Let the renderer draw a few frames (reflections, shadows) before showing it
        private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(200);

        // Never leave the player stuck on the loading layer if the renderer reports nothing
        private static readonly TimeSpan RevealTimeout = TimeSpan.FromSeconds(15);

        private DispatcherTimer? _revealTimer;
        private bool _revealed;

        public GarageScreenView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            Viewport3D.Ready += (_, _) => RevealAfter(SettleDelay);
            Viewport3D.Failed += (_, _) => RevealAfter(TimeSpan.Zero);

            // Parts are picked in the 3D view; what a pick means is the workbench's business
            Viewport3D.PartClicked += part => (DataContext as GarageScreenViewModel)?.Workbench.OnPartClicked(part);
            Viewport3D.StandClicked += ShowStand;
            Viewport3D.EngineOnStand += ShowStand;
            Viewport3D.CarClicked += () =>
            {
                if (DataContext is GarageScreenViewModel { Workbench.IsAvailable: true } viewModel) viewModel.Workbench.IsCarView = true;
            };
            Viewport3D.CandidateClicked += candidate => (DataContext as GarageScreenViewModel)?.Workbench.OnCandidateClicked(candidate);
            Viewport3D.PlacementChanged += text =>
            {
                if (DataContext is GarageScreenViewModel viewModel) viewModel.Workbench.PlacementDisplay = text ?? string.Empty;
            };

            // The car drives off to the lot: the view plays it, the view model says when
            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is GarageScreenViewModel old)
                {
                    old.DriveCarOff = null;
                    old.PutCarBack = null;
                }

                if (e.NewValue is GarageScreenViewModel viewModel)
                {
                    viewModel.DriveCarOff = DriveCarOffAsync;
                    viewModel.PutCarBack = PutCarBack;
                }
            };

            // Placement mode is driven from the keyboard, wherever the focus is on the screen
            Unloaded += (_, _) => { if (_window != null) _window.PreviewKeyDown -= OnWindowKeyDown; };
        }

        private Window? _window;

        private void OnUnloaded(object sender, RoutedEventArgs e) => _revealTimer?.Stop();

        /// <summary>The work goes over to the engine stand, when there is a workbench to work at</summary>
        private void ShowStand()
        {
            if (DataContext is GarageScreenViewModel { Workbench.IsAvailable: true } viewModel) viewModel.Workbench.IsStandView = true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _window = Window.GetWindow(this);
            if (_window != null)
            {
                _window.PreviewKeyDown -= OnWindowKeyDown;
                _window.PreviewKeyDown += OnWindowKeyDown;
            }

            // Re-loaded with the garage already rendered (or failed): nothing to wait for
            if (Viewport3D.IsReady || Viewport3D.HasFailed)
            {
                RevealAfter(TimeSpan.Zero);
            }
            else
            {
                RevealAfter(RevealTimeout);
            }
        }

        /// <summary>
        /// Placement mode (a tool for fitting the converted parts, not gameplay): F5 on a selected part turns it on,
        /// then the arrows move the part across and fore-aft, PgUp/PgDn up and down, in the engine's axes; Ctrl
        /// steps a millimetre, Shift two centimetres, plain five; Tab moves the pad it sits on instead; R takes the
        /// slot back to where the packs put it; F5 or Esc ends it. Every step is written to the slot shifts.
        /// </summary>
        private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // The keys of a dialog in front are the dialog's
            if (!IsLoaded || StreetCorsa.Dialogs.ModalState.IsOpen) return;

            // An engine on its way between the car and the stand: no placement until it is down (the screen itself is
            // disabled meanwhile)
            if (Viewport3D.IsMovingEngine) return;

            if (e.Key == System.Windows.Input.Key.F5)
            {
                Viewport3D.TogglePlacement();
                e.Handled = true;
                return;
            }

            if (!Viewport3D.IsPlacing) return;

            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            var step = modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control) ? 0.001f
                : modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift) ? 0.02f
                : 0.005f;

            switch (e.Key)
            {
                case System.Windows.Input.Key.Left: Viewport3D.NudgePlacement(-step, 0, 0); break;
                case System.Windows.Input.Key.Right: Viewport3D.NudgePlacement(step, 0, 0); break;
                case System.Windows.Input.Key.Up: Viewport3D.NudgePlacement(0, 0, step); break;
                case System.Windows.Input.Key.Down: Viewport3D.NudgePlacement(0, 0, -step); break;
                case System.Windows.Input.Key.PageUp: Viewport3D.NudgePlacement(0, step, 0); break;
                case System.Windows.Input.Key.PageDown: Viewport3D.NudgePlacement(0, -step, 0); break;
                case System.Windows.Input.Key.Tab: Viewport3D.TogglePlacementTarget(); break;
                case System.Windows.Input.Key.R: Viewport3D.ResetPlacement(); break;
                case System.Windows.Input.Key.Escape: Viewport3D.TogglePlacement(); break;
                default: return;
            }

            e.Handled = true;
        }

        /// <summary>
        /// The car in the garage starts up and drives off, and the screen goes black over it; nothing on the screen can be
        /// used meanwhile. A car that cannot (no engine that runs, its parts out) stays, and nothing is shown.
        /// </summary>
        private async Task DriveCarOffAsync()
        {
            // Only from the car's own view, where its engine is on show
            if (!EnginePanel.IsVisible) return;

            ContentLayer.IsHitTestVisible = false;
            await StreetCorsa.Controls.DriveOff.PlayAsync(Viewport3D, EnginePanel.Runner, CurtainDownAsync);
        }

        /// <summary>The garage stays after all: the curtain up, the car back where it stood</summary>
        private void PutCarBack()
        {
            Curtain.BeginAnimation(OpacityProperty, new DoubleAnimation(0, QuickFadeInDuration));
            Viewport3D.StayPut();
            StreetCorsa.Controls.DriveOff.Loud(EnginePanel.Runner);
            ContentLayer.IsHitTestVisible = _revealed;
        }

        private Task CurtainDownAsync(TimeSpan duration)
        {
            var done = new TaskCompletionSource();
            var animation = new DoubleAnimation(1, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            animation.Completed += (_, _) => done.TrySetResult();
            Curtain.BeginAnimation(OpacityProperty, animation);

            // Not shown (the window gone): no animation will finish
            if (!IsLoaded) done.TrySetResult();
            return done.Task;
        }

        /// <summary>
        /// Schedules the reveal; a shorter delay replaces a pending longer one (timeout -> ready)
        /// </summary>
        private void RevealAfter(TimeSpan delay)
        {
            if (_revealed) return;

            _revealTimer?.Stop();

            if (delay <= TimeSpan.Zero)
            {
                Reveal();
                return;
            }

            _revealTimer = new DispatcherTimer { Interval = delay };
            _revealTimer.Tick += (_, _) => Reveal();
            _revealTimer.Start();
        }

        /// <summary>
        /// Swaps the loading layer for the finished screen: garage, car and overlay fade in together
        /// </summary>
        private void Reveal()
        {
            _revealTimer?.Stop();
            if (_revealed) return;
            _revealed = true;

            var skipAnimation = (DataContext as GarageScreenViewModel)?.SkipEnterAnimation == true;
            var duration = skipAnimation ? QuickFadeInDuration : FadeInDuration;

            // Invisible buttons must not be clickable while loading
            ContentLayer.IsHitTestVisible = true;

            ContentLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            LoadingLayer.IsLoading = false;
        }
    }
}
