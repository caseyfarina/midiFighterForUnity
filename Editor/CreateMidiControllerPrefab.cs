using UnityEditor;
using UnityEngine;

namespace MidiFighter64.Editor
{
    /// <summary>
    /// Maintainer tool: regenerates the shipped <c>MIDI Controller</c> prefab.
    ///
    /// Consumers never run this — the prefab ships with the package and is dragged
    /// straight into a scene. It exists so the prefab can be rebuilt deterministically
    /// whenever a component gains a field, instead of being hand-assembled once and
    /// then drifting from the components it is supposed to represent.
    ///
    /// Deliberately generated through Unity's own API rather than authored as YAML:
    /// a .prefab is full of GUIDs and internal fileIDs that cannot be written by hand
    /// with any confidence.
    /// </summary>
    public static class CreateMidiControllerPrefab
    {
        const string PrefabPath = "Packages/com.caseyfarina.midifighter64/Runtime/MIDI Controller.prefab";

        /// <summary>The device allow list the rig ships with.
        ///
        /// This is not cosmetic. <see cref="MidiEventManager"/>'s own field defaults to
        /// empty, which means "merge every MIDI input port" — and a monitor, loopback,
        /// or network port echoing a controller then delivers every message twice,
        /// toggling each latch on and straight back off. The filter used to be defaulted
        /// by MidiSceneBootstrapper; with that gone, the prefab is what carries it.</summary>
        static readonly string[] DefaultAllowedDevices = { "Fighter", "MIDI Mix" };

        [MenuItem("Tools/MidiFighter64/Regenerate Controller Prefab")]
        public static void Generate()
        {
            var root = new GameObject("MIDI Controller");

            try
            {
                // Order here is the order they appear in the Inspector. Input plumbing
                // first, then routing, then output, then the on-screen readout.
                root.AddComponent<MidiEventManager>();
                root.AddComponent<UnityMainThreadDispatcher>();
                root.AddComponent<MidiGridRouter>();
                root.AddComponent<MidiMixRouter>();
                root.AddComponent<MidiFighterButtonRouter>();
                root.AddComponent<MidiFighterOutput>();
                root.AddComponent<MidiMixOutput>();
                root.AddComponent<MidiStatusDrawer>();

                ApplyShippingDefaults(root);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok || prefab == null)
                {
                    Debug.LogError($"[MidiFighter64] Failed to write the prefab to {PrefabPath}. " +
                                   "If this package is installed read-only (git URL / PackageCache), " +
                                   "regeneration only works from an embedded copy.");
                    return;
                }

                AssetDatabase.SaveAssets();
                Debug.Log($"[MidiFighter64] Wrote {PrefabPath}");
                EditorGUIUtility.PingObject(prefab);
            }
            finally
            {
                // The scratch instance must not survive as a stray scene object,
                // including on the error path above.
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Overrides only the values whose *component* default differs from what the
        /// rig should ship with. Everything else is deliberately left alone so the
        /// prefab keeps tracking the components' own defaults rather than freezing a
        /// second copy of them.
        /// </summary>
        static void ApplyShippingDefaults(GameObject root)
        {
            var manager = root.GetComponent<MidiEventManager>();

            // Written through SerializedObject rather than a setter: the field is
            // private, and SetDeviceFilter also kicks off reconnection, which has no
            // business running in edit mode against a prefab that is being authored.
            var so = new SerializedObject(manager);
            var allowed = so.FindProperty("_allowedDeviceNames");
            allowed.arraySize = DefaultAllowedDevices.Length;
            for (int i = 0; i < DefaultAllowedDevices.Length; i++)
                allowed.GetArrayElementAtIndex(i).stringValue = DefaultAllowedDevices[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
