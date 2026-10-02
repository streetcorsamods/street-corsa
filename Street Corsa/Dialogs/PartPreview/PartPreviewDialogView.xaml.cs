using System.ComponentModel;
using System.Windows;

namespace StreetCorsa.Dialogs.PartPreview
{
    public partial class PartPreviewDialogView : System.Windows.Controls.UserControl
    {
        // The descriptor keeps whatever listens to it for good: listened to only while the view is up
        private static readonly DependencyPropertyDescriptor NothingToShow =
            DependencyPropertyDescriptor.FromProperty(Controls.PartViewport3D.HasNothingToShowProperty, typeof(Controls.PartViewport3D));

        public PartPreviewDialogView()
        {
            InitializeComponent();

            // No picture after all: the part has no model that reads, or there is no 3D on this machine
            Viewport.Failed += (_, _) => ShowNoPicture();
            Loaded += (_, _) =>
            {
                NothingToShow.AddValueChanged(Viewport, OnNothingToShow);
                Update();
            };
            Unloaded += (_, _) => NothingToShow.RemoveValueChanged(Viewport, OnNothingToShow);
        }

        private void OnNothingToShow(object? sender, EventArgs e) => Update();

        private void Update()
        {
            if (Viewport.HasNothingToShow || Viewport.HasFailed) ShowNoPicture();
        }

        private void ShowNoPicture()
        {
            NoPicture.Visibility = Visibility.Visible;
            Hint.Visibility = Visibility.Collapsed;
            Viewport.Visibility = Visibility.Collapsed;
            Loader.IsLoading = false;
        }
    }
}
