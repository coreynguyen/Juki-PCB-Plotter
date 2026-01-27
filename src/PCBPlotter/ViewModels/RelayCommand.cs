using System;
using System.ComponentModel;
using System.Windows.Input;

namespace PCBPlotter.ViewModels
{
    /// <summary>
    /// A command that relays its functionality to delegates
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            if (execute == null)
                throw new ArgumentNullException("execute");

            _execute = execute;
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(o => execute(), canExecute != null ? new Predicate<object>(o => canExecute()) : null)
        {
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// A generic command that relays its functionality to delegates
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            if (execute == null)
                throw new ArgumentNullException("execute");

            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null)
                return true;

            T typedParam;
            if (!TryConvertParameter(parameter, out typedParam))
                return false;

            return _canExecute(typedParam);
        }

        public void Execute(object parameter)
        {
            T typedParam;
            if (TryConvertParameter(parameter, out typedParam))
            {
                _execute(typedParam);
            }
        }

        private bool TryConvertParameter(object parameter, out T result)
        {
            result = default(T);

            // Null check for value types
            if (parameter == null)
            {
                return !typeof(T).IsValueType;
            }

            // Already the correct type
            if (parameter is T)
            {
                result = (T)parameter;
                return true;
            }

            // Try to convert using TypeConverter
            try
            {
                var converter = TypeDescriptor.GetConverter(typeof(T));
                if (converter != null && converter.CanConvertFrom(parameter.GetType()))
                {
                    result = (T)converter.ConvertFrom(parameter);
                    return true;
                }

                // Fallback: try Convert.ChangeType for primitive types
                if (typeof(T).IsPrimitive || typeof(T) == typeof(decimal))
                {
                    result = (T)Convert.ChangeType(parameter, typeof(T));
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
