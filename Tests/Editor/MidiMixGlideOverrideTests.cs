using NUnit.Framework;
using UnityEngine;

namespace MidiFighter64.Tests
{
    /// <summary>Edit-mode tests for MidiMixRouter's per-control glide times: an override wins
    /// for its own control, every other control keeps the shared value.</summary>
    public class MidiMixGlideOverrideTests
    {
        GameObject _go;
        MidiMixRouter _router;

        [SetUp] public void SetUp()
        {
            _go = new GameObject("router");
            _router = _go.AddComponent<MidiMixRouter>();
            _router.KnobGlideSeconds  = 0.1f;
            _router.FaderGlideSeconds = 0.05f;
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_go);

        [Test] public void KnobOverride_AppliesToThatKnobOnly()
        {
            _router.SetKnobGlide(2, 1, 0.3f);
            Assert.AreEqual(0.3f, _router.GetKnobGlide(2, 1));
            Assert.AreEqual(0.1f, _router.GetKnobGlide(2, 2));
            Assert.AreEqual(0.1f, _router.GetKnobGlide(1, 1));
        }

        [Test] public void KnobOverride_SetAgain_Replaces()
        {
            _router.SetKnobGlide(2, 1, 0.3f);
            _router.SetKnobGlide(2, 1, 0f);
            Assert.AreEqual(0f, _router.GetKnobGlide(2, 1), "0 turns that knob's glide off");
        }

        [Test] public void SharedValue_LeavesOverridesAlone()
        {
            _router.SetKnobGlide(2, 1, 0.3f);
            _router.KnobGlideSeconds = 0.2f;
            Assert.AreEqual(0.3f, _router.GetKnobGlide(2, 1));
            Assert.AreEqual(0.2f, _router.GetKnobGlide(3, 3));
        }

        [Test] public void FaderOverride_ChannelAndMaster()
        {
            _router.SetFaderGlide(1, 0.25f);
            _router.SetFaderGlide(0, 0f);
            Assert.AreEqual(0.25f, _router.GetFaderGlide(1));
            Assert.AreEqual(0f,    _router.GetFaderGlide(0), "channel 0 is the master");
            Assert.AreEqual(0.05f, _router.GetFaderGlide(2));
        }
    }
}
