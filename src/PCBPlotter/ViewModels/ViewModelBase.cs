using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PCBPlotter.Core.Events;

namespace PCBPlotter.ViewModels
{
    /// <summary>
    /// Base class for all ViewModels with property change notification
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        private bool _isDisposed;

        public event PropertyChangedEventHandler PropertyChanged;

        protected IEventAggregator EventAggregator
        {
            get { return Core.Events.EventAggregator.Instance; }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void RaisePropertyChanged(string propertyName)
        {
            OnPropertyChanged(propertyName);
        }

        /// <summary>
        /// Subscribe to an event
        /// </summary>
        protected void Subscribe<TEvent>(Action<TEvent> handler)
        {
            EventAggregator.Subscribe(handler);
        }

        /// <summary>
        /// Publish an event
        /// </summary>
        protected void Publish<TEvent>(TEvent eventData)
        {
            EventAggregator.Publish(eventData);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // Cleanup managed resources
                }
                _isDisposed = true;
            }
        }
    }
}
