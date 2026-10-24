# Midi Fighter 64 for Unity

MIDI input and LED output bridge for the [DJ Tech Tools Midi Fighter 64](https://www.midifighter.com/) grid controller and the [Akai MIDI Mix](https://www.akaipro.com/midimix) in Unity. Converts raw MIDI into logical grid coordinates and typed C# events.

- **MF64 grid** — 8×8 pads as `(row, col)` events, per-pad Button/Toggle modes, LED output with a hardware-verified color palette.
- **Akai MIDI Mix** — 24 knobs, 9 faders, and 24 buttons as typed events; Mute and Rec-Arm latch (press-to-toggle) by default and the host echoes their LEDs.
- **Status drawer** — an optional screen-space overlay mirroring both controllers live, with dark/light themes and adjustable opacity, stroke weight and scale.

Full documentation is in [`Documentation~/index.html`](Documentation~/index.html) — open it locally, or use **View documentation** in Package Manager.

---

## ⚠️ MIDI hot-plug crash — install the fixed RtMidi before live use

**With stock dependencies, connecting or disconnecting any MIDI device while Unity or a
build of your project is running can hard-crash the process**: no exception, no dialog,
no recovery. It affects shipped builds as well as the editor.

**It is fixed, one layer down, in RtMidi.** The cause was RtMidi 6.0.0 (bundled by
`jp.keijiro.rtmidi` 2.2.0, which Minis depends on): when a device vanished, WinMM's
`closePort()` threw `RtMidiError` from a destructor, and the process hit `terminate()`.
[RtMidi 6.1.0](https://github.com/thestk/rtmidi/releases/tag/6.1.0) fixed it
([thestk/rtmidi#376](https://github.com/thestk/rtmidi/issues/376)). Until
`jp.keijiro.rtmidi` ships 6.1.0, add this fork, with its Windows DLL rebuilt from 6.1.0,
to your project's `Packages/manifest.json`:

```json
"jp.keijiro.rtmidi": "https://github.com/caseyfarina/jp.keijiro.rtmidi.git?path=Packages/jp.keijiro.rtmidi#2.2.1-cf.1"
```

It overrides the RtMidi that Minis pulls in; keep stock Minis. A Unity package cannot
declare a git dependency, so this package cannot do it for you. Verified on hardware with
an MF64 and a MIDI Mix: unplug and replug in Edit and Play mode, and domain reloads with
both connected, all survive. This package then reconnects and re-pushes the LEDs on its
own.

> An earlier workaround patched Minis to abandon handles instead of freeing them
> (`MINIS_HOTPLUG_PATCH.md`). It is superseded: don't apply it, and remove it where it is.

### Without the fix

If you are on stock `jp.keijiro.rtmidi` (Windows), don't hot-plug while running. What
makes it dangerous is that almost none of it is obvious:

- **The trigger is a change in the system's MIDI port *count*, in either direction.**
  Plugging a controller in is as risky as pulling one out, and any app creating or
  destroying a virtual MIDI port (a DAW, Bome, loopMIDI, a MIDI monitor) trips it.
- **It often does not crash when you touch the cable.** It can land frames later, or on
  the next domain reload after a script edit, so it looks unrelated to MIDI.
- **Nothing in this package can prevent it.** The device allow list filters
  `InputSystem.devices`, which Minis builds after it has already opened every port, and
  a C# `try`/`catch` cannot catch a native `terminate()`.

### Don't unplug to turn the LEDs off

Quitting cleanly already does it. `MidiFighterOutput.ClearOnStart` is on by default and
blanks all 64 pads on `OnDestroy`, so exiting play mode or closing the app turns the
hardware lights off on its own.

### Keep the MIDI stack tidy

Minis opens *every* MIDI input port on the system. Even with the fix, every port change
makes Minis close and reopen all of them, so input drops briefly on every device.
Uninstall MIDI drivers you don't use, connect controllers before starting, and watch the
startup console: `MidiEventManager` names duplicated or virtual ports it can see.
Duplicated ports also deliver every message twice, which breaks latching.

See `DEVNOTES.md` for the full analysis and the captured crash stacks.

---

## Requirements

- **Unity 6** (6000.0+)
- **Minis** — MIDI input package by Keijiro Takahashi. Must be installed separately (see below).

## Installation

### 1. Add Keijiro's scoped registry (required for Minis)

Open **Edit > Project Settings > Package Manager** and add a scoped registry:

| Field | Value |
|-------|-------|
| Name | Keijiro |
| URL | `https://registry.npmjs.com` |
| Scope(s) | `jp.keijiro` |

### 2. Install Minis

In **Window > Package Manager**, switch to **My Registries**, find **Minis**, and install it. This will also pull in its RtMidi dependency.

### 3. Install this package

In **Window > Package Manager**, click **+ > Add package from git URL** and enter:

```
https://github.com/caseyfarina/midiFighterForUnity.git
```

## Usage

### Quick Start

1. Drag **`Runtime/MIDI Controller.prefab`** into your scene. That's the whole setup — it carries MIDI input, routing, LED output, and the on-screen status overlay, already wired.
2. Select the prefab instance to configure anything: device filter, pad modes and colors, MIDI Mix latching, drawer appearance. Every setting lives on the component that owns it.
3. Subscribe to events:

```csharp
using MidiFighter64;

void OnEnable()
{
    // Raw note events
    MidiEventManager.OnNoteOn  += HandleNoteOn;
    MidiEventManager.OnNoteOff += HandleNoteOff;

    // Parsed grid events (default routing)
    MidiGridRouter.OnRow1        += HandleRow1;
    MidiGridRouter.OnGridPreset  += HandlePreset;
    MidiGridRouter.OnGridRandomize += HandleRandomize;
    MidiGridRouter.OnRow5        += HandleRow5;
    MidiGridRouter.OnSlotToggle  += HandleSlot;

    // Or subscribe to raw grid button events for custom routing
    MidiGridRouter.OnGridButton  += HandleGridButton;
}

void HandleNoteOn(int noteNumber, float velocity) { }
void HandleRow1(int col) { }                        // col 1-8
void HandlePreset(int row, int col) { }             // row 2-4, col 1-7
void HandleRandomize(int row) { }                   // row 2-4
void HandleRow5(int col) { }                        // col 1-8
void HandleSlot(int slot, bool isNoteOn) { }        // slot 1-24
void HandleGridButton(GridButton btn, bool isNoteOn) { }
```

### Grid Layout

The Midi Fighter 64 is an 8×8 button grid sending MIDI notes 36–99.
The grid uses a **split-half layout**: left half (cols 1–4) = notes 36–67,
right half (cols 5–8) = notes 68–99, each half numbered 4 notes per row from
bottom to top. Hardware-confirmed corners: top-left=64, top-right=99,
bottom-left=36, bottom-right=71 (all Channel 3).

```
        Col1  Col2  Col3  Col4  Col5  Col6  Col7  Col8
Row 1:  [ 64] [ 65] [ 66] [ 67] [ 96] [ 97] [ 98] [ 99]  <- OnRow1
Row 2:  [ 60] [ 61] [ 62] [ 63] [ 92] [ 93] [ 94] [ 95]  <- OnGridPreset / OnGridRandomize
Row 3:  [ 56] [ 57] [ 58] [ 59] [ 88] [ 89] [ 90] [ 91]  <- OnGridPreset / OnGridRandomize
Row 4:  [ 52] [ 53] [ 54] [ 55] [ 84] [ 85] [ 86] [ 87]  <- OnGridPreset / OnGridRandomize
Row 5:  [ 48] [ 49] [ 50] [ 51] [ 80] [ 81] [ 82] [ 83]  <- OnRow5
Row 6:  [ 44] [ 45] [ 46] [ 47] [ 76] [ 77] [ 78] [ 79]  <- OnSlotToggle (slots 1-8)
Row 7:  [ 40] [ 41] [ 42] [ 43] [ 72] [ 73] [ 74] [ 75]  <- OnSlotToggle (slots 9-16)
Row 8:  [ 36] [ 37] [ 38] [ 39] [ 68] [ 69] [ 70] [ 71]  <- OnSlotToggle (slots 17-24)
```

**Required Utility settings**: Bank 1 / MIDI Channel 3 / MIDI Type = Notes /
Corner Button Bank Change = disabled.

### Custom Routing

Subclass `MidiGridRouter` and override `RouteButton()` to define your own layout:

```csharp
public class MyCustomRouter : MidiGridRouter
{
    protected override void RouteButton(GridButton btn, bool isNoteOn)
    {
        // Your custom routing logic
    }
}
```

### Direct Note-to-Grid Conversion

Use `MidiFighter64InputMap` directly without MonoBehaviours:

```csharp
if (MidiFighter64InputMap.IsInRange(noteNumber))
{
    GridButton btn = MidiFighter64InputMap.FromNote(noteNumber);
    Debug.Log($"Row {btn.row}, Col {btn.col}");
}
```

## API Reference

### MidiEventManager
| Member | Description |
|--------|-------------|
| `static event Action<int, float> OnNoteOn` | Raw MIDI note on (noteNumber, velocity 0-1) |
| `static event Action<int> OnNoteOff` | Raw MIDI note off (noteNumber) |
| `string DeviceName` | Name of the connected MIDI device |
| `string[] AllowedDeviceNames` | Connect only to ports matching these substrings; empty = every port |
| `string[] BlockedDeviceNames` | Never connect to matching ports; applied after the allow list |
| `SetDeviceFilter(allowed, blocked)` | Set both filters and re-run device discovery |

> **Filter your ports if a MIDI monitor, loopback, or network session is running.** `MidiEventManager` merges every MIDI input port, so a port carrying a *copy* of your controller's traffic delivers each message twice — which cancels out any press-to-toggle logic. See Troubleshooting in the full docs.

### MidiGridRouter
| Member | Description |
|--------|-------------|
| `static event Action<int> OnRow1` | Row 1 button press (col 1-8) |
| `static event Action<int, int> OnGridPreset` | Rows 2-4, cols 1-7 press (row, col) |
| `static event Action<int> OnGridRandomize` | Rows 2-4, col 8 press (row) |
| `static event Action<int> OnRow5` | Row 5 button press (col 1-8) |
| `static event Action<int, bool> OnSlotToggle` | Rows 6-8 toggle (slot 1-24, isNoteOn) |
| `static event Action<GridButton, bool> OnGridButton` | Any grid button (button, isNoteOn) |

### MidiFighter64InputMap
| Member | Description |
|--------|-------------|
| `static GridButton FromNote(int noteNumber)` | Convert MIDI note to grid coordinates |
| `static bool IsInRange(int noteNumber)` | Check if note is in MF64 range (36-99) |

### GridButton
| Field | Description |
|-------|-------------|
| `int row` | 1-8 (1 = top row) |
| `int col` | 1-8 (1 = left) |
| `int linearIndex` | 0-63 |
| `int noteNumber` | Original MIDI note (36-99) |
| `bool IsValid` | Whether the note is in valid MF64 range |

## License

MIT
