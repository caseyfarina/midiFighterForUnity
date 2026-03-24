using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using MidiFighter64.Samples;

[CustomEditor(typeof(MusicMode))]
public class MusicModeEditor : Editor
{
    SerializedProperty _toggleKey;
    SerializedProperty _videoClip, _videoFilePath, _useStreamingAssets;
    SerializedProperty _mixerGroup;
    SerializedProperty _buttonGroups, _rootFolder;

    void OnEnable()
    {
        _toggleKey          = serializedObject.FindProperty("toggleKey");
        _videoClip          = serializedObject.FindProperty("videoClip");
        _videoFilePath      = serializedObject.FindProperty("videoFilePath");
        _useStreamingAssets = serializedObject.FindProperty("useStreamingAssets");
        _mixerGroup         = serializedObject.FindProperty("mixerGroup");
        _buttonGroups       = serializedObject.FindProperty("buttonGroups");
        _rootFolder         = serializedObject.FindProperty("sampleRootFolder");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        Section("Toggle");
        EditorGUILayout.PropertyField(_toggleKey);

        Section("Video");
        EditorGUILayout.PropertyField(_videoClip,
            new GUIContent("Video Clip", "Drag a VideoClip asset here (takes priority over file path)"));
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("— or file path —", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.PropertyField(_videoFilePath, new GUIContent("File Path"));
        EditorGUILayout.PropertyField(_useStreamingAssets,
            new GUIContent("Relative to StreamingAssets"));

        Section("Audio");
        EditorGUILayout.PropertyField(_mixerGroup,
            new GUIContent("Mixer Group", "Route sample playback through this AudioMixerGroup for effects"));

        Section("Sample Subfolders");
        DrawFolderPicker();
        DrawSubfolderGrid();

        Section("Test Controls");
        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button(MusicMode.IsActive ? "Exit Music Mode" : "Enter Music Mode"))
            ((MusicMode)target).Toggle();
        EditorGUI.EndDisabledGroup();
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play mode to test. Press S in-game to toggle.", MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }

    // ── Folder picker ─────────────────────────────────────────────────────────

    void DrawFolderPicker()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Root Folder");
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField(_rootFolder.stringValue);
        EditorGUI.EndDisabledGroup();
        if (GUILayout.Button("Browse", GUILayout.Width(58)))
        {
            string chosen = EditorUtility.OpenFolderPanel("Select Samples Root Folder", "Assets", "");
            if (!string.IsNullOrEmpty(chosen))
            {
                if (chosen.StartsWith(Application.dataPath))
                    chosen = "Assets" + chosen.Substring(Application.dataPath.Length);
                _rootFolder.stringValue = chosen;
            }
        }
        if (GUILayout.Button("Reload", GUILayout.Width(54)))
            ReloadSubfolders();
        EditorGUILayout.EndHorizontal();

        // Summary
        int assigned = 0;
        for (int i = 0; i < _buttonGroups.arraySize; i++)
        {
            var clips = _buttonGroups.GetArrayElementAtIndex(i).FindPropertyRelative("clips");
            if (clips != null && clips.arraySize > 0) assigned++;
        }
        EditorGUILayout.LabelField($"  {assigned} / 64 buttons have clips assigned", EditorStyles.miniLabel);
    }

    void ReloadSubfolders()
    {
        string root = _rootFolder.stringValue;
        if (string.IsNullOrEmpty(root)) { Debug.LogWarning("[MusicMode] No root folder set."); return; }

        // Find immediate subdirectories, sorted alphabetically
        string absRoot = Path.Combine(Application.dataPath, root.Substring("Assets/".Length));
        if (!Directory.Exists(absRoot)) { Debug.LogWarning($"[MusicMode] Folder not found: {root}"); return; }

        var subfolders = Directory.GetDirectories(absRoot)
                                  .Select(p => "Assets" + p.Substring(Application.dataPath.Length)
                                                            .Replace('\\', '/'))
                                  .OrderBy(p => p)
                                  .ToArray();

        // Ensure buttonGroups is exactly 64 entries
        _buttonGroups.arraySize = 64;

        for (int i = 0; i < 64; i++)
        {
            var entry      = _buttonGroups.GetArrayElementAtIndex(i);
            var nameProp   = entry.FindPropertyRelative("folderName");
            var clipsProp  = entry.FindPropertyRelative("clips");

            if (i < subfolders.Length)
            {
                string folder     = subfolders[i];
                string folderName = Path.GetFileName(folder);
                nameProp.stringValue = folderName;

                var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
                clipsProp.arraySize = guids.Length;
                for (int j = 0; j < guids.Length; j++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[j]);
                    clipsProp.GetArrayElementAtIndex(j).objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            }
            else
            {
                nameProp.stringValue = "";
                clipsProp.arraySize  = 0;
            }
        }

        serializedObject.ApplyModifiedProperties();
        Debug.Log($"[MusicMode] Loaded {Mathf.Min(subfolders.Length, 64)} subfolders from {root}");
    }

    // ── Subfolder grid ────────────────────────────────────────────────────────
    // Green  = subfolder assigned with clips
    // Yellow = subfolder assigned but empty (no audio files found)
    // Dark   = unassigned

    void DrawSubfolderGrid()
    {
        if (_buttonGroups.arraySize == 0) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("green = has clips  |  yellow = folder assigned, no clips  |  dark = empty",
                                   EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(22);
        for (int c = 1; c <= 8; c++)
            GUILayout.Label($"C{c}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(28));
        EditorGUILayout.EndHorizontal();

        for (int r = 0; r < 8; r++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"R{r + 1}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(22));
            for (int c = 0; c < 8; c++)
            {
                int idx = r * 8 + c;
                Color color = new Color(0.4f, 0.4f, 0.4f); // dark = empty

                if (idx < _buttonGroups.arraySize)
                {
                    var entry    = _buttonGroups.GetArrayElementAtIndex(idx);
                    var nameProp = entry.FindPropertyRelative("folderName");
                    var clips    = entry.FindPropertyRelative("clips");
                    bool hasName = !string.IsNullOrEmpty(nameProp.stringValue);
                    bool hasClips = clips != null && clips.arraySize > 0;

                    if (hasClips)        color = new Color(0.3f, 0.85f, 0.4f); // green
                    else if (hasName)    color = new Color(0.85f, 0.75f, 0.2f); // yellow
                }

                var prev = GUI.backgroundColor;
                GUI.backgroundColor = color;

                // Tooltip shows folder name + clip count
                string tooltip = "";
                if (idx < _buttonGroups.arraySize)
                {
                    var entry    = _buttonGroups.GetArrayElementAtIndex(idx);
                    var nameProp = entry.FindPropertyRelative("folderName");
                    var clips    = entry.FindPropertyRelative("clips");
                    if (!string.IsNullOrEmpty(nameProp.stringValue))
                        tooltip = $"{nameProp.stringValue} ({clips?.arraySize ?? 0} clips)";
                }

                GUILayout.Button(new GUIContent("", tooltip), GUILayout.Width(28), GUILayout.Height(18));
                GUI.backgroundColor = prev;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    static void Section(string title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}
