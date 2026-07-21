using UnityEditor;
using UnityEngine;

namespace MidiFighter64.Editor
{
    /// <summary>
    /// Inspector for MidiFighter64ButtonConfig. Draws the shared 8×8 pad grid
    /// (mode checkbox + LED color dropdown + note number per cell) via
    /// <see cref="MidiFighter64PadGridGUI"/>.
    /// </summary>
    [CustomEditor(typeof(MidiFighter64ButtonConfig))]
    public class MidiFighter64ButtonConfigEditor : UnityEditor.Editor
    {
        SerializedProperty _defaultMode;
        SerializedProperty _padModes;
        SerializedProperty _padColors;

        void OnEnable()
        {
            _defaultMode = serializedObject.FindProperty("_defaultMode");
            _padModes    = serializedObject.FindProperty("_padModes");
            _padColors   = serializedObject.FindProperty("_padColors");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_defaultMode);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All Button", GUILayout.Height(22)))
                MidiFighter64PadGridGUI.SetAllModes(_padModes, MidiFighterButtonMode.Button);
            if (GUILayout.Button("All Toggle", GUILayout.Height(22)))
                MidiFighter64PadGridGUI.SetAllModes(_padModes, MidiFighterButtonMode.Toggle);
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("All Colors → Off (use default)", GUILayout.Height(22)))
                MidiFighter64PadGridGUI.SetAllColors(_padColors, MidiFighterLEDColor.Off);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Pad Grid  (Mode ☑ = Toggle  ☐ = Button   ·   Color = active LED)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Row 1 = top row of hardware. Color = Off means use router default.", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            MidiFighter64PadGridGUI.Draw(_padModes, _padColors);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
