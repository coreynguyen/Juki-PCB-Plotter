using System;
using System.Collections.Generic;
using System.Linq;

namespace PCBPlotter.Core.Events
{
    /// <summary>
    /// Simple event aggregator for decoupled communication between components
    /// </summary>
    public class EventAggregator : IEventAggregator
    {
        private readonly Dictionary<Type, List<WeakReference>> _subscribers = new Dictionary<Type, List<WeakReference>>();
        private readonly object _lock = new object();

        private static EventAggregator _instance;
        public static EventAggregator Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new EventAggregator();
                return _instance;
            }
        }

        /// <summary>
        /// Subscribe to an event type
        /// </summary>
        public void Subscribe<TEvent>(Action<TEvent> handler)
        {
            lock (_lock)
            {
                var eventType = typeof(TEvent);
                if (!_subscribers.ContainsKey(eventType))
                {
                    _subscribers[eventType] = new List<WeakReference>();
                }

                _subscribers[eventType].Add(new WeakReference(handler));
            }
        }

        /// <summary>
        /// Unsubscribe from an event type
        /// </summary>
        public void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            lock (_lock)
            {
                var eventType = typeof(TEvent);
                if (_subscribers.ContainsKey(eventType))
                {
                    _subscribers[eventType].RemoveAll(wr => !wr.IsAlive || wr.Target.Equals(handler));
                }
            }
        }

        /// <summary>
        /// Publish an event to all subscribers
        /// </summary>
        public void Publish<TEvent>(TEvent eventData)
        {
            List<Action<TEvent>> handlers = new List<Action<TEvent>>();

            lock (_lock)
            {
                var eventType = typeof(TEvent);
                if (_subscribers.ContainsKey(eventType))
                {
                    var deadRefs = new List<WeakReference>();
                    foreach (var wr in _subscribers[eventType])
                    {
                        if (wr.IsAlive)
                        {
                            var handler = wr.Target as Action<TEvent>;
                            if (handler != null)
                            {
                                handlers.Add(handler);
                            }
                        }
                        else
                        {
                            deadRefs.Add(wr);
                        }
                    }

                    // Clean up dead references
                    foreach (var dead in deadRefs)
                    {
                        _subscribers[eventType].Remove(dead);
                    }
                }
            }

            // Invoke handlers outside lock
            foreach (var handler in handlers)
            {
                try
                {
                    handler(eventData);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Event handler error: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Clear all subscriptions
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _subscribers.Clear();
            }
        }
    }

    public interface IEventAggregator
    {
        void Subscribe<TEvent>(Action<TEvent> handler);
        void Unsubscribe<TEvent>(Action<TEvent> handler);
        void Publish<TEvent>(TEvent eventData);
    }
}
