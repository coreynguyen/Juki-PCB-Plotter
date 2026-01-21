using System;
using System.Collections.Generic;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Manages undo/redo operations using the Command pattern
    /// </summary>
    public class UndoRedoService
    {
        private readonly Stack<IUndoableCommand> _undoStack;
        private readonly Stack<IUndoableCommand> _redoStack;
        private readonly int _maxHistorySize;
        private bool _isExecuting;

        public event EventHandler StackChanged;

        public bool CanUndo { get { return _undoStack.Count > 0; } }
        public bool CanRedo { get { return _redoStack.Count > 0; } }
        public int UndoCount { get { return _undoStack.Count; } }
        public int RedoCount { get { return _redoStack.Count; } }

        public string UndoDescription
        {
            get { return CanUndo ? _undoStack.Peek().Description : null; }
        }

        public string RedoDescription
        {
            get { return CanRedo ? _redoStack.Peek().Description : null; }
        }

        public UndoRedoService(int maxHistorySize = 100)
        {
            _maxHistorySize = maxHistorySize;
            _undoStack = new Stack<IUndoableCommand>();
            _redoStack = new Stack<IUndoableCommand>();
        }

        /// <summary>
        /// Execute a command and add it to the undo stack
        /// </summary>
        public void Execute(IUndoableCommand command)
        {
            if (_isExecuting) return;

            try
            {
                _isExecuting = true;
                command.Execute();
                _undoStack.Push(command);
                _redoStack.Clear(); // Clear redo stack on new action

                // Trim history if needed
                while (_undoStack.Count > _maxHistorySize)
                {
                    // Remove oldest (bottom) item - need to rebuild stack
                    var temp = new Stack<IUndoableCommand>();
                    while (_undoStack.Count > 1)
                    {
                        temp.Push(_undoStack.Pop());
                    }
                    _undoStack.Pop(); // Remove oldest
                    while (temp.Count > 0)
                    {
                        _undoStack.Push(temp.Pop());
                    }
                }

                OnStackChanged();
            }
            finally
            {
                _isExecuting = false;
            }
        }

        /// <summary>
        /// Undo the last command
        /// </summary>
        public void Undo()
        {
            if (!CanUndo || _isExecuting) return;

            try
            {
                _isExecuting = true;
                var command = _undoStack.Pop();
                command.Undo();
                _redoStack.Push(command);
                OnStackChanged();
            }
            finally
            {
                _isExecuting = false;
            }
        }

        /// <summary>
        /// Redo the last undone command
        /// </summary>
        public void Redo()
        {
            if (!CanRedo || _isExecuting) return;

            try
            {
                _isExecuting = true;
                var command = _redoStack.Pop();
                command.Execute();
                _undoStack.Push(command);
                OnStackChanged();
            }
            finally
            {
                _isExecuting = false;
            }
        }

        /// <summary>
        /// Clear all history
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            OnStackChanged();
        }

        /// <summary>
        /// Begin a batch operation (groups multiple commands)
        /// </summary>
        public BatchCommand BeginBatch(string description)
        {
            return new BatchCommand(description);
        }

        /// <summary>
        /// Execute a batch command
        /// </summary>
        public void ExecuteBatch(BatchCommand batch)
        {
            if (batch.Commands.Count > 0)
            {
                Execute(batch);
            }
        }

        private void OnStackChanged()
        {
            var handler = StackChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Interface for undoable commands
    /// </summary>
    public interface IUndoableCommand
    {
        string Description { get; }
        void Execute();
        void Undo();
    }

    /// <summary>
    /// A batch of commands that can be undone/redone as a single unit
    /// </summary>
    public class BatchCommand : IUndoableCommand
    {
        public string Description { get; private set; }
        public List<IUndoableCommand> Commands { get; private set; }

        public BatchCommand(string description)
        {
            Description = description;
            Commands = new List<IUndoableCommand>();
        }

        public void Add(IUndoableCommand command)
        {
            Commands.Add(command);
        }

        public void Execute()
        {
            foreach (var cmd in Commands)
            {
                cmd.Execute();
            }
        }

        public void Undo()
        {
            // Undo in reverse order
            for (int i = Commands.Count - 1; i >= 0; i--)
            {
                Commands[i].Undo();
            }
        }
    }

    /// <summary>
    /// Generic command using delegates
    /// </summary>
    public class DelegateCommand : IUndoableCommand
    {
        private readonly Action _execute;
        private readonly Action _undo;

        public string Description { get; private set; }

        public DelegateCommand(string description, Action execute, Action undo)
        {
            Description = description;
            _execute = execute;
            _undo = undo;
        }

        public void Execute()
        {
            _execute();
        }

        public void Undo()
        {
            _undo();
        }
    }

    /// <summary>
    /// Command for property changes
    /// </summary>
    public class PropertyChangeCommand<T> : IUndoableCommand
    {
        private readonly object _target;
        private readonly string _propertyName;
        private readonly T _oldValue;
        private readonly T _newValue;
        private readonly System.Reflection.PropertyInfo _property;

        public string Description { get; private set; }

        public PropertyChangeCommand(object target, string propertyName, T oldValue, T newValue, string description = null)
        {
            _target = target;
            _propertyName = propertyName;
            _oldValue = oldValue;
            _newValue = newValue;
            _property = target.GetType().GetProperty(propertyName);
            Description = description ?? string.Format("Change {0}", propertyName);
        }

        public void Execute()
        {
            _property.SetValue(_target, _newValue, null);
        }

        public void Undo()
        {
            _property.SetValue(_target, _oldValue, null);
        }
    }
}
