using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StreetCorsa.Controls
{
    /// <summary>
    /// The loader every screen shows while its scene comes up: a turning ring over what is being waited for. Going from
    /// loading to loaded fades it out (and collapses it, so it stops turning); back to loading fades it in again.
    /// </summary>
    public partial class LoadingIndicator : System.Windows.Controls.UserControl
    {
        public static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(300);

        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            nameof(Message), typeof(string), typeof(LoadingIndicator),
            new PropertyMetadata(string.Empty, (d, e) => ((LoadingIndicator)d).MessageText.Text = e.NewValue as string ?? string.Empty));

        public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
            nameof(IsLoading), typeof(bool), typeof(LoadingIndicator),
            new PropertyMetadata(true, (d, e) => ((LoadingIndicator)d).OnIsLoadingChanged((bool)e.NewValue)));

        /// <summary>What is being waited for: "Opening the garage..."</summary>
        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        /// <summary>True (the default) while there is something to wait for</summary>
        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        public static readonly DependencyProperty WaitForProperty = DependencyProperty.Register(
            nameof(WaitFor), typeof(D3DViewportBase), typeof(LoadingIndicator),
            new PropertyMetadata(null, (d, e) => ((LoadingIndicator)d).OnWaitForChanged((D3DViewportBase?)e.NewValue, (D3DViewportBase?)e.OldValue)));

        /// <summary>A 3D view to wait for: loading until its first frame is up, or it has failed</summary>
        public D3DViewportBase? WaitFor
        {
            get => (D3DViewportBase?)GetValue(WaitForProperty);
            set => SetValue(WaitForProperty, value);
        }

        private void OnWaitForChanged(D3DViewportBase? viewport, D3DViewportBase? old)
        {
            if (old != null)
            {
                old.Ready -= OnViewportSettled;
                old.Failed -= OnViewportSettled;
                old.NothingToShow -= OnViewportSettled;
            }

            if (viewport == null) return;
            viewport.Ready += OnViewportSettled;
            viewport.Failed += OnViewportSettled;
            viewport.NothingToShow += OnViewportSettled;
            IsLoading = !viewport.IsReady && !viewport.HasFailed;
        }

        private void OnViewportSettled(object? sender, EventArgs e) => IsLoading = false;

        public LoadingIndicator()
        {
            InitializeComponent();

            // The ring turns only while it is seen: a running clock keeps WPF drawing every frame, hidden or not
            IsVisibleChanged += (_, _) => Spin(IsVisible);
        }

        private void Spin(bool spin) => SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, spin
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);

        private void OnIsLoadingChanged(bool loading)
        {
            // Before the screen is up there is nothing to fade: it simply starts as it is
            if (!IsLoaded)
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = loading ? 1 : 0;
                Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            var fade = new DoubleAnimation(Opacity, loading ? 1 : 0, FadeDuration);
            if (loading)
            {
                Visibility = Visibility.Visible;
            }
            else
            {
                fade.Completed += (_, _) =>
                {
                    if (!IsLoading) Visibility = Visibility.Collapsed;
                };
            }

            BeginAnimation(OpacityProperty, fade);
        }
    }
}
