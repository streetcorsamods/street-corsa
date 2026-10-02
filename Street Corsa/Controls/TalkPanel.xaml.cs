using System.Collections.Specialized;
using System.Windows;
using StreetCorsa.Screens.Shared;

namespace StreetCorsa.Controls
{
    /// <summary>
    /// A talk (<see cref="TalkViewModel"/>, its DataContext) in a talk window or on a screen: the newest line always in
    /// sight, the box to type in focused when the talk is shown
    /// </summary>
    public partial class TalkPanel : System.Windows.Controls.UserControl
    {
        private TalkViewModel? _talk;

        public TalkPanel()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Follow(DataContext as TalkViewModel);
            Loaded += (_, _) =>
            {
                Follow(DataContext as TalkViewModel);
                if (IsVisible) Input.Focus();
            };
            Unloaded += (_, _) => Follow(null);
        }

        private void Follow(TalkViewModel? talk)
        {
            if (ReferenceEquals(talk, _talk)) return;
            if (_talk != null) _talk.Lines.CollectionChanged -= OnLines;
            _talk = talk;
            if (_talk != null)
            {
                _talk.Lines.CollectionChanged += OnLines;
                Scroller.ScrollToEnd();
            }
        }

        private void OnLines(object? sender, NotifyCollectionChangedEventArgs e) =>
            Dispatcher.BeginInvoke(() => Scroller.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);
    }
}
