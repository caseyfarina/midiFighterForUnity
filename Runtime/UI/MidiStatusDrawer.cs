using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MidiFighter64
{
    /// <summary>Where <see cref="MidiStatusDrawer"/> anchors on the display.
    /// Vertically centered in both modes, at any aspect ratio.</summary>
    public enum DrawerPlacement
    {
        /// <summary>Pinned to the right edge, centered vertically.</summary>
        RightCentered,
        /// <summary>Centered on both axes.</summary>
        ScreenCentered,
    }

    /// <summary>Which way <see cref="MidiStatusDrawer"/> reads against the scene
    /// behind it. The panels are semi-transparent, so they tint toward whatever
    /// is rendered underneath — pick the theme that opposes your project's
    /// background, not the one that matches it.</summary>
    public enum DrawerTheme
    {
        /// <summary>Dark panels, light ink. For dark scenes. Default.</summary>
        Dark,
        /// <summary>Light panels, dark ink. For bright/white scenes.</summary>
        Light,
    }

    /// <summary>How <see cref="MidiStatusDrawer"/> arranges its widgets. All layouts
    /// mirror the same live MIDI state; only the geometry differs.</summary>
    public enum DrawerLayout
    {
        /// <summary>The shipping layout: an 8×8 flex pad grid above eight vertical
        /// mixer strips. Must stay value 0 — a serialized field added later
        /// deserializes to zero on existing scenes and prefab instances, so zero
        /// has to be the safe default rather than a layout nobody chose.</summary>
        Linear1 = 0,
        /// <summary>Concentric rings, centre outward: MF64 pads as four rings, then
        /// knob / fader arcs per channel, a full-circumference master ring, and a
        /// mute / rec-arm toggle ring.</summary>
        Radial1 = 1,
        /// <summary>Reserved — the sunburst layout, not built yet. Falls back to
        /// <see cref="Linear1"/>.</summary>
        Radial2 = 2,
    }

    /// <summary>
    /// Screen-space overlay showing live state of both MIDI controllers
    /// (Midi Fighter 64 + Akai MIDI Mix). Toggled with backtick or F1.
    /// Read-only: mirrors MIDI events, does not send.
    ///
    /// Renders on every active display: one UIDocument per active display in
    /// <see cref="Display.displays"/>. Toggle state and MIDI state are shared
    /// across all displays.
    /// </summary>
    [DisallowMultipleComponent]
    public class MidiStatusDrawer : MonoBehaviour
    {
        public static MidiStatusDrawer Instance { get; private set; }

        [Tooltip("Optional ThemeStyleSheet for the runtime PanelSettings.")]
        [SerializeField] ThemeStyleSheet _themeStyleSheet;

        [Tooltip("If enabled, the last-touched MF64 pad grows (fisheye focus) while its row/column neighbors deform to compensate.")]
        [SerializeField] bool _enableMf64Fisheye = true;

        [Tooltip("How far the focused pad grows. It's a flex-grow weight against the " +
                 "other 7 rows/columns, not a pixel size: 1 = no growth, 3 = the focused " +
                 "row/column takes 3 shares to everyone else's 1.")]
        [Range(1f, 6f)]
        [SerializeField] float _mf64FisheyeScale = 3f;

        public bool EnableMf64Fisheye
        {
            get => _enableMf64Fisheye;
            set
            {
                if (_enableMf64Fisheye == value) return;
                _enableMf64Fisheye = value;
                if (!value) ClearMf64Focus();
            }
        }

        /// <summary>Growth weight of the focused row/column, 1–6. 1 disables the
        /// visible effect without turning the feature off.</summary>
        public float Mf64FisheyeScale
        {
            get => _mf64FisheyeScale;
            set
            {
                float v = Mathf.Clamp(value, 1f, 6f);
                if (Mathf.Approximately(_mf64FisheyeScale, v)) return;
                _mf64FisheyeScale = v;
                // Re-apply to a pad that's focused right now, so dragging the
                // slider in play mode shows the new scale immediately.
                if (_focusRow >= 0 && _focusCol >= 0)
                    FocusMf64Pad(_focusRow + 1, _focusCol + 1);
            }
        }

        [Tooltip("Show the Midi Fighter 64 pad grid. Turn off when working with the MIDI Mix alone.")]
        [SerializeField] bool _showMf64 = true;

        [Tooltip("Show the Akai MIDI Mix strips. Turn off when working with the Midi Fighter 64 alone.")]
        [SerializeField] bool _showMidiMix = true;

        public bool ShowMf64
        {
            get => _showMf64;
            set { if (_showMf64 == value) return; _showMf64 = value; RebuildIfLive(); }
        }

        public bool ShowMidiMix
        {
            get => _showMidiMix;
            set { if (_showMidiMix == value) return; _showMidiMix = value; RebuildIfLive(); }
        }

        /// <summary>Set both section toggles with a single rebuild. Prefer this
        /// over the individual properties when changing both at once.</summary>
        public void SetVisibleSections(bool showMf64, bool showMidiMix)
        {
            if (_showMf64 == showMf64 && _showMidiMix == showMidiMix) return;
            _showMf64 = showMf64;
            _showMidiMix = showMidiMix;
            RebuildIfLive();
        }

        // Section visibility is baked into the UI tree, so changing it has to
        // rebuild. Cheap and rare (inspector / bootstrapper toggle), and the
        // hidden/compact flags survive because they're plain fields.
        void RebuildIfLive()
        {
            if (isActiveAndEnabled) BuildAllViews();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        // ─── Per-display view ─────────────────────────────────────────────
        /// <summary>One drawer instance rendered on one display.</summary>
        class DrawerView
        {
            public GameObject         host;
            public UIDocument         doc;
            public PanelSettings      settings;
            public VisualElement      root;      // flex container that positions the drawer
            public VisualElement      drawer;

            public PadCell[]          pads       = new PadCell[64];  // linearIndex → cell
            public VisualElement[]    mf64Rows   = new VisualElement[8]; // row containers for fisheye scaling
            public KnobDisplay[,]     knobs      = new KnobDisplay[8, 3];
            public VisualElement[]    faderBars   = new VisualElement[8];
            public VisualElement[]    faderTracks = new VisualElement[8]; // opacity target for "seen" flip
            public VisualElement      masterFaderBar;
            public VisualElement      masterFaderTrack;
            public PadCell[]          mutes      = new PadCell[8];
            public PadCell[]          recArms    = new PadCell[8];
            public PadCell            soloModifier;
            public PadCell            bankLeft;
            public PadCell            bankRight;
            public Label              messageLabel;

            // Radial layouts only; null in Linear1. The knob/fader/master widgets
            // above stay untouched so the linear layout is unaffected, and every
            // handler updates both sets behind a null check.
            public RadialArc[,]       knobArcs   = new RadialArc[8, 3];
            public RadialArc[]        faderArcs  = new RadialArc[8];
            public RadialArc          masterArc;
            public VisualElement      radialSection;    // vertical-offset target
            public VisualElement      floatingMessage;  // absolute, on root not drawer
        }

        readonly List<DrawerView> _views = new();
        bool _hidden = true;
        [Tooltip("Fraction of the display the drawer fills, on whichever axis binds " +
                 "first. Landscape fills this much height; portrait fills this much " +
                 "width. Never crops.")]
        [Range(0.1f, 1f)]
        [SerializeField] float _screenFraction = 0.90f;

        public float ScreenFraction
        {
            get => _screenFraction;
            set
            {
                float v = Mathf.Clamp(value, 0.1f, 1f);
                if (Mathf.Approximately(_screenFraction, v)) return;
                _screenFraction = v;
                ApplyReferenceResolution();
            }
        }

        [Tooltip("Dump one resolved-layout report to the console shortly after the " +
                 "drawer builds. Diagnostic only — leave off in normal use.")]
        [SerializeField] bool _logLayoutDiagnostics;

        public bool LogLayoutDiagnostics
        {
            get => _logLayoutDiagnostics;
            set => _logLayoutDiagnostics = value;
        }

        [Tooltip("Listen for the F-key shortcuts: F1 show-hide, F2 placement, F3 theme. " +
                 "On by default. Untick when the project binds those keys itself. " +
                 "Backtick (`) always toggles the drawer regardless of this setting.")]
        [SerializeField] bool _enableFunctionKeys = true;

        /// <summary>Whether F1–F3 are live. Backtick is never gated — it's the
        /// show/hide key, and a hidden drawer with no way back is a dead end.
        /// Turning this off changes nothing about the drawer's state, only who
        /// can change it; every shortcut has a scripting equivalent.</summary>
        public bool EnableFunctionKeys
        {
            get => _enableFunctionKeys;
            set => _enableFunctionKeys = value;
        }

        [Tooltip("Widget arrangement. Linear 1 is the pad grid over mixer strips; " +
                 "Radial 1 is concentric rings, centre outward. Cycled at runtime with F4.")]
        [SerializeField] DrawerLayout _layout = DrawerLayout.Linear1;

        /// <summary>Which geometry the drawer builds. Rebuilds on change, because
        /// the arrangement is baked into the UI tree — unlike theme or opacity,
        /// there is nothing to restyle in place.</summary>
        public DrawerLayout Layout
        {
            get => _layout;
            set
            {
                if (_layout == value) return;
                _layout = value;
                // Fisheye focus is flex-grow based and meaningless off Linear1;
                // clear it so a stale focus can't be re-applied after the switch.
                ClearMf64Focus();
                RebuildIfLive();
            }
        }

        [Tooltip("Radial only. Rotates the MF64 pad rings about the centre. Negative " +
                 "is counter-clockwise. Affects the pads only — the knob, fader and " +
                 "toggle bands stay keyed to their channel angles.")]
        [Range(-180f, 180f)]
        [SerializeField] float _radialPadRotation = -45f;

        [Tooltip("Radial only. Diameter of the MF64 pads, as a multiple of the " +
                 "reference layout. Ring 0 (the outer 28) runs out of room first — " +
                 "past about 1.9 at full spread its pads start to touch.")]
        [Range(0.5f, 2f)]
        [SerializeField] float _radialPadScale = 1.5f;

        [Tooltip("Radial only. Multiplies the four pad ring radii, pulling the grid " +
                 "in toward the centre or pushing it out. Lower values tighten the " +
                 "cluster but also shorten each ring's circumference, so pads crowd " +
                 "sooner as Pad Size rises.")]
        [Range(0.5f, 1.25f)]
        [SerializeField] float _radialRingSpread = 0.85f;

        [Tooltip("Radial only. Moves the ring stack up or down within the display. " +
                 "-1 is half a square up, +1 half a square down, 0 centred.")]
        [Range(-1f, 1f)]
        [SerializeField] float _radialVerticalOffset = 0f;

        [Tooltip("Radial only. Distance of the MIDI event readout from the bottom-left " +
                 "corner of the display, in design units.")]
        [Range(0f, 200f)]
        [SerializeField] float _radialMessagePadding = 24f;

        /// <summary>Radial only. Shifts the ring stack vertically within the display,
        /// -1 to +1, in units of half the radial square. Applied as a transform rather
        /// than a margin so it never feeds back into the size budget, and applied to
        /// the section rather than the drawer because the drawer's own translate is
        /// already owned by the show/hide slide.</summary>
        public float RadialVerticalOffset
        {
            get => _radialVerticalOffset;
            set
            {
                float v = Mathf.Clamp(value, -1f, 1f);
                if (Mathf.Approximately(_radialVerticalOffset, v)) return;
                _radialVerticalOffset = v;
                ApplyRadialTweaks();
            }
        }

        /// <summary>Radial only. Rotates the pad rings about the centre, in degrees.
        /// Negative is counter-clockwise (y grows downward in UI coordinates, so
        /// increasing angles run clockwise). Pads only — rotating the mixer bands
        /// would break the channel-angle mapping the arcs and toggles depend on.</summary>
        public float RadialPadRotation
        {
            get => _radialPadRotation;
            set
            {
                float v = Mathf.Clamp(value, -180f, 180f);
                if (Mathf.Approximately(_radialPadRotation, v)) return;
                _radialPadRotation = v;
                ApplyRadialTweaks();
            }
        }

        /// <summary>Radial only. Pad diameter as a multiple of the reference layout's,
        /// which is 1. The measured per-ring proportions are preserved — this scales
        /// all four together rather than flattening them.</summary>
        public float RadialPadScale
        {
            get => _radialPadScale;
            set
            {
                float v = Mathf.Clamp(value, 0.5f, 2f);
                if (Mathf.Approximately(_radialPadScale, v)) return;
                _radialPadScale = v;
                ApplyRadialTweaks();
            }
        }

        /// <summary>Radial only. Multiplier on the four pad ring radii. Below 1 the
        /// grid tightens toward the centre, which also shortens each ring's
        /// circumference — so this and <see cref="RadialPadScale"/> compete for the
        /// same arc, and pads touch sooner when both move together.</summary>
        public float RadialRingSpread
        {
            get => _radialRingSpread;
            set
            {
                float v = Mathf.Clamp(value, 0.5f, 1.25f);
                if (Mathf.Approximately(_radialRingSpread, v)) return;
                _radialRingSpread = v;
                ApplyRadialTweaks();
            }
        }

        /// <summary>Radial only. Inset of the floating event readout from the display's
        /// bottom-left corner, in design units.</summary>
        public float RadialMessagePadding
        {
            get => _radialMessagePadding;
            set
            {
                float v = Mathf.Clamp(value, 0f, 200f);
                if (Mathf.Approximately(_radialMessagePadding, v)) return;
                _radialMessagePadding = v;
                ApplyRadialTweaks();
            }
        }

        /// <summary>Pushes the two radial-only placement settings to live elements.
        /// Like <see cref="ApplyTheme"/> this restyles in place and never rebuilds, so
        /// both sliders are live in play mode and no "seen" state is lost.</summary>
        void ApplyRadialTweaks()
        {
            foreach (var v in _views)
            {
                if (v.radialSection != null)
                    v.radialSection.style.translate =
                        new Translate(0, _radialVerticalOffset * RadialSideDesign * 0.5f);

                if (v.floatingMessage != null)
                {
                    v.floatingMessage.style.left   = _radialMessagePadding;
                    v.floatingMessage.style.bottom = _radialMessagePadding;
                }

                PlaceRadialPads(v);
            }
        }

        [Tooltip("Where the drawer sits on the display. Vertically centered either way.")]
        [SerializeField] DrawerPlacement _placement = DrawerPlacement.RightCentered;

        public DrawerPlacement Placement
        {
            get => _placement;
            set { if (_placement == value) return; _placement = value; ApplyPlacement(); }
        }

        // Shared "seen" state — same across displays
        readonly bool[,] _knobsSeen  = new bool[8, 3];
        readonly bool[]  _fadersSeen = new bool[8];
        bool _masterFaderSeen;

        /// <summary>Last LED color each MF64 pad was lit with, *before* the theme
        /// adapted it. Kept so a theme change can re-adapt from the original —
        /// re-adapting an already-adapted color would drift darker every switch.
        /// Shared across displays, like the "seen" state.</summary>
        readonly Color[] _padRawFill = new Color[64];

        MidiFighterButtonRouter _btnRouter;
        Font _uiFont;      // resolved cache — cleared when the override changes

        [Tooltip("Optional typeface override. Leave empty to use the font bundled with this sample.")]
        [SerializeField] Font _fontOverride;

        /// <summary>Font asset shipped with this sample, loaded by name from
        /// <c>UI/Resources/</c>. Package Manager copies it into the consumer's
        /// Assets on sample import, where it becomes a real Resources folder.</summary>
        public const string BundledFontResourceName = "CossetteTitre-Regular";

        Font UiFont
        {
            get
            {
                // Explicit != null throughout: Unity's overloaded operator treats
                // destroyed/unassigned objects as null, which ?? does not.
                if (_uiFont != null) return _uiFont;

                // 1. Explicit override, 2. the font bundled with this sample,
                // 3. a dynamic OS font so text still renders if the sample's
                //    Resources folder was removed.
                if (_fontOverride != null) _uiFont = _fontOverride;
                if (_uiFont == null) _uiFont = Resources.Load<Font>(BundledFontResourceName);
                if (_uiFont == null) _uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "sans-serif" }, 14);
                if (_uiFont == null) _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                return _uiFont;
            }
        }

        /// <summary>Swap the drawer's typeface. Rebuilds so live labels pick it
        /// up; pass null to fall back to the bundled font.</summary>
        public Font FontOverride
        {
            get => _fontOverride;
            set
            {
                if (_fontOverride == value) return;
                _fontOverride = value;
                _uiFont = null;      // force the fallback chain to re-resolve
                RebuildIfLive();
            }
        }

        const float UnseenOpacity = 0.4f;
        const float SeenOpacity   = 1.0f;

        // ─── Theme ────────────────────────────────────────────────────────

        [Tooltip("Dark panels with light ink, or light panels with dark ink. " +
                 "Pick the one that opposes the scene behind the drawer.")]
        [SerializeField] DrawerTheme _theme = DrawerTheme.Dark;

        [Tooltip("Alpha of the drawer's background panels. 0 = widgets float on the " +
                 "scene with no panel at all; 1 = fully opaque. The widgets themselves " +
                 "are never faded by this.")]
        [Range(0f, 1f)]
        [SerializeField] float _panelOpacity = 0.30f;

        [Tooltip("Opacity of the widgets themselves — pads, knobs, faders, mixer " +
                 "buttons. The complement to Panel Opacity, which fades only the " +
                 "backing panels. Multiplies with the dimming applied to controls " +
                 "that haven't received a MIDI event yet.")]
        [Range(0f, 1f)]
        [SerializeField] float _uiOpacity = 1f;

        [Tooltip("Multiplier on the line weight of every stroked widget — knob bodies " +
                 "and pad rings. 1 = the design weight. Only thickness changes; the " +
                 "widgets stay the same size and proportions.")]
        [Range(0.25f, 4f)]
        [SerializeField] float _strokeWeight = 1f;

        /// <summary>Dark or light. Restyles in place — no rebuild, so "seen" state survives.</summary>
        public DrawerTheme Theme
        {
            get => _theme;
            set { if (_theme == value) return; _theme = value; ApplyTheme(); }
        }

        /// <summary>Alpha of the background panels, 0–1. Widget ink is unaffected.</summary>
        public float PanelOpacity
        {
            get => _panelOpacity;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(_panelOpacity, v)) return;
                _panelOpacity = v;
                ApplyTheme();
            }
        }

        /// <summary>Opacity of the widgets themselves, 0–1. The complement to
        /// <see cref="PanelOpacity"/>: that one fades the backing panels and
        /// deliberately leaves widget ink alone, so with both at 0 the drawer
        /// disappears entirely, and with panels at 0 and this at 1 the widgets float
        /// on the scene. Multiplies with each control's unseen dimming rather than
        /// replacing it, so "not yet touched" stays distinguishable at any setting.</summary>
        public float UiOpacity
        {
            get => _uiOpacity;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(_uiOpacity, v)) return;
                _uiOpacity = v;
                ApplyUiOpacity();
            }
        }

        /// <summary>Global line-weight multiplier for every stroked widget, 0.25–4.
        /// Sizes and proportions are untouched — only thickness.</summary>
        public float StrokeWeight
        {
            get => _strokeWeight;
            set
            {
                float v = Mathf.Clamp(value, 0.25f, 4f);
                if (Mathf.Approximately(_strokeWeight, v)) return;
                _strokeWeight = v;
                ApplyTheme();
            }
        }

        /// <summary>
        /// Every color the drawer paints, resolved from <see cref="DrawerTheme"/>.
        /// Built fresh on each theme/opacity change rather than cached, so there is
        /// no second copy to fall out of sync.
        /// </summary>
        readonly struct Palette
        {
            public readonly Color SectionBg;    // panel background (carries PanelOpacity)
            public readonly Color Ink;          // fader fills, master, lit knob ticks, mix pads
            public readonly Color Track;        // fader/master troughs
            public readonly Color TickOff;      // knob ticks above the current value
            public readonly Color PadStroke;    // pad outline ring
            public readonly Color Label;        // captions
            public readonly Color MessageBg;
            public readonly Color MessageText;
            public readonly bool  IsLight;

            Palette(Color sectionBg, Color ink, Color track, Color tickOff, Color padStroke,
                    Color label, Color messageBg, Color messageText, bool isLight)
            {
                SectionBg = sectionBg; Ink = ink; Track = track; TickOff = tickOff;
                PadStroke = padStroke; Label = label; MessageBg = messageBg;
                MessageText = messageText; IsLight = isLight;
            }

            public static Palette For(DrawerTheme theme, float panelOpacity)
            {
                float a = Mathf.Clamp01(panelOpacity);
                // Light values are strictly neutral (r == g == b). A color bias
                // here reads as a tint once the semi-transparent panel blends
                // with the scene behind it — a blue-biased "near black" came out
                // brown over a warm background.
                return theme == DrawerTheme.Light
                    ? new Palette(
                        sectionBg:   new Color(0.94f, 0.94f, 0.94f, a),
                        ink:         new Color(0.05f, 0.05f, 0.05f),
                        track:       new Color(0.72f, 0.72f, 0.72f),
                        tickOff:     new Color(0.76f, 0.76f, 0.76f),
                        padStroke:   new Color(0.34f, 0.34f, 0.34f),
                        label:       new Color(0.16f, 0.16f, 0.16f),
                        messageBg:   new Color(1f, 1f, 1f, a),
                        messageText: new Color(0.09f, 0.09f, 0.09f),
                        isLight:     true)
                    : new Palette(
                        sectionBg:   new Color(0.055f, 0.055f, 0.060f, a),
                        ink:         Color.white,
                        track:       new Color(0.14f, 0.14f, 0.16f),
                        tickOff:     new Color(0.22f, 0.22f, 0.24f),
                        padStroke:   new Color(0.55f, 0.55f, 0.60f),
                        label:       new Color(0.75f, 0.75f, 0.78f),
                        messageBg:   new Color(0f, 0f, 0f, a),
                        messageText: new Color(0.85f, 0.85f, 0.90f),
                        isLight:     false);
            }

            /// <summary>
            /// Adapts a hardware LED color for this theme. The MF64 pads mirror real
            /// LED colors, so they are deliberately *not* themed — except that the
            /// greyscale end of the palette (White, Grey, DarkGrey) is invisible on a
            /// light panel. Only near-white fills are darkened, and only in Light
            /// theme; the saturated colors read fine either way and pass through
            /// untouched. Without this, Light theme silently loses half the palette.
            /// </summary>
            public Color AdaptLed(Color c)
            {
                if (!IsLight) return c;
                float luminance = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                if (luminance < 0.62f) return c;
                // Keep the hue, drop the value to sit alongside the theme's ink.
                float t = Mathf.InverseLerp(0.62f, 1f, luminance);
                return Color.Lerp(c, new Color(c.r * 0.22f, c.g * 0.22f, c.b * 0.22f, c.a), t);
            }
        }

        Palette Colors => Palette.For(_theme, _panelOpacity);

        // USS classes, so a theme change can find the elements the DrawerView
        // struct doesn't retain (section panels, captions) without a rebuild.
        const string SectionClass = "midi-drawer-section";
        const string LabelClass   = "midi-drawer-label";
        const string MessageClass = "midi-drawer-message";

        // The drawer needs a *definite* width. It's absolutely positioned with
        // only right/top/bottom set, so without this it is shrink-to-fit — and
        // the MF64 grid's `width: 100%` then resolves against the full available
        // width instead of the panel, stretching the whole drawer to the screen
        // edge. Wide enough for the MIDI Mix section's 380px minWidth plus the
        // drawer's 14px side padding.
        // Padding constants, referenced both where they're applied and in the
        // size math below. Never inline these — if the applied padding and the
        // math disagree, the pad grid stops being square.
        const float DrawerPadX  = 14f;   // drawer left/right padding
        const float DrawerPadY  = 12f;   // drawer top/bottom padding
        const float SectionPad  = 10f;   // MakeSection padding, all sides
        const float CellMargin  = 2f;    // per-cell right/bottom margin
        const float Mf64SectionGap = 10f; // MF64 section's marginBottom

        /// <summary>Design-space side of the 8×8 pad square. An arbitrary authoring
        /// unit — the panel scales it to the screen, so its absolute value only sets
        /// internal proportions, never the on-screen size. ScreenFraction does that.</summary>
        const float GridSideDesign = 600f;

        // ─── MIDI Mix widget sizes ───────────────────────────────────────
        // Class-level so MixSectionHeight can be derived from them. Changing a
        // size here automatically corrects the height budget; a local constant
        // would silently leave the budget stale.
        const float MixPadSize   = 20f;   // mute / rec-arm / solo / bank pads
        const float FaderWidth   = 24f;
        const float FaderHeight  = 64f;
        const float KnobGap      = 4f;

        /// <summary>Knob diameter, tracked to the MF64 pad cell so both sections
        /// read as one instrument. 0.88 leaves a little air in the column.</summary>
        const float KnobSize = ((GridSideDesign - (8f * CellMargin)) / 8f) * 0.88f;

        /// <summary>One channel strip: 3 knobs, mute, rec-arm, fader.</summary>
        const float StripHeight = (3f * (KnobSize + KnobGap))
                                + (MixPadSize + 4f) + (MixPadSize + 6f) + FaderHeight;

        /// <summary>Master row + utility row + section padding. The only estimated
        /// number in the layout — it depends on label metrics, so the bundled font
        /// shifts it a few px. It affects how exactly ScreenFraction is hit and
        /// nothing else; it can never make the pad grid non-square. Correct it from
        /// the "mix section h" line of the Log Layout Report.</summary>
        const float MixChromeHeight = 109f;

        // Type sizes, in design units. Doubled from the originals (10 / 7) when the
        // drawer grew — they were authored against a much smaller panel.
        const int MessageFontSize = 20;
        const int CaptionFontSize = 14;

        /// <summary>Fixed content height of the MIDI Mix section, in design units.</summary>
        const float MixSectionHeight = StripHeight + MixChromeHeight;

        /// <summary>Content height of the MF64 section: its own padding plus the
        /// pad square.</summary>
        const float Mf64SectionHeight = (2f * SectionPad) + GridSideDesign;

        /// <summary>Height of the standalone message strip, built only when the mix
        /// section is hidden (with Mix shown the strip lives in its utility row and
        /// is already inside <see cref="MixChromeHeight"/>). Estimated the same way
        /// and for the same reason: a text line plus its padding.</summary>
        const float MessageSectionHeight = (2f * SectionPad) + (MessageFontSize * 1.2f) + 8f;

        /// <summary>Side of the radial layouts' square, in design units. Like
        /// <see cref="GridSideDesign"/> this is an authoring unit only — every radius
        /// below is a fraction of half this, so changing it rescales the whole layout
        /// together and changes nothing on screen. 920 matches the reference SVG's
        /// viewBox, which keeps the measured fractions directly comparable.
        ///
        /// It is a constant and must stay one. Measuring the container and writing a
        /// size back is the pattern that hard-freezes the editor — see DEVNOTES.</summary>
        const float RadialSideDesign = 920f;

        /// <summary>Content height of a radial layout's section: its own padding plus
        /// the square.</summary>
        const float RadialSectionHeight = (2f * SectionPad) + RadialSideDesign;

        /// <summary>True when the active layout draws a radial square rather than the
        /// linear grid-over-strips stack. Radial2 is not built yet and falls back to
        /// Linear1, so it deliberately does not count as radial.</summary>
        bool IsRadial => _layout == DrawerLayout.Radial1;

        /// <summary>Side of the 8×8 pad square, in design units.</summary>
        float GridSide => GridSideDesign;

        /// <summary>Follows from the square: the grid plus everything beside it.
        /// Unconditional within Linear1 on purpose — the mix section is deliberately
        /// built to the same content width so the two sections' 8 columns line up, so
        /// it wants the grid's width even when the grid isn't shown. That reasoning is
        /// linear-only: a radial layout has exactly one square and no columns to
        /// align, so both of its axes come from the same constant.</summary>
        float DrawerWidth => (IsRadial ? RadialSideDesign : GridSide)
                           + (2f * SectionPad) + (2f * DrawerPadX);

        /// <summary>
        /// Full drawer height in design units, summing exactly what <c>BuildView</c>
        /// lays out for the current section visibility.
        ///
        /// Every term must be conditional on its section. A term that outlives the
        /// section it measures leaves phantom units in the derived reference, and
        /// since `Expand` scales by min(screenW/refW, screenH/refH) the drawer then
        /// under-fills by exactly that ratio — Screen Fill still "works", it just
        /// fills a budget that is mostly empty space. Hiding the MF64 grid used to
        /// do this, costing 600 units and rendering the mixer at ~40% of the display.
        ///
        /// There is always at least one section: with Mix hidden the message strip
        /// becomes its own panel, so all four visibility combinations have content.
        /// </summary>
        float DrawerHeight
        {
            get
            {
                float h = 2f * DrawerPadY;

                // Radial draws one square plus the message strip, which always gets
                // its own panel there because the strip's usual home is the mix
                // section's utility row and no radial layout builds one.
                // Radial's readout floats at the display's bottom-left, outside the
                // flow, so it contributes nothing to the budget. Only the square does.
                if (IsRadial) return h + RadialSectionHeight;

                // The gap is the MF64 section's own marginBottom, and a section
                // always follows it — so it belongs to the MF64 term, not between.
                if (_showMf64) h += Mf64SectionHeight + Mf64SectionGap;
                h += _showMidiMix ? MixSectionHeight : MessageSectionHeight;
                return h;
            }
        }

        /// <summary>Reference resolution is derived, not authored. `Expand` scales by
        /// min(screenW/refW, screenH/refH), so making the reference the drawer's own
        /// design size divided by ScreenFraction means the binding axis — height in
        /// landscape, width in portrait — lands exactly on that fraction. No branch
        /// on orientation is needed; min() already picks the constraining axis.</summary>
        Vector2Int DerivedReferenceResolution => new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(DrawerWidth  / _screenFraction)),
            Mathf.Max(1, Mathf.RoundToInt(DrawerHeight / _screenFraction)));

        void ApplyReferenceResolution()
        {
            var res = DerivedReferenceResolution;
            foreach (var v in _views)
                if (v.settings != null) v.settings.referenceResolution = res;
        }

        // ─── Fisheye focus ────────────────────────────────────────────────
        // Active pad grows in both axes so it stays proportional (square);
        // its row (height) and its column (width across all rows) scale by
        // the same factor, so neighbors in the row/column deform.
        const int   FocusHoldMs = 800;   // toggle mode: keep focused this long
        int _focusRow = -1, _focusCol = -1;
        IVisualElementScheduledItem _focusClearTimer;
        // ─── Unity lifecycle ──────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // Color's default is transparent black, which would blank a pad the
            // moment the theme changed before it was ever lit.
            for (int i = 0; i < _padRawFill.Length; i++) _padRawFill[i] = Color.white;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // Survives view rebuilds (destroying a PanelSettings doesn't cascade to
            // the sheet it references), so it's released here rather than in teardown.
            if (_fallbackTheme != null) Destroy(_fallbackTheme);
        }

        // Restyle-only settings, so they can be dialed in live from this
        // component's Inspector during play mode. Deliberately does NOT touch
        // anything that rebuilds: a rebuild destroys and creates GameObjects,
        // which is illegal from OnValidate and a route to editor deadlock. With no
        // views built yet (edit mode, or before OnEnable) both calls are no-ops.
        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            ApplyTheme();
            ApplyUiOpacity();
            ApplyRadialTweaks();
            ApplyPlacement();
        }

        void OnEnable()
        {
            // Must precede BuildAllViews: BuildMf64Section reads _btnRouter.Config to
            // decide each pad's Button/Toggle mode, so finding the router afterwards
            // built every cell as Button no matter how the grid was configured.
            _btnRouter = Object.FindFirstObjectByType<MidiFighterButtonRouter>();

            BuildAllViews();

            // Subscribe MF64
            MidiFighterButtonRouter.OnToggle         += HandleToggle;
            MidiFighterButtonRouter.OnButtonPress    += HandleButtonPress;
            MidiFighterButtonRouter.OnButtonRelease  += HandleButtonRelease;

            // Subscribe MIDI Mix
            MidiMixRouter.OnKnob          += HandleMixKnob;
            MidiMixRouter.OnChannelFader  += HandleMixChannelFader;
            MidiMixRouter.OnMasterFader   += HandleMixMasterFader;
            MidiMixRouter.OnMute          += HandleMixMute;
            MidiMixRouter.OnRecArm        += HandleMixRecArm;
            MidiMixRouter.OnSoloModifier  += HandleMixSoloModifier;
            MidiMixRouter.OnBankLeft      += HandleMixBankLeft;
            MidiMixRouter.OnBankRight     += HandleMixBankRight;
        }

        void OnDisable()
        {
            MidiFighterButtonRouter.OnToggle         -= HandleToggle;
            MidiFighterButtonRouter.OnButtonPress    -= HandleButtonPress;
            MidiFighterButtonRouter.OnButtonRelease  -= HandleButtonRelease;

            MidiMixRouter.OnKnob          -= HandleMixKnob;
            MidiMixRouter.OnChannelFader  -= HandleMixChannelFader;
            MidiMixRouter.OnMasterFader   -= HandleMixMasterFader;
            MidiMixRouter.OnMute          -= HandleMixMute;
            MidiMixRouter.OnRecArm        -= HandleMixRecArm;
            MidiMixRouter.OnSoloModifier  -= HandleMixSoloModifier;
            MidiMixRouter.OnBankLeft      -= HandleMixBankLeft;
            MidiMixRouter.OnBankRight     -= HandleMixBankRight;

            TearDownAllViews();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // Backtick is never gated. It's the show/hide key, and a hidden
            // drawer with no way back is a dead end; the function keys are the
            // ones projects actually collide with, so only they answer to
            // EnableFunctionKeys. F1 is an alias for backtick, so it's gated.
            if (kb.backquoteKey.wasPressedThisFrame ||
                (_enableFunctionKeys && kb.f1Key.wasPressedThisFrame))
            {
                _hidden = !_hidden;
                ApplyHiddenState(instant: false);
            }

            if (!_enableFunctionKeys) return;

            if (kb.f2Key.wasPressedThisFrame)
                Placement = _placement == DrawerPlacement.RightCentered
                    ? DrawerPlacement.ScreenCentered
                    : DrawerPlacement.RightCentered;
            if (kb.f3Key.wasPressedThisFrame)
                Theme = _theme == DrawerTheme.Dark ? DrawerTheme.Light : DrawerTheme.Dark;
            // Radial2 is not built yet, so F4 cycles only the two live layouts.
            if (kb.f4Key.wasPressedThisFrame)
                Layout = _layout == DrawerLayout.Linear1 ? DrawerLayout.Radial1 : DrawerLayout.Linear1;
        }

        // The drawer is vertically centered in both modes; only the horizontal
        // anchor differs. Applied to the root flex container, so no rebuild.
        void ApplyPlacement()
        {
            foreach (var v in _views)
            {
                if (v.root == null) continue;
                v.root.style.justifyContent = _placement == DrawerPlacement.ScreenCentered
                    ? Justify.Center
                    : Justify.FlexEnd;
            }
        }

        /// <summary>
        /// Repaints every themed element in place — palette and stroke weight,
        /// the two settings that change how widgets look without resizing them
        /// or touching the tree. Like <see cref="ApplyPlacement"/>
        /// this deliberately avoids a rebuild: rebuilding resets each widget's
        /// "seen" opacity, so flipping the theme would wrongly re-dim every control
        /// the user had already touched. Section panels and captions aren't held in
        /// DrawerView, hence the class-name queries.
        /// </summary>
        void ApplyTheme()
        {
            var p = Colors;
            foreach (var v in _views)
            {
                if (v.root == null) continue;

                v.root.Query<VisualElement>(className: SectionClass)
                      .ForEach(s => s.style.backgroundColor = p.SectionBg);
                v.root.Query<Label>(className: LabelClass)
                      .ForEach(l => l.style.color = p.Label);
                v.root.Query<Label>(className: MessageClass).ForEach(l =>
                {
                    l.style.backgroundColor = p.MessageBg;
                    l.style.color           = p.MessageText;
                });
                v.root.Query<KnobDisplay>().ForEach(k =>
                {
                    k.SetInk(p.Ink, p.TickOff, p.PadStroke);
                    k.StrokeScale = _strokeWeight;
                });
                v.root.Query<PadCell>().ForEach(c =>
                {
                    c.StrokeColor = p.PadStroke;
                    c.StrokeScale = _strokeWeight;
                });
                // Type query rather than a walk over the arc arrays, matching the two
                // above — it then covers any arc added later for free.
                v.root.Query<RadialArc>().ForEach(a =>
                {
                    a.SetInk(p.Ink, p.Track);
                    a.StrokeScale = _strokeWeight;
                });

                foreach (var bar in v.faderBars)     if (bar   != null) bar.style.backgroundColor   = p.Ink;
                foreach (var track in v.faderTracks) if (track != null) track.style.backgroundColor = p.Track;
                if (v.masterFaderBar   != null) v.masterFaderBar.style.backgroundColor   = p.Ink;
                if (v.masterFaderTrack != null) v.masterFaderTrack.style.backgroundColor = p.Track;

                // Mixer pads are drawer chrome — they take the theme's ink.
                foreach (var pad in v.mutes)   if (pad != null) pad.FillColor = p.Ink;
                foreach (var pad in v.recArms) if (pad != null) pad.FillColor = p.Ink;
                if (v.soloModifier != null) v.soloModifier.FillColor = p.Ink;
                if (v.bankLeft     != null) v.bankLeft.FillColor     = p.Ink;
                if (v.bankRight    != null) v.bankRight.FillColor    = p.Ink;

                // MF64 pads mirror hardware LED colors, so they re-adapt from the
                // raw color rather than taking the ink — see Palette.AdaptLed.
                for (int i = 0; i < v.pads.Length; i++)
                    if (v.pads[i] != null) v.pads[i].FillColor = p.AdaptLed(_padRawFill[i]);
            }
        }

        /// <summary>
        /// Repaints widget opacity from stored state. Like <see cref="ApplyTheme"/>
        /// this deliberately avoids a rebuild, and for the same reason: rebuilding
        /// would reset each control's "seen" flag and wrongly re-dim everything the
        /// user had already touched.
        ///
        /// Recomputed from <c>_knobsSeen</c> / <c>_fadersSeen</c> / <c>_masterFaderSeen</c>
        /// rather than read back off the elements, because the stored flags are the
        /// source of truth — the elements only ever mirror them. That also means the
        /// build sites need no changes: <see cref="BuildAllViews"/> calls this once at
        /// the end and every widget lands correctly regardless of what it was built at.
        /// </summary>
        void ApplyUiOpacity()
        {
            foreach (var v in _views)
            {
                if (v.root == null) continue;

                // Pads have no seen state — a pad is either lit by hardware or not,
                // and the LED colour already carries that.
                foreach (var pad in v.pads) if (pad != null) pad.style.opacity = _uiOpacity;
                foreach (var pad in v.mutes)   if (pad != null) pad.style.opacity = _uiOpacity;
                foreach (var pad in v.recArms) if (pad != null) pad.style.opacity = _uiOpacity;
                if (v.soloModifier != null) v.soloModifier.style.opacity = _uiOpacity;
                if (v.bankLeft     != null) v.bankLeft.style.opacity     = _uiOpacity;
                if (v.bankRight    != null) v.bankRight.style.opacity    = _uiOpacity;

                for (int ch = 0; ch < 8; ch++)
                    for (int r = 0; r < 3; r++)
                        if (v.knobs[ch, r] != null)
                            v.knobs[ch, r].style.opacity = SeenScale(_knobsSeen[ch, r]);

                for (int ch = 0; ch < 8; ch++)
                    if (v.faderTracks[ch] != null)
                        v.faderTracks[ch].style.opacity = SeenScale(_fadersSeen[ch]);

                if (v.masterFaderTrack != null)
                    v.masterFaderTrack.style.opacity = SeenScale(_masterFaderSeen);

                // Radial arcs mirror the same controls, so they take the same seen
                // state — otherwise they would sit permanently at full brightness
                // while the linear widgets dimmed.
                for (int ch = 0; ch < 8; ch++)
                    for (int r = 0; r < 3; r++)
                        if (v.knobArcs[ch, r] != null)
                            v.knobArcs[ch, r].style.opacity = SeenScale(_knobsSeen[ch, r]);

                for (int ch = 0; ch < 8; ch++)
                    if (v.faderArcs[ch] != null)
                        v.faderArcs[ch].style.opacity = SeenScale(_fadersSeen[ch]);

                if (v.masterArc != null)
                    v.masterArc.style.opacity = SeenScale(_masterFaderSeen);

                // Type is part of the readout, not the panel — captions ("MASTER",
                // "SOLO"), the bank arrows, and the event strip all fade with the
                // widgets, so both sliders at 0 leaves nothing on screen. Found by
                // class rather than held on the view, the same way ApplyTheme finds
                // them; every label goes through MakeLabel so the classes are
                // exhaustive. Note the message strip carries its own background, so
                // that panel responds to this slider as well as to Panel Opacity.
                v.root.Query<Label>(className: LabelClass)
                      .ForEach(l => l.style.opacity = _uiOpacity);
                v.root.Query<Label>(className: MessageClass)
                      .ForEach(l => l.style.opacity = _uiOpacity);
            }
        }

        /// <summary>A control's opacity: its unseen dimming scaled by the global
        /// widget opacity. Multiplied, not replaced, so the not-yet-touched cue
        /// survives at every setting.</summary>
        float SeenScale(bool seen) => (seen ? SeenOpacity : UnseenOpacity) * _uiOpacity;

        // ─── View lifecycle ──────────────────────────────────────────────
        void BuildAllViews()
        {
            TearDownAllViews();

            var displays = Display.displays;
            // Always render on display 0 (main). Additional displays only if
            // they've been Activate()d by user code.
            for (int i = 0; i < displays.Length; i++)
            {
                if (i != 0 && !displays[i].active) continue;
                _views.Add(BuildView(i));
            }
            ApplyHiddenState(instant: true);
            ApplyPlacement();
            // One pass over the finished tree, so anything the build sites didn't
            // theme explicitly (KnobDisplay ink, for one) still lands correctly.
            ApplyTheme();
            ApplyUiOpacity();
        }

        /// <summary>Dumps resolved geometry so layout can be verified from the
        /// editor log instead of by eye. Compares each measured value against
        /// what the design math predicts.</summary>
        void LogLayout()
        {
            if (!_logLayoutDiagnostics || _views.Count == 0) return;

            var v = _views[0];
            if (v.drawer == null) return;

            // A display:none element has no resolved layout — every measurement
            // comes back NaN. The drawer starts hidden, so only report once it
            // has actually been shown.
            if (v.drawer.resolvedStyle.display == DisplayStyle.None
                || float.IsNaN(v.drawer.resolvedStyle.width))
                return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[MidiStatusDrawer] layout diagnostics");
            sb.AppendLine($"  screen           {Screen.width}x{Screen.height}  (aspect {(float)Screen.width / Mathf.Max(1, Screen.height):0.000})");
            sb.AppendLine($"  refResolution    {DerivedReferenceResolution}  (design {DrawerWidth:0.#}x{DrawerHeight:0.#}, fill {_screenFraction:0.00})");
            // Which sections are in the height budget. Without this the coverage
            // numbers below can't be read: an under-filling drawer looks identical
            // whether ScreenFraction is wrong or the budget counts a hidden section.
            sb.AppendLine($"  sections         mf64={_showMf64} mix={_showMidiMix}  (budget: " +
                          $"{(_showMf64 ? $"mf64 {Mf64SectionHeight:0.#} + gap {Mf64SectionGap:0.#} + " : "")}" +
                          $"{(_showMidiMix ? $"mix {MixSectionHeight:0.#}" : $"msg {MessageSectionHeight:0.#}")}" +
                          $" + pad {2f * DrawerPadY:0.#})");

            float dw = v.drawer.resolvedStyle.width, dh = v.drawer.resolvedStyle.height;
            sb.AppendLine($"  drawer resolved  {dw:0.#}x{dh:0.#}");

            // Coverage must compare design units to design units. Dividing by
            // Screen.width mixes units and reports the panel scale factor as if
            // it were coverage. The root element IS the canvas in design units.
            float cw = v.root != null ? v.root.resolvedStyle.width  : float.NaN;
            float ch = v.root != null ? v.root.resolvedStyle.height : float.NaN;
            sb.AppendLine($"  canvas (design)  {cw:0.#}x{ch:0.#}");
            sb.AppendLine($"  coverage         width {dw / cw:0.000}  height {dh / ch:0.000}   <- one should be ~{_screenFraction:0.00}");

            var row = v.mf64Rows[0];
            var grid = row?.parent;
            if (grid != null)
                sb.AppendLine($"  grid resolved    {grid.resolvedStyle.width:0.#}x{grid.resolvedStyle.height:0.#}   <- must be square");

            var cell = v.pads[0];
            if (cell != null)
                sb.AppendLine($"  pad cell         {cell.resolvedStyle.width:0.##}x{cell.resolvedStyle.height:0.##}   <- must be square, else pads render elliptical");

            // Mix strips must span the same width as the grid or the 8 columns drift.
            var strip = v.knobs[0, 0]?.parent;
            var stripsRow = strip?.parent;
            if (stripsRow != null && grid != null)
                sb.AppendLine($"  strips row       {stripsRow.resolvedStyle.width:0.#}   vs grid {grid.resolvedStyle.width:0.#}   <- must match");
            if (strip != null && cell != null)
                sb.AppendLine($"  strip width      {strip.resolvedStyle.width:0.##}  vs cell {cell.resolvedStyle.width:0.##}   <- must match");

            // MixSectionHeight is hand-summed; this is how to correct it.
            var mixSection = stripsRow?.parent;
            if (mixSection != null)
                sb.AppendLine($"  mix section h    {mixSection.resolvedStyle.height:0.#}   <- MixSectionHeight const is {MixSectionHeight}");

            Debug.Log(sb.ToString());
        }

        void TearDownAllViews()
        {
            // Scheduled against an element we're about to destroy — drop it
            // before the elements go, not after.
            _focusClearTimer?.Pause();
            _focusClearTimer = null;
            _focusRow = _focusCol = -1;

            foreach (var v in _views)
            {
                if (v.host != null) Destroy(v.host);
                if (v.settings != null) Destroy(v.settings);
            }
            _views.Clear();
        }

        // A single empty ThemeStyleSheet shared by every view, purely to keep the
        // PanelSettings' themeStyleSheet non-null. Shared rather than per-view because
        // it carries no per-view state and would otherwise be recreated on every
        // rebuild; destroyed once in OnDestroy. hideFlags keeps it out of any save.
        ThemeStyleSheet _fallbackTheme;
        ThemeStyleSheet FallbackTheme
        {
            get
            {
                if (_fallbackTheme == null)
                {
                    _fallbackTheme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                    _fallbackTheme.name = "MidiStatusDrawer_FallbackTheme";
                    _fallbackTheme.hideFlags = HideFlags.DontSave;
                }
                return _fallbackTheme;
            }
        }

        DrawerView BuildView(int displayIndex)
        {
            var view = new DrawerView();

            // Own PanelSettings per view — targetDisplay is a PanelSettings property.
            view.settings = ScriptableObject.CreateInstance<PanelSettings>();
            view.settings.name = $"MidiStatusDrawer_PanelSettings_D{displayIndex}";
            view.settings.targetDisplay = displayIndex;

            // Without this the panel defaults to ConstantPixelSize, where every
            // px below is a literal screen pixel — the drawer then overflows a
            // small Game view and ignores resolution entirely. ScaleWithScreenSize
            // turns those into *design* units against ReferenceResolution, so the
            // whole drawer scales as one piece and stays proportional.
            //
            // Expand (never Shrink) guarantees the canvas is at least the
            // reference size on both axes, so the UI is never cropped.
            view.settings.scaleMode           = PanelScaleMode.ScaleWithScreenSize;
            view.settings.referenceResolution = DerivedReferenceResolution;
            view.settings.screenMatchMode     = PanelScreenMatchMode.Expand;
            // A PanelSettings with a null themeStyleSheet logs a warning on every
            // build ("UI will not render properly"). The drawer styles every element
            // itself and reads no theme USS variables — which is why it renders fine
            // regardless — so an empty fallback theme satisfies the check with no
            // asset to ship. An author-supplied sheet still wins.
            view.settings.themeStyleSheet = _themeStyleSheet != null ? _themeStyleSheet : FallbackTheme;

            // Host GameObject per view (child of this) so each has its own UIDocument.
            view.host = new GameObject($"Drawer_Display{displayIndex}");
            view.host.transform.SetParent(transform, false);
            view.doc = view.host.AddComponent<UIDocument>();
            view.doc.panelSettings = view.settings;
            view.doc.sortingOrder = 1000f;

            BuildTree(view);
            return view;
        }

        // ─── UI tree ─────────────────────────────────────────────────────
        void BuildTree(DrawerView view)
        {
            // Root is a flex row that centers the drawer vertically and anchors
            // it horizontally per DrawerPlacement. Using flow layout rather than
            // absolute offsets keeps the centering aspect-ratio independent.
            var root = view.doc.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;      // vertical centering
            root.pickingMode = PickingMode.Ignore;
            view.root = root;

            var drawer = new VisualElement { name = "drawer" };
            drawer.style.width = DrawerWidth;
            // Never exceed the display on either axis.
            drawer.style.maxWidth = new Length(100, LengthUnit.Percent);
            drawer.style.maxHeight = new Length(100, LengthUnit.Percent);
            drawer.style.flexShrink = 0;
            // Symmetric so ScreenCentered stays truly centered; the right margin
            // doubles as the edge gap in RightCentered.
            drawer.style.marginLeft = 12;
            drawer.style.marginRight = 12;
            drawer.style.paddingTop = DrawerPadY;
            drawer.style.paddingBottom = DrawerPadY;
            drawer.style.paddingLeft = DrawerPadX;
            drawer.style.paddingRight = DrawerPadX;
            // Leave drawer background unassigned so it stays fully transparent
            // regardless of any inherited runtime-theme styling.
            drawer.style.borderTopLeftRadius = 6;
            drawer.style.borderTopRightRadius = 6;
            drawer.style.borderBottomLeftRadius = 6;
            drawer.style.borderBottomRightRadius = 6;
            drawer.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("translate") };
            drawer.style.transitionDuration = new List<TimeValue> { new TimeValue(200, TimeUnit.Millisecond) };
            drawer.pickingMode = PickingMode.Ignore;
            view.drawer = drawer;
            root.Add(drawer);

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Column;
            body.style.alignItems = Align.Stretch;
            body.style.flexGrow = 1;
            drawer.Add(body);

            if (IsRadial)
            {
                body.Add(BuildRadial1Section(view));
            }
            else
            {
                if (_showMf64)    body.Add(BuildMf64Section(view));   // top
                if (_showMidiMix) body.Add(BuildMixSection(view));    // bottom
            }

            if (IsRadial)
            {
                // Pinned to the display's bottom-left rather than stacked under the
                // square: the radial layout is a disc, so flowing a full-width strip
                // beneath it wastes the corners and drags the square upward. On the
                // root, not the drawer, so it is positioned against the display —
                // which is why ApplyHiddenState has to hide it explicitly.
                var holder = new VisualElement { name = "radial-message" };
                holder.style.position = Position.Absolute;
                holder.style.left   = _radialMessagePadding;
                holder.style.bottom = _radialMessagePadding;
                holder.pickingMode = PickingMode.Ignore;

                view.messageLabel = MakeMessageLabel();
                holder.Add(view.messageLabel);
                view.floatingMessage = holder;
                root.Add(holder);
            }
            else if (!_showMidiMix)
            {
                // The strip normally lives in the MIDI Mix utility row; with that
                // section hidden it would vanish, so give it its own panel.
                var msgSection = MakeSection("Message");
                view.messageLabel = MakeMessageLabel();
                msgSection.Add(view.messageLabel);
                body.Add(msgSection);
            }
        }

        // ─── Radial 1 geometry ───────────────────────────────────────────
        // Every radius and size is a fraction of the square's half-side, measured
        // from radial_A_centered.svg. They are the tuning surface for the layout —
        // the SVG is authoritative for geometry only, never for colour.

        /// <summary>Pad ring radii, indexed by ring: 0 = the outer 8×8 border,
        /// 3 = the centre 2×2.</summary>
        static readonly float[] PadRingRadius = { 0.3957f, 0.2957f, 0.1957f, 0.0957f };

        /// <summary>Pad diameters per ring. Inner rings are slightly smaller because
        /// they have less circumference to share — expected, not a defect.</summary>
        static readonly float[] PadRingSize   = { 0.0457f, 0.0500f, 0.0522f, 0.0543f };

        static readonly float[] KnobArcRadius = { 0.4870f, 0.5478f, 0.6087f };
        const float KnobArcStroke    = 0.0152f;
        const float FaderArcRadius   = 0.6870f;
        const float FaderArcStroke   = 0.0239f;   // visibly thicker than a knob arc
        const float MasterArcRadius  = 0.7652f;
        const float MasterArcStroke  = 0.0283f;   // thickest of the three
        const float ToggleRingRadius = 0.8522f;
        const float ToggleSize       = 0.0261f;
        const float ChannelLabelRadius = 0.9043f;

        /// <summary>Arc length of one channel's slice. The slice is 45° wide; the arc
        /// is 39°, leaving a 6° gap so neighbouring channels read apart.</summary>
        const float SliceSweepDeg = 39f;

        /// <summary>Angular offset of the Mute / Rec-Arm pair either side of their
        /// channel angle, so the outer ring still reads by channel.</summary>
        const float TogglePairOffsetDeg = 7f;

        /// <summary>Channel <paramref name="c"/> (0-based) sits at this UI angle.
        /// Channel 1 is at the top and channels advance clockwise — y grows downward,
        /// so -90° is straight up.</summary>
        static float ChannelAngle(int c) => -90f + c * 45f;

        /// <summary>Absolutely positions <paramref name="el"/> with its *centre* at
        /// polar (<paramref name="r"/>, <paramref name="deg"/>) from the square's
        /// centre. UI degrees throughout: y down, so increasing angle runs clockwise.</summary>
        static void PlacePolar(VisualElement el, float cx, float cy, float r, float deg, float size)
        {
            float rad = deg * Mathf.Deg2Rad;
            el.style.position = Position.Absolute;
            el.style.width  = size;
            el.style.height = size;
            el.style.left = cx + Mathf.Cos(rad) * r - size * 0.5f;
            el.style.top  = cy + Mathf.Sin(rad) * r - size * 0.5f;
        }

        /// <summary>
        /// Walks one square ring of the 8×8 grid clockwise from its top-left cell,
        /// yielding 1-based (row, col). Ring 0 is the outer border (28 cells), 3 the
        /// centre 2×2 (4 cells). Walking rather than sorting is what preserves
        /// ring-neighbour adjacency, so the circle still reads like the grid.
        /// </summary>
        static System.Collections.Generic.IEnumerable<(int row, int col)> WalkRing(int ring)
        {
            int lo = ring + 1, hi = 8 - ring;
            for (int c = lo; c <= hi; c++) yield return (lo, c);          // top, L→R
            for (int r = lo + 1; r <= hi; r++) yield return (r, hi);      // right, T→B
            for (int c = hi - 1; c >= lo; c--) yield return (hi, c);      // bottom, R→L
            for (int r = hi - 1; r >= lo + 1; r--) yield return (r, lo);  // left, B→T
        }

        /// <summary>
        /// Radial 1 — concentric rings, centre outward. Populates the same
        /// <see cref="DrawerView"/> arrays as the linear builder with the same widget
        /// types, so every event handler works unchanged; only the geometry differs.
        ///
        /// Section visibility is honoured by skipping bands, with radii left fixed:
        /// hiding the mixer leaves the pad rings where they are rather than re-flowing
        /// the stack, which would need a second radius table per combination.
        /// </summary>
        VisualElement BuildRadial1Section(DrawerView view)
        {
            var section = MakeSection("Radial");
            section.style.alignSelf  = Align.Stretch;
            section.style.alignItems = Align.Center;
            view.radialSection = section;

            // Explicit square, exactly like the pad grid: arithmetic from a constant,
            // never measured. Absolute children position against this box.
            var square = new VisualElement { name = "radial" };
            square.style.width  = RadialSideDesign;
            square.style.height = RadialSideDesign;
            square.style.flexShrink = 0;
            square.pickingMode = PickingMode.Ignore;

            const float R  = RadialSideDesign * 0.5f;
            const float cx = R, cy = R;

            if (_showMf64)
            {
                var cfg = _btnRouter != null ? _btnRouter.Config : null;

                for (int ring = 0; ring < 4; ring++)
                {
                    foreach (var (row, col) in WalkRing(ring))
                    {
                        int note = MidiFighter64InputMap.ToNote(row, col);
                        var btn  = MidiFighter64InputMap.FromNote(note);
                        var mode = cfg != null ? cfg.GetMode(btn) : MidiFighterButtonMode.Button;

                        var cell = new PadCell
                        {
                            CellMode = mode == MidiFighterButtonMode.Toggle
                                     ? PadCell.Mode.Toggle : PadCell.Mode.Button,
                            FillColor   = Colors.AdaptLed(_padRawFill[btn.linearIndex]),
                            StrokeColor = Colors.PadStroke,
                        };
                        view.pads[btn.linearIndex] = cell;
                        square.Add(cell);
                    }
                }
            }

            if (_showMidiMix)
            {
                for (int ch = 0; ch < 8; ch++)
                {
                    float mid   = ChannelAngle(ch);
                    float start = mid - SliceSweepDeg * 0.5f;   // fill grows clockwise from here

                    for (int row = 0; row < 3; row++)
                    {
                        var arc = MakeArc(KnobArcRadius[row] * R, start, SliceSweepDeg,
                                          KnobArcStroke * R);
                        view.knobArcs[ch, row] = arc;
                        square.Add(arc);
                    }

                    var fader = MakeArc(FaderArcRadius * R, start, SliceSweepDeg,
                                        FaderArcStroke * R);
                    view.faderArcs[ch] = fader;
                    square.Add(fader);

                    // Mute leads its channel angle, Rec-Arm trails it.
                    var mute = new PadCell { CellMode = PadCell.Mode.Toggle, FillColor = Colors.Ink,
                                             StrokeColor = Colors.PadStroke };
                    PlacePolar(mute, cx, cy, ToggleRingRadius * R,
                               mid - TogglePairOffsetDeg, ToggleSize * R);
                    view.mutes[ch] = mute;
                    square.Add(mute);

                    var rec = new PadCell { CellMode = PadCell.Mode.Toggle, FillColor = Colors.Ink,
                                            StrokeColor = Colors.PadStroke };
                    PlacePolar(rec, cx, cy, ToggleRingRadius * R,
                               mid + TogglePairOffsetDeg, ToggleSize * R);
                    view.recArms[ch] = rec;
                    square.Add(rec);

                    // Channel caption, outside the toggle ring. Given a generous box
                    // and centred text, since a label's own width isn't known here.
                    var label = MakeLabel($"{ch + 1}", CaptionFontSize);
                    float labelBox = ToggleSize * R * 4f;
                    PlacePolar(label, cx, cy, ChannelLabelRadius * R, mid, labelBox);
                    label.style.height = StyleKeyword.Auto;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    square.Add(label);
                }

                // Master: the only continuous ring among segmented arcs, which is what
                // distinguishes it — no separate colour needed.
                view.masterArc = MakeArc(MasterArcRadius * R, -90f, 360f, MasterArcStroke * R);
                square.Add(view.masterArc);
            }

            // Solo modifier and Bank L/R are deliberately not built in radial layouts.
            // The handlers null-check, so leaving those view fields null is enough.

            section.Add(square);
            // Geometry comes from the shared placer so the build path and the live
            // sliders can never disagree about where a pad belongs.
            PlaceRadialPads(view);
            return section;
        }

        /// <summary>
        /// Positions the 64 pads on their four rings. Split out from the builder so
        /// the Pad Size and Ring Spread sliders can move them without a rebuild.
        ///
        /// Ring 0 is the crowding constraint: 28 pads share the longest circumference
        /// but still get the least arc each, so it is what touches first when Pad Size
        /// rises or Ring Spread falls.
        /// </summary>
        void PlaceRadialPads(DrawerView v)
        {
            // Hard guard: in Linear1 these same PadCells are flex children of the row
            // containers, and absolutely positioning them would collapse the grid.
            if (!IsRadial) return;

            const float R = RadialSideDesign * 0.5f;

            for (int ring = 0; ring < 4; ring++)
            {
                var cells = new System.Collections.Generic.List<(int row, int col)>(WalkRing(ring));
                float rr   = PadRingRadius[ring] * _radialRingSpread * R;
                float size = PadRingSize[ring]   * _radialPadScale   * R;

                for (int i = 0; i < cells.Count; i++)
                {
                    var (row, col) = cells[i];
                    var btn  = MidiFighter64InputMap.FromNote(MidiFighter64InputMap.ToNote(row, col));
                    var cell = v.pads[btn.linearIndex];
                    if (cell == null) continue;   // MF64 section hidden

                    // Evenly distributed over the full circle, starting at the top.
                    PlacePolar(cell, R, R, rr,
                               -90f + _radialPadRotation + (i / (float)cells.Count) * 360f,
                               size);
                }
            }
        }

        /// <summary>One radial arc, inked from the current palette so it lands
        /// correctly even before <see cref="ApplyTheme"/>'s pass.</summary>
        RadialArc MakeArc(float radius, float startDeg, float sweepDeg, float stroke)
        {
            var arc = new RadialArc
            {
                Radius     = radius,
                StartDeg   = startDeg,
                SweepDeg   = sweepDeg,
                BaseStroke = stroke,
                StrokeScale = _strokeWeight,
            };
            arc.SetInk(Colors.Ink, Colors.Track);
            return arc;
        }

        VisualElement BuildMf64Section(DrawerView view)
        {
            var section = MakeSection("Midi Fighter 64");
            section.style.marginBottom = Mf64SectionGap;
            section.style.alignSelf = Align.Stretch;
            section.style.alignItems = Align.Center;

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Column;
            grid.pickingMode = PickingMode.Ignore;

            // Explicit square, computed from constants — no percentage width and
            // no GeometryChangedEvent. Both of those made the grid's size depend
            // on a layout pass that had not settled yet, which is what stretched
            // the pads into ellipses (and, when the guard re-fired every pass,
            // hard-froze the editor).
            grid.style.width  = GridSide;
            grid.style.height = GridSide;
            grid.style.flexShrink = 0;   // never compress; stay square

            var cfg = _btnRouter != null ? _btnRouter.Config : null;

            for (int row = 1; row <= 8; row++)
            {
                var rowEl = new VisualElement();
                rowEl.style.flexDirection = FlexDirection.Row;
                rowEl.style.flexGrow = 1;
                rowEl.style.flexBasis = 0;
                rowEl.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("flex-grow") };
                view.mf64Rows[row - 1] = rowEl;
                for (int col = 1; col <= 8; col++)
                {
                    int note = MidiFighter64InputMap.ToNote(row, col);
                    var btn  = MidiFighter64InputMap.FromNote(note);
                    var mode = cfg != null ? cfg.GetMode(btn) : MidiFighterButtonMode.Button;

                    var cell = new PadCell
                    {
                        CellMode = mode == MidiFighterButtonMode.Toggle ? PadCell.Mode.Toggle : PadCell.Mode.Button,
                    };
                    cell.style.flexGrow = 1;
                    cell.style.flexBasis = 0;
                    // Equal right/bottom margins keep the cell box square inside
                    // a square grid — which is what makes the pad render round.
                    cell.style.marginRight = CellMargin;
                    cell.style.marginBottom = CellMargin;
                    cell.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("flex-grow") };
                    view.pads[btn.linearIndex] = cell;
                    rowEl.Add(cell);
                }
                grid.Add(rowEl);
            }

            section.Add(grid);
            return section;
        }

        VisualElement BuildMixSection(DrawerView view)
        {
            var section = MakeSection("Akai MIDI Mix");
            section.style.alignSelf = Align.Stretch;
            section.style.alignItems = Align.Stretch;
            // No minWidth: the mix section must resolve to exactly the same
            // content width as the MF64 section or their 8 columns won't line up.

            // Sizes come from the class-level constants above so the height
            // budget stays in sync — see MixSectionHeight.
            const float KNOB_SIZE    = KnobSize;
            const float PAD_SIZE     = MixPadSize;
            const float FADER_WIDTH  = FaderWidth;
            const float FADER_HEIGHT = FaderHeight;

            // 8 channel strips, identical width — every widget in channel N shares
            // the same horizontal center automatically.
            var stripsRow = new VisualElement();
            stripsRow.style.flexDirection = FlexDirection.Row;
            stripsRow.style.alignSelf = Align.Stretch;

            for (int ch = 0; ch < 8; ch++)
            {
                var strip = new VisualElement();
                strip.style.flexDirection = FlexDirection.Column;
                strip.style.alignItems = Align.Center;
                // Must match the MF64 pad cell's flex + margin exactly, or the
                // mixer columns drift out of line with the pad grid columns.
                strip.style.flexGrow = 1;
                strip.style.flexBasis = 0;
                strip.style.marginRight = CellMargin;

                for (int rowIdx = 0; rowIdx < 3; rowIdx++)
                {
                    var knob = new KnobDisplay();
                    knob.style.width = KNOB_SIZE;
                    knob.style.height = KNOB_SIZE;
                    knob.style.marginBottom = KnobGap;
                    knob.style.opacity = SeenScale(_knobsSeen[ch, rowIdx]);
                    view.knobs[ch, rowIdx] = knob;
                    strip.Add(knob);
                }

                var mute = MakeSizedPad(PadCell.Mode.Toggle, Colors.Ink, PAD_SIZE);
                mute.style.marginBottom = 4;
                view.mutes[ch] = mute;
                strip.Add(mute);

                var rec = MakeSizedPad(PadCell.Mode.Toggle, Colors.Ink, PAD_SIZE);
                rec.style.marginBottom = 6;
                view.recArms[ch] = rec;
                strip.Add(rec);

                VisualElement bar;
                var faderCol = MakeFader(out bar, _fadersSeen[ch]);
                faderCol.style.width = FADER_WIDTH;
                faderCol.style.height = FADER_HEIGHT;
                view.faderBars[ch]   = bar;
                view.faderTracks[ch] = faderCol;
                strip.Add(faderCol);

                stripsRow.Add(strip);
            }
            section.Add(stripsRow);

            // Master fader — horizontal bar spanning full width.
            var masterWrap = new VisualElement();
            masterWrap.style.flexDirection = FlexDirection.Column;
            masterWrap.style.marginTop = 10;
            masterWrap.style.alignSelf = Align.Stretch;

            var masterLabel = MakeLabel("MASTER", CaptionFontSize);
            masterLabel.style.marginBottom = 3;
            masterWrap.Add(masterLabel);

            var masterTrack = new VisualElement();
            masterTrack.style.height = 10;
            masterTrack.style.backgroundColor = Colors.Track;
            masterTrack.style.borderTopLeftRadius = 3;
            masterTrack.style.borderTopRightRadius = 3;
            masterTrack.style.borderBottomLeftRadius = 3;
            masterTrack.style.borderBottomRightRadius = 3;
            masterTrack.style.opacity = SeenScale(_masterFaderSeen);
            masterTrack.style.overflow = Overflow.Hidden;

            view.masterFaderBar = new VisualElement();
            view.masterFaderBar.style.height = new Length(100, LengthUnit.Percent);
            view.masterFaderBar.style.width = 0;
            view.masterFaderBar.style.backgroundColor = Colors.Ink;
            view.masterFaderBar.style.borderTopLeftRadius = 3;
            view.masterFaderBar.style.borderTopRightRadius = 3;
            view.masterFaderBar.style.borderBottomLeftRadius = 3;
            view.masterFaderBar.style.borderBottomRightRadius = 3;
            masterTrack.Add(view.masterFaderBar);
            view.masterFaderTrack = masterTrack;
            masterWrap.Add(masterTrack);

            section.Add(masterWrap);

            // Solo modifier + message display + bank L/R
            var utilRow = new VisualElement();
            utilRow.style.flexDirection = FlexDirection.Row;
            utilRow.style.alignItems = Align.Center;
            utilRow.style.alignSelf = Align.Stretch;
            utilRow.style.marginTop = 8;
            view.soloModifier = MakeSizedPad(PadCell.Mode.Button, Colors.Ink, PAD_SIZE);
            view.bankLeft     = MakeSizedPad(PadCell.Mode.Button, Colors.Ink, PAD_SIZE);
            view.bankRight    = MakeSizedPad(PadCell.Mode.Button, Colors.Ink, PAD_SIZE);
            utilRow.Add(WithCaption(view.soloModifier, "SOLO"));

            // Message display — fills space between SOLO and Bank L/R
            var msg = MakeMessageLabel();
            msg.style.marginLeft = 8;
            msg.style.marginRight = 8;
            view.messageLabel = msg;
            utilRow.Add(msg);

            var bankGroup = new VisualElement();
            bankGroup.style.flexDirection = FlexDirection.Row;
            bankGroup.Add(WithCaption(view.bankLeft, "◄"));
            var sp = new VisualElement(); sp.style.width = 6; bankGroup.Add(sp);
            bankGroup.Add(WithCaption(view.bankRight, "►"));
            utilRow.Add(bankGroup);

            section.Add(utilRow);

            return section;
        }

        PadCell MakeSizedPad(PadCell.Mode mode, Color fill, float size)
        {
            var cell = new PadCell { CellMode = mode, FillColor = fill, StrokeColor = Colors.PadStroke };
            cell.style.width = size;
            cell.style.height = size;
            return cell;
        }

        VisualElement MakeFader(out VisualElement bar, bool seen)
        {
            var col = new VisualElement();
            col.style.backgroundColor = Colors.Track;
            col.style.borderTopLeftRadius = 3;
            col.style.borderTopRightRadius = 3;
            col.style.borderBottomLeftRadius = 3;
            col.style.borderBottomRightRadius = 3;
            col.style.justifyContent = Justify.FlexEnd;
            col.style.opacity = SeenScale(seen);

            bar = new VisualElement();
            bar.style.height = 0;
            bar.style.backgroundColor = Colors.Ink;
            bar.style.borderTopLeftRadius = 3;
            bar.style.borderTopRightRadius = 3;
            bar.style.borderBottomLeftRadius = 3;
            bar.style.borderBottomRightRadius = 3;
            col.Add(bar);
            return col;
        }

        VisualElement WithCaption(VisualElement child, string caption)
        {
            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Column;
            wrap.style.alignItems = Align.Center;
            wrap.Add(child);
            var lbl = MakeLabel(caption, CaptionFontSize);
            lbl.style.marginTop = 2;
            wrap.Add(lbl);
            return wrap;
        }

        VisualElement MakeSection(string title)
        {
            // Title is unused — kept for API compatibility with the existing callers.
            var s = new VisualElement();
            s.AddToClassList(SectionClass);
            s.style.backgroundColor = Colors.SectionBg;
            s.style.paddingTop = SectionPad;
            s.style.paddingBottom = SectionPad;
            s.style.paddingLeft = SectionPad;
            s.style.paddingRight = SectionPad;
            s.style.borderTopLeftRadius = 4;
            s.style.borderTopRightRadius = 4;
            s.style.borderBottomLeftRadius = 4;
            s.style.borderBottomRightRadius = 4;
            return s;
        }

        /// <summary>The most-recent-event readout. Shared by the MIDI Mix
        /// utility row and the standalone strip used when Mix is hidden.</summary>
        Label MakeMessageLabel()
        {
            var msg = MakeLabel("—", MessageFontSize);
            // The strip has its own ink and background, so take it out of the
            // caption class or a theme change would restyle it twice.
            msg.RemoveFromClassList(LabelClass);
            msg.AddToClassList(MessageClass);
            msg.style.flexGrow = 1;
            msg.style.paddingLeft = 8;
            msg.style.paddingRight = 8;
            msg.style.paddingTop = 4;
            msg.style.paddingBottom = 4;
            msg.style.backgroundColor = Colors.MessageBg;
            msg.style.borderTopLeftRadius = 3;
            msg.style.borderTopRightRadius = 3;
            msg.style.borderBottomLeftRadius = 3;
            msg.style.borderBottomRightRadius = 3;
            msg.style.unityTextAlign = TextAnchor.MiddleLeft;
            msg.style.color = Colors.MessageText;
            return msg;
        }

        Label MakeLabel(string text, int size)
        {
            var l = new Label(text);
            l.AddToClassList(LabelClass);
            l.style.color = Colors.Label;
            l.style.fontSize = size;
            l.style.unityFont = new StyleFont(UiFont);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        void SetMessage(string text)
        {
            var upper = text?.ToUpperInvariant() ?? string.Empty;
            foreach (var v in _views)
                if (v.messageLabel != null) v.messageLabel.text = upper;
        }

        const int SlideMs = 200;

        static StyleTranslate OffscreenTranslate =>
            new StyleTranslate(new Translate(new Length(120, LengthUnit.Percent), 0, 0));
        static StyleTranslate HomeTranslate =>
            new StyleTranslate(new Translate(0, 0, 0));

        void ApplyHiddenState(bool instant)
        {
            foreach (var v in _views)
            {
                // Lives on the root, so the drawer's slide never covers it. No
                // animation — there is nothing offscreen for it to slide to.
                if (v.floatingMessage != null)
                    v.floatingMessage.style.display = _hidden ? DisplayStyle.None : DisplayStyle.Flex;

                var d = v.drawer;
                if (d == null) continue;

                if (instant)
                {
                    d.style.transitionDuration = new List<TimeValue> { new TimeValue(0, TimeUnit.Millisecond) };
                    d.schedule.Execute(() =>
                    {
                        d.style.transitionDuration = new List<TimeValue> { new TimeValue(SlideMs, TimeUnit.Millisecond) };
                    }).ExecuteLater(1);
                }

                if (_hidden)
                {
                    d.style.translate = OffscreenTranslate;

                    // The slide is 120% of the drawer's OWN width, so it only
                    // clears the viewport when the drawer is already flush right.
                    // Under ScreenCentered it stops short and stays visible, and
                    // no fixed percentage can be correct for every placement and
                    // display size. Take it out of layout once the slide is done.
                    if (instant)
                        d.style.display = DisplayStyle.None;
                    else
                        d.schedule.Execute(() =>
                        {
                            if (_hidden) d.style.display = DisplayStyle.None;
                        }).ExecuteLater(SlideMs);
                }
                else
                {
                    // Re-enter layout still offset, then slide home next frame —
                    // a transition can't animate from a display:none element.
                    d.style.display = DisplayStyle.Flex;
                    // Report once the drawer is visible and settled; hidden it
                    // has no resolved layout to measure.
                    if (_logLayoutDiagnostics)
                        d.schedule.Execute(LogLayout).ExecuteLater(SlideMs + 200);
                    if (instant)
                    {
                        d.style.translate = HomeTranslate;
                    }
                    else
                    {
                        d.style.translate = OffscreenTranslate;
                        d.schedule.Execute(() =>
                        {
                            if (!_hidden) d.style.translate = HomeTranslate;
                        }).ExecuteLater(1);
                    }
                }
            }
        }

        // ─── MF64 handlers (fan out to all views) ────────────────────────
        void HandleToggle(GridButton btn, bool state)
        {
            var color = ResolvePadColor(btn, fallbackGlobal: _btnRouter != null ? _btnRouter.ToggleOnColor : MidiFighterLEDColor.White);
            _padRawFill[btn.linearIndex] = color;
            var shown = Colors.AdaptLed(color);
            foreach (var v in _views)
            {
                var cell = v.pads[btn.linearIndex];
                if (cell == null) continue;
                cell.CellMode = PadCell.Mode.Toggle;
                cell.FillColor = shown;
                cell.Active = state;
            }
            if (_enableMf64Fisheye)
            {
                FocusMf64Pad(btn.row, btn.col);
                ScheduleFocusClear(FocusHoldMs);
            }
            SetMessage($"MF64 R{btn.row}C{btn.col} toggle {(state ? "ON" : "OFF")}");
        }

        void HandleButtonPress(GridButton btn, float velocity)
        {
            var color = ResolvePadColor(btn, fallbackGlobal: _btnRouter != null ? _btnRouter.ButtonDownColor : MidiFighterLEDColor.BrightPink);
            _padRawFill[btn.linearIndex] = color;
            var shown = Colors.AdaptLed(color);
            foreach (var v in _views)
            {
                var cell = v.pads[btn.linearIndex];
                if (cell == null) continue;
                cell.CellMode = PadCell.Mode.Button;
                cell.FillColor = shown;
                cell.Active = true;
            }
            if (_enableMf64Fisheye)
            {
                _focusClearTimer?.Pause();
                FocusMf64Pad(btn.row, btn.col);
            }
            SetMessage($"MF64 R{btn.row}C{btn.col} press  v={velocity:0.00}");
        }

        // ─── Fisheye focus impl ──────────────────────────────────────────
        // Grow the active pad's row (height) and its column (across all rows,
        // width) by Mf64FisheyeScale so the pad stays proportional. Timing
        // is asymmetric: snap in ~100ms, ease out over ~700ms.
        static readonly List<TimeValue> FocusInDuration  = new() { new TimeValue(100, TimeUnit.Millisecond) };
        static readonly List<TimeValue> FocusOutDuration = new() { new TimeValue(700, TimeUnit.Millisecond) };
        static readonly List<EasingFunction> FocusInEase  = new() { new EasingFunction(EasingMode.EaseOutCubic) };
        static readonly List<EasingFunction> FocusOutEase = new() { new EasingFunction(EasingMode.EaseOutCirc) };

        void FocusMf64Pad(int row1Based, int col1Based)
        {
            // Fisheye is flex-grow on the row containers, which only exist in Linear1;
            // a radial builder never populates mf64Rows. Guarding here rather than at
            // every call site keeps the press handlers layout-agnostic.
            if (IsRadial) return;

            int activeRow = row1Based - 1;
            int activeCol = col1Based - 1;
            _focusRow = activeRow;
            _focusCol = activeCol;

            foreach (var v in _views)
            {
                for (int r = 0; r < 8; r++)
                {
                    var rowEl = v.mf64Rows[r];
                    if (rowEl != null)
                    {
                        rowEl.style.transitionDuration = FocusInDuration;
                        rowEl.style.transitionTimingFunction = FocusInEase;
                        rowEl.style.flexGrow = (r == activeRow) ? _mf64FisheyeScale : 1f;
                    }
                    for (int c = 0; c < 8; c++)
                    {
                        var cell = v.pads[r * 8 + c];
                        if (cell == null) continue;
                        cell.style.transitionDuration = FocusInDuration;
                        cell.style.transitionTimingFunction = FocusInEase;
                        cell.style.flexGrow = (c == activeCol) ? _mf64FisheyeScale : 1f;
                    }
                }
            }
        }

        void ClearMf64Focus()
        {
            if (_focusRow < 0 && _focusCol < 0) return;
            _focusRow = _focusCol = -1;

            foreach (var v in _views)
            {
                for (int r = 0; r < 8; r++)
                {
                    var rowEl = v.mf64Rows[r];
                    if (rowEl != null)
                    {
                        rowEl.style.transitionDuration = FocusOutDuration;
                        rowEl.style.transitionTimingFunction = FocusOutEase;
                        rowEl.style.flexGrow = 1f;
                    }
                    for (int c = 0; c < 8; c++)
                    {
                        var cell = v.pads[r * 8 + c];
                        if (cell == null) continue;
                        cell.style.transitionDuration = FocusOutDuration;
                        cell.style.transitionTimingFunction = FocusOutEase;
                        cell.style.flexGrow = 1f;
                    }
                }
            }
        }

        void ScheduleFocusClear(int delayMs)
        {
            _focusClearTimer?.Pause();
            // schedule needs a live VisualElement — grab the first available drawer.
            VisualElement anchor = null;
            foreach (var v in _views) { if (v.drawer != null) { anchor = v.drawer; break; } }
            if (anchor == null) return;
            _focusClearTimer = anchor.schedule.Execute(ClearMf64Focus);
            _focusClearTimer.ExecuteLater(delayMs);
        }

        /// <summary>
        /// Resolves the on-screen color for a pad using the same priority the
        /// router uses for hardware LEDs: per-pad config color wins, else the
        /// caller-supplied global mode color.
        /// </summary>
        Color ResolvePadColor(GridButton btn, MidiFighterLEDColor fallbackGlobal)
        {
            var cfg = _btnRouter != null ? _btnRouter.Config : null;
            if (cfg != null)
            {
                var perPad = cfg.GetColor(btn);
                if (perPad != MidiFighterLEDColor.Off) return perPad.ToUnityColor();
            }
            return fallbackGlobal.ToUnityColor();
        }

        void HandleButtonRelease(GridButton btn)
        {
            foreach (var v in _views)
            {
                var cell = v.pads[btn.linearIndex];
                if (cell != null) cell.Active = false;
            }
            ClearMf64Focus();
            SetMessage($"MF64 R{btn.row}C{btn.col} release");
        }

        // ─── MIDI Mix handlers (fan out) ─────────────────────────────────
        void HandleMixKnob(int channel, int row, float value)
        {
            int ch = channel - 1, r = row - 1;
            if (ch < 0 || ch > 7 || r < 0 || r > 2) return;
            bool wasSeen = _knobsSeen[ch, r];
            _knobsSeen[ch, r] = true;
            foreach (var v in _views)
            {
                // Each layout populates one of these and leaves the other null, so
                // both are guarded independently — an early `continue` on the linear
                // widget would silently skip the radial one.
                var k = v.knobs[ch, r];
                if (k != null)
                {
                    k.Value = value;
                    if (!wasSeen) k.style.opacity = SeenScale(true);
                }
                var arc = v.knobArcs[ch, r];
                if (arc != null)
                {
                    arc.SetValue(value);
                    if (!wasSeen) arc.style.opacity = SeenScale(true);
                }
            }
            SetMessage($"Mix Knob R{row} Ch{channel}  {value:0.00}");
        }

        void HandleMixChannelFader(int channel, float value)
        {
            int ch = channel - 1;
            if (ch < 0 || ch > 7) return;
            bool wasSeen = _fadersSeen[ch];
            _fadersSeen[ch] = true;
            foreach (var v in _views)
            {
                var bar = v.faderBars[ch];
                if (bar != null)
                {
                    bar.style.height = new Length(Mathf.Clamp01(value) * 100f, LengthUnit.Percent);
                    if (!wasSeen && v.faderTracks[ch] != null)
                        v.faderTracks[ch].style.opacity = SeenScale(true);
                }
                var arc = v.faderArcs[ch];
                if (arc != null)
                {
                    arc.SetValue(value);
                    if (!wasSeen) arc.style.opacity = SeenScale(true);
                }
            }
            SetMessage($"Mix Fader Ch{channel}  {value:0.00}");
        }

        void HandleMixMasterFader(float value)
        {
            bool wasSeen = _masterFaderSeen;
            _masterFaderSeen = true;
            foreach (var v in _views)
            {
                if (v.masterFaderBar != null)
                {
                    v.masterFaderBar.style.width = new Length(Mathf.Clamp01(value) * 100f, LengthUnit.Percent);
                    if (!wasSeen && v.masterFaderTrack != null)
                        v.masterFaderTrack.style.opacity = SeenScale(true);
                }
                if (v.masterArc != null)
                {
                    v.masterArc.SetValue(value);
                    if (!wasSeen) v.masterArc.style.opacity = SeenScale(true);
                }
            }
            SetMessage($"Mix Master  {value:0.00}");
        }

        // isOn is the router's latched state when LatchMute / LatchRecArm are on
        // (the default) and the raw button state when they aren't. ON / OFF reads
        // correctly either way, where DOWN / UP only described the momentary case.
        void HandleMixMute(int channel, bool isOn)
        {
            int ch = channel - 1;
            if (ch < 0 || ch > 7) return;
            foreach (var v in _views)
                if (v.mutes[ch] != null) v.mutes[ch].Active = isOn;
            SetMessage($"Mix Mute Ch{channel}  {(isOn ? "ON" : "OFF")}");
        }

        void HandleMixRecArm(int channel, bool isOn)
        {
            int ch = channel - 1;
            if (ch < 0 || ch > 7) return;
            foreach (var v in _views)
                if (v.recArms[ch] != null) v.recArms[ch].Active = isOn;
            SetMessage($"Mix RecArm Ch{channel}  {(isOn ? "ON" : "OFF")}");
        }

        void HandleMixSoloModifier(bool isDown)
        {
            foreach (var v in _views)
                if (v.soloModifier != null) v.soloModifier.Active = isDown;
            SetMessage($"Mix SOLO modifier  {(isDown ? "DOWN" : "UP")}");
        }

        void HandleMixBankLeft()  { foreach (var v in _views) FlashPad(v.bankLeft);  SetMessage("Mix Bank ◄"); }
        void HandleMixBankRight() { foreach (var v in _views) FlashPad(v.bankRight); SetMessage("Mix Bank ►"); }

        void FlashPad(PadCell cell)
        {
            if (cell == null) return;
            cell.Active = true;
            cell.schedule.Execute(() => cell.Active = false).ExecuteLater(120);
        }
    }
}
