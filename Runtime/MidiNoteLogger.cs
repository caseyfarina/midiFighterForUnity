using UnityEngine;

namespace MidiFighter64
{
    /// <summary>
    /// Diagnostic example: logs every raw MIDI note/CC to the console. Ships
    /// in Runtime and is easy to leave enabled by accident — an unthrottled
    /// Debug.Log per event can cause frame hitches under input flooding
    /// (e.g. the MIDI Mix "Send All" burst). Turn off logging once you've
    /// confirmed your MIDI mapping, or leave <see cref="_minLogInterval"/>
    /// set for a live show.
    /// </summary>
    public class MidiNoteLogger : MonoBehaviour
    {
        [Tooltip("Enable/disable console logging without removing the component.")]
        [SerializeField] bool _enableLogging = true;

        [Tooltip("Minimum seconds between log lines. 0 = log every event (default, matches prior behaviour).")]
        [SerializeField] float _minLogInterval = 0f;

        private int _pressCount;
        private int _ccCount;
        private float _nextLogTime;

        void OnEnable()
        {
            _pressCount = 0;
            _ccCount = 0;
            _nextLogTime = 0f;
            MidiEventManager.OnNoteOn        += HandleNoteOn;
            MidiEventManager.OnNoteOff       += HandleNoteOff;
            MidiEventManager.OnControlChange += HandleCC;
            if (_enableLogging) Debug.Log("[MidiNoteLogger] Listening for raw MIDI notes + CCs.");
        }

        void OnDisable()
        {
            MidiEventManager.OnNoteOn        -= HandleNoteOn;
            MidiEventManager.OnNoteOff       -= HandleNoteOff;
            MidiEventManager.OnControlChange -= HandleCC;
        }

        bool ShouldLog()
        {
            if (!_enableLogging) return false;
            if (_minLogInterval <= 0f) return true;
            if (Time.unscaledTime < _nextLogTime) return false;
            _nextLogTime = Time.unscaledTime + _minLogInterval;
            return true;
        }

        void HandleNoteOn(int noteNumber, float velocity)
        {
            _pressCount++;
            if (ShouldLog()) Debug.Log($"[MidiNoteLogger] NOTE_ON  #{_pressCount:D3}  note={noteNumber}  vel={velocity:F2}");
        }

        void HandleNoteOff(int noteNumber)
        {
            if (ShouldLog()) Debug.Log($"[MidiNoteLogger] NOTE_OFF note={noteNumber}");
        }

        void HandleCC(int cc, float value)
        {
            _ccCount++;
            if (ShouldLog()) Debug.Log($"[MidiNoteLogger] CC #{_ccCount:D3}  cc={cc}  val={value:F2}");
        }
    }
}
