using UnityEditor;
using UnityEngine;

namespace MidiFighter64.Editor
{
    /// <summary>
    /// Shared IMGUI helper that draws the MF64's 8×8 pad grid with a mode
    /// checkbox, LED-colour dropdown, and hardware note number per cell.
    /// Used by both <see cref="MidiFighter64ButtonConfigEditor"/> (SO asset)
    /// and <see cref="MidiFighterButtonRouterEditor"/> (inline arrays on a MonoBehaviour).
    /// </summary>
    public static class MidiFighter64PadGridGUI
    {
        const int   GRID        = MidiFighter64InputMap.GRID_SIZE;
        const float CELL_WIDTH  = 56f;
        const float ROW_LABEL_W = 28f;

        /// <summary>
        /// Draws the grid. Both properties must be arrays of length 64:
        ///  - <paramref name="padModes"/> holds <see cref="MidiFighterButtonMode"/> per cell.
        ///  - <paramref name="padColors"/> holds <see cref="MidiFighterLEDColor"/> per cell.
        /// </summary>
        public static void Draw(SerializedProperty padModes, SerializedProperty padColors)
        {
            // Column header
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(ROW_LABEL_W));
            for (int c = 1; c <= GRID; c++)
                GUILayout.Label($"C{c}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(CELL_WIDTH));
            EditorGUILayout.EndHorizontal();

            for (int row = 1; row <= GRID; row++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"R{row}", EditorStyles.miniLabel, GUILayout.Width(ROW_LABEL_W));

                for (int col = 1; col <= GRID; col++)
                {
                    int note        = MidiFighter64InputMap.ToNote(row, col);
                    var btn         = MidiFighter64InputMap.FromNote(note);
                    int linearIndex = btn.linearIndex;

                    if (linearIndex >= padModes.arraySize) continue;

                    var modeElement  = padModes.GetArrayElementAtIndex(linearIndex);
                    var colorElement = padColors.GetArrayElementAtIndex(linearIndex);
                    bool isToggle    = modeElement.enumValueIndex == 1;

                    EditorGUILayout.BeginVertical(GUILayout.Width(CELL_WIDTH));

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    bool nextIsToggle = EditorGUILayout.Toggle(isToggle, GUILayout.Width(16));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.PropertyField(colorElement, GUIContent.none, GUILayout.Width(CELL_WIDTH));

                    GUILayout.Label(note.ToString(), EditorStyles.centeredGreyMiniLabel,
                                    GUILayout.Width(CELL_WIDTH));

                    EditorGUILayout.EndVertical();

                    if (nextIsToggle != isToggle)
                        modeElement.enumValueIndex = nextIsToggle ? 1 : 0;
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
        }

        public static void SetAllModes(SerializedProperty padModes, MidiFighterButtonMode value)
        {
            for (int i = 0; i < padModes.arraySize; i++)
                padModes.GetArrayElementAtIndex(i).enumValueIndex = (int)value;
        }

        public static void SetAllColors(SerializedProperty padColors, MidiFighterLEDColor value)
        {
            for (int i = 0; i < padColors.arraySize; i++)
                padColors.GetArrayElementAtIndex(i).enumValueIndex = (int)value;
        }
    }
}
