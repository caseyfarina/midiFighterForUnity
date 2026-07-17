using System;
using System.Collections.Generic;
using UnityEngine;

namespace MidiFighter64
{
    /// <summary>
    /// Subscribes to <see cref="MidiEventManager"/> and routes MF64 pads as Button or Toggle
    /// events according to an optional <see cref="MidiFighter64ButtonConfig"/>.
    ///
    /// Button mode: OnButtonPress on note-on, OnButtonHold every frame (or at holdRepeatInterval),
    ///              OnButtonRelease on note-off. Simultaneous presses are tracked independently.
    ///
    /// Toggle mode: OnToggle(btn, true/false) on note-on; note-off is ignored.
    ///
    /// When driveToggleLEDs is true, toggle state changes are mirrored to
    /// <see cref="MidiFighterOutput.Instance"/> via SetLED/ClearLED automatically.
    /// </summary>
    public class MidiFighterButtonRouter : MonoBehaviour
    {
        [SerializeField] MidiFighter64ButtonConfig _config;

        [Tooltip("Seconds between OnButtonHold fires while a pad is held. " +
                 "0 = fire every frame (elapsed keeps accumulating).")]
        [SerializeField] float _holdRepeatInterval = 0f;

        [Header("LED Feedback")]
        [Tooltip("If true, toggled-on pads light up via MidiFighterOutput; toggled-off pads clear.")]
        [SerializeField] bool _driveToggleLEDs = false;

        [Tooltip("Velocity sent to the LED when a pad is toggled on.")]
        [SerializeField, Range(1, 127)] int _toggleOnVelocity = 127;

        // ------------------------------------------------------------------

        /// <summary>Fired when a Button-mode pad is first pressed (note-on).</summary>
        public static event Action<GridButton, float> OnButtonPress;

        /// <summary>
        /// Fired each frame (or at holdRepeatInterval) while a Button-mode pad is held.
        /// Second argument is total elapsed seconds since the press.
        /// </summary>
        public static event Action<GridButton, float> OnButtonHold;

        /// <summary>Fired when a Button-mode pad is released (note-off).</summary>
        public static event Action<GridButton> OnButtonRelease;

        /// <summary>Fired when a Toggle-mode pad changes state (note-on only).</summary>
        public static event Action<GridButton, bool> OnToggle;

        // ------------------------------------------------------------------

        // note → elapsed hold time (Button mode)
        readonly Dictionary<int, float> _heldPads    = new();
        // note → toggled state (Toggle mode)
        readonly Dictionary<int, bool>  _toggleStates = new();

        // ------------------------------------------------------------------

        void OnEnable()
        {
            MidiEventManager.OnNoteOn  += HandleNoteOn;
            MidiEventManager.OnNoteOff += HandleNoteOff;
            PushToggleLEDs();
        }

        void OnDisable()
        {
            MidiEventManager.OnNoteOn  -= HandleNoteOn;
            MidiEventManager.OnNoteOff -= HandleNoteOff;
            _heldPads.Clear();
        }

        void Update()
        {
            if (_heldPads.Count == 0) return;

            // Snapshot keys to avoid mutation-during-enumeration
            var snapshot = new List<int>(_heldPads.Keys);
            foreach (int note in snapshot)
            {
                if (!_heldPads.TryGetValue(note, out float elapsed)) continue;

                elapsed += Time.deltaTime;
                _heldPads[note] = elapsed;

                bool shouldFire = _holdRepeatInterval <= 0f || elapsed >= _holdRepeatInterval;
                if (shouldFire)
                {
                    OnButtonHold?.Invoke(MidiFighter64InputMap.FromNote(note), elapsed);
                    if (_holdRepeatInterval > 0f)
                        _heldPads[note] = 0f;
                }
            }
        }

        // ------------------------------------------------------------------

        void HandleNoteOn(int note, float velocity)
        {
            if (!MidiFighter64InputMap.IsInRange(note)) return;

            var btn  = MidiFighter64InputMap.FromNote(note);
            var mode = _config != null ? _config.GetMode(btn) : MidiFighterButtonMode.Button;

            if (mode == MidiFighterButtonMode.Toggle)
            {
                bool next = !(_toggleStates.TryGetValue(note, out bool s) && s);
                _toggleStates[note] = next;
                OnToggle?.Invoke(btn, next);
                if (_driveToggleLEDs) DriveToggleLED(note, next);
            }
            else
            {
                _heldPads[note] = 0f;
                OnButtonPress?.Invoke(btn, velocity);
            }
        }

        void HandleNoteOff(int note)
        {
            if (!MidiFighter64InputMap.IsInRange(note)) return;

            var btn  = MidiFighter64InputMap.FromNote(note);
            var mode = _config != null ? _config.GetMode(btn) : MidiFighterButtonMode.Button;

            if (mode == MidiFighterButtonMode.Button)
            {
                _heldPads.Remove(note);
                OnButtonRelease?.Invoke(btn);
            }
            // Toggle mode ignores note-off
        }

        // ------------------------------------------------------------------
        // Public query API
        // ------------------------------------------------------------------

        /// <returns>True if this note is currently in toggle-on state.</returns>
        public bool IsToggled(int noteNumber)
            => _toggleStates.TryGetValue(noteNumber, out bool s) && s;

        /// <returns>True if this note is currently held (Button mode).</returns>
        public bool IsHeld(int noteNumber)
            => _heldPads.ContainsKey(noteNumber);

        /// <summary>
        /// Programmatically sets a Toggle-mode note's state.
        /// </summary>
        /// <param name="noteNumber">Hardware note number (36–99).</param>
        /// <param name="value">New toggle state.</param>
        /// <param name="fireEvent">Whether to invoke <see cref="OnToggle"/>.</param>
        public void SetToggle(int noteNumber, bool value, bool fireEvent)
        {
            if (!MidiFighter64InputMap.IsInRange(noteNumber)) return;
            _toggleStates[noteNumber] = value;
            if (fireEvent)
                OnToggle?.Invoke(MidiFighter64InputMap.FromNote(noteNumber), value);
            if (_driveToggleLEDs)
                DriveToggleLED(noteNumber, value);
        }

        // ------------------------------------------------------------------
        // LED helpers
        // ------------------------------------------------------------------

        void DriveToggleLED(int noteNumber, bool isOn)
        {
            var output = MidiFighterOutput.Instance;
            if (output == null) return;
            if (isOn)
                output.SetLED(noteNumber, _toggleOnVelocity);
            else
                output.ClearLED(noteNumber);
        }

        /// <summary>
        /// Pushes the current toggle state of every tracked pad to the hardware LEDs.
        /// Called automatically on OnEnable when driveToggleLEDs is true.
        /// </summary>
        public void PushToggleLEDs()
        {
            if (!_driveToggleLEDs) return;
            foreach (var kvp in _toggleStates)
                DriveToggleLED(kvp.Key, kvp.Value);
        }
    }
}
