using UnityEngine;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Drop this on any GameObject in an empty scene. It ensures all core MIDI
    /// components exist and creates the debug UI overlay.
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

            if (Object.FindFirstObjectByType<FloorCameraController>() == null)
                new GameObject("FloorCameraController").AddComponent<FloorCameraController>();

            if (Object.FindFirstObjectByType<MidiFighterInteriorSpawner>() == null)
                new GameObject("MidiFighterInteriorSpawner").AddComponent<MidiFighterInteriorSpawner>();

            if (Object.FindFirstObjectByType<CloseUpCameraController>() == null)
                new GameObject("CloseUpCameraController").AddComponent<CloseUpCameraController>();

            if (Object.FindFirstObjectByType<OrbitingCameraController>() == null)
                new GameObject("OrbitingCameraController").AddComponent<OrbitingCameraController>();

            // if (Object.FindFirstObjectByType<MidiDebugUI>() == null)
            //     new GameObject("MidiDebugUI").AddComponent<MidiDebugUI>();

            if (Object.FindFirstObjectByType<MidiNoteLogger>() == null)
                new GameObject("MidiNoteLogger").AddComponent<MidiNoteLogger>();

            if (Object.FindFirstObjectByType<MiniBokehFaderDriver>() == null)
                new GameObject("MiniBokehFaderDriver").AddComponent<MiniBokehFaderDriver>();
        }
    }
}
