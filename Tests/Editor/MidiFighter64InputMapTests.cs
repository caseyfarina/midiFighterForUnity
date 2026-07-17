using NUnit.Framework;

namespace MidiFighter64.Tests
{
    /// <summary>
    /// Edit-mode tests locking the confirmed split-half MF64 grid layout.
    /// Hardware-confirmed corners: top-left=64, top-right=99, bottom-left=36, bottom-right=71.
    /// Do NOT change MidiFighter64InputMap.FromNote() — split-half is verified on hardware.
    /// </summary>
    public class MidiFighter64InputMapTests
    {
        // ------------------------------------------------------------------ //
        // Corner assertions — hardware-confirmed on Channel 3
        // ------------------------------------------------------------------ //

        [Test]
        public void FromNote_TopLeftCorner_Note64_Row1Col1()
        {
            var btn = MidiFighter64InputMap.FromNote(64);
            Assert.AreEqual(1, btn.row, "top-left row");
            Assert.AreEqual(1, btn.col, "top-left col");
        }

        [Test]
        public void FromNote_TopRightCorner_Note99_Row1Col8()
        {
            var btn = MidiFighter64InputMap.FromNote(99);
            Assert.AreEqual(1, btn.row, "top-right row");
            Assert.AreEqual(8, btn.col, "top-right col");
        }

        [Test]
        public void FromNote_BottomLeftCorner_Note36_Row8Col1()
        {
            var btn = MidiFighter64InputMap.FromNote(36);
            Assert.AreEqual(8, btn.row, "bottom-left row");
            Assert.AreEqual(1, btn.col, "bottom-left col");
        }

        [Test]
        public void FromNote_BottomRightCorner_Note71_Row8Col8()
        {
            // Split-half formula: half=1 (71>=68), offset=3, physicalRow=0 → row=8, col=3+1+4=8
            var btn = MidiFighter64InputMap.FromNote(71);
            Assert.AreEqual(8, btn.row, "bottom-right row");
            Assert.AreEqual(8, btn.col, "bottom-right col");
        }

        // ------------------------------------------------------------------ //
        // Split-half boundary: first note of right half
        // ------------------------------------------------------------------ //

        [Test]
        public void FromNote_Note68_Row8Col5()
        {
            // 68 = first note of right half (cols 5-8), bottom row
            var btn = MidiFighter64InputMap.FromNote(68);
            Assert.AreEqual(8, btn.row);
            Assert.AreEqual(5, btn.col);
        }

        [Test]
        public void FromNote_Note67_Row1Col4()
        {
            // 67 = last note of left half (cols 1-4), top row
            var btn = MidiFighter64InputMap.FromNote(67);
            Assert.AreEqual(1, btn.row);
            Assert.AreEqual(4, btn.col);
        }

        // ------------------------------------------------------------------ //
        // linearIndex and noteNumber round-trip
        // ------------------------------------------------------------------ //

        [Test]
        public void FromNote_LinearIndex_TopLeft_IsZero()
        {
            Assert.AreEqual(0, MidiFighter64InputMap.FromNote(64).linearIndex);
        }

        [Test]
        public void FromNote_LinearIndex_BottomRight_Is63()
        {
            Assert.AreEqual(63, MidiFighter64InputMap.FromNote(71).linearIndex);
        }

        [Test]
        public void FromNote_NoteNumber_PreservedRoundTrip()
        {
            Assert.AreEqual(64, MidiFighter64InputMap.FromNote(64).noteNumber);
            Assert.AreEqual(36, MidiFighter64InputMap.FromNote(36).noteNumber);
        }

        // ------------------------------------------------------------------ //
        // IsInRange
        // ------------------------------------------------------------------ //

        [Test]
        public void IsInRange_Note36_True()  => Assert.IsTrue(MidiFighter64InputMap.IsInRange(36));
        [Test]
        public void IsInRange_Note99_True()  => Assert.IsTrue(MidiFighter64InputMap.IsInRange(99));
        [Test]
        public void IsInRange_Note35_False() => Assert.IsFalse(MidiFighter64InputMap.IsInRange(35));
        [Test]
        public void IsInRange_Note100_False() => Assert.IsFalse(MidiFighter64InputMap.IsInRange(100));

        // ------------------------------------------------------------------ //
        // GridButton.IsValid matches IsInRange
        // ------------------------------------------------------------------ //

        [Test]
        public void GridButton_IsValid_MatchesIsInRange_ForCornerNotes()
        {
            foreach (int note in new[] { 36, 64, 71, 99 })
            {
                var btn = MidiFighter64InputMap.FromNote(note);
                Assert.AreEqual(MidiFighter64InputMap.IsInRange(note), btn.IsValid,
                    $"IsValid mismatch for note {note}");
            }
        }
    }
}
