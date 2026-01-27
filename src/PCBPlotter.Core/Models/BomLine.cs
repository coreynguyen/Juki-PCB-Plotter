using System;
using System.Collections.Generic;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Represents a single line in a Bill of Materials
    /// </summary>
    public class BomLine : ModelBase
    {
        private string _partNumber;
        private string _value;
        private string _packageName;
        private string _manufacturer;
        private string _manufacturerPartNumber;
        private string _description;
        private int _quantity;
        private List<string> _references;

        /// <summary>
        /// Internal part number
        /// </summary>
        public string PartNumber
        {
            get { return _partNumber; }
            set { SetProperty(ref _partNumber, value); }
        }

        /// <summary>
        /// Component value (e.g., "10uF", "4.7k")
        /// </summary>
        public string Value
        {
            get { return _value; }
            set { SetProperty(ref _value, value); }
        }

        /// <summary>
        /// Package/footprint name
        /// </summary>
        public string PackageName
        {
            get { return _packageName; }
            set { SetProperty(ref _packageName, value); }
        }

        /// <summary>
        /// Manufacturer name
        /// </summary>
        public string Manufacturer
        {
            get { return _manufacturer; }
            set { SetProperty(ref _manufacturer, value); }
        }

        /// <summary>
        /// Manufacturer part number (MPN)
        /// </summary>
        public string ManufacturerPartNumber
        {
            get { return _manufacturerPartNumber; }
            set { SetProperty(ref _manufacturerPartNumber, value); }
        }

        /// <summary>
        /// Part description
        /// </summary>
        public string Description
        {
            get { return _description; }
            set { SetProperty(ref _description, value); }
        }

        /// <summary>
        /// Quantity (can be derived from reference count)
        /// </summary>
        public int Quantity
        {
            get { return _quantity; }
            set { SetProperty(ref _quantity, value); }
        }

        /// <summary>
        /// List of reference designators (expanded from ranges like "C1-5")
        /// </summary>
        public List<string> References
        {
            get { return _references; }
            set
            {
                if (SetProperty(ref _references, value))
                {
                    OnPropertyChanged("ReferenceCount");
                }
            }
        }

        /// <summary>
        /// Number of references (for display/binding)
        /// </summary>
        public int ReferenceCount
        {
            get { return _references != null ? _references.Count : 0; }
        }

        /// <summary>
        /// Gets references as a comma-separated string
        /// </summary>
        public string ReferencesText
        {
            get
            {
                return _references != null ? string.Join(", ", _references) : "";
            }
        }

        public BomLine()
        {
            _references = new List<string>();
        }

        public override string ToString()
        {
            return string.Format("{0} x{1} ({2})", PartNumber ?? "(no P/N)", Quantity, Value ?? "");
        }
    }
}
