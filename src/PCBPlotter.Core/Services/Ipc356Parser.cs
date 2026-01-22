using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Parser for IPC-D-356 (IPC-D-356A) format files.
    /// Extracts netlist data, test point locations, pad geometry, and component information.
    /// </summary>
    public class Ipc356Parser
    {
        #region Nested Types

        public class Ipc356Data
        {
            public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, string> NetAliases { get; set; } = new Dictionary<string, string>();
            public List<Ipc356Pad> Pads { get; set; } = new List<Ipc356Pad>();
            public Dictionary<string, Ipc356Component> Components { get; set; } = new Dictionary<string, Ipc356Component>();
            public Dictionary<string, List<NetConnection>> Netlist { get; set; } = new Dictionary<string, List<NetConnection>>();

            public bool IsMillimeter { get; set; } = true;
            public int DecimalPlaces { get; set; } = 4;
            public double UnitScale { get { return Math.Pow(10, -DecimalPlaces); } }
        }

        public class Ipc356Pad
        {
            public string RecordType { get; set; }
            public string NetName { get; set; }
            public string RefDes { get; set; }
            public string PinNumber { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double Rotation { get; set; }
            public int SolderMask { get; set; }
            public int AccessLayer { get; set; }
            public double DrillDiameter { get; set; }
            public bool IsSmt { get; set; }
            public bool IsVia { get; set; }
            public bool IsTooling { get; set; }
        }

        public class Ipc356Component
        {
            public string RefDes { get; set; }
            public int PinCount { get; set; }
            public double CentroidX { get; set; }
            public double CentroidY { get; set; }
            public double MinX { get; set; }
            public double MaxX { get; set; }
            public double MinY { get; set; }
            public double MaxY { get; set; }
            public double Width { get { return MaxX - MinX; } }
            public double Height { get { return MaxY - MinY; } }
            public bool IsSmt { get; set; }
            public bool IsThruHole { get; set; }
            public List<Ipc356Pad> Pads { get; set; } = new List<Ipc356Pad>();
        }

        public class NetConnection
        {
            public string RefDes { get; set; }
            public string Pin { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Parse an IPC-D-356 file and return structured data
        /// </summary>
        public Ipc356Data Parse(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("IPC-D-356 file not found", filePath);

            var data = new Ipc356Data();

            using (var reader = new StreamReader(filePath))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.TrimEnd();
                    if (line.Length < 3) continue;

                    string recType = line.Substring(0, 3);

                    switch (recType)
                    {
                        case "P  ":
                            ParseParameterRecord(line, data);
                            break;

                        case "C  ":
                            ParseCommentRecord(line, data);
                            break;

                        case "317":
                        case "327":
                        case "367":
                            var pad = ParseTestRecord(line, data);
                            if (pad != null)
                            {
                                data.Pads.Add(pad);

                                // Build netlist
                                if (!string.IsNullOrEmpty(pad.NetName) &&
                                    pad.NetName != "N/C" &&
                                    !pad.IsVia &&
                                    !string.IsNullOrEmpty(pad.RefDes))
                                {
                                    if (!data.Netlist.ContainsKey(pad.NetName))
                                        data.Netlist[pad.NetName] = new List<NetConnection>();

                                    data.Netlist[pad.NetName].Add(new NetConnection
                                    {
                                        RefDes = pad.RefDes,
                                        Pin = pad.PinNumber,
                                        X = pad.X,
                                        Y = pad.Y
                                    });
                                }
                            }
                            break;

                        case "999":
                            // End of file
                            break;
                    }
                }
            }

            // Group pads by component and compute component data
            var padsByRef = new Dictionary<string, List<Ipc356Pad>>();
            foreach (var pad in data.Pads)
            {
                if (!string.IsNullOrEmpty(pad.RefDes) && !pad.IsVia)
                {
                    if (!padsByRef.ContainsKey(pad.RefDes))
                        padsByRef[pad.RefDes] = new List<Ipc356Pad>();
                    padsByRef[pad.RefDes].Add(pad);
                }
            }

            foreach (var kvp in padsByRef)
            {
                var pads = kvp.Value;
                var xs = pads.Select(p => p.X).ToList();
                var ys = pads.Select(p => p.Y).ToList();

                data.Components[kvp.Key] = new Ipc356Component
                {
                    RefDes = kvp.Key,
                    PinCount = pads.Count,
                    CentroidX = xs.Average(),
                    CentroidY = ys.Average(),
                    MinX = xs.Min(),
                    MaxX = xs.Max(),
                    MinY = ys.Min(),
                    MaxY = ys.Max(),
                    IsSmt = pads.Any(p => p.IsSmt),
                    IsThruHole = pads.Any(p => !p.IsSmt),
                    Pads = pads
                };
            }

            return data;
        }

        /// <summary>
        /// Convert parsed IPC data to Package objects
        /// </summary>
        public List<Package> CreatePackagesFromData(Ipc356Data data)
        {
            var packages = new List<Package>();
            var uniquePackages = new Dictionary<string, Package>();

            foreach (var comp in data.Components.Values)
            {
                // Generate a package key based on pin count and approximate size
                string pkgKey = GeneratePackageKey(comp);

                if (!uniquePackages.ContainsKey(pkgKey))
                {
                    var package = CreatePackageFromComponent(comp, data);
                    uniquePackages[pkgKey] = package;
                    packages.Add(package);
                }
            }

            return packages;
        }

        /// <summary>
        /// Convert parsed IPC data to Placement objects
        /// </summary>
        public List<Placement> CreatePlacementsFromData(Ipc356Data data, Dictionary<string, Package> packageMap)
        {
            var placements = new List<Placement>();

            foreach (var comp in data.Components.Values)
            {
                string pkgKey = GeneratePackageKey(comp);
                Package package = null;
                packageMap?.TryGetValue(pkgKey, out package);

                var placement = new Placement
                {
                    Reference = comp.RefDes,
                    X = comp.CentroidX,
                    Y = comp.CentroidY,
                    Rotation = 0, // Cannot determine from IPC-356 alone
                    Side = comp.IsSmt ? BoardSide.Top : BoardSide.Top, // Determine from access layer
                    Package = package
                };

                placements.Add(placement);
            }

            return placements;
        }

        #endregion

        #region Private Methods

        private void ParseParameterRecord(string line, Ipc356Data data)
        {
            string content = line.Length > 3 ? line.Substring(3).Trim() : "";

            if (content.StartsWith("JOB"))
            {
                data.Metadata["Job"] = content.Substring(3).Trim();
            }
            else if (content.StartsWith("UNITS"))
            {
                data.Metadata["Units"] = content.Substring(5).Trim();
            }
            else if (content.StartsWith("NNAME"))
            {
                // Net name alias: NNAMEm0000 FGRU_EVTRIG_IN_P1
                var match = Regex.Match(content, @"NNAME(\S+)\s+(.+)");
                if (match.Success)
                {
                    data.NetAliases[match.Groups[1].Value] = match.Groups[2].Value.Trim();
                }
            }
            else if (content.StartsWith("VER"))
            {
                data.Metadata["Version"] = content.Substring(3).Trim();
            }
        }

        private void ParseCommentRecord(string line, Ipc356Data data)
        {
            string content = line.Length > 3 ? line.Substring(3) : "";

            if (content.Contains("Unit of Measure:"))
            {
                data.IsMillimeter = content.ToLower().Contains("millimeter");
                data.Metadata["Unit"] = data.IsMillimeter ? "mm" : "inch";
            }
            else if (content.Contains("Decimal Place Accuracy:"))
            {
                var match = Regex.Match(content, @":\s*(\d+)");
                if (match.Success)
                {
                    data.DecimalPlaces = int.Parse(match.Groups[1].Value);
                    data.Metadata["DecimalPlaces"] = data.DecimalPlaces.ToString();
                }
            }
            else if (content.Contains("Number of etch Layers:"))
            {
                var match = Regex.Match(content, @":\s*(\d+)");
                if (match.Success)
                {
                    data.Metadata["Layers"] = match.Groups[1].Value;
                }
            }
            else if (content.Contains("Board Thickness"))
            {
                var match = Regex.Match(content, @":\s*([\d.]+)");
                if (match.Success)
                {
                    data.Metadata["Thickness"] = match.Groups[1].Value;
                }
            }
        }

        private Ipc356Pad ParseTestRecord(string line, Ipc356Data data)
        {
            if (line.Length < 40) return null;

            var pad = new Ipc356Pad
            {
                RecordType = line.Substring(0, 3)
            };

            // Record type determines SMT/TH/Tooling
            pad.IsSmt = pad.RecordType == "327";
            pad.IsTooling = pad.RecordType == "367";

            // Net name (columns 4-17, 14 chars)
            string netName = line.Substring(3, Math.Min(14, line.Length - 3)).Trim();

            // Resolve net alias
            if (data.NetAliases.ContainsKey(netName))
                netName = data.NetAliases[netName];

            pad.NetName = netName;

            // Parse reference designator and pin from columns 18-40
            string refPinSection = line.Length > 17 ? line.Substring(17, Math.Min(23, line.Length - 17)) : "";
            int dashPos = refPinSection.IndexOf('-');

            if (dashPos >= 0)
            {
                pad.RefDes = refPinSection.Substring(0, dashPos).Trim();
                string pinPart = refPinSection.Substring(dashPos + 1).Trim().Split(' ')[0];
                pad.PinNumber = pinPart.TrimEnd('M').Trim();
            }
            else
            {
                pad.RefDes = refPinSection.Substring(0, Math.Min(9, refPinSection.Length)).Trim();
            }

            pad.IsVia = pad.RefDes == "VIA";

            // Parse coordinates using regex
            string rest = line.Length > 17 ? line.Substring(17) : "";

            // X coordinate: X+NNNNNN or X-NNNNNN
            var xMatch = Regex.Match(rest, @"X([+-])(\d+)");
            if (xMatch.Success)
            {
                int sign = xMatch.Groups[1].Value == "+" ? 1 : -1;
                long rawValue = long.Parse(xMatch.Groups[2].Value);
                pad.X = sign * rawValue * data.UnitScale;

                // Convert to mm if needed
                if (!data.IsMillimeter)
                    pad.X *= 25.4;
            }

            // Y coordinate: Y+NNNNNN or Y-NNNNNN
            var yMatch = Regex.Match(rest, @"Y([+-])(\d+)");
            if (yMatch.Success)
            {
                int sign = yMatch.Groups[1].Value == "+" ? 1 : -1;
                long rawValue = long.Parse(yMatch.Groups[2].Value);
                pad.Y = sign * rawValue * data.UnitScale;

                if (!data.IsMillimeter)
                    pad.Y *= 25.4;
            }

            // Parse pad dimensions after coordinates
            if (yMatch.Success)
            {
                string dimSection = rest.Substring(yMatch.Index + yMatch.Length);

                // Pad width: XNNNN
                var widthMatch = Regex.Match(dimSection, @"X(\d+)");
                if (widthMatch.Success)
                {
                    pad.Width = int.Parse(widthMatch.Groups[1].Value) * data.UnitScale;
                    if (!data.IsMillimeter) pad.Width *= 25.4;

                    // Pad height: YNNNN (optional, defaults to width for square pads)
                    string afterWidth = dimSection.Substring(widthMatch.Index + widthMatch.Length);
                    var heightMatch = Regex.Match(afterWidth, @"Y(\d+)");
                    if (heightMatch.Success)
                    {
                        pad.Height = int.Parse(heightMatch.Groups[1].Value) * data.UnitScale;
                        if (!data.IsMillimeter) pad.Height *= 25.4;
                    }
                    else
                    {
                        pad.Height = pad.Width; // Square pad
                    }
                }

                // Rotation: RNNN
                var rotMatch = Regex.Match(dimSection, @"R(\d{3})");
                if (rotMatch.Success)
                {
                    pad.Rotation = int.Parse(rotMatch.Groups[1].Value);
                }

                // Solder mask: SN
                var smMatch = Regex.Match(dimSection, @"S(\d)");
                if (smMatch.Success)
                {
                    pad.SolderMask = int.Parse(smMatch.Groups[1].Value);
                }

                // Access layer: ANN
                var accessMatch = Regex.Match(dimSection, @"A(\d{2})");
                if (accessMatch.Success)
                {
                    pad.AccessLayer = int.Parse(accessMatch.Groups[1].Value);
                }

                // Drill diameter: DNNNN
                var drillMatch = Regex.Match(dimSection, @"D(\d{4})");
                if (drillMatch.Success)
                {
                    pad.DrillDiameter = int.Parse(drillMatch.Groups[1].Value) * data.UnitScale;
                    if (!data.IsMillimeter) pad.DrillDiameter *= 25.4;
                }
            }

            return pad;
        }

        private string GeneratePackageKey(Ipc356Component comp)
        {
            // Create a key based on pin count and rough dimensions
            int pinCount = comp.PinCount;
            double width = Math.Round(comp.Width, 1);
            double height = Math.Round(comp.Height, 1);
            string type = comp.IsSmt ? "SMT" : "TH";

            return string.Format("{0}_{1}P_{2}x{3}", type, pinCount, width, height);
        }

        private Package CreatePackageFromComponent(Ipc356Component comp, Ipc356Data data)
        {
            var package = new Package(GeneratePackageKey(comp))
            {
                Width = comp.Width,
                Length = comp.Height,
                Height = comp.IsSmt ? 1.0 : 2.0,
                HasPolarity = comp.PinCount > 2
            };

            // Classify the package
            if (comp.PinCount == 2)
            {
                package.PartClass = PartClass.Chip;
            }
            else if (comp.PinCount <= 8 && comp.IsSmt)
            {
                package.PartClass = PartClass.SOT;
            }
            else if (comp.IsSmt)
            {
                if (comp.Width > 5 && comp.Height > 5)
                    package.PartClass = PartClass.QFP;
                else
                    package.PartClass = PartClass.SOP;
            }
            else
            {
                package.PartClass = PartClass.Other;
            }

            // Add body outline
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = -comp.Width / 2,
                Y = -comp.Height / 2,
                Width = comp.Width,
                Height = comp.Height,
                IsFilled = false,
                StrokeThickness = 0.1
            });

            // Add pin 1 marker if polarized
            if (package.HasPolarity)
            {
                double markerSize = Math.Min(comp.Width, comp.Height) * 0.1;
                package.Graphics.Add(new PackageGraphic
                {
                    ShapeType = GraphicShapeType.Circle,
                    X = -comp.Width / 2 + markerSize,
                    Y = -comp.Height / 2 + markerSize,
                    Width = markerSize,
                    Height = markerSize,
                    IsFilled = true,
                    IsPin1Indicator = true
                });
            }

            // Add pads/pins from component data
            int pinNum = 1;
            foreach (var pad in comp.Pads.OrderBy(p => p.PinNumber))
            {
                // Normalize position relative to centroid
                double relX = pad.X - comp.CentroidX;
                double relY = pad.Y - comp.CentroidY;

                PinShape shape = PinShape.Rectangle;
                if (pad.DrillDiameter > 0)
                    shape = PinShape.Circle;
                else if (pad.Width != pad.Height)
                    shape = PinShape.Oval;

                int pinNumber;
                if (!int.TryParse(pad.PinNumber, out pinNumber))
                    pinNumber = pinNum;

                package.Pins.Add(new Pin
                {
                    Number = pinNumber,
                    Name = pad.PinNumber,
                    X = relX,
                    Y = relY,
                    Width = pad.Width > 0 ? pad.Width : 0.5,
                    Height = pad.Height > 0 ? pad.Height : 0.5,
                    Shape = shape
                });

                pinNum++;
            }

            return package;
        }

        #endregion
    }
}
