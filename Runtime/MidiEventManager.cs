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

        // The two setters deliberately only assign — they do NOT reconnect. Setting
        // both would otherwise re-run discovery twice; batch the changes, then call
        // Reconnect() once (or use SetDeviceFilter, which does exactly that).
        /// <summary>Substrings of port names to connect to. Empty = connect to all. Call <see cref="Reconnect"/> after changing.</summary>
        public string[] AllowedDeviceNames { get => _allowedDeviceNames; set => _allowedDeviceNames = value ?? new string[0]; }

        /// <summary>Substrings of port names to never connect to. Applied after the allow list.</summary>
        public string[] BlockedDeviceNames { get => _blockedDeviceNames; set => _blockedDeviceNames = value ?? new string[0]; }

        /// <summary>Sets both filters and re-runs device discovery in one step. The
        /// combined primitive, so a filter change costs one reconnect rather than the
        /// two that setting the properties separately and reconnecting would.</summary>
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

            // Reconnect() runs far more often than a hot-plug does: the Input System
            // reports a HardReset for every device each time the editor regains focus,
            // and each one lands here. Raising the reconnect event on those would re-push
            // every LED on every alt-tab. Compare device *ids* across the reconnect
            // instead — a HardReset keeps the same InputDevice, while a genuine
            // remove/re-add produces a new one, so this distinguishes the two even when
            // the port names are identical before and after (unplug/replug of one device).
            string before = DeviceSignature();
            DisconnectAllDevices();
            ConnectAllDevices();
            if (DeviceSignature() != before) OnDevicesReconnected?.Invoke();
        }

        string DeviceSignature()
        {
            if (_devices.Count == 0) return string.Empty;
            var ids = new List<int>(_devices.Count);
            foreach (var midi in _devices) ids.Add(midi.deviceId);
            ids.Sort();
            return string.Join(",", ids);
        }

        public static event Action<int, float> OnNoteOn;        // noteNumber, velocity 0-1
        public static event Action<int>        OnNoteOff;       // noteNumber
        public static event Action<int, float> OnControlChange; // controlNumber, value 0-1

        /// <summary>
        /// Raised after <see cref="Reconnect"/> has re-run device discovery and the set of
        /// connected devices actually changed - i.e. after a real hot-plug, or a filter
        /// edit that changed what connects. Deliberately does <b>not</b> fire for the
        /// HardReset the Input System reports for every device on editor focus, which
        /// would otherwise re-push every LED on every alt-tab.
        ///
        /// Reconnect restores <b>input</b> subscriptions only. Anything holding
        /// <b>output</b> state - hardware LEDs above all - is stale at that point and must
        /// re-push, or the surface silently disagrees with application state. That is worse
        /// than dark LEDs, because the hardware is then lying to the performer.
        /// <see cref="MidiFighterButtonRouter"/> re-pushes its toggle colours off this;
        /// subscribe here if you drive LEDs yourself.
        ///
        /// Fires on the main thread. The MF64 output port may not be reopen yet when this
        /// lands, so gate a re-push on <see cref="MidiFighterOutput.IsReady"/> - or use
        /// <see cref="MidiFighterOutput.OnOutputPortOpened"/>, which fires when it is.
        /// </summary>
        public static event Action OnDevicesReconnected;

        public string DeviceName { get; private set; } = "No MIDI Device";

        readonly List<Minis.MidiDevice> _devices = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance        = null;
            OnNoteOn        = null;
            OnNoteOff       = null;
            OnControlChange = null;
            OnDevicesReconnected = null;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroys the whole GameObject, not just this component. On the
                // shipped prefab that GameObject also carries the routers, both
                // Output components, and the status drawer, so a second rig
                // (per-scene, additively loaded, etc.) loses all of them, not
                // just the manager. This package assumes exactly one rig; if you
                // need per-scene rigs, split MidiEventManager onto its own
                // GameObject away from the other seven components.
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

        // Only reconnect while playing: OnValidate also fires in edit mode and
        // during deserialization, where there are no live devices to bind and the
        // guard in Reconnect wouldn't distinguish that from a real disable. Matches
        // MidiFighterButtonRouter's OnValidate, which gates its LED preview the same
        // way. This is what makes editing the filter in the inspector during play
        // take effect immediately — the whole point of the filter is diagnosing a
        // duplicate port live, so it has to respond live.
        void OnValidate()
        {
            if (Application.isPlaying) Reconnect();
        }

        void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is not Minis.MidiDevice) return;

            Debug.Log($"[MidiEventManager] Device {change}: {device.description.product} — reconnecting...");
            Reconnect();
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

            WarnOnPortClutter();
        }

        // ------------------------------------------------------------------
        // Port clutter
        // ------------------------------------------------------------------

        // Name fragments that identify a port as virtual — a software loopback, a
        // router, a network session, a driver's test endpoint. Substring, case-
        // insensitive, deliberately broad: a false positive costs one console line,
        // a false negative hides the hazard. Extend via VirtualPortNameHints.
        static readonly string[] DefaultVirtualPortHints =
        {
            "virtual", "loopmidi", "loopbe", "loop device", "bome", "iac driver",
            "network session", "rtpmidi", "midi yoke", "maple", "midi 2.0",
            "tobias erichsen", "haute technique",
        };

        /// <summary>Extra name fragments to treat as virtual ports in the startup clutter
        /// warning. Additive to the built-in list; affects the warning only, never which
        /// devices connect (use <see cref="BlockedDeviceNames"/> for that).</summary>
        public static string[] VirtualPortNameHints { get; set; } = new string[0];

        // Signature of the last port set we warned about, so a stable setup warns once
        // rather than on every Reconnect — but a genuinely new port still gets reported.
        string _lastClutterSignature;

        // Minis opens EVERY MIDI input port on the system, unconditionally, before the
        // Input System ever surfaces a device here. Every open port is one more handle
        // that must later be freed, and one bad free is a native terminate() that kills
        // the process (see README, "MIDI hot-plug hazard"). The device filter above cannot
        // help: it runs downstream of the opening and only decides what we subscribe to.
        //
        // So the only thing this package can do about a cluttered MIDI stack is make it
        // visible. Duplicates additionally break latching outright, which is why the same
        // fault has a second, more specific warning in WarnOnDuplicateDelivery.
        void WarnOnPortClutter()
        {
            // Minis names a device "<port name> Channel <n>" (MidiUtility.MakeDeviceDescription),
            // so one port shows up as up to 16 devices. Collapse to distinct ports first —
            // multiple channels off one port are normal, not clutter.
            var ports = new List<string>();
            foreach (var device in InputSystem.devices)
            {
                if (device is not Minis.MidiDevice) continue;
                string port = PortNameOf(device.description.product ?? device.displayName);
                if (!string.IsNullOrWhiteSpace(port) && !ports.Contains(port)) ports.Add(port);
            }
            if (ports.Count == 0) return;

            // Duplicates do NOT share a name: WinMM appends its own index, so four copies
            // of one controller read as "MIDI Mix 0", "MIDI Mix 2", "MIDI Mix 3"... Group
            // by the name with that trailing index stripped, and call a base name
            // duplicated when two distinct ports share it.
            var byBase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string port in ports)
            {
                string base_ = BasePortName(port);
                byBase[base_] = byBase.TryGetValue(base_, out int n) ? n + 1 : 1;
            }

            var duplicates = new List<string>();
            foreach (var pair in byBase)
                if (pair.Value > 1) duplicates.Add($"{pair.Key} (x{pair.Value})");

            var virtuals = new List<string>();
            foreach (string port in ports)
                if (IsVirtualPortName(port)) virtuals.Add(port);

            if (duplicates.Count == 0 && virtuals.Count == 0) { _lastClutterSignature = null; return; }

            ports.Sort(StringComparer.OrdinalIgnoreCase);
            string signature = string.Join("|", ports);
            if (signature == _lastClutterSignature) return;
            _lastClutterSignature = signature;

            var message = new System.Text.StringBuilder();
            message.Append($"[MidiEventManager] MIDI port clutter detected ({ports.Count} input ports open). ");
            if (duplicates.Count > 0)
                message.Append($"Duplicated: {string.Join(", ", duplicates)}. ");
            if (virtuals.Count > 0)
                message.Append($"Virtual/loopback: {string.Join(", ", virtuals)}. ");
            message.Append(
                "Minis opens every one of these regardless of the device filter, and each open port is " +
                "another chance for a bad close to hard-crash the process on hot-plug or domain reload " +
                "(see README, \"MIDI hot-plug hazard\"). Uninstalling MIDI drivers you don't use lowers " +
                "that risk. Duplicated ports also deliver every message twice, which makes press-to-toggle " +
                "controls appear dead — block them by name in Blocked Device Names.");
            Debug.LogWarning(message.ToString());
        }

        // "MIDI Mix 0 Channel 3" -> "MIDI Mix 0". Minis' own naming convention; if it ever
        // changes, the string is still returned unharmed, just less precisely grouped.
        static string PortNameOf(string productName)
        {
            if (string.IsNullOrEmpty(productName)) return productName;
            int i = productName.LastIndexOf(" Channel ", StringComparison.Ordinal);
            return i > 0 ? productName.Substring(0, i) : productName;
        }

        // "MIDI Mix 0" -> "MIDI Mix". Strips the trailing index the OS appends to
        // distinguish same-named ports, which is exactly what makes duplicates of one
        // device read as different names.
        static string BasePortName(string portName)
        {
            if (string.IsNullOrEmpty(portName)) return portName;
            int end = portName.Length;
            while (end > 0 && char.IsDigit(portName[end - 1])) end--;
            if (end == portName.Length || end == 0) return portName;   // no trailing index
            if (portName[end - 1] != ' ') return portName;             // digits are part of the name ("MPK249")
            return portName.Substring(0, end - 1);
        }

        static bool IsVirtualPortName(string name)
        {
            if (ContainsAnyFragment(name, DefaultVirtualPortHints)) return true;
            var extra = VirtualPortNameHints;
            return extra != null && extra.Length > 0 && ContainsAnyFragment(name, extra);
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

        // Guarded per device so one bad handle cannot leave the rest subscribed to a
        // manager that is tearing down. Hygiene only. This runs on the same domain-reload
        // path as the native RtMidi crash documented in README, but it is NOT a guard
        // against it: that failure is a C++ terminate() inside the native port close, so
        // managed code never runs again and there is nothing here to catch.
        void DisconnectAllDevices()
        {
            foreach (var midi in _devices)
            {
                try
                {
                    midi.onWillNoteOn        -= HandleNoteOn;
                    midi.onWillNoteOff       -= HandleNoteOff;
                    midi.onWillControlChange -= HandleControlChange;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MidiEventManager] Teardown failed for a device: {e.Message}");
                }
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
