# midiFighterForUnity — Claude Code Project Memory

This is a **Unity Package Manager (UPM) package** (`com.caseyfarina.midifighter64`).
It bridges two MIDI controllers into Unity via the Minis input package:
- **DJ Tech Tools Midi Fighter 64** — 8×8 button grid (notes 36–99)
- **Akai MIDI Mix** — 8-channel mixer (24 knobs, 8+1 faders, 24 buttons)

Target: **Unity 6** (6000.0+). Dependencies: `com.unity.inputsystem` 1.7.0, `jp.keijiro.minis` 1.5.1.

---

## File Map

```
Runtime/
  MidiEventManager.cs          Minis → C# event bridge (OnNoteOn, OnNoteOff, OnControlChange)
  UnityMainThreadDispatcher.cs Thread-safe action queue; flush in Update()
  MidiFighter64InputMap.cs     Note 36–99 → GridButton{row,col,linearIndex,noteNumber}
  MidiGridRouter.cs            MonoBehaviour; routes GridButtons to typed row/slot events
  MidiFighterOutput.cs         LED control via winmm.dll (Windows/Editor only)
  MidiMixInputMap.cs           CC/note → MixKnob / MixFader / MixButton structs
  MidiMixRouter.cs             MonoBehaviour; routes CC+notes to typed mixer events

Samples~/TestScene/
  MidiFighterTestScene.cs      Programmatic 8×8 sphere grid; wave animation; tests MF64
  MidiMixTestScene.cs          Programmatic mixer visual; tests MIDI Mix
  MidiFighter64.Samples.asmdef References MidiFighter64.Runtime
```

There are no `.unity` scene files. Both sample scenes build themselves in `Awake()`.

---

## Architecture

### Event flow

```
Hardware → Minis → MidiEventManager (static events)
                        ↓                   ↓
               MidiGridRouter        MidiMixRouter
               (MF64 routing)        (MIDI Mix routing)
                    ↓                       ↓
             typed static events     typed static events
```

`MidiEventManager` is the single subscriber to Minis. Everything else subscribes to `MidiEventManager`'s static events. **Never subscribe directly to Minis from feature code.**

### Input Map vs Router

| Class | Role | Instantiation |
|---|---|---|
| `*InputMap` | Pure static lookup — no MonoBehaviour | Call statically |
| `*Router` | MonoBehaviour that wires events | Add to a GameObject |
| `MidiEventManager` | Singleton MonoBehaviour | One per scene |
| `UnityMainThreadDispatcher` | Singleton MonoBehaviour | One per scene |

### Extending routing

Subclass `MidiGridRouter` or `MidiMixRouter` and override the virtual method:

```csharp
// MF64
protected override void RouteButton(GridButton btn, bool isNoteOn) { }

// MIDI Mix
protected override void RouteCC(int ccNumber, float value) { }
protected override void RouteNote(int noteNumber, bool isNoteOn) { }
```

---

## Conventions

- **1-based** everywhere user-facing: `row` 1–8, `col` 1–8, `channel` 1–8, knob `row` 1–3.
- **0-based** only in internal arrays: `KnobCC[row, ch]`, `FaderCC[ch]`.
- `MixFader.channel` is 0 for master, 1–8 for strips (exception to the rule — use `isMaster` to distinguish).
- All CC/fader values arrive as `float` 0–1 (Minis normalises for us).
- Velocity on `OnNoteOn` is also 0–1.
- Namespace: `MidiFighter64` for all runtime code, `MidiFighter64.Samples` for samples.

---

## Midi Fighter 64 — note layout

```
        Col 1  Col 2  Col 3  Col 4  Col 5  Col 6  Col 7  Col 8
Row 1:  [ 92]  [ 93]  [ 94]  [ 95]  [ 96]  [ 97]  [ 98]  [ 99]
Row 2:  [ 84]  [ 85]  [ 86]  [ 87]  [ 88]  [ 89]  [ 90]  [ 91]
Row 3:  [ 76]  [ 77]  [ 78]  [ 79]  [ 80]  [ 81]  [ 82]  [ 83]
Row 4:  [ 68]  [ 69]  [ 70]  [ 71]  [ 72]  [ 73]  [ 74]  [ 75]
Row 5:  [ 60]  [ 61]  [ 62]  [ 63]  [ 64]  [ 65]  [ 66]  [ 67]
Row 6:  [ 52]  [ 53]  [ 54]  [ 55]  [ 56]  [ 57]  [ 58]  [ 59]
Row 7:  [ 44]  [ 45]  [ 46]  [ 47]  [ 48]  [ 49]  [ 50]  [ 51]
Row 8:  [ 36]  [ 37]  [ 38]  [ 39]  [ 40]  [ 41]  [ 42]  [ 43]
```

Hardware note 36 = bottom-left. `MidiFighter64InputMap.FromNote()` **inverts Y** so row 1 = top.

Default `MidiGridRouter` routing:

