using System;
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
    /// Hotplug: PortCount is polled each Update; if it changes the port is re-opened.
    /// </summary>
    public class MidiFighterOutput : MonoBehaviour
    {
        public static MidiFighterOutput Instance { get; private set; }

        const int COLOR_CHANNEL_INDEX     = 2; // MIDI Ch3
        const int ANIMATION_CHANNEL_INDEX = 3; // MIDI Ch4

        [Header("LED Settings")]
        [Tooltip("MIDI channel layer (0-based). 2 = Channel 3 (color, default). 3 = Channel 4 (animation).")]
        [Range(0, 3)]
        public int ledChannelIndex = COLOR_CHANNEL_INDEX;

        [Header("Startup")]
        [Tooltip("Send Note Off (velocity 0) to all 64 pads immediately after connecting, clearing any LED state left over from a previous session.")]
        [SerializeField] bool _clearOnStart = true;

        const float PORT_POLL_INTERVAL = 1.0f; // seconds between hotplug checks

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

        void Start()
        {
            _probe = MidiOut.Create();
            _lastPortCount     = _probe.PortCount;
            _nextPortPollTime  = Time.unscaledTime + PORT_POLL_INTERVAL;
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
                if (name != null &&
                    name.IndexOf("Fighter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _output = MidiOut.Create();
                    _output.OpenPort(i);
                    Debug.Log($"[MidiFighterOutput] Opened: {name}");
                    return;
                }
            }

            // Fallback to first available output if Midi Fighter not found by name
            if (count > 0)
            {
                _output = MidiOut.Create();
                _output.OpenPort(0);
                Debug.Log($"[MidiFighterOutput] Midi Fighter not found — using first available: {_probe.GetPortName(0)}");
            }
            else
            {
                Debug.LogWarning("[MidiFighterOutput] No MIDI output device found. LEDs will not respond.");
            }
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

        void SendNoteOn(int channelIndex, int note, int velocity)
        {
            if (_output == null || _output.IsInvalid) return;
            Span<byte> msg = stackalloc byte[]
            {
                (byte)(0x90 | (channelIndex & 0x0F)),
                (byte)(note     & 0x7F),
                (byte)(velocity & 0x7F)
            };
            _output.SendMessage(msg);
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
        public bool IsReady => _output != null && !_output.IsInvalid;

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
