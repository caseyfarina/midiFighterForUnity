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
    /// </summary>
    public class MidiEventManager : MonoBehaviour
    {
        public static MidiEventManager Instance { get; private set; }

        public static event Action<int, float> OnNoteOn;        // noteNumber, velocity 0-1
        public static event Action<int>        OnNoteOff;       // noteNumber
        public static event Action<int, float> OnControlChange; // controlNumber, value 0-1

        public string DeviceName { get; private set; } = "No MIDI Device";

        readonly List<Minis.MidiDevice> _devices = new();

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

            DisconnectAllDevices();
            ConnectAllDevices();
        }

        void ConnectAllDevices()
        {
            foreach (var device in InputSystem.devices)
            {
                if (device is not Minis.MidiDevice midi) continue;

                midi.onWillNoteOn       += HandleNoteOn;
                midi.onWillNoteOff      += HandleNoteOff;
                midi.onWillControlChange += HandleControlChange;
                _devices.Add(midi);

                DeviceName = device.description.product ?? device.displayName;
            }
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

        static void HandleNoteOn(Minis.MidiNoteControl note, float velocity)
            => OnNoteOn?.Invoke(note.noteNumber, velocity);

        static void HandleNoteOff(Minis.MidiNoteControl note)
            => OnNoteOff?.Invoke(note.noteNumber);

        static void HandleControlChange(Minis.MidiValueControl control, float value)
            => OnControlChange?.Invoke(control.controlNumber, value);
    }
}
