using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.Cinemachine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Row 8, Col 3 — toggle a slowly orbiting camera with height wobble.
    /// Press once to activate, press again to deactivate.
    ///
    /// MIDI Mix Row 1, Ch 5 (CC 46) — FOV / focal length (0 = 70° wide, 1 = 5° telephoto)
    /// MIDI Mix Row 1, Ch 6 (CC 50) — orbit radius (0 = 4, 1 = 30 units)
    /// MIDI Mix Row 1, Ch 7 (CC 54) — orbit speed (0 = stopped, 1 = ~90°/s)
    /// MIDI Mix Row 1, Ch 8 (CC 58) — wobble amount (0 = none, 1 = ±4 units)
    ///
    /// Orbits around the current floor's FloorVolume bounds center.
    /// Priority 30 overrides CloseUp (20) and Floor (10) vcams while active.
    /// </summary>
    public class OrbitingCameraController : MonoBehaviour
    {
        const float ORBIT_HEIGHT  = 4f;   // height above floor center
        const float RADIUS_MIN    = 4f;   // orbit radius at knob = 0
        const float RADIUS_MAX    = 30f;  // orbit radius at knob = 1
        const float SPEED_MAX     = 90f;  // degrees/sec at knob = 1
        const float WOBBLE_MAX    = 4f;   // peak height offset at knob = 1
        const float WOBBLE_FREQ   = 0.4f; // cycles per second
        const float FOV_WIDE      = 70f;  // FOV at knob = 0 (wide angle)
        const float FOV_TELE      = 5f;   // FOV at knob = 1 (telephoto)

        // MIDI Mix Row 1 (0-based row index 0), Ch 5/6/7/8 (0-based channel indices 4/5/6/7)
        static readonly int CC_FOV    = MidiMixInputMap.KnobCC[0, 4]; // 46
        static readonly int CC_RADIUS = MidiMixInputMap.KnobCC[0, 5]; // 50
        static readonly int CC_SPEED  = MidiMixInputMap.KnobCC[0, 6]; // 54
        static readonly int CC_WOBBLE = MidiMixInputMap.KnobCC[0, 7]; // 58

        CinemachineCamera _vcam;
        FloorVolume[]     _floorVolumes;

        public static event System.Action OnOrbitActivated;

        bool  _active;
        float _orbitAngle;
        float _fovNorm    = 0.25f; // ~53° default
        float _radiusNorm = 0.35f; // ~12 units default
        float _speedNorm  = 0f;
        float _wobbleNorm = 0f;

        // ── lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            var mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogWarning("[OrbitingCameraController] No Main Camera found.");
                return;
            }

            var go = new GameObject("OrbitVcam");
            go.transform.SetPositionAndRotation(
                mainCam.transform.position,
                mainCam.transform.rotation);

            _vcam          = go.AddComponent<CinemachineCamera>();
            _vcam.Priority = 30; // beats CloseUp (20) and Floor (10)

            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;

            go.SetActive(false);
        }

        void Start()
        {
            _floorVolumes = Object.FindObjectsByType<FloorVolume>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        void OnEnable()
        {
            MidiGridRouter.OnGridButton += HandleButton;
            MidiMixRouter.OnKnob       += HandleKnob;
        }

        void OnDisable()
        {
            MidiGridRouter.OnGridButton -= HandleButton;
            MidiMixRouter.OnKnob       -= HandleKnob;
        }

        // ── MIDI handlers ────────────────────────────────────────────────────

        void HandleButton(GridButton btn, bool isNoteOn)
        {
            if (btn.row != 8 || !isNoteOn || _vcam == null) return;

            if (btn.col == 3)
                SetOrbitActive(true);
            else if (btn.col == 1 || btn.col == 8)
                SetOrbitActive(false);
        }

        void SetOrbitActive(bool active)
        {
            _active = active;
            _vcam.gameObject.SetActive(active);
            if (active) OnOrbitActivated?.Invoke();
        }

        void HandleKnob(int channel, int row, float value)
        {
            if (row != 1) return;
            if (channel == 5) _fovNorm    = value;
            if (channel == 6) _radiusNorm = value;
            if (channel == 7) _speedNorm  = value;
            if (channel == 8) _wobbleNorm = value;
        }

        // ── per-frame orbit ──────────────────────────────────────────────────

        void Update()
        {
            if (!_active || _vcam == null) return;

            float speed = _speedNorm * SPEED_MAX;
            _orbitAngle = (_orbitAngle + speed * Time.deltaTime) % 360f;

            Vector3 center = GetFloorCenter();
            float   radius = Mathf.Lerp(RADIUS_MIN, RADIUS_MAX, _radiusNorm);

            float wobble = Mathf.Sin(Time.time * WOBBLE_FREQ * Mathf.PI * 2f)
                           * (_wobbleNorm * WOBBLE_MAX);

            float rad = _orbitAngle * Mathf.Deg2Rad;
            var pos = center + new Vector3(
                Mathf.Cos(rad) * radius,
                ORBIT_HEIGHT + wobble,
                Mathf.Sin(rad) * radius);

            // Wobble subtly displaces the look-at so the horizon line breathes
            Vector3 lookTarget = center + Vector3.up * (wobble * 0.25f);
            var     rot        = Quaternion.LookRotation(lookTarget - pos);

            var lens = _vcam.Lens;
            lens.FieldOfView = Mathf.Lerp(FOV_WIDE, FOV_TELE, _fovNorm);
            _vcam.Lens = lens;

            _vcam.ForceCameraPosition(pos, rot);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        Vector3 GetFloorCenter()
        {
            int current = FloorCameraController.CurrentFloor;
            if (_floorVolumes != null)
                foreach (var fv in _floorVolumes)
                    if (fv.floorIndex == current)
                        return fv.WorldBounds.center;

            // Fallback: derive from floor index constants (floor height 8, offset 4 to mid-floor)
            return new Vector3(0f, current * 8f + 4f, 0f);
        }
    }
}
