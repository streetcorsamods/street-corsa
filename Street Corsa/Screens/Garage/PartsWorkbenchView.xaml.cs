using System.Windows;
using StreetCorsa.Controls;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>
    /// The workbench's cards round the 3D view while the parts are on show: the shelf at the left, the engine sheet and
    /// the picked part at the right, the job being done at the bottom
    /// </summary>
    public partial class PartsWorkbenchView : System.Windows.Controls.UserControl
    {
        public PartsWorkbenchView()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty CoversProperty = DependencyProperty.Register(
            nameof(Covers), typeof(D3DViewportBase), typeof(PartsWorkbenchView),
            new PropertyMetadata(null, (d, e) => ((PartsWorkbenchView)d).OnCoversChanged(e.NewValue as D3DViewportBase)));

        /// <summary>
        /// The 3D view the side cards cover (<see cref="D3DViewportBase.CoversProperty"/>): the view stands the car
        /// between them, as far back as fits it there, while they are shown
        /// </summary>
        public D3DViewportBase? Covers
        {
            get => (D3DViewportBase?)GetValue(CoversProperty);
            set => SetValue(CoversProperty, value);
        }

        private void OnCoversChanged(D3DViewportBase? view)
        {
            D3DViewportBase.SetCovers(Shelf, view);
            D3DViewportBase.SetCovers(EngineSheet, view);
        }
    }
}
