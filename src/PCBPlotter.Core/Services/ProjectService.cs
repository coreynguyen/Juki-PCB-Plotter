using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PCBPlotter.Core.Events;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Service for project file operations (save/load)
    /// </summary>
    public class ProjectService
    {
        private const string FileVersion = "1.0";
        private static ProjectService _instance;

        public static ProjectService Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new ProjectService();
                return _instance;
            }
        }

        public Project CurrentProject { get; private set; }

        public event EventHandler<Project> ProjectLoaded;
        public event EventHandler<Project> ProjectSaved;
        public event EventHandler ProjectClosed;

        /// <summary>
        /// Create a new empty project
        /// </summary>
        public Project NewProject(string name = "Untitled")
        {
            CurrentProject = new Project
            {
                Name = name,
                Created = DateTime.Now,
                Modified = DateTime.Now
            };

            EventAggregator.Instance.Publish(new ProjectLoadedEvent { Project = CurrentProject });
            return CurrentProject;
        }

        /// <summary>
        /// Save project to file
        /// </summary>
        public bool SaveProject(string filePath = null)
        {
            if (CurrentProject == null) return false;

            if (string.IsNullOrEmpty(filePath))
                filePath = CurrentProject.FilePath;

            if (string.IsNullOrEmpty(filePath))
                return false;

            try
            {
                var doc = SerializeProject(CurrentProject);
                doc.Save(filePath);

                CurrentProject.FilePath = filePath;
                CurrentProject.Modified = DateTime.Now;

                var handler = ProjectSaved;
                if (handler != null) handler(this, CurrentProject);

                EventAggregator.Instance.Publish(new ProjectSavedEvent
                {
                    Project = CurrentProject,
                    FilePath = filePath
                });

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Save error: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Load project from file
        /// </summary>
        public Project LoadProject(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var doc = XDocument.Load(filePath);
                CurrentProject = DeserializeProject(doc);
                CurrentProject.FilePath = filePath;

                var handler = ProjectLoaded;
                if (handler != null) handler(this, CurrentProject);

                EventAggregator.Instance.Publish(new ProjectLoadedEvent { Project = CurrentProject });

                return CurrentProject;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Load error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Close the current project
        /// </summary>
        public void CloseProject()
        {
            CurrentProject = null;

            var handler = ProjectClosed;
            if (handler != null) handler(this, EventArgs.Empty);

            EventAggregator.Instance.Publish(new ProjectClosedEvent());
        }

        #region Serialization

        private XDocument SerializeProject(Project project)
        {
            var root = new XElement("PCBPlotterProject",
                new XAttribute("Version", FileVersion),
                new XElement("Name", project.Name ?? ""),
                new XElement("PcbNumber", project.PcbNumber ?? ""),
                new XElement("Revision", project.Revision ?? ""),
                new XElement("Created", project.Created.ToString("o")),
                new XElement("Modified", DateTime.Now.ToString("o")),
                new XElement("Units", project.Units.ToString()),
                new XElement("Side1Name", project.Side1Name ?? "SIDE1"),
                new XElement("Side2Name", project.Side2Name ?? "SIDE2")
            );

            // Board definition
            root.Add(SerializeBoard(project.Board));

            // Collections
            root.Add(new XElement("Packages", project.Packages.Select(SerializePackage)));
            root.Add(new XElement("Components", project.Components.Select(SerializeComponent)));
            root.Add(new XElement("Placements", project.Placements.Select(p => SerializePlacement(p, project))));
            root.Add(new XElement("Fiducials", project.Fiducials.Select(SerializeFiducial)));
            root.Add(new XElement("CircuitInstances", project.CircuitInstances.Select(SerializeCircuitInstance)));
            root.Add(new XElement("SelectionSets", project.SelectionSets.Select(SerializeSelectionSet)));

            return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        }

        private XElement SerializeBoard(BoardDefinition board)
        {
            return new XElement("Board",
                new XElement("Width", board.Width),
                new XElement("Height", board.Height),
                new XElement("Thickness", board.Thickness),
                new XElement("OriginX", board.Origin.X),
                new XElement("OriginY", board.Origin.Y),
                new XElement("LowerLeftX", board.LowerLeftOffset.X),
                new XElement("LowerLeftY", board.LowerLeftOffset.Y),
                new XElement("CircuitCountX", board.CircuitCountX),
                new XElement("CircuitCountY", board.CircuitCountY),
                new XElement("PitchX", board.PitchX),
                new XElement("PitchY", board.PitchY),
                new XElement("CircuitStartX", board.CircuitStartPosition.X),
                new XElement("CircuitStartY", board.CircuitStartPosition.Y)
            );
        }

        private XElement SerializePackage(Package pkg)
        {
            var elem = new XElement("Package",
                new XAttribute("Id", pkg.Id),
                new XElement("Name", pkg.Name ?? ""),
                new XElement("Description", pkg.Description ?? ""),
                new XElement("OriginX", pkg.Origin.X),
                new XElement("OriginY", pkg.Origin.Y),
                new XElement("Width", pkg.Width),
                new XElement("Length", pkg.Length),
                new XElement("Height", pkg.Height),
                new XElement("PartClass", pkg.PartClass.ToString()),
                new XElement("HasPolarity", pkg.HasPolarity),
                new XElement("DefaultRotation", pkg.DefaultRotation)
            );

            elem.Add(new XElement("Graphics", pkg.Graphics.Select(SerializeGraphic)));
            elem.Add(new XElement("Pins", pkg.Pins.Select(SerializePin)));

            return elem;
        }

        private XElement SerializeGraphic(PackageGraphic g)
        {
            var elem = new XElement("Graphic",
                new XAttribute("Type", g.ShapeType.ToString()),
                new XElement("X", g.X),
                new XElement("Y", g.Y),
                new XElement("Width", g.Width),
                new XElement("Height", g.Height),
                new XElement("Rotation", g.Rotation),
                new XElement("CornerRadius", g.CornerRadius),
                new XElement("IsFilled", g.IsFilled),
                new XElement("IsPad", g.IsPad),
                new XElement("IsPin1Indicator", g.IsPin1Indicator),
                new XElement("StrokeThickness", g.StrokeThickness),
                new XElement("FillColor", g.FillColorArgb),
                new XElement("StrokeColor", g.StrokeColorArgb)
            );

            if (!string.IsNullOrEmpty(g.Text))
            {
                elem.Add(new XElement("Text", g.Text));
                elem.Add(new XElement("TextSize", g.TextSize));
            }

            if (g.Points != null && g.Points.Count > 0)
            {
                elem.Add(new XElement("Points",
                    g.Points.Select(p => new XElement("Point",
                        new XAttribute("X", p.X),
                        new XAttribute("Y", p.Y)))));
            }

            return elem;
        }

        private XElement SerializePin(Pin pin)
        {
            return new XElement("Pin",
                new XAttribute("Number", pin.Number),
                new XElement("Name", pin.Name ?? ""),
                new XElement("X", pin.X),
                new XElement("Y", pin.Y),
                new XElement("Width", pin.Width),
                new XElement("Height", pin.Height),
                new XElement("Shape", pin.Shape.ToString())
            );
        }

        private XElement SerializeComponent(Component comp)
        {
            return new XElement("Component",
                new XAttribute("Id", comp.Id),
                new XElement("PartNumber", comp.PartNumber ?? ""),
                new XElement("Description", comp.Description ?? ""),
                new XElement("Manufacturer", comp.Manufacturer ?? ""),
                new XElement("ManufacturerPartNumber", comp.ManufacturerPartNumber ?? ""),
                new XElement("Value", comp.Value ?? ""),
                new XElement("DefaultPackageId", comp.DefaultPackage != null ? comp.DefaultPackage.Id : ""),
                new XElement("ReferenceDesignators", string.Join(",", comp.ReferenceDesignators ?? new List<string>()))
            );
        }

        private XElement SerializePlacement(Placement p, Project project)
        {
            return new XElement("Placement",
                new XAttribute("Id", p.Id),
                new XElement("Reference", p.Reference ?? ""),
                new XElement("X", p.X),
                new XElement("Y", p.Y),
                new XElement("Rotation", p.Rotation),
                new XElement("Side", p.Side.ToString()),
                new XElement("ComponentId", p.Component != null ? p.Component.Id : ""),
                new XElement("PackageId", p.Package != null ? p.Package.Id : ""),
                new XElement("IsExportEnabledTop", p.IsExportEnabledTop),
                new XElement("IsExportEnabledBottom", p.IsExportEnabledBottom),
                new XElement("IsVisible", p.IsVisible)
            );
        }

        private XElement SerializeFiducial(Fiducial f)
        {
            return new XElement("Fiducial",
                new XAttribute("Id", f.Id),
                new XElement("Name", f.Name ?? ""),
                new XElement("X", f.X),
                new XElement("Y", f.Y),
                new XElement("Diameter", f.Diameter),
                new XElement("Type", f.Type.ToString()),
                new XElement("Side", f.Side.ToString()),
                new XElement("SharedBetweenSides", f.SharedBetweenSides),
                new XElement("CircuitIndex", f.CircuitIndex)
            );
        }

        private XElement SerializeCircuitInstance(CircuitInstance ci)
        {
            return new XElement("CircuitInstance",
                new XAttribute("Id", ci.Id),
                new XElement("Name", ci.Name ?? ""),
                new XElement("IndexX", ci.IndexX),
                new XElement("IndexY", ci.IndexY),
                new XElement("OffsetX", ci.OffsetX),
                new XElement("OffsetY", ci.OffsetY),
                new XElement("Rotation", ci.Rotation),
                new XElement("IsVisible", ci.IsVisible),
                new XElement("IsEnabled", ci.IsEnabled)
            );
        }

        private XElement SerializeSelectionSet(SelectionSet ss)
        {
            return new XElement("SelectionSet",
                new XAttribute("Id", ss.Id),
                new XElement("Name", ss.Name ?? ""),
                new XElement("Description", ss.Description ?? ""),
                new XElement("Created", ss.Created.ToString("o")),
                new XElement("TargetSide", ss.TargetSide.HasValue ? ss.TargetSide.Value.ToString() : ""),
                new XElement("PlacementIds", string.Join(",", ss.PlacementIds))
            );
        }

        #endregion

        #region Deserialization

        private Project DeserializeProject(XDocument doc)
        {
            var root = doc.Root;
            var project = new Project();

            project.Name = (string)root.Element("Name") ?? "";
            project.PcbNumber = (string)root.Element("PcbNumber") ?? "";
            project.Revision = (string)root.Element("Revision") ?? "";

            DateTime created;
            if (DateTime.TryParse((string)root.Element("Created"), out created))
                project.Created = created;

            Units units;
            if (Enum.TryParse((string)root.Element("Units"), out units))
                project.Units = units;

            project.Side1Name = (string)root.Element("Side1Name") ?? "SIDE1";
            project.Side2Name = (string)root.Element("Side2Name") ?? "SIDE2";

            // Board
            var boardElem = root.Element("Board");
            if (boardElem != null)
            {
                project.Board = DeserializeBoard(boardElem);
            }

            // Packages (must be loaded first for references)
            var packagesElem = root.Element("Packages");
            if (packagesElem != null)
            {
                foreach (var pkgElem in packagesElem.Elements("Package"))
                {
                    project.Packages.Add(DeserializePackage(pkgElem));
                }
            }

            // Components
            var componentsElem = root.Element("Components");
            if (componentsElem != null)
            {
                foreach (var compElem in componentsElem.Elements("Component"))
                {
                    project.Components.Add(DeserializeComponent(compElem, project));
                }
            }

            // Placements
            var placementsElem = root.Element("Placements");
            if (placementsElem != null)
            {
                foreach (var pElem in placementsElem.Elements("Placement"))
                {
                    project.Placements.Add(DeserializePlacement(pElem, project));
                }
            }

            // Fiducials
            var fiducialsElem = root.Element("Fiducials");
            if (fiducialsElem != null)
            {
                foreach (var fElem in fiducialsElem.Elements("Fiducial"))
                {
                    project.Fiducials.Add(DeserializeFiducial(fElem));
                }
            }

            // Circuit Instances
            var circuitsElem = root.Element("CircuitInstances");
            if (circuitsElem != null)
            {
                foreach (var ciElem in circuitsElem.Elements("CircuitInstance"))
                {
                    project.CircuitInstances.Add(DeserializeCircuitInstance(ciElem));
                }
            }

            // Selection Sets
            var setsElem = root.Element("SelectionSets");
            if (setsElem != null)
            {
                foreach (var ssElem in setsElem.Elements("SelectionSet"))
                {
                    project.SelectionSets.Add(DeserializeSelectionSet(ssElem));
                }
            }

            return project;
        }

        private BoardDefinition DeserializeBoard(XElement elem)
        {
            var board = new BoardDefinition();

            board.Width = (double?)elem.Element("Width") ?? 0;
            board.Height = (double?)elem.Element("Height") ?? 0;
            board.Thickness = (double?)elem.Element("Thickness") ?? 1.6;
            board.Origin = new System.Windows.Point(
                (double?)elem.Element("OriginX") ?? 0,
                (double?)elem.Element("OriginY") ?? 0);
            board.LowerLeftOffset = new System.Windows.Point(
                (double?)elem.Element("LowerLeftX") ?? 0,
                (double?)elem.Element("LowerLeftY") ?? 0);
            board.CircuitCountX = (int?)elem.Element("CircuitCountX") ?? 1;
            board.CircuitCountY = (int?)elem.Element("CircuitCountY") ?? 1;
            board.PitchX = (double?)elem.Element("PitchX") ?? 0;
            board.PitchY = (double?)elem.Element("PitchY") ?? 0;
            board.CircuitStartPosition = new System.Windows.Point(
                (double?)elem.Element("CircuitStartX") ?? 0,
                (double?)elem.Element("CircuitStartY") ?? 0);

            return board;
        }

        private Package DeserializePackage(XElement elem)
        {
            var pkg = new Package();

            pkg.Id = (string)elem.Attribute("Id") ?? Guid.NewGuid().ToString();
            pkg.Name = (string)elem.Element("Name") ?? "";
            pkg.Description = (string)elem.Element("Description") ?? "";
            pkg.Origin = new System.Windows.Point(
                (double?)elem.Element("OriginX") ?? 0,
                (double?)elem.Element("OriginY") ?? 0);
            pkg.Width = (double?)elem.Element("Width") ?? 0;
            pkg.Length = (double?)elem.Element("Length") ?? 0;
            pkg.Height = (double?)elem.Element("Height") ?? 0;

            PartClass partClass;
            if (Enum.TryParse((string)elem.Element("PartClass"), out partClass))
                pkg.PartClass = partClass;

            pkg.HasPolarity = (bool?)elem.Element("HasPolarity") ?? false;
            pkg.DefaultRotation = (double?)elem.Element("DefaultRotation") ?? 0;

            // Graphics
            var graphicsElem = elem.Element("Graphics");
            if (graphicsElem != null)
            {
                foreach (var gElem in graphicsElem.Elements("Graphic"))
                {
                    pkg.Graphics.Add(DeserializeGraphic(gElem));
                }
            }

            // Pins
            var pinsElem = elem.Element("Pins");
            if (pinsElem != null)
            {
                foreach (var pinElem in pinsElem.Elements("Pin"))
                {
                    pkg.Pins.Add(DeserializePin(pinElem));
                }
            }

            return pkg;
        }

        private PackageGraphic DeserializeGraphic(XElement elem)
        {
            var g = new PackageGraphic();

            GraphicShapeType shapeType;
            if (Enum.TryParse((string)elem.Attribute("Type"), out shapeType))
                g.ShapeType = shapeType;

            g.X = (double?)elem.Element("X") ?? 0;
            g.Y = (double?)elem.Element("Y") ?? 0;
            g.Width = (double?)elem.Element("Width") ?? 0;
            g.Height = (double?)elem.Element("Height") ?? 0;
            g.Rotation = (double?)elem.Element("Rotation") ?? 0;
            g.CornerRadius = (double?)elem.Element("CornerRadius") ?? 0;
            g.IsFilled = (bool?)elem.Element("IsFilled") ?? true;
            g.IsPad = (bool?)elem.Element("IsPad") ?? false;
            g.IsPin1Indicator = (bool?)elem.Element("IsPin1Indicator") ?? false;
            g.StrokeThickness = (double?)elem.Element("StrokeThickness") ?? 0.1;
            g.FillColorArgb = (uint?)elem.Element("FillColor") ?? 0xFF4A4A4A;
            g.StrokeColorArgb = (uint?)elem.Element("StrokeColor") ?? 0xFFFFFFFF;
            g.Text = (string)elem.Element("Text");
            g.TextSize = (double?)elem.Element("TextSize") ?? 0.5;

            var pointsElem = elem.Element("Points");
            if (pointsElem != null)
            {
                foreach (var ptElem in pointsElem.Elements("Point"))
                {
                    g.Points.Add(new System.Windows.Point(
                        (double?)ptElem.Attribute("X") ?? 0,
                        (double?)ptElem.Attribute("Y") ?? 0));
                }
            }

            return g;
        }

        private Pin DeserializePin(XElement elem)
        {
            var pin = new Pin();

            pin.Number = (int?)elem.Attribute("Number") ?? 1;
            pin.Name = (string)elem.Element("Name") ?? "";
            pin.X = (double?)elem.Element("X") ?? 0;
            pin.Y = (double?)elem.Element("Y") ?? 0;
            pin.Width = (double?)elem.Element("Width") ?? 0.3;
            pin.Height = (double?)elem.Element("Height") ?? 0.3;

            PinShape shape;
            if (Enum.TryParse((string)elem.Element("Shape"), out shape))
                pin.Shape = shape;

            return pin;
        }

        private Component DeserializeComponent(XElement elem, Project project)
        {
            var comp = new Component();

            comp.Id = (string)elem.Attribute("Id") ?? Guid.NewGuid().ToString();
            comp.PartNumber = (string)elem.Element("PartNumber") ?? "";
            comp.Description = (string)elem.Element("Description") ?? "";
            comp.Manufacturer = (string)elem.Element("Manufacturer") ?? "";
            comp.ManufacturerPartNumber = (string)elem.Element("ManufacturerPartNumber") ?? "";
            comp.Value = (string)elem.Element("Value") ?? "";

            var defaultPkgId = (string)elem.Element("DefaultPackageId");
            if (!string.IsNullOrEmpty(defaultPkgId))
            {
                comp.DefaultPackage = project.Packages.FirstOrDefault(p => p.Id == defaultPkgId);
            }

            var refDes = (string)elem.Element("ReferenceDesignators");
            if (!string.IsNullOrEmpty(refDes))
            {
                comp.ReferenceDesignators = refDes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            return comp;
        }

        private Placement DeserializePlacement(XElement elem, Project project)
        {
            var p = new Placement();

            p.Id = (string)elem.Attribute("Id") ?? Guid.NewGuid().ToString();
            p.Reference = (string)elem.Element("Reference") ?? "";
            p.X = (double?)elem.Element("X") ?? 0;
            p.Y = (double?)elem.Element("Y") ?? 0;
            p.Rotation = (double?)elem.Element("Rotation") ?? 0;

            BoardSide side;
            if (Enum.TryParse((string)elem.Element("Side"), out side))
                p.Side = side;

            var compId = (string)elem.Element("ComponentId");
            if (!string.IsNullOrEmpty(compId))
            {
                p.Component = project.Components.FirstOrDefault(c => c.Id == compId);
            }

            var pkgId = (string)elem.Element("PackageId");
            if (!string.IsNullOrEmpty(pkgId))
            {
                p.Package = project.Packages.FirstOrDefault(pk => pk.Id == pkgId);
            }

            p.IsExportEnabledTop = (bool?)elem.Element("IsExportEnabledTop") ?? false;
            p.IsExportEnabledBottom = (bool?)elem.Element("IsExportEnabledBottom") ?? false;
            p.IsVisible = (bool?)elem.Element("IsVisible") ?? true;

            return p;
        }

        private Fiducial DeserializeFiducial(XElement elem)
        {
            var f = new Fiducial();

            f.Id = (string)elem.Attribute("Id") ?? Guid.NewGuid().ToString();
            f.Name = (string)elem.Element("Name") ?? "";
            f.X = (double?)elem.Element("X") ?? 0;
            f.Y = (double?)elem.Element("Y") ?? 0;
            f.Diameter = (double?)elem.Element("Diameter") ?? 1.0;

            FiducialType fType;
            if (Enum.TryParse((string)elem.Element("Type"), out fType))
                f.Type = fType;

            BoardSide fSide;
            if (Enum.TryParse((string)elem.Element("Side"), out fSide))
                f.Side = fSide;

            f.SharedBetweenSides = (bool?)elem.Element("SharedBetweenSides") ?? true;
            f.CircuitIndex = (int?)elem.Element("CircuitIndex") ?? -1;

            return f;
        }

        private CircuitInstance DeserializeCircuitInstance(XElement elem)
        {
            var ci = new CircuitInstance();

            ci.Id = (string)elem.Attribute("Id") ?? Guid.NewGuid().ToString();
            ci.Name = (string)elem.Element("Name") ?? "";
            ci.IndexX = (int?)elem.Element("IndexX") ?? 0;
            ci.IndexY = (int?)elem.Element("IndexY") ?? 0;
            ci.OffsetX = (double?)elem.Element("OffsetX") ?? 0;
            ci.OffsetY = (double?)elem.Element("OffsetY") ?? 0;
            ci.Rotation = (double?)elem.Element("Rotation") ?? 0;
            ci.IsVisible = (bool?)elem.Element("IsVisible") ?? true;
            ci.IsEnabled = (bool?)elem.Element("IsEnabled") ?? true;

            return ci;
        }

        private SelectionSet DeserializeSelectionSet(XElement elem)
        {
            var ss = new SelectionSet();

            ss.Id = (string)elem.Attribute("Id") ?? Guid.NewGuid().ToString();
            ss.Name = (string)elem.Element("Name") ?? "";
            ss.Description = (string)elem.Element("Description") ?? "";

            DateTime created;
            if (DateTime.TryParse((string)elem.Element("Created"), out created))
                ss.Created = created;

            var targetSideStr = (string)elem.Element("TargetSide");
            if (!string.IsNullOrEmpty(targetSideStr))
            {
                BoardSide targetSide;
                if (Enum.TryParse(targetSideStr, out targetSide))
                    ss.TargetSide = targetSide;
            }

            var placementIds = (string)elem.Element("PlacementIds");
            if (!string.IsNullOrEmpty(placementIds))
            {
                ss.PlacementIds = placementIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            return ss;
        }

        #endregion
    }
}
