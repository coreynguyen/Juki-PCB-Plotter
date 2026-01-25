using System.Collections.Generic;
using System.Windows;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Controls
{
    /// <summary>
    /// Quadtree for spatial indexing of Gerber primitives.
    /// Used for efficient viewport culling during rendering.
    /// </summary>
    public class GerberQuadtree
    {
        private const int MAX_ITEMS_PER_NODE = 50;
        private const int MAX_DEPTH = 8;

        private Rect _bounds;
        private int _depth;
        private List<GerberPrimitive> _items;
        private GerberQuadtree[] _children;
        private readonly object _lock = new object();

        public GerberQuadtree(Rect bounds, int depth = 0)
        {
            _bounds = bounds;
            _depth = depth;
            _items = new List<GerberPrimitive>();
        }

        /// <summary>
        /// Insert a primitive into the quadtree.
        /// Uses loose quadtree pattern: primitives that span child boundaries stay at parent.
        /// </summary>
        public void Insert(GerberPrimitive prim)
        {
            var primBounds = prim.GetBounds();
            if (!_bounds.IntersectsWith(primBounds))
                return;

            lock (_lock)
            {
                if (_children != null)
                {
                    // Find which child(ren) the primitive intersects
                    int targetChild = GetContainingChild(primBounds);
                    if (targetChild >= 0)
                    {
                        // Primitive fits entirely in one child - insert there
                        _children[targetChild].Insert(prim);
                    }
                    else
                    {
                        // Primitive spans multiple children - store at this level
                        _items.Add(prim);
                    }
                    return;
                }

                _items.Add(prim);

                if (_items.Count > MAX_ITEMS_PER_NODE && _depth < MAX_DEPTH)
                {
                    Subdivide();
                }
            }
        }

        /// <summary>
        /// Returns the index of the child that fully contains the bounds, or -1 if spans multiple children.
        /// </summary>
        private int GetContainingChild(Rect primBounds)
        {
            double midX = _bounds.X + _bounds.Width / 2;
            double midY = _bounds.Y + _bounds.Height / 2;

            bool fitsLeft = primBounds.Right <= midX;
            bool fitsRight = primBounds.Left >= midX;
            bool fitsBottom = primBounds.Top <= midY;
            bool fitsTop = primBounds.Bottom >= midY;

            // Child layout: 0=bottomLeft, 1=bottomRight, 2=topLeft, 3=topRight
            if (fitsLeft && fitsBottom) return 0;
            if (fitsRight && fitsBottom) return 1;
            if (fitsLeft && fitsTop) return 2;
            if (fitsRight && fitsTop) return 3;

            return -1; // Spans multiple quadrants
        }

        private void Subdivide()
        {
            double halfW = _bounds.Width / 2;
            double halfH = _bounds.Height / 2;
            double x = _bounds.X;
            double y = _bounds.Y;

            _children = new GerberQuadtree[4];
            _children[0] = new GerberQuadtree(new Rect(x, y, halfW, halfH), _depth + 1);
            _children[1] = new GerberQuadtree(new Rect(x + halfW, y, halfW, halfH), _depth + 1);
            _children[2] = new GerberQuadtree(new Rect(x, y + halfH, halfW, halfH), _depth + 1);
            _children[3] = new GerberQuadtree(new Rect(x + halfW, y + halfH, halfW, halfH), _depth + 1);

            // Re-insert items - those that fit in one child go there, others stay here
            var itemsToKeep = new List<GerberPrimitive>();
            foreach (var item in _items)
            {
                var itemBounds = item.GetBounds();
                int targetChild = GetContainingChild(itemBounds);
                if (targetChild >= 0)
                {
                    _children[targetChild].Insert(item);
                }
                else
                {
                    // Spans multiple children - keep at this level
                    itemsToKeep.Add(item);
                }
            }

            _items = itemsToKeep;
        }

        /// <summary>
        /// Query primitives near a point within a given radius
        /// </summary>
        public List<GerberPrimitive> Query(Point worldPoint, double radius)
        {
            var results = new List<GerberPrimitive>();
            var queryRect = new Rect(worldPoint.X - radius, worldPoint.Y - radius, radius * 2, radius * 2);
            QueryRect(queryRect, results);
            return results;
        }

        /// <summary>
        /// Query all primitives that intersect a rectangle
        /// </summary>
        public List<GerberPrimitive> QueryRect(Rect rect)
        {
            var results = new List<GerberPrimitive>();
            QueryRect(rect, results);
            return results;
        }

        private void QueryRect(Rect rect, List<GerberPrimitive> results)
        {
            if (!_bounds.IntersectsWith(rect))
                return;

            lock (_lock)
            {
                // Always check items at this node (loose quadtree stores spanning items here)
                foreach (var item in _items)
                {
                    if (rect.IntersectsWith(item.GetBounds()))
                        results.Add(item);
                }

                // If subdivided, recurse into children
                if (_children != null)
                {
                    foreach (var child in _children)
                        child.QueryRect(rect, results);
                }
            }
        }
    }
}
