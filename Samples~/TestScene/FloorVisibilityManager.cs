using UnityEngine;
using DG.Tweening;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Disables floor root GameObjects that aren't visible to save performance.
    ///
    /// Assign the root GameObject for each floor (0–7) in the inspector.
    /// On a floor change:
    ///   - The destination floor is enabled immediately (visible during the slide)
    ///   - The previous floor is disabled after the camera tween finishes
    ///
    /// transitionDuration should match FloorCameraController._duration (default 0.6s).
    /// </summary>
    public class FloorVisibilityManager : MonoBehaviour
    {
        [Header("Floor Roots (index = floor number, 0–7)")]
        public GameObject[] floorRoots = new GameObject[8];

        [Header("Timing")]
        [Tooltip("Should match FloorCameraController duration (default 0.6s).")]
        public float transitionDuration = 0.6f;

        int _currentFloor = -1;

        void Start()
        {
            _currentFloor = FloorCameraController.CurrentFloor;

            for (int i = 0; i < 8; i++)
            {
                if (floorRoots[i] != null)
                    floorRoots[i].SetActive(i == _currentFloor);
            }
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += OnButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= OnButton;

        void OnButton(GridButton btn, bool isNoteOn)
        {
            if (btn.col != 8 || !isNoteOn) return;

            int newFloor = 8 - btn.row;
            if (newFloor == _currentFloor) return;

            int prevFloor = _currentFloor;
            _currentFloor = newFloor;

            // Enable destination immediately so it's visible during the slide
            if (newFloor < floorRoots.Length && floorRoots[newFloor] != null)
                floorRoots[newFloor].SetActive(true);

            // Disable previous floor once the camera has finished moving
            if (prevFloor >= 0 && prevFloor < floorRoots.Length && floorRoots[prevFloor] != null)
            {
                var toDisable = floorRoots[prevFloor];
                DOVirtual.DelayedCall(transitionDuration, () =>
                {
                    if (toDisable != null) toDisable.SetActive(false);
                });
            }
        }
    }
}
