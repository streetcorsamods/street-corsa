using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Animation;

namespace StreetCorsa.Screens.Station
{
    /// <summary>
    /// The gas station from the driver's seat; the attendant's words scroll to the latest. A black curtain lifts as the
    /// car rolls in and comes down when it has gone, before the garage.
    /// </summary>
    public partial class GasStationScreenView : System.Windows.Controls.UserControl
    {
        private static readonly TimeSpan CurtainUp = TimeSpan.FromSeconds(1.2);
        private static readonly TimeSpan CurtainDown = TimeSpan.FromSeconds(0.9);

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
            if (_viewModel != null)
            {
                _viewModel.AttendantLines.CollectionChanged -= OnLine;
                _viewModel.OpenCurtain = null;
                _viewModel.CloseCurtain = null;
            }

            _viewModel = viewModel;
            if (_viewModel == null) return;
            _viewModel.AttendantLines.CollectionChanged += OnLine;
            _viewModel.OpenCurtain = () =>
            {
                Curtain.BeginAnimation(OpacityProperty, new DoubleAnimation(0, CurtainUp));

                // The car rolls in with the player in it: the radio is the car's from here
                Audio.Radio.RadioPlayer.Shared.SetInCar(true);
            };
            _viewModel.CloseCurtain = CloseCurtainAsync;
        }

        private Task CloseCurtainAsync()
        {
            var done = new TaskCompletionSource();
            var animation = new DoubleAnimation(1, CurtainDown);
            animation.Completed += (_, _) => done.TrySetResult();
            Curtain.BeginAnimation(OpacityProperty, animation);

            // Not shown (the window gone): no animation will finish
            if (!IsLoaded) done.TrySetResult();
            return done.Task;
        }

        private void OnLine(object? sender, NotifyCollectionChangedEventArgs e) =>
            Dispatcher.BeginInvoke(() => LinesScroller.ScrollToEnd());
    }
}
