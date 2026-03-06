using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace MidiFighter64
{
    /// <summary>
    /// Dispatches actions onto the Unity main thread. Useful when MIDI
    /// callbacks arrive on background threads.
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        public static UnityMainThreadDispatcher Instance { get; private set; }

        readonly ConcurrentQueue<Action> _queue = new();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void Enqueue(Action action)
        {
            if (action != null)
                _queue.Enqueue(action);
        }

        void Update()
        {
            while (_queue.TryDequeue(out var action))
                action.Invoke();
        }
    }
}
