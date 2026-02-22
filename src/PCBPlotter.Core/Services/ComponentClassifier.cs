using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Features extracted from a cluster of pads for component classification.
    /// </summary>
    public class PadClusterFeatures
    {
        public int PadCount { get; set; }
        public Rect BoundingBox { get; set; }
        public Point Centroid { get; set; }
        public double DominantPitch { get; set; }
        public double PadAspectRatio { get; set; }
        public bool SymmetricX { get; set; }
        public bool SymmetricY { get; set; }
        public bool AllCircular { get; set; }
        public bool IsGrid { get; set; }
        public int RowCount { get; set; }
        public int ColCount { get; set; }
        public bool HasLargePad { get; set; }
        public int SidesWithPads { get; set; }
        public bool IsHShape { get; set; }
        public List<PadInfo> Pads { get; set; } = new List<PadInfo>();

        // New features for SMD/THT detection
        public double AveragePadDiameter { get; set; }
        public double MinPadSize { get; set; }
        public double MaxPadSize { get; set; }
        public MountType DetectedMountType { get; set; }

        // Lead bounding boxes (simplified from pad clusters)
        public List<Rect> LeadBounds { get; set; } = new List<Rect>();
    }

    /// <summary>
    /// Simplified pad info for analysis.
    /// </summary>
    public class PadInfo
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsCircular { get; set; }
        public double Area { get; set; }
        public double Diameter => Math.Max(Width, Height);
        public int LeadGroupIndex { get; set; } = -1; // For grouping pads into leads
    }

    /// <summary>
    /// Result of component classification.
    /// </summary>
    public class ClassificationResult
    {
        public PartClass PartClass { get; set; }
        public string SuggestedName { get; set; }
        public bool HasPolarity { get; set; }
        public int Pin1Index { get; set; }
        public string Description { get; set; }
        public MountType MountType { get; set; }
        public SmdPackageDefinition MatchedPackage { get; set; }
        public string SuggestedDesignator { get; set; }
    }

    /// <summary>
    /// Extracts features from pad clusters and classifies components.
    /// Implements a decision-tree classifier based on pad geometry.
    /// </summary>
    public static class ComponentClassifier
    {
        private const double TOLERANCE = 0.05; // mm tolerance for comparisons
        private const double THT_MIN_DIAMETER = 0.6; // mm - minimum diameter for through-hole pads
        private const double THT_TYPICAL_DIAMETER = 0.9; // mm - typical THT pad diameter

        /// <summary>
        /// Extract features from a set of gerber primitives (pads).
        /// Uses rasterization and connected component detection to find logical pads.
        /// </summary>
        public static PadClusterFeatures ExtractFeatures(List<GerberPrimitive> primitives)
        {
            var features = new PadClusterFeatures();
            if (primitives == null || primitives.Count == 0)
                return features;

            // Filter to dark primitives only
            var darkPrimitives = primitives.Where(p => p.IsDark).ToList();
            if (darkPrimitives.Count == 0) return features;

            // Step 1: Find overall bounding box
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var prim in darkPrimitives)
            {
                var bounds = prim.GetBounds();
                minX = Math.Min(minX, bounds.Left);
                minY = Math.Min(minY, bounds.Top);
                maxX = Math.Max(maxX, bounds.Right);
                maxY = Math.Max(maxY, bounds.Bottom);
            }

            double totalWidth = maxX - minX;
            double totalHeight = maxY - minY;
            if (totalWidth <= 0 || totalHeight <= 0) return features;

            // Step 2: Create rasterization grid
            // Use ~100 pixels per mm for good resolution, cap at reasonable size
            const double pixelsPerMm = 100.0;
            const int maxGridSize = 2000;

            int gridWidth = Math.Min(maxGridSize, Math.Max(10, (int)(totalWidth * pixelsPerMm) + 2));
            int gridHeight = Math.Min(maxGridSize, Math.Max(10, (int)(totalHeight * pixelsPerMm) + 2));

            double scaleX = (gridWidth - 1) / totalWidth;
            double scaleY = (gridHeight - 1) / totalHeight;
            double scale = Math.Min(scaleX, scaleY);

            bool[,] grid = new bool[gridWidth, gridHeight];

            // Step 3: Rasterize each primitive onto the grid
            foreach (var prim in darkPrimitives)
            {
                RasterizePrimitive(grid, prim, minX, minY, scale, gridWidth, gridHeight);
            }

            // Step 4: Find connected components using flood fill
            int[,] labels = new int[gridWidth, gridHeight];
            int nextLabel = 1;
            var componentBounds = new Dictionary<int, (int minX, int minY, int maxX, int maxY, int pixelCount)>();

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    if (grid[x, y] && labels[x, y] == 0)
                    {
                        // Flood fill this component
                        var bounds = FloodFill(grid, labels, x, y, nextLabel, gridWidth, gridHeight);
                        componentBounds[nextLabel] = bounds;
                        nextLabel++;
                    }
                }
            }

            // Step 5: Convert components to PadInfo
            foreach (var kvp in componentBounds)
            {
                var (cMinX, cMinY, cMaxX, cMaxY, pixelCount) = kvp.Value;

                // Convert back to world coordinates
                double padLeft = minX + cMinX / scale;
                double padTop = minY + cMinY / scale;
                double padRight = minX + (cMaxX + 1) / scale;
                double padBottom = minY + (cMaxY + 1) / scale;

                double padW = padRight - padLeft;
                double padH = padBottom - padTop;
                double padCX = (padLeft + padRight) / 2;
                double padCY = (padTop + padBottom) / 2;

                // Determine if circular by comparing pixel count to what a circle vs rectangle would have
                // Rectangle fills 100% of bounding box, circle fills ~78.5% (pi/4)
                int boundingPixels = (cMaxX - cMinX + 1) * (cMaxY - cMinY + 1);
                double fillRatio = boundingPixels > 0 ? (double)pixelCount / boundingPixels : 1.0;

                // Circle has fill ratio ~0.785, rectangle has ~1.0
                // Also require aspect ratio to be very close to 1.0
                double aspectRatio = Math.Min(padW, padH) / Math.Max(padW, padH);
                bool isCircular = aspectRatio > 0.95 && fillRatio < 0.88 && fillRatio > 0.70;

                features.Pads.Add(new PadInfo
                {
                    X = padCX,
                    Y = padCY,
                    Width = padW,
                    Height = padH,
                    IsCircular = isCircular,
                    Area = padW * padH
                });
            }

            features.PadCount = features.Pads.Count;
            if (features.PadCount == 0) return features;

            // Bounding box (from pad centers/extents)
            double bbMinX = features.Pads.Min(p => p.X - p.Width / 2);
            double bbMinY = features.Pads.Min(p => p.Y - p.Height / 2);
            double bbMaxX = features.Pads.Max(p => p.X + p.Width / 2);
            double bbMaxY = features.Pads.Max(p => p.Y + p.Height / 2);
            features.BoundingBox = new Rect(bbMinX, bbMinY, bbMaxX - bbMinX, bbMaxY - bbMinY);

            // Centroid
            features.Centroid = new Point(
                features.Pads.Average(p => p.X),
                features.Pads.Average(p => p.Y));

            // All circular?
            features.AllCircular = features.Pads.All(p => p.IsCircular);

            // Pad size statistics
            features.AveragePadDiameter = features.Pads.Average(p => p.Diameter);
            features.MinPadSize = features.Pads.Min(p => Math.Min(p.Width, p.Height));
            features.MaxPadSize = features.Pads.Max(p => Math.Max(p.Width, p.Height));

            // Detect mount type (SMD vs Through-hole)
            features.DetectedMountType = DetectMountType(features);

            // Pad aspect ratio (dominant)
            var avgW = features.Pads.Average(p => p.Width);
            var avgH = features.Pads.Average(p => p.Height);
            features.PadAspectRatio = avgH > TOLERANCE ? avgW / avgH : 1.0;

            // Has large pad? (one pad > 2x average area)
            double avgArea = features.Pads.Average(p => p.Area);
            features.HasLargePad = features.Pads.Any(p => p.Area > avgArea * 2);

            // Dominant pitch - most common distance between adjacent pad centers
            features.DominantPitch = ComputeDominantPitch(features.Pads);

            // Symmetry checks
            features.SymmetricX = CheckSymmetry(features.Pads, true, features.Centroid);
            features.SymmetricY = CheckSymmetry(features.Pads, false, features.Centroid);

            // Grid detection
            DetectGrid(features);

            // Sides with pads (for QFP detection)
            features.SidesWithPads = CountSidesWithPads(features);

            // H-shape detection (for tact switches)
            features.IsHShape = DetectHShape(features);

            // Compute simplified lead bounding boxes
            features.LeadBounds = ComputeLeadBoundingBoxes(features);

            return features;
        }

        /// <summary>
        /// Detect mount type based on pad characteristics.
        /// Through-hole pads are typically circular and larger (>0.6mm diameter).
        /// SMD pads are typically rectangular and smaller.
        /// </summary>
        private static MountType DetectMountType(PadClusterFeatures features)
        {
            // All circular pads with diameter >= THT minimum suggests through-hole
            if (features.AllCircular && features.AveragePadDiameter >= THT_MIN_DIAMETER)
            {
                // Additional check: THT pads tend to be uniform in size
                double sizeVariance = features.Pads.Select(p => p.Diameter).Max() -
                                      features.Pads.Select(p => p.Diameter).Min();
                if (sizeVariance < features.AveragePadDiameter * 0.3)
                    return MountType.ThroughHole;
            }

            // Mixed circular/rectangular or small pads suggest SMD
            if (!features.AllCircular || features.AveragePadDiameter < THT_MIN_DIAMETER)
                return MountType.SMD;

            // Large circular pads in a grid pattern (BGA) are SMD
            if (features.IsGrid && features.PadCount > 9)
                return MountType.SMD;

            return MountType.Unknown;
        }

        /// <summary>
        /// Compute simplified lead bounding boxes from pad clusters.
        /// Groups adjacent pads and creates bounding rectangles for each lead.
        /// </summary>
        private static List<Rect> ComputeLeadBoundingBoxes(PadClusterFeatures features)
        {
            var leadBounds = new List<Rect>();
            if (features.Pads.Count == 0) return leadBounds;

            // For simple 2-pad components, each pad is a lead
            if (features.PadCount <= 2)
            {
                foreach (var pad in features.Pads)
                {
                    leadBounds.Add(new Rect(
                        pad.X - pad.Width / 2,
                        pad.Y - pad.Height / 2,
                        pad.Width,
                        pad.Height));
                }
                return leadBounds;
            }

            // For multi-pad components, group pads by proximity
            // Use clustering based on dominant pitch
            double clusterThreshold = features.DominantPitch > 0 ?
                features.DominantPitch * 0.4 : features.MaxPadSize * 1.5;

            var assigned = new bool[features.Pads.Count];
            int leadIndex = 0;

            for (int i = 0; i < features.Pads.Count; i++)
            {
                if (assigned[i]) continue;

                // Start a new lead group
                var group = new List<PadInfo> { features.Pads[i] };
                assigned[i] = true;
                features.Pads[i].LeadGroupIndex = leadIndex;

                // Find all pads within cluster threshold
                for (int j = i + 1; j < features.Pads.Count; j++)
                {
                    if (assigned[j]) continue;

                    // Check if pad j is close to any pad in the group
                    foreach (var gPad in group)
                    {
                        double dist = Math.Sqrt(
                            Math.Pow(features.Pads[j].X - gPad.X, 2) +
                            Math.Pow(features.Pads[j].Y - gPad.Y, 2));

                        if (dist < clusterThreshold)
                        {
                            group.Add(features.Pads[j]);
                            assigned[j] = true;
                            features.Pads[j].LeadGroupIndex = leadIndex;
                            break;
                        }
                    }
                }

                // Compute bounding box for this lead group
                double minX = group.Min(p => p.X - p.Width / 2);
                double minY = group.Min(p => p.Y - p.Height / 2);
                double maxX = group.Max(p => p.X + p.Width / 2);
                double maxY = group.Max(p => p.Y + p.Height / 2);

                leadBounds.Add(new Rect(minX, minY, maxX - minX, maxY - minY));
                leadIndex++;
            }

            return leadBounds;
        }

        /// <summary>
        /// Classify a pad cluster into a component type using decision-tree rules.
        /// </summary>
        public static ClassificationResult Classify(PadClusterFeatures features)
        {
            if (features.PadCount == 0)
                return new ClassificationResult
                {
                    PartClass = PartClass.Other,
                    SuggestedName = "UNKNOWN",
                    Description = "No pads detected",
                    MountType = MountType.Unknown
                };

            ClassificationResult result;

            // Decision tree
            if (features.PadCount == 2)
                result = ClassifyChip(features);
            else if (features.PadCount == 3)
                result = ClassifySOT3(features);
            else if (features.PadCount >= 3 && features.PadCount <= 6 && features.HasLargePad)
                result = ClassifySOTLarge(features);
            else if (features.PadCount == 4 && features.IsHShape)
                result = ClassifySwitch(features);
            else if (features.PadCount > 9 && features.AllCircular && features.IsGrid)
                result = ClassifyBGA(features);
            else if (features.PadCount >= 16 && features.SidesWithPads == 4)
                result = ClassifyQuadIC(features);
            else if (features.PadCount >= 4 && features.PadCount % 2 == 0 && HasTwoParallelRows(features))
                result = ClassifyDualRowIC(features);
            else if (features.PadCount >= 4 && HasSingleRow(features))
                result = ClassifyConnector(features);
            else
            {
                // Fallback
                result = new ClassificationResult
                {
                    PartClass = PartClass.Other,
                    SuggestedName = $"PKG{features.PadCount}",
                    HasPolarity = false,
                    Pin1Index = 0,
                    Description = $"{features.PadCount}-pad component"
                };
            }

            // Set mount type from detected type
            result.MountType = features.DetectedMountType;

            // Try to match to known package
            result.MatchedPackage = MatchToKnownPackage(features, result);
            if (result.MatchedPackage != null)
            {
                result.SuggestedName = result.MatchedPackage.Name;
            }

            // Set suggested designator
            result.SuggestedDesignator = SmdPackageDatabase.GetDesignatorPrefix(
                result.PartClass, result.SuggestedName);

            return result;
        }

        /// <summary>
        /// Try to match detected features to a known SMD package.
        /// </summary>
        private static SmdPackageDefinition MatchToKnownPackage(
            PadClusterFeatures features, ClassificationResult classification)
        {
            // For 2-pad chips, use pad distance
            if (features.PadCount == 2)
            {
                double padDistance = Math.Sqrt(
                    Math.Pow(features.Pads[0].X - features.Pads[1].X, 2) +
                    Math.Pow(features.Pads[0].Y - features.Pads[1].Y, 2));
                return SmdPackageDatabase.FindChipByPadDistance(padDistance);
            }

            // For other packages, try dimension matching
            var matches = SmdPackageDatabase.FindByDimensions(
                features.BoundingBox.Width,
                features.BoundingBox.Height,
                0.25); // 25% tolerance

            // Filter by pin count if reasonable
            var pinMatches = matches.Where(p =>
                p.PinCount == 0 || // Variable pin count
                Math.Abs(p.PinCount - features.PadCount) <= features.PadCount * 0.1).ToList();

            if (pinMatches.Count > 0)
                return pinMatches.First();

            if (matches.Count > 0)
                return matches.First();

            return null;
        }

        #region Classification Methods

        private static ClassificationResult ClassifyChip(PadClusterFeatures f)
        {
            // Check if one pad is larger (polarized - diode/tantalum)
            bool polarized = f.Pads.Count == 2 &&
                Math.Abs(f.Pads[0].Area - f.Pads[1].Area) > f.Pads.Average(p => p.Area) * 0.15;

            double bodyW = f.BoundingBox.Width;
            double bodyL = f.BoundingBox.Height;
            string size = EstimateChipSize(bodyW, bodyL);

            return new ClassificationResult
            {
                PartClass = PartClass.Chip,
                SuggestedName = size,
                HasPolarity = polarized,
                Pin1Index = polarized ? FindLargerPadIndex(f.Pads) : -1,
                Description = polarized ? "Polarized 2-terminal (Diode/Tantalum)" : "Passive chip component"
            };
        }

        private static ClassificationResult ClassifySOT3(PadClusterFeatures f)
        {
            return new ClassificationResult
            {
                PartClass = PartClass.SOT,
                SuggestedName = "SOT23",
                HasPolarity = true,
                Pin1Index = FindPin1ByTopLeft(f.Pads),
                Description = "SOT-23 transistor/MOSFET"
            };
        }

        private static ClassificationResult ClassifySOTLarge(PadClusterFeatures f)
        {
            string name = f.PadCount <= 4 ? "SOT223" : $"SOT{f.PadCount}";
            return new ClassificationResult
            {
                PartClass = PartClass.SOT,
                SuggestedName = name,
                HasPolarity = true,
                Pin1Index = FindPin1ByTopLeft(f.Pads),
                Description = $"SOT package with {f.PadCount} pads"
            };
        }

        private static ClassificationResult ClassifySwitch(PadClusterFeatures f)
        {
            return new ClassificationResult
            {
                PartClass = PartClass.Other,
                SuggestedName = "TACT_SW",
                HasPolarity = false,
                Pin1Index = -1,
                Description = "Tactile switch (H-shape)"
            };
        }

        private static ClassificationResult ClassifyBGA(PadClusterFeatures f)
        {
            string name = $"BGA{f.PadCount}";
            if (f.RowCount > 0 && f.ColCount > 0)
                name = $"BGA{f.PadCount}_{f.ColCount}x{f.RowCount}";

            return new ClassificationResult
            {
                PartClass = PartClass.BGA,
                SuggestedName = name,
                HasPolarity = true,
                Pin1Index = FindPin1ByTopLeft(f.Pads),
                Description = $"BGA {f.ColCount}x{f.RowCount} grid"
            };
        }

        private static ClassificationResult ClassifyQuadIC(PadClusterFeatures f)
        {
            int pinsPerSide = f.PadCount / 4;
            string name = $"QFP{f.PadCount}";

            return new ClassificationResult
            {
                PartClass = PartClass.QFP,
                SuggestedName = name,
                HasPolarity = true,
                Pin1Index = FindPin1ByTopLeft(f.Pads),
                Description = $"Quad flat package {pinsPerSide} pins/side"
            };
        }

        private static ClassificationResult ClassifyDualRowIC(PadClusterFeatures f)
        {
            int totalPins = f.PadCount;
            string prefix = totalPins <= 8 ? "SOIC" : (totalPins <= 20 ? "TSSOP" : "SOP");
            string name = $"{prefix}{totalPins}";

            return new ClassificationResult
            {
                PartClass = PartClass.SOP,
                SuggestedName = name,
                HasPolarity = true,
                Pin1Index = FindPin1ByTopLeft(f.Pads),
                Description = $"Dual-row IC with {totalPins} pins"
            };
        }

        private static ClassificationResult ClassifyConnector(PadClusterFeatures f)
        {
            return new ClassificationResult
            {
                PartClass = PartClass.Connector,
                SuggestedName = $"CONN{f.PadCount}",
                HasPolarity = true,
                Pin1Index = FindPin1ByTopLeft(f.Pads),
                Description = $"Connector/header with {f.PadCount} pins"
            };
        }

        #endregion

        #region Feature Helpers

        /// <summary>
        /// Rasterize a Gerber primitive onto a boolean grid.
        /// </summary>
        private static void RasterizePrimitive(bool[,] grid, GerberPrimitive prim,
            double offsetX, double offsetY, double scale, int gridWidth, int gridHeight)
        {
            var bounds = prim.GetBounds();

            // Convert world bounds to grid coordinates
            int gxMin = Math.Max(0, (int)((bounds.Left - offsetX) * scale));
            int gyMin = Math.Max(0, (int)((bounds.Top - offsetY) * scale));
            int gxMax = Math.Min(gridWidth - 1, (int)((bounds.Right - offsetX) * scale) + 1);
            int gyMax = Math.Min(gridHeight - 1, (int)((bounds.Bottom - offsetY) * scale) + 1);

            double cx = prim.X;
            double cy = prim.Y;
            double halfW = prim.Width / 2;
            double halfH = prim.Height / 2;

            for (int gy = gyMin; gy <= gyMax; gy++)
            {
                for (int gx = gxMin; gx <= gxMax; gx++)
                {
                    // Convert grid coord back to world coord (center of pixel)
                    double wx = offsetX + (gx + 0.5) / scale;
                    double wy = offsetY + (gy + 0.5) / scale;

                    bool inside = false;

                    switch (prim.Type)
                    {
                        case GerberPrimitiveType.Circle:
                            // Circle: check distance from center
                            double dx = wx - cx;
                            double dy = wy - cy;
                            inside = (dx * dx + dy * dy) <= (halfW * halfW);
                            break;

                        case GerberPrimitiveType.Rectangle:
                        case GerberPrimitiveType.Flash:
                            // Rectangle: check bounds
                            inside = wx >= (cx - halfW) && wx <= (cx + halfW) &&
                                     wy >= (cy - halfH) && wy <= (cy + halfH);
                            break;

                        case GerberPrimitiveType.Obround:
                            // Obround: rectangle with semicircle ends
                            if (prim.Width > prim.Height)
                            {
                                // Horizontal obround
                                double r = halfH;
                                double rectHalfW = halfW - r;
                                if (wx >= (cx - rectHalfW) && wx <= (cx + rectHalfW) &&
                                    wy >= (cy - r) && wy <= (cy + r))
                                {
                                    inside = true;
                                }
                                else
                                {
                                    // Check semicircle ends
                                    double d1 = Math.Pow(wx - (cx - rectHalfW), 2) + Math.Pow(wy - cy, 2);
                                    double d2 = Math.Pow(wx - (cx + rectHalfW), 2) + Math.Pow(wy - cy, 2);
                                    inside = d1 <= r * r || d2 <= r * r;
                                }
                            }
                            else
                            {
                                // Vertical obround
                                double r = halfW;
                                double rectHalfH = halfH - r;
                                if (wy >= (cy - rectHalfH) && wy <= (cy + rectHalfH) &&
                                    wx >= (cx - r) && wx <= (cx + r))
                                {
                                    inside = true;
                                }
                                else
                                {
                                    double d1 = Math.Pow(wy - (cy - rectHalfH), 2) + Math.Pow(wx - cx, 2);
                                    double d2 = Math.Pow(wy - (cy + rectHalfH), 2) + Math.Pow(wx - cx, 2);
                                    inside = d1 <= r * r || d2 <= r * r;
                                }
                            }
                            break;

                        case GerberPrimitiveType.Line:
                            // Line: check distance to line segment
                            if (prim.Points != null && prim.Points.Count >= 2)
                            {
                                double lineWidth = Math.Max(prim.Width, prim.Height);
                                if (lineWidth <= 0) lineWidth = 0.1;
                                double halfLine = lineWidth / 2;

                                for (int i = 0; i < prim.Points.Count - 1; i++)
                                {
                                    var p1 = prim.Points[i];
                                    var p2 = prim.Points[i + 1];
                                    double dist = DistanceToSegment(wx, wy, p1.X, p1.Y, p2.X, p2.Y);
                                    if (dist <= halfLine)
                                    {
                                        inside = true;
                                        break;
                                    }
                                }
                            }
                            break;

                        case GerberPrimitiveType.Polygon:
                        case GerberPrimitiveType.Contour:
                            // Polygon: point-in-polygon test
                            if (prim.Points != null && prim.Points.Count >= 3)
                            {
                                inside = PointInPolygon(wx, wy, prim.Points);
                            }
                            break;

                        default:
                            // Default: use bounding box
                            inside = bounds.Contains(new Point(wx, wy));
                            break;
                    }

                    if (inside)
                    {
                        grid[gx, gy] = true;
                    }
                }
            }
        }

        private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double lengthSq = dx * dx + dy * dy;

            if (lengthSq == 0) return Math.Sqrt(Math.Pow(px - x1, 2) + Math.Pow(py - y1, 2));

            double t = Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / lengthSq));
            double projX = x1 + t * dx;
            double projY = y1 + t * dy;

            return Math.Sqrt(Math.Pow(px - projX, 2) + Math.Pow(py - projY, 2));
        }

        private static bool PointInPolygon(double x, double y, List<Point> polygon)
        {
            bool inside = false;
            int n = polygon.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = polygon[i].X, yi = polygon[i].Y;
                double xj = polygon[j].X, yj = polygon[j].Y;

                if (((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// Flood fill to find a connected component and compute its bounds.
        /// Returns (minX, minY, maxX, maxY, pixelCount).
        /// </summary>
        private static (int minX, int minY, int maxX, int maxY, int pixelCount) FloodFill(
            bool[,] grid, int[,] labels, int startX, int startY, int label, int width, int height)
        {
            int minX = startX, maxX = startX;
            int minY = startY, maxY = startY;
            int count = 0;

            var stack = new Stack<(int x, int y)>();
            stack.Push((startX, startY));

            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();

                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                if (!grid[x, y] || labels[x, y] != 0)
                    continue;

                labels[x, y] = label;
                count++;

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);

                // 4-connectivity
                stack.Push((x + 1, y));
                stack.Push((x - 1, y));
                stack.Push((x, y + 1));
                stack.Push((x, y - 1));
            }

            return (minX, minY, maxX, maxY, count);
        }

        private static double ComputeDominantPitch(List<PadInfo> pads)
        {
            if (pads.Count < 2) return 0;

            var distances = new List<double>();
            for (int i = 0; i < pads.Count; i++)
            {
                double minDist = double.MaxValue;
                for (int j = 0; j < pads.Count; j++)
                {
                    if (i == j) continue;
                    double d = Math.Sqrt(Math.Pow(pads[i].X - pads[j].X, 2) +
                                         Math.Pow(pads[i].Y - pads[j].Y, 2));
                    if (d < minDist) minDist = d;
                }
                if (minDist < double.MaxValue)
                    distances.Add(Math.Round(minDist, 3));
            }

            if (distances.Count == 0) return 0;

            // Find most frequent distance (dominant pitch)
            return distances.GroupBy(d => d)
                .OrderByDescending(g => g.Count())
                .First().Key;
        }

        private static bool CheckSymmetry(List<PadInfo> pads, bool xAxis, Point centroid)
        {
            foreach (var pad in pads)
            {
                double mirroredX = xAxis ? 2 * centroid.X - pad.X : pad.X;
                double mirroredY = xAxis ? pad.Y : 2 * centroid.Y - pad.Y;

                bool found = pads.Any(p =>
                    Math.Abs(p.X - mirroredX) < TOLERANCE * 2 &&
                    Math.Abs(p.Y - mirroredY) < TOLERANCE * 2);
                if (!found) return false;
            }
            return true;
        }

        private static void DetectGrid(PadClusterFeatures f)
        {
            if (f.PadCount < 4) return;

            // Cluster X and Y coordinates
            var xClusters = ClusterValues(f.Pads.Select(p => p.X).ToList());
            var yClusters = ClusterValues(f.Pads.Select(p => p.Y).ToList());

            f.ColCount = xClusters.Count;
            f.RowCount = yClusters.Count;

            // It's a grid if rows*cols approximately equals pad count
            f.IsGrid = f.ColCount >= 2 && f.RowCount >= 2 &&
                        Math.Abs(f.ColCount * f.RowCount - f.PadCount) <= f.PadCount * 0.1;
        }

        private static List<double> ClusterValues(List<double> values)
        {
            if (values.Count == 0) return new List<double>();
            var sorted = values.OrderBy(v => v).ToList();
            var clusters = new List<double> { sorted[0] };

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - clusters.Last() > TOLERANCE * 4)
                    clusters.Add(sorted[i]);
            }
            return clusters;
        }

        private static int CountSidesWithPads(PadClusterFeatures f)
        {
            if (f.PadCount < 4) return 0;
            var bb = f.BoundingBox;
            double margin = Math.Min(bb.Width, bb.Height) * 0.3;

            int sides = 0;
            if (f.Pads.Any(p => p.X - p.Width / 2 <= bb.Left + margin)) sides++;   // Left
            if (f.Pads.Any(p => p.X + p.Width / 2 >= bb.Right - margin)) sides++;  // Right
            if (f.Pads.Any(p => p.Y - p.Height / 2 <= bb.Top + margin)) sides++;   // Top
            if (f.Pads.Any(p => p.Y + p.Height / 2 >= bb.Bottom - margin)) sides++;// Bottom
            return sides;
        }

        private static bool DetectHShape(PadClusterFeatures f)
        {
            if (f.PadCount != 4) return false;

            // H-shape: two pairs of pads with short intra-pair distance and long inter-pair distance
            var dists = new List<double>();
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                {
                    double d = Math.Sqrt(Math.Pow(f.Pads[i].X - f.Pads[j].X, 2) +
                                         Math.Pow(f.Pads[i].Y - f.Pads[j].Y, 2));
                    dists.Add(d);
                }

            dists.Sort();
            // In an H-shape: 2 short distances, 2 medium, 2 long
            if (dists.Count == 6 && dists[1] > 0)
            {
                double ratio = dists[4] / dists[1];
                return ratio > 1.5; // Long distances are significantly longer than short
            }
            return false;
        }

        private static bool HasTwoParallelRows(PadClusterFeatures f)
        {
            var xClusters = ClusterValues(f.Pads.Select(p => p.X).ToList());
            var yClusters = ClusterValues(f.Pads.Select(p => p.Y).ToList());

            // Two columns with many rows, or two rows with many columns
            return (xClusters.Count == 2 && yClusters.Count >= 2) ||
                   (yClusters.Count == 2 && xClusters.Count >= 2);
        }

        private static bool HasSingleRow(PadClusterFeatures f)
        {
            var xClusters = ClusterValues(f.Pads.Select(p => p.X).ToList());
            var yClusters = ClusterValues(f.Pads.Select(p => p.Y).ToList());
            return xClusters.Count == 1 || yClusters.Count == 1;
        }

        /// <summary>
        /// Find pin 1 by top-left sort (min X+Y sum).
        /// </summary>
        private static int FindPin1ByTopLeft(List<PadInfo> pads)
        {
            if (pads.Count == 0) return -1;
            int bestIdx = 0;
            double bestSum = double.MaxValue;
            for (int i = 0; i < pads.Count; i++)
            {
                double sum = pads[i].X + pads[i].Y;
                if (sum < bestSum)
                {
                    bestSum = sum;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private static int FindLargerPadIndex(List<PadInfo> pads)
        {
            if (pads.Count < 2) return 0;
            return pads[0].Area >= pads[1].Area ? 0 : 1;
        }

        private static string EstimateChipSize(double w, double h)
        {
            double major = Math.Max(w, h);
            double minor = Math.Min(w, h);

            if (major < 0.7) return "0201";
            if (major < 1.2) return "0402";
            if (major < 2.0) return "0603";
            if (major < 2.6) return "0805";
            if (major < 3.8) return "1206";
            if (major < 5.5) return "1812";
            return "2512";
        }

        /// <summary>
        /// Compute the principal orientation angle of a pad cluster in degrees (0, 90, 180, 270).
        /// The canonical orientation is defined as the angle where the longest axis of the
        /// bounding box is horizontal (0°). Returns the CCW rotation from canonical.
        /// </summary>
        public static double ComputePrincipalAngle(PadClusterFeatures features)
        {
            if (features.PadCount < 2) return 0;

            if (features.PadCount == 2)
            {
                // For 2-pad chips: angle of the vector from pad[0] to pad[1]
                var p0 = features.Pads[0];
                var p1 = features.Pads[1];
                double dx = p1.X - p0.X;
                double dy = p1.Y - p0.Y;
                double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                return SnapTo90(angle);
            }

            // For multi-pad: use bounding box aspect ratio
            // If wider than tall, 0°. If taller than wide, 90°.
            double w = features.BoundingBox.Width;
            double h = features.BoundingBox.Height;

            if (Math.Abs(w - h) < TOLERANCE * 4)
                return 0; // Square - no rotation detectable

            return h > w ? 90 : 0;
        }

        /// <summary>
        /// Snap an angle to the nearest 90° increment (0, 90, 180, 270).
        /// Uses CCW convention (standard for CAM).
        /// </summary>
        private static double SnapTo90(double angle)
        {
            // Normalize to [0, 360)
            angle = angle % 360;
            if (angle < 0) angle += 360;

            // Snap to nearest 90
            if (angle < 45 || angle >= 315) return 0;
            if (angle < 135) return 90;
            if (angle < 225) return 180;
            return 270;
        }

        #endregion
    }

    /// <summary>
    /// Generates package body graphics and pin 1 indicators based on classification.
    /// Takes gerber primitives (pads) and a classification result, produces a Package.
    /// </summary>
    public static class PackageBodyGenerator
    {
        /// <summary>
        /// Build a complete Package from selected gerber primitives with auto-classification.
        /// </summary>
        /// <param name="selectedPrimitives">Gerber primitives (pads) to process</param>
        /// <param name="packageName">Optional package name override</param>
        /// <param name="useSimplifiedLeads">If true, use simplified lead bounding boxes instead of individual pads</param>
        public static Package BuildPackage(
            List<GerberPrimitive> selectedPrimitives,
            string packageName = null,
            bool useSimplifiedLeads = false)
        {
            var features = ComponentClassifier.ExtractFeatures(selectedPrimitives);
            var classification = ComponentClassifier.Classify(features);

            if (string.IsNullOrEmpty(packageName))
                packageName = classification.SuggestedName;

            var package = new Package(packageName)
            {
                PartClass = classification.PartClass,
                HasPolarity = classification.HasPolarity,
                Description = classification.Description,
                Width = features.BoundingBox.Width,
                Length = features.BoundingBox.Height,
                Height = classification.MatchedPackage?.HeightMm ?? 0.5
            };

            // Center offset: all coordinates relative to centroid
            double cx = features.Centroid.X;
            double cy = features.Centroid.Y;

            if (useSimplifiedLeads && features.Pads.Count > 0)
            {
                // Use clustered pads directly as leads for placement display
                AddSimplifiedLeadGraphics(package, features, cx, cy);
            }
            else
            {
                // Use individual pad details
                AddDetailedPadGraphics(package, features, classification, cx, cy);
            }

            // Add body outline based on classification
            AddBodyGraphics(package, features, classification, cx, cy);

            // Add pin 1 indicator if polarized
            var sortedPads = SortPadsForNumbering(features, classification);
            if (classification.HasPolarity && classification.Pin1Index >= 0 &&
                sortedPads.Count > 0)
            {
                AddPin1Indicator(package, sortedPads[0], cx, cy, classification);
            }

            return package;
        }

        /// <summary>
        /// Add simplified lead graphics using clustered pad bounds (for placement display).
        /// Each clustered pad becomes one lead at its full size - no separate pad graphic.
        /// </summary>
        private static void AddSimplifiedLeadGraphics(
            Package package, PadClusterFeatures features, double cx, double cy)
        {
            int pinNumber = 1;

            // Use the clustered pads directly - each pad IS a lead at its full size
            // Sort for consistent numbering (top-left first)
            var sortedPads = features.Pads
                .OrderBy(p => p.X + p.Y)
                .ToList();

            foreach (var pad in sortedPads)
            {
                // Lead position relative to component centroid
                double leadX = pad.X - pad.Width / 2 - cx;
                double leadY = pad.Y - pad.Height / 2 - cy;
                double leadCenterX = pad.X - cx;
                double leadCenterY = pad.Y - cy;

                // The lead graphic IS the full pad size - no separate pad rectangle
                var graphic = new PackageGraphic
                {
                    X = leadX,
                    Y = leadY,
                    Width = pad.Width,
                    Height = pad.Height,
                    ShapeType = pad.IsCircular ? GraphicShapeType.Circle : GraphicShapeType.Rectangle,
                    IsPad = false,  // This is a lead, not a pad marker
                    IsFilled = true,
                    FillColor = System.Windows.Media.Color.FromRgb(180, 140, 60) // Golden lead color
                };
                package.Graphics.Add(graphic);

                // Pin matches the lead size for pick-and-place
                package.Pins.Add(new Pin
                {
                    Number = pinNumber,
                    X = leadCenterX,
                    Y = leadCenterY,
                    Width = pad.Width,
                    Height = pad.Height,
                    Shape = pad.IsCircular ? PinShape.Circle : PinShape.Rectangle
                });
                pinNumber++;
            }
        }

        /// <summary>
        /// Add detailed pad graphics (original behavior for detailed view).
        /// </summary>
        private static void AddDetailedPadGraphics(
            Package package, PadClusterFeatures features,
            ClassificationResult classification, double cx, double cy)
        {
            int pinNumber = 1;
            var sortedPads = SortPadsForNumbering(features, classification);

            foreach (var pad in sortedPads)
            {
                var graphic = new PackageGraphic
                {
                    X = pad.X - cx,
                    Y = pad.Y - cy,
                    Width = pad.Width,
                    Height = pad.Height,
                    ShapeType = pad.IsCircular ? GraphicShapeType.Circle : GraphicShapeType.Rectangle,
                    IsPad = true,
                    IsFilled = true
                };
                package.Graphics.Add(graphic);

                package.Pins.Add(new Pin
                {
                    Number = pinNumber,
                    X = pad.X - cx,
                    Y = pad.Y - cy,
                    Width = pad.Width * 0.5,
                    Height = pad.Height * 0.5,
                    Shape = pad.IsCircular ? PinShape.Circle : PinShape.Rectangle
                });
                pinNumber++;
            }
        }

        private static List<PadInfo> SortPadsForNumbering(
            PadClusterFeatures features, ClassificationResult classification)
        {
            var pads = new List<PadInfo>(features.Pads);

            switch (classification.PartClass)
            {
                case PartClass.SOP:
                    // Dual-row: sort CCW starting from top-left
                    return SortDualRowCCW(pads, features);

                case PartClass.QFP:
                    // Quad: sort CCW starting from top-left
                    return SortQuadCCW(pads, features);

                case PartClass.BGA:
                    // Grid: sort row-major (top-left to bottom-right)
                    return pads.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();

                default:
                    // Default: sort by (X+Y) sum for consistent top-left-first ordering
                    return pads.OrderBy(p => p.X + p.Y).ToList();
            }
        }

        private static List<PadInfo> SortDualRowCCW(List<PadInfo> pads, PadClusterFeatures f)
        {
            // Determine if rows are horizontal (two Y clusters) or vertical (two X clusters)
            var xClusters = ClusterValues(pads.Select(p => p.X).ToList());
            var yClusters = ClusterValues(pads.Select(p => p.Y).ToList());

            if (xClusters.Count == 2)
            {
                // Two columns: left column top-to-bottom, then right column bottom-to-top
                double midX = (xClusters[0] + xClusters[1]) / 2;
                var left = pads.Where(p => p.X < midX).OrderBy(p => p.Y).ToList();
                var right = pads.Where(p => p.X >= midX).OrderByDescending(p => p.Y).ToList();
                left.AddRange(right);
                return left;
            }
            else
            {
                // Two rows: top row left-to-right, then bottom row right-to-left
                double midY = (yClusters[0] + yClusters[1]) / 2;
                var top = pads.Where(p => p.Y < midY).OrderBy(p => p.X).ToList();
                var bottom = pads.Where(p => p.Y >= midY).OrderByDescending(p => p.X).ToList();
                top.AddRange(bottom);
                return top;
            }
        }

        private static List<PadInfo> SortQuadCCW(List<PadInfo> pads, PadClusterFeatures f)
        {
            var bb = f.BoundingBox;
            double cx = bb.X + bb.Width / 2;
            double cy = bb.Y + bb.Height / 2;
            double margin = Math.Min(bb.Width, bb.Height) * 0.3;

            // Categorize pads by side
            var left = pads.Where(p => p.X < cx - margin).OrderBy(p => p.Y).ToList();
            var bottom = pads.Where(p => p.Y > cy + margin).OrderByDescending(p => p.X).ToList();
            var right = pads.Where(p => p.X > cx + margin).OrderByDescending(p => p.Y).ToList();
            var top = pads.Where(p => p.Y < cy - margin).OrderBy(p => p.X).ToList();

            // Remaining (center pads, e.g. thermal) go at end
            var used = new HashSet<PadInfo>(left.Concat(bottom).Concat(right).Concat(top));
            var remaining = pads.Where(p => !used.Contains(p)).ToList();

            var result = new List<PadInfo>();
            result.AddRange(left);
            result.AddRange(bottom);
            result.AddRange(right);
            result.AddRange(top);
            result.AddRange(remaining);
            return result;
        }

        private static void AddBodyGraphics(
            Package package, PadClusterFeatures features,
            ClassificationResult classification, double cx, double cy)
        {
            // The body represents the plastic/ceramic package BETWEEN the leads
            // Leads (pads) extend FROM the body - they are NOT inside it
            var bb = features.BoundingBox;
            double bx = bb.X - cx;
            double by = bb.Y - cy;
            double bw = bb.Width;
            double bh = bb.Height;

            switch (classification.PartClass)
            {
                case PartClass.Chip:
                    // 2-terminal chip: body bridges between the two pads
                    // Body is BETWEEN the pad centers, not encompassing them
                    if (features.Pads.Count >= 2)
                    {
                        var sortedPads = features.Pads.OrderBy(p => p.X + p.Y).ToList();
                        var pad1 = sortedPads[0];
                        var pad2 = sortedPads[1];

                        // Determine orientation (horizontal or vertical)
                        bool isHorizontal = Math.Abs(pad1.Y - pad2.Y) < Math.Abs(pad1.X - pad2.X);

                        if (isHorizontal)
                        {
                            // Pads are left-right, body spans between inner edges
                            double bodyLeft = Math.Min(pad1.X, pad2.X) + Math.Min(pad1.Width, pad2.Width) * 0.3;
                            double bodyRight = Math.Max(pad1.X, pad2.X) - Math.Min(pad1.Width, pad2.Width) * 0.3;
                            double bodyTop = Math.Min(pad1.Y - pad1.Height / 2, pad2.Y - pad2.Height / 2);
                            double bodyBottom = Math.Max(pad1.Y + pad1.Height / 2, pad2.Y + pad2.Height / 2);

                            package.Graphics.Add(new PackageGraphic
                            {
                                ShapeType = GraphicShapeType.Rectangle,
                                X = bodyLeft - cx,
                                Y = bodyTop - cy,
                                Width = bodyRight - bodyLeft,
                                Height = bodyBottom - bodyTop,
                                IsFilled = true,
                                IsPad = false,
                                FillColor = System.Windows.Media.Color.FromArgb(140, 60, 60, 60)
                            });
                        }
                        else
                        {
                            // Pads are top-bottom, body spans between inner edges
                            double bodyTop = Math.Min(pad1.Y, pad2.Y) + Math.Min(pad1.Height, pad2.Height) * 0.3;
                            double bodyBottom = Math.Max(pad1.Y, pad2.Y) - Math.Min(pad1.Height, pad2.Height) * 0.3;
                            double bodyLeft = Math.Min(pad1.X - pad1.Width / 2, pad2.X - pad2.Width / 2);
                            double bodyRight = Math.Max(pad1.X + pad1.Width / 2, pad2.X + pad2.Width / 2);

                            package.Graphics.Add(new PackageGraphic
                            {
                                ShapeType = GraphicShapeType.Rectangle,
                                X = bodyLeft - cx,
                                Y = bodyTop - cy,
                                Width = bodyRight - bodyLeft,
                                Height = bodyBottom - bodyTop,
                                IsFilled = true,
                                IsPad = false,
                                FillColor = System.Windows.Media.Color.FromArgb(140, 60, 60, 60)
                            });
                        }
                    }
                    break;

                case PartClass.SOT:
                    // SOT23/SOT223: Body is BETWEEN the leads, not encompassing them
                    // Typical SOT23: 1 lead on one side, 2 leads on opposite side
                    // Body inset from lead outer edges
                    {
                        // Find the extent of leads and compute body as inset region
                        double leadInset = features.MaxPadSize * 0.6; // Body starts 60% into the leads
                        double bodyInsetX = bw > bh ? leadInset : 0;
                        double bodyInsetY = bh > bw ? leadInset : 0;

                        // If roughly square, inset based on pad positions
                        if (Math.Abs(bw - bh) < features.MaxPadSize)
                        {
                            bodyInsetX = leadInset * 0.5;
                            bodyInsetY = leadInset * 0.5;
                        }

                        package.Graphics.Add(new PackageGraphic
                        {
                            ShapeType = GraphicShapeType.Rectangle,
                            X = bx + bodyInsetX,
                            Y = by + bodyInsetY,
                            Width = bw - 2 * bodyInsetX,
                            Height = bh - 2 * bodyInsetY,
                            IsFilled = true,
                            IsPad = false,
                            FillColor = System.Windows.Media.Color.FromArgb(140, 50, 50, 50)
                        });
                    }
                    break;

                case PartClass.SOP:
                    // SOP/SOIC: Body is BETWEEN the two rows of leads
                    // Leads extend from the body sides
                    {
                        // Cluster pads by position to find the two rows
                        var xClusters = ClusterValues(features.Pads.Select(p => p.X).ToList());
                        var yClusters = ClusterValues(features.Pads.Select(p => p.Y).ToList());

                        double bodyX, bodyY, bodyW, bodyH;

                        if (xClusters.Count == 2 && yClusters.Count >= 2)
                        {
                            // Two columns of pads (vertical orientation)
                            // Body spans between the inner edges of the columns
                            var leftPads = features.Pads.Where(p => p.X < (xClusters[0] + xClusters[1]) / 2).ToList();
                            var rightPads = features.Pads.Where(p => p.X >= (xClusters[0] + xClusters[1]) / 2).ToList();

                            double leftInnerEdge = leftPads.Max(p => p.X + p.Width / 2);
                            double rightInnerEdge = rightPads.Min(p => p.X - p.Width / 2);
                            double topEdge = features.Pads.Min(p => p.Y - p.Height / 2);
                            double bottomEdge = features.Pads.Max(p => p.Y + p.Height / 2);

                            // Body starts a bit inside the lead inner edges
                            double leadOverlap = (leftPads.Average(p => p.Width) + rightPads.Average(p => p.Width)) / 4 * 0.3;
                            bodyX = leftInnerEdge - leadOverlap - cx;
                            bodyY = topEdge - cy - 0.1;
                            bodyW = rightInnerEdge - leftInnerEdge + 2 * leadOverlap;
                            bodyH = bottomEdge - topEdge + 0.2;
                        }
                        else if (yClusters.Count == 2)
                        {
                            // Two rows of pads (horizontal orientation)
                            var topPads = features.Pads.Where(p => p.Y < (yClusters[0] + yClusters[1]) / 2).ToList();
                            var bottomPads = features.Pads.Where(p => p.Y >= (yClusters[0] + yClusters[1]) / 2).ToList();

                            double topInnerEdge = topPads.Max(p => p.Y + p.Height / 2);
                            double bottomInnerEdge = bottomPads.Min(p => p.Y - p.Height / 2);
                            double leftEdge = features.Pads.Min(p => p.X - p.Width / 2);
                            double rightEdge = features.Pads.Max(p => p.X + p.Width / 2);

                            double leadOverlap = (topPads.Average(p => p.Height) + bottomPads.Average(p => p.Height)) / 4 * 0.3;
                            bodyX = leftEdge - cx - 0.1;
                            bodyY = topInnerEdge - leadOverlap - cy;
                            bodyW = rightEdge - leftEdge + 0.2;
                            bodyH = bottomInnerEdge - topInnerEdge + 2 * leadOverlap;
                        }
                        else
                        {
                            // Fallback: simple inset
                            double inset = Math.Max(bw, bh) * 0.15;
                            bodyX = bx + inset;
                            bodyY = by + inset;
                            bodyW = bw - 2 * inset;
                            bodyH = bh - 2 * inset;
                        }

                        package.Graphics.Add(new PackageGraphic
                        {
                            ShapeType = GraphicShapeType.Rectangle,
                            X = bodyX,
                            Y = bodyY,
                            Width = bodyW,
                            Height = bodyH,
                            IsFilled = true,
                            IsPad = false,
                            FillColor = System.Windows.Media.Color.FromArgb(140, 40, 40, 40)
                        });
                    }
                    break;

                case PartClass.QFP:
                case PartClass.QFN:
                    // Quad packages: Body is INSIDE the ring of pads
                    {
                        // Find the inner edges of pads on all four sides
                        double padMargin = Math.Min(bw, bh) * 0.12;
                        var leftPads = features.Pads.Where(p => p.X < bb.X + bb.Width * 0.25 + cx).ToList();
                        var rightPads = features.Pads.Where(p => p.X > bb.X + bb.Width * 0.75 + cx).ToList();
                        var topPads = features.Pads.Where(p => p.Y < bb.Y + bb.Height * 0.25 + cy).ToList();
                        var bottomPads = features.Pads.Where(p => p.Y > bb.Y + bb.Height * 0.75 + cy).ToList();

                        double bodyLeft = leftPads.Count > 0 ? leftPads.Max(p => p.X + p.Width / 2) : bb.X + padMargin;
                        double bodyRight = rightPads.Count > 0 ? rightPads.Min(p => p.X - p.Width / 2) : bb.X + bb.Width - padMargin;
                        double bodyTop = topPads.Count > 0 ? topPads.Max(p => p.Y + p.Height / 2) : bb.Y + padMargin;
                        double bodyBottom = bottomPads.Count > 0 ? bottomPads.Min(p => p.Y - p.Height / 2) : bb.Y + bb.Height - padMargin;

                        package.Graphics.Add(new PackageGraphic
                        {
                            ShapeType = GraphicShapeType.Rectangle,
                            X = bodyLeft - cx,
                            Y = bodyTop - cy,
                            Width = bodyRight - bodyLeft,
                            Height = bodyBottom - bodyTop,
                            IsFilled = classification.PartClass == PartClass.QFN,
                            IsPad = false,
                            StrokeThickness = 0.12,
                            FillColor = classification.PartClass == PartClass.QFN
                                ? System.Windows.Media.Color.FromArgb(100, 50, 50, 50)
                                : System.Windows.Media.Colors.Transparent
                        });
                    }
                    break;

                case PartClass.BGA:
                    // BGA: Body encompasses all pads (balls are under the package)
                    double bgaMargin = 0.3;
                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Rectangle,
                        X = bx - bgaMargin,
                        Y = by - bgaMargin,
                        Width = bw + 2 * bgaMargin,
                        Height = bh + 2 * bgaMargin,
                        IsFilled = true,
                        IsPad = false,
                        FillColor = System.Windows.Media.Color.FromArgb(120, 40, 60, 40)
                    });
                    break;

                case PartClass.Connector:
                    // Connector: Outline around all pins
                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Rectangle,
                        X = bx - 0.2,
                        Y = by - 0.2,
                        Width = bw + 0.4,
                        Height = bh + 0.4,
                        IsFilled = false,
                        IsPad = false,
                        StrokeThickness = 0.15
                    });
                    break;

                default:
                    // Switch/other: circle in center
                    if (classification.SuggestedName.Contains("TACT") ||
                        classification.SuggestedName.Contains("SW"))
                    {
                        double radius = Math.Min(bw, bh) * 0.3;
                        package.Graphics.Add(new PackageGraphic
                        {
                            ShapeType = GraphicShapeType.Circle,
                            X = -radius,
                            Y = -radius,
                            Width = radius * 2,
                            Height = radius * 2,
                            IsFilled = false,
                            IsPad = false,
                            StrokeThickness = 0.1
                        });
                    }
                    else
                    {
                        package.Graphics.Add(new PackageGraphic
                        {
                            ShapeType = GraphicShapeType.Rectangle,
                            X = bx,
                            Y = by,
                            Width = bw,
                            Height = bh,
                            IsFilled = false,
                            IsPad = false,
                            StrokeThickness = 0.1
                        });
                    }
                    break;
            }
        }

        private static void AddPin1Indicator(
            Package package, PadInfo pin1Pad, double cx, double cy,
            ClassificationResult classification)
        {
            double px = pin1Pad.X - cx;
            double py = pin1Pad.Y - cy;
            double dotRadius;

            // Pin 1 indicator color: red for visibility
            var pin1Color = System.Windows.Media.Color.FromRgb(220, 60, 60);

            switch (classification.PartClass)
            {
                case PartClass.BGA:
                    // Chamfer on corner near pin A1
                    var bb = package.Bounds;
                    double chamferSize = Math.Min(bb.Width, bb.Height) * 0.12;
                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Line,
                        X = bb.Left,
                        Y = bb.Top + chamferSize,
                        Width = chamferSize,
                        Height = -chamferSize,
                        Points = new List<Point>
                        {
                            new Point(bb.Left, bb.Top + chamferSize),
                            new Point(bb.Left + chamferSize, bb.Top)
                        },
                        IsPin1Indicator = true,
                        IsPad = false,
                        StrokeThickness = 0.15,
                        StrokeColor = pin1Color
                    });
                    return;

                case PartClass.Chip:
                    if (!classification.HasPolarity) return;
                    // Cathode band (bar) on cathode end for diodes
                    double barW = pin1Pad.Width * 0.25;
                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Rectangle,
                        X = px - barW / 2,
                        Y = py - pin1Pad.Height * 0.6,
                        Width = barW,
                        Height = pin1Pad.Height * 1.2,
                        IsFilled = true,
                        IsPin1Indicator = true,
                        IsPad = false,
                        FillColor = System.Windows.Media.Color.FromRgb(180, 180, 180)
                    });
                    return;

                case PartClass.SOT:
                case PartClass.SOP:
                case PartClass.QFP:
                case PartClass.QFN:
                    // Place a small red dot near pin 1, outside the body
                    // Position it at the outer corner closest to pin 1
                    dotRadius = Math.Min(pin1Pad.Width, pin1Pad.Height) * 0.2;
                    if (dotRadius < 0.1) dotRadius = 0.1;
                    if (dotRadius > 0.3) dotRadius = 0.3;

                    // Calculate position: offset from pin 1 towards the nearest corner
                    // The dot should be between pin 1 and the body corner
                    double dotX = px;
                    double dotY = py;

                    // Offset towards the outer edge (away from center)
                    if (Math.Abs(px) > Math.Abs(py))
                    {
                        // Pin 1 is more horizontal - offset vertically towards corner
                        dotX = px + (px > 0 ? dotRadius * 1.5 : -dotRadius * 1.5);
                        dotY = py + (py > 0 ? -dotRadius * 2 : dotRadius * 2);
                    }
                    else
                    {
                        // Pin 1 is more vertical - offset horizontally towards corner
                        dotX = px + (px > 0 ? -dotRadius * 2 : dotRadius * 2);
                        dotY = py + (py > 0 ? dotRadius * 1.5 : -dotRadius * 1.5);
                    }

                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Circle,
                        X = dotX - dotRadius,
                        Y = dotY - dotRadius,
                        Width = dotRadius * 2,
                        Height = dotRadius * 2,
                        IsFilled = true,
                        IsPin1Indicator = true,
                        IsPad = false,
                        FillColor = pin1Color
                    });
                    return;

                default:
                    // Generic dot near pin 1
                    dotRadius = Math.Min(pin1Pad.Width, pin1Pad.Height) * 0.25;
                    if (dotRadius < 0.08) dotRadius = 0.08;

                    // Place dot at corner nearest to pin 1
                    double defaultDotX = px + (px < 0 ? -dotRadius * 2.5 : dotRadius * 2.5);
                    double defaultDotY = py + (py < 0 ? -dotRadius * 2.5 : dotRadius * 2.5);

                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Circle,
                        X = defaultDotX - dotRadius,
                        Y = defaultDotY - dotRadius,
                        Width = dotRadius * 2,
                        Height = dotRadius * 2,
                        IsFilled = true,
                        IsPin1Indicator = true,
                        IsPad = false,
                        FillColor = pin1Color
                    });
                    break;
            }
        }

        private static List<double> ClusterValues(List<double> values)
        {
            if (values.Count == 0) return new List<double>();
            var sorted = values.OrderBy(v => v).ToList();
            var clusters = new List<double> { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - clusters.Last() > 0.2)
                    clusters.Add(sorted[i]);
            }
            return clusters;
        }
    }
}
