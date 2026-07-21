using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MidiFighter64.Samples
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

        // ─── Colors / geometry ────────────────────────────────────────────
        static readonly Color BgSection     = new Color(0.055f, 0.055f, 0.060f, 0.30f); // semi-transparent light dark tint
        static readonly Color BarFillColor  = Color.white;                              // bright-white highlight
        static readonly Color BarTrackColor = new Color(0.14f,  0.14f,  0.16f);
        static readonly Color LabelColor    = new Color(0.75f,  0.75f,  0.78f);
        static readonly Color MasterColor   = Color.white;                              // bright-white master fill
        const float UnseenOpacity = 0.4f;
        const float SeenOpacity   = 1.0f;

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

        /// <summary>Side of the 8×8 pad square, in design units.</summary>
        float GridSide => GridSideDesign;

        /// <summary>Follows from the square: the grid plus everything beside it.</summary>
        float DrawerWidth => GridSide + (2f * SectionPad) + (2f * DrawerPadX);

        /// <summary>Full drawer height in design units. Sums the same constants the
        /// layout actually applies, so it tracks whether the mix section is shown.</summary>
        float DrawerHeight => (2f * DrawerPadY) + (2f * SectionPad) + GridSide
                            + (_showMidiMix ? Mf64SectionGap + MixSectionHeight : 0f);

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
        const float FocusScale  = 3f;
        const int   FocusHoldMs = 800;   // toggle mode: keep focused this long
        int _focusRow = -1, _focusCol = -1;
        IVisualElementScheduledItem _focusClearTimer;

        // ─── Unity lifecycle ──────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            BuildAllViews();

            _btnRouter = Object.FindFirstObjectByType<MidiFighterButtonRouter>();

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
            if (kb.backquoteKey.wasPressedThisFrame || kb.f1Key.wasPressedThisFrame)
            {
                _hidden = !_hidden;
                ApplyHiddenState(instant: false);
            }
            if (kb.f2Key.wasPressedThisFrame)
                Placement = _placement == DrawerPlacement.RightCentered
                    ? DrawerPlacement.ScreenCentered
                    : DrawerPlacement.RightCentered;
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
            if (_themeStyleSheet != null)
                view.settings.themeStyleSheet = _themeStyleSheet;

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

            if (_showMf64)    body.Add(BuildMf64Section(view));   // top
            if (_showMidiMix) body.Add(BuildMixSection(view));    // bottom

            // The message strip normally lives in the MIDI Mix utility row. With
            // that section hidden it would vanish, so give it its own panel.
            if (!_showMidiMix)
            {
                var msgSection = MakeSection("Message");
                view.messageLabel = MakeMessageLabel();
                msgSection.Add(view.messageLabel);
                body.Add(msgSection);
            }
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
                    knob.style.opacity = _knobsSeen[ch, rowIdx] ? SeenOpacity : UnseenOpacity;
                    view.knobs[ch, rowIdx] = knob;
                    strip.Add(knob);
                }

                var mute = MakeSizedPad(PadCell.Mode.Toggle, BarFillColor, PAD_SIZE);
                mute.style.marginBottom = 4;
                view.mutes[ch] = mute;
                strip.Add(mute);

                var rec = MakeSizedPad(PadCell.Mode.Toggle, BarFillColor, PAD_SIZE);
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
            masterTrack.style.backgroundColor = BarTrackColor;
            masterTrack.style.borderTopLeftRadius = 3;
            masterTrack.style.borderTopRightRadius = 3;
            masterTrack.style.borderBottomLeftRadius = 3;
            masterTrack.style.borderBottomRightRadius = 3;
            masterTrack.style.opacity = _masterFaderSeen ? SeenOpacity : UnseenOpacity;
            masterTrack.style.overflow = Overflow.Hidden;

            view.masterFaderBar = new VisualElement();
            view.masterFaderBar.style.height = new Length(100, LengthUnit.Percent);
            view.masterFaderBar.style.width = 0;
            view.masterFaderBar.style.backgroundColor = MasterColor;
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
            view.soloModifier = MakeSizedPad(PadCell.Mode.Button, MasterColor, PAD_SIZE);
            view.bankLeft     = MakeSizedPad(PadCell.Mode.Button, BarFillColor, PAD_SIZE);
            view.bankRight    = MakeSizedPad(PadCell.Mode.Button, BarFillColor, PAD_SIZE);
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
            var cell = new PadCell { CellMode = mode, FillColor = fill };
            cell.style.width = size;
            cell.style.height = size;
            return cell;
        }

        VisualElement MakeFader(out VisualElement bar, bool seen)
        {
            var col = new VisualElement();
            col.style.backgroundColor = BarTrackColor;
            col.style.borderTopLeftRadius = 3;
            col.style.borderTopRightRadius = 3;
            col.style.borderBottomLeftRadius = 3;
            col.style.borderBottomRightRadius = 3;
            col.style.justifyContent = Justify.FlexEnd;
            col.style.opacity = seen ? SeenOpacity : UnseenOpacity;

            bar = new VisualElement();
            bar.style.height = 0;
            bar.style.backgroundColor = BarFillColor;
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
            s.style.backgroundColor = BgSection;
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
            msg.style.flexGrow = 1;
            msg.style.paddingLeft = 8;
            msg.style.paddingRight = 8;
            msg.style.paddingTop = 4;
            msg.style.paddingBottom = 4;
            msg.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            msg.style.borderTopLeftRadius = 3;
            msg.style.borderTopRightRadius = 3;
            msg.style.borderBottomLeftRadius = 3;
            msg.style.borderBottomRightRadius = 3;
            msg.style.unityTextAlign = TextAnchor.MiddleLeft;
            msg.style.color = new Color(0.85f, 0.85f, 0.90f);
            return msg;
        }

        Label MakeLabel(string text, int size)
        {
            var l = new Label(text);
            l.style.color = LabelColor;
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
            foreach (var v in _views)
            {
                var cell = v.pads[btn.linearIndex];
                if (cell == null) continue;
                cell.CellMode = PadCell.Mode.Toggle;
                cell.FillColor = color;
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
            foreach (var v in _views)
            {
                var cell = v.pads[btn.linearIndex];
                if (cell == null) continue;
                cell.CellMode = PadCell.Mode.Button;
                cell.FillColor = color;
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
        // width) by FocusScale so the pad itself stays proportional. Timing
        // is asymmetric: snap in ~100ms, ease out over ~700ms.
        static readonly List<TimeValue> FocusInDuration  = new() { new TimeValue(100, TimeUnit.Millisecond) };
        static readonly List<TimeValue> FocusOutDuration = new() { new TimeValue(700, TimeUnit.Millisecond) };
        static readonly List<EasingFunction> FocusInEase  = new() { new EasingFunction(EasingMode.EaseOutCubic) };
        static readonly List<EasingFunction> FocusOutEase = new() { new EasingFunction(EasingMode.EaseOutCirc) };

        void FocusMf64Pad(int row1Based, int col1Based)
        {
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
                        rowEl.style.flexGrow = (r == activeRow) ? FocusScale : 1f;
                    }
                    for (int c = 0; c < 8; c++)
                    {
                        var cell = v.pads[r * 8 + c];
                        if (cell == null) continue;
                        cell.style.transitionDuration = FocusInDuration;
                        cell.style.transitionTimingFunction = FocusInEase;
                        cell.style.flexGrow = (c == activeCol) ? FocusScale : 1f;
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
                var k = v.knobs[ch, r];
                if (k == null) continue;
                k.Value = value;
                if (!wasSeen) k.style.opacity = SeenOpacity;
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
                if (bar == null) continue;
                bar.style.height = new Length(Mathf.Clamp01(value) * 100f, LengthUnit.Percent);
                if (!wasSeen && v.faderTracks[ch] != null) v.faderTracks[ch].style.opacity = SeenOpacity;
            }
            SetMessage($"Mix Fader Ch{channel}  {value:0.00}");
        }

        void HandleMixMasterFader(float value)
        {
            bool wasSeen = _masterFaderSeen;
            _masterFaderSeen = true;
            foreach (var v in _views)
            {
                if (v.masterFaderBar == null) continue;
                v.masterFaderBar.style.width = new Length(Mathf.Clamp01(value) * 100f, LengthUnit.Percent);
                if (!wasSeen && v.masterFaderTrack != null) v.masterFaderTrack.style.opacity = SeenOpacity;
            }
            SetMessage($"Mix Master  {value:0.00}");
        }

        void HandleMixMute(int channel, bool isNoteOn)
        {
            int ch = channel - 1;
            if (ch < 0 || ch > 7) return;
            foreach (var v in _views)
                if (v.mutes[ch] != null) v.mutes[ch].Active = isNoteOn;
            SetMessage($"Mix Mute Ch{channel}  {(isNoteOn ? "DOWN" : "UP")}");
        }

        void HandleMixRecArm(int channel, bool isNoteOn)
        {
            int ch = channel - 1;
            if (ch < 0 || ch > 7) return;
            foreach (var v in _views)
                if (v.recArms[ch] != null) v.recArms[ch].Active = isNoteOn;
            SetMessage($"Mix RecArm Ch{channel}  {(isNoteOn ? "DOWN" : "UP")}");
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
