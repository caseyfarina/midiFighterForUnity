using System;
using System.Threading;
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
    /// Hotplug: PortCount is polled on a background thread; if it changes the port
    /// is re-opened there too. Native RtMidi enumerate/open/close calls can block
    /// for a noticeable stretch during a hotplug transition, so none of this runs
    /// on the main thread — doing so previously could stall or hang the editor.
    /// `_output` is only ever swapped under `_lock`, so a send from the main thread
    /// and a reopen from the poll thread can never race on the same handle.
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

        readonly object _lock = new object();

        MidiOut _probe;
        MidiOut _output;
        int     _lastPortCount = -1;
        volatile bool _running;
        Thread  _pollThread;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()
        {
            // Subscribe to router events so LEDs follow the router's semantics —
            // momentary or latched — instead of raw note-on/note-off. Always
            // subscribed, regardless of _autoMirrorButtons, so flipping that flag
            // on at runtime takes effect immediately; the handlers check the flag.
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
            _running = true;
            _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "MidiMixOutput.Poll" };
            _pollThread.Start();
        }

        void OnDestroy()
        {
            _running = false;
            if (_pollThread != null && _pollThread.IsAlive) _pollThread.Join(2000);
            try { if (_clearOnStart) ClearAllLEDs(); } catch { }
            try { CloseOutput(); } catch { }
            try { if (_probe != null && !_probe.IsInvalid) _probe.Dispose(); } catch { }
            _probe = null;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // Background thread: all hotplug enumeration/open/close happens here.
        // ------------------------------------------------------------------

        void PollLoop()
        {
            try { _lastPortCount = _probe.PortCount; } catch { _lastPortCount = -1; }
            TryOpenPort();
            if (_clearOnStart) ClearAllLEDs();

            while (_running)
            {
                // Sleep in short increments so OnDestroy's Join(2000) returns promptly
                // instead of waiting out the full poll interval.
                for (int waited = 0; waited < PORT_POLL_INTERVAL * 1000 && _running; waited += 100)
                    Thread.Sleep(100);
                if (!_running) break;
                if (_probe == null || _probe.IsInvalid) continue;

                int count;
                try { count = _probe.PortCount; } catch { continue; }
                if (count == _lastPortCount) continue;
                _lastPortCount = count;

                CloseOutput();
                TryOpenPort();
            }
        }

        void TryOpenPort()
        {
            if (_probe == null || _probe.IsInvalid) return;
            int count;
            try { count = _probe.PortCount; } catch { return; }

            for (int i = 0; i < count; i++)
            {
                string name;
                try { name = _probe.GetPortName(i); } catch { continue; }
                if (name != null && name.IndexOf("MIDI Mix", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    OpenAndAssign(i, name);
                    return;
                }
            }

            Debug.LogWarning("[MidiMixOutput] Akai MIDI Mix output port not found. LEDs will not light up.");
        }

        void OpenAndAssign(int index, string name)
        {
            try
            {
                var output = MidiOut.Create();
                output.OpenPort(index);
                lock (_lock) { _output = output; }
                Debug.Log($"[MidiMixOutput] Opened: {name}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MidiMixOutput] Failed to open port {index} ({name}): {e.Message}");
            }
        }

        void CloseOutput()
        {
            MidiOut old;
            lock (_lock) { old = _output; _output = null; }
            if (old != null && !old.IsInvalid)
            {
                try { old.ClosePort(); } catch { }
                try { old.Dispose(); } catch { }
            }
        }

        void SendNoteOn(int note, int velocity)
        {
            MidiOut output;
            lock (_lock) { output = _output; }
            if (output == null || output.IsInvalid) return;

            try
            {
                Span<byte> msg = stackalloc byte[]
                {
                    (byte)NOTE_ON_STATUS,
                    (byte)(note     & 0x7F),
                    (byte)(velocity & 0x7F),
                };
                output.SendMessage(msg);
            }
            catch (Exception e)
            {
                // A native throw here would otherwise unwind into whatever static
                // event invoked us (e.g. OnMute), aborting any subscribers still
                // queued after us. Swallow it and drop the port so the next poll
                // reopens it instead.
                Debug.LogWarning($"[MidiMixOutput] LED send failed, will reopen on next poll: {e.Message}");
                CloseOutput();
            }
        }

        // ------------------------------------------------------------------
        // Auto-mirror handlers
        // ------------------------------------------------------------------

        void HandleMute(int channel, bool state)   { if (_autoMirrorButtons) SetMuteLED(channel, state); }
        void HandleSolo(int channel, bool state)   { if (_autoMirrorButtons) SetSoloLED(channel, state); }
        void HandleRecArm(int channel, bool state) { if (_autoMirrorButtons) SetRecArmLED(channel, state); }

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
