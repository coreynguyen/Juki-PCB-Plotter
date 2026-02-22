using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Themed About dialog with procedural animated graphics and optional 8-bit audio
    /// </summary>
    public partial class AboutDialog : Window
    {
        private DispatcherTimer _animationTimer;
        private List<UIElement> _traceElements = new List<UIElement>();
        private Random _random = new Random();
        private int _clickCount = 0;
        private DateTime _lastClickTime = DateTime.MinValue;
        private bool _easterEggActive = false;
        private ChiptunePlayer _chiptunePlayer;

        public AboutDialog()
        {
            InitializeComponent();
            Loaded += AboutDialog_Loaded;
            Closed += AboutDialog_Closed;
        }

        private void AboutDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Start procedural background animation
            CreateProceduralBackground();
            StartAnimation();

            // Animate title with glow pulse
            AnimateTitleGlow();
        }

        private void AboutDialog_Closed(object sender, EventArgs e)
        {
            _animationTimer?.Stop();
            _chiptunePlayer?.Stop();
        }

        private void CreateProceduralBackground()
        {
            // Create animated PCB trace lines
            for (int i = 0; i < 15; i++)
            {
                CreateTrace();
            }

            // Create some "component" shapes
            for (int i = 0; i < 8; i++)
            {
                CreateComponent();
            }

            // Create via points
            for (int i = 0; i < 12; i++)
            {
                CreateVia();
            }
        }

        private void CreateTrace()
        {
            var accentColor = TryFindResource("Accent") as Color? ?? Colors.Cyan;

            var line = new Line
            {
                X1 = _random.NextDouble() * ActualWidth,
                Y1 = _random.NextDouble() * ActualHeight,
                X2 = _random.NextDouble() * ActualWidth,
                Y2 = _random.NextDouble() * ActualHeight,
                Stroke = new SolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B)),
                StrokeThickness = _random.Next(1, 3),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            _traceElements.Add(line);
            BackgroundCanvas.Children.Add(line);
        }

        private void CreateComponent()
        {
            var accentColor = TryFindResource("Accent") as Color? ?? Colors.Cyan;
            double x = _random.NextDouble() * (ActualWidth - 40);
            double y = _random.NextDouble() * (ActualHeight - 20);

            var rect = new Rectangle
            {
                Width = _random.Next(20, 50),
                Height = _random.Next(10, 25),
                Fill = new SolidColorBrush(Color.FromArgb(25, accentColor.R, accentColor.G, accentColor.B)),
                Stroke = new SolidColorBrush(Color.FromArgb(50, accentColor.R, accentColor.G, accentColor.B)),
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2
            };

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            _traceElements.Add(rect);
            BackgroundCanvas.Children.Add(rect);
        }

        private void CreateVia()
        {
            var accentColor = TryFindResource("Accent") as Color? ?? Colors.Cyan;
            double x = _random.NextDouble() * ActualWidth;
            double y = _random.NextDouble() * ActualHeight;

            var outerCircle = new Ellipse
            {
                Width = 12,
                Height = 12,
                Stroke = new SolidColorBrush(Color.FromArgb(60, accentColor.R, accentColor.G, accentColor.B)),
                StrokeThickness = 1,
                Fill = Brushes.Transparent
            };

            var innerCircle = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B))
            };

            Canvas.SetLeft(outerCircle, x - 6);
            Canvas.SetTop(outerCircle, y - 6);
            Canvas.SetLeft(innerCircle, x - 3);
            Canvas.SetTop(innerCircle, y - 3);

            _traceElements.Add(outerCircle);
            _traceElements.Add(innerCircle);
            BackgroundCanvas.Children.Add(outerCircle);
            BackgroundCanvas.Children.Add(innerCircle);
        }

        private void StartAnimation()
        {
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _animationTimer.Tick += AnimationTimer_Tick;
            _animationTimer.Start();
        }

        private double _animationPhase = 0;

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            _animationPhase += 0.02;

            // Subtle opacity animation for trace elements
            for (int i = 0; i < _traceElements.Count; i++)
            {
                var element = _traceElements[i];
                double phase = _animationPhase + (i * 0.3);
                double opacity = 0.3 + 0.2 * Math.Sin(phase);

                if (element is Line line)
                {
                    var accentColor = TryFindResource("Accent") as Color? ?? Colors.Cyan;
                    byte alpha = (byte)(opacity * 100);
                    line.Stroke = new SolidColorBrush(Color.FromArgb(alpha, accentColor.R, accentColor.G, accentColor.B));
                }
                else if (element is Shape shape)
                {
                    shape.Opacity = opacity;
                }
            }
        }

        private void AnimateTitleGlow()
        {
            var glowAnimation = new DoubleAnimation
            {
                From = 10,
                To = 20,
                Duration = TimeSpan.FromSeconds(1.5),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            if (TitleText.Effect is DropShadowEffect effect)
            {
                effect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowAnimation);
            }
        }

        private void EasterEggArea_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.Now;

            // Check for triple-click within 500ms window
            if ((now - _lastClickTime).TotalMilliseconds < 500)
            {
                _clickCount++;
            }
            else
            {
                _clickCount = 1;
            }
            _lastClickTime = now;

            if (_clickCount >= 3 && !_easterEggActive)
            {
                ActivateEasterEgg();
            }
        }

        private void ActivateEasterEgg()
        {
            _easterEggActive = true;
            EasterEggHint.Text = "8-BIT MODE ACTIVATED!";

            // Start chiptune music
            _chiptunePlayer = new ChiptunePlayer();
            _chiptunePlayer.Play();

            // Make background more vibrant
            var colorAnimation = new ColorAnimation
            {
                To = Colors.Purple,
                Duration = TimeSpan.FromSeconds(0.5)
            };

            // Flash effect
            var flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            int flashCount = 0;
            flashTimer.Tick += (s, e) =>
            {
                flashCount++;
                MainGrid.Opacity = flashCount % 2 == 0 ? 1.0 : 0.8;
                if (flashCount > 6)
                {
                    flashTimer.Stop();
                    MainGrid.Opacity = 1.0;
                }
            };
            flashTimer.Start();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _chiptunePlayer?.Stop();
            DialogResult = true;
            Close();
        }
    }

    /// <summary>
    /// Simple 8-bit chiptune audio generator using NAudio-free approach (beep-based fallback)
    /// </summary>
    internal class ChiptunePlayer
    {
        private DispatcherTimer _noteTimer;
        private int _noteIndex = 0;
        private bool _isPlaying = false;

        // Simple 8-bit melody (frequencies in Hz)
        private readonly int[] _melody = new int[]
        {
            523, 587, 659, 698, 784, 698, 659, 587,  // C5-D5-E5-F5-G5-F5-E5-D5
            523, 659, 784, 880, 784, 659, 523, 0,    // C5-E5-G5-A5-G5-E5-C5-rest
            392, 440, 523, 587, 659, 587, 523, 440,  // G4-A4-C5-D5-E5-D5-C5-A4
            392, 523, 392, 330, 262, 330, 392, 0     // G4-C5-G4-E4-C4-E4-G4-rest
        };

        private readonly int[] _durations = new int[]
        {
            150, 150, 150, 150, 200, 150, 150, 150,
            200, 150, 150, 300, 150, 150, 300, 200,
            150, 150, 150, 150, 200, 150, 150, 150,
            200, 150, 150, 150, 200, 150, 300, 400
        };

        public void Play()
        {
            if (_isPlaying) return;
            _isPlaying = true;
            _noteIndex = 0;

            _noteTimer = new DispatcherTimer();
            PlayNextNote();
        }

        private void PlayNextNote()
        {
            if (!_isPlaying || _noteIndex >= _melody.Length)
            {
                _noteIndex = 0; // Loop
                if (!_isPlaying) return;
            }

            int freq = _melody[_noteIndex];
            int duration = _durations[_noteIndex];
            _noteIndex++;

            if (freq > 0)
            {
                try
                {
                    // Use Console.Beep for simple audio (works on Windows)
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            Console.Beep(freq, duration - 20);
                        }
                        catch { }
                    });
                }
                catch { }
            }

            _noteTimer.Interval = TimeSpan.FromMilliseconds(duration);
            _noteTimer.Tick -= NoteTimer_Tick;
            _noteTimer.Tick += NoteTimer_Tick;
            _noteTimer.Start();
        }

        private void NoteTimer_Tick(object sender, EventArgs e)
        {
            _noteTimer.Stop();
            if (_isPlaying)
            {
                PlayNextNote();
            }
        }

        public void Stop()
        {
            _isPlaying = false;
            _noteTimer?.Stop();
        }
    }
}
