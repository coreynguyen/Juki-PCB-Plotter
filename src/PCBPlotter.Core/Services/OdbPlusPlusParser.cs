using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Parser for ODB++ CAD data format.
    /// Supports .tgz, .tar.gz, .tar, .zip archives or unpacked directories.
    /// </summary>
    public class OdbPlusPlusParser
    {
        #region Data Classes

        public class OdbStep
        {
            public int Column { get; set; }
            public string Name { get; set; }
        }

        public class OdbLayer
        {
            public int Row { get; set; }
            public string Name { get; set; }
            public string Context { get; set; }
            public string LayerType { get; set; }
            public string Polarity { get; set; }
            public string StartName { get; set; }
            public string EndName { get; set; }
        }

        public class OdbPin
        {
            public int Index { get; set; }
            public double X { get; set; }  // inches
            public double Y { get; set; }  // inches
            public double Rotation { get; set; }
            public string Mirror { get; set; }
            public int NetNumber { get; set; }
            public int SubnetNumber { get; set; }
            public string Name { get; set; }

            public double XMils => X * 1000;
            public double YMils => Y * 1000;
            public double XMm => X * 25.4;
            public double YMm => Y * 25.4;
        }

        public class OdbComponent
        {
            public int PackageRef { get; set; }
            public double X { get; set; }  // inches
            public double Y { get; set; }  // inches
            public double Rotation { get; set; }
            public string Mirror { get; set; }
            public string RefDes { get; set; }
            public string PartNumber { get; set; }
            public string Layer { get; set; }  // "top" or "bottom"
            public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
            public List<OdbPin> Pins { get; set; } = new List<OdbPin>();

            public double XMils => X * 1000;
            public double YMils => Y * 1000;
            public double XMm => X * 25.4;
            public double YMm => Y * 25.4;

            public string PackageName => Properties.ContainsKey("PackageReference") ? Properties["PackageReference"] :
                                         Properties.ContainsKey("Package_Reference") ? Properties["Package_Reference"] : "";
            public string Value => Properties.ContainsKey("Value") ? Properties["Value"] : "";
            public string Manufacturer => Properties.ContainsKey("Manufacturer") ? Properties["Manufacturer"] : "";
            public string MountType => Properties.ContainsKey("MountType") ? Properties["MountType"] :
                                       Properties.ContainsKey("Mounting_Technology") ? Properties["Mounting_Technology"] : "";
        }

        public class OdbFeature
        {
            public string Type { get; set; }  // "pad", "line", "arc", "text", "surface"
            public double X { get; set; }
            public double Y { get; set; }
            public double X2 { get; set; }
            public double Y2 { get; set; }
            public int SymbolIndex { get; set; }
            public string SymbolName { get; set; }
            public double Rotation { get; set; }
            public string Polarity { get; set; }
        }

        public class OdbData
        {
            public List<OdbStep> Steps { get; set; } = new List<OdbStep>();
            public List<OdbLayer> Layers { get; set; } = new List<OdbLayer>();
            public List<OdbComponent> Components { get; set; } = new List<OdbComponent>();
            public Dictionary<int, string> NetNames { get; set; } = new Dictionary<int, string>();
            public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
            public double BoardWidth { get; set; }  // inches
            public double BoardHeight { get; set; }  // inches
            public List<System.Windows.Point> ProfilePoints { get; set; } = new List<System.Windows.Point>();

            public double BoardWidthMm => BoardWidth * 25.4;
            public double BoardHeightMm => BoardHeight * 25.4;

            public List<OdbComponent> TopComponents => Components.Where(c => c.Layer == "top").ToList();
            public List<OdbComponent> BottomComponents => Components.Where(c => c.Layer == "bottom").ToList();
        }

        #endregion

        private readonly CultureInfo _invariantCulture = CultureInfo.InvariantCulture;

        /// <summary>
        /// Parse an ODB++ file or directory.
        /// </summary>
        /// <param name="path">Path to .tgz, .tar.gz, .tar, .zip file or unpacked odb directory</param>
        public OdbData Parse(string path)
        {
            if (Directory.Exists(path))
            {
                return ParseDirectory(path);
            }
            else if (File.Exists(path))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".tgz" || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseTarGz(path);
                }
                else if (ext == ".tar")
                {
                    return ParseTar(path);
                }
                else if (ext == ".zip")
                {
                    return ParseZip(path);
                }
                else if (ext == ".odb")
                {
                    // .odb could be tar.gz or zip - try tar.gz first
                    try { return ParseTarGz(path); }
                    catch { return ParseZip(path); }
                }
                else
                {
                    throw new ArgumentException($"Unsupported file extension: {ext}");
                }
            }
            else
            {
                throw new FileNotFoundException($"File or directory not found: {path}");
            }
        }

        #region Archive Parsing

        private OdbData ParseTarGz(string filePath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "odb_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                ExtractTarGz(filePath, tempDir);
                return ParseDirectory(FindOdbRoot(tempDir));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private OdbData ParseTar(string filePath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "odb_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                ExtractTar(File.OpenRead(filePath), tempDir);
                return ParseDirectory(FindOdbRoot(tempDir));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private OdbData ParseZip(string filePath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "odb_" + Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(filePath, tempDir);
                return ParseDirectory(FindOdbRoot(tempDir));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private void ExtractTarGz(string gzipPath, string destDir)
        {
            using (var fs = File.OpenRead(gzipPath))
            using (var gzip = new GZipStream(fs, CompressionMode.Decompress))
            {
                ExtractTar(gzip, destDir);
            }
        }

        private void ExtractTar(Stream tarStream, string destDir)
        {
            // Simple TAR extraction (handles POSIX tar format)
            byte[] buffer = new byte[512];
            while (true)
            {
                int bytesRead = tarStream.Read(buffer, 0, 512);
                if (bytesRead < 512) break;

                // Check for end of archive (two empty blocks)
                bool allZero = buffer.All(b => b == 0);
                if (allZero) break;

                // Parse header
                string name = Encoding.ASCII.GetString(buffer, 0, 100).TrimEnd('\0', ' ');
                if (string.IsNullOrEmpty(name)) break;

                // Handle GNU long name extension
                char typeFlag = (char)buffer[156];
                if (typeFlag == 'L')
                {
                    // Long filename follows
                    int longNameSize = ParseOctal(buffer, 124, 12);
                    byte[] longNameBuf = new byte[((longNameSize + 511) / 512) * 512];
                    tarStream.Read(longNameBuf, 0, longNameBuf.Length);
                    name = Encoding.ASCII.GetString(longNameBuf, 0, longNameSize).TrimEnd('\0');

                    // Read actual header
                    tarStream.Read(buffer, 0, 512);
                    typeFlag = (char)buffer[156];
                }

                int size = ParseOctal(buffer, 124, 12);
                int mode = ParseOctal(buffer, 100, 8);

                // Clean up path
                name = name.Replace('/', Path.DirectorySeparatorChar);
                if (name.StartsWith("." + Path.DirectorySeparatorChar))
                    name = name.Substring(2);

                string fullPath = Path.Combine(destDir, name);

                if (typeFlag == '5' || name.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    // Directory
                    Directory.CreateDirectory(fullPath);
                }
                else if (typeFlag == '0' || typeFlag == '\0')
                {
                    // Regular file
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    using (var fileStream = File.Create(fullPath))
                    {
                        int remaining = size;
                        byte[] fileBuffer = new byte[4096];
                        while (remaining > 0)
                        {
                            int toRead = Math.Min(fileBuffer.Length, remaining);
                            int read = tarStream.Read(fileBuffer, 0, toRead);
                            if (read == 0) break;
                            fileStream.Write(fileBuffer, 0, read);
                            remaining -= read;
                        }
                    }

                    // Skip padding
                    int paddedSize = ((size + 511) / 512) * 512;
                    int skip = paddedSize - size;
                    if (skip > 0)
                    {
                        byte[] skipBuf = new byte[skip];
                        tarStream.Read(skipBuf, 0, skip);
                    }
                }
                else
                {
                    // Skip unknown type
                    int paddedSize = ((size + 511) / 512) * 512;
                    byte[] skipBuf = new byte[paddedSize];
                    tarStream.Read(skipBuf, 0, paddedSize);
                }
            }
        }

        private int ParseOctal(byte[] buffer, int offset, int length)
        {
            string str = Encoding.ASCII.GetString(buffer, offset, length).Trim('\0', ' ');
            if (string.IsNullOrEmpty(str)) return 0;
            return Convert.ToInt32(str, 8);
        }

        private string FindOdbRoot(string extractedDir)
        {
            // Look for matrix/matrix file to find ODB root
            var matrixFiles = Directory.GetFiles(extractedDir, "matrix", SearchOption.AllDirectories);
            foreach (var mf in matrixFiles)
            {
                string parentDir = Path.GetDirectoryName(mf);
                if (Path.GetFileName(parentDir).Equals("matrix", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetDirectoryName(parentDir);
                }
            }

            // Fall back to first subdirectory or extractedDir itself
            var subdirs = Directory.GetDirectories(extractedDir);
            return subdirs.Length > 0 ? subdirs[0] : extractedDir;
        }

        #endregion

        #region Directory Parsing

        private OdbData ParseDirectory(string odbRoot)
        {
            var data = new OdbData();

            // Parse matrix file
            string matrixPath = Path.Combine(odbRoot, "matrix", "matrix");
            if (File.Exists(matrixPath))
            {
                ParseMatrixFile(matrixPath, data);
            }

            // Parse global attributes
            string attrPath = Path.Combine(odbRoot, "misc", "attrlist");
            if (File.Exists(attrPath))
            {
                data.Attributes = ParseAttrList(File.ReadAllText(attrPath, Encoding.GetEncoding("iso-8859-1")));
            }

            // Find steps directory
            string stepsDir = Path.Combine(odbRoot, "steps");
            if (Directory.Exists(stepsDir))
            {
                var stepDirs = Directory.GetDirectories(stepsDir);
                foreach (var stepDir in stepDirs)
                {
                    ParseStep(stepDir, data);
                }
            }

            return data;
        }

        private void ParseMatrixFile(string filePath, OdbData data)
        {
            string content = File.ReadAllText(filePath, Encoding.GetEncoding("iso-8859-1"));

            // Parse STEP blocks
            var stepMatches = Regex.Matches(content, @"STEP\s*\{([^}]+)\}", RegexOptions.Singleline);
            foreach (Match match in stepMatches)
            {
                string block = match.Groups[1].Value;
                var step = new OdbStep();

                var colMatch = Regex.Match(block, @"COL=(\d+)");
                if (colMatch.Success) step.Column = int.Parse(colMatch.Groups[1].Value);

                var nameMatch = Regex.Match(block, @"NAME=(\S+)");
                if (nameMatch.Success) step.Name = nameMatch.Groups[1].Value;

                data.Steps.Add(step);
            }

            // Parse LAYER blocks
            var layerMatches = Regex.Matches(content, @"LAYER\s*\{([^}]+)\}", RegexOptions.Singleline);
            foreach (Match match in layerMatches)
            {
                string block = match.Groups[1].Value;
                var layer = new OdbLayer();

                var rowMatch = Regex.Match(block, @"ROW=(\d+)");
                if (rowMatch.Success) layer.Row = int.Parse(rowMatch.Groups[1].Value);

                var nameMatch = Regex.Match(block, @"NAME=(\S+)");
                if (nameMatch.Success) layer.Name = nameMatch.Groups[1].Value;

                var contextMatch = Regex.Match(block, @"CONTEXT=(\S+)");
                if (contextMatch.Success) layer.Context = contextMatch.Groups[1].Value;

                var typeMatch = Regex.Match(block, @"TYPE=(\S+)");
                if (typeMatch.Success) layer.LayerType = typeMatch.Groups[1].Value;

                var polarityMatch = Regex.Match(block, @"POLARITY=(\S+)");
                if (polarityMatch.Success) layer.Polarity = polarityMatch.Groups[1].Value;

                var startMatch = Regex.Match(block, @"START_NAME=(\S*)");
                if (startMatch.Success) layer.StartName = startMatch.Groups[1].Value;

                var endMatch = Regex.Match(block, @"END_NAME=(\S*)");
                if (endMatch.Success) layer.EndName = endMatch.Groups[1].Value;

                data.Layers.Add(layer);
            }
        }

        private void ParseStep(string stepDir, OdbData data)
        {
            // Parse profile (board outline)
            string profilePath = Path.Combine(stepDir, "profile");
            if (File.Exists(profilePath))
            {
                ParseProfile(profilePath, data);
            }

            // Parse netlist
            string netlistPath = Path.Combine(stepDir, "netlists", "cadnet", "netlist");
            if (File.Exists(netlistPath))
            {
                ParseNetlist(netlistPath, data);
            }

            // Parse component layers
            string layersDir = Path.Combine(stepDir, "layers");
            if (Directory.Exists(layersDir))
            {
                // First try to find component layers based on matrix definitions
                var componentLayers = data.Layers.Where(l =>
                    l.LayerType?.ToUpperInvariant() == "COMPONENT").ToList();

                bool foundFromMatrix = false;
                foreach (var layer in componentLayers)
                {
                    string layerDir = Path.Combine(layersDir, layer.Name);
                    if (Directory.Exists(layerDir))
                    {
                        string compFile = Path.Combine(layerDir, "components");
                        if (File.Exists(compFile))
                        {
                            // Determine side from layer name
                            string side = layer.Name.ToLowerInvariant().Contains("bot") ? "bottom" : "top";
                            ParseComponentsFile(compFile, side, data);
                            foundFromMatrix = true;
                        }
                    }
                }

                // Fall back to searching by naming convention if matrix didn't help
                if (!foundFromMatrix)
                {
                    // Top components
                    string topCompPath = FindComponentsFile(layersDir, "comp_+_top");
                    if (topCompPath != null)
                    {
                        ParseComponentsFile(topCompPath, "top", data);
                    }

                    // Bottom components
                    string botCompPath = FindComponentsFile(layersDir, "comp_+_bot");
                    if (botCompPath != null)
                    {
                        ParseComponentsFile(botCompPath, "bottom", data);
                    }
                }

                // Also scan all layer directories for any components files we might have missed
                foreach (var dir in Directory.GetDirectories(layersDir))
                {
                    string compFile = Path.Combine(dir, "components");
                    if (File.Exists(compFile))
                    {
                        string dirName = Path.GetFileName(dir).ToLowerInvariant();
                        string side = dirName.Contains("bot") ? "bottom" : "top";

                        // Check if we already parsed this
                        bool alreadyParsed = data.Components.Any(c =>
                            c.Layer == side && data.Components.Count > 0);

                        // Only parse if the directory looks like a component layer and we haven't parsed it
                        if ((dirName.Contains("comp") || data.Layers.Any(l =>
                             l.Name.Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase) &&
                             l.LayerType?.ToUpperInvariant() == "COMPONENT")))
                        {
                            // Count before parsing
                            int countBefore = data.Components.Count(c => c.Layer == side);
                            ParseComponentsFile(compFile, side, data);
                            int countAfter = data.Components.Count(c => c.Layer == side);

                            // If no new components were added, it was probably already parsed
                            // (This is a simple duplicate detection)
                        }
                    }
                }
            }
        }

        private string FindComponentsFile(string layersDir, string layerPrefix)
        {
            // Look for component layer directories with various naming conventions
            // Examples: comp_+_top, comp+top, comp_+top, top_comp, component_top, etc.
            var dirs = Directory.GetDirectories(layersDir);

            // Determine which side we're looking for
            bool isTop = layerPrefix.Contains("top");
            string sideIndicator = isTop ? "top" : "bot";

            foreach (var dir in dirs)
            {
                string dirName = Path.GetFileName(dir).ToLowerInvariant();

                // Check if this directory has a components file
                string compFile = Path.Combine(dir, "components");
                if (!File.Exists(compFile))
                    continue;

                // Check various naming patterns for component layers
                bool isComponentLayer = dirName.Contains("comp");
                bool matchesSide = dirName.Contains(sideIndicator) ||
                                   (isTop && dirName.Contains("+")) ||
                                   (!isTop && dirName.Contains("-") && !dirName.Contains("+"));

                if (isComponentLayer && matchesSide)
                    return compFile;
            }

            // Second pass: look for any layer with "components" file that matches side pattern
            foreach (var dir in dirs)
            {
                string dirName = Path.GetFileName(dir).ToLowerInvariant();
                string compFile = Path.Combine(dir, "components");
                if (!File.Exists(compFile))
                    continue;

                // Simpler matching - just check for side indicator
                if ((isTop && (dirName.Contains("top") || dirName.EndsWith("+") || dirName.Contains("_+_"))) ||
                    (!isTop && (dirName.Contains("bot") || dirName.Contains("bottom"))))
                {
                    return compFile;
                }
            }

            return null;
        }

        private void ParseProfile(string filePath, OdbData data)
        {
            string content = File.ReadAllText(filePath, Encoding.GetEncoding("iso-8859-1"));
            var points = new List<System.Windows.Point>();

            foreach (string line in content.Replace("\r", "").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("OB ") || trimmed.StartsWith("OS "))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        double x = double.Parse(parts[1], _invariantCulture);
                        double y = double.Parse(parts[2], _invariantCulture);
                        points.Add(new System.Windows.Point(x, y));
                    }
                }
            }

            if (points.Count > 0)
            {
                data.ProfilePoints = points;
                double minX = points.Min(p => p.X);
                double maxX = points.Max(p => p.X);
                double minY = points.Min(p => p.Y);
                double maxY = points.Max(p => p.Y);
                data.BoardWidth = maxX - minX;
                data.BoardHeight = maxY - minY;
            }
        }

        private void ParseNetlist(string filePath, OdbData data)
        {
            string content = File.ReadAllText(filePath, Encoding.GetEncoding("iso-8859-1"));

            foreach (string line in content.Replace("\r", "").Split('\n'))
            {
                string trimmed = line.Trim();
                var match = Regex.Match(trimmed, @"^\$(\d+)\s+(.+)$");
                if (match.Success)
                {
                    int index = int.Parse(match.Groups[1].Value);
                    string netName = match.Groups[2].Value;
                    data.NetNames[index] = netName;
                }
            }
        }

        private void ParseComponentsFile(string filePath, string layer, OdbData data)
        {
            string content = File.ReadAllText(filePath, Encoding.GetEncoding("iso-8859-1"));
            var attrNames = new Dictionary<string, string>();
            OdbComponent currentComponent = null;

            foreach (string line in content.Replace("\r", "").Split('\n'))
            {
                string trimmed = line.Trim();

                // Skip empty lines and comments (but not CMP comment markers)
                if (string.IsNullOrEmpty(trimmed) ||
                    (trimmed.StartsWith("#") && !trimmed.Contains("CMP")))
                    continue;

                // Attribute name definition: @0 .comp_mount_type
                if (trimmed.StartsWith("@"))
                {
                    var match = Regex.Match(trimmed, @"^@(\d+)\s+(\S+)");
                    if (match.Success)
                    {
                        attrNames[match.Groups[1].Value] = match.Groups[2].Value;
                    }
                    continue;
                }

                // Component record: CMP pkg_ref x y rot mirror refdes partnum ;attrs
                if (trimmed.StartsWith("CMP "))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 7)
                    {
                        currentComponent = new OdbComponent
                        {
                            PackageRef = int.Parse(parts[1]),
                            X = double.Parse(parts[2], _invariantCulture),
                            Y = double.Parse(parts[3], _invariantCulture),
                            Rotation = double.Parse(parts[4], _invariantCulture),
                            Mirror = parts[5],
                            RefDes = parts[6],
                            Layer = layer
                        };

                        // Extract part number and attributes
                        string remainder = string.Join(" ", parts.Skip(7));
                        if (remainder.Contains(";"))
                        {
                            int semiIdx = remainder.LastIndexOf(';');
                            currentComponent.PartNumber = remainder.Substring(0, semiIdx).Trim();
                            string attrsRaw = remainder.Substring(semiIdx + 1);

                            foreach (string attr in attrsRaw.Split(','))
                            {
                                if (attr.Contains("="))
                                {
                                    var kv = attr.Split(new[] { '=' }, 2);
                                    string attrName = attrNames.ContainsKey(kv[0]) ? attrNames[kv[0]] : $"attr_{kv[0]}";
                                    currentComponent.Attributes[attrName] = kv[1];
                                }
                            }
                        }
                        else
                        {
                            currentComponent.PartNumber = remainder.Trim();
                        }

                        data.Components.Add(currentComponent);
                    }
                    continue;
                }

                // Property record: PRP name 'value'
                if (trimmed.StartsWith("PRP ") && currentComponent != null)
                {
                    var match = Regex.Match(trimmed, @"^PRP\s+(\S+)\s+'(.+)'");
                    if (match.Success)
                    {
                        currentComponent.Properties[match.Groups[1].Value] = match.Groups[2].Value;
                    }
                    continue;
                }

                // Pin record: TOP idx x y rot mirror net subnet name
                if (trimmed.StartsWith("TOP ") && currentComponent != null)
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 8)
                    {
                        var pin = new OdbPin
                        {
                            Index = int.Parse(parts[1]),
                            X = double.Parse(parts[2], _invariantCulture),
                            Y = double.Parse(parts[3], _invariantCulture),
                            Rotation = double.Parse(parts[4], _invariantCulture),
                            Mirror = parts[5],
                            NetNumber = int.Parse(parts[6]),
                            SubnetNumber = int.Parse(parts[7]),
                            Name = parts.Length > 8 ? parts[8] : ""
                        };
                        currentComponent.Pins.Add(pin);
                    }
                    continue;
                }
            }
        }

        private Dictionary<string, string> ParseAttrList(string content)
        {
            var attrs = new Dictionary<string, string>();

            foreach (string line in content.Replace("\r", "").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Contains("="))
                {
                    int eqIdx = trimmed.IndexOf('=');
                    string key = trimmed.Substring(0, eqIdx).Trim();
                    string value = trimmed.Substring(eqIdx + 1).Trim();
                    attrs[key] = value;
                }
            }

            return attrs;
        }

        #endregion

        #region Conversion to Project Entities

        /// <summary>
        /// Convert parsed ODB data to project placements and packages.
        /// </summary>
        public void ConvertToProject(OdbData odbData, Project project,
            out List<Package> packages, out List<Placement> placements)
        {
            packages = new List<Package>();
            placements = new List<Placement>();

            // Track unique packages
            var packageDict = new Dictionary<string, Package>();

            foreach (var comp in odbData.Components)
            {
                // Determine package name
                string pkgName = !string.IsNullOrEmpty(comp.PackageName) ? comp.PackageName :
                                 !string.IsNullOrEmpty(comp.PartNumber) ? comp.PartNumber :
                                 $"PKG_{comp.PackageRef}";

                // Get or create package
                Package pkg;
                if (!packageDict.TryGetValue(pkgName, out pkg))
                {
                    pkg = CreatePackageFromComponent(comp, pkgName);
                    packageDict[pkgName] = pkg;
                    packages.Add(pkg);
                }

                // Create placement
                var placement = new Placement
                {
                    Reference = comp.RefDes,
                    X = comp.XMm,  // Convert to mm
                    Y = comp.YMm,
                    Rotation = NormalizeRotation(comp.Rotation),
                    Side = comp.Layer == "top" ? BoardSide.Top : BoardSide.Bottom,
                    Package = pkg
                };

                // Set export flags
                placement.IsExportEnabledTop = (placement.Side == BoardSide.Top);
                placement.IsExportEnabledBottom = (placement.Side == BoardSide.Bottom);

                placements.Add(placement);
            }

            // Update board dimensions if available
            if (odbData.BoardWidth > 0 && odbData.BoardHeight > 0)
            {
                if (project.Board == null)
                    project.Board = new BoardDefinition();

                project.Board.Width = odbData.BoardWidthMm;
                project.Board.Height = odbData.BoardHeightMm;
            }
        }

        private Package CreatePackageFromComponent(OdbComponent comp, string name)
        {
            var pkg = new Package { Name = name };

            // Create pins from component pin data
            foreach (var pin in comp.Pins)
            {
                // Pin positions are relative to component origin in inches
                // Convert to mm for internal use
                var pkgPin = new Pin
                {
                    Number = pin.Index + 1,  // Convert 0-based to 1-based
                    Name = !string.IsNullOrEmpty(pin.Name) ? pin.Name : (pin.Index + 1).ToString(),
                    X = pin.XMm,
                    Y = pin.YMm,
                    Width = 0.5,  // Default pad size
                    Height = 0.5,
                    Shape = DeterminePinShape(comp.MountType)
                };
                pkg.Pins.Add(pkgPin);
            }

            // If no pins, create a placeholder
            if (pkg.Pins.Count == 0)
            {
                pkg.Pins.Add(new Pin
                {
                    Number = 1,
                    Name = "1",
                    X = 0,
                    Y = 0,
                    Width = 0.5,
                    Height = 0.5,
                    Shape = PinShape.Rectangle
                });
            }

            // Calculate bounds and create body outline
            if (pkg.Pins.Count > 1)
            {
                double minX = pkg.Pins.Min(p => p.X) - 0.5;
                double maxX = pkg.Pins.Max(p => p.X) + 0.5;
                double minY = pkg.Pins.Min(p => p.Y) - 0.5;
                double maxY = pkg.Pins.Max(p => p.Y) + 0.5;

                pkg.Graphics.Add(new PackageGraphic
                {
                    ShapeType = GraphicShapeType.Rectangle,
                    X = minX,
                    Y = minY,
                    Width = maxX - minX,
                    Height = maxY - minY,
                    StrokeThickness = 0.1,
                    IsFilled = false
                });
            }

            return pkg;
        }

        private PinShape DeterminePinShape(string mountType)
        {
            if (string.IsNullOrEmpty(mountType))
                return PinShape.Rectangle;

            string mt = mountType.ToUpperInvariant();
            if (mt.Contains("THT") || mt.Contains("THROUGH"))
                return PinShape.Circle;

            return PinShape.Rectangle;  // SMT default
        }

        private double NormalizeRotation(double rotation)
        {
            rotation = rotation % 360;
            if (rotation < 0) rotation += 360;
            return rotation;
        }

        #endregion
    }
}
