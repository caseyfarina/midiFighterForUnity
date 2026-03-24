using UnityEngine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Rotates this GameObject continuously. Rotation speed is driven by
    /// MIDI Mix Row 1 Ch 1 knob (0–1 mapped to minSpeed–maxSpeed deg/s).
    /// </summary>
    public class MidiRotator : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float minSpeed = 0f;
        public float maxSpeed = 360f;

        private float _speed = 0f;

        private void OnEnable()
        {
            MidiMixRouter.OnKnob += HandleKnob;
            OrbitingCameraController.OnOrbitActivated += ResetSpeed;
        }

        private void OnDisable()
        {
            MidiMixRouter.OnKnob -= HandleKnob;
            OrbitingCameraController.OnOrbitActivated -= ResetSpeed;
        }

        private void ResetSpeed() => _speed = 0f;

        private void HandleKnob(int channel, int row, float value)
        {
            if (channel == 1 && row == 1)
                _speed = Mathf.Lerp(minSpeed, maxSpeed, value);
        }

        private void Update()
        {
            transform.Rotate(axis, _speed * Time.deltaTime, Space.Self);
        }
    }
}
