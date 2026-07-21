using System;
using UnityEngine;
using RtMidi;

namespace MidiFighter64
{
    /// <summary>
    /// Sends MIDI Note On messages back to the Akai MIDI Mix to control its
    /// Mute (amber) and Rec-Arm (red) button LEDs. The MIDI Mix buttons do
    /// not self-illuminate on press — the host must echo the state back.
    ///
    /// Uses jp.keijiro.rtmidi (RtMidi.Runtime) for cross-platform MIDI output.
    ///
    /// When AutoMirrorButtons is enabled (default), Mute / Solo-mute / Rec-Arm
    /// state is echoed to light the corresponding LED. It mirrors the router's
    /// events rather than raw MIDI, so it follows whatever semantics the router
    /// is using: with <see cref="MidiMixRouter.LatchMute"/> / LatchRecArm on
    /// (the default) the LED holds until the button is pressed again, and with
    /// them off it lights on press and clears on release. Solo is always
    /// momentary. For fully custom state, disable auto-mirror and call
    /// <see cref="SetMuteLED"/> / <see cref="SetRecArmLED"/> yourself.
    ///
    /// Hotplug: PortCount is polled each Update; if it changes the port is re-opened.
    /// </summary>
    public class MidiMixOutput : MonoBehaviour
    {
        public static MidiMixOutput Instance { get; private set; }

        const int NOTE_ON_STATUS = 0x90; // channel 1

        [Header("Behavior")]
        [Tooltip("Automatically echo Mute / Solo-mute / Rec-Arm button presses back to the hardware so LEDs light on press and clear on release.")]
        [SerializeField] bool _autoMirrorButtons = true;

        [Header("Startup")]
        [Tooltip("Send Note Off to every Mute / Solo / Rec-Arm note immediately after connecting, clearing any LED state left over from a previous session.")]
        [SerializeField] bool _clearOnStart = true;

        const float PORT_POLL_INTERVAL = 1.0f;

        MidiOut _probe;
        MidiOut _output;
        int     _lastPortCount    = -1;
        float   _nextPortPollTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()
        {
            if (!_autoMirrorButtons) return;
            // Subscribe to router events so LEDs follow the router's semantics —
            // momentary or latched — instead of raw note-on/note-off.
            MidiMixRouter.OnMute   += HandleMute;
            MidiMixRouter.OnSolo   += HandleSolo;
            MidiMixRouter.OnRecArm += HandleRecArm;
        }

        void OnDisable()
        {
            MidiMixRouter.OnMute   -= HandleMute;
            MidiMixRouter.OnSolo   -= HandleSolo;
            MidiMixRouter.OnRecArm -= HandleRecArm;
        }

        void Start()
        {
            _probe = MidiOut.Create();
            _lastPortCount    = _probe.PortCount;
            _nextPortPollTime = Time.unscaledTime + PORT_POLL_INTERVAL;
            TryOpenPort();
            if (_clearOnStart) ClearAllLEDs();
        }

        void Update()
        {
            if (_probe == null || _probe.IsInvalid) return;
            if (Time.unscaledTime < _nextPortPollTime) return;
            _nextPortPollTime = Time.unscaledTime + PORT_POLL_INTERVAL;

            int count = _probe.PortCount;
            if (count == _lastPortCount) return;
            _lastPortCount = count;
            CloseOutput();
            TryOpenPort();
        }

        void OnDestroy()
        {
            try { if (_clearOnStart) ClearAllLEDs(); } catch { }
            try { CloseOutput(); } catch { }
            try { if (_probe != null && !_probe.IsInvalid) _probe.Dispose(); } catch { }
            _probe = null;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------

        void TryOpenPort()
        {
            if (_probe == null || _probe.IsInvalid) return;
            int count = _probe.PortCount;

            for (int i = 0; i < count; i++)
            {
                string name = _probe.GetPortName(i);
                if (name != null && name.IndexOf("MIDI Mix", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _output = MidiOut.Create();
                    _output.OpenPort(i);
                    Debug.Log($"[MidiMixOutput] Opened: {name}");
                    return;
                }
            }

            Debug.LogWarning("[MidiMixOutput] Akai MIDI Mix output port not found. LEDs will not light up.");
        }

        void CloseOutput()
        {
            if (_output != null && !_output.IsInvalid)
            {
                _output.ClosePort();
                _output.Dispose();
            }
            _output = null;
        }

        void SendNoteOn(int note, int velocity)
        {
            if (_output == null || _output.IsInvalid) return;
            Span<byte> msg = stackalloc byte[]
            {
                (byte)NOTE_ON_STATUS,
                (byte)(note     & 0x7F),
                (byte)(velocity & 0x7F),
            };
            _output.SendMessage(msg);
        }

        // ------------------------------------------------------------------
        // Auto-mirror handlers
        // ------------------------------------------------------------------

        void HandleMute(int channel, bool state)   => SetMuteLED(channel, state);
        void HandleSolo(int channel, bool state)   => SetSoloLED(channel, state);
        void HandleRecArm(int channel, bool state) => SetRecArmLED(channel, state);

        // ------------------------------------------------------------------
        // Public API — call these to override state manually.
        // ------------------------------------------------------------------

        /// <summary>Light or clear a Mute button LED. Channel is 1-based (1–8).</summary>
        public void SetMuteLED(int channel, bool lit)
            => SetButtonLED(MidiMixInputMap.MuteNotes, channel, lit);

        /// <summary>Light or clear a Rec-Arm button LED. Channel is 1-based (1–8).</summary>
        public void SetRecArmLED(int channel, bool lit)
            => SetButtonLED(MidiMixInputMap.RecArmNotes, channel, lit);

        /// <summary>Light or clear the "Mute in Solo mode" LED (fires when SOLO is held). Channel is 1-based (1–8).</summary>
        public void SetSoloLED(int channel, bool lit)
            => SetButtonLED(MidiMixInputMap.SoloNotes, channel, lit);

        void SetButtonLED(int[] notes, int channel, bool lit)
        {
            if (channel < 1 || channel > notes.Length) return;
            SendNoteOn(notes[channel - 1], lit ? 127 : 0);
        }

        /// <summary>Clears every Mute, Solo, and Rec-Arm LED.</summary>
        public void ClearAllLEDs()
        {
            for (int ch = 1; ch <= MidiMixInputMap.CHANNEL_COUNT; ch++)
            {
                SetMuteLED(ch,   false);
                SetSoloLED(ch,   false);
                SetRecArmLED(ch, false);
            }
        }
    }
}
