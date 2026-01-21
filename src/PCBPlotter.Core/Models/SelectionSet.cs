using System;
using System.Collections.Generic;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// A named set of placement selections that can be saved and recalled
    /// </summary>
    public class SelectionSet : ModelBase
    {
        private string _id;
        private string _name;
        private string _description;
        private DateTime _created;
        private List<string> _placementIds;
        private BoardSide? _targetSide;

        /// <summary>
        /// Unique identifier
        /// </summary>
        public string Id
        {
            get { return _id; }
            set { SetProperty(ref _id, value); }
        }

        /// <summary>
        /// Display name for this selection set
        /// </summary>
        public string Name
        {
            get { return _name; }
            set { SetProperty(ref _name, value); }
        }

        /// <summary>
        /// Optional description
        /// </summary>
        public string Description
        {
            get { return _description; }
            set { SetProperty(ref _description, value); }
        }

        /// <summary>
        /// When this selection set was created
        /// </summary>
        public DateTime Created
        {
            get { return _created; }
            set { SetProperty(ref _created, value); }
        }

        /// <summary>
        /// List of placement IDs in this selection
        /// </summary>
        public List<string> PlacementIds
        {
            get { return _placementIds; }
            set { SetProperty(ref _placementIds, value); }
        }

        /// <summary>
        /// Optional target side for this selection (for export assignments)
        /// </summary>
        public BoardSide? TargetSide
        {
            get { return _targetSide; }
            set { SetProperty(ref _targetSide, value); }
        }

        /// <summary>
        /// Number of placements in this set
        /// </summary>
        public int Count
        {
            get { return _placementIds != null ? _placementIds.Count : 0; }
        }

        public SelectionSet()
        {
            _id = Guid.NewGuid().ToString();
            _created = DateTime.Now;
            _placementIds = new List<string>();
        }

        public SelectionSet(string name) : this()
        {
            _name = name;
        }

        public SelectionSet(string name, IEnumerable<Placement> placements) : this(name)
        {
            foreach (var placement in placements)
            {
                _placementIds.Add(placement.Id);
            }
        }

        /// <summary>
        /// Adds a placement to this set
        /// </summary>
        public void Add(Placement placement)
        {
            if (placement != null && !_placementIds.Contains(placement.Id))
            {
                _placementIds.Add(placement.Id);
                OnPropertyChanged("Count");
            }
        }

        /// <summary>
        /// Removes a placement from this set
        /// </summary>
        public void Remove(Placement placement)
        {
            if (placement != null && _placementIds.Remove(placement.Id))
            {
                OnPropertyChanged("Count");
            }
        }

        /// <summary>
        /// Checks if a placement is in this set
        /// </summary>
        public bool Contains(Placement placement)
        {
            return placement != null && _placementIds.Contains(placement.Id);
        }

        /// <summary>
        /// Clears all placements from this set
        /// </summary>
        public void Clear()
        {
            _placementIds.Clear();
            OnPropertyChanged("Count");
        }

        public override string ToString()
        {
            return string.Format("{0} ({1} items)", Name ?? "Unnamed", Count);
        }
    }
}
