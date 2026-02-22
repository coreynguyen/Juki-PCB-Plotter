using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using PCBPlotter.Core.Interfaces;

namespace PCBPlotter.Services
{
    /// <summary>
    /// WPF DrawingVisual-based renderer for high performance 2D graphics
    /// </summary>
    public class WpfDrawingRenderer : ICanvasRenderer
    {
        private DrawingContext _drawingContext;
        private DrawingGroup _drawingGroup;
        private Stack<Matrix> _transformStack;
        private Stack<Geometry> _clipStack;
        private Dictionary<int, Pen> _penCache;
        private Dictionary<int, SolidColorBrush> _brushCache;
        private List<Tuple<Rect, object>> _hitTestObjects;

        public string Name { get { return "WPF DrawingVisual"; } }
        public bool IsHardwareAccelerated { get { return true; } } // WPF uses DirectX

        public DrawingGroup DrawingGroup { get { return _drawingGroup; } }

        public WpfDrawingRenderer()
        {
            _transformStack = new Stack<Matrix>();
            _clipStack = new Stack<Geometry>();
            _penCache = new Dictionary<int, Pen>();
            _brushCache = new Dictionary<int, SolidColorBrush>();
            _hitTestObjects = new List<Tuple<Rect, object>>();
        }

        public void Initialize(object target)
        {
            // Target can be a DrawingVisual or DrawingGroup
            _drawingGroup = target as DrawingGroup ?? new DrawingGroup();
        }

        public void BeginFrame()
        {
            _drawingGroup = new DrawingGroup();
            _drawingContext = _drawingGroup.Open();
            _transformStack.Clear();
            _clipStack.Clear();
            _hitTestObjects.Clear();
        }

        public void EndFrame()
        {
            if (_drawingContext != null)
            {
                _drawingContext.Close();
                _drawingContext = null;
            }
        }

        public void Clear(Color color)
        {
            // Draw background rectangle
            var brush = GetCachedBrush(color);
            _drawingContext?.DrawRectangle(brush, null, new Rect(0, 0, 10000, 10000));
        }

        public void SetTransform(Matrix transform)
        {
            if (_drawingContext != null)
            {
                _drawingContext.PushTransform(new MatrixTransform(transform));
            }
        }

        public void PushTransform(Matrix transform)
        {
            _transformStack.Push(transform);
            if (_drawingContext != null)
            {
                _drawingContext.PushTransform(new MatrixTransform(transform));
            }
        }

        public void PopTransform()
        {
            if (_transformStack.Count > 0)
            {
                _transformStack.Pop();
                _drawingContext?.Pop();
            }
        }

        public void PushClip(Rect clipRect)
        {
            var geometry = new RectangleGeometry(clipRect);
            _clipStack.Push(geometry);
            _drawingContext?.PushClip(geometry);
        }

        public void PopClip()
        {
            if (_clipStack.Count > 0)
            {
                _clipStack.Pop();
                _drawingContext?.Pop();
            }
        }

        public void DrawLine(Point p1, Point p2, Color color, double thickness)
        {
            var pen = GetCachedPen(color, thickness);
            _drawingContext?.DrawLine(pen, p1, p2);
        }

        public void DrawRectangle(Rect bounds, Color? fill, Color? stroke, double strokeThickness = 1)
        {
            var brush = fill.HasValue ? GetCachedBrush(fill.Value) : null;
            var pen = stroke.HasValue ? GetCachedPen(stroke.Value, strokeThickness) : null;
            _drawingContext?.DrawRectangle(brush, pen, bounds);
        }

        public void DrawRoundedRectangle(Rect bounds, double radiusX, double radiusY, Color? fill, Color? stroke, double strokeThickness = 1)
        {
            var brush = fill.HasValue ? GetCachedBrush(fill.Value) : null;
            var pen = stroke.HasValue ? GetCachedPen(stroke.Value, strokeThickness) : null;
            _drawingContext?.DrawRoundedRectangle(brush, pen, bounds, radiusX, radiusY);
        }

        public void DrawEllipse(Point center, double radiusX, double radiusY, Color? fill, Color? stroke, double strokeThickness = 1)
        {
            var brush = fill.HasValue ? GetCachedBrush(fill.Value) : null;
            var pen = stroke.HasValue ? GetCachedPen(stroke.Value, strokeThickness) : null;
            _drawingContext?.DrawEllipse(brush, pen, center, radiusX, radiusY);
        }

