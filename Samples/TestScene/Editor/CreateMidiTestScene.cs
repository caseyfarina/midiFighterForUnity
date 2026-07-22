using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MidiFighter64.Samples.Editor
{
    /// <summary>
    /// Menu: <c>Tools → MidiFighter64 → Create Test Scene</c>.
    /// Builds a fresh scene containing a single GameObject with
    /// <see cref="MidiFighterTestScene"/> + <see cref="MidiDebugUI"/> attached,
    /// then saves it next to the currently open scene.
    /// </summary>
    public static class CreateMidiTestScene
    {
        const string DEFAULT_PATH = "Assets/MidiControllersTestScene.unity";

        const string ControllerPrefabPath =
            "Packages/com.caseyfarina.midifighter64/Runtime/MIDI Controller.prefab";

        [MenuItem("Tools/MidiFighter64/Create Test Scene", priority = 100)]
        public static void Create()
        {
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("MidiTestScene");
            go.AddComponent<MidiFighterTestScene>();
            go.AddComponent<MidiDebugUI>();

            // The whole rig comes from one prefab now. InstantiatePrefab (not
            // Instantiate) keeps the instance linked to the asset, so later package
            // updates to the prefab still reach scenes generated today.
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(ControllerPrefabPath);
            if (rig != null)
                PrefabUtility.InstantiatePrefab(rig, scene);
            else
                Debug.LogWarning($"[MidiFighter64] Could not find {ControllerPrefabPath}. " +
                                 "The generated scene will have no MIDI rig — drag the prefab in manually.");

            SceneManager.SetActiveScene(scene);

            string path = EditorUtility.SaveFilePanelInProject(
                "Save MIDI Controllers Test Scene",
                "MidiControllersTestScene",
                "unity",
                "Choose where to save the generated test scene.",
                "Assets");

            if (string.IsNullOrEmpty(path)) path = DEFAULT_PATH;

            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[MidiFighter64] Test scene created at {path}. Press Play to test both controllers.");
        }
    }
}
