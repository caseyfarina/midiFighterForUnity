using System;
using UnityEngine;

namespace MidiFighter64
{
    /// <summary>
    /// Subscribes to MidiEventManager and converts raw MIDI events into typed,
    /// Akai MIDI Mix–specific C# events. Add this MonoBehaviour to the same
    /// GameObject as MidiEventManager (or any active GameObject in the scene).
    ///
    /// Event summary:
    ///   OnKnob(channel, row, value)     — a knob was turned
    ///   OnChannelFader(channel, value)  — a channel fader moved
    ///   OnMasterFader(value)            — the master fader moved
    ///   OnMute(channel, isOn)           — a Mute button (Solo NOT held)
    ///   OnSolo(channel, isNoteOn)       — a Mute button while Solo IS held
    ///   OnRecArm(channel, isOn)         — a Rec Arm button
    ///   OnSoloModifier(isDown)          — the SOLO modifier button state
    ///   OnBankLeft / OnBankRight        — bank navigation buttons pressed
    ///   IsSoloHeld                       — static bool, reflects modifier state
    ///
    /// Raw variants (OnKnobRaw, OnFaderRaw, OnButtonRaw) carry the full struct
    /// for cases where you need the CC/note number or want to switch on type.
    ///
    /// All channel arguments are 1-based (1–8). Fader/knob values are 0–1.
    ///
    /// OnKnob, OnChannelFader and OnMasterFader glide to each new value (see
    /// KnobGlideSeconds / FaderGlideSeconds, on by default), firing once a frame while
    /// they move. The raw events are never smoothed: read them for a stepped selector
    /// or a value passed on as MIDI.
    ///
    /// Mute and Rec-Arm latch (press-to-toggle) by default — see LatchMute /
    /// LatchRecArm. OnSolo is always momentary, because the SOLO modifier has
    /// to be held for those notes to be emitted at all.
    ///
    /// Override RouteCC() or RouteNote() in a subclass for custom behaviour.
    /// </summary>
    public class MidiMixRouter : MonoBehaviour
    {
        [Header("Latching (press-to-toggle) buttons")]
        [Tooltip("When true (default), Mute buttons latch on press. OnMute fires with the new latched state; note-off is ignored. Untick for momentary behaviour.")]
        [SerializeField] bool _latchMute   = true;
        [Tooltip("When true (default), Rec-Arm buttons latch on press. OnRecArm fires with the new latched state; note-off is ignored. Untick for momentary behaviour.")]
        [SerializeField] bool _latchRecArm = true;

        public bool LatchMute   { get => _latchMute;   set => _latchMute   = value; }
        public bool LatchRecArm { get => _latchRecArm; set => _latchRecArm = value; }

        readonly bool[] _muteLatched   = new bool[8];
        readonly bool[] _recArmLatched = new bool[8];

        [Header("Glide (knobs and faders)")]
        [Tooltip("Seconds a knob takes to glide to each new MIDI value, so 7-bit steps don't show. 0 = off: OnKnob reports MIDI values as they arrive. OnKnobRaw is never smoothed.")]
        [SerializeField, Min(0)] float _knobGlideSeconds  = 0.08f;
        [Tooltip("Seconds a fader (channel or master) takes to glide to each new MIDI value. 0 = off. OnFaderRaw is never smoothed.")]
        [SerializeField, Min(0)] float _faderGlideSeconds = 0.08f;

        /// <summary>Glide time for <see cref="OnKnob"/>, in seconds; 0 turns it off.
        /// Read <see cref="OnKnobRaw"/> for a control that needs the MIDI value itself
        /// (a stepped selector, a value passed on as MIDI).</summary>
        public float KnobGlideSeconds  { get => _knobGlideSeconds;  set => _knobGlideSeconds  = Mathf.Max(0f, value); }
        /// <summary>Glide time for <see cref="OnChannelFader"/> and <see cref="OnMasterFader"/>,
        /// in seconds; 0 turns it off. <see cref="OnFaderRaw"/> is never smoothed.</summary>
        public float FaderGlideSeconds { get => _faderGlideSeconds; set => _faderGlideSeconds = Mathf.Max(0f, value); }

