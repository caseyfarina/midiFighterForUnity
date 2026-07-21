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
        [Header("MIDI Devices")]
        [Tooltip("Only connect to MIDI input ports whose name contains one of these fragments (case-insensitive). " +
                 "Defaults to the two controllers this sample is built around, so MIDI monitors, loopback and " +
                 "network ports carrying a copy of their traffic can't deliver every message twice. " +
                 "Clear the list to accept every port.")]
        [SerializeField] string[] _allowedDeviceNames = { "Fighter", "MIDI Mix" };

        [Tooltip("Ports whose name contains one of these fragments are never connected. Applied after the allow list.")]
        [SerializeField] string[] _blockedDeviceNames = new string[0];

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
        [Tooltip("Press MIDI Mix Mute buttons once to latch on, press again to release. LED follows latched state. On by default; untick for momentary.")]
        [SerializeField] bool _latchMute   = true;

        [Tooltip("Press MIDI Mix Rec-Arm buttons once to latch on, press again to release. LED follows latched state. On by default; untick for momentary.")]
        [SerializeField] bool _latchRecArm = true;

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

        [Tooltip("Dark panels with light ink, or light panels with dark ink. Pick the one that " +
                 "opposes the scene behind the drawer. Cycled at runtime with F3.")]
        [SerializeField] DrawerTheme _drawerTheme = DrawerTheme.Dark;

        [Tooltip("Alpha of the drawer's background panels. 0 = widgets float on the scene with " +
                 "no panel at all; 1 = fully opaque. Widget ink is never faded by this.")]
        [Range(0f, 1f)]
        [SerializeField] float _drawerPanelOpacity = 0.30f;

        [Tooltip("Multiplier on the line weight of every stroked widget — knob bodies and pad " +
                 "rings. 1 = the design weight. Only thickness changes; widgets keep their size.")]
        [Range(0.25f, 4f)]
        [SerializeField] float _drawerStrokeWeight = 1f;

        [Tooltip("Show the Midi Fighter 64 pad grid. Turn off when working with the MIDI Mix alone.")]
        [SerializeField] bool _showMf64 = true;

        [Tooltip("Show the Akai MIDI Mix strips. Turn off when working with the Midi Fighter 64 alone.")]
        [SerializeField] bool _showMidiMix = true;

        [Tooltip("Last-touched MF64 pad grows while its row/column neighbors deform to compensate.")]
        [SerializeField] bool _enableMf64Fisheye = true;

        [Tooltip("How far the focused pad grows. A flex-grow weight against the other 7 " +
                 "rows/columns, not a pixel size: 1 = no growth, 3 = three shares to everyone else's 1.")]
        [Range(1f, 6f)]
        [SerializeField] float _mf64FisheyeScale = 3f;

        [Tooltip("Listen for the drawer's F-key shortcuts: F1 show-hide, F2 placement, F3 theme. " +
                 "On by default. Untick when the project binds those keys itself. " +
                 "Backtick (`) always toggles the drawer regardless of this setting.")]
        [SerializeField] bool _enableDrawerFunctionKeys = true;

        [Tooltip("Optional typeface override for the drawer. Leave empty to use the font bundled with this sample.")]
        [SerializeField] Font _drawerFont;

        [Tooltip("Log one resolved-layout report to the console after the drawer builds. " +
                 "Diagnostic only — leave off in normal use.")]
        [SerializeField] bool _logDrawerLayout;

        /// <summary>Bumped whenever a serialized default changes in a way the old
        /// value can't be told apart from a deliberate setting. See
        /// <see cref="MigrateSerializedDefaults"/>.</summary>
        const int CurrentSerializedVersion = 4;

        [HideInInspector]
        [SerializeField] int _serializedVersion; // 0 on anything saved before versioning

        void Awake()
        {
            NormalizeInlineArrays();
            MigrateSerializedDefaults();
            EnsureCoreComponents(transform, _spawnStatusDrawer);
            ApplyDeviceFilter();
            ApplyMf64Config();
            ApplyMixLatchConfig();
            ApplyDrawerConfig();
        }

        // Must run before any device is touched: MidiEventManager connects to
        // whatever it finds in its own OnEnable, which has already happened by
        // the time EnsureCoreComponents returns. Reconnect() re-runs discovery
        // against the filter.
        void ApplyDeviceFilter()
        {
            var manager = Object.FindFirstObjectByType<MidiEventManager>();
            if (manager == null) return;
            manager.SetDeviceFilter(_allowedDeviceNames, _blockedDeviceNames);
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
            drawer.Mf64FisheyeScale  = _mf64FisheyeScale;    // never rebuilds
            drawer.EnableFunctionKeys = _enableDrawerFunctionKeys;
            drawer.Placement         = _drawerPlacement;     // restyles, never rebuilds
            drawer.Theme             = _drawerTheme;         // restyles, never rebuilds
            drawer.PanelOpacity      = _drawerPanelOpacity;  // restyles, never rebuilds
            drawer.StrokeWeight      = _drawerStrokeWeight;  // restyles, never rebuilds
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
        void OnValidate()
        {
            NormalizeInlineArrays();
            MigrateSerializedDefaults();
        }

        // A bool whose default flips can't be repaired by sniffing its value the
        // way _drawerScreenFraction can — a serialized `false` from an older scene
        // is indistinguishable from one the user deliberately unticked. So the
        // version stamp records which defaults an instance was authored against,
        // and each bump applies only to instances that predate it. New instances
        // get the field initializers and are stamped current on first validate.
        void MigrateSerializedDefaults()
        {
            if (_serializedVersion >= CurrentSerializedVersion) return;

            // v1: Mute / Rec-Arm latch (press-to-toggle) became the default.
            if (_serializedVersion < 1)
            {
                _latchMute   = true;
                _latchRecArm = true;
            }

            // v2: the device allow list gained a default. An empty array is a
            // legitimate setting ("accept every port"), so it needs the version
            // stamp for the same reason the v1 bools did — a scene saved before
            // the field existed can't be told apart from one deliberately cleared.
            if (_serializedVersion < 2)
                _allowedDeviceNames = new[] { "Fighter", "MIDI Mix" };

            // v3: drawer panel opacity. Zero is a legitimate setting ("no panel,
            // widgets only"), so it can't be repaired by a value guard either —
            // an old scene deserializes it to 0 and the panels would vanish.
            if (_serializedVersion < 3)
                _drawerPanelOpacity = 0.30f;

            // v4: drawer F-key shortcuts default on. Another bool, so another
            // stamp — an old scene's `false` can't be told from a deliberate one.
            if (_serializedVersion < 4)
                _enableDrawerFunctionKeys = true;

            _serializedVersion = CurrentSerializedVersion;
        }

        // Instances serialized before these fields existed deserialize to zero —
        // length-0 arrays, (0,0) vectors — because field initializers don't re-run
        // for already-serialized instances. Anything added here later needs a
        // guard in this method or it silently starts at zero on existing scenes.
        // Only for values where zero is never a legitimate setting; when it is
        // (any bool), use MigrateSerializedDefaults instead.
        void NormalizeInlineArrays()
        {
            if (_inlinePadModes  == null || _inlinePadModes.Length  != 64) System.Array.Resize(ref _inlinePadModes,  64);
            if (_inlinePadColors == null || _inlinePadColors.Length != 64) System.Array.Resize(ref _inlinePadColors, 64);

            // Deserializes to 0 on scenes saved before this field existed, which
            // would shrink the drawer to nothing.
            if (_drawerScreenFraction <= 0f) _drawerScreenFraction = 0.90f;

            // Below their sliders' own minimums, so 0 can only mean "saved before
            // this field existed" — a value guard is enough, no version stamp.
            if (_mf64FisheyeScale   < 1f)    _mf64FisheyeScale   = 3f;
            if (_drawerStrokeWeight < 0.25f) _drawerStrokeWeight = 1f;
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
