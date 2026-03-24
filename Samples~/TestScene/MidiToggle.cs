using UnityEngine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Toggles a target GameObject on/off when a configured MF64 button is pressed.
    /// Reflects toggle state to the MF64 LED for that button (Windows/Editor only).
    /// Set defaultActive to control the state on Start.
    /// </summary>
    public class MidiToggle : MonoBehaviour
    {
        [Header("MIDI Button (Row 1-8, Col 1-8)")]
        public int buttonRow = 1;
        public int buttonCol = 1;

        [Header("Target")]
        public GameObject target;
        public bool defaultActive = true;

        bool _state;

        void Start()
        {
            _state = defaultActive;
            Apply();
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += OnButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= OnButton;

        void OnButton(GridButton btn, bool isNoteOn)
        {
            if (!isNoteOn) return;
            if (btn.row == buttonRow && btn.col == buttonCol)
                Toggle();
        }

        public void Toggle()
        {
            _state = !_state;
            Apply();
        }

        public void SetState(bool active)
        {
            _state = active;
            Apply();
        }

        void Apply()
        {
            if (target != null)
                target.SetActive(_state);

            UpdateLED();
        }

        void UpdateLED()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var output = MidiFighterOutput.Instance;
            if (output == null) return;
            int note = NoteFromRowCol(buttonRow, buttonCol);
            if (_state)
                output.SetLED(note, 127);
            else
                output.ClearLED(note);
#endif
        }

        // Inverse of MidiFighter64InputMap.FromNote — derives note number from row/col.
        static int NoteFromRowCol(int row, int col)
        {
            int physicalRow = MidiFighter64InputMap.GRID_SIZE - row; // 0 = bottom
            int halfCol     = col <= 4 ? col - 1 : col - 5;         // 0–3 within half
            int offset      = physicalRow * 4 + halfCol;
            return col <= 4
                ? MidiFighter64InputMap.NOTE_OFFSET + offset          // 36–67
                : 68 + offset;                                        // 68–99
        }
    }
}
