using System.Collections.Specialized;
using StreetCorsa.Screens.Shared;

namespace StreetCorsa.Screens.Station
{
    /// <summary>
    /// The gas station from the driver's seat; the attendant's words scroll to the latest. A black curtain lifts as the
    /// car rolls in and comes down when it has gone, before the garage (<see cref="DriveInCurtain"/>).
    /// </summary>
    public partial class GasStationScreenView : System.Windows.Controls.UserControl
    {
        private GasStationScreenViewModel? _viewModel;

        public GasStationScreenView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Listen(DataContext as GasStationScreenViewModel);
            Loaded += (_, _) => Listen(DataContext as GasStationScreenViewModel);
            Unloaded += (_, _) => Listen(null);
        }

        private void Listen(GasStationScreenViewModel? viewModel)
        {
            if (_viewModel != null) _viewModel.AttendantLines.CollectionChanged -= OnLine;
            DriveInCurtain.Hang(Curtain, _viewModel, viewModel);

            _viewModel = viewModel;
            if (_viewModel != null) _viewModel.AttendantLines.CollectionChanged += OnLine;
        }

        private void OnLine(object? sender, NotifyCollectionChangedEventArgs e) =>
            Dispatcher.BeginInvoke(() => LinesScroller.ScrollToEnd());
    }
}
