using System.Windows;
using System.Windows.Media.Animation;

namespace StreetCorsa.Screens.Diner
{
    /// <summary>
    /// The diner's street from the driver's seat, the diner's window over it. A black curtain lifts as the car comes down
    /// the street (or stands in its bay, back from a race) and comes down once it has driven off.
    /// </summary>
    public partial class DinerScreenView : System.Windows.Controls.UserControl
    {
        private static readonly TimeSpan CurtainUp = TimeSpan.FromSeconds(1.2);
        private static readonly TimeSpan CurtainDown = TimeSpan.FromSeconds(0.9);

        private DinerScreenViewModel? _viewModel;

        public DinerScreenView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Listen(DataContext as DinerScreenViewModel);
            Loaded += (_, _) => Listen(DataContext as DinerScreenViewModel);
            Unloaded += (_, _) => Listen(null);
        }

        private void Listen(DinerScreenViewModel? viewModel)
        {
            if (_viewModel != null)
            {
                _viewModel.OpenCurtain = null;
                _viewModel.CloseCurtain = null;
            }

            _viewModel = viewModel;
            if (_viewModel == null) return;
            _viewModel.OpenCurtain = () =>
            {
                Curtain.BeginAnimation(OpacityProperty, new DoubleAnimation(0, CurtainUp));

                // The car comes down the street with the player in it: the radio is the car's from here
                Audio.Radio.RadioPlayer.Shared.SetInCar(true);
            };
            _viewModel.CloseCurtain = () => Controls.CurtainFade.ToAsync(Curtain, 1, CurtainDown);
        }
    }
}
