using UnityEngine;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Drop this on any GameObject in an empty scene. It ensures all core MIDI
    /// components exist. Add MidiDebugUI or visualizer components manually as needed.
    /// </summary>
    public class MidiSceneBootstrapper : MonoBehaviour
    {
        void Awake()
        {
            EnsureCoreComponents();
        }

        void EnsureCoreComponents()
        {
            if (Object.FindFirstObjectByType<MidiEventManager>() == null)
                new GameObject("MidiEventManager").AddComponent<MidiEventManager>();

            if (Object.FindFirstObjectByType<UnityMainThreadDispatcher>() == null)
                new GameObject("UnityMainThreadDispatcher").AddComponent<UnityMainThreadDispatcher>();

            if (Object.FindFirstObjectByType<MidiMixRouter>() == null)
                new GameObject("MidiMixRouter").AddComponent<MidiMixRouter>();

            if (Object.FindFirstObjectByType<MidiGridRouter>() == null)
                new GameObject("MidiGridRouter").AddComponent<MidiGridRouter>();
        }
    }
}
