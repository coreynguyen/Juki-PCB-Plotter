using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCBPlotter.Controls
{
    /// <summary>
    /// Control for displaying cursor coordinates
    /// </summary>
    public class CoordinateDisplay : Control
    {
        static CoordinateDisplay()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CoordinateDisplay),
                new FrameworkPropertyMetadata(typeof(CoordinateDisplay)));
        }

        public static readonly DependencyProperty XProperty =
            DependencyProperty.Register("X", typeof(double), typeof(CoordinateDisplay),
                new PropertyMetadata(0.0, OnCoordinateChanged));

        public static readonly DependencyProperty YProperty =
            DependencyProperty.Register("Y", typeof(double), typeof(CoordinateDisplay),
                new PropertyMetadata(0.0, OnCoordinateChanged));

        public static readonly DependencyProperty UnitsProperty =
            DependencyProperty.Register("Units", typeof(string), typeof(CoordinateDisplay),
                new PropertyMetadata("mm", OnCoordinateChanged));

        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.Register("FormattedText", typeof(string), typeof(CoordinateDisplay),
                new PropertyMetadata("X: 0.000 mm  Y: 0.000 mm"));

        public double X
        {
            get { return (double)GetValue(XProperty); }
            set { SetValue(XProperty, value); }
        }

        public double Y
        {
            get { return (double)GetValue(YProperty); }
            set { SetValue(YProperty, value); }
        }

        public string Units
        {
            get { return (string)GetValue(UnitsProperty); }
            set { SetValue(UnitsProperty, value); }
        }

        public string FormattedText
        {
            get { return (string)GetValue(FormattedTextProperty); }
            private set { SetValue(FormattedTextProperty, value); }
        }

        private static void OnCoordinateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var display = d as CoordinateDisplay;
            if (display != null)
            {
                display.UpdateFormattedText();
            }
        }

        private void UpdateFormattedText()
        {
            FormattedText = string.Format("X: {0:F3} {2}  Y: {1:F3} {2}", X, Y, Units);
        }

        public CoordinateDisplay()
        {
            UpdateFormattedText();
        }
    }
}
