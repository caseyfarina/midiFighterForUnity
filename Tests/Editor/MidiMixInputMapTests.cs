using NUnit.Framework;

namespace MidiFighter64.Tests
{
    /// <summary>
    /// Edit-mode tests locking the Akai MIDImix control map (default firmware, MIDI channel 1).
    /// Values verified against the Carnelian Ableton control-surface script and
    /// the Bitwig community controller script (mfeyx/akai-midimix-bitwig).
    /// Do NOT change constants in MidiMixInputMap without updating these tests.
    /// </summary>
    public class MidiMixInputMapTests
    {
        // ------------------------------------------------------------------ //
        // Knob CCs — [row, channel]
        // ------------------------------------------------------------------ //

        [Test] public void KnobCC_TopLeft_Row0Ch0_Is16()      => Assert.AreEqual(16, MidiMixInputMap.KnobCC[0, 0]);
        [Test] public void KnobCC_TopRight_Row0Ch7_Is58()     => Assert.AreEqual(58, MidiMixInputMap.KnobCC[0, 7]);
        [Test] public void KnobCC_BottomLeft_Row2Ch0_Is18()   => Assert.AreEqual(18, MidiMixInputMap.KnobCC[2, 0]);
        [Test] public void KnobCC_BottomRight_Row2Ch7_Is60()  => Assert.AreEqual(60, MidiMixInputMap.KnobCC[2, 7]);

        [Test] public void KnobLookup_CC24_IsChannel3Row1()
        {
            Assert.IsTrue(MidiMixInputMap.TryGetKnob(24, out var knob));
            Assert.AreEqual(3, knob.channel);
            Assert.AreEqual(1, knob.row);
        }

        // ------------------------------------------------------------------ //
        // Fader CCs
        // ------------------------------------------------------------------ //

        [Test] public void FaderCC_Ch1_Is19()  => Assert.AreEqual(19, MidiMixInputMap.FaderCC[0]);
        [Test] public void FaderCC_Ch8_Is61()  => Assert.AreEqual(61, MidiMixInputMap.FaderCC[7]);
        [Test] public void FaderCC_Master_Is62() => Assert.AreEqual(62, MidiMixInputMap.MasterFaderCC);

        [Test]
        public void FaderLookup_MasterCC_HasIsMasterTrue()
        {
            Assert.IsTrue(MidiMixInputMap.TryGetFader(62, out var fader));
            Assert.IsTrue(fader.isMaster);
            Assert.AreEqual(0, fader.channel);
        }

        [Test]
        public void FaderLookup_Ch1CC_HasIsMasterFalse()
        {
            Assert.IsTrue(MidiMixInputMap.TryGetFader(19, out var fader));
            Assert.IsFalse(fader.isMaster);
            Assert.AreEqual(1, fader.channel);
        }

        // ------------------------------------------------------------------ //
        // Button notes
        // ------------------------------------------------------------------ //

        [Test] public void MuteNote_Ch1_Is1()    => Assert.AreEqual(1,  MidiMixInputMap.MuteNotes[0]);
        [Test] public void MuteNote_Ch8_Is22()   => Assert.AreEqual(22, MidiMixInputMap.MuteNotes[7]);
        [Test] public void SoloNote_Ch1_Is2()    => Assert.AreEqual(2,  MidiMixInputMap.SoloNotes[0]);
        [Test] public void RecArmNote_Ch1_Is3()  => Assert.AreEqual(3,  MidiMixInputMap.RecArmNotes[0]);
        [Test] public void RecArmNote_Ch8_Is24() => Assert.AreEqual(24, MidiMixInputMap.RecArmNotes[7]);

        [Test]
        public void ButtonLookup_MuteNote_ReturnsMuteType()
        {
            Assert.IsTrue(MidiMixInputMap.TryGetButton(1, out var b));
            Assert.AreEqual(MidiMixButton.Mute, b.type);
            Assert.AreEqual(1, b.channel);
        }

        [Test]
        public void ButtonLookup_SoloNote_ReturnsSoloType()
        {
            Assert.IsTrue(MidiMixInputMap.TryGetButton(2, out var b));
            Assert.AreEqual(MidiMixButton.Solo, b.type);
        }

        [Test]
        public void ButtonLookup_RecArmNote_ReturnsRecArmType()
        {
            Assert.IsTrue(MidiMixInputMap.TryGetButton(3, out var b));
            Assert.AreEqual(MidiMixButton.RecArm, b.type);
        }

        // ------------------------------------------------------------------ //
        // Bank / Solo modifier notes
        // ------------------------------------------------------------------ //

        [Test] public void BankLeftNote_Is25()      => Assert.AreEqual(25, MidiMixInputMap.BankLeftNote);
        [Test] public void BankRightNote_Is26()     => Assert.AreEqual(26, MidiMixInputMap.BankRightNote);
        [Test] public void SoloModifierNote_Is27()  => Assert.AreEqual(27, MidiMixInputMap.SoloModifierNote);

        [Test] public void IsBankLeft_25_True()          => Assert.IsTrue(MidiMixInputMap.IsBankLeft(25));
        [Test] public void IsBankRight_26_True()         => Assert.IsTrue(MidiMixInputMap.IsBankRight(26));
        [Test] public void IsSoloModifier_27_True()      => Assert.IsTrue(MidiMixInputMap.IsSoloModifier(27));
        [Test] public void IsBankLeft_Other_False()      => Assert.IsFalse(MidiMixInputMap.IsBankLeft(1));

        // ------------------------------------------------------------------ //
        // Unknown notes/CCs return false
        // ------------------------------------------------------------------ //

        [Test] public void TryGetKnob_UnknownCC_False()    => Assert.IsFalse(MidiMixInputMap.TryGetKnob(0,   out _));
        [Test] public void TryGetFader_UnknownCC_False()   => Assert.IsFalse(MidiMixInputMap.TryGetFader(50, out _));
        [Test] public void TryGetButton_UnknownNote_False() => Assert.IsFalse(MidiMixInputMap.TryGetButton(99, out _));
    }
}
