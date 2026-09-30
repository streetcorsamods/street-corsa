using System.Windows;
using System.Windows.Media.Animation;
using StreetCorsa.Audio;
using StreetCorsa.Controls;
using StreetCorsa.Logging;

namespace StreetCorsa.Screens.CarLot
{
    public partial class CarLotScreenView : System.Windows.Controls.UserControl
    {
        private static readonly TimeSpan CurtainUp = TimeSpan.FromSeconds(0.25);

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarLot");

        // The engine of the car taken, heard as it drives off; the warehouse's car rocks on it
        private readonly EngineRunner _engine = new();

        public CarLotScreenView()
        {
            InitializeComponent();
            Viewport.RunningEngine = _engine;

            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is CarLotScreenViewModel old)
                {
                    old.DriveCarOff = null;
                    old.PutCarBack = null;
                }

                if (e.NewValue is CarLotScreenViewModel viewModel)
                {
                    viewModel.DriveCarOff = DriveCarOffAsync;
                    viewModel.PutCarBack = PutCarBack;
                }
            };

            Unloaded += (_, _) =>
            {
                // The screen is going: silence, and let go of the bank so a race can move it
                _engine.Detach();
                _ = LetGoOfSoundAsync();
            };
        }

        /// <summary>The car picked starts up with <paramref name="spec"/> and drives out of its bay; the screen goes black over it</summary>
        private async Task DriveCarOffAsync(EngineSpec? spec)
        {
            if (spec is not { Runs: true }) return;

            await _engine.SetEngineAsync(spec);
            await DriveOff.PlayAsync(Viewport, _engine, CurtainDownAsync);
        }

        /// <summary>The lot stays after all: the curtain up, the car back in its bay and quiet</summary>
        private void PutCarBack()
        {
            Curtain.BeginAnimation(OpacityProperty, new DoubleAnimation(0, CurtainUp));
            Viewport.StayPut();
            _engine.StopNow();
            DriveOff.Loud(_engine);
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

        private async Task LetGoOfSoundAsync()
        {
            try
            {
                await EngineAudio.Shared.UnloadAllAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Car lot: letting go of the engine sound failed");
            }
        }
    }
}
