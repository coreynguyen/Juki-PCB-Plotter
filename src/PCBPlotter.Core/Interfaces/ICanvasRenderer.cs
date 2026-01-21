using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PCBPlotter.Core.Interfaces
{
    /// <summary>
    /// Interface for canvas rendering implementations
    /// Allows swapping between WPF, DirectX, and GDI+ renderers
    /// </summary>
    public interface ICanvasRenderer : IDisposable
    {
        /// <summary>
        /// Name of this renderer
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether this renderer supports GPU acceleration
        /// </summary>
        bool IsHardwareAccelerated { get; }

        /// <summary>
        /// Initialize the renderer with the target visual/surface
        /// </summary>
        void Initialize(object target);

        /// <summary>
        /// Begin a new frame
        /// </summary>
        void BeginFrame();

        /// <summary>
        /// End the current frame and present
        /// </summary>
        void EndFrame();

        /// <summary>
        /// Clear the canvas with a color
        /// </summary>
        void Clear(Color color);

        /// <summary>
        /// Set the current transform matrix
        /// </summary>
        void SetTransform(Matrix transform);

        /// <summary>
        /// Push a transform onto the stack
        /// </summary>
        void PushTransform(Matrix transform);

        /// <summary>
        /// Pop the last transform from the stack
        /// </summary>
        void PopTransform();

        /// <summary>
        /// Set a clip region
        /// </summary>
        void PushClip(Rect clipRect);

        /// <summary>
        /// Remove the clip region
        /// </summary>
        void PopClip();

        #region Drawing Methods

        void DrawLine(Point p1, Point p2, Color color, double thickness);

        void DrawRectangle(Rect bounds, Color? fill, Color? stroke, double strokeThickness = 1);

        void DrawRoundedRectangle(Rect bounds, double radiusX, double radiusY, Color? fill, Color? stroke, double strokeThickness = 1);

        void DrawEllipse(Point center, double radiusX, double radiusY, Color? fill, Color? stroke, double strokeThickness = 1);

        void DrawCircle(Point center, double radius, Color? fill, Color? stroke, double strokeThickness = 1);

        void DrawPolygon(IList<Point> points, Color? fill, Color? stroke, double strokeThickness = 1);

        void DrawPolyline(IList<Point> points, Color color, double thickness);

        void DrawArc(Point center, double radius, double startAngle, double endAngle, Color color, double thickness);

        void DrawText(string text, Point position, double size, Color color, string fontFamily = "Segoe UI");

        void DrawPath(string pathData, Color? fill, Color? stroke, double strokeThickness = 1);

        #endregion

        #region Hit Testing

        /// <summary>
        /// Test if a point hits any rendered object
        /// </summary>
        object HitTest(Point point);

        /// <summary>
        /// Get all objects within a rectangle
        /// </summary>
        IList<object> HitTestRect(Rect bounds);

        #endregion
    }

    /// <summary>
    /// Render state for batching similar draw calls
    /// </summary>
    public class RenderState
    {
        public Color? FillColor { get; set; }
        public Color? StrokeColor { get; set; }
        public double StrokeThickness { get; set; }
        public Matrix Transform { get; set; }

        public RenderState()
        {
            StrokeThickness = 1;
            Transform = Matrix.Identity;
        }
    }

    /// <summary>
    /// Factory for creating renderer instances
    /// </summary>
    public static class RendererFactory
    {
        public static ICanvasRenderer Create(Models.RendererType type)
        {
            switch (type)
            {
                case Models.RendererType.WpfDrawingVisual:
                    // Will be implemented in UI project
                    throw new NotImplementedException("WPF renderer is in the UI project");

                case Models.RendererType.DirectX:
                    throw new NotImplementedException("DirectX renderer not yet implemented");

                case Models.RendererType.GdiPlus:
                    throw new NotImplementedException("GDI+ renderer not yet implemented");

                default:
                    throw new ArgumentException("Unknown renderer type");
            }
        }
    }
}
