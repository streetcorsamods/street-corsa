using StreetCorsa.Screens.Shared;

namespace StreetCorsa.Screens.Diner
{
    /// <summary>
    /// The diner's street from the driver's seat, the diner's window over it. A black curtain lifts as the car comes down
    /// the street (or stands in its bay, back from a race) and comes down once it has driven off (<see cref="DriveInCurtain"/>).
    /// </summary>
    public partial class DinerScreenView : System.Windows.Controls.UserControl
    {
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
            DriveInCurtain.Hang(Curtain, _viewModel, viewModel);
            _viewModel = viewModel;
        }
    }
}
