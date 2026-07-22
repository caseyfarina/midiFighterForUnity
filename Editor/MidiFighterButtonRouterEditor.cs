using UnityEditor;
using UnityEngine;

namespace MidiFighter64.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="MidiFighterButtonRouter"/>. Draws the inline
    /// 8×8 pad grid with the shared <see cref="MidiFighter64PadGridGUI"/> — without
    /// it the two 64-element arrays render as unusable foldouts.
    ///
    /// The grid used to live on MidiSceneBootstrapper, which configured this router
    /// from the outside. It sits here now because this is the component it actually
    /// configures, which is also what lets the whole rig ship as one prefab.
    /// </summary>
    [CustomEditor(typeof(MidiFighterButtonRouter))]
    public class MidiFighterButtonRouterEditor : UnityEditor.Editor
    {
        SerializedProperty _config;
        SerializedProperty _inlineDefaultMode;
        SerializedProperty _inlinePadModes;
        SerializedProperty _inlinePadColors;
        SerializedProperty _holdRepeatInterval;
        SerializedProperty _driveToggleLEDs;
        SerializedProperty _toggleOnColor;
        SerializedProperty _toggleOffColor;
        SerializedProperty _driveButtonLEDs;
        SerializedProperty _buttonDownColor;

        void OnEnable()
        {
            _config             = serializedObject.FindProperty("_config");
            _inlineDefaultMode  = serializedObject.FindProperty("_inlineDefaultMode");
            _inlinePadModes     = serializedObject.FindProperty("_inlinePadModes");
            _inlinePadColors    = serializedObject.FindProperty("_inlinePadColors");
            _holdRepeatInterval = serializedObject.FindProperty("_holdRepeatInterval");
            _driveToggleLEDs    = serializedObject.FindProperty("_driveToggleLEDs");
            _toggleOnColor      = serializedObject.FindProperty("_toggleOnColor");
            _toggleOffColor     = serializedObject.FindProperty("_toggleOffColor");
            _driveButtonLEDs    = serializedObject.FindProperty("_driveButtonLEDs");
            _buttonDownColor    = serializedObject.FindProperty("_buttonDownColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_config, new GUIContent("Pad Config Asset"));

            bool usingAsset = _config.objectReferenceValue != null;
            using (new EditorGUI.DisabledScope(usingAsset))
            {
                if (usingAsset)
                    EditorGUILayout.HelpBox(
                        "Asset assigned — inline grid is ignored. Clear the Pad Config Asset slot to use the inline grid below.",
                        MessageType.Info);

                EditorGUILayout.PropertyField(_inlineDefaultMode, new GUIContent("Inline Default Mode"));
                EditorGUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("All Button", GUILayout.Height(22)))
                    MidiFighter64PadGridGUI.SetAllModes(_inlinePadModes, MidiFighterButtonMode.Button);
                if (GUILayout.Button("All Toggle", GUILayout.Height(22)))
                    MidiFighter64PadGridGUI.SetAllModes(_inlinePadModes, MidiFighterButtonMode.Toggle);
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("All Colors → Off (use default)", GUILayout.Height(22)))
                    MidiFighter64PadGridGUI.SetAllColors(_inlinePadColors, MidiFighterLEDColor.Off);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Inline Pad Grid  (Mode ☑ = Toggle  ☐ = Button  ·  Color = active LED)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Row 1 = top row of hardware. Color = Off means use the router default below.", EditorStyles.miniLabel);
                EditorGUILayout.Space(2);

                MidiFighter64PadGridGUI.Draw(_inlinePadModes, _inlinePadColors);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(_holdRepeatInterval, new GUIContent("Hold Repeat Interval"));

            // No LabelField for "LED Feedback" — _driveToggleLEDs carries a [Header]
            // attribute and PropertyField draws it, so adding one here renders the
            // heading twice.
            EditorGUILayout.PropertyField(_driveToggleLEDs, new GUIContent("Drive Toggle LEDs"));
            using (new EditorGUI.DisabledScope(!_driveToggleLEDs.boolValue))
            {
                EditorGUILayout.PropertyField(_toggleOnColor,  new GUIContent("Toggle On Color"));
                EditorGUILayout.PropertyField(_toggleOffColor, new GUIContent("Toggle Off Color"));
            }

            EditorGUILayout.PropertyField(_driveButtonLEDs, new GUIContent("Drive Button LEDs"));
            using (new EditorGUI.DisabledScope(!_driveButtonLEDs.boolValue))
                EditorGUILayout.PropertyField(_buttonDownColor, new GUIContent("Button Down Color"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
