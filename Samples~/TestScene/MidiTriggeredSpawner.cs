using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    public enum SpawnMode    { Volume, Surface }
    public enum RotationMode { RandomY, FullyRandom }

    /// <summary>
    /// Spawns random prefabs from a configurable pool inside or on the surface of a collider volume.
    /// Two MF64 buttons are used: one to spawn one instance per press, one to clear all active instances.
    /// Configure prefabs via the folder loader in the inspector; test spawn/clear in Play mode.
    /// </summary>
    public class MidiTriggeredSpawner : MonoBehaviour
    {
        [Header("MIDI Buttons (Row 1-8, Col 1-8)")]
        public int spawnButtonRow = 1;
        public int spawnButtonCol = 1;
        public int clearButtonRow = 1;
        public int clearButtonCol = 2;

        [Header("Prefabs")]
        public GameObject[] prefabs;
        [HideInInspector] public string prefabFolderPath = "Assets/";

        [Header("Spawn Volume")]
        public Collider spawnVolume;
        public SpawnMode spawnMode = SpawnMode.Volume;
        public bool alignToSurfaceNormal = true;

        [Header("Transform Randomization")]
        public float scaleMin = 1f;
        public float scaleMax = 3f;
        public RotationMode rotationMode = RotationMode.RandomY;

        [Header("Pop-in Animation")]
        public float popDurationMin = 0.1f;
        public float popDurationMax = 0.2f;

        [Header("Clear Animation")]
        public float clearScaleDuration  = 0.1f;
        public float clearStaggerDelay   = 0.1f;

        [Header("Pool")]
        public int poolSize = 32;
        public bool expandIfExhausted = true;

        readonly Queue<GameObject> _pool   = new();
        readonly List<GameObject>  _active = new();
        GameObject _poolRoot;

        void Awake()
        {
            _poolRoot = new GameObject($"[{name}] Pool");
            WarmPool();
        }

        void OnDestroy()
        {
            if (_poolRoot != null) Destroy(_poolRoot);
        }

        void WarmPool()
        {
            if (prefabs == null || prefabs.Length == 0) return;
            for (int i = 0; i < poolSize; i++)
                _pool.Enqueue(MakeInstance(i));
        }

        GameObject MakeInstance(int index)
        {
            var prefab = prefabs[index % prefabs.Length];
            var go = Instantiate(prefab, _poolRoot.transform);
            go.SetActive(false);
            return go;
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += OnButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= OnButton;

        void OnButton(GridButton btn, bool isNoteOn)
        {
            if (!isNoteOn) return;
            if (btn.row == spawnButtonRow && btn.col == spawnButtonCol)
                SpawnOne();
            else if (btn.row == clearButtonRow && btn.col == clearButtonCol)
                ClearAll();
        }

        public void SpawnOne()
        {
            if (spawnVolume == null || prefabs == null || prefabs.Length == 0)
            {
                Debug.LogWarning($"[MidiTriggeredSpawner:{name}] Missing spawnVolume or prefabs.");
                return;
            }

            GameObject go;
            if (_pool.Count > 0)
            {
                go = _pool.Dequeue();
                go.transform.DOKill();
            }
            else if (expandIfExhausted)
            {
                go = MakeInstance(_active.Count);
            }
            else
            {
                Debug.LogWarning($"[MidiTriggeredSpawner:{name}] Pool exhausted ({poolSize} active).");
                return;
            }

            PlaceInstance(go);
            Vector3 targetScale = go.transform.localScale;
            go.transform.localScale = Vector3.zero;
            go.SetActive(true);
            go.transform.DOScale(targetScale, Random.Range(popDurationMin, popDurationMax))
                        .SetEase(Ease.OutQuad);
            _active.Add(go);
        }

        public void ClearAll()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                var go = _active[i];
                go.transform.DOKill();
                go.transform.DOScale(Vector3.zero, clearScaleDuration)
                            .SetDelay(i * clearStaggerDelay)
                            .SetEase(Ease.InQuad)
                            .OnComplete(() =>
                            {
                                go.SetActive(false);
                                go.transform.SetParent(_poolRoot.transform);
                                _pool.Enqueue(go);
                            });
            }
            _active.Clear();
        }

        void PlaceInstance(GameObject go)
        {
            Vector3    pos;
            Quaternion rot;

            if (spawnMode == SpawnMode.Surface)
            {
                pos = SampleSurface(out Vector3 normal);
                rot = BuildRotation(normal, useSurfaceNormal: alignToSurfaceNormal);
            }
            else
            {
                pos = SampleVolume();
                rot = BuildRotation(Vector3.up, useSurfaceNormal: false);
            }

            go.transform.SetParent(null);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * Random.Range(scaleMin, scaleMax);
        }

        Quaternion BuildRotation(Vector3 upRef, bool useSurfaceNormal)
        {
            if (rotationMode == RotationMode.FullyRandom)
                return Random.rotation;

            // RandomY: spin around world-up (or surface normal when aligned)
            var spin = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            if (useSurfaceNormal)
                return Quaternion.FromToRotation(Vector3.up, upRef) * spin;
            return spin;
        }

        // Shoots a ray from the collider's center outward in a random direction to land on the surface.
        // Requires the spawnVolume's layer to be collidable with raycasts.
        Vector3 SampleSurface(out Vector3 normal)
        {
            var   bounds  = spawnVolume.bounds;
            float maxDist = bounds.extents.magnitude * 2.5f;
            int   layer   = 1 << spawnVolume.gameObject.layer;

            for (int i = 0; i < 50; i++)
            {
                if (Physics.Raycast(bounds.center, Random.onUnitSphere, out RaycastHit hit,
                                    maxDist, layer, QueryTriggerInteraction.Collide))
                {
                    normal = hit.normal;
                    return hit.point;
                }
            }

            Debug.LogWarning($"[MidiTriggeredSpawner:{name}] SampleSurface: no raycast hit after 50 tries. " +
                             "Ensure spawnVolume's layer is not ignored by Physics.");
            normal = Vector3.up;
            return bounds.center;
        }

        // Samples a random point inside the collider bounds and rejects points outside the collider.
        // Works accurately for convex colliders (Box, Sphere, Capsule, convex Mesh).
        // Non-convex MeshColliders will fall back to the bounds center after 50 tries.
        Vector3 SampleVolume()
        {
            var b = spawnVolume.bounds;
            for (int i = 0; i < 50; i++)
            {
                var p = new Vector3(
                    Random.Range(b.min.x, b.max.x),
                    Random.Range(b.min.y, b.max.y),
                    Random.Range(b.min.z, b.max.z));
                if ((spawnVolume.ClosestPoint(p) - p).sqrMagnitude < 1e-6f)
                    return p;
            }
            return b.center;
        }
    }
}
