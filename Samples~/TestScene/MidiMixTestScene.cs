using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Drop this component onto any GameObject in an empty scene and press Play.
    /// It will programmatically build a visual representation of the Akai MIDI Mix:
    ///
    ///   • 8 channel strips (left → right), each with:
    ///       – 3 knob spheres  (brighten as value increases)
    ///       – Mute button cube (dim green → bright green; yellow in Solo mode)
    ///       – Rec Arm button cube (dim red → bright red)
    ///       – Fader (blue fill rises from the bottom as value increases)
    ///   • Master fader (wider, on the far right)
    ///   • Bank Left / Bank Right button cubes (flash gold on press)
    ///
    /// All controls respond live to MIDI input from a connected Akai MIDI Mix.
    ///
    /// Required components (created automatically if absent):
    ///   MidiEventManager, MidiMixRouter, UnityMainThreadDispatcher
    /// </summary>
    public class MidiMixTestScene : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Layout constants
        // ------------------------------------------------------------------ //

        const int   CHANNELS      = MidiMixInputMap.CHANNEL_COUNT; // 8
        const int   KNOB_ROWS     = MidiMixInputMap.KNOB_ROWS;      // 3
        const float CH_SPACING    = 1.4f;   // x distance between channel strips
        const float ROW_SPACING   = 1.0f;   // y distance between rows
        const float FADER_HEIGHT  = 2.0f;   // world-unit height of the fader track
        const float KNOB_SIZE     = 0.44f;
        const float BTN_SIZE      = 0.40f;
        const float FADER_WIDTH   = 0.22f;  // track width for channel faders

        // Y positions (top = 0, downward negative)
        // Knob rows: row 0 → y=0, row 1 → y=-1, row 2 → y=-2
        const float MUTE_Y        = -(KNOB_ROWS + 0) * ROW_SPACING; // -3
        const float RECARM_Y      = -(KNOB_ROWS + 1) * ROW_SPACING; // -4
        const float FADER_TOP_Y   = -(KNOB_ROWS + 2) * ROW_SPACING; // -5
        const float FADER_BOT_Y   = FADER_TOP_Y - FADER_HEIGHT;     // -7
        const float FADER_CEN_Y   = (FADER_TOP_Y + FADER_BOT_Y) * 0.5f; // -6
        const float BANK_Y        = FADER_BOT_Y - 0.8f;             // -7.8

        // Master fader sits one gap to the right of channel 8
        const float MASTER_X      = (CHANNELS + 0.7f) * CH_SPACING;
        const float MASTER_TRACK  = 0.40f;  // wider track for master
        const float MASTER_FILL   = 0.36f;

        // ------------------------------------------------------------------ //
        // Colors
        // ------------------------------------------------------------------ //

        static readonly Color BG_COLOR            = new Color(0.05f, 0.05f, 0.06f);
        static readonly Color IDLE_COLOR          = new Color(0.12f, 0.12f, 0.14f);
        static readonly Color KNOB_MIN_COLOR      = new Color(0.10f, 0.12f, 0.14f);
        static readonly Color KNOB_MAX_COLOR      = new Color(0.25f, 0.70f, 1.00f);
        static readonly Color FADER_TRACK_COLOR   = new Color(0.10f, 0.10f, 0.12f);
        static readonly Color FADER_FILL_COLOR    = new Color(0.25f, 0.70f, 1.00f);
        static readonly Color MUTE_IDLE_COLOR     = new Color(0.08f, 0.18f, 0.08f);
        static readonly Color MUTE_ACTIVE_COLOR   = new Color(0.15f, 0.90f, 0.25f);
        static readonly Color SOLO_ACTIVE_COLOR   = new Color(1.00f, 0.80f, 0.10f);
        static readonly Color RECARM_IDLE_COLOR   = new Color(0.20f, 0.06f, 0.06f);
        static readonly Color RECARM_ACTIVE_COLOR = new Color(0.95f, 0.15f, 0.10f);
        static readonly Color BANK_IDLE_COLOR     = new Color(0.15f, 0.15f, 0.20f);
        static readonly Color BANK_ACTIVE_COLOR   = new Color(1.00f, 0.88f, 0.20f);

        const float FLASH_DURATION = 0.15f;

        // ------------------------------------------------------------------ //
        // Visual element references (0-based indexing)
        // ------------------------------------------------------------------ //

        // [row * CHANNELS + ch]
        readonly Material[] _knobMats      = new Material[KNOB_ROWS * CHANNELS];

        // [ch]
        readonly Material[] _muteMats      = new Material[CHANNELS];
        readonly Material[] _recArmMats    = new Material[CHANNELS];
        readonly Transform[] _faderFills   = new Transform[CHANNELS];

        Transform _masterFill;
        Material  _bankLeftMat;
        Material  _bankRightMat;

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        void Awake()
        {
            EnsureCoreComponents();
            BuildScene();
        }

        void OnEnable()
        {
            MidiMixRouter.OnKnob         += HandleKnob;
            MidiMixRouter.OnChannelFader += HandleChannelFader;
            MidiMixRouter.OnMasterFader  += HandleMasterFader;
            MidiMixRouter.OnMute         += HandleMute;
            MidiMixRouter.OnSolo         += HandleSolo;
            MidiMixRouter.OnRecArm       += HandleRecArm;
            MidiMixRouter.OnBankLeft     += HandleBankLeft;
            MidiMixRouter.OnBankRight    += HandleBankRight;
        }

        void OnDisable()
        {
            MidiMixRouter.OnKnob         -= HandleKnob;
            MidiMixRouter.OnChannelFader -= HandleChannelFader;
            MidiMixRouter.OnMasterFader  -= HandleMasterFader;
            MidiMixRouter.OnMute         -= HandleMute;
            MidiMixRouter.OnSolo         -= HandleSolo;
            MidiMixRouter.OnRecArm       -= HandleRecArm;
            MidiMixRouter.OnBankLeft     -= HandleBankLeft;
            MidiMixRouter.OnBankRight    -= HandleBankRight;
        }

        // ------------------------------------------------------------------ //
        // Scene construction
        // ------------------------------------------------------------------ //

        void EnsureCoreComponents()
        {
            if (Object.FindFirstObjectByType<MidiEventManager>() == null)
                new GameObject("MidiEventManager").AddComponent<MidiEventManager>();

            if (Object.FindFirstObjectByType<UnityMainThreadDispatcher>() == null)
                new GameObject("UnityMainThreadDispatcher").AddComponent<UnityMainThreadDispatcher>();

            if (Object.FindFirstObjectByType<MidiMixRouter>() == null)
                new GameObject("MidiMixRouter").AddComponent<MidiMixRouter>();
        }

        void BuildScene()
        {
            SetupCamera();
            SetupLight();
            BuildChannelStrips();
            BuildMasterFader();
            BuildBankButtons();
            BuildUI();
        }

        void SetupCamera()
        {
            if (Camera.main != null) return;

            float cx = MASTER_X * 0.5f;
            float cy = BANK_Y   * 0.5f;

            var go  = new GameObject("Main Camera");
            go.tag  = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG_COLOR;
            cam.fieldOfView     = 55f;

            go.transform.position = new Vector3(cx, cy, -16f);
            go.transform.LookAt(new Vector3(cx, cy, 0f));
        }

        void SetupLight()
        {
            var go  = new GameObject("Directional Light");
            var lit = go.AddComponent<Light>();
            lit.type      = LightType.Directional;
            lit.intensity = 1.2f;
            go.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

        void BuildChannelStrips()
        {
            var root = new GameObject("MIDI Mix Channels");

            for (int ch = 0; ch < CHANNELS; ch++)
            {
                float cx = ch * CH_SPACING;

                // 3 knob spheres
                for (int row = 0; row < KNOB_ROWS; row++)
                {
                    int   idx = row * CHANNELS + ch;
                    float y   = -row * ROW_SPACING;
                    var   go  = CreateSphere($"Knob_R{row+1}_Ch{ch+1}", root.transform,
                                             new Vector3(cx, y, 0f), KNOB_SIZE);
                    _knobMats[idx] = SetMaterial(go, KNOB_MIN_COLOR);
                }

                // Mute button
                {
                    var go = CreateCube($"Mute_Ch{ch+1}", root.transform,
                                        new Vector3(cx, MUTE_Y, 0f), BTN_SIZE);
                    _muteMats[ch] = SetMaterial(go, MUTE_IDLE_COLOR);
                }

                // Rec Arm button
                {
                    var go = CreateCube($"RecArm_Ch{ch+1}", root.transform,
                                        new Vector3(cx, RECARM_Y, 0f), BTN_SIZE);
                    _recArmMats[ch] = SetMaterial(go, RECARM_IDLE_COLOR);
                }

                // Channel fader
                _faderFills[ch] = BuildFader($"Fader_Ch{ch+1}", root.transform, cx,
                                              FADER_WIDTH, FADER_WIDTH - 0.03f);
            }
        }

        void BuildMasterFader()
        {
            var root = new GameObject("Master Fader");
            _masterFill = BuildFader("Master", root.transform, MASTER_X,
                                     MASTER_TRACK, MASTER_FILL);
        }

        void BuildBankButtons()
        {
            var root = new GameObject("Bank Buttons");

            var left  = CreateCube("BankLeft",  root.transform,
                                   new Vector3(0f,          BANK_Y, 0f), BTN_SIZE);
            var right = CreateCube("BankRight", root.transform,
                                   new Vector3(CH_SPACING,  BANK_Y, 0f), BTN_SIZE);

            _bankLeftMat  = SetMaterial(left,  BANK_IDLE_COLOR);
            _bankRightMat = SetMaterial(right, BANK_IDLE_COLOR);
        }

        /// <summary>
        /// Creates a fader track + fill pair. The fill is a child of a pivot
        /// at the bottom of the track so it grows upward when scaled in Y.
        /// Returns the fill Transform (used by SetFaderValue).
        /// </summary>
        Transform BuildFader(string name, Transform parent, float x,
                             float trackWidth, float fillWidth)
        {
            // Track
            var track = CreateCube($"{name}_Track", parent,
                                   new Vector3(x, FADER_CEN_Y, 0f),
                                   new Vector3(trackWidth, FADER_HEIGHT, trackWidth * 0.8f));
            SetMaterial(track, FADER_TRACK_COLOR);

            // Pivot sits at the bottom of the track
            var pivot = new GameObject($"{name}_FillPivot");
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = new Vector3(x, FADER_BOT_Y, 0f);

            // Fill starts at zero height; SetFaderValue drives it
            var fill = CreateCube($"{name}_Fill", pivot.transform,
                                  Vector3.zero,
                                  new Vector3(fillWidth, 0.001f, fillWidth * 0.8f));
            SetMaterial(fill, FADER_FILL_COLOR);
            return fill.transform;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("Canvas");
            var canvas   = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel     = new GameObject("Panel");
            panel.transform.SetParent(canvasGo.transform, false);
            var panelImg  = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.60f);
            var panelRect  = panel.GetComponent<RectTransform>();
            panelRect.anchorMin        = new Vector2(0f, 0f);
            panelRect.anchorMax        = new Vector2(1f, 0f);
            panelRect.pivot            = new Vector2(0.5f, 0f);
            panelRect.sizeDelta        = new Vector2(0f, 50f);
            panelRect.anchoredPosition = Vector2.zero;

            var labelGo  = new GameObject("Label");
            labelGo.transform.SetParent(panel.transform, false);
            var label    = labelGo.AddComponent<Text>();
            label.text      = "Akai MIDI Mix — connect device and move controls";
            label.alignment = TextAnchor.MiddleCenter;
            label.color     = new Color(0.80f, 0.80f, 0.80f);
            label.fontSize  = 16;
            label.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var labelRect   = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;
        }

        // ------------------------------------------------------------------ //
        // MIDI event handlers
        // ------------------------------------------------------------------ //

        void HandleKnob(int channel, int row, float value)
        {
            int idx = (row - 1) * CHANNELS + (channel - 1);
            if (_knobMats[idx] != null)
                _knobMats[idx].color = Color.Lerp(KNOB_MIN_COLOR, KNOB_MAX_COLOR, value);
        }

        void HandleChannelFader(int channel, float value)
            => SetFaderValue(_faderFills[channel - 1], value, FADER_WIDTH - 0.03f);

        void HandleMasterFader(float value)
            => SetFaderValue(_masterFill, value, MASTER_FILL);

        // Mute buttons glow green when held
        void HandleMute(int channel, bool isOn)
        {
            if (_muteMats[channel - 1] != null)
                _muteMats[channel - 1].color = isOn ? MUTE_ACTIVE_COLOR : MUTE_IDLE_COLOR;
        }

        // Same physical Mute buttons in Solo mode glow gold
        void HandleSolo(int channel, bool isOn)
        {
            if (_muteMats[channel - 1] != null)
                _muteMats[channel - 1].color = isOn ? SOLO_ACTIVE_COLOR : MUTE_IDLE_COLOR;
        }

        void HandleRecArm(int channel, bool isOn)
        {
            if (_recArmMats[channel - 1] != null)
                _recArmMats[channel - 1].color = isOn ? RECARM_ACTIVE_COLOR : RECARM_IDLE_COLOR;
        }

        void HandleBankLeft()  => StartCoroutine(Flash(_bankLeftMat,  BANK_ACTIVE_COLOR, BANK_IDLE_COLOR));
        void HandleBankRight() => StartCoroutine(Flash(_bankRightMat, BANK_ACTIVE_COLOR, BANK_IDLE_COLOR));

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Scales the fader fill upward from the bottom of the track.
        /// The fill cube is parented to a pivot at FADER_BOT_Y, so we offset
        /// its local position by half its height to keep the bottom anchored.
        /// </summary>
        static void SetFaderValue(Transform fill, float value, float fillWidth)
        {
            if (fill == null) return;
            float height = Mathf.Max(0.001f, value * FADER_HEIGHT);
            float depth  = fillWidth * 0.8f;
            fill.localScale    = new Vector3(fillWidth, height, depth);
            fill.localPosition = new Vector3(0f, height * 0.5f, 0f);
        }

        IEnumerator Flash(Material mat, Color on, Color off)
        {
            if (mat == null) yield break;
            mat.color = on;
            yield return new WaitForSeconds(FLASH_DURATION);
            mat.color = off;
        }

        static GameObject CreateSphere(string name, Transform parent, Vector3 pos, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = Vector3.one * size;
            return go;
        }

        static GameObject CreateCube(string name, Transform parent, Vector3 pos, float size)
            => CreateCube(name, parent, pos, Vector3.one * size);

        static GameObject CreateCube(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = scale;
            return go;
        }

        static Material SetMaterial(GameObject go, Color color)
        {
            var mat = new Material(Shader.Find("Standard")) { color = color };
            mat.SetFloat("_Metallic",   0.05f);
            mat.SetFloat("_Glossiness", 0.60f);
            go.GetComponent<Renderer>().material = mat;
            return mat;
        }
    }
}
