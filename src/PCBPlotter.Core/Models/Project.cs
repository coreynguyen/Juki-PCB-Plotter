using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Root project model containing all PCB plotter data
    /// </summary>
    public class Project : ModelBase
    {
        private string _name;
        private string _filePath;
        private string _pcbNumber;
        private string _revision;
        private DateTime _created;
        private DateTime _modified;
        private Units _units = Units.Millimeters;
        private string _side1Name = "SIDE1";
        private string _side2Name = "SIDE2";
        private BoardDefinition _board;

        public string Name
        {
            get { return _name; }
            set { SetProperty(ref _name, value); }
        }

        public string FilePath
        {
            get { return _filePath; }
            set { SetProperty(ref _filePath, value); }
        }

        public string PcbNumber
        {
            get { return _pcbNumber; }
            set { SetProperty(ref _pcbNumber, value); }
        }

        public string Revision
        {
            get { return _revision; }
            set { SetProperty(ref _revision, value); }
        }

        public DateTime Created
        {
            get { return _created; }
            set { SetProperty(ref _created, value); }
        }

        public DateTime Modified
        {
            get { return _modified; }
            set { SetProperty(ref _modified, value); }
        }

        public Units Units
        {
            get { return _units; }
            set { SetProperty(ref _units, value); }
        }

        public string Side1Name
        {
            get { return _side1Name; }
            set { SetProperty(ref _side1Name, value); }
        }

        public string Side2Name
        {
            get { return _side2Name; }
            set { SetProperty(ref _side2Name, value); }
        }

        public BoardDefinition Board
        {
            get { return _board; }
            set { SetProperty(ref _board, value); }
        }

        public ObservableCollection<Placement> Placements { get; private set; }
        public ObservableCollection<Component> Components { get; private set; }
        public ObservableCollection<Package> Packages { get; private set; }
        public ObservableCollection<Fiducial> Fiducials { get; private set; }
        public ObservableCollection<CircuitInstance> CircuitInstances { get; private set; }
        public ObservableCollection<SelectionSet> SelectionSets { get; private set; }
        public ObservableCollection<GerberLayer> GerberLayers { get; private set; }

        public Project()
        {
            _created = DateTime.Now;
            _modified = DateTime.Now;
            _board = new BoardDefinition();
            Placements = new ObservableCollection<Placement>();
            Components = new ObservableCollection<Component>();
            Packages = new ObservableCollection<Package>();
            Fiducials = new ObservableCollection<Fiducial>();
            CircuitInstances = new ObservableCollection<CircuitInstance>();
            SelectionSets = new ObservableCollection<SelectionSet>();
            GerberLayers = new ObservableCollection<GerberLayer>();
        }

        public void MarkModified()
        {
            Modified = DateTime.Now;
        }
    }

    /// <summary>
    /// Base class for all models with property change notification
    /// </summary>
    public abstract class ModelBase : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
