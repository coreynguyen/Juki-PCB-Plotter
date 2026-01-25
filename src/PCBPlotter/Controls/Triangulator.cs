using System;
using System.Collections.Generic;
using System.Windows;

namespace PCBPlotter.Controls
{
    /// <summary>
    /// Triangulates polygons using the Ear Clipping algorithm.
    /// Handles concave shapes correctly, unlike a simple Triangle Fan.
    /// </summary>
    public static class Triangulator
    {
        /// <summary>
        /// Triangulates a polygon using the Ear Clipping algorithm.
        /// </summary>
        /// <param name="points">The polygon vertices</param>
        /// <returns>List of triangle indices (3 per triangle)</returns>
        public static List<int> Triangulate(IList<Point> points)
        {
            List<int> indices = new List<int>();
            if (points == null || points.Count < 3) return indices;

            // Create a linked list of vertex indices
            List<int> vertList = new List<int>(points.Count);
            if (IsCounterClockwise(points))
            {
                for (int i = 0; i < points.Count; i++) vertList.Add(i);
            }
            else
            {
                for (int i = 0; i < points.Count; i++) vertList.Add(points.Count - 1 - i);
            }

            // Loop until we have removed enough vertices
            while (vertList.Count > 3)
            {
                bool earFound = false;

                for (int i = 0; i < vertList.Count; i++)
                {
                    int iPrev = (i == 0) ? vertList.Count - 1 : i - 1;
                    int iNext = (i == vertList.Count - 1) ? 0 : i + 1;

                    int a = vertList[iPrev];
                    int b = vertList[i];
                    int c = vertList[iNext];

                    if (IsEar(a, b, c, points, vertList))
                    {
                        // Add triangle indices
                        indices.Add(a);
                        indices.Add(b);
                        indices.Add(c);

                        // Remove the ear vertex
                        vertList.RemoveAt(i);
                        earFound = true;
                        break;
                    }
                }

                if (!earFound)
                {
                    // Failed to find an ear (degenerate polygon or self-intersecting)
                    // Fallback: use triangle fan for remaining vertices
                    break;
                }
            }

            // Add the final triangle
            if (vertList.Count == 3)
            {
                indices.Add(vertList[0]);
                indices.Add(vertList[1]);
                indices.Add(vertList[2]);
            }

            return indices;
        }

        private static bool IsEar(int a, int b, int c, IList<Point> points, List<int> vertList)
        {
            Point A = points[a];
            Point B = points[b];
            Point C = points[c];

            // Check if the triangle is convex (not a reflex angle)
            if (CrossProduct(A, B, C) <= 0) return false;

            // Check if any other vertex is inside this triangle
            for (int i = 0; i < vertList.Count; i++)
            {
                int pIndex = vertList[i];
                if (pIndex == a || pIndex == b || pIndex == c) continue;

                if (IsPointInTriangle(points[pIndex], A, B, C))
                    return false;
            }

            return true;
        }

        private static bool IsCounterClockwise(IList<Point> points)
        {
            double sum = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Point p1 = points[i];
                Point p2 = points[(i + 1) % points.Count];
                sum += (p2.X - p1.X) * (p2.Y + p1.Y);
            }
            return sum < 0; // Negative sum means CCW for standard math coordinates
        }

        private static double CrossProduct(Point a, Point b, Point c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static bool IsPointInTriangle(Point p, Point a, Point b, Point c)
        {
            // Barycentric coordinate technique
            double v0x = c.X - a.X, v0y = c.Y - a.Y;
            double v1x = b.X - a.X, v1y = b.Y - a.Y;
            double v2x = p.X - a.X, v2y = p.Y - a.Y;

            double dot00 = v0x * v0x + v0y * v0y;
            double dot01 = v0x * v1x + v0y * v1y;
            double dot02 = v0x * v2x + v0y * v2y;
            double dot11 = v1x * v1x + v1y * v1y;
            double dot12 = v1x * v2x + v1y * v2y;

            double denom = dot00 * dot11 - dot01 * dot01;
            if (Math.Abs(denom) < 1e-10) return false; // Degenerate triangle

            double invDenom = 1 / denom;
            double u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            double v = (dot00 * dot12 - dot01 * dot02) * invDenom;

            return (u >= 0) && (v >= 0) && (u + v < 1);
        }
    }
}
