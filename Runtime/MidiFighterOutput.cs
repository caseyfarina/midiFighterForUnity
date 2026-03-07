using UnityEngine;
using System;
using System.Runtime.InteropServices;

namespace MidiFighter64
{
    /// <summary>
    /// Sends MIDI Note On messages back to the Midi Fighter 64 to control its
    /// button LEDs. The MF64 responds to Note On messages on channels 1–4,
    /// each channel corresponding to a different LED colour in the firmware.
    ///
    /// On Windows the output is opened via the winmm.dll MIDI API. macOS and
    /// Linux are not yet implemented; a warning will be logged on those platforms.
    /// </summary>
    public class MidiFighterOutput : MonoBehaviour
    {
        public static MidiFighterOutput Instance { get; private set; }

        [Header("LED Settings")]
        [Tooltip("MIDI channel index (0–3) used for LED messages.\n" +
                 "Channel 0 = Blue, 1 = Purple, 2 = Red, 3 = White (default MF64 firmware).")]
        [Range(0, 3)]
        public int ledChannelIndex = 0;

        // ------------------------------------------------------------------ //
        // Windows MIDI output (winmm.dll)
        // ------------------------------------------------------------------ //

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("winmm.dll")] static extern int midiOutGetNumDevs();

        [DllImport("winmm.dll")]
        static extern int midiOutOpen(out IntPtr handle, int deviceId,
                                      IntPtr callback, IntPtr instance, int flags);

        [DllImport("winmm.dll")] static extern int midiOutShortMsg(IntPtr handle, uint message);
        [DllImport("winmm.dll")] static extern int midiOutClose(IntPtr handle);

        [DllImport("winmm.dll", CharSet = CharSet.Ansi)]
        static extern int midiOutGetDevCaps(int deviceId, ref MIDIOUTCAPS caps, int size);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct MIDIOUTCAPS
        {
            public ushort wMid, wPid;
            public uint   vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public ushort wTechnology, wVoices, wNotes, wChannelMask;
            public uint   dwSupport;
        }

        IntPtr _outHandle = IntPtr.Zero;
#endif

        // ------------------------------------------------------------------ //

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()  => OpenOutput();

        void OnDestroy()
        {
            CloseOutput();
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ //

        void OpenOutput()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            int count = midiOutGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                var caps = new MIDIOUTCAPS();
                midiOutGetDevCaps(i, ref caps, Marshal.SizeOf(caps));
                if (caps.szPname != null &&
                    caps.szPname.IndexOf("Fighter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (midiOutOpen(out _outHandle, i, IntPtr.Zero, IntPtr.Zero, 0) == 0)
                    {
                        Debug.Log($"[MidiFighterOutput] Opened MIDI output: {caps.szPname}");
                        return;
                    }
                }
            }
            // Fall back to first available device
            if (count > 0 && midiOutOpen(out _outHandle, 0, IntPtr.Zero, IntPtr.Zero, 0) == 0)
                Debug.Log("[MidiFighterOutput] Midi Fighter not found – using first available MIDI output.");
            else
                Debug.LogWarning("[MidiFighterOutput] No MIDI output device found. LED control will not work.");
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            Debug.LogWarning("[MidiFighterOutput] macOS MIDI output not yet implemented.");
#else
            Debug.LogWarning("[MidiFighterOutput] MIDI output not implemented on this platform.");
#endif
        }

        void CloseOutput()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_outHandle != IntPtr.Zero)
            {
                midiOutClose(_outHandle);
                _outHandle = IntPtr.Zero;
            }
#endif
        }

        // ------------------------------------------------------------------ //
        // Public API
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Sets the LED for a single button on the Midi Fighter 64.
        /// </summary>
        /// <param name="noteNumber">MIDI note number (36–99).</param>
        /// <param name="velocity">Brightness / colour index. 0 = off, 1–127 = on.</param>
        public void SetLED(int noteNumber, int velocity)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_outHandle == IntPtr.Zero) return;
            uint msg = (uint)(0x90 | (ledChannelIndex & 0x0F))
                     | ((uint)(noteNumber & 0x7F) << 8)
                     | ((uint)(velocity  & 0x7F) << 16);
            midiOutShortMsg(_outHandle, msg);
#endif
        }

        /// <summary>Turns off a single LED.</summary>
        public void ClearLED(int noteNumber) => SetLED(noteNumber, 0);

        /// <summary>Turns off every LED on the 8×8 grid.</summary>
        public void ClearAllLEDs()
        {
            for (int n = MidiFighter64InputMap.NOTE_OFFSET; n <= MidiFighter64InputMap.NOTE_MAX; n++)
                ClearLED(n);
        }
    }
}
