using UnityEngine;
using Unity.Cinemachine;
using MidiFighter64;
using DG.Tweening;

namespace MidiFighter64.Samples
{
    public class FloorCameraController : MonoBehaviour
    {
        const float FLOOR_HEIGHT    = 8f;
        const float CAMERA_Y_OFFSET = 7.7f;

        [SerializeField] float _duration = 0.6f;

        CinemachineCamera _floorVcam;
        Tweener           _activeTween;

        /// <summary>Current floor index (0–7). Updated on every MF64 col-8 press.</summary>
        public static int CurrentFloor { get; private set; }

        void Awake()
        {
            var mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogWarning("[FloorCameraController] No Main Camera found.");
                return;
            }

            // Ensure CinemachineBrain exists (all transitions are instant cuts;
            // floor movement is handled by DOTween-ing the vcam transform directly).
            if (mainCam.GetComponent<CinemachineBrain>() == null)
            {
                var brain = mainCam.gameObject.AddComponent<CinemachineBrain>();
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.Cut, 0f);
            }

            // Create floor virtual camera, inheriting the scene-placed camera's
            // position and rotation (preserves X/Z and any initial tilt).
            var go = new GameObject("FloorVcam");
            go.transform.SetPositionAndRotation(
                mainCam.transform.position,
                mainCam.transform.rotation);

            _floorVcam          = go.AddComponent<CinemachineCamera>();
            _floorVcam.Priority = 10;

            // Derive starting floor from camera's initial Y
            CurrentFloor = Mathf.Clamp(
                Mathf.RoundToInt((mainCam.transform.position.y - CAMERA_Y_OFFSET) / FLOOR_HEIGHT),
                0, 7);
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += HandleButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= HandleButton;

        void HandleButton(GridButton btn, bool isNoteOn)
        {
            if (btn.col != 8 || !isNoteOn || _floorVcam == null) return;

            int floor    = 8 - btn.row;
            CurrentFloor = floor;

            float targetY = floor * FLOOR_HEIGHT + CAMERA_Y_OFFSET;
            _activeTween?.Kill();
            _activeTween = _floorVcam.transform
                .DOMoveY(targetY, _duration)
                .SetEase(Ease.InOutCubic);
        }
    }
}