        // [row-1, channel-1], and faders by channel with the master at index 0.
        readonly MidiGlide[,] _knobGlide  = new MidiGlide[MidiMixInputMap.KNOB_ROWS, MidiMixInputMap.CHANNEL_COUNT];
        readonly MidiGlide[]  _faderGlide = new MidiGlide[MidiMixInputMap.CHANNEL_COUNT + 1];

        // ------------------------------------------------------------------ //
        // Public events
        // ------------------------------------------------------------------ //

        /// <summary>A knob was turned. Args: channel (1–8), row (1–3), value (0–1).</summary>
        public static event Action<int, int, float> OnKnob;

        /// <summary>A channel fader moved. Args: channel (1–8), value (0–1).</summary>
        public static event Action<int, float> OnChannelFader;

        /// <summary>The master fader moved. Args: value (0–1).</summary>
        public static event Action<float> OnMasterFader;

        /// <summary>
        /// A Mute button changed state. Args: channel (1–8), isOn.
        /// While <see cref="LatchMute"/> is on (the default) this fires once per
        /// press with the new latched state; otherwise it fires on both note-on
        /// and note-off with the raw button state.
        /// </summary>
        public static event Action<int, bool> OnMute;

        /// <summary>
        /// A Mute button in Solo mode was pressed or released. Args: channel (1–8), isNoteOn.
        /// The hardware SOLO toggle causes the Mute row to emit a different note set.
        /// </summary>
        public static event Action<int, bool> OnSolo;

        /// <summary>
        /// A Rec Arm button changed state. Args: channel (1–8), isOn.
        /// While <see cref="LatchRecArm"/> is on (the default) this fires once per
        /// press with the new latched state; otherwise it fires on both note-on
        /// and note-off with the raw button state.
        /// </summary>
        public static event Action<int, bool> OnRecArm;

        /// <summary>The SOLO modifier button was pressed or released. Args: isDown.</summary>
        public static event Action<bool> OnSoloModifier;

        /// <summary>The Bank Left button was pressed.</summary>
        public static event Action OnBankLeft;

        /// <summary>The Bank Right button was pressed.</summary>
        public static event Action OnBankRight;

        /// <summary>True while the SOLO modifier button is being held.</summary>
        public static bool IsSoloHeld { get; private set; }

        // Raw events — useful when you need the full struct or want to fan-out
        // custom routing without subclassing.
        public static event Action<MixKnob,  float> OnKnobRaw;
        public static event Action<MixFader, float> OnFaderRaw;
        public static event Action<MixButton, bool> OnButtonRaw;

        static MidiMixRouter _active;
        bool _isActiveInstance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnKnob = null; OnChannelFader = null; OnMasterFader = null;
            OnMute = null; OnSolo = null; OnRecArm = null;
            OnSoloModifier = null; OnBankLeft = null; OnBankRight = null;
            OnKnobRaw = null; OnFaderRaw = null; OnButtonRaw = null;
            IsSoloHeld = false;
            _active = null;
        }

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        void OnEnable()
        {
            // A second live router (duplicated prefab, additive scene) would
            // otherwise double-fire every event and desync each instance's own
            // latch arrays — disable the duplicate instead.
            if (_active != null && _active != this)
            {
                Debug.LogWarning($"[MidiMixRouter] Another instance ({_active.name}) is already active — disabling this duplicate on {name} to avoid double-firing events.");
                enabled = false;
                return;
            }
            _active = this;
            _isActiveInstance = true;

            MidiEventManager.OnControlChange += HandleCC;
            MidiEventManager.OnNoteOn        += HandleNoteOn;
            MidiEventManager.OnNoteOff       += HandleNoteOff;
        }

        void OnDisable()
        {
            if (!_isActiveInstance) return; // was a disabled duplicate; nothing to unwind
            _isActiveInstance = false;
            if (_active == this) _active = null;

            MidiEventManager.OnControlChange -= HandleCC;
            MidiEventManager.OnNoteOn        -= HandleNoteOn;
            MidiEventManager.OnNoteOff       -= HandleNoteOff;

            // Re-enabled, each control's next value is taken as-is, not glided from a stale one.
            for (int r = 0; r < MidiMixInputMap.KNOB_ROWS; r++)
                for (int c = 0; c < MidiMixInputMap.CHANNEL_COUNT; c++)
                    _knobGlide[r, c].Reset();
            for (int i = 0; i < _faderGlide.Length; i++) _faderGlide[i].Reset();
        }

