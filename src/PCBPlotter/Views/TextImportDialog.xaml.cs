using System.Windows;

namespace PCBPlotter.Views
{
    public partial class TextImportDialog : Window
    {
        public TextImportDialog()
        {
            InitializeComponent();
        }

        public TextImportDialog(string filePath) : this()
        {
            var vm = DataContext as ViewModels.TextImportViewModel;
            if (vm != null)
            {
                vm.FilePath = filePath;
            }
        }
    }
}
