using System;
using UnityEngine;
using RtMidi;

namespace MidiFighter64
{
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

        [Header("LED Settings")]
        [Tooltip("MIDI channel layer (0-based). 2 = Channel 3 (color, default). 3 = Channel 4 (animation).")]
        [Range(0, 3)]
        public int ledChannelIndex = 2;

        // ------------------------------------------------------------------
        // MidiFighterColor — velocity → color mapping (MF64 User Guide Fig 2)
        // TODO: transcribe the complete palette from Fig 2 / the Utility app.
        //       Do NOT invent values. The only confirmed anchor is velocity 5 = bright red on Ch3.
        // ------------------------------------------------------------------
        public static class MidiFighterColor
        {
            /// <summary>Velocity 0 reverts to the Utility inactive color (set it to black for true off).</summary>
            public const int Off = 0;

            // TODO: fill from MF64 User Guide Fig 2 after confirming on hardware
            // public const int BrightRed    =   5; // confirmed: Ch3 vel 5 = bright red
            // public const int ...          = ...;
        }

        // ------------------------------------------------------------------

        MidiOut _probe;   // enumerate / hotplug (never opened to a specific port)
        MidiOut _output;  // open port handle used for SendMessage
        int     _lastPortCount = -1;

        // ------------------------------------------------------------------

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            _probe = MidiOut.Create();
            _lastPortCount = _probe.PortCount;
            TryOpenPort();
        }

        void Update()
        {
            if (_probe == null || _probe.IsInvalid) return;
            int count = _probe.PortCount;
            if (count != _lastPortCount)
            {
                _lastPortCount = count;
                CloseOutput();
                TryOpenPort();
            }
        }

        void OnDestroy()
        {
            CloseOutput();
            if (_probe != null && !_probe.IsInvalid) _probe.Dispose();
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
        /// <param name="velocity">Color velocity (0–127). See <see cref="MidiFighterColor"/>.</param>
        public void SetLED(int noteNumber, int velocity)
            => SendNoteOn(ledChannelIndex, noteNumber, velocity);

        /// <summary>
        /// Sends velocity 0 for this note, reverting it to the Utility inactive color.
        /// Set the inactive color to black in the Utility for a true "off".
        /// </summary>
        public void ClearLED(int noteNumber) => SetLED(noteNumber, 0);

        /// <summary>Clears all 64 LEDs.</summary>
        public void ClearAllLEDs()
        {
            for (int n = MidiFighter64InputMap.NOTE_OFFSET; n <= MidiFighter64InputMap.NOTE_MAX; n++)
                ClearLED(n);
        }

        /// <summary>
        /// Sends an animation trigger on Channel 4 (layer index 3).
        /// The animation note is automatically offset by −36 per Appendix 2.
        /// </summary>
        /// <param name="noteNumber">Hardware note (36–99); offset applied internally.</param>
        /// <param name="velocity">Animation index / intensity (0–127).</param>
        public void SetAnimation(int noteNumber, int velocity)
            => SendNoteOn(3, noteNumber - MidiFighter64InputMap.NOTE_OFFSET, velocity);
    }
}
