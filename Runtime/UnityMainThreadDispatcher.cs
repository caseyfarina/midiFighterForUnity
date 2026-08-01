using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace MidiFighter64
{
    /// <summary>
    /// Dispatches actions onto the Unity main thread. Not used internally by
    /// this package — Minis already delivers MIDI callbacks on the main thread
    /// via the Input System, and the output components' background poll
    /// threads talk to Unity only through thread-safe calls (Debug.Log,
    /// lock-guarded fields), so nothing needs marshaling today. Kept as a
    /// general-purpose utility for consumer code that adds its own
    /// background-thread work and needs to hop back to the main thread.
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
