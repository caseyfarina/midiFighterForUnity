using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.InputSystem;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    [System.Serializable]
    public class AudioClipGroup
    {
        public string      folderName;
        public AudioClip[] clips;
    }

    /// <summary>
    /// "Music Mode" — toggled by pressing S on the keyboard.
    ///
    /// On enter:
    ///   - Plays a pre-recorded movie fullscreen (VideoPlayer → RenderTexture → Canvas)
    ///   - Each MF64 button triggers a random clip from its assigned subfolder
    ///   - Other MF64 handlers (interior spawner, floor camera) are suspended
    ///   - All MF64 LEDs light up
    ///
    /// On exit (S key again, or movie reaches end):
    ///   - Video hidden, suspended scripts re-enabled, LEDs cleared
    ///
    /// Subfolders are mapped row-major from the top-left button (linearIndex 0).
    /// Use the inspector Reload button to scan the root folder and populate buttonGroups.
    /// Assign an AudioMixerGroup to mixerGroup to route samples through effects.
    /// </summary>
    public class MusicMode : MonoBehaviour
    {
        [Header("Toggle Key")]
        public Key toggleKey = Key.S;

        [Header("Video")]
        public VideoClip videoClip;
        [Tooltip("Used if no VideoClip asset is assigned. Relative to StreamingAssets if useStreamingAssets is true.")]
        public string videoFilePath = "";
        public bool useStreamingAssets = true;

        [Header("Audio")]
        [Tooltip("Optional — route samples through an AudioMixer group for effects.")]
        public AudioMixerGroup mixerGroup;

        [Header("Button Sample Groups (index 0–63, row-major from top-left)")]
        public AudioClipGroup[] buttonGroups = new AudioClipGroup[64];
        [HideInInspector] public string sampleRootFolder = "Assets/";

        // ── State ─────────────────────────────────────────────────────────────
        public static bool IsActive { get; private set; }

        VideoPlayer _videoPlayer;
        Canvas      _canvas;
        AudioSource _audioSource;

        MidiFighterInteriorSpawner _interiorSpawner;
        FloorCameraController      _floorCamera;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            BuildVideoDisplay();
            BuildAudioSource();
        }

        void Start()
        {
            _interiorSpawner = FindFirstObjectByType<MidiFighterInteriorSpawner>();
            _floorCamera     = FindFirstObjectByType<FloorCameraController>();
        }

        void OnDestroy()
        {
            if (_videoPlayer != null)
                _videoPlayer.loopPointReached -= OnVideoEnd;
            if (IsActive) ExitMusicMode();
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += OnButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= OnButton;

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
                Toggle();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Toggle()
        {
            if (IsActive) ExitMusicMode();
            else          EnterMusicMode();
        }

        // ── MIDI ──────────────────────────────────────────────────────────────

        void OnButton(GridButton btn, bool isNoteOn)
        {
            if (!IsActive || !isNoteOn) return;

            int idx = btn.linearIndex; // 0–63
            if (buttonGroups == null || idx >= buttonGroups.Length) return;

            var group = buttonGroups[idx];
            if (group?.clips == null || group.clips.Length == 0) return;

            var clip = group.clips[Random.Range(0, group.clips.Length)];
            if (clip != null) _audioSource.PlayOneShot(clip);
        }

        // ── Mode transitions ──────────────────────────────────────────────────

        void EnterMusicMode()
        {
            IsActive = true;

            if (_interiorSpawner != null) _interiorSpawner.enabled = false;
            if (_floorCamera     != null) _floorCamera.enabled     = false;

            _canvas.gameObject.SetActive(true);
            StartVideo();
            SetAllLEDs(true);
        }

        void ExitMusicMode()
        {
            IsActive = false;

            _videoPlayer.Stop();
            _canvas.gameObject.SetActive(false);

            if (_interiorSpawner != null) _interiorSpawner.enabled = true;
            if (_floorCamera     != null) _floorCamera.enabled     = true;

            SetAllLEDs(false);
        }

        void OnVideoEnd(VideoPlayer _) { if (IsActive) ExitMusicMode(); }

        // ── Video ─────────────────────────────────────────────────────────────

        void StartVideo()
        {
            if (videoClip != null)
            {
                _videoPlayer.source = VideoSource.VideoClip;
                _videoPlayer.clip   = videoClip;
            }
            else if (!string.IsNullOrEmpty(videoFilePath))
            {
                _videoPlayer.source = VideoSource.Url;
                _videoPlayer.url    = useStreamingAssets
                    ? System.IO.Path.Combine(Application.streamingAssetsPath, videoFilePath)
                    : videoFilePath;
            }
            else
            {
                Debug.LogWarning("[MusicMode] No video clip or file path assigned.");
                return;
            }

            _videoPlayer.Prepare();
            _videoPlayer.prepareCompleted += OnPrepared;
        }

        void OnPrepared(VideoPlayer vp)
        {
            vp.prepareCompleted -= OnPrepared;
            vp.Play();
        }

        void BuildVideoDisplay()
        {
            var rt = new RenderTexture(1920, 1080, 0);

            var canvasGo = new GameObject("[MusicMode] Canvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            _canvas = canvas;

            var imgGo = new GameObject("VideoImage", typeof(RectTransform));
            imgGo.transform.SetParent(canvasGo.transform, false);
            var raw = imgGo.AddComponent<RawImage>();
            raw.texture = rt;
            var rect = imgGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var vpGo = new GameObject("[MusicMode] VideoPlayer");
            vpGo.transform.SetParent(transform);
            _videoPlayer                  = vpGo.AddComponent<VideoPlayer>();
            _videoPlayer.renderMode       = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture    = rt;
            _videoPlayer.audioOutputMode  = VideoAudioOutputMode.AudioSource;
            _videoPlayer.playOnAwake      = false;
            _videoPlayer.loopPointReached += OnVideoEnd;

            var videoAudio = vpGo.AddComponent<AudioSource>();
            _videoPlayer.SetTargetAudioSource(0, videoAudio);

            canvasGo.SetActive(false);
        }

        void BuildAudioSource()
        {
            var go = new GameObject("[MusicMode] AudioSource");
            go.transform.SetParent(transform);
            _audioSource             = go.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            if (mixerGroup != null)
                _audioSource.outputAudioMixerGroup = mixerGroup;
        }

        // ── LEDs ──────────────────────────────────────────────────────────────

        void SetAllLEDs(bool on)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var output = MidiFighterOutput.Instance;
            if (output == null) return;
            for (int n = MidiFighter64InputMap.NOTE_OFFSET; n <= MidiFighter64InputMap.NOTE_MAX; n++)
            {
                if (on) output.SetLED(n, 127);
                else    output.ClearLED(n);
            }
#endif
        }
    }
}
