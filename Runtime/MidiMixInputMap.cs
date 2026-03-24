using System.Collections.Generic;

namespace MidiFighter64
{
    /// <summary>
    /// Button type for the Akai MIDI Mix.
    /// Each channel strip has a Mute button and a Rec Arm button.
    /// </summary>
    public enum MidiMixButton
    {
        Mute,
        /// <summary>
        /// Same physical Mute buttons in Solo mode (SOLO toggle engaged on hardware).
        /// The firmware emits a different note number set when this mode is active.
        /// </summary>
        Solo,
        RecArm,
        /// <summary>
        /// Same physical Rec Arm buttons in shifted mode (base note + 32).
        /// Activated by the Bank Left/Bank Right shift function.
        /// </summary>
        RecArmShifted,
    }

    /// <summary>A knob on the Akai MIDI Mix (3 rows × 8 channels).</summary>
    public struct MixKnob
    {
        public int channel;    // 1–8 (left to right)
        public int row;        // 1–3 (top to bottom)
        public int ccNumber;
    }

    /// <summary>A fader on the Akai MIDI Mix (8 channel faders + master).</summary>
    public struct MixFader
    {
        public int  channel;   // 1–8 for channel strips; 0 for master
        public bool isMaster;
        public int  ccNumber;
    }

    /// <summary>A button on the Akai MIDI Mix.</summary>
    public struct MixButton
    {
        public int           channel;    // 1–8
        public MidiMixButton type;
        public int           noteNumber;
    }

    /// <summary>
    /// Default MIDI mapping for the Akai MIDI Mix.
    ///
    /// All controls operate on MIDI channel 1 out of the box.
    ///
    /// Physical layout per channel strip (left→right, top→bottom):
    ///   Knob Row 1  | Knob Row 2  | Knob Row 3
    ///   [ MUTE ]    [ REC ARM ]
    ///   [  Fader  ]
    ///
    /// Additionally there are BANK LEFT and BANK RIGHT buttons.
    ///
    /// CC numbers were verified against the Akai MIDImix hardware
    /// default firmware and community documentation (AkaiGoBRRR project).
    /// </summary>
    public static class MidiMixInputMap
    {
        public const int CHANNEL_COUNT = 8;
        public const int KNOB_ROWS     = 3;

        // ------------------------------------------------------------------ //
        // CC numbers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// CC numbers for knobs indexed [row, channel] (both 0-based).
        /// Row 0 = top row, channel 0 = leftmost strip.
        /// </summary>
        public static readonly int[,] KnobCC = new int[KNOB_ROWS, CHANNEL_COUNT]
        {
            { 16, 20, 24, 28, 46, 50, 54, 58 }, // Row 1 (top)
            { 17, 21, 25, 29, 47, 51, 55, 59 }, // Row 2 (middle)
            { 18, 22, 26, 30, 48, 52, 56, 60 }, // Row 3 (bottom)
        };

        /// <summary>CC numbers for the 8 channel faders (0-based index).</summary>
        public static readonly int[] FaderCC = { 19, 23, 27, 31, 49, 53, 57, 61 };

        /// <summary>CC number for the master fader.</summary>
        public const int MasterFaderCC = 127;

        // ------------------------------------------------------------------ //
        // Note numbers
        // ------------------------------------------------------------------ //

        /// <summary>Note numbers for the 8 Mute buttons in normal mode (0-based index).</summary>
        public static readonly int[] MuteNotes = { 1, 4, 7, 10, 13, 16, 19, 22 };

        /// <summary>
        /// Note numbers emitted by the 8 Mute buttons when the hardware SOLO toggle is engaged.
        /// These are the same physical buttons as MuteNotes but the firmware sends a different
        /// note set (base + 1) while Solo mode is active.
        /// </summary>
        public static readonly int[] SoloNotes = { 2, 5, 8, 11, 14, 17, 20, 23 };

        /// <summary>Note numbers for the 8 Rec Arm buttons in normal mode (0-based index).</summary>
        public static readonly int[] RecArmNotes = { 3, 6, 9, 12, 15, 18, 21, 24 };

        /// <summary>
        /// Shifted Rec Arm note numbers (base + 32), active when the Bank shift function is used.
        /// </summary>
        public static readonly int[] RecArmShiftedNotes = { 35, 38, 41, 44, 47, 50, 53, 56 };

        /// <summary>Note number for the Bank Left button.</summary>
        public const int BankLeftNote  = 25;

        /// <summary>Note number for the Bank Right button.</summary>
        public const int BankRightNote = 26;

        // ------------------------------------------------------------------ //
        // Reverse-lookup tables (built once at class initialisation)
        // ------------------------------------------------------------------ //

        static readonly Dictionary<int, MixKnob>   _knobByCC   = new();
        static readonly Dictionary<int, MixFader>  _faderByCC  = new();
        static readonly Dictionary<int, MixButton> _buttonByNote = new();

        static MidiMixInputMap()
        {
            for (int row = 0; row < KNOB_ROWS; row++)
            for (int ch  = 0; ch  < CHANNEL_COUNT; ch++)
            {
                int cc = KnobCC[row, ch];
                _knobByCC[cc] = new MixKnob { channel = ch + 1, row = row + 1, ccNumber = cc };
            }

            for (int ch = 0; ch < CHANNEL_COUNT; ch++)
            {
                int cc = FaderCC[ch];
                _faderByCC[cc] = new MixFader { channel = ch + 1, isMaster = false, ccNumber = cc };
            }
            _faderByCC[MasterFaderCC] = new MixFader { channel = 0, isMaster = true, ccNumber = MasterFaderCC };

            for (int ch = 0; ch < CHANNEL_COUNT; ch++)
            {
                int muteNote          = MuteNotes[ch];
                int soloNote          = SoloNotes[ch];
                int recArmNote        = RecArmNotes[ch];
                int recArmShiftedNote = RecArmShiftedNotes[ch];
                _buttonByNote[muteNote]          = new MixButton { channel = ch + 1, type = MidiMixButton.Mute,          noteNumber = muteNote };
                _buttonByNote[soloNote]          = new MixButton { channel = ch + 1, type = MidiMixButton.Solo,          noteNumber = soloNote };
                _buttonByNote[recArmNote]        = new MixButton { channel = ch + 1, type = MidiMixButton.RecArm,        noteNumber = recArmNote };
                _buttonByNote[recArmShiftedNote] = new MixButton { channel = ch + 1, type = MidiMixButton.RecArmShifted, noteNumber = recArmShiftedNote };
            }
        }

        // ------------------------------------------------------------------ //
        // Public lookup API
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns true and fills <paramref name="knob"/> if the CC number
        /// belongs to a MIDI Mix knob.
        /// </summary>
        public static bool TryGetKnob(int ccNumber, out MixKnob knob)
            => _knobByCC.TryGetValue(ccNumber, out knob);

        /// <summary>
        /// Returns true and fills <paramref name="fader"/> if the CC number
        /// belongs to a MIDI Mix fader (channel or master).
        /// </summary>
        public static bool TryGetFader(int ccNumber, out MixFader fader)
            => _faderByCC.TryGetValue(ccNumber, out fader);

        /// <summary>
        /// Returns true and fills <paramref name="button"/> if the note number
        /// belongs to a MIDI Mix button (Mute or Rec Arm).
        /// </summary>
        public static bool TryGetButton(int noteNumber, out MixButton button)
            => _buttonByNote.TryGetValue(noteNumber, out button);

        public static bool IsBankLeft(int noteNumber)  => noteNumber == BankLeftNote;
        public static bool IsBankRight(int noteNumber) => noteNumber == BankRightNote;
    }
}
