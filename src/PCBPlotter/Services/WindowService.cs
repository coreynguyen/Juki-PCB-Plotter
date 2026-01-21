using System;
using System.Collections.Generic;
using System.Windows;
using PCBPlotter.Core.Events;
using PCBPlotter.Views;

namespace PCBPlotter.Services
{
    /// <summary>
    /// Service for managing floating windows and dialogs
    /// </summary>
    public class WindowService
    {
        private static WindowService _instance;
        public static WindowService Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new WindowService();
                return _instance;
            }
        }

        private Dictionary<string, FloatingWindow> _floatingWindows;
        private Window _mainWindow;

        public WindowService()
        {
            _floatingWindows = new Dictionary<string, FloatingWindow>();
            EventAggregator.Instance.Subscribe<FloatWindowEvent>(OnFloatWindow);
        }

        public void SetMainWindow(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void FloatView(string viewType, FrameworkElement content, string title)
        {
            // Check if already floating
            if (_floatingWindows.ContainsKey(viewType))
            {
                var existing = _floatingWindows[viewType];
                existing.Activate();
                return;
            }

            var window = new FloatingWindow
            {
                Title = title,
                Width = 800,
                Height = 600,
                Owner = _mainWindow
            };

            // Store reference to allow docking back
            window.Tag = viewType;
            window.Closed += (s, e) =>
            {
                _floatingWindows.Remove(viewType);
                EventAggregator.Instance.Publish(new FloatWindowEvent { ViewType = viewType, Float = false });
            };

            window.SetContent(content);
            _floatingWindows[viewType] = window;
            window.Show();
        }

        public bool IsFloating(string viewType)
        {
            return _floatingWindows.ContainsKey(viewType);
        }

        public void DockView(string viewType)
        {
            if (_floatingWindows.ContainsKey(viewType))
            {
                var window = _floatingWindows[viewType];
                window.Close();
                _floatingWindows.Remove(viewType);
            }
        }

        public void CloseAllFloatingWindows()
        {
            foreach (var window in _floatingWindows.Values)
            {
                window.Close();
            }
            _floatingWindows.Clear();
        }

        private void OnFloatWindow(FloatWindowEvent e)
        {
            if (e.Float)
            {
                // Request to float - handled by MainWindow
            }
            else
            {
                // Request to dock - close floating window
                DockView(e.ViewType);
            }
        }
    }
}
