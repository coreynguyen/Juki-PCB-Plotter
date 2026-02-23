using System.Collections.Generic;
using System.Linq;
using PCBPlotter.Core.Events;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Undoable command for adding a placement from Gerber selection.
    /// Handles the full lifecycle: placement, package (if new), consumed primitives, and consumed regions.
    /// </summary>
    public class AddPlacementFromSelectionCommand : IUndoableCommand
    {
        private readonly Project _project;
        private readonly Placement _placement;
        private readonly Package _package;
        private readonly bool _isNewPackage;
        private readonly List<GerberPrimitive> _consumedPrimitives;
        private readonly GerberLayer _layer;
        private readonly string _reference;

        public string Description => $"Add placement '{_placement.Reference}'";

        /// <summary>
        /// Creates a new AddPlacementFromSelectionCommand.
        /// </summary>
        /// <param name="project">The project to add the placement to</param>
        /// <param name="placement">The placement to add</param>
        /// <param name="package">The package for the placement</param>
        /// <param name="isNewPackage">True if this is a new package that should be added to project</param>
        /// <param name="consumedPrimitives">The primitives to mark as consumed</param>
        /// <param name="layer">The layer to add consumed regions to</param>
        public AddPlacementFromSelectionCommand(
            Project project,
            Placement placement,
            Package package,
            bool isNewPackage,
            List<GerberPrimitive> consumedPrimitives,
            GerberLayer layer)
        {
            _project = project;
            _placement = placement;
            _package = package;
            _isNewPackage = isNewPackage;
            _consumedPrimitives = consumedPrimitives;
            _layer = layer;
            _reference = placement.Reference;
        }

        public void Execute()
        {
            // Add new package if needed
            if (_isNewPackage && !_project.Packages.Contains(_package))
            {
                _project.Packages.Add(_package);
                EventAggregator.Instance.Publish(new PackageAddedEvent { Package = _package });
            }

            // Add placement
            if (!_project.Placements.Contains(_placement))
            {
                _project.Placements.Add(_placement);
                EventAggregator.Instance.Publish(new PlacementAddedEvent { Placement = _placement });
            }

            // Mark primitives as consumed
            foreach (var prim in _consumedPrimitives)
            {
                prim.IsConsumed = true;
                prim.IsSelected = false;
            }

            // Add consumed regions to the layer
            if (_layer != null)
            {
                _layer.AddConsumedRegions(_consumedPrimitives, _reference);
            }

            EventAggregator.Instance.Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        public void Undo()
        {
            // Remove consumed regions from the layer
            if (_layer != null)
            {
                _layer.RemoveConsumedRegionsByReference(_reference);
            }

            // Unmark primitives as consumed
            foreach (var prim in _consumedPrimitives)
            {
                prim.IsConsumed = false;
            }

            // Remove placement
            if (_project.Placements.Contains(_placement))
            {
                _project.Placements.Remove(_placement);
                EventAggregator.Instance.Publish(new PlacementRemovedEvent { Placement = _placement });
            }

            // Remove package if it was newly added and no other placements use it
            if (_isNewPackage)
            {
                bool packageStillUsed = _project.Placements.Any(p => p.Package == _package);
                if (!packageStillUsed && _project.Packages.Contains(_package))
                {
                    _project.Packages.Remove(_package);
                    EventAggregator.Instance.Publish(new PackageRemovedEvent { Package = _package });
                }
            }

            EventAggregator.Instance.Publish(new RequestRefreshEvent { FullRefresh = true });
        }
    }
}
