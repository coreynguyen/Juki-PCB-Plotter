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
        /// </summary>
        public static PadClusterFeatures ExtractFeatures(List<GerberPrimitive> primitives)
        {
            var features = new PadClusterFeatures();
            if (primitives == null || primitives.Count == 0)
                return features;

            // Build pad info list
            foreach (var prim in primitives)
            {
                if (!prim.IsDark) continue;
                var bounds = prim.GetBounds();
                features.Pads.Add(new PadInfo
                {
                    X = prim.X,
                    Y = prim.Y,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    IsCircular = prim.Type == GerberPrimitiveType.Circle ||
                                 (Math.Abs(bounds.Width - bounds.Height) < TOLERANCE),
                    Area = bounds.Width * bounds.Height
                });
            }

            features.PadCount = features.Pads.Count;
            if (features.PadCount == 0) return features;

            // Bounding box
            double minX = features.Pads.Min(p => p.X - p.Width / 2);
            double minY = features.Pads.Min(p => p.Y - p.Height / 2);
            double maxX = features.Pads.Max(p => p.X + p.Width / 2);
            double maxY = features.Pads.Max(p => p.Y + p.Height / 2);
            features.BoundingBox = new Rect(minX, minY, maxX - minX, maxY - minY);

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

            if (useSimplifiedLeads && features.LeadBounds.Count > 0)
            {
                // Use simplified lead bounding boxes for placement display
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
        /// Add simplified lead graphics using bounding boxes (for placement display).
        /// Each lead is represented as a single rectangular bounding box.
        /// </summary>
        private static void AddSimplifiedLeadGraphics(
            Package package, PadClusterFeatures features, double cx, double cy)
        {
            int pinNumber = 1;

            // Sort lead bounds for consistent numbering (top-left first)
            var sortedLeads = features.LeadBounds
                .Select((rect, idx) => new { Rect = rect, Index = idx })
                .OrderBy(r => r.Rect.X + r.Rect.Y)
                .Select(r => r.Rect)
                .ToList();

            foreach (var leadRect in sortedLeads)
            {
                // Lead center relative to component centroid
                double leadCenterX = leadRect.X + leadRect.Width / 2 - cx;
                double leadCenterY = leadRect.Y + leadRect.Height / 2 - cy;

                var graphic = new PackageGraphic
                {
                    X = leadRect.X - cx,
                    Y = leadRect.Y - cy,
                    Width = leadRect.Width,
                    Height = leadRect.Height,
                    ShapeType = GraphicShapeType.Rectangle,
                    IsPad = true,
                    IsFilled = true,
                    FillColor = System.Windows.Media.Color.FromRgb(180, 140, 60) // Golden lead color
                };
                package.Graphics.Add(graphic);

                // Add pin at lead center
                package.Pins.Add(new Pin
                {
                    Number = pinNumber,
                    X = leadCenterX,
                    Y = leadCenterY,
                    Width = leadRect.Width * 0.5,
                    Height = leadRect.Height * 0.5,
                    Shape = PinShape.Rectangle
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
            var bb = features.BoundingBox;
            double bx = bb.X - cx;
            double by = bb.Y - cy;
            double bw = bb.Width;
            double bh = bb.Height;

            switch (classification.PartClass)
            {
                case PartClass.Chip:
                    // Body bridging the two pads (inset 30% from edges)
                    double insetX = bw * 0.15;
                    double insetY = bh * 0.05;
                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Rectangle,
                        X = bx + insetX,
                        Y = by - insetY,
                        Width = bw - 2 * insetX,
                        Height = bh + 2 * insetY,
                        IsFilled = true,
                        IsPad = false,
                        FillColor = System.Windows.Media.Color.FromArgb(120, 80, 80, 80)
                    });
                    break;

                case PartClass.SOT:
                    // Body rectangle encompassing pad centroids
                    double sotMargin = features.DominantPitch * 0.15;
                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Rectangle,
                        X = bx - sotMargin,
                        Y = by - sotMargin,
                        Width = bw + 2 * sotMargin,
                        Height = bh + 2 * sotMargin,
                        IsFilled = false,
                        IsPad = false,
                        StrokeThickness = 0.1
                    });
                    break;

                case PartClass.SOP:
                    // Body between the two rows of pads
                    double sopInset = bw * 0.2;
                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Rectangle,
                        X = bx + sopInset,
                        Y = by - 0.1,
                        Width = bw - 2 * sopInset,
                        Height = bh + 0.2,
                        IsFilled = false,
                        IsPad = false,
                        StrokeThickness = 0.12
                    });
                    break;

                case PartClass.QFP:
                case PartClass.QFN:
                    // Square body inside the ring of pads
                    double qfpInset = Math.Min(bw, bh) * 0.15;
                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Rectangle,
                        X = bx + qfpInset,
                        Y = by + qfpInset,
                        Width = bw - 2 * qfpInset,
                        Height = bh - 2 * qfpInset,
                        IsFilled = classification.PartClass == PartClass.QFN,
                        IsPad = false,
                        StrokeThickness = 0.12,
                        FillColor = classification.PartClass == PartClass.QFN
                            ? System.Windows.Media.Color.FromArgb(80, 60, 60, 60)
                            : System.Windows.Media.Colors.Transparent
                    });
                    break;

                case PartClass.BGA:
                    // Large square enclosing all pads
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
                        FillColor = System.Windows.Media.Color.FromArgb(100, 40, 60, 40)
                    });
                    break;

                case PartClass.Connector:
                    // Outline rectangle
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

            switch (classification.PartClass)
            {
                case PartClass.BGA:
                    // Chamfer on corner
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
                        StrokeThickness = 0.15
                    });
                    return;

                case PartClass.Chip:
                    if (!classification.HasPolarity) return;
                    // Bar on one end
                    double barW = pin1Pad.Width * 0.3;
                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Rectangle,
                        X = px - barW / 2,
                        Y = py - pin1Pad.Height / 2,
                        Width = barW,
                        Height = pin1Pad.Height,
                        IsFilled = true,
                        IsPin1Indicator = true,
                        IsPad = false,
                        FillColor = System.Windows.Media.Color.FromRgb(200, 200, 200)
                    });
                    return;

                default:
                    // Dot near pin 1
                    dotRadius = Math.Min(pin1Pad.Width, pin1Pad.Height) * 0.25;
                    if (dotRadius < 0.08) dotRadius = 0.08;
                    // Offset the dot slightly outside the pad
                    double offsetX = px < 0 ? -dotRadius * 2 : dotRadius * 2;
                    double offsetY = py < 0 ? -dotRadius * 2 : dotRadius * 2;

                    package.Graphics.Add(new PackageGraphic
                    {
                        ShapeType = GraphicShapeType.Circle,
                        X = px - offsetX - dotRadius,
                        Y = py - offsetY - dotRadius,
                        Width = dotRadius * 2,
                        Height = dotRadius * 2,
                        IsFilled = true,
                        IsPin1Indicator = true,
                        IsPad = false,
                        FillColor = System.Windows.Media.Color.FromRgb(255, 255, 255)
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
