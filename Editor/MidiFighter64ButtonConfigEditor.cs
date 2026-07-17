using UnityEditor;
using UnityEngine;

namespace MidiFighter64.Editor
{
    /// <summary>
    /// Draws an 8×8 checkbox grid for MidiFighter64ButtonConfig that mirrors the physical MF64.
    /// Row 1 is the top row of the hardware. Checked = Toggle mode, unchecked = Button mode.
    /// Each cell shows the hardware MIDI note number so the mapping is unambiguous.
    /// </summary>
    [CustomEditor(typeof(MidiFighter64ButtonConfig))]
    public class MidiFighter64ButtonConfigEditor : UnityEditor.Editor
    {
        SerializedProperty _defaultMode;
        SerializedProperty _padModes;

        const int GRID = MidiFighter64InputMap.GRID_SIZE; // 8
        const float CELL_WIDTH  = 48f;
        const float CELL_HEIGHT = 38f;
        const float ROW_LABEL_W = 28f;

        void OnEnable()
        {
            _defaultMode = serializedObject.FindProperty("_defaultMode");
            _padModes    = serializedObject.FindProperty("_padModes");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_defaultMode);
            EditorGUILayout.Space(4);

            // Quick-fill buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All Button", GUILayout.Height(22)))
                SetAll(0);
            if (GUILayout.Button("All Toggle", GUILayout.Height(22)))
                SetAll(1);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Pad Mode Grid  (☑ = Toggle  ☐ = Button)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Row 1 = top row of hardware", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            // Column header row
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(ROW_LABEL_W));
            for (int c = 1; c <= GRID; c++)
                GUILayout.Label($"C{c}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(CELL_WIDTH));
            EditorGUILayout.EndHorizontal();

            // 8×8 grid (row 1 = top of hardware)
            for (int row = 1; row <= GRID; row++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"R{row}", EditorStyles.miniLabel, GUILayout.Width(ROW_LABEL_W));

                for (int col = 1; col <= GRID; col++)
                {
                    // Map (row,col) → note → linearIndex via the InputMap so layout stays
                    // in sync with split-half or any future hardware variant
                    int note        = NoteFromRowCol(row, col);
                    var btn         = MidiFighter64InputMap.FromNote(note);
                    int linearIndex = btn.linearIndex;

                    if (linearIndex >= _padModes.arraySize) continue;

                    var element  = _padModes.GetArrayElementAtIndex(linearIndex);
                    bool isToggle = element.enumValueIndex == 1;

                    EditorGUILayout.BeginVertical(GUILayout.Width(CELL_WIDTH));
                    // Centre the checkbox
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    bool next = EditorGUILayout.Toggle(isToggle, GUILayout.Width(16));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    // Note number label beneath the checkbox
                    GUILayout.Label(note.ToString(), EditorStyles.centeredGreyMiniLabel,
                                    GUILayout.Width(CELL_WIDTH));
                    EditorGUILayout.EndVertical();

                    if (next != isToggle)
                        element.enumValueIndex = next ? 1 : 0;
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------

        void SetAll(int enumValue)
        {
            for (int i = 0; i < _padModes.arraySize; i++)
                _padModes.GetArrayElementAtIndex(i).enumValueIndex = enumValue;
        }

        /// <summary>
        /// Inverse of MidiFighter64InputMap.FromNote: converts (row, col) to hardware note number.
        /// Mirrors the split-half formula so the grid label always shows the real note.
        /// </summary>
        static int NoteFromRowCol(int row, int col)
        {
            int physicalRow = GRID - row;               // 0 = bottom of hardware
            int halfCol     = col <= 4 ? col - 1 : col - 5; // 0-based within left or right half
            int offset      = physicalRow * 4 + halfCol;
            return col <= 4
                ? MidiFighter64InputMap.NOTE_OFFSET + offset   // 36–67 left half
                : 68 + offset;                                  // 68–99 right half
        }
    }
}
