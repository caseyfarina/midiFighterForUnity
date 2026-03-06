using System;
using UnityEngine;

namespace MidiFighter64
{
    /// <summary>
    /// Subscribes to MidiEventManager and converts raw notes to typed grid events
    /// based on the Midi Fighter 64's 8x8 layout. Consumers subscribe to the
    /// static events they care about.
    ///
    /// Default row assignments:
    ///   Row 1:   OnRow1 (e.g. camera select)
    ///   Row 2-4: OnGridPreset (col 1-7) / OnGridRandomize (col 8)
    ///   Row 5:   OnRow5 (e.g. light toggles)
    ///   Row 6-8: OnSlotToggle (24 slots, note on/off for momentary)
    ///
    /// Override RouteButton() in a subclass to customize the routing.
    /// </summary>
    public class MidiGridRouter : MonoBehaviour
    {
        // Row 1 button pressed (col 1-8)
        public static event Action<int> OnRow1;

        // Rows 2-4, cols 1-7: grid preset selected (row, col)
        public static event Action<int, int> OnGridPreset;

        // Rows 2-4, col 8: randomize for that row
        public static event Action<int> OnGridRandomize;

        // Row 5 button pressed (col 1-8)
        public static event Action<int> OnRow5;

        // Rows 6-8: slot toggle (slot 1-24, isNoteOn)
        public static event Action<int, bool> OnSlotToggle;

        // Raw grid button event for any custom routing (button, isNoteOn)
        public static event Action<GridButton, bool> OnGridButton;

        void OnEnable()
        {
            MidiEventManager.OnNoteOn  += HandleNoteOn;
            MidiEventManager.OnNoteOff += HandleNoteOff;
        }

        void OnDisable()
        {
            MidiEventManager.OnNoteOn  -= HandleNoteOn;
            MidiEventManager.OnNoteOff -= HandleNoteOff;
        }

        void HandleNoteOn(int noteNumber, float velocity)
        {
            if (!MidiFighter64InputMap.IsInRange(noteNumber)) return;

            var btn = MidiFighter64InputMap.FromNote(noteNumber);
            OnGridButton?.Invoke(btn, true);
            RouteButton(btn, isNoteOn: true);
        }

        void HandleNoteOff(int noteNumber)
        {
            if (!MidiFighter64InputMap.IsInRange(noteNumber)) return;

            var btn = MidiFighter64InputMap.FromNote(noteNumber);
            OnGridButton?.Invoke(btn, false);
            RouteButton(btn, isNoteOn: false);
        }

        protected virtual void RouteButton(GridButton btn, bool isNoteOn)
        {
            switch (btn.row)
            {
                case 1:
                    if (isNoteOn) OnRow1?.Invoke(btn.col);
                    break;

                case 2:
                case 3:
                case 4:
                    if (!isNoteOn) break;
                    if (btn.col == 8)
                        OnGridRandomize?.Invoke(btn.row);
                    else
                        OnGridPreset?.Invoke(btn.row, btn.col);
                    break;

                case 5:
                    if (isNoteOn) OnRow5?.Invoke(btn.col);
                    break;

                case 6:
                case 7:
                case 8:
                    int slot = ((btn.row - 6) * 8) + btn.col; // 1-24
                    OnSlotToggle?.Invoke(slot, isNoteOn);
                    break;
            }
        }
    }
}
