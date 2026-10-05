using StreetCorsa.Screens.Shared;

namespace StreetCorsa.Screens.PaintBooth
{
    /// <summary>
    /// The paint booth: a black curtain lifts as the car rolls in and comes down as the player goes home
    /// (<see cref="DriveInCurtain"/>)
    /// </summary>
    public partial class PaintBoothScreenView : System.Windows.Controls.UserControl
    {
        private PaintBoothScreenViewModel? _viewModel;

        public PaintBoothScreenView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Listen(DataContext as PaintBoothScreenViewModel);
            Loaded += (_, _) => Listen(DataContext as PaintBoothScreenViewModel);
            Unloaded += (_, _) => Listen(null);
        }

        private void Listen(PaintBoothScreenViewModel? viewModel)
        {
            DriveInCurtain.Hang(Curtain, _viewModel, viewModel);
            _viewModel = viewModel;
        }
    }
}