| Rows | Col(s) | Event fired |
|------|--------|-------------|
| 1 | 1–8 | `OnRow1(col)` — note-on only |
| 2–4 | 1–7 | `OnGridPreset(row, col)` — note-on only |
| 2–4 | 8 | `OnGridRandomize(row)` — note-on only |
| 5 | 1–8 | `OnRow5(col)` — note-on only |
| 6–8 | 1–8 | `OnSlotToggle(slot 1–24, isNoteOn)` — both note-on and note-off |

`OnGridButton(GridButton, isNoteOn)` fires for every button regardless of routing.

---

## Akai MIDI Mix — CC and note map

**Knob CCs** — `KnobCC[row, channel]` (both 0-based):

```
         Ch1  Ch2  Ch3  Ch4  Ch5  Ch6  Ch7  Ch8
Row 1:   16   20   24   28   46   50   54   58
Row 2:   17   21   25   29   47   51   55   59
Row 3:   18   22   26   30   48   52   56   60
```

**Fader CCs**: channels 1–8 → `{19, 23, 27, 31, 49, 53, 57, 61}`. Master fader → CC 127.

**Button notes** (per channel, 0-based index):

| Type | Ch1 | Ch2 | Ch3 | Ch4 | Ch5 | Ch6 | Ch7 | Ch8 |
|------|-----|-----|-----|-----|-----|-----|-----|-----|
| Mute | 1 | 4 | 7 | 10 | 13 | 16 | 19 | 22 |
| Solo (Mute in Solo mode) | 2 | 5 | 8 | 11 | 14 | 17 | 20 | 23 |
| Rec Arm | 3 | 6 | 9 | 12 | 15 | 18 | 21 | 24 |
| Rec Arm Shifted | 35 | 38 | 41 | 44 | 47 | 50 | 53 | 56 |
| Bank Left | 25 | — | — | — | — | — | — | — |
| Bank Right | 26 | — | — | — | — | — | — | — |

Solo mode: the hardware SOLO toggle makes the Mute row emit Solo notes instead of Mute notes.
Shifted Rec Arm: activated by the Bank shift function (base note + 32).

---

## MidiMixRouter events

```csharp
// Convenience (most common)
static event Action<int, int, float> OnKnob          // channel(1-8), row(1-3), value(0-1)
static event Action<int, float>      OnChannelFader   // channel(1-8), value(0-1)
static event Action<float>           OnMasterFader    // value(0-1)
static event Action<int, bool>       OnMute           // channel(1-8), isNoteOn
static event Action<int, bool>       OnSolo           // channel(1-8), isNoteOn  ← same physical key as Mute
static event Action<int, bool>       OnRecArm         // channel(1-8), isNoteOn
static event Action<int, bool>       OnRecArmShifted  // channel(1-8), isNoteOn
static event Action                  OnBankLeft
static event Action                  OnBankRight

// Raw (carries full struct for custom fan-out)
static event Action<MixKnob,   float> OnKnobRaw
static event Action<MixFader,  float> OnFaderRaw
static event Action<MixButton, bool>  OnButtonRaw
```

---

## MidiFighterOutput (LED control)

Windows and Unity Editor only. macOS/Linux compile but do nothing at runtime.

```csharp
MidiFighterOutput.Instance.SetLED(noteNumber, velocity); // velocity 0-127
MidiFighterOutput.Instance.ClearLED(noteNumber);
MidiFighterOutput.Instance.ClearAllLEDs();
MidiFighterOutput.Instance.ledChannelIndex = 0; // 0=Blue 1=Purple 2=Red 3=White
```

Do not assume LED feedback works on non-Windows builds.

---

## Sample scene pattern

Both sample `MonoBehaviour`s follow the same pattern — copy it for new scenes:

1. `Awake()` — call `EnsureCoreComponents()` then `BuildScene()`
2. `EnsureCoreComponents()` — `FindFirstObjectByType<T>() ?? new GameObject(...).AddComponent<T>()` for each required component
3. `OnEnable()` / `OnDisable()` — subscribe / unsubscribe static events
4. `BuildScene()` — create camera, light, geometry, UI entirely from code

---

## Known issues

- **`GridButton.IsValid`** uses `< NOTE_OFFSET + GRID_SIZE * GRID_SIZE` (exclusive upper bound = 100) while `IsInRange()` correctly uses `<= NOTE_MAX` (99). They disagree on note 100, which cannot come from real hardware but is a latent inconsistency. Fix: change `IsValid` to use `<= NOTE_MAX`.
- `MidiFighterOutput` silently does nothing on macOS/Linux with no log output.
- `CHANGELOG.md` and `package.json` version have not been bumped since the initial commit despite significant additions (MIDI Mix support, MidiFighterOutput, test scenes).

---

## Adding support for a new MIDI controller

1. Create `Runtime/YourDevice InputMap.cs` — static class, structs, reverse-lookup dictionaries, constants.
2. Create `Runtime/YourDeviceRouter.cs` — `MonoBehaviour`, subscribe `MidiEventManager` events in `OnEnable`/`OnDisable`, fire typed static events.
3. Create `Samples~/TestScene/YourDeviceTestScene.cs` — follows the sample scene pattern above.
4. No `.unity` files — scenes are built at runtime.
5. The samples `.asmdef` already references `MidiFighter64.Runtime`, so new runtime classes are automatically available.
