using UnityEngine;
using UnityEngine.Rendering;
using DG.Tweening;
using MidiFighter64;

/// <summary>
/// Routes MF64 button presses to post-processing preset switching.
/// Holds an array of VolumeProfiles indexed by MF64 linear button index (0-63).
/// Swaps sharedProfile on the target Volume — no GameObject enable/disable,
/// no volume stack invalidation, just a reference swap.
///
/// Optionally crossfades using a second Volume (blendVolume) and DOTween weight lerp.
/// Leave blendVolume null for instant snap.
/// </summary>
public class MidiPostProcessSwitcher : MonoBehaviour
{
    [Header("Volume")]
    [Tooltip("The single global post-process Volume whose profile gets swapped.")]
    [SerializeField] private Volume _volume;

    [Tooltip("Optional second Volume for crossfading. Leave null for instant snap.")]
    [SerializeField] private Volume _blendVolume;

    [Tooltip("Crossfade duration in seconds. Only used when blendVolume is assigned.")]
    [SerializeField] private float _blendDuration = 0.4f;

    [Header("Presets")]
    [Tooltip("Index maps to MF64 linear button index (0=row1/col1 ... 63=row8/col8). " +
             "Leave slots empty to ignore that button.")]
    [SerializeField] private VolumeProfile[] _profiles = new VolumeProfile[64];

    [Header("MF64 Filter")]
    [Tooltip("Only respond to buttons on this row (1-8). 0 = any row.")]
    [SerializeField] private int _rowFilter = 0;

    [Tooltip("Only respond to buttons in this column range (inclusive). 0,0 = all columns.")]
    [SerializeField] private int _colMin = 0;
    [SerializeField] private int _colMax = 0;

    private int _activeIndex = -1;

    private void OnEnable()
    {
        MidiGridRouter.OnGridButton += HandleButton;
    }

    private void OnDisable()
    {
        MidiGridRouter.OnGridButton -= HandleButton;
    }

    private void HandleButton(GridButton btn, bool isPressed)
    {
        if (!isPressed) return;

        if (_rowFilter > 0 && btn.row != _rowFilter) return;
        if (_colMin > 0 && btn.col < _colMin) return;
        if (_colMax > 0 && btn.col > _colMax) return;

        int idx = btn.linearIndex;
        if (idx < 0 || idx >= _profiles.Length) return;

        VolumeProfile profile = _profiles[idx];
        if (profile == null) return;
        if (idx == _activeIndex) return;   // already active

        _activeIndex = idx;
        SwitchTo(profile);
    }

    private void SwitchTo(VolumeProfile profile)
    {
        if (_volume == null) return;

        if (_blendVolume != null)
        {
            // Crossfade: blendVolume fades in with new profile, then takes over
            _blendVolume.sharedProfile = profile;
            _blendVolume.weight = 0f;
            _blendVolume.enabled = true;

            DOTween.To(
                () => _blendVolume.weight,
                x  => _blendVolume.weight = x,
                1f, _blendDuration
            ).OnComplete(() =>
            {
                _volume.sharedProfile = profile;
                _volume.weight = 1f;
                _blendVolume.enabled = false;
            });
        }
        else
        {
            // Instant snap
            _volume.sharedProfile = profile;
        }
    }

    /// <summary>
    /// Switch to a profile by index directly (for non-MIDI triggers).
    /// </summary>
    public void SwitchToIndex(int index)
    {
        if (index < 0 || index >= _profiles.Length) return;
        if (_profiles[index] == null) return;
        _activeIndex = index;
        SwitchTo(_profiles[index]);
    }
}
