using System.Windows;
using System.Windows.Media;

namespace PCBPlotter.Views
{
    /// <summary>
    /// A themed message box that follows the application's dark/light theme
    /// </summary>
    public partial class ThemedMessageBox : Window
    {
        private MessageBoxResult _result = MessageBoxResult.None;

        public ThemedMessageBox()
        {
            InitializeComponent();
        }

        public static MessageBoxResult Show(string message, string title = "Message",
            MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None,
            Window owner = null)
        {
            var msgBox = new ThemedMessageBox();
            msgBox.Title = title;
            msgBox.MessageText.Text = message;
            msgBox.Owner = owner ?? Application.Current.MainWindow;

            // Configure buttons
            msgBox.ConfigureButtons(buttons);

            // Configure icon
            msgBox.ConfigureIcon(icon);

            msgBox.ShowDialog();
            return msgBox._result;
        }

        public static MessageBoxResult Show(Window owner, string message, string title = "Message",
            MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None)
        {
            return Show(message, title, buttons, icon, owner);
        }

        private void ConfigureButtons(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    OkButton.Visibility = Visibility.Visible;
                    OkButton.IsDefault = true;
                    break;

                case MessageBoxButton.OKCancel:
                    OkButton.Visibility = Visibility.Visible;
                    OkButton.IsDefault = true;
                    CancelButton.Visibility = Visibility.Visible;
                    break;

                case MessageBoxButton.YesNo:
                    YesButton.Visibility = Visibility.Visible;
                    YesButton.IsDefault = true;
                    NoButton.Visibility = Visibility.Visible;
                    break;

                case MessageBoxButton.YesNoCancel:
                    YesButton.Visibility = Visibility.Visible;
                    YesButton.IsDefault = true;
                    NoButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void ConfigureIcon(MessageBoxImage icon)
        {
            if (icon == MessageBoxImage.None)
            {
                IconBorder.Visibility = Visibility.Collapsed;
                return;
            }

            IconBorder.Visibility = Visibility.Visible;
            string pathData = "";
            Brush iconColor = Brushes.White;

            switch (icon)
            {
                case MessageBoxImage.Information:
                    // Info circle icon
                    pathData = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M11,16.5H13V10.5H11V16.5M11,8.5H13V6.5H11V8.5Z";
                    iconColor = new SolidColorBrush(Color.FromRgb(52, 152, 219)); // Blue
                    break;

                case MessageBoxImage.Warning:
                case MessageBoxImage.Exclamation:
                    // Warning triangle icon
                    pathData = "M13,14H11V10H13M13,18H11V16H13M1,21H23L12,2L1,21Z";
                    iconColor = new SolidColorBrush(Color.FromRgb(241, 196, 15)); // Yellow
                    break;

                case MessageBoxImage.Error:
                case MessageBoxImage.Stop:
                case MessageBoxImage.Hand:
                    // Error circle icon
                    pathData = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M15.5,7.5L12,11L8.5,7.5L7.5,8.5L11,12L7.5,15.5L8.5,16.5L12,13L15.5,16.5L16.5,15.5L13,12L16.5,8.5L15.5,7.5Z";
                    iconColor = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Red
                    break;

                case MessageBoxImage.Question:
                    // Question circle icon
                    pathData = "M15.07,11.25L14.17,12.17C13.45,12.89 13,13.5 13,15H11V14.5C11,13.39 11.45,12.39 12.17,11.67L13.41,10.41C13.78,10.05 14,9.55 14,9C14,7.89 13.1,7 12,7A2,2 0 0,0 10,9H8A4,4 0 0,1 12,5A4,4 0 0,1 16,9C16,9.88 15.64,10.67 15.07,11.25M13,19H11V17H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12C22,6.47 17.5,2 12,2Z";
                    iconColor = new SolidColorBrush(Color.FromRgb(155, 89, 182)); // Purple
                    break;
            }

            IconPath.Data = Geometry.Parse(pathData);
            IconPath.Fill = iconColor;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.OK;
            DialogResult = true;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Yes;
            DialogResult = true;
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.No;
            DialogResult = false;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Cancel;
            DialogResult = false;
        }
    }
}
