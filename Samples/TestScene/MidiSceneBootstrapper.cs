using UnityEngine;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Drop this on any GameObject in an empty scene. It ensures all core MIDI
    /// components exist. Newly spawned components are parented under this
    /// bootstrapper's transform for a clean hierarchy.
    ///
    /// The MF64 pad-mode grid can be configured two ways:
    ///   1. Assign a <see cref="MidiFighter64ButtonConfig"/> asset to <c>_mf64ButtonConfig</c>.
    ///   2. Or leave that slot empty and use the inline 8×8 grid embedded on
    ///      this component (drawn by <c>MidiSceneBootstrapperEditor</c>).
    /// If both are set, the asset wins.
    /// </summary>
    public class MidiSceneBootstrapper : MonoBehaviour
    {
        [Header("Midi Fighter 64 — Pad Config")]
        [Tooltip("Optional per-pad Button/Toggle mode config asset. If assigned it overrides " +
                 "the inline grid below.")]
        [SerializeField] MidiFighter64ButtonConfig _mf64ButtonConfig;

        [Tooltip("Fallback default mode used by any pad whose inline entry equals it.")]
        [SerializeField] MidiFighterButtonMode _inlineDefaultMode = MidiFighterButtonMode.Button;

        [Tooltip("Inline 8×8 pad mode grid, indexed by GridButton.linearIndex " +
                 "(row 1 top-left = 0, row 8 bottom-right = 63). Edited via the " +
                 "custom inspector; the raw array is exposed here for serialization.")]
        [SerializeField] MidiFighterButtonMode[] _inlinePadModes = new MidiFighterButtonMode[64];

        [Tooltip("Inline 8×8 pad active-color grid. Off = use router default color for that pad's mode.")]
        [SerializeField] MidiFighterLEDColor[] _inlinePadColors = new MidiFighterLEDColor[64]; // enum default (Off) = fallback

        [Header("Midi Fighter 64 — LED Colors")]
        [Tooltip("LED color when a Toggle pad is turned on.")]
        [SerializeField] MidiFighterLEDColor _toggleOnColor  = MidiFighterLEDColor.White;

        [Tooltip("LED color when a Toggle pad is turned off.")]
        [SerializeField] MidiFighterLEDColor _toggleOffColor = MidiFighterLEDColor.DarkGrey;

        [Tooltip("LED color while a Button pad is held down.")]
        [SerializeField] MidiFighterLEDColor _buttonDownColor = MidiFighterLEDColor.BrightPink;

        [Header("MIDI Mix — Latching Buttons")]
        [Tooltip("Press MIDI Mix Mute buttons once to latch on, press again to release. LED follows latched state.")]
        [SerializeField] bool _latchMute   = false;

        [Tooltip("Press MIDI Mix Rec-Arm buttons once to latch on, press again to release. LED follows latched state.")]
        [SerializeField] bool _latchRecArm = false;

        [Header("Status Drawer")]
        [Tooltip("Spawn the on-screen MidiStatusDrawer overlay. Toggle it in play mode with backtick / F1.")]
        [SerializeField] bool _spawnStatusDrawer = true;

        [Tooltip("Fraction of the display the drawer fills, on whichever axis runs out " +
                 "first — height on a landscape display, width on a portrait one. " +
                 "The drawer is never cropped at any aspect ratio.")]
        [Range(0.1f, 1f)]
        [SerializeField] float _drawerScreenFraction = 0.90f;

        [Tooltip("Right Centered = pinned to the right edge, centered vertically. " +
                 "Screen Centered = centered on both axes. Same at any aspect ratio.")]
        [SerializeField] DrawerPlacement _drawerPlacement = DrawerPlacement.RightCentered;

        [Tooltip("Show the Midi Fighter 64 pad grid. Turn off when working with the MIDI Mix alone.")]
        [SerializeField] bool _showMf64 = true;

        [Tooltip("Show the Akai MIDI Mix strips. Turn off when working with the Midi Fighter 64 alone.")]
        [SerializeField] bool _showMidiMix = true;

        [Tooltip("Last-touched MF64 pad grows while its row/column neighbors deform to compensate.")]
        [SerializeField] bool _enableMf64Fisheye = true;

        [Tooltip("Optional typeface override for the drawer. Leave empty to use the font bundled with this sample.")]
        [SerializeField] Font _drawerFont;

        [Tooltip("Log one resolved-layout report to the console after the drawer builds. " +
                 "Diagnostic only — leave off in normal use.")]
        [SerializeField] bool _logDrawerLayout;

        void Awake()
        {
            NormalizeInlineArrays();
            EnsureCoreComponents(transform, _spawnStatusDrawer);
            ApplyMf64Config();
            ApplyMixLatchConfig();
            ApplyDrawerConfig();
        }

        void ApplyDrawerConfig()
        {
            var drawer = Object.FindFirstObjectByType<MidiStatusDrawer>();
            if (drawer == null) return;
            // Set before the others: the report fires on a delay after the build,
            // so it picks this up even though the drawer already built in OnEnable.
            drawer.LogLayoutDiagnostics = _logDrawerLayout;
            // Each setter no-ops when the value already matches, so the common
            // all-defaults case costs zero rebuilds.
            drawer.EnableMf64Fisheye = _enableMf64Fisheye;   // never rebuilds
            drawer.Placement         = _drawerPlacement;     // restyles, never rebuilds
            drawer.ScreenFraction    = _drawerScreenFraction;
            drawer.FontOverride      = _drawerFont;
            drawer.SetVisibleSections(_showMf64, _showMidiMix);
        }

        void ApplyMixLatchConfig()
        {
            var mix = Object.FindFirstObjectByType<MidiMixRouter>();
            if (mix == null) return;
            mix.LatchMute   = _latchMute;
            mix.LatchRecArm = _latchRecArm;
        }

        // Deliberately does NOT push drawer config. Some drawer setters rebuild
        // the view, which destroys and creates GameObjects — illegal from
        // OnValidate and a route to editor deadlock. Use the MidiStatusDrawer
        // component's own inspector to tweak the drawer during play mode.
        void OnValidate() => NormalizeInlineArrays();

        // Instances serialized before these fields existed deserialize to zero —
        // length-0 arrays, (0,0) vectors — because field initializers don't re-run
        // for already-serialized instances. Anything added here later needs a
        // guard in this method or it silently starts at zero on existing scenes.
        void NormalizeInlineArrays()
        {
            if (_inlinePadModes  == null || _inlinePadModes.Length  != 64) System.Array.Resize(ref _inlinePadModes,  64);
            if (_inlinePadColors == null || _inlinePadColors.Length != 64) System.Array.Resize(ref _inlinePadColors, 64);

            // Deserializes to 0 on scenes saved before this field existed, which
            // would shrink the drawer to nothing.
            if (_drawerScreenFraction <= 0f) _drawerScreenFraction = 0.90f;
        }

        /// <summary>
        /// Ensures every core MIDI component exists in the scene, spawning any
        /// that are missing. When <paramref name="parent"/> is non-null, spawned
        /// GameObjects are parented under it; otherwise they land at the scene root.
        /// Pass <paramref name="includeStatusDrawer"/> = false to skip the
        /// on-screen overlay.
        /// </summary>
        public static void EnsureCoreComponents(Transform parent = null, bool includeStatusDrawer = true)
        {
            Ensure<MidiEventManager>(parent);
            Ensure<UnityMainThreadDispatcher>(parent);
            Ensure<MidiMixRouter>(parent);
            Ensure<MidiGridRouter>(parent);
            Ensure<MidiFighterButtonRouter>(parent);
            Ensure<MidiFighterOutput>(parent);
            Ensure<MidiMixOutput>(parent);
            if (includeStatusDrawer) Ensure<MidiStatusDrawer>(parent);
        }

        void ApplyMf64Config()
        {
            var router = Object.FindFirstObjectByType<MidiFighterButtonRouter>();
            if (router == null) return;

            var cfg = _mf64ButtonConfig;
            if (cfg == null)
            {
                // Build a runtime config from the inline grid.
                cfg = ScriptableObject.CreateInstance<MidiFighter64ButtonConfig>();
                cfg.name = "MF64ButtonConfig_Inline";
                cfg.hideFlags = HideFlags.DontSave;
                cfg.SetPadModes(_inlineDefaultMode, _inlinePadModes);
                cfg.SetPadColors(_inlinePadColors);
            }

            router.Config          = cfg;
            router.ToggleOnColor   = _toggleOnColor;
            router.ToggleOffColor  = _toggleOffColor;
            router.ButtonDownColor = _buttonDownColor;
        }

        static T Ensure<T>(Transform parent) where T : Component
        {
            var existing = Object.FindFirstObjectByType<T>();
            if (existing != null) return existing;
            var go = new GameObject(typeof(T).Name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }
    }
}
