using System.Windows;

namespace StreetCorsa.Dialogs.PartPreview
{
    public partial class PartPreviewDialogView : System.Windows.Controls.UserControl
    {
        public PartPreviewDialogView()
        {
            InitializeComponent();

            // No picture after all: the part has no model that reads, or there is no 3D on this machine
            Viewport.Failed += (_, _) => ShowNoPicture();
            DataContextChanged += (_, _) => Update();
            Viewport.IsVisibleChanged += (_, _) => Update();
            DependencyPropertyDescriptorFor(Controls.PartViewport3D.HasNothingToShowProperty).AddValueChanged(Viewport, (_, _) => Update());
        }

        private static System.ComponentModel.DependencyPropertyDescriptor DependencyPropertyDescriptorFor(DependencyProperty property) =>
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(property, typeof(Controls.PartViewport3D));

        private void Update()
        {
            var noPicture = DataContext is PartPreviewDialogViewModel { HasPicture: false } || Viewport.HasNothingToShow || Viewport.HasFailed;
            if (noPicture) ShowNoPicture();
        }

        private void ShowNoPicture()
        {
            NoPicture.Visibility = Visibility.Visible;
            Loader.IsLoading = false;
        }
    }
}