        public void DrawCircle(Point center, double radius, Color? fill, Color? stroke, double strokeThickness = 1)
        {
            DrawEllipse(center, radius, radius, fill, stroke, strokeThickness);
        }

        public void DrawPolygon(IList<Point> points, Color? fill, Color? stroke, double strokeThickness = 1)
        {
            if (points == null || points.Count < 3) return;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], fill.HasValue, true);
                for (int i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(points[i], stroke.HasValue, false);
                }
            }
            geometry.Freeze();

            var brush = fill.HasValue ? GetCachedBrush(fill.Value) : null;
            var pen = stroke.HasValue ? GetCachedPen(stroke.Value, strokeThickness) : null;
            _drawingContext?.DrawGeometry(brush, pen, geometry);
        }

        public void DrawPolyline(IList<Point> points, Color color, double thickness)
        {
            if (points == null || points.Count < 2) return;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], false, false);
                for (int i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(points[i], true, false);
                }
            }
            geometry.Freeze();

            var pen = GetCachedPen(color, thickness);
            _drawingContext?.DrawGeometry(null, pen, geometry);
        }

        public void DrawArc(Point center, double radius, double startAngle, double endAngle, Color color, double thickness)
        {
            // Convert angles to radians
            double startRad = startAngle * Math.PI / 180;
            double endRad = endAngle * Math.PI / 180;

            Point startPoint = new Point(
                center.X + radius * Math.Cos(startRad),
                center.Y + radius * Math.Sin(startRad)
            );

            Point endPoint = new Point(
                center.X + radius * Math.Cos(endRad),
                center.Y + radius * Math.Sin(endRad)
            );

            bool isLargeArc = Math.Abs(endAngle - startAngle) > 180;
            SweepDirection direction = endAngle > startAngle ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(startPoint, false, false);
                ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc, direction, true, false);
            }
            geometry.Freeze();

            var pen = GetCachedPen(color, thickness);
            _drawingContext?.DrawGeometry(null, pen, geometry);
        }

        public void DrawText(string text, Point position, double size, Color color, string fontFamily = "Segoe UI")
        {
            if (string.IsNullOrEmpty(text)) return;

            var typeface = new Typeface(fontFamily);
            double pixelsPerDip = 1.0;
            try
            {
                if (Application.Current?.MainWindow != null)
                    pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
            }
            catch { /* Use default 1.0 */ }

            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                GetCachedBrush(color),
                pixelsPerDip
            );

            _drawingContext?.DrawText(formattedText, position);
        }

        public void DrawPath(string pathData, Color? fill, Color? stroke, double strokeThickness = 1)
        {
            try
            {
                var geometry = Geometry.Parse(pathData);
                var brush = fill.HasValue ? GetCachedBrush(fill.Value) : null;
                var pen = stroke.HasValue ? GetCachedPen(stroke.Value, strokeThickness) : null;
                _drawingContext?.DrawGeometry(brush, pen, geometry);
            }
            catch
            {
                // Invalid path data
            }
        }

        public object HitTest(Point point)
        {
            foreach (var item in _hitTestObjects)
            {
                if (item.Item1.Contains(point))
                {
                    return item.Item2;
                }
            }
            return null;
        }

        public IList<object> HitTestRect(Rect bounds)
        {
            var results = new List<object>();
            foreach (var item in _hitTestObjects)
            {
                if (bounds.IntersectsWith(item.Item1))
                {
                    results.Add(item.Item2);
                }
            }
            return results;
        }

        public void RegisterHitTestObject(Rect bounds, object obj)
        {
            _hitTestObjects.Add(Tuple.Create(bounds, obj));
        }

        #region Caching

        private Pen GetCachedPen(Color color, double thickness)
        {
            int key = color.GetHashCode() ^ thickness.GetHashCode();
            Pen pen;
            if (!_penCache.TryGetValue(key, out pen))
            {
                pen = new Pen(GetCachedBrush(color), thickness);
                pen.Freeze();
                _penCache[key] = pen;
            }
            return pen;
        }

        private SolidColorBrush GetCachedBrush(Color color)
        {
            int key = color.GetHashCode();
            SolidColorBrush brush;
            if (!_brushCache.TryGetValue(key, out brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                _brushCache[key] = brush;
            }
            return brush;
        }

        public void ClearCache()
        {
            _penCache.Clear();
            _brushCache.Clear();
        }

        #endregion

        public void Dispose()
        {
            ClearCache();
            _drawingContext = null;
            _drawingGroup = null;
        }
    }
}
