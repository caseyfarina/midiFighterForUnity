namespace MidiFighter64
{
    public struct GridButton
    {
        public int row;         // 1-8, 1 = top
        public int col;         // 1-8, 1 = left
        public int linearIndex; // 0-63
        public int noteNumber;

        public bool IsValid => noteNumber >= MidiFighter64InputMap.NOTE_OFFSET
                            && noteNumber < MidiFighter64InputMap.NOTE_OFFSET
                                          + MidiFighter64InputMap.GRID_SIZE
                                          * MidiFighter64InputMap.GRID_SIZE;

        public override string ToString()
            => $"Grid[R{row},C{col}] note={noteNumber}";
    }

    /// <summary>
    /// Converts raw MIDI note numbers from the Midi Fighter 64 to logical grid
    /// coordinates. The MF64 sends notes 36-99 for its 8x8 grid, with note 36
    /// at the bottom-left. This class inverts the Y axis so row 1 = top.
    /// </summary>
    public static class MidiFighter64InputMap
    {
        public const int NOTE_OFFSET = 36;
        public const int GRID_SIZE   = 8;
        public const int NOTE_MAX    = NOTE_OFFSET + GRID_SIZE * GRID_SIZE - 1; // 99

        public static GridButton FromNote(int noteNumber)
        {
            int index       = noteNumber - NOTE_OFFSET;
            int physicalRow = index / GRID_SIZE;           // 0 = bottom
            int col         = (index % GRID_SIZE) + 1;
            int row         = GRID_SIZE - physicalRow;     // invert: 1 = top

            return new GridButton
            {
                row         = row,
                col         = col,
                linearIndex = index,
                noteNumber  = noteNumber
            };
        }

        public static bool IsInRange(int noteNumber)
            => noteNumber >= NOTE_OFFSET && noteNumber <= NOTE_MAX;
    }
}
