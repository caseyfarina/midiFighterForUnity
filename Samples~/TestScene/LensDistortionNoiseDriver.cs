using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Drives all LensDistortion parameters with independent Perlin noise oscillators.
    /// Each parameter gets its own frequency and amplitude so they drift out of phase,
    /// producing a continuous vibratory / wobble effect.
    ///
    /// Attach to any GameObject. Assign the Volume whose profile contains a
    /// LensDistortion override. Enable/disable this component to start/stop the effect.
    ///
    /// The component modifies parameter overrides directly on the profile at runtime —
    /// no cloning needed, but changes will appear in the Inspector while in Play mode.
    /// </summary>
    public class LensDistortionNoiseDriver : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Volume containing a LensDistortion override.")]
        [SerializeField] private Volume _volume;

        [Header("Master")]
        [Tooltip("Global time multiplier — higher = faster vibration across all params.")]
        [SerializeField, Range(0f, 20f)] private float _masterSpeed = 4f;

        [Tooltip("Global amplitude multiplier applied on top of each param's own amplitude.")]
        [SerializeField, Range(0f, 1f)] private float _masterAmplitude = 1f;

        [Header("Intensity  (range −1 → 1)")]
        [SerializeField, Range(0f, 10f)] private float _intensitySpeed = 1.3f;
        [SerializeField, Range(0f, 1f)]  private float _intensityAmplitude = 0.6f;
        [SerializeField] private float _intensityCenter = 0f;   // bias / DC offset

        [Header("X Multiplier  (range 0 → 1)")]
        [SerializeField, Range(0f, 10f)] private float _xMultSpeed = 1.7f;
        [SerializeField, Range(0f, 1f)]  private float _xMultAmplitude = 0.4f;
        [SerializeField, Range(0f, 1f)]  private float _xMultCenter = 0.5f;

        [Header("Y Multiplier  (range 0 → 1)")]
        [SerializeField, Range(0f, 10f)] private float _yMultSpeed = 2.1f;
        [SerializeField, Range(0f, 1f)]  private float _yMultAmplitude = 0.4f;
        [SerializeField, Range(0f, 1f)]  private float _yMultCenter = 0.5f;

        [Header("Center X  (range 0 → 1)")]
        [SerializeField, Range(0f, 10f)] private float _centerXSpeed = 0.9f;
        [SerializeField, Range(0f, 0.5f)] private float _centerXAmplitude = 0.1f;
        [SerializeField, Range(0f, 1f)]  private float _centerXCenter = 0.5f;

        [Header("Center Y  (range 0 → 1)")]
        [SerializeField, Range(0f, 10f)] private float _centerYSpeed = 1.1f;
        [SerializeField, Range(0f, 0.5f)] private float _centerYAmplitude = 0.1f;
        [SerializeField, Range(0f, 1f)]  private float _centerYCenter = 0.5f;

        [Header("Scale  (range 0.01 → 5)")]
        [SerializeField, Range(0f, 10f)] private float _scaleSpeed = 1.5f;
        [SerializeField, Range(0f, 2f)]  private float _scaleAmplitude = 0.15f;
        [SerializeField, Range(0.01f, 5f)] private float _scaleCenter = 1f;

        // Unique Perlin seeds per parameter so they don't move in unison
        private static readonly float[] _seeds = { 0f, 31.7f, 67.3f, 113.9f, 157.2f, 199.5f };

        private LensDistortion _ld;

        private void OnEnable()
        {
            if (_volume == null)
            {
                Debug.LogWarning("[LensDistortionNoiseDriver] No Volume assigned.", this);
                enabled = false;
                return;
            }

            if (!_volume.profile.TryGet(out _ld))
            {
                Debug.LogWarning("[LensDistortionNoiseDriver] Volume profile has no LensDistortion override. " +
                                 "Add one and enable it.", this);
                enabled = false;
                return;
            }

            // Make sure all params are overridden so our writes take effect
            _ld.intensity.overrideState    = true;
            _ld.xMultiplier.overrideState  = true;
            _ld.yMultiplier.overrideState  = true;
            _ld.center.overrideState       = true;
            _ld.scale.overrideState        = true;
        }

        private void Update()
        {
            if (_ld == null) return;

            float t = Time.time * _masterSpeed;
            float amp = _masterAmplitude;

            _ld.intensity.value   = Noise(t, _intensitySpeed, _seeds[0], _intensityAmplitude  * amp, _intensityCenter, -1f, 1f);
            _ld.xMultiplier.value = Noise(t, _xMultSpeed,     _seeds[1], _xMultAmplitude      * amp, _xMultCenter,     0f,  1f);
            _ld.yMultiplier.value = Noise(t, _yMultSpeed,     _seeds[2], _yMultAmplitude      * amp, _yMultCenter,     0f,  1f);
            _ld.scale.value       = Noise(t, _scaleSpeed,     _seeds[5], _scaleAmplitude      * amp, _scaleCenter,     0.01f, 5f);

            float cx = Noise(t, _centerXSpeed, _seeds[3], _centerXAmplitude * amp, _centerXCenter, 0f, 1f);
            float cy = Noise(t, _centerYSpeed, _seeds[4], _centerYAmplitude * amp, _centerYCenter, 0f, 1f);
            _ld.center.value = new Vector2(cx, cy);
        }

        private void OnDisable()
        {
            // Reset to neutral on disable so the effect doesn't stick
            if (_ld == null) return;
            _ld.intensity.value   = 0f;
            _ld.xMultiplier.value = 1f;
            _ld.yMultiplier.value = 1f;
            _ld.center.value      = new Vector2(0.5f, 0.5f);
            _ld.scale.value       = 1f;
        }

        /// <summary>
        /// Returns a Perlin-based value oscillating around <paramref name="center"/>
        /// with the given amplitude, clamped to [min, max].
        /// Perlin sample is mapped from [0,1] to [-1,1] before amplitude scaling.
        /// </summary>
        private static float Noise(float t, float speed, float seed, float amplitude, float center, float min, float max)
        {
            float raw = Mathf.PerlinNoise(t * speed + seed, seed * 0.37f); // [0, 1]
            float bipolar = raw * 2f - 1f;                                  // [-1, 1]
            return Mathf.Clamp(center + bipolar * amplitude, min, max);
        }
    }
}
