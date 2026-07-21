using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MidiFighter64
{
    /// <summary>
    /// Bridge between the Minis MIDI input package and Unity.
    /// Subscribes to all Minis MidiDevice callbacks and re-exposes them
    /// as simple C# events with note number / velocity arguments.
    ///
    /// Every MIDI input port on the system is connected and merged into one
    /// event stream unless <see cref="AllowedDeviceNames"/> /
    /// <see cref="BlockedDeviceNames"/> narrow it. Filter when a MIDI monitor,
    /// loopback, or network port carries a *copy* of a controller's traffic —
    /// otherwise every message is delivered twice, which silently cancels out
    /// any press-to-toggle logic downstream (see MidiMixRouter.LatchMute).
    /// </summary>
    public class MidiEventManager : MonoBehaviour
    {
        public static MidiEventManager Instance { get; private set; }

        [Header("Device filter")]
        [Tooltip("If non-empty, only MIDI input ports whose name contains one of these fragments are connected. " +
                 "Case-insensitive substring match, e.g. \"MIDI Mix\". Leave empty to connect to every port.")]
        [SerializeField] string[] _allowedDeviceNames = new string[0];

        [Tooltip("MIDI input ports whose name contains one of these fragments are never connected — applied after the " +
                 "allow list. Use it to block monitors and loopback ports that echo another device's traffic.")]
        [SerializeField] string[] _blockedDeviceNames = new string[0];

        /// <summary>Substrings of port names to connect to. Empty = connect to all. Call <see cref="Reconnect"/> after changing.</summary>
        public string[] AllowedDeviceNames { get => _allowedDeviceNames; set => _allowedDeviceNames = value ?? new string[0]; }

        /// <summary>Substrings of port names to never connect to. Applied after the allow list.</summary>
        public string[] BlockedDeviceNames { get => _blockedDeviceNames; set => _blockedDeviceNames = value ?? new string[0]; }

        /// <summary>Sets both filters and re-runs device discovery in one step.</summary>
        public void SetDeviceFilter(string[] allowed, string[] blocked)
        {
            AllowedDeviceNames = allowed;
            BlockedDeviceNames = blocked;
            Reconnect();
        }

        /// <summary>Drops all device subscriptions and re-runs discovery against the current filters.</summary>
        public void Reconnect()
        {
            if (!isActiveAndEnabled) return;
            DisconnectAllDevices();
            ConnectAllDevices();
        }

        public static event Action<int, float> OnNoteOn;        // noteNumber, velocity 0-1
        public static event Action<int>        OnNoteOff;       // noteNumber
        public static event Action<int, float> OnControlChange; // controlNumber, value 0-1

        public string DeviceName { get; private set; } = "No MIDI Device";

        readonly List<Minis.MidiDevice> _devices = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance        = null;
            OnNoteOn        = null;
            OnNoteOff       = null;
            OnControlChange = null;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnEnable()
        {
            InputSystem.onDeviceChange += HandleDeviceChange;
            ConnectAllDevices();
        }

        void OnDisable()
        {
            DisconnectAllDevices();
            InputSystem.onDeviceChange -= HandleDeviceChange;
        }

        void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is not Minis.MidiDevice) return;

            Debug.Log($"[MidiEventManager] Device {change}: {device.description.product} — reconnecting...");
            DisconnectAllDevices();
            ConnectAllDevices();
        }

        void ConnectAllDevices()
        {
            int found = 0;
            foreach (var device in InputSystem.devices)
            {
                if (device is not Minis.MidiDevice midi) continue;

                string name = device.description.product ?? device.displayName;
                if (!IsDeviceAllowed(name))
                {
                    Debug.Log($"[MidiEventManager] Skipped (device filter): {name}");
                    continue;
                }

                midi.onWillNoteOn        += HandleNoteOn;
                midi.onWillNoteOff       += HandleNoteOff;
                midi.onWillControlChange += HandleControlChange;
                _devices.Add(midi);

                DeviceName = name;
                found++;
                Debug.Log($"[MidiEventManager] Connected: {DeviceName}");
            }

            if (found == 0)
                Debug.Log("[MidiEventManager] No MIDI devices in InputSystem yet — connect device and touch any control to trigger detection.");
        }

        bool IsDeviceAllowed(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (_allowedDeviceNames != null && _allowedDeviceNames.Length > 0 &&
                !ContainsAnyFragment(name, _allowedDeviceNames)) return false;
            if (_blockedDeviceNames != null && _blockedDeviceNames.Length > 0 &&
                ContainsAnyFragment(name, _blockedDeviceNames)) return false;
            return true;
        }

        static bool ContainsAnyFragment(string name, string[] fragments)
        {
            foreach (var fragment in fragments)
                if (!string.IsNullOrWhiteSpace(fragment) &&
                    name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        void DisconnectAllDevices()
        {
            foreach (var midi in _devices)
            {
                midi.onWillNoteOn       -= HandleNoteOn;
                midi.onWillNoteOff      -= HandleNoteOff;
                midi.onWillControlChange -= HandleControlChange;
            }
            _devices.Clear();
        }

        void HandleNoteOn(Minis.MidiNoteControl note, float velocity)
        {
            WarnOnDuplicateDelivery(note);
            OnNoteOn?.Invoke(note.noteNumber, velocity);
        }

        void HandleNoteOff(Minis.MidiNoteControl note)
            => OnNoteOff?.Invoke(note.noteNumber);

        void HandleControlChange(Minis.MidiValueControl control, float value)
            => OnControlChange?.Invoke(control.controlNumber, value);

        // Two ports carrying a copy of the same stream — a MIDI monitor, a
        // loopback, a network session — deliver every note twice in one frame.
        // Momentary consumers can't tell (on/on then off/off lands in the same
        // place); anything press-to-toggle silently cancels itself out. Warns
        // once per session, because the symptom points nowhere near the cause.
        int    _lastNoteOnNote  = -1;
        int    _lastNoteOnFrame = -1;
        string _lastNoteOnDevice;
        bool   _duplicateWarned;

        void WarnOnDuplicateDelivery(Minis.MidiNoteControl note)
        {
            string device = note.device.description.product ?? note.device.displayName;

            if (!_duplicateWarned &&
                Time.frameCount == _lastNoteOnFrame &&
                note.noteNumber == _lastNoteOnNote &&
                device          != _lastNoteOnDevice)
            {
                _duplicateWarned = true;
                Debug.LogWarning(
                    $"[MidiEventManager] Note {note.noteNumber} arrived from two MIDI ports in the same frame " +
                    $"('{_lastNoteOnDevice}' and '{device}'). One is echoing the other, so every message is being " +
                    $"handled twice — press-to-toggle controls will appear dead. Add the echoing port's name to " +
                    $"Blocked Device Names (or list only the real controllers in Allowed Device Names).");
            }

            _lastNoteOnNote   = note.noteNumber;
            _lastNoteOnFrame  = Time.frameCount;
            _lastNoteOnDevice = device;
        }
    }
}
