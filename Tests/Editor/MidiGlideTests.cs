using NUnit.Framework;

namespace MidiFighter64.Tests
{
    /// <summary>Edit-mode tests for the knob/fader glide behind MidiMixRouter's OnKnob,
    /// OnChannelFader and OnMasterFader.</summary>
    public class MidiGlideTests
    {
        const float Seconds = 0.08f;
        const float Frame   = 1f / 60f;

        [Test] public void FirstValue_IsTakenAsIs()
        {
            var g = new MidiGlide();
            Assert.IsTrue(g.SetTarget(0.7f, Seconds));
            Assert.AreEqual(0.7f, g.Value);
            Assert.IsFalse(g.Step(Seconds, Frame), "nothing to glide after the first value");
        }

        [Test] public void GlideOff_ReportsEachValueAtOnce()
        {
            var g = new MidiGlide();
            g.SetTarget(0.2f, 0f);
            Assert.IsTrue(g.SetTarget(0.9f, 0f));
            Assert.AreEqual(0.9f, g.Value);
            Assert.IsFalse(g.Step(0f, Frame));
        }

        [Test] public void LaterValue_GlidesThereWithoutOvershoot()
        {
            var g = new MidiGlide();
            g.SetTarget(0f, Seconds);
            Assert.IsFalse(g.SetTarget(1f, Seconds), "a glided value is reported from Step, not at once");

            float last = 0f;
            int frames = 0;
            while (g.Moving && frames < 600)
            {
                Assert.IsTrue(g.Step(Seconds, Frame));
                Assert.GreaterOrEqual(g.Value, last, "moves one way");
                Assert.LessOrEqual(g.Value, 1f, "never passes the target");
                last = g.Value;
                frames++;
            }
            Assert.IsFalse(g.Moving, "settles");
            Assert.AreEqual(1f, g.Value, "lands exactly on the target");
            Assert.Greater(frames, 3, "took more than a few frames");
            Assert.Less(frames, 60, "settled within a second");
        }

        [Test] public void FirstFrame_MovesPartWay()
        {
            var g = new MidiGlide();
            g.SetTarget(0f, Seconds);
            g.SetTarget(1f, Seconds);
            g.Step(Seconds, Frame);
            Assert.Greater(g.Value, 0f);
            Assert.Less(g.Value, 1f);
        }

        [Test] public void TurningGlideOffMidGlide_Lands()
        {
            var g = new MidiGlide();
            g.SetTarget(0f, Seconds);
            g.SetTarget(1f, Seconds);
            g.Step(Seconds, Frame);
            Assert.IsTrue(g.Step(0f, Frame));
            Assert.AreEqual(1f, g.Value);
            Assert.IsFalse(g.Moving);
        }

        [Test] public void ZeroFrame_DoesNotReport()
        {
            var g = new MidiGlide();
            g.SetTarget(0f, Seconds);
            g.SetTarget(1f, Seconds);
            Assert.IsFalse(g.Step(Seconds, 0f));
            Assert.IsTrue(g.Moving);
        }

        [Test] public void Reset_TakesTheNextValueAsIs()
        {
            var g = new MidiGlide();
            g.SetTarget(0f, Seconds);
            g.SetTarget(1f, Seconds);
            g.Reset();
            Assert.IsTrue(g.SetTarget(0.4f, Seconds));
            Assert.AreEqual(0.4f, g.Value);
        }
    }
}
