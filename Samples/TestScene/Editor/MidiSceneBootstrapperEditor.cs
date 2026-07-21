using MidiFighter64;
using MidiFighter64.Editor;
using UnityEditor;
using UnityEngine;

namespace MidiFighter64.Samples.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="MidiSceneBootstrapper"/>. Reuses the
    /// shared <see cref="MidiFighter64PadGridGUI"/> for the inline pad grid.
    /// </summary>
    [CustomEditor(typeof(MidiSceneBootstrapper))]
    public class MidiSceneBootstrapperEditor : UnityEditor.Editor
    {
        SerializedProperty _mf64ButtonConfig;
        SerializedProperty _inlineDefaultMode;
        SerializedProperty _inlinePadModes;
        SerializedProperty _inlinePadColors;
        SerializedProperty _toggleOnColor;
        SerializedProperty _toggleOffColor;
        SerializedProperty _buttonDownColor;
        SerializedProperty _latchMute;
        SerializedProperty _latchRecArm;
        SerializedProperty _spawnStatusDrawer;
        SerializedProperty _drawerPlacement;
        SerializedProperty _drawerScreenFraction;
        SerializedProperty _showMf64;
        SerializedProperty _showMidiMix;
        SerializedProperty _enableMf64Fisheye;
        SerializedProperty _drawerFont;
        SerializedProperty _logDrawerLayout;

        void OnEnable()
        {
            _mf64ButtonConfig  = serializedObject.FindProperty("_mf64ButtonConfig");
            _inlineDefaultMode = serializedObject.FindProperty("_inlineDefaultMode");
            _inlinePadModes    = serializedObject.FindProperty("_inlinePadModes");
            _inlinePadColors   = serializedObject.FindProperty("_inlinePadColors");
            _toggleOnColor     = serializedObject.FindProperty("_toggleOnColor");
            _toggleOffColor    = serializedObject.FindProperty("_toggleOffColor");
            _buttonDownColor   = serializedObject.FindProperty("_buttonDownColor");
            _latchMute         = serializedObject.FindProperty("_latchMute");
            _latchRecArm       = serializedObject.FindProperty("_latchRecArm");
            _spawnStatusDrawer = serializedObject.FindProperty("_spawnStatusDrawer");
            _drawerPlacement   = serializedObject.FindProperty("_drawerPlacement");
            _drawerScreenFraction = serializedObject.FindProperty("_drawerScreenFraction");
            _showMf64          = serializedObject.FindProperty("_showMf64");
            _showMidiMix       = serializedObject.FindProperty("_showMidiMix");
            _enableMf64Fisheye = serializedObject.FindProperty("_enableMf64Fisheye");
            _drawerFont        = serializedObject.FindProperty("_drawerFont");
            _logDrawerLayout   = serializedObject.FindProperty("_logDrawerLayout");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Section titles come from the [Header] attributes on the component's
            // fields — PropertyField draws them. Don't add matching LabelFields
            // here or every heading renders twice.
            EditorGUILayout.PropertyField(_mf64ButtonConfig, new GUIContent("Pad Config Asset"));

            bool usingAsset = _mf64ButtonConfig.objectReferenceValue != null;
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
                EditorGUILayout.LabelField("Row 1 = top row of hardware. Color = Off means use router default.", EditorStyles.miniLabel);
                EditorGUILayout.Space(2);

                MidiFighter64PadGridGUI.Draw(_inlinePadModes, _inlinePadColors);
            }

            EditorGUILayout.PropertyField(_toggleOnColor);
            EditorGUILayout.PropertyField(_toggleOffColor);
            EditorGUILayout.PropertyField(_buttonDownColor);

            EditorGUILayout.PropertyField(_latchMute,   new GUIContent("Latch Mute"));
            EditorGUILayout.PropertyField(_latchRecArm, new GUIContent("Latch Rec-Arm"));

            EditorGUILayout.PropertyField(_spawnStatusDrawer, new GUIContent("Spawn Status Drawer"));
            using (new EditorGUI.DisabledScope(!_spawnStatusDrawer.boolValue))
            {
                EditorGUILayout.PropertyField(_drawerPlacement, new GUIContent("Placement"));
                EditorGUILayout.PropertyField(_drawerScreenFraction, new GUIContent("Screen Fill"));
                EditorGUILayout.LabelField(" ", "Fraction of the binding axis: height if landscape, width if portrait", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_showMf64,    new GUIContent("Show Midi Fighter 64"));
                EditorGUILayout.PropertyField(_showMidiMix, new GUIContent("Show MIDI Mix"));

                if (!_showMf64.boolValue && !_showMidiMix.boolValue)
                    EditorGUILayout.HelpBox(
                        "Both sections hidden — the drawer will show only the message strip. " +
                        "Untick Spawn Status Drawer to remove it entirely.",
                        MessageType.Info);

                // Nothing to magnify when the pad grid isn't drawn.
                using (new EditorGUI.DisabledScope(!_showMf64.boolValue))
                    EditorGUILayout.PropertyField(_enableMf64Fisheye, new GUIContent("Enable MF64 Fisheye"));

                EditorGUILayout.PropertyField(_drawerFont, new GUIContent("Drawer Font"));
                if (_drawerFont.objectReferenceValue == null)
                    EditorGUILayout.LabelField(" ", "Using bundled CossetteTitre-Regular", EditorStyles.miniLabel);

                EditorGUILayout.PropertyField(_logDrawerLayout, new GUIContent("Log Layout Report"));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
