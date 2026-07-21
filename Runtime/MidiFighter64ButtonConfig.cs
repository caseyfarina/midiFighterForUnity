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

        [Tooltip("Per-pad active color (used when a Toggle pad is on, or while a Button pad is held). " +
                 "Indexed by GridButton.linearIndex (0–63). Any pad set to Off falls back to the " +
                 "router's global colour for that mode.")]
        [SerializeField] MidiFighterLEDColor[] _padColors = DefaultColorArray();

        public MidiFighterButtonMode DefaultMode => _defaultMode;

        public MidiFighterButtonMode GetMode(GridButton btn) => GetMode(btn.linearIndex);

        public MidiFighterButtonMode GetMode(int linearIndex)
        {
            if (linearIndex < 0 || linearIndex >= 64) return _defaultMode;
            return _padModes[linearIndex];
        }

        /// <summary>
        /// Returns this pad's active color. <see cref="MidiFighterLEDColor.Off"/> means
        /// "no override — use the router's global color for the pad's current mode."
        /// </summary>
        public MidiFighterLEDColor GetColor(GridButton btn) => GetColor(btn.linearIndex);

        public MidiFighterLEDColor GetColor(int linearIndex)
        {
            if (_padColors == null || linearIndex < 0 || linearIndex >= _padColors.Length)
                return MidiFighterLEDColor.Off;
            return _padColors[linearIndex];
        }

        /// <summary>Overwrites the default mode and per-pad mode grid.</summary>
        public void SetPadModes(MidiFighterButtonMode defaultMode, MidiFighterButtonMode[] padModes)
        {
            _defaultMode = defaultMode;
            if (_padModes == null || _padModes.Length != 64) _padModes = new MidiFighterButtonMode[64];
            if (padModes != null)
            {
                int n = System.Math.Min(64, padModes.Length);
                for (int i = 0; i < n; i++) _padModes[i] = padModes[i];
            }
        }

        /// <summary>Overwrites the per-pad active-colour grid.</summary>
        public void SetPadColors(MidiFighterLEDColor[] padColors)
        {
            if (_padColors == null || _padColors.Length != 64) _padColors = new MidiFighterLEDColor[64];
            if (padColors != null)
            {
                int n = System.Math.Min(64, padColors.Length);
                for (int i = 0; i < n; i++) _padColors[i] = padColors[i];
            }
        }

        static MidiFighterLEDColor[] DefaultColorArray()
        {
            var arr = new MidiFighterLEDColor[64];
            for (int i = 0; i < 64; i++) arr[i] = MidiFighterLEDColor.Off; // Off = "use router default"
            return arr;
        }

        void OnEnable()   => NormalizeArrays();
        void OnValidate() => NormalizeArrays();

        // Assets/instances created before _padColors existed can deserialize with a
        // length-0 array. Field initializers don't re-run for serialized objects, so
        // pad it out here to prevent GetColor from always returning Off.
        void NormalizeArrays()
        {
            if (_padModes  == null || _padModes.Length  != 64) System.Array.Resize(ref _padModes,  64);
            if (_padColors == null || _padColors.Length != 64) System.Array.Resize(ref _padColors, 64);
        }
    }
}
