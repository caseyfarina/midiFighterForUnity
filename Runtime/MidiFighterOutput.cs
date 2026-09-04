using System;
using System.Threading;
using UnityEngine;
using RtMidi;

namespace MidiFighter64
{
    /// <summary>
    /// Hardware LED color palette for the Midi Fighter 64 (velocity → color, Ch3).
    /// Values confirmed on hardware, firmware 24 Jul 2017 (expanded color palette).
    /// </summary>
    public enum MidiFighterLEDColor
    {
        Off        = 0,
        DarkGrey   = 1,
        Grey       = 2,
        White      = 3,
        BrightBlue = 37,
        DarkBlue   = 39,
        BrightPink = 56,
        DarkPink   = 59,
    }

    /// <summary>
    /// Approximate RGB values matching each MF64 palette entry. Useful for keeping
    /// on-screen visuals in sync with hardware LED colors.
    /// </summary>
    public static class MidiFighterLEDColorExtensions
    {
        public static UnityEngine.Color ToUnityColor(this MidiFighterLEDColor c) => c switch
        {
            // The three neutrals are strictly r == g == b, for the same reason
            // MidiStatusDrawer.Palette's neutrals are: a channel bias reads as a
            // tint once the pad sits against the semi-transparent panel, and a
            // blue-biased dark grey reads distinctly brown. DarkGrey is also kept
            // clear of the dark panel's own lightness — at 0.25 it was close
            // enough to read as a muddy patch rather than a lit pad, and an
            // ambiguous patch takes its apparent hue from whatever surrounds it.
            MidiFighterLEDColor.Off        => new UnityEngine.Color(0.05f, 0.05f, 0.05f),
            MidiFighterLEDColor.DarkGrey   => new UnityEngine.Color(0.34f, 0.34f, 0.34f),
            MidiFighterLEDColor.Grey       => new UnityEngine.Color(0.56f, 0.56f, 0.56f),
            MidiFighterLEDColor.White      => UnityEngine.Color.white,
            MidiFighterLEDColor.BrightBlue => new UnityEngine.Color(0.25f, 0.55f, 1.00f),
            MidiFighterLEDColor.DarkBlue   => new UnityEngine.Color(0.10f, 0.20f, 0.60f),
            MidiFighterLEDColor.BrightPink => new UnityEngine.Color(1.00f, 0.30f, 0.75f),
            MidiFighterLEDColor.DarkPink   => new UnityEngine.Color(0.55f, 0.15f, 0.40f),
            _                              => UnityEngine.Color.magenta,
        };
    }


    /// <summary>
    /// Sends MIDI Note On messages to the Midi Fighter 64 to control its LEDs.
    /// Uses jp.keijiro.rtmidi (RtMidi.Runtime) for cross-platform MIDI output.
    ///
    /// Color model (MF64 User Guide Fig 2):
    ///   Color = velocity sent on Channel 3 (MIDI channel 3, layer index 2), same note as the pad.
    ///   Velocity 0 disables MIDI color and reverts to the Utility inactive color.
    ///   Set the inactive color to black in the Utility for a true "off" effect.
    ///   Animations are on Channel 4 (layer index 3), note = hardware note − 36 (Appendix 2).
    ///
    /// ledChannelIndex selects the output layer (0-based):
    ///   2 → MIDI Channel 3 — color layer (default)
    ///   3 → MIDI Channel 4 — animation layer
    ///
    /// Hotplug: PortCount is polled on a background thread; if it changes the port
    /// is re-opened there too. Native RtMidi enumerate/open/close calls can block
    /// for a noticeable stretch during a hotplug transition, so none of this runs
    /// on the main thread — doing so previously could stall or hang the editor.
    /// `_output` is only ever swapped under `_lock`, so a send from the main thread
    /// and a reopen from the poll thread can never race on the same handle.
    /// </summary>
    public class MidiFighterOutput : MonoBehaviour
    {
        public static MidiFighterOutput Instance { get; private set; }

        /// <summary>
        /// Raised on the main thread once the MF64 output port has been (re)opened and
        /// LED writes will actually reach the hardware.
        ///
        /// The port is opened on this component's background poll thread, which is why
        /// this exists rather than having consumers watch <see cref="IsReady"/>: it is the
        /// safe, main-thread edge of that transition. After a hot-plug the hardware LEDs
        /// are whatever the device powered up with, so every owner of LED state must
        /// re-push here or the surface will disagree with application state.
        /// <see cref="MidiFighterButtonRouter"/> re-pushes its toggle colours off this;
        /// subscribe if you drive LEDs yourself.
        ///
        /// Pairs with <see cref="MidiEventManager.OnDevicesReconnected"/>, which covers the
        /// input side. Either may fire first — a re-push must be idempotent.
        /// </summary>
        public static event Action OnOutputPortOpened;

        const int COLOR_CHANNEL_INDEX     = 2; // MIDI Ch3
        const int ANIMATION_CHANNEL_INDEX = 3; // MIDI Ch4

        [Header("LED Settings")]
        [Tooltip("MIDI channel layer (0-based). 2 = Channel 3 (color, default). 3 = Channel 4 (animation).")]
        [Range(0, 3)]
        public int ledChannelIndex = COLOR_CHANNEL_INDEX;

        [Header("Startup")]
        [Tooltip("Send Note Off (velocity 0) to all 64 pads immediately after connecting, clearing any LED state left over from a previous session.")]
        [SerializeField] bool _clearOnStart = true;

