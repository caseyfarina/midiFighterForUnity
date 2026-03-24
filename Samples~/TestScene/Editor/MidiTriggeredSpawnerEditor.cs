using UnityEngine;
using UnityEditor;
using MidiFighter64.Samples;

[CustomEditor(typeof(MidiTriggeredSpawner))]
public class MidiTriggeredSpawnerEditor : Editor
{
    SerializedProperty _spawnRow, _spawnCol;
    SerializedProperty _clearRow, _clearCol;
    SerializedProperty _prefabs, _folderPath;
    SerializedProperty _spawnVolume, _spawnMode, _alignNormal;
    SerializedProperty _scaleMin, _scaleMax, _rotMode;
    SerializedProperty _poolSize, _expandPool;

    void OnEnable()
    {
        _spawnRow    = serializedObject.FindProperty("spawnButtonRow");
        _spawnCol    = serializedObject.FindProperty("spawnButtonCol");
        _clearRow    = serializedObject.FindProperty("clearButtonRow");
        _clearCol    = serializedObject.FindProperty("clearButtonCol");
        _prefabs     = serializedObject.FindProperty("prefabs");
        _folderPath  = serializedObject.FindProperty("prefabFolderPath");
        _spawnVolume = serializedObject.FindProperty("spawnVolume");
        _spawnMode   = serializedObject.FindProperty("spawnMode");
        _alignNormal = serializedObject.FindProperty("alignToSurfaceNormal");
        _scaleMin    = serializedObject.FindProperty("scaleMin");
        _scaleMax    = serializedObject.FindProperty("scaleMax");
        _rotMode     = serializedObject.FindProperty("rotationMode");
        _poolSize    = serializedObject.FindProperty("poolSize");
        _expandPool  = serializedObject.FindProperty("expandIfExhausted");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ── MIDI Buttons ──────────────────────────────────────────────────
        Section("MIDI Buttons");
        DrawGridPicker("Spawn Button (green)", _spawnRow, _spawnCol, new Color(0.3f, 0.85f, 0.4f));
        EditorGUILayout.Space(6);
        DrawGridPicker("Clear Button (red)", _clearRow, _clearCol, new Color(0.9f, 0.3f, 0.3f));

        // ── Prefabs ───────────────────────────────────────────────────────
        Section("Prefabs");
        DrawFolderPicker();
        EditorGUILayout.PropertyField(_prefabs, true);

        // ── Spawn Volume ──────────────────────────────────────────────────
        Section("Spawn Volume");
        EditorGUILayout.PropertyField(_spawnVolume);
        EditorGUILayout.PropertyField(_spawnMode);
        if (_spawnMode.enumValueIndex == (int)SpawnMode.Surface)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_alignNormal);
            EditorGUI.indentLevel--;
        }

        // ── Transform Randomization ───────────────────────────────────────
        Section("Transform Randomization");
        EditorGUILayout.PropertyField(_scaleMin);
        EditorGUILayout.PropertyField(_scaleMax);
        EditorGUILayout.PropertyField(_rotMode);

        // ── Pool ──────────────────────────────────────────────────────────
        Section("Pool");
        EditorGUILayout.PropertyField(_poolSize);
        EditorGUILayout.PropertyField(_expandPool);

        // ── Test Controls (Play mode only) ────────────────────────────────
        Section("Test Controls");
        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Test Spawn"))
            ((MidiTriggeredSpawner)target).SpawnOne();
        if (GUILayout.Button("Clear All"))
            ((MidiTriggeredSpawner)target).ClearAll();
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play mode to test spawn / clear.", MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Folder picker + prefab loader
    // ─────────────────────────────────────────────────────────────────────

    void DrawFolderPicker()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Folder");
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField(_folderPath.stringValue);
        EditorGUI.EndDisabledGroup();
        if (GUILayout.Button("Browse", GUILayout.Width(58)))
        {
            string chosen = EditorUtility.OpenFolderPanel("Select Prefab Folder", "Assets", "");
            if (!string.IsNullOrEmpty(chosen))
            {
                if (chosen.StartsWith(Application.dataPath))
                    chosen = "Assets" + chosen.Substring(Application.dataPath.Length);
                _folderPath.stringValue = chosen;
            }
        }
        if (GUILayout.Button("Reload", GUILayout.Width(54)))
            ReloadPrefabs();
        EditorGUILayout.EndHorizontal();

        int n = _prefabs.arraySize;
        EditorGUILayout.LabelField($"  {n} prefab{(n == 1 ? "" : "s")} loaded", EditorStyles.miniLabel);
    }

    void ReloadPrefabs()
    {
        string folder = _folderPath.stringValue;
        if (string.IsNullOrEmpty(folder))
        {
            Debug.LogWarning("[MidiTriggeredSpawner] No folder path set.");
            return;
        }
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        _prefabs.arraySize = guids.Length;
        for (int i = 0; i < guids.Length; i++)
        {
            string path   = AssetDatabase.GUIDToAssetPath(guids[i]);
            var    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            _prefabs.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
        }
        serializedObject.ApplyModifiedProperties();
        Debug.Log($"[MidiTriggeredSpawner] Loaded {guids.Length} prefabs from {folder}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 8×8 grid button picker
    // ─────────────────────────────────────────────────────────────────────

    static void DrawGridPicker(string label, SerializedProperty rowProp, SerializedProperty colProp,
                                Color highlight)
    {
        EditorGUILayout.LabelField(label, EditorStyles.miniLabel);

        // Column header row
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

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    static void Section(string title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}
