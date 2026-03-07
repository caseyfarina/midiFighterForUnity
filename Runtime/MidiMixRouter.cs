using System;
using UnityEngine;

namespace MidiFighter64
{
    /// <summary>
    /// Subscribes to MidiEventManager and converts raw MIDI events into typed,
    /// Akai MIDI Mix–specific C# events. Add this MonoBehaviour to the same
    /// GameObject as MidiEventManager (or any active GameObject in the scene).
    ///
    /// Event summary:
    ///   OnKnob(channel, row, value)   — a knob was turned
    ///   OnChannelFader(channel, value) — a channel fader moved
    ///   OnMasterFader(value)           — the master fader moved
    ///   OnMute(channel, isNoteOn)      — a Mute button was pressed/released
    ///   OnRecArm(channel, isNoteOn)    — a Rec Arm button was pressed/released
    ///   OnBankLeft / OnBankRight       — bank navigation buttons pressed
    ///
    /// Raw variants (OnKnobRaw, OnFaderRaw, OnButtonRaw) carry the full struct
    /// for cases where you need the CC/note number or want to switch on type.
    ///
    /// All channel arguments are 1-based (1–8). Fader/knob values are 0–1.
    /// Override RouteCC() or RouteNote() in a subclass for custom behaviour.
    /// </summary>
    public class MidiMixRouter : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Public events
        // ------------------------------------------------------------------ //

        /// <summary>A knob was turned. Args: channel (1–8), row (1–3), value (0–1).</summary>
        public static event Action<int, int, float> OnKnob;

        /// <summary>A channel fader moved. Args: channel (1–8), value (0–1).</summary>
        public static event Action<int, float> OnChannelFader;

        /// <summary>The master fader moved. Args: value (0–1).</summary>
        public static event Action<float> OnMasterFader;

        /// <summary>A Mute button was pressed or released. Args: channel (1–8), isNoteOn.</summary>
        public static event Action<int, bool> OnMute;

        /// <summary>
        /// A Mute button in Solo mode was pressed or released. Args: channel (1–8), isNoteOn.
        /// The hardware SOLO toggle causes the Mute row to emit a different note set.
        /// </summary>
        public static event Action<int, bool> OnSolo;

        /// <summary>A Rec Arm button was pressed or released. Args: channel (1–8), isNoteOn.</summary>
        public static event Action<int, bool> OnRecArm;

        /// <summary>A Rec Arm button in shifted mode was pressed or released. Args: channel (1–8), isNoteOn.</summary>
        public static event Action<int, bool> OnRecArmShifted;

        /// <summary>The Bank Left button was pressed.</summary>
        public static event Action OnBankLeft;

        /// <summary>The Bank Right button was pressed.</summary>
        public static event Action OnBankRight;

        // Raw events — useful when you need the full struct or want to fan-out
        // custom routing without subclassing.
        public static event Action<MixKnob,  float> OnKnobRaw;
        public static event Action<MixFader, float> OnFaderRaw;
        public static event Action<MixButton, bool> OnButtonRaw;

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        void OnEnable()
        {
            MidiEventManager.OnControlChange += HandleCC;
            MidiEventManager.OnNoteOn        += HandleNoteOn;
            MidiEventManager.OnNoteOff       += HandleNoteOff;
        }

        void OnDisable()
        {
            MidiEventManager.OnControlChange -= HandleCC;
            MidiEventManager.OnNoteOn        -= HandleNoteOn;
            MidiEventManager.OnNoteOff       -= HandleNoteOff;
        }

        // ------------------------------------------------------------------ //
        // Handlers
        // ------------------------------------------------------------------ //

        void HandleCC(int ccNumber, float value) => RouteCC(ccNumber, value);

        void HandleNoteOn(int noteNumber, float velocity)  => RouteNote(noteNumber, isNoteOn: true);
        void HandleNoteOff(int noteNumber)                 => RouteNote(noteNumber, isNoteOn: false);

        // ------------------------------------------------------------------ //
        // Routing — override in a subclass for custom logic
        // ------------------------------------------------------------------ //

        protected virtual void RouteCC(int ccNumber, float value)
        {
            if (MidiMixInputMap.TryGetKnob(ccNumber, out var knob))
            {
                OnKnobRaw?.Invoke(knob, value);
                OnKnob?.Invoke(knob.channel, knob.row, value);
                return;
            }

            if (MidiMixInputMap.TryGetFader(ccNumber, out var fader))
            {
                OnFaderRaw?.Invoke(fader, value);
                if (fader.isMaster)
                    OnMasterFader?.Invoke(value);
                else
                    OnChannelFader?.Invoke(fader.channel, value);
            }
        }

        protected virtual void RouteNote(int noteNumber, bool isNoteOn)
        {
            if (MidiMixInputMap.TryGetButton(noteNumber, out var button))
            {
                OnButtonRaw?.Invoke(button, isNoteOn);
                switch (button.type)
                {
                    case MidiMixButton.Mute:          OnMute?.Invoke(button.channel, isNoteOn);          break;
                    case MidiMixButton.Solo:          OnSolo?.Invoke(button.channel, isNoteOn);          break;
                    case MidiMixButton.RecArm:        OnRecArm?.Invoke(button.channel, isNoteOn);        break;
                    case MidiMixButton.RecArmShifted: OnRecArmShifted?.Invoke(button.channel, isNoteOn); break;
                }
                return;
            }

            if (isNoteOn && MidiMixInputMap.IsBankLeft(noteNumber))  OnBankLeft?.Invoke();
            if (isNoteOn && MidiMixInputMap.IsBankRight(noteNumber)) OnBankRight?.Invoke();
        }
    }
}
