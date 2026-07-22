using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Drop this component onto any GameObject in an empty scene and press Play.
    /// It will programmatically create:
    ///   • A camera and directional light
    ///   • 64 spheres arranged in an 8×8 grid (one per Midi Fighter 64 button)
    ///   • A UI "Play Wave" button that animates the grid LEDs left → right
    ///
    /// Button presses received from the hardware highlight the corresponding sphere.
    /// The wave animation sends MIDI Note On messages via MidiFighterOutput so that
    /// the physical device LEDs follow the on-screen animation.
    ///
    /// Required package components (created automatically if absent):
    ///   MidiEventManager, MidiFighterOutput, UnityMainThreadDispatcher
    /// </summary>
    public class MidiFighterTestScene : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Constants
        // ------------------------------------------------------------------ //

        const int   GRID          = MidiFighter64InputMap.GRID_SIZE;
        const float SPHERE_SIZE   = 0.85f;
        const float SPACING       = 1.15f;
        const float WAVE_INTERVAL = 0.08f;  // seconds between column steps
        const int   WAVE_VELOCITY = 100;    // LED brightness during wave

        static readonly Color COLOR_IDLE = new Color(0.12f, 0.12f, 0.14f);
        static readonly Color COLOR_WAVE = new Color(1.00f, 0.75f, 0.10f);

        [Tooltip("Fallback MIDI Controller prefab, instantiated at Awake only if the " +
                 "scene has no rig yet. Normally the prefab is already in the scene " +
                 "and this can be left empty.")]
        [SerializeField] GameObject _controllerPrefab;

        // ------------------------------------------------------------------ //
        // State
        // ------------------------------------------------------------------ //

        // [row, col] — row 0 = top of grid (MF64 row 1), col 0 = left (MF64 col 1)
        readonly Material[] _mats    = new Material[GRID * GRID];
        readonly bool[]     _pressed = new bool[GRID * GRID];

        MidiFighterOutput       _output;
        MidiFighterButtonRouter _router;
        WaitForSeconds          _waveWait;
        bool _waveRunning;

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        void Awake()
        {
            EnsureCoreComponents();
            _waveWait = new WaitForSeconds(WAVE_INTERVAL);
            BuildScene();
        }

        void OnEnable()
        {
            MidiEventManager.OnNoteOn  += HandleNoteOn;
            MidiEventManager.OnNoteOff += HandleNoteOff;
        }

        void OnDisable()
        {
            MidiEventManager.OnNoteOn  -= HandleNoteOn;
            MidiEventManager.OnNoteOff -= HandleNoteOff;
        }

        void OnDestroy()
        {
            // Renderer.material auto-instantiates; release the copies.
            for (int i = 0; i < _mats.Length; i++)
                if (_mats[i] != null) Destroy(_mats[i]);
        }

        // ------------------------------------------------------------------ //
        // Scene construction
        // ------------------------------------------------------------------ //

        void EnsureCoreComponents()
        {
            // The rig normally comes from the MIDI Controller prefab already sitting
            // in the scene — that is the documented setup, and what the scene
            // generator produces. The fallback below only covers dropping this script
            // into an empty scene by hand.
            if (Object.FindFirstObjectByType<MidiEventManager>() == null && _controllerPrefab != null)
                Instantiate(_controllerPrefab);

            _router = Object.FindFirstObjectByType<MidiFighterButtonRouter>();
            _output = Object.FindFirstObjectByType<MidiFighterOutput>();

            if (_router == null)
                Debug.LogWarning(
                    "[MidiFighter64] No MIDI rig in the scene. Drag " +
                    "Packages/com.caseyfarina.midifighter64/Runtime/MIDI Controller.prefab " +
                    "into the scene, or assign it to this component's Controller Prefab field.");
        }

        void BuildScene()
        {
            SetupCamera();
            SetupLight();
            BuildGrid();
            BuildUI();
        }

        void SetupCamera()
        {
            if (Camera.main != null) return;

            float span = (GRID - 1) * SPACING;
            float cx   = span * 0.5f;
            float cy   = -span * 0.5f;

            var go  = new GameObject("Main Camera");
            go.tag  = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
            cam.fieldOfView     = 55f;

            float dist = span * 1.05f;
            go.transform.position = new Vector3(cx, cy, -dist);
            go.transform.LookAt(new Vector3(cx, cy, 0f));
        }

        void SetupLight()
        {
            var go   = new GameObject("Directional Light");
            var lit  = go.AddComponent<Light>();
            lit.type      = LightType.Directional;
            lit.intensity = 1.2f;
            go.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

        void BuildGrid()
        {
            var root = new GameObject("MF64 Grid");

            for (int r = 0; r < GRID; r++)
            for (int c = 0; c < GRID; c++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name                     = $"Btn_R{r + 1}_C{c + 1}";
                go.transform.parent         = root.transform;
                go.transform.localPosition  = new Vector3(c * SPACING, -r * SPACING, 0f);
                go.transform.localScale     = Vector3.one * SPHERE_SIZE;

                // Reuse the material Unity's CreatePrimitive assigned — it already has
                // the correct shader for the active render pipeline (URP or BiRP).
                var mat = go.GetComponent<Renderer>().material;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", COLOR_IDLE);
                mat.color = COLOR_IDLE;
                if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0.1f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.65f);
                _mats[r * GRID + c] = mat;
            }
        }

        void BuildUI()
        {
            // Canvas
            var canvasGo = new GameObject("Canvas");
            var canvas   = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Bottom panel
            var panelGo   = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelImg  = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.55f);
            var panelRect  = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin      = new Vector2(0f, 0f);
            panelRect.anchorMax      = new Vector2(1f, 0f);
            panelRect.pivot          = new Vector2(0.5f, 0f);
            panelRect.sizeDelta      = new Vector2(0f, 80f);
            panelRect.anchoredPosition = Vector2.zero;

            // Wave button
            var btnGo   = new GameObject("WaveButton");
            btnGo.transform.SetParent(panelGo.transform, false);
            var btn     = btnGo.AddComponent<Button>();
            var btnImg  = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.2f, 0.5f, 1f);
            var btnRect  = btnGo.GetComponent<RectTransform>();
            btnRect.anchorMin        = btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.sizeDelta        = new Vector2(200f, 50f);
            btnRect.anchoredPosition = Vector2.zero;

            // Label
            var labelGo   = new GameObject("Label");
            labelGo.transform.SetParent(btnGo.transform, false);
            var label     = labelGo.AddComponent<Text>();
            label.text      = "Play Wave";
            label.alignment = TextAnchor.MiddleCenter;
            label.color     = Color.white;
            label.fontSize  = 18;
            label.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var labelRect   = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin  = Vector2.zero;
            labelRect.anchorMax  = Vector2.one;
            labelRect.sizeDelta  = Vector2.zero;

            btn.targetGraphic = btnImg;
            btn.onClick.AddListener(OnWaveClicked);
        }

        // ------------------------------------------------------------------ //
        // MIDI input handlers
        // ------------------------------------------------------------------ //

        void HandleNoteOn(int note, float velocity)
        {
            if (!MidiFighter64InputMap.IsInRange(note)) return;
            var btn = MidiFighter64InputMap.FromNote(note);
            _pressed[btn.linearIndex] = true;
            SetColor(btn.linearIndex, _router.ButtonDownColor.ToUnityColor());
        }

        void HandleNoteOff(int note)
        {
            if (!MidiFighter64InputMap.IsInRange(note)) return;
            var btn = MidiFighter64InputMap.FromNote(note);
            _pressed[btn.linearIndex] = false;
            SetColor(btn.linearIndex, COLOR_IDLE);
        }

        // ------------------------------------------------------------------ //
        // Wave animation
        // ------------------------------------------------------------------ //

        void OnWaveClicked()
        {
            if (!_waveRunning)
                StartCoroutine(PlayWave());
        }

        /// <summary>
        /// Sweeps a lit column from left to right across the grid.
        /// Each step lights one column of spheres (COLOR_WAVE) and sends Note On
        /// messages so the physical Midi Fighter 64 LEDs mirror the animation.
        /// </summary>
        IEnumerator PlayWave()
        {
            _waveRunning = true;

            for (int c = 0; c < GRID; c++)
            {
                // Light this column
                for (int r = 0; r < GRID; r++)
                {
                    int idx  = r * GRID + c;
                    int note = NoteFromArrayIndex(r, c);
                    if (!_pressed[idx]) SetColor(idx, COLOR_WAVE);
                    _output?.SetLED(note, WAVE_VELOCITY);
                }

                yield return _waveWait;

                // Extinguish previous column
                if (c > 0)
                {
                    int prevC = c - 1;
                    for (int r = 0; r < GRID; r++)
                    {
                        int idx  = r * GRID + prevC;
                        int note = NoteFromArrayIndex(r, prevC);
                        if (!_pressed[idx]) SetColor(idx, COLOR_IDLE);
                        _output?.ClearLED(note);
                    }
                }
            }

            // Clear the last column
            for (int r = 0; r < GRID; r++)
            {
                int idx  = r * GRID + (GRID - 1);
                int note = NoteFromArrayIndex(r, GRID - 1);
                if (!_pressed[idx]) SetColor(idx, COLOR_IDLE);
                _output?.ClearLED(note);
            }

            _waveRunning = false;
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        void SetColor(int flatIndex, Color color)
        {
            var mat = _mats[flatIndex];
            if (mat == null) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.color = color;
        }

        /// <summary>
        /// Converts 0-based array indices (row 0 = top, col 0 = left) to the
        /// MIDI note number used by the Midi Fighter 64. Delegates to the
        /// split-half-aware <see cref="MidiFighter64InputMap.ToNote"/>.
        /// </summary>
        static int NoteFromArrayIndex(int arrayRow, int arrayCol)
            => MidiFighter64InputMap.ToNote(arrayRow + 1, arrayCol + 1);
    }
}
