using UnityEngine;
using UnityEditor;
using MidiFighter64.Samples;

[CustomEditor(typeof(MidiToggle))]
public class MidiToggleEditor : Editor
{
    SerializedProperty _buttonRow, _buttonCol;
    SerializedProperty _target, _defaultActive;

    void OnEnable()
    {
        _buttonRow     = serializedObject.FindProperty("buttonRow");
        _buttonCol     = serializedObject.FindProperty("buttonCol");
        _target        = serializedObject.FindProperty("target");
        _defaultActive = serializedObject.FindProperty("defaultActive");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        Section("MIDI Button");
        DrawGridPicker("Toggle Button (green)", _buttonRow, _buttonCol, new Color(0.3f, 0.85f, 0.4f));

        Section("Target");
        EditorGUILayout.PropertyField(_target);
        EditorGUILayout.PropertyField(_defaultActive);

        Section("Test Controls");
        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("Test Toggle"))
            ((MidiToggle)target).Toggle();
        EditorGUI.EndDisabledGroup();
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play mode to test toggle.", MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }

    static void DrawGridPicker(string label, SerializedProperty rowProp, SerializedProperty colProp,
                                Color highlight)
    {
        EditorGUILayout.LabelField(label, EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(22);
        for (int c = 1; c <= 8; c++)
            GUILayout.Label($"C{c}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(28));
        EditorGUILayout.EndHorizontal();

        for (int r = 1; r <= 8; r++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"R{r}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(22));
            for (int c = 1; c <= 8; c++)
            {
                bool selected = rowProp.intValue == r && colProp.intValue == c;
                var  prev     = GUI.backgroundColor;
                GUI.backgroundColor = selected ? highlight : new Color(0.65f, 0.65f, 0.65f);
                if (GUILayout.Button("", GUILayout.Width(28), GUILayout.Height(18)))
                {
                    rowProp.intValue = r;
                    colProp.intValue = c;
                }
                GUI.backgroundColor = prev;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.LabelField($"  → Row {rowProp.intValue}, Col {colProp.intValue}",
                                   EditorStyles.miniLabel);
    }

    static void Section(string title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}
