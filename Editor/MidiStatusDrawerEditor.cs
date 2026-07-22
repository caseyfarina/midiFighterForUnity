using UnityEditor;
using UnityEngine;

namespace MidiFighter64.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="MidiStatusDrawer"/>, grouping its settings by
    /// what they affect and, critically, by which layout they apply to.
    ///
    /// The layout-specific groups are shown but disabled rather than hidden when they
    /// do not apply. Hiding them makes settings appear to vanish when the layout
    /// changes, which reads as data loss; greying them keeps the drawer's full surface
    /// visible while making it obvious which half is live.
    /// </summary>
    [CustomEditor(typeof(MidiStatusDrawer))]
    public class MidiStatusDrawerEditor : UnityEditor.Editor
    {
        SerializedProperty _layout;
        SerializedProperty _theme, _panelOpacity, _uiOpacity, _strokeWeight;
        SerializedProperty _fontOverride, _themeStyleSheet;
        SerializedProperty _placement, _screenFraction;
        SerializedProperty _showMf64, _showMidiMix;
        SerializedProperty _enableMf64Fisheye, _mf64FisheyeScale;
        SerializedProperty _radialVerticalOffset, _radialMessagePadding;
        SerializedProperty _radialPadScale, _radialRingSpread;
        SerializedProperty _enableFunctionKeys, _logLayoutDiagnostics;

        void OnEnable()
        {
            _layout               = serializedObject.FindProperty("_layout");
            _theme                = serializedObject.FindProperty("_theme");
            _panelOpacity         = serializedObject.FindProperty("_panelOpacity");
            _uiOpacity            = serializedObject.FindProperty("_uiOpacity");
            _strokeWeight         = serializedObject.FindProperty("_strokeWeight");
            _fontOverride         = serializedObject.FindProperty("_fontOverride");
            _themeStyleSheet      = serializedObject.FindProperty("_themeStyleSheet");
            _placement            = serializedObject.FindProperty("_placement");
            _screenFraction       = serializedObject.FindProperty("_screenFraction");
            _showMf64             = serializedObject.FindProperty("_showMf64");
            _showMidiMix          = serializedObject.FindProperty("_showMidiMix");
            _enableMf64Fisheye    = serializedObject.FindProperty("_enableMf64Fisheye");
            _mf64FisheyeScale     = serializedObject.FindProperty("_mf64FisheyeScale");
            _radialVerticalOffset = serializedObject.FindProperty("_radialVerticalOffset");
            _radialMessagePadding = serializedObject.FindProperty("_radialMessagePadding");
            _radialPadScale       = serializedObject.FindProperty("_radialPadScale");
            _radialRingSpread     = serializedObject.FindProperty("_radialRingSpread");
            _enableFunctionKeys   = serializedObject.FindProperty("_enableFunctionKeys");
            _logLayoutDiagnostics = serializedObject.FindProperty("_logLayoutDiagnostics");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var layout = (DrawerLayout)_layout.enumValueIndex;
            bool isRadial = layout == DrawerLayout.Radial1;

            EditorGUILayout.PropertyField(_layout, new GUIContent("Layout"));
            EditorGUILayout.LabelField(" ", "F4 cycles Linear 1 ⇄ Radial 1 at runtime", EditorStyles.miniLabel);
            if (layout == DrawerLayout.Radial2)
                EditorGUILayout.HelpBox(
                    "Radial 2 (sunburst) is not built yet — the drawer falls back to Linear 1.",
                    MessageType.Info);

            Header("Sections");
            EditorGUILayout.PropertyField(_showMf64,    new GUIContent("Show Midi Fighter 64"));
            EditorGUILayout.PropertyField(_showMidiMix, new GUIContent("Show MIDI Mix"));
            if (!_showMf64.boolValue && !_showMidiMix.boolValue)
                EditorGUILayout.HelpBox(
                    "Both sections hidden — only the event readout will render.",
                    MessageType.Info);
            if (isRadial)
                EditorGUILayout.LabelField(" ", "Radial keeps its band radii fixed; hiding a section leaves a gap", EditorStyles.miniLabel);

            Header("Placement & Size");
            EditorGUILayout.PropertyField(_placement,      new GUIContent("Placement"));
            EditorGUILayout.PropertyField(_screenFraction, new GUIContent("Screen Fill"));
            EditorGUILayout.LabelField(" ", "Fraction of the binding axis: height if landscape, width if portrait", EditorStyles.miniLabel);

            Header("Appearance");
            EditorGUILayout.PropertyField(_theme,        new GUIContent("Theme"));
            EditorGUILayout.LabelField(" ", "Pick the theme that opposes the scene behind the drawer  ·  F3 cycles", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_panelOpacity, new GUIContent("Panel Opacity"));
            EditorGUILayout.LabelField(" ", "Backing panels only — widget ink is untouched", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_uiOpacity,    new GUIContent("UI Opacity"));
            EditorGUILayout.LabelField(" ", "Widgets and type  ·  multiplies with the dimming on untouched controls", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_strokeWeight, new GUIContent("Stroke Weight"));
            EditorGUILayout.PropertyField(_fontOverride, new GUIContent("Font Override"));
            if (_fontOverride.objectReferenceValue == null)
                EditorGUILayout.LabelField(" ", "Using bundled CossetteTitre-Regular", EditorStyles.miniLabel);

            // ── Layout-specific ───────────────────────────────────────────
            Header("Linear 1 only");
            using (new EditorGUI.DisabledScope(isRadial))
            {
                EditorGUILayout.PropertyField(_enableMf64Fisheye, new GUIContent("Enable MF64 Fisheye"));
                using (new EditorGUI.DisabledScope(!_enableMf64Fisheye.boolValue))
                {
                    EditorGUILayout.PropertyField(_mf64FisheyeScale, new GUIContent("Fisheye Scale"));
                    EditorGUILayout.LabelField(" ", "Growth weight of the focused row/column  ·  1 = no growth", EditorStyles.miniLabel);
                }
                if (isRadial)
                    EditorGUILayout.LabelField(" ", "Fisheye is flex-grow based and does not apply to radial", EditorStyles.miniLabel);
            }

            Header("Radial only");
            using (new EditorGUI.DisabledScope(!isRadial))
            {
                EditorGUILayout.PropertyField(_radialPadScale,   new GUIContent("Pad Size"));
                EditorGUILayout.PropertyField(_radialRingSpread, new GUIContent("Ring Spread"));
                EditorGUILayout.LabelField(" ", "Ring Spread pulls the grid inward, which also shortens each ring — the two compete for the same arc", EditorStyles.miniLabel);
                if (_radialPadScale.floatValue / Mathf.Max(0.01f, _radialRingSpread.floatValue) > 1.9f)
                    EditorGUILayout.HelpBox(
                        "Pads on the outer ring are probably touching — that ring has the least arc per pad. "
                        + "Lower Pad Size or raise Ring Spread.",
                        MessageType.Info);

                EditorGUILayout.PropertyField(_radialVerticalOffset, new GUIContent("Vertical Offset"));
                EditorGUILayout.LabelField(" ", "Moves the ring stack up / down  ·  ±1 = half a square", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_radialMessagePadding, new GUIContent("Readout Padding"));
                EditorGUILayout.LabelField(" ", "Inset of the event readout from the display's bottom-left corner", EditorStyles.miniLabel);
            }

            Header("Input & Diagnostics");
            EditorGUILayout.PropertyField(_enableFunctionKeys, new GUIContent("Enable Function Keys"));
            EditorGUILayout.LabelField(" ", "F1 show-hide · F2 placement · F3 theme · F4 layout  ·  backtick always works", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_logLayoutDiagnostics, new GUIContent("Log Layout Report"));
            EditorGUILayout.PropertyField(_themeStyleSheet, new GUIContent("Theme Style Sheet"));

            serializedObject.ApplyModifiedProperties();
        }

        // The drawer's fields carry no [Header] attributes, so unlike the router's
        // inspector these headings cannot render twice.
        static void Header(string text)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }
    }
}
