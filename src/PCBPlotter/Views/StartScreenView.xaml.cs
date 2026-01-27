using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PCBPlotter.Views
{
    public partial class StartScreenView : UserControl
    {
        public StartScreenView()
        {
            InitializeComponent();
        }

        private void RecentProject_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as ViewModels.MainViewModel;
            if (viewModel?.OpenRecentProjectCommand?.CanExecute(null) == true)
            {
                viewModel.OpenRecentProjectCommand.Execute(null);
            }
        }
    }
}
