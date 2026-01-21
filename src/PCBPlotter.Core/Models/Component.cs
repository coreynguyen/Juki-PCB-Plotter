using System;
using System.Collections.Generic;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Represents a component from the BOM
    /// </summary>
    public class Component : ModelBase
    {
        private string _id;
        private string _partNumber;
        private string _description;
        private string _manufacturer;
        private string _manufacturerPartNumber;
        private string _value;
        private Package _defaultPackage;
        private ComponentStatus _status = ComponentStatus.Valid;
        private List<string> _referenceDesignators;
        private string _feederSlot;
        private string _tapeWidth;
        private string _tapePitch;

        /// <summary>
        /// Unique identifier
        /// </summary>
        public string Id
        {
            get { return _id; }
            set { SetProperty(ref _id, value); }
        }

        /// <summary>
        /// Internal part number
        /// </summary>
        public string PartNumber
        {
            get { return _partNumber; }
            set
            {
                SetProperty(ref _partNumber, value);
                UpdateStatus();
            }
        }

        /// <summary>
        /// Part description (used as comment in export)
        /// </summary>
        public string Description
        {
            get { return _description; }
            set { SetProperty(ref _description, value); }
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
        /// Manufacturer part number
        /// </summary>
        public string ManufacturerPartNumber
        {
            get { return _manufacturerPartNumber; }
            set { SetProperty(ref _manufacturerPartNumber, value); }
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
        /// Default package for this component
        /// </summary>
        public Package DefaultPackage
        {
            get { return _defaultPackage; }
            set { SetProperty(ref _defaultPackage, value); }
        }

        /// <summary>
        /// Validation status
        /// </summary>
        public ComponentStatus Status
        {
            get { return _status; }
            set { SetProperty(ref _status, value); }
        }

        /// <summary>
        /// List of reference designators using this component (e.g., C1, C2, C3)
        /// </summary>
        public List<string> ReferenceDesignators
        {
            get { return _referenceDesignators; }
            set { SetProperty(ref _referenceDesignators, value); }
        }

        /// <summary>
        /// Count of placements using this component
        /// </summary>
        public int PlacementCount
        {
            get { return _referenceDesignators != null ? _referenceDesignators.Count : 0; }
        }

        /// <summary>
        /// Alias for DefaultPackage (used in UI bindings)
        /// </summary>
        public Package Package
        {
            get { return _defaultPackage; }
            set { DefaultPackage = value; }
        }

        /// <summary>
        /// Feeder slot assignment for pick and place machine
        /// </summary>
        public string FeederSlot
        {
            get { return _feederSlot; }
            set { SetProperty(ref _feederSlot, value); }
        }

        /// <summary>
        /// Tape width for component packaging (8mm, 12mm, 16mm, etc.)
        /// </summary>
        public string TapeWidth
        {
            get { return _tapeWidth; }
            set { SetProperty(ref _tapeWidth, value); }
        }

        /// <summary>
        /// Tape pitch (component spacing in tape)
        /// </summary>
        public string TapePitch
        {
            get { return _tapePitch; }
            set { SetProperty(ref _tapePitch, value); }
        }

        public Component()
        {
            _id = Guid.NewGuid().ToString();
            _referenceDesignators = new List<string>();
        }

        public Component(string partNumber, string description = null)
            : this()
        {
            _partNumber = partNumber;
            _description = description;
        }

        private void UpdateStatus()
        {
            ComponentStatus newStatus = ComponentStatus.Valid;

            if (string.IsNullOrEmpty(_partNumber))
                newStatus |= ComponentStatus.NoPartNumber;

            if (_referenceDesignators == null || _referenceDesignators.Count == 0)
                newStatus |= ComponentStatus.NoPlacements;

            Status = newStatus;
        }

        /// <summary>
        /// Parses reference designator ranges like "C1-5" or "C1,2,3,4,5"
        /// </summary>
        public static List<string> ParseReferenceRange(string input)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(input))
                return result;

            input = input.Trim();

            // Extract prefix (letters) and numeric part
            string prefix = "";
            int prefixEnd = 0;

            for (int i = 0; i < input.Length; i++)
            {
                if (char.IsLetter(input[i]))
                {
                    prefix += input[i];
                    prefixEnd = i + 1;
                }
                else
                {
                    break;
                }
            }

            if (string.IsNullOrEmpty(prefix))
                return result;

            string numbers = input.Substring(prefixEnd).Trim();

            // Handle range format: C1-5
            if (numbers.Contains("-"))
            {
                var parts = numbers.Split('-');
                if (parts.Length == 2)
                {
                    int start, end;
                    if (int.TryParse(parts[0].Trim(), out start) &&
                        int.TryParse(parts[1].Trim(), out end))
                    {
                        for (int i = start; i <= end; i++)
                        {
                            result.Add(prefix + i);
                        }
                    }
                }
            }
            // Handle comma-separated format: C1,2,3,4,5 or C1, C2, C3
            else if (numbers.Contains(","))
            {
                var parts = numbers.Split(',');
                foreach (var part in parts)
                {
                    string trimmed = part.Trim();
                    // Check if it's just a number or includes prefix
                    int num;
                    if (int.TryParse(trimmed, out num))
                    {
                        result.Add(prefix + num);
                    }
                    else if (trimmed.StartsWith(prefix))
                    {
                        result.Add(trimmed);
                    }
                }
            }
            // Single reference
            else
            {
                result.Add(input);
            }

            return result;
        }

        public override string ToString()
        {
            return string.Format("{0} - {1}", PartNumber ?? "(no P/N)", Description ?? "");
        }
    }
}
