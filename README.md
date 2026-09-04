# Midi Fighter 64 for Unity

MIDI input and LED output bridge for the [DJ Tech Tools Midi Fighter 64](https://www.midifighter.com/) grid controller and the [Akai MIDI Mix](https://www.akaipro.com/midimix) in Unity. Converts raw MIDI into logical grid coordinates and typed C# events.

- **MF64 grid** — 8×8 pads as `(row, col)` events, per-pad Button/Toggle modes, LED output with a hardware-verified color palette.
- **Akai MIDI Mix** — 24 knobs, 9 faders, and 24 buttons as typed events; Mute and Rec-Arm latch (press-to-toggle) by default and the host echoes their LEDs.
- **Status drawer** — an optional screen-space overlay mirroring both controllers live, with dark/light themes and adjustable opacity, stroke weight and scale.

Full documentation is in [`Documentation~/index.html`](Documentation~/index.html) — open it locally, or use **View documentation** in Package Manager.

---

## ⚠️ MIDI hot-plug hazard — read before live use

**By default, connecting or disconnecting any MIDI device while Unity or a build of your
project is running can hard-crash the process** — no exception, no dialog, no recovery.

This is a bug in the native MIDI layer beneath this package
([RtMidi](https://github.com/thestk/rtmidi), via `jp.keijiro.rtmidi` and
`jp.keijiro.minis`). **Nothing in this package can prevent it**, and no version of this
package will claim to — see [Why this package can't fix
it](#why-this-package-cant-fix-it).

> **There is a working fix, one level down.** It is a small patch to **Minis**, not to
> this package: cancel the native callbacks and abandon the RtMidi handle instead of
> freeing it, so the throwing call is never made. It has been verified to survive both
> crash paths (hot-plug during play, and domain reload with controllers connected).
>
> Because it lives in a *dependency*, this package cannot ship it for you. To apply it,
> follow **`MINIS_HOTPLUG_PATCH.md`** — a self-contained guide you can drop into any
> project that uses Minis and hand to Claude Code.
>
> Until you apply it, everything below is live and the guidance is: don't hot-plug.

What makes it dangerous is that almost none of it is obvious:

- **It affects shipped builds, not just the editor.** `Minis.MidiDriver.Update()` runs
  every frame in players too. If you are using this for live performance, a crash
  mid-set is a real outcome.
- **The trigger is a change in the system's MIDI port *count*, not the unplug.**
  Plugging a controller *in* is exactly as risky as pulling one out.
- **You do not have to touch a cable.** Any other application creating or destroying a
  virtual MIDI port trips it — a DAW launching, Bome, loopMIDI, a MIDI monitor opening.
- **It often does not crash at the moment you touch the cable.** The failure can land
  frames later, or on the next domain reload after you edit a script. That delay makes
  it look unrelated to the MIDI device, which is the single most misleading part of it.
- **Recompiling with controllers connected carries the same risk**, because a domain
  reload closes every port.

### Don't unplug to turn the LEDs off

Quitting cleanly already does it. `MidiFighterOutput.ClearOnStart` is on by default and
blanks all 64 pads on `OnDestroy`, so exiting play mode or closing the app turns the
hardware lights off on its own. Unplugging the controller to kill the LEDs at the end of
a session is the most common way people walk into this crash — and it is unnecessary.

### Reduce your exposure

- **Uninstall MIDI drivers you don't use.** Minis opens *every* MIDI input port on the
  system, so each unused virtual driver, loopback, or duplicated port is one more handle
  that must be closed cleanly, and one more chance to hit the bug. Machines with a clean
  MIDI stack hit this far less often. This is the single most effective thing you can do.
- **Connect controllers before starting Unity, and leave them connected.**
- **Quit the app or exit play mode before changing anything about your MIDI setup.**
- **Watch the console at startup.** `MidiEventManager` warns when it sees duplicated or
  virtual ports, naming them. That warning is a direct readout of your exposure.

### Why this package can't fix it

Two independent reasons, both worth stating because both look fixable and aren't:

1. **The device filter can't help.** `AllowedDeviceNames` / `BlockedDeviceNames` filter
   `InputSystem.devices` — but Minis has already called `OpenPort` on every port on the
   system by the time that list exists. The filter decides which devices we *subscribe
   to*, not which ports get *opened*, so it has no effect on which handles must later be
   freed. Tightening the allow list does not reduce crash risk.
2. **A C# `try`/`catch` can't help either.** The failure is
   `terminate called after throwing an instance of 'rt::midi::RtMidiError'` — a C++
   exception escaping RtMidi's C API wrapper on the port-close path. The C++ runtime
   aborts during unwinding and never returns to managed code, so there is nothing for a
   managed `catch` to catch. The process is already gone. Wrapping the P/Invoke looks
   like the obvious fix and is a dead end.

The real *fix* is in RtMidi's C wrapper, which should catch `RtMidiError` at the language
boundary and return an error code. But the crash can be **avoided** without it, by not
calling the broken function at all — which is what `MINIS_HOTPLUG_PATCH.md` does, in
Minis rather than here.

Either way, everything downstream already works: this package subscribes to
`InputSystem.onDeviceChange`, reconnects, and re-pushes LED state, so once the native
abort stops, a hot-plug becomes a self-healing hiccup rather than a process kill. That
has been confirmed on hardware with the Minis patch applied.

**Status:** a verified Minis-level workaround exists (`MINIS_HOTPLUG_PATCH.md`);
upstream issues not yet filed. See `DEVNOTES.md` for the full analysis, both captured
crash stacks, and the two upstream changes worth proposing (catching `RtMidiError` in
`rtmidi_c.cpp`; having Minis close only the port that actually changed rather than all
of them).

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