        [Tooltip("If no port with \"Fighter\" in its name is found, fall back to the first available MIDI output port. " +
                 "Off by default: with a synth, DAW, or virtual port and no MF64 attached, an enabled fallback sends LED " +
                 "data (and ClearOnStart's note-off burst) to whatever that unrelated device is.")]
        [SerializeField] bool _allowFallbackPort = false;

        const float PORT_POLL_INTERVAL = 1.0f; // seconds between hotplug checks

        readonly object _lock = new object();

        MidiOut _probe;
        MidiOut _output;
        int     _lastPortCount = -1;
        volatile bool _running;
        Thread  _pollThread;

        // Set by the poll thread when it opens a port; consumed by Update on the main
        // thread, which is where OnOutputPortOpened is actually raised. Unity API and
        // arbitrary subscriber code must never run on the poll thread.
        volatile bool _portOpenedPending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance           = null;
            OnOutputPortOpened = null;
        }

        void Update()
        {
            if (!_portOpenedPending) return;
            _portOpenedPending = false;
            OnOutputPortOpened?.Invoke();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            _probe = MidiOut.Create();
            _running = true;
            _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "MidiFighterOutput.Poll" };
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
                if (name != null && name.IndexOf("Fighter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    OpenAndAssign(i, name);
                    return;
                }
            }

            if (count == 0)
            {
                Debug.LogWarning("[MidiFighterOutput] No MIDI output device found. LEDs will not respond.");
            }
            else if (_allowFallbackPort)
            {
                string name = null;
                try { name = _probe.GetPortName(0); } catch { }
                OpenAndAssign(0, name);
                Debug.Log($"[MidiFighterOutput] Midi Fighter not found — using first available: {name}");
            }
            else
            {
                Debug.LogWarning("[MidiFighterOutput] Midi Fighter not found among MIDI outputs (fallback port disabled). LEDs will not respond.");
            }
        }

        void OpenAndAssign(int index, string name)
        {
            try
            {
                var output = MidiOut.Create();
                output.OpenPort(index);
                lock (_lock) { _output = output; }
                // Flag rather than invoke: this runs on the poll thread. Update raises it.
                _portOpenedPending = true;
                Debug.Log($"[MidiFighterOutput] Opened: {name}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MidiFighterOutput] Failed to open port {index} ({name}): {e.Message}");
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

        void SendNoteOn(int channelIndex, int note, int velocity)
        {
            MidiOut output;
            lock (_lock) { output = _output; }
            if (output == null || output.IsInvalid) return;

            try
            {
                Span<byte> msg = stackalloc byte[]
                {
                    (byte)(0x90 | (channelIndex & 0x0F)),
                    (byte)(note     & 0x7F),
                    (byte)(velocity & 0x7F)
                };
                output.SendMessage(msg);
            }
            catch (Exception e)
            {
                // A native throw here would otherwise unwind into whatever static
                // event invoked us (e.g. OnMute), aborting any subscribers still
                // queued after us. Swallow it and drop the port so the next poll
                // reopens it instead.
                Debug.LogWarning($"[MidiFighterOutput] LED send failed, will reopen on next poll: {e.Message}");
                CloseOutput();
            }
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Sets the LED color for a pad. Color is determined by velocity (User Guide Fig 2).
        /// Velocity 0 reverts to the Utility inactive color.
        /// </summary>
        /// <param name="noteNumber">Hardware MIDI note (36–99).</param>
        /// <param name="velocity">Color velocity (0–127). See <see cref="MidiFighterLEDColor"/>.</param>
        public void SetLED(int noteNumber, int velocity)
            => SendNoteOn(ledChannelIndex, noteNumber, velocity);

        /// <summary>Sets the LED color for a pad using the palette enum.</summary>
        public void SetLED(int noteNumber, MidiFighterLEDColor color)
            => SendNoteOn(ledChannelIndex, noteNumber, (int)color);

        /// <summary>True once the MF64 output port is open and writes will actually reach the hardware.</summary>
        public bool IsReady { get { lock (_lock) { return _output != null && !_output.IsInvalid; } } }

        /// <summary>Sends the same velocity to every one of the 64 pads.</summary>
        public void SetAllLEDs(int velocity)
        {
            for (int n = MidiFighter64InputMap.NOTE_OFFSET; n <= MidiFighter64InputMap.NOTE_MAX; n++)
                SetLED(n, velocity);
        }

        /// <summary>
        /// Sends velocity 0 for this note, reverting it to the Utility inactive color.
        /// Set the inactive color to black in the Utility for a true "off".
        /// </summary>
        public void ClearLED(int noteNumber) => SetLED(noteNumber, 0);

        /// <summary>Clears all 64 LEDs.</summary>
        public void ClearAllLEDs() => SetAllLEDs(0);

        /// <summary>
        /// Sends an animation trigger on Channel 4 (layer index 3).
        /// The animation note is automatically offset by −36 per Appendix 2.
        /// </summary>
        /// <param name="noteNumber">Hardware note (36–99); offset applied internally.</param>
        /// <param name="velocity">Animation index / intensity (0–127).</param>
        public void SetAnimation(int noteNumber, int velocity)
            => SendNoteOn(ANIMATION_CHANNEL_INDEX, noteNumber - MidiFighter64InputMap.NOTE_OFFSET, velocity);
    }
}
