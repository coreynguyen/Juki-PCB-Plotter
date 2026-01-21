using System;
using System.Windows;
using System.Windows.Media;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Rendering
{
    /// <summary>
    /// Context for rendering operations, holds common state and utilities
    /// </summary>
    public class RenderContext
    {
        /// <summary>
        /// Current zoom level
        /// </summary>
        public double Zoom { get; set; }

        /// <summary>
        /// Pan offset X
        /// </summary>
        public double PanX { get; set; }

        /// <summary>
        /// Pan offset Y
        /// </summary>
        public double PanY { get; set; }

        /// <summary>
        /// Viewport width in pixels
        /// </summary>
        public double ViewportWidth { get; set; }

        /// <summary>
        /// Viewport height in pixels
        /// </summary>
        public double ViewportHeight { get; set; }

        /// <summary>
        /// Current view side
        /// </summary>
        public BoardSide ViewSide { get; set; }

        /// <summary>
        /// Current view orientation
        /// </summary>
        public ViewOrientation Orientation { get; set; }

        /// <summary>
        /// Whether to show polarity indicators
        /// </summary>
        public bool ShowPolarity { get; set; }

        /// <summary>
        /// Whether to show reference labels
        /// </summary>
        public bool ShowLabels { get; set; }

        /// <summary>
        /// Whether to show grid
        /// </summary>
        public bool ShowGrid { get; set; }

        /// <summary>
        /// Grid spacing in units
        /// </summary>
        public double GridSpacing { get; set; }

        /// <summary>
        /// Current units
        /// </summary>
        public Units Units { get; set; }

        /// <summary>
        /// Board width for coordinate flipping
        /// </summary>
        public double BoardWidth { get; set; }

        public RenderContext()
        {
            Zoom = 1.0;
            ViewSide = BoardSide.Top;
            Orientation = ViewOrientation.TopDown;
            ShowPolarity = true;
            ShowLabels = true;
            ShowGrid = true;
            GridSpacing = 1.0;
            Units = Units.Millimeters;
        }

        /// <summary>
        /// Transform world coordinates to screen coordinates
        /// </summary>
        public Point WorldToScreen(Point world)
        {
            double x = world.X;
            double y = world.Y;

            // Flip X for bottom view
            if (ViewSide == BoardSide.Bottom)
            {
                x = BoardWidth - x;
            }

            // Apply zoom and pan
            x = (x * Zoom) + PanX;
            y = (y * Zoom) + PanY;

            // Flip Y for screen coordinates (Y increases downward)
            y = ViewportHeight - y;

            return new Point(x, y);
        }

        /// <summary>
        /// Transform screen coordinates to world coordinates
        /// </summary>
        public Point ScreenToWorld(Point screen)
        {
            double x = screen.X;
            double y = screen.Y;

            // Flip Y for world coordinates
            y = ViewportHeight - y;

            // Remove pan and zoom
            x = (x - PanX) / Zoom;
            y = (y - PanY) / Zoom;

            // Flip X for bottom view
            if (ViewSide == BoardSide.Bottom)
            {
                x = BoardWidth - x;
            }

            return new Point(x, y);
        }

        /// <summary>
        /// Transform a world rectangle to screen rectangle
        /// </summary>
        public Rect WorldToScreenRect(Rect world)
        {
            var topLeft = WorldToScreen(new Point(world.Left, world.Top));
            var bottomRight = WorldToScreen(new Point(world.Right, world.Bottom));

            return new Rect(
                Math.Min(topLeft.X, bottomRight.X),
                Math.Min(topLeft.Y, bottomRight.Y),
                Math.Abs(bottomRight.X - topLeft.X),
                Math.Abs(bottomRight.Y - topLeft.Y)
            );
        }

        /// <summary>
        /// Get visible world bounds
        /// </summary>
        public Rect GetVisibleWorldBounds()
        {
            var topLeft = ScreenToWorld(new Point(0, 0));
            var bottomRight = ScreenToWorld(new Point(ViewportWidth, ViewportHeight));

            return new Rect(
                Math.Min(topLeft.X, bottomRight.X),
                Math.Min(topLeft.Y, bottomRight.Y),
                Math.Abs(bottomRight.X - topLeft.X),
                Math.Abs(bottomRight.Y - topLeft.Y)
            );
        }

        /// <summary>
        /// Check if a world point is visible
        /// </summary>
        public bool IsVisible(Point worldPoint)
        {
            var bounds = GetVisibleWorldBounds();
            return bounds.Contains(worldPoint);
        }

        /// <summary>
        /// Check if a world rectangle is visible (at least partially)
        /// </summary>
        public bool IsVisible(Rect worldRect)
        {
            var bounds = GetVisibleWorldBounds();
            return bounds.IntersectsWith(worldRect);
        }

        /// <summary>
        /// Get appropriate line thickness for current zoom
        /// </summary>
        public double GetLineThickness(double baseThickness = 1.0)
        {
            // Scale line thickness inversely with zoom for consistent appearance
            return Math.Max(0.5, baseThickness / Zoom);
        }

        /// <summary>
        /// Get appropriate text size for current zoom
        /// </summary>
        public double GetTextSize(double baseSize = 12.0)
        {
            // Keep text readable at different zoom levels
            return Math.Max(8, Math.Min(24, baseSize / Zoom));
        }
    }

    /// <summary>
    /// Color scheme for rendering
    /// </summary>
    public static class RenderColors
    {
        // Background
        public static Color Background = Color.FromRgb(30, 30, 30);
        public static Color Grid = Color.FromRgb(50, 50, 50);
        public static Color GridMajor = Color.FromRgb(70, 70, 70);

        // Board
        public static Color BoardOutline = Color.FromRgb(255, 255, 0);
        public static Color CircuitOutline = Color.FromRgb(0, 255, 255);

        // Placements
        public static Color PlacementBody = Color.FromRgb(74, 74, 74);
        public static Color PlacementOutline = Color.FromRgb(200, 200, 200);
        public static Color PlacementPad = Color.FromRgb(180, 150, 50);
        public static Color PlacementSelected = Color.FromRgb(0, 150, 255);
        public static Color PlacementError = Color.FromRgb(255, 80, 80);

        // Polarity
        public static Color Pin1Indicator = Color.FromRgb(255, 50, 50);

        // Fiducials
        public static Color Fiducial = Color.FromRgb(255, 0, 255);

        // Labels
        public static Color LabelText = Color.FromRgb(255, 255, 255);
        public static Color LabelBackground = Color.FromArgb(180, 0, 0, 0);

        // Selection
        public static Color SelectionBox = Color.FromArgb(100, 0, 150, 255);
        public static Color SelectionBorder = Color.FromRgb(0, 150, 255);

        // Origin/Axes
        public static Color OriginMarker = Color.FromRgb(255, 255, 255);
        public static Color AxisX = Color.FromRgb(255, 0, 0);
        public static Color AxisY = Color.FromRgb(0, 255, 0);
    }
}
