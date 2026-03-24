using UnityEngine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Spawns 64 interior prefab instances (one per Midi Fighter 64 button)
    /// scattered randomly in 3D space at large random scales.
    /// Pressing a button toggles that instance on/off.
    /// Prefabs from the Basic Asset Pack Interior cycle if there are fewer than 64.
    /// </summary>
    public class MidiFighterInteriorSpawner : MonoBehaviour
    {
        const int   BUTTON_COUNT = 64;
        const float SPREAD       = 25f;

        readonly GameObject[] _instances = new GameObject[BUTTON_COUNT];

        void Start()
        {
            var refs = Resources.Load<MidiFighterInteriorRefs>(MidiFighterInteriorRefs.ResourceName);
            if (refs == null || refs.prefabs == null || refs.prefabs.Length == 0)
            {
                Debug.LogWarning("[MidiFighterInteriorSpawner] MidiFighterInteriorRefs not found in Resources.");
                return;
            }

            var root = new GameObject("Interior Objects");
            int count = refs.prefabs.Length;

            for (int i = 0; i < BUTTON_COUNT; i++)
            {
                var prefab = refs.prefabs[i % count];
                if (prefab == null) continue;

                var go = Instantiate(prefab, parent: root.transform);
                go.name = $"{prefab.name}_{i}";
                float x = Random.Range(-SPREAD, SPREAD);
                float z = Random.Range(-SPREAD, SPREAD);
                go.transform.position = new Vector3(x, 0f, z);
                int step = Random.Range(0, 4);
                go.transform.rotation = Quaternion.Euler(0f, step * 90f, 0f);
                go.SetActive(false);

                _instances[i] = go;
            }
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += HandleButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= HandleButton;

        void HandleButton(GridButton btn, bool isNoteOn)
        {
            // Col 8 = floor navigation — hide all active instances on floor change
            if (btn.col == 8)
            {
                if (isNoteOn) HideAll();
                return;
            }

            // Row 8 col 1-2 = close-up camera controls
            if (btn.row == 8 && btn.col <= 2) return;

            int idx = btn.linearIndex;
            if (idx < 0 || idx >= BUTTON_COUNT) return;
            var go = _instances[idx];
            if (go != null) go.SetActive(isNoteOn);
        }

        void HideAll()
        {
            foreach (var go in _instances)
                if (go != null) go.SetActive(false);
        }
    }
}