        void Update()
        {
            if (!_isActiveInstance) return;
            float dt = Time.unscaledDeltaTime;

            for (int r = 0; r < MidiMixInputMap.KNOB_ROWS; r++)
                for (int c = 0; c < MidiMixInputMap.CHANNEL_COUNT; c++)
                    if (_knobGlide[r, c].Step(_knobGlideSeconds, dt))
                        OnKnob?.Invoke(c + 1, r + 1, _knobGlide[r, c].Value);

            for (int i = 0; i < _faderGlide.Length; i++)
                if (_faderGlide[i].Step(_faderGlideSeconds, dt))
                    FireFader(i, _faderGlide[i].Value);
        }

        static void FireFader(int channel, float value)
        {
            if (channel == 0) OnMasterFader?.Invoke(value);
            else              OnChannelFader?.Invoke(channel, value);
        }

        // ------------------------------------------------------------------ //
        // Handlers
        // ------------------------------------------------------------------ //

        void HandleCC(int ccNumber, float value) => RouteCC(ccNumber, value);

        void HandleNoteOn(int noteNumber, float velocity)  => RouteNote(noteNumber, isNoteOn: true);
        void HandleNoteOff(int noteNumber)                 => RouteNote(noteNumber, isNoteOn: false);

        // ------------------------------------------------------------------ //
        // Routing — override in a subclass for custom logic
        // ------------------------------------------------------------------ //

        protected virtual void RouteCC(int ccNumber, float value)
        {
            if (MidiMixInputMap.TryGetKnob(ccNumber, out var knob))
            {
                OnKnobRaw?.Invoke(knob, value);
                // Gliding, OnKnob fires from Update as the value moves.
                if (_knobGlide[knob.row - 1, knob.channel - 1].SetTarget(value, _knobGlideSeconds))
                    OnKnob?.Invoke(knob.channel, knob.row, value);
                return;
            }

            if (MidiMixInputMap.TryGetFader(ccNumber, out var fader))
            {
                OnFaderRaw?.Invoke(fader, value);
                int i = fader.isMaster ? 0 : fader.channel;
                if (_faderGlide[i].SetTarget(value, _faderGlideSeconds))
                    FireFader(i, value);
            }
        }

        protected virtual void RouteNote(int noteNumber, bool isNoteOn)
        {
            if (MidiMixInputMap.TryGetButton(noteNumber, out var button))
            {
                OnButtonRaw?.Invoke(button, isNoteOn);
                switch (button.type)
                {
                    case MidiMixButton.Mute:   FireMuteOrRecArm(button.channel, isNoteOn, _latchMute,   _muteLatched,   OnMute);   break;
                    case MidiMixButton.Solo:   OnSolo?.Invoke(button.channel, isNoteOn);   break;
                    case MidiMixButton.RecArm: FireMuteOrRecArm(button.channel, isNoteOn, _latchRecArm, _recArmLatched, OnRecArm); break;
                }
                return;
            }

            if (MidiMixInputMap.IsSoloModifier(noteNumber))
            {
                IsSoloHeld = isNoteOn;
                OnSoloModifier?.Invoke(isNoteOn);
                return;
            }

            if (isNoteOn && MidiMixInputMap.IsBankLeft(noteNumber))  OnBankLeft?.Invoke();
            if (isNoteOn && MidiMixInputMap.IsBankRight(noteNumber)) OnBankRight?.Invoke();
        }

        static void FireMuteOrRecArm(int channel, bool isNoteOn, bool latch,
                                     bool[] state, Action<int, bool> evt)
        {
            int idx = channel - 1;
            if (idx < 0 || idx >= state.Length) return;

            if (latch)
            {
                if (!isNoteOn) return;             // ignore note-off in latch mode
                state[idx] = !state[idx];
                evt?.Invoke(channel, state[idx]);  // fire with new latched state
            }
            else
            {
                evt?.Invoke(channel, isNoteOn);    // momentary
            }
        }
    }
}
