using System;
using System.Collections.Generic;
using System.Windows;

namespace PCBPlotter.Controls
{
    /// <summary>
    /// Optimized frustum culling for 2D orthographic projection.
    /// Uses AABB (Axis-Aligned Bounding Box) tests for fast visibility determination.
    /// </summary>
    public class FrustumCuller
    {
        private Rect _viewBounds;
        private double _minVisibleSize;
        private double _expansionMargin;

        // Statistics
        private int _totalTested;
        private int _culledCount;
        private int _lodFiltered;

        /// <summary>
        /// Updates the view frustum bounds.
        /// </summary>
        /// <param name="viewBounds">The visible world-space bounds</param>
        /// <param name="zoom">Current zoom level for LOD calculations</param>
        /// <param name="margin">Margin to expand bounds (prevents popping at edges)</param>
        public void SetViewBounds(Rect viewBounds, double zoom, double margin = 0.1)
        {
            // Expand bounds slightly to prevent edge popping
            _expansionMargin = Math.Max(viewBounds.Width, viewBounds.Height) * margin;
            _viewBounds = new Rect(
                viewBounds.X - _expansionMargin,
                viewBounds.Y - _expansionMargin,
                viewBounds.Width + _expansionMargin * 2,
                viewBounds.Height + _expansionMargin * 2);

            // Calculate minimum visible size for LOD filtering
            // Objects smaller than ~1 pixel are filtered out
            _minVisibleSize = 0.5 / zoom;

            // Reset statistics
            _totalTested = 0;
            _culledCount = 0;
            _lodFiltered = 0;
        }

        /// <summary>
        /// Tests if a point is within the view frustum.
        /// </summary>
        public bool IsVisible(double x, double y)
        {
            _totalTested++;
            bool visible = _viewBounds.Contains(x, y);
            if (!visible) _culledCount++;
            return visible;
        }

        /// <summary>
        /// Tests if a circle is within or intersects the view frustum.
        /// </summary>
        public bool IsVisible(double centerX, double centerY, double radius)
        {
            _totalTested++;

            // LOD filtering for tiny circles
            if (radius * 2 < _minVisibleSize)
            {
                _lodFiltered++;
                return false;
            }

            // AABB vs circle test
            // Find the closest point on the rectangle to the circle center
            double closestX = Math.Max(_viewBounds.Left, Math.Min(centerX, _viewBounds.Right));
            double closestY = Math.Max(_viewBounds.Top, Math.Min(centerY, _viewBounds.Bottom));

            // Calculate the distance from that point to the circle center
            double distX = centerX - closestX;
            double distY = centerY - closestY;
            double distanceSquared = distX * distX + distY * distY;

            bool visible = distanceSquared <= radius * radius;
            if (!visible) _culledCount++;
            return visible;
        }

        /// <summary>
        /// Tests if an AABB (Axis-Aligned Bounding Box) is within or intersects the view frustum.
        /// </summary>
        public bool IsVisible(double x, double y, double width, double height)
        {
            _totalTested++;

            // LOD filtering for tiny boxes
            if (width < _minVisibleSize && height < _minVisibleSize)
            {
                _lodFiltered++;
                return false;
            }

            // Calculate bounds
            double minX = x - width / 2;
            double maxX = x + width / 2;
            double minY = y - height / 2;
            double maxY = y + height / 2;

            // AABB vs AABB intersection test
            bool visible = !(maxX < _viewBounds.Left ||
                           minX > _viewBounds.Right ||
                           maxY < _viewBounds.Top ||
                           minY > _viewBounds.Bottom);

            if (!visible) _culledCount++;
            return visible;
        }

        /// <summary>
        /// Tests if a line segment is within or intersects the view frustum.
        /// </summary>
        public bool IsLineVisible(double x1, double y1, double x2, double y2, double width)
        {
            _totalTested++;

            // Calculate line AABB
            double minX = Math.Min(x1, x2) - width / 2;
            double maxX = Math.Max(x1, x2) + width / 2;
            double minY = Math.Min(y1, y2) - width / 2;
            double maxY = Math.Max(y1, y2) + width / 2;

            // Quick AABB rejection test
            if (maxX < _viewBounds.Left ||
                minX > _viewBounds.Right ||
                maxY < _viewBounds.Top ||
                minY > _viewBounds.Bottom)
            {
                _culledCount++;
                return false;
            }

            // Line intersects or is contained in view bounds
            return true;
        }

        /// <summary>
        /// Tests if a polygon's AABB is within or intersects the view frustum.
        /// </summary>
        public bool IsPolygonVisible(IList<Point> points)
        {
            if (points == null || points.Count < 3)
                return false;

            _totalTested++;

            // Calculate polygon AABB
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (var pt in points)
            {
                minX = Math.Min(minX, pt.X);
                maxX = Math.Max(maxX, pt.X);
                minY = Math.Min(minY, pt.Y);
                maxY = Math.Max(maxY, pt.Y);
            }

            double width = maxX - minX;
            double height = maxY - minY;

            // LOD filtering for tiny polygons
            if (width < _minVisibleSize && height < _minVisibleSize)
            {
                _lodFiltered++;
                return false;
            }

            // AABB vs AABB intersection test
            bool visible = !(maxX < _viewBounds.Left ||
                           minX > _viewBounds.Right ||
                           maxY < _viewBounds.Top ||
                           minY > _viewBounds.Bottom);

            if (!visible) _culledCount++;
            return visible;
        }

        /// <summary>
        /// Gets the current view bounds.
        /// </summary>
        public Rect ViewBounds => _viewBounds;

        /// <summary>
        /// Gets the minimum size for LOD filtering.
        /// </summary>
        public double MinVisibleSize => _minVisibleSize;

        /// <summary>
        /// Gets culling statistics for the current frame.
        /// </summary>
        public CullingStats GetStats()
        {
            return new CullingStats
            {
                TotalTested = _totalTested,
                CulledCount = _culledCount,
                LodFiltered = _lodFiltered,
                VisibleCount = _totalTested - _culledCount - _lodFiltered
            };
        }

        /// <summary>
        /// Resets statistics for a new frame.
        /// </summary>
        public void ResetStats()
        {
            _totalTested = 0;
            _culledCount = 0;
            _lodFiltered = 0;
        }
    }

    /// <summary>
    /// Statistics about frustum culling efficiency.
    /// </summary>
    public struct CullingStats
    {
        public int TotalTested;
        public int CulledCount;
        public int LodFiltered;
        public int VisibleCount;

        public float CullRate =>
            TotalTested > 0 ? (float)(CulledCount + LodFiltered) / TotalTested : 0;

        public override string ToString()
        {
            return $"Tested: {TotalTested}, Visible: {VisibleCount}, " +
                   $"Culled: {CulledCount}, LOD filtered: {LodFiltered} " +
                   $"({CullRate:P0} rejection rate)";
        }
    }
}
