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
    /// When driveToggleLEDs / driveButtonLEDs are true, pad state is mirrored to
    /// <see cref="MidiFighterOutput.Instance"/> automatically.
    /// </summary>
    public class MidiFighterButtonRouter : MonoBehaviour
    {
        [SerializeField] MidiFighter64ButtonConfig _config;

        [Tooltip("Seconds between OnButtonHold fires while a pad is held. " +
                 "0 = fire every frame (elapsed keeps accumulating).")]
        [SerializeField] float _holdRepeatInterval = 0f;

        [Header("LED Feedback")]
        [Tooltip("Mirror toggle state to MidiFighterOutput LEDs automatically.")]
        [SerializeField] bool _driveToggleLEDs = true;

        [Tooltip("LED color when a Toggle pad is turned on.")]
        [SerializeField] MidiFighterLEDColor _toggleOnColor  = MidiFighterLEDColor.White;

        [Tooltip("LED color when a Toggle pad is turned off.")]
        [SerializeField] MidiFighterLEDColor _toggleOffColor = MidiFighterLEDColor.DarkGrey;

        [Tooltip("Flash LEDs while a Button pad is held.")]
        [SerializeField] bool _driveButtonLEDs = true;

        [Tooltip("LED color while a Button pad is held down.")]
        [SerializeField] MidiFighterLEDColor _buttonDownColor = MidiFighterLEDColor.BrightPink;

        public MidiFighterLEDColor ButtonDownColor
        {
            get => _buttonDownColor;
            set => _buttonDownColor = value;
        }

        public MidiFighterLEDColor ToggleOnColor
        {
            get => _toggleOnColor;
            set => _toggleOnColor = value;
        }

        public MidiFighterLEDColor ToggleOffColor
        {
            get => _toggleOffColor;
            set => _toggleOffColor = value;
        }

        /// <summary>The ScriptableObject that decides Button vs Toggle per pad. May be null.</summary>
        public MidiFighter64ButtonConfig Config
        {
            get => _config;
            set => _config = value;
        }

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnButtonPress = null; OnButtonHold = null;
            OnButtonRelease = null; OnToggle = null;
        }

        // ------------------------------------------------------------------

        // note → elapsed hold time (Button mode)
        readonly Dictionary<int, float> _heldPads     = new();
        // note → toggled state (Toggle mode)
        readonly Dictionary<int, bool>  _toggleStates = new();
        // reused per-frame to avoid List allocs in Update
        readonly List<int>              _heldSnapshot = new(64);

        // Whether we've done the initial toggle-off LED push. Deferred until
        // MidiFighterOutput reports IsReady, because Router.OnEnable typically
        // runs before MidiFighterOutput.Start opens the MIDI port.
        bool _initialLedPushDone;

        // ------------------------------------------------------------------

        void OnEnable()
        {
            MidiEventManager.OnNoteOn  += HandleNoteOn;
            MidiEventManager.OnNoteOff += HandleNoteOff;
            _initialLedPushDone = false;
            PushToggleLEDs(); // may no-op if the output port isn't open yet;
                              // Update() retries once MidiFighterOutput.IsReady.
        }

        void OnDisable()
        {
            MidiEventManager.OnNoteOn  -= HandleNoteOn;
            MidiEventManager.OnNoteOff -= HandleNoteOff;
            _heldPads.Clear();
        }

        void Update()
        {
            // Retry the initial LED push until MidiFighterOutput is ready.
            if (!_initialLedPushDone
                && MidiFighterOutput.Instance != null
                && MidiFighterOutput.Instance.IsReady)
            {
                PushToggleLEDs();
                _initialLedPushDone = true;
            }

            if (_heldPads.Count == 0) return;

            // Snapshot keys to avoid mutation-during-enumeration when hold-repeat resets elapsed
            _heldSnapshot.Clear();
            _heldSnapshot.AddRange(_heldPads.Keys);

            foreach (int note in _heldSnapshot)
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
                if (_driveButtonLEDs) DriveButtonLED(note, true);
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
                if (_driveButtonLEDs) DriveButtonLED(note, false);
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
            MidiFighterLEDColor color;
            if (isOn)
            {
                // Per-pad override wins over the global _toggleOnColor.
                color = GetPerPadColorOr(noteNumber, _toggleOnColor);
            }
            else color = _toggleOffColor;
            SetPadColor(noteNumber, color);
        }

        void DriveButtonLED(int noteNumber, bool isDown)
        {
            MidiFighterLEDColor color;
            if (isDown)
            {
                color = GetPerPadColorOr(noteNumber, _buttonDownColor);
            }
            else color = MidiFighterLEDColor.Off;
            SetPadColor(noteNumber, color);
        }

        MidiFighterLEDColor GetPerPadColorOr(int noteNumber, MidiFighterLEDColor fallback)
        {
            if (_config == null) return fallback;
            var btn = MidiFighter64InputMap.FromNote(noteNumber);
            var c = _config.GetColor(btn);
            return c == MidiFighterLEDColor.Off ? fallback : c;
        }

        static void SetPadColor(int noteNumber, MidiFighterLEDColor color)
            => MidiFighterOutput.Instance?.SetLED(noteNumber, color);

        MidiFighterLEDColor _lastPreviewColor;

        void OnValidate()
        {
            if (!Application.isPlaying) return;
            if (_toggleOnColor == _lastPreviewColor) return;
            _lastPreviewColor = _toggleOnColor;
            MidiFighterOutput.Instance?.SetAllLEDs((int)_toggleOnColor);
        }

        /// <summary>
        /// Pushes toggle-state LED colors to the hardware for every pad that is
        /// currently in Toggle mode (per the assigned <see cref="MidiFighter64ButtonConfig"/>).
        /// Untouched pads receive the toggle-off color so they visually match the
        /// state that toggling them will exit. Called automatically on OnEnable
        /// (and retried from Update once the output port is open).
        /// </summary>
        public void PushToggleLEDs()
        {
            if (!_driveToggleLEDs) return;

            // Explicitly push toggle-off (or current known state) to every pad
            // whose config mode is Toggle. Prevents "invisible" pads at launch
            // that only light up after the first press.
            for (int i = 0; i < 64; i++)
            {
                int note = MidiFighter64InputMap.NOTE_OFFSET + i; // any 36-99 works; we resolve mode via config
                var btn  = MidiFighter64InputMap.FromNote(note);
                var mode = _config != null ? _config.GetMode(btn) : MidiFighterButtonMode.Button;
                if (mode != MidiFighterButtonMode.Toggle) continue;

                bool state = _toggleStates.TryGetValue(note, out bool s) && s;
                DriveToggleLED(note, state);
            }
        }
    }
}
