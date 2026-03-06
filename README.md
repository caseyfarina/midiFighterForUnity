# Midi Fighter 64 for Unity

MIDI input bridge for the [DJ Tech Tools Midi Fighter 64](https://www.midifighter.com/) grid controller in Unity. Converts raw MIDI note events into logical 8x8 grid coordinates and routes them as typed C# events.

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

1. Add a GameObject with the **MidiEventManager** and **MidiGridRouter** components.
2. Optionally add **UnityMainThreadDispatcher** if you need to dispatch work from callbacks.
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

The Midi Fighter 64 is an 8x8 button grid sending MIDI notes 36-99.

```
        Col1  Col2  Col3  Col4  Col5  Col6  Col7  Col8
Row 1:  [92]  [93]  [94]  [95]  [96]  [97]  [98]  [99]   <- OnRow1
Row 2:  [84]  [85]  [86]  [87]  [88]  [89]  [90]  [91]   <- OnGridPreset / OnGridRandomize
Row 3:  [76]  [77]  [78]  [79]  [80]  [81]  [82]  [83]   <- OnGridPreset / OnGridRandomize
Row 4:  [68]  [69]  [70]  [71]  [72]  [73]  [74]  [75]   <- OnGridPreset / OnGridRandomize
Row 5:  [60]  [61]  [62]  [63]  [64]  [65]  [66]  [67]   <- OnRow5
Row 6:  [52]  [53]  [54]  [55]  [56]  [57]  [58]  [59]   <- OnSlotToggle (slots 1-8)
Row 7:  [44]  [45]  [46]  [47]  [48]  [49]  [50]  [51]   <- OnSlotToggle (slots 9-16)
Row 8:  [36]  [37]  [38]  [39]  [40]  [41]  [42]  [43]   <- OnSlotToggle (slots 17-24)
```

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
