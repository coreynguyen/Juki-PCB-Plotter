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
        // Tolerance for numerical comparisons (handles floating point errors)
        private const double EPSILON = 1e-10;

        /// <summary>
        /// Triangulates a polygon using the Ear Clipping algorithm.
        /// NOTE: This method internally removes duplicate consecutive vertices.
        /// Use TriangulateWithCleanedPoints if you need the cleaned vertex list.
        /// </summary>
        /// <param name="points">The polygon vertices</param>
        /// <returns>List of triangle indices (3 per triangle) into the CLEANED point list</returns>
        public static List<int> Triangulate(IList<Point> points)
        {
            List<Point> cleanedPoints;
            return TriangulateWithCleanedPoints(points, out cleanedPoints);
        }

        /// <summary>
        /// Triangulates a polygon using the Ear Clipping algorithm, also returning the cleaned points.
        /// Use this when you need to add vertices to a mesh buffer, as the indices are into cleanedPoints.
        /// </summary>
        /// <param name="points">The polygon vertices</param>
        /// <param name="cleanedPoints">Output: the cleaned polygon vertices (duplicates removed)</param>
        /// <returns>List of triangle indices (3 per triangle) into cleanedPoints</returns>
        public static List<int> TriangulateWithCleanedPoints(IList<Point> points, out List<Point> cleanedPoints)
        {
            List<int> indices = new List<int>();
            cleanedPoints = new List<Point>();
            if (points == null || points.Count < 3) return indices;

            // Pre-process: remove duplicate consecutive vertices that can cause issues
            cleanedPoints = RemoveDuplicateVertices(points);
            if (cleanedPoints.Count < 3) return indices;

            // Create a linked list of vertex indices
            List<int> vertList = new List<int>(cleanedPoints.Count);
            if (IsCounterClockwise(cleanedPoints))
            {
                for (int i = 0; i < cleanedPoints.Count; i++) vertList.Add(i);
            }
            else
            {
                for (int i = 0; i < cleanedPoints.Count; i++) vertList.Add(cleanedPoints.Count - 1 - i);
            }

            // Track failed ear searches to detect infinite loops
            int failedSearches = 0;
            int maxFailedSearches = vertList.Count * 2; // Safety limit

            // Loop until we have removed enough vertices
            while (vertList.Count > 3 && failedSearches < maxFailedSearches)
            {
                bool earFound = false;

                for (int i = 0; i < vertList.Count; i++)
                {
                    int iPrev = (i == 0) ? vertList.Count - 1 : i - 1;
                    int iNext = (i == vertList.Count - 1) ? 0 : i + 1;

                    int a = vertList[iPrev];
                    int b = vertList[i];
                    int c = vertList[iNext];

                    if (IsEar(a, b, c, cleanedPoints, vertList))
                    {
                        // Add triangle indices
                        indices.Add(a);
                        indices.Add(b);
                        indices.Add(c);

                        // Remove the ear vertex
                        vertList.RemoveAt(i);
                        earFound = true;
                        failedSearches = 0; // Reset counter on success
                        break;
                    }
                }

                if (!earFound)
                {
                    failedSearches++;

                    // Try with relaxed constraints (allow slightly reflex vertices)
                    if (failedSearches == 1)
                    {
                        // First failure - try to find a "nearly convex" ear
                        for (int i = 0; i < vertList.Count; i++)
                        {
                            int iPrev = (i == 0) ? vertList.Count - 1 : i - 1;
                            int iNext = (i == vertList.Count - 1) ? 0 : i + 1;

                            int a = vertList[iPrev];
                            int b = vertList[i];
                            int c = vertList[iNext];

                            if (IsEarRelaxed(a, b, c, cleanedPoints, vertList))
                            {
                                indices.Add(a);
                                indices.Add(b);
                                indices.Add(c);
                                vertList.RemoveAt(i);
                                earFound = true;
                                failedSearches = 0;
                                break;
                            }
                        }
                    }

                    if (!earFound && failedSearches >= maxFailedSearches)
                    {
                        // Still no ear found after many attempts - polygon is likely degenerate
                        // or self-intersecting. Return empty list to signal failure,
                        // allowing the caller (OpenGLCanvas) to use outline rendering fallback.
                        return new List<int>();
                    }
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

        /// <summary>
        /// Remove consecutive duplicate vertices that can cause triangulation issues.
        /// </summary>
        private static List<Point> RemoveDuplicateVertices(IList<Point> points)
        {
            var result = new List<Point>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                var current = points[i];
                var next = points[(i + 1) % points.Count];

                // Only add if not a duplicate of the next vertex
                double dx = current.X - next.X;
                double dy = current.Y - next.Y;
                if (dx * dx + dy * dy > EPSILON * EPSILON)
                {
                    result.Add(current);
                }
            }
            return result;
        }

        /// <summary>
        /// Fallback triangulation using convex hull approach.
        /// Better than triangle fan for self-intersecting polygons.
        /// </summary>
        private static void TriangulateConvexHullFallback(List<int> vertList, IList<Point> points, List<int> indices)
        {
            if (vertList.Count < 3) return;

            // Find the vertex with minimum Y (and minimum X as tiebreaker) - guaranteed to be convex
            int minIdx = 0;
            Point minPt = points[vertList[0]];
            for (int i = 1; i < vertList.Count; i++)
            {
                Point pt = points[vertList[i]];
                if (pt.Y < minPt.Y || (Math.Abs(pt.Y - minPt.Y) < EPSILON && pt.X < minPt.X))
                {
                    minIdx = i;
                    minPt = pt;
                }
            }

            // Use this vertex as the fan center - it's guaranteed to be on the convex hull
            int centerVert = vertList[minIdx];

            // Create triangles from the center to each edge
            for (int i = 0; i < vertList.Count; i++)
            {
                if (i == minIdx) continue;
                int nextI = (i + 1) % vertList.Count;
                if (nextI == minIdx) nextI = (nextI + 1) % vertList.Count;
                if (nextI == minIdx || i == nextI) continue;

                // Only add triangles with positive area (skip degenerate/inverted ones)
                double area = CrossProduct(points[centerVert], points[vertList[i]], points[vertList[nextI]]);
                if (area > EPSILON)
                {
                    indices.Add(centerVert);
                    indices.Add(vertList[i]);
                    indices.Add(vertList[nextI]);
                }
            }
        }

        private static bool IsEar(int a, int b, int c, IList<Point> points, List<int> vertList)
        {
            Point A = points[a];
            Point B = points[b];
            Point C = points[c];

            // Check if the triangle is convex (not a reflex angle)
            double cross = CrossProduct(A, B, C);
            if (cross <= EPSILON) return false;

            // OPTIMIZATION: Calculate bounding box of the triangle for fast rejection
            double minX = Math.Min(A.X, Math.Min(B.X, C.X));
            double maxX = Math.Max(A.X, Math.Max(B.X, C.X));
            double minY = Math.Min(A.Y, Math.Min(B.Y, C.Y));
            double maxY = Math.Max(A.Y, Math.Max(B.Y, C.Y));

            // Check if any other vertex is inside this triangle
            for (int i = 0; i < vertList.Count; i++)
            {
                int pIndex = vertList[i];
                if (pIndex == a || pIndex == b || pIndex == c) continue;

                Point p = points[pIndex];

                // FAST REJECTION: If point is outside bounding box, skip expensive math
                if (p.X < minX - EPSILON || p.X > maxX + EPSILON ||
                    p.Y < minY - EPSILON || p.Y > maxY + EPSILON)
                    continue;

                if (IsPointInTriangle(p, A, B, C))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Relaxed ear test that accepts nearly-flat triangles.
        /// Used as fallback when strict ear test fails.
        /// </summary>
        private static bool IsEarRelaxed(int a, int b, int c, IList<Point> points, List<int> vertList)
        {
            Point A = points[a];
            Point B = points[b];
            Point C = points[c];

            // Accept nearly-flat triangles (cross product close to zero)
            double cross = CrossProduct(A, B, C);
            if (cross < -EPSILON * 100) return false; // Only reject clearly reflex angles

            // Still check for vertices inside
            for (int i = 0; i < vertList.Count; i++)
            {
                int pIndex = vertList[i];
                if (pIndex == a || pIndex == b || pIndex == c) continue;

                Point p = points[pIndex];
                if (IsPointInTriangleRelaxed(p, A, B, C))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Relaxed point-in-triangle test with tolerance.
        /// </summary>
        private static bool IsPointInTriangleRelaxed(Point p, Point a, Point b, Point c)
        {
            double v0x = c.X - a.X, v0y = c.Y - a.Y;
            double v1x = b.X - a.X, v1y = b.Y - a.Y;
            double v2x = p.X - a.X, v2y = p.Y - a.Y;

            double dot00 = v0x * v0x + v0y * v0y;
            double dot01 = v0x * v1x + v0y * v1y;
            double dot02 = v0x * v2x + v0y * v2y;
            double dot11 = v1x * v1x + v1y * v1y;
            double dot12 = v1x * v2x + v1y * v2y;

            double denom = dot00 * dot11 - dot01 * dot01;
            if (Math.Abs(denom) < EPSILON) return false;

            double invDenom = 1 / denom;
            double u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            double v = (dot00 * dot12 - dot01 * dot02) * invDenom;

            // Use relaxed tolerance for "inside" check
            double tolerance = 0.001;
            return (u >= -tolerance) && (v >= -tolerance) && (u + v < 1 + tolerance);
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
