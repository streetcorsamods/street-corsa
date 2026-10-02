using StreetCorsa.Audio;
using StreetCorsa.Controls;
using StreetCorsa.Services.Scripting;

namespace StreetCorsa.Screens.CarLot
{
    public partial class CarLotScreenView : System.Windows.Controls.UserControl
    {
        private static readonly TimeSpan CurtainUp = TimeSpan.FromSeconds(0.25);

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

            // The screen is going: silence. The bank stays loaded: the garage plays the same car next, and a race lets
            // go of it itself before it moves it.
            Unloaded += (_, _) => _engine.Detach();
        }

        /// <summary>
        /// The car picked starts up with <paramref name="spec"/> and is taken out as its script has it (it drives out of
        /// its bay, and the screen goes black over it). True when it drove off.
        /// </summary>
        private async Task<bool> DriveCarOffAsync(EngineSpec? spec)
        {
            if (spec is not { Runs: true }) return false;

            await _engine.SetEngineAsync(spec);
            var facts = (DataContext as CarLotScreenViewModel)?.ScriptFacts ?? ScriptFacts.None;
            if (Viewport.TakeOut(_engine, facts, CurtainAsync) is not { } run) return false;

            await run.Released;
            if (!run.Drove) return false;

            // Gone: its engine goes with it, before the garage takes up the same sound
            _engine.Detach();
            return true;
        }

        /// <summary>The lot stays after all: the curtain up, the car back in its bay and quiet</summary>
        private void PutCarBack()
        {
            _ = CurtainFade.ToAsync(Curtain, 0, CurtainUp);
            Viewport.StayPut();
            _engine.StopNow();
            _engine.Fade = 1f;
        }

        private Task CurtainAsync(float opacity, TimeSpan over, FadeEase ease) => CurtainFade.ToAsync(Curtain, opacity, over, CurtainFade.Easing(ease));
    }
}
