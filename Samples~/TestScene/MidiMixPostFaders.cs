using UnityEngine;
using UnityEngine.Rendering;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Maps MIDI Mix channel faders to post-processing Volume weights.
    /// One Volume per effect — fader value (0–1) drives Volume.weight directly.
    ///
    /// Default fader mapping:
    ///   Ch1 → Bloom
    ///   Ch2 → Lens Distortion
    ///   Ch3 → Motion Blur
    ///   Ch4 → Depth of Field
    ///   Ch5–8 → optional extras
    ///
    /// Always-on effects (AO, Color Grading) live in a separate base Volume
    /// at weight=1 that this script never touches.
    /// </summary>
    public class MidiMixPostFaders : MonoBehaviour
    {
        [System.Serializable]
        public class FaderVolumeEntry
        {
            [Tooltip("MIDI Mix channel (1–8).")]
            [Range(1, 8)] public int channel = 1;

            [Tooltip("The Volume whose weight this fader controls.")]
            public Volume volume;

            [Tooltip("Label for the debug display.")]
            public string label = "Effect";
        }

        [Header("Fader → Volume Bindings")]
        [SerializeField] private FaderVolumeEntry[] _bindings = new FaderVolumeEntry[]
        {
            new FaderVolumeEntry { channel = 1, label = "Bloom" },
            new FaderVolumeEntry { channel = 2, label = "Lens Distortion" },
            new FaderVolumeEntry { channel = 3, label = "Motion Blur" },
            new FaderVolumeEntry { channel = 4, label = "Depth of Field" },
        };

        [Header("Always-On Base Volume")]
        [Tooltip("Volume containing AO + Color Grading. Never touched by faders — stays at weight 1.")]
        [SerializeField] private Volume _baseVolume;

        private void OnEnable()
        {
            MidiMixRouter.OnChannelFader += HandleFader;
        }

        private void OnDisable()
        {
            MidiMixRouter.OnChannelFader -= HandleFader;
        }

        private void Start()
        {
            // Ensure base volume is always on
            if (_baseVolume != null)
            {
                _baseVolume.weight = 1f;
                _baseVolume.enabled = true;
            }

            // Start all effect volumes at zero weight
            foreach (var binding in _bindings)
            {
                if (binding.volume != null)
                {
                    binding.volume.weight = 0f;
                    binding.volume.enabled = true;
                }
            }
        }

        private void HandleFader(int channel, float value)
        {
            foreach (var binding in _bindings)
            {
                if (binding.channel == channel && binding.volume != null)
                {
                    binding.volume.weight = value;
                    Debug.Log($"[PostFaders] Ch{channel} ({binding.label}) weight={value:F2}");
                    return;
                }
            }
        }
    }
}
