using System;
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

        // A car taken out of the lot is driving in: the screen takes no clicks until it is in its place
        private bool _arriving;
        private bool _droveIn;

        public GarageScreenView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            Viewport3D.Ready += (_, _) =>
            {
                // Once the scene has done setting itself up (the camera framing the car included)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, DriveIn);
                RevealAfter(SettleDelay);
                // A garage revealed before its first frame (the timeout) is ready only now
                Dispatcher.BeginInvoke(DispatcherPriority.Background, TellSceneReady);
            };

            // The car comes after the first frame: the flow's enter script plays with it, not before it is there
            System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(StreetCorsa.Controls.CarViewport3D.HasCarProperty, typeof(StreetCorsa.Controls.CarViewport3D))
                .AddValueChanged(Viewport3D, (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, TellSceneReady));
            Viewport3D.Failed += (_, _) => RevealAfter(TimeSpan.Zero);

            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is GarageScreenViewModel old)
                {
                    old.DriveCarOut = null;
                    old.PutCarBack = null;
                    old.PlayScreenScript = null;
                }

                if (e.NewValue is GarageScreenViewModel viewModel)
                {
                    viewModel.DriveCarOut = DriveCarOutAsync;
                    viewModel.PutCarBack = PutCarBack;
                    viewModel.PlayScreenScript = PlayScreenScript;
                }
            };

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

            // Placement mode is driven from the keyboard, wherever the focus is on the screen
            Unloaded += (_, _) => { if (_window != null) _window.PreviewKeyDown -= OnWindowKeyDown; };

            EnginePanel.Runner.PropertyChanged += OnRunnerChanged;
            Unloaded += (_, _) => _keyBackTimer?.Stop();
        }

        // ----- the key -----

        // The buttons under the car and the engine bar trade places this fast, sinking and rising this far
        private static readonly TimeSpan BarSwapDuration = TimeSpan.FromMilliseconds(380);
        private const double BarSwapDrop = 24;

        // An engine that would not catch says so on the bar for this long before the buttons come back
        private static readonly TimeSpan WontStartPause = TimeSpan.FromSeconds(2);

        // The player turned the key: the engine is theirs to run, and the bar shows it. A script that starts the
        // engine (the drive-in, a mod's) never turns the key, so the buttons stay where they are.
        private bool _keyOn;
        private DispatcherTimer? _keyBackTimer;

        /// <summary>
        /// The key: turned on it starts the engine and the engine bar rises in place of the buttons; turned off it
        /// stops the engine and the buttons come back
        /// </summary>
        private void OnKeyClick(object sender, RoutedEventArgs e)
        {
            var runner = EnginePanel.Runner;
            if (_keyOn)
            {
                if (runner.IsRunning) runner.Toggle();
                TurnKey(false);
                return;
            }

            if (!runner.CanToggle)
            {
                KeyButton.IsChecked = false;
                return;
            }

            // Already running (a script left it so): the player simply takes it over
            if (!runner.IsRunning) runner.Toggle();
            TurnKey(true);
        }

        /// <summary>
        /// The engine went off by itself with the key on (it would not catch, the car or its engine changed, the panel
        /// went out of sight): the key goes back and so do the buttons
        /// </summary>
        private void OnRunnerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!_keyOn || e.PropertyName is not (nameof(Audio.EngineRunner.State) or nameof(Audio.EngineRunner.CanToggle))) return;

            var runner = EnginePanel.Runner;
            if (runner.State != Audio.EngineState.Off)
            {
                _keyBackTimer?.Stop();
                return;
            }

            if (!runner.FailedToStart || !runner.CanToggle || !EnginePanel.IsVisible)
            {
                TurnKey(false);
                return;
            }

            // Left a moment to read why it would not start
            if (_keyBackTimer is { IsEnabled: true }) return;
            _keyBackTimer ??= new DispatcherTimer();
            _keyBackTimer.Interval = WontStartPause;
            _keyBackTimer.Tick -= OnKeyBack;
            _keyBackTimer.Tick += OnKeyBack;
            _keyBackTimer.Start();
        }

        private void OnKeyBack(object? sender, EventArgs e)
        {
            _keyBackTimer?.Stop();
            if (_keyOn && EnginePanel.Runner.State == Audio.EngineState.Off) TurnKey(false);
        }

        /// <summary>The key on or off: the engine bar and the buttons trade places</summary>
        private void TurnKey(bool on)
        {
            _keyOn = on;
            _keyBackTimer?.Stop();
            KeyButton.IsChecked = on;
            Swap(EnginePanel, on);
            Swap(CarButtons, !on);
        }

        /// <summary>
        /// Rises and fades in, or fades out sinking. Out of sight it takes no clicks; it is never collapsed, since the
        /// engine panel switches its engine off when it is not on screen.
        /// </summary>
        private static void Swap(FrameworkElement element, bool show)
        {
            var slide = element.RenderTransform as System.Windows.Media.TranslateTransform;
            if (slide == null || slide.IsFrozen) element.RenderTransform = slide = new System.Windows.Media.TranslateTransform(0, slide?.Y ?? 0);

            element.IsHitTestVisible = show;
            // The buttons go out of the keyboard's reach too, once they are gone
            if (show) element.ClearValue(IsEnabledProperty);

            var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
            var fade = new DoubleAnimation(show ? 1 : 0, BarSwapDuration) { EasingFunction = ease };
            if (!show && element is not StreetCorsa.Controls.EnginePanel)
                fade.Completed += (_, _) =>
                {
                    if (!element.IsHitTestVisible) element.IsEnabled = false;
                };

            element.BeginAnimation(OpacityProperty, fade);
            slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(show ? 0 : BarSwapDrop, BarSwapDuration) { EasingFunction = ease });
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
        /// The car just taken out of the lot drives in as the garage comes up (once: the garage is only arrived at once).
        /// Asked for only for a car that drove off out of the lot; one that did not was towed in, and is simply there.
        /// </summary>
        private async void DriveIn()
        {
            if (_droveIn || DataContext is not GarageScreenViewModel { CarArriving: true } viewModel) return;
            _droveIn = true;

            // A garage that took so long it was shown before its first frame has shown the car in its place: it does
            // not jump back to drive in
            if (_revealed) return;

            _arriving = true;
            ContentLayer.IsHitTestVisible = false;
            try
            {
                // The engine is worked out on a worker while the garage comes up: the script starts it the moment it is there
                if (Viewport3D.Arrive(() => EnginePanel.Runner, viewModel.ScriptFacts) is { } run) await run.Ended;
            }
            catch (Exception ex)
            {
                StreetCorsa.Logging.AppLoggerFactory.CreateLogger("Garage").Error(ex, "The car could not drive in");
            }
            finally
            {
                _arriving = false;
                ContentLayer.IsHitTestVisible = _revealed;
                TellSceneReady();
            }
        }

        /// <summary>
        /// The garage is up, its scene rendered with the car in it, and no car is driving in: the flow's enter script may
        /// play (once: the navigation keeps count). A garage with no car never says so: there is nothing to play it with.
        /// </summary>
        private void TellSceneReady()
        {
            if (_revealed && !_arriving && Viewport3D.IsReady && Viewport3D.HasCar && DataContext is GarageScreenViewModel viewModel)
                viewModel.SceneReady();
        }

        /// <summary>
        /// A script of the flow's (the garage's screen.&lt;id&gt;.enter or .leave) with the car on show; done once it lets
        /// the screen go on. The screen takes no clicks meanwhile. Null when none plays.
        /// </summary>
        private Task? PlayScreenScript(string trigger, StreetCorsa.Services.Scripting.ScriptFacts facts)
        {
            var running = EnginePanel.Runner.IsRunning;
            if (Viewport3D.PlayTrigger(trigger, () => EnginePanel.Runner, facts, CurtainAsync) is not { Script: not null } run) return null;

            // A leave that goes nowhere puts the engine back as it was before this script, not before an older one
            if (StreetCorsa.Services.Scripting.ScriptTriggers.IsScreenLeave(trigger)) _runningBeforeLeave = running;
            ContentLayer.IsHitTestVisible = false;
            return AfterScreenScript(run);
        }

        private async Task AfterScreenScript(StreetCorsa.Services.Scripting.ScriptRun run)
        {
            try
            {
                await run.Released;
            }
            finally
            {
                ContentLayer.IsHitTestVisible = _revealed && !_arriving;
            }
        }

        /// <summary>
        /// The car leaves as its script has it (the base game has none: it is simply gone with the screen); done when
        /// the garage may go. True when a script played, and the car may need putting back.
        /// </summary>
        private async Task<bool> DriveCarOutAsync()
        {
            if (DataContext is not GarageScreenViewModel viewModel) return false;
            _runningBeforeLeave = EnginePanel.Runner.IsRunning;
            if (Viewport3D.Leave(() => EnginePanel.Runner, viewModel.ScriptFacts, CurtainAsync) is not { Script: not null } run) return false;

            ContentLayer.IsHitTestVisible = false;
            await run.Released;
            return true;
        }

        // The engine was running before the car set off to leave: put back, it is left running
        private bool _runningBeforeLeave;

        /// <summary>
        /// The garage stays after all: the curtain up, the car back in its place and the screen to be used, its engine
        /// as loud as before and off again if the script started it
        /// </summary>
        private void PutCarBack()
        {
            _ = StreetCorsa.Controls.CurtainFade.ToAsync(Curtain, 0, TimeSpan.FromSeconds(0.25));
            Viewport3D.PutBack();
            EnginePanel.Runner.Fade = 1f;
            if (!_runningBeforeLeave) EnginePanel.Runner.StopNow();
            ContentLayer.IsHitTestVisible = _revealed && !_arriving;
        }

        private Task CurtainAsync(float opacity, TimeSpan over, StreetCorsa.Services.Scripting.FadeEase ease) =>
            StreetCorsa.Controls.CurtainFade.ToAsync(Curtain, opacity, over, StreetCorsa.Controls.CurtainFade.Easing(ease));

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

            // Invisible buttons must not be clickable while loading, nor while a car is driving in
            ContentLayer.IsHitTestVisible = !_arriving;

            ContentLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            LoadingLayer.IsLoading = false;
            TellSceneReady();
        }
    }
}
