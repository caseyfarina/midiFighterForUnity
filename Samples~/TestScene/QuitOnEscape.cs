using UnityEngine;
using UnityEngine.InputSystem;

namespace MidiFighter64.Samples
{
    /// <summary>Quits the application when Escape is pressed. No-op in the Editor.</summary>
    public class QuitOnEscape : MonoBehaviour
    {
        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Application.Quit();
        }
    }
}
