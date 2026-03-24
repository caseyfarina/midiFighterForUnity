using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Cinemachine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Row 8, Col 1 — hold to cut to a close-up camera inside the current room.
    ///               Release to cut back to the floor camera.
    /// Row 8, Col 2 — press to reposition the close-up camera near a random
    ///               object in the current room, found via Physics.OverlapBox
    ///               on the floor's FloorVolume trigger collider.
    ///               DOF focus distance is updated to match the target.
    /// </summary>
    public class CloseUpCameraController : MonoBehaviour
    {
        const float DIST_MIN   = 1.0f;
        const float DIST_MAX   = 3.0f;
        const float RADIUS_CAP = 1.5f; // clamp object radius contribution so large objects don't push cam out of the room

        CinemachineCamera _closeUpVcam;
        FloorVolume[]     _floorVolumes;

        // Active-session state
        Volume       _activeDOFVolume;
        DepthOfField _activeDOF;
        FloorVolume  _activeFloorVolume;

        void Awake()
        {
            var mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogWarning("[CloseUpCameraController] No Main Camera found.");
                return;
            }

            var go = new GameObject("CloseUpVcam");
            go.transform.SetPositionAndRotation(
                mainCam.transform.position,
                mainCam.transform.rotation);

            _closeUpVcam          = go.AddComponent<CinemachineCamera>();
            _closeUpVcam.Priority = 20;   // beats FloorVcam (10) when active
            go.SetActive(false);

        }

        void Start()
        {
            // Include inactive so FloorVisibilityManager disabling floors doesn't break the cache
            _floorVolumes = Object.FindObjectsByType<FloorVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += HandleButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= HandleButton;

        void HandleButton(GridButton btn, bool isNoteOn)
        {
            if (btn.row != 8 || !isNoteOn || _closeUpVcam == null) return;

            if (btn.col == 1)
                SetCloseUpActive(true);
            else if (btn.col == 2)
                RandomizePosition();
            else if (btn.col == 3 || btn.col == 8)
                SetCloseUpActive(false);
        }

        // ── activation ───────────────────────────────────────────────────────

        void SetCloseUpActive(bool active)
        {
            if (active)
            {
                _activeFloorVolume = GetCurrentFloorVolume();

                // Activate camera and effects
                _closeUpVcam.gameObject.SetActive(true);

                // Immediately place on a random object in the current floor
                RandomizePosition();

                if (_activeFloorVolume != null && _activeFloorVolume.dofVolumeObject != null)
                {
                    var vol = _activeFloorVolume.dofVolumeObject.GetComponent<Volume>();
                    if (vol != null && vol.sharedProfile != null)
                    {
                        // Clone profile so we never mutate the saved asset
                        vol.profile      = Object.Instantiate(vol.sharedProfile);
                        _activeDOFVolume = vol;
                        _activeDOFVolume.enabled = true;
                        vol.profile.TryGet(out _activeDOF);
                    }
                }
            }
            else
            {
                _closeUpVcam.gameObject.SetActive(false);

                if (_activeDOFVolume != null)
                {
                    _activeDOFVolume.enabled = false;
                    // Restore shared profile reference so the clone can be GC'd
                    _activeDOFVolume.profile = _activeDOFVolume.sharedProfile;
                    _activeDOFVolume         = null;
                }

                _activeDOF         = null;
                _activeFloorVolume = null;
            }
        }

        // ── repositioning ────────────────────────────────────────────────────

        void RandomizePosition()
        {
            if (_closeUpVcam == null) return;

            var floorVol = _activeFloorVolume ?? GetCurrentFloorVolume();
            if (floorVol == null)
            {
                Debug.LogWarning("[CloseUpCameraController] No FloorVolume for current floor.");
                return;
            }

            Bounds roomBounds = floorVol.WorldBounds;

            // Primary: Physics.OverlapBox — fast broadphase query
            var hits       = Physics.OverlapBox(roomBounds.center, roomBounds.extents);
            var candidates = new System.Collections.Generic.List<Renderer>(16);

            foreach (var hit in hits)
            {
                if (hit.gameObject == _closeUpVcam.gameObject) continue;
                // Prefer the renderer on the hit GO; fall back to first child renderer
                var r = hit.GetComponent<Renderer>()
                     ?? hit.GetComponentInChildren<Renderer>();
                if (r != null && r.enabled && r.gameObject.activeInHierarchy)
                    candidates.Add(r);
            }

            // Fallback: Y-band scan (catches objects without colliders)
            if (candidates.Count == 0)
            {
                var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                foreach (var r in allRenderers)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (r.gameObject == _closeUpVcam.gameObject) continue;
                    float y = r.bounds.center.y;
                    if (y >= roomBounds.min.y && y <= roomBounds.max.y)
                        candidates.Add(r);
                }
            }

            if (candidates.Count == 0)
            {
                Debug.LogWarning("[CloseUpCameraController] No objects found in floor volume.");
                return;
            }

            PlaceCameraOnTarget(candidates[Random.Range(0, candidates.Count)]);
        }

        void PlaceCameraOnTarget(Renderer target)
        {
            Bounds  bounds = target.bounds;
            Vector3 center = bounds.center;
            float   radius = Mathf.Min(Mathf.Max(bounds.extents.magnitude, 0.3f), RADIUS_CAP);

            float angle  = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist   = radius + Random.Range(DIST_MIN, DIST_MAX);
            float height = Random.Range(-radius * 0.4f, radius * 0.4f);

            Vector3    pos = center + new Vector3(
                Mathf.Cos(angle) * dist,
                height,
                Mathf.Sin(angle) * dist);
            Quaternion rot = Quaternion.LookRotation(center - pos);

            // ForceCameraPosition snaps instantly — avoids Cinemachine interpolating
            // the active vcam's own transform movement across frames
            _closeUpVcam.ForceCameraPosition(pos, rot);

            // Update DOF focus distance to match actual camera-to-target distance
            if (_activeDOF != null)
                _activeDOF.focusDistance.Override(Vector3.Distance(pos, center));
        }

        // ── helpers ──────────────────────────────────────────────────────────

        FloorVolume GetCurrentFloorVolume()
        {
            int current = FloorCameraController.CurrentFloor;
            if (_floorVolumes != null)
                foreach (var fv in _floorVolumes)
                    if (fv.floorIndex == current) return fv;
            return null;
        }
    }
}
