using UnityEngine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Maps MIDI Mix faders to MiniBokeh parameters.
    ///   Ch5 → MaxBlurRadius (0.1–5)
    ///   Ch6 → MiniBokehFocusPlane.yOffset (-4 to +4 units from floor centre)
    /// Assign bokehController and focusPlane in the inspector, or leave null to auto-find.
    /// </summary>
    public class MiniBokehFaderDriver : MonoBehaviour
    {
        [Header("Max Blur Radius")]
        [Range(1, 8)] public int blurChannel = 5;
        public float minRadius = 0.1f;
        public float maxRadius = 5f;

        [Header("Focus Distance")]
        [Range(1, 8)] public int focusChannel = 6;
        public float minFocus = 0.5f;
        public float maxFocus = 50f;

        [Header("References")]
        [Tooltip("Auto-found if null.")]
        public MiniBokeh.MiniBokehController bokehController;
        [Tooltip("Auto-found if null.")]
        public MiniBokehFocusPlane focusPlane;

        private void Start()
        {
            if (bokehController == null)
                bokehController = Object.FindFirstObjectByType<MiniBokeh.MiniBokehController>();
            if (focusPlane == null)
                focusPlane = Object.FindFirstObjectByType<MiniBokehFocusPlane>();

            if (bokehController == null)
                Debug.LogWarning("[MiniBokehFaderDriver] No MiniBokehController found.");
            if (focusPlane == null)
                Debug.LogWarning("[MiniBokehFaderDriver] No MiniBokehFocusPlane found.");
        }

        private void OnEnable()  => MidiMixRouter.OnChannelFader += HandleFader;
        private void OnDisable() => MidiMixRouter.OnChannelFader -= HandleFader;

        private void HandleFader(int ch, float value)
        {
            if (ch == blurChannel && bokehController != null)
                bokehController.MaxBlurRadius = Mathf.Lerp(minRadius, maxRadius, value);
            else if (ch == focusChannel && focusPlane != null)
                focusPlane.SetFocusDistance(Mathf.Lerp(minFocus, maxFocus, value));
        }
    }
}
