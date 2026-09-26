using System.Windows.Input;

namespace StreetCorsa.Screens.Init
{
    public partial class InitScreenView : System.Windows.Controls.UserControl
    {
        public InitScreenView()
        {
            InitializeComponent();
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as InitScreenViewModel;
            viewModel?.ProceedCommand.Execute(null);
        }
    }
}
