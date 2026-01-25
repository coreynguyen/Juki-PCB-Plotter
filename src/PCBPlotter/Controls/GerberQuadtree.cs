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
        /// Insert a primitive into the quadtree
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
                    foreach (var child in _children)
                        child.Insert(prim);
                    return;
                }

                _items.Add(prim);

                if (_items.Count > MAX_ITEMS_PER_NODE && _depth < MAX_DEPTH)
                {
                    Subdivide();
                }
            }
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

            foreach (var item in _items)
            {
                foreach (var child in _children)
                    child.Insert(item);
            }

            _items.Clear();
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
                if (_children != null)
                {
                    foreach (var child in _children)
                        child.QueryRect(rect, results);
                }
                else
                {
                    foreach (var item in _items)
                    {
                        if (rect.IntersectsWith(item.GetBounds()))
                            results.Add(item);
                    }
                }
            }
        }
    }
}
