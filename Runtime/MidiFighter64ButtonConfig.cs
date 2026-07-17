using UnityEngine;

namespace MidiFighter64
{
    /// <summary>
    /// ScriptableObject that assigns a Button or Toggle mode to each of the 64 pads.
    /// Indexed by <see cref="GridButton.linearIndex"/> (0 = top-left, 63 = bottom-right).
    /// Create via Assets > Create > MidiFighter64 > Button Config.
    /// </summary>
    [CreateAssetMenu(menuName = "MidiFighter64/Button Config", fileName = "MF64ButtonConfig")]
    public class MidiFighter64ButtonConfig : ScriptableObject
    {
        [Tooltip("Mode applied to any pad whose entry in the grid equals the default.")]
        [SerializeField] MidiFighterButtonMode _defaultMode = MidiFighterButtonMode.Button;

        [Tooltip("Per-pad modes, indexed by GridButton.linearIndex (0–63). " +
                 "Row 1 top-left = index 0, row 8 bottom-right = index 63.")]
        [SerializeField] MidiFighterButtonMode[] _padModes = new MidiFighterButtonMode[64];

        public MidiFighterButtonMode DefaultMode => _defaultMode;

        public MidiFighterButtonMode GetMode(GridButton btn) => GetMode(btn.linearIndex);

        public MidiFighterButtonMode GetMode(int linearIndex)
        {
            if (linearIndex < 0 || linearIndex >= 64) return _defaultMode;
            return _padModes[linearIndex];
        }
    }
}
