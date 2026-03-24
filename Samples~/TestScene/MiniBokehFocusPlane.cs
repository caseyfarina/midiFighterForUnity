using UnityEngine;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Parents itself to the main camera and holds the MiniBokeh reference plane
    /// perpendicular to the view axis, giving true depth-of-field blur.
    ///
    /// Local rotation is fixed at Euler(-90, 0, 0) so transform.up == camera.forward.
    /// MiniBokeh then blurs pixels by (camera-space depth − FocusDistance).
    ///
    /// Drive FocusDistance (localPosition.z) via MiniBokehFaderDriver Ch6.
    /// Assign this GameObject as ReferencePlane on MiniBokehController.
    /// Set AutoFocus = true on the controller.
    /// </summary>
    public class MiniBokehFocusPlane : MonoBehaviour
    {
        [Tooltip("Focus distance in front of the camera (metres).")]
        public float FocusDistance = 10f;

        private void Awake()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[MiniBokehFocusPlane] No main camera found.");
                return;
            }

            transform.SetParent(cam.transform, false);
            transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // up → camera forward
            transform.localPosition = new Vector3(0f, 0f, FocusDistance);
        }

        /// <summary>Called by MiniBokehFaderDriver to update focus distance.</summary>
        public void SetFocusDistance(float distance)
        {
            FocusDistance = distance;
            transform.localPosition = new Vector3(0f, 0f, distance);
        }
    }
}
