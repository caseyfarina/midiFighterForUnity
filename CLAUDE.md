# midiFighterForUnity — Claude Code Project Memory

This is a **Unity Package Manager (UPM) package** (`com.caseyfarina.midifighter64`).
It bridges two MIDI controllers into Unity via the Minis input package:
- **DJ Tech Tools Midi Fighter 64** — 8×8 button grid (notes 36–99)
- **Akai MIDI Mix** — 8-channel mixer (24 knobs, 8+1 faders, 24 buttons)

Target: **Unity 6** (6000.3.7f1), **URP**. Installed: `jp.keijiro.minis 1.3.2`, `jp.keijiro.rtmidi 2.2.0`.

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

Assets/midiSupport/Samples/TestScene/          ← ACTIVE scripts (compiled, in scene)
  MidiSceneBootstrapper.cs         Scene bootstrapper; EnsureCoreComponents only
  MidiDebugUI.cs                   MIDI device status bar + raw event log overlay
  FloorCameraController.cs         MF64 col-8 → DOTween vertical camera between 8 floors
  MidiMixCameraRig.cs              8 orbit cameras (INACTIVE — removed from bootstrapper)
  MidiMixDataVisualizer.cs         TMP label cloud for all 51 MIDI Mix controls
  MidiMixCloner.cs                 DrawMeshInstanced cloner; Row-2 knobs tune
  MidiFighterInteriorSpawner.cs    56 interior prefab instances; MF64 cols 1-7 hold=show
  MidiMixParticleRefs.cs           ScriptableObject — particle + explosion prefab refs
  MidiMixParticleRefsInit.cs       [InitializeOnLoad] editor auto-populator
  MidiFighterInteriorRefs.cs       ScriptableObject — Basic Asset Pack Interior refs (49 prefabs)
  MidiFighterInteriorRefsInit.cs   [InitializeOnLoad] editor auto-populator
  MidiFighter64.Samples.asmdef     Refs: MidiFighter64.Runtime, Unity.TextMeshPro, URP.Runtime

Assets/midiSupport/Samples/Resources/         ← ScriptableObject assets (build-safe)
  MidiMixParticleRefs.asset        Auto-created; holds fader + explosion prefab refs
  MidiFighterInteriorRefs.asset    Auto-created; holds 49 interior prefab refs

Assets/Scenes/pincushionededed.unity           ← Active scene; has MidiSceneBootstrapper on "GameObject"
```

`Samples~/TestScene/` is NOT compiled — it's the upstream source. The active code is in `Samples/TestScene/`.

---

## Architecture

### Event flow

```
Hardware → Minis (RtMidi) → MidiEventManager (static events)
                                   ↓                    ↓
                          MidiGridRouter          MidiMixRouter
                          (MF64 routing)          (MIDI Mix routing)
                               ↓                        ↓
                        typed static events      typed static events
                               ↓                        ↓
                 MidiFighterInteriorSpawner    FloorCameraController
                 (cols 1-7 only)              (col 8 only)
                                              MidiMixDataVisualizer
                                              MidiMixCloner
```

`MidiEventManager` is the single subscriber to Minis. **Never subscribe directly to Minis.**
Both `MidiGridRouter` AND `MidiMixRouter` must be instantiated in the scene — their static events only fire when an active MonoBehaviour instance is subscribed to `MidiEventManager`.

### EnsureCoreComponents (MidiSceneBootstrapper.Awake)

Creates these if absent:
1. `MidiEventManager` — singleton
2. `UnityMainThreadDispatcher` — singleton
3. `MidiMixRouter`
4. `MidiGridRouter` ← **required for MF64; easy to forget**
5. `FloorCameraController`
6. `MidiFighterInteriorSpawner`
7. `MidiDebugUI`

### Input Map vs Router

| Class | Role | Instantiation |
|---|---|---|
| `*InputMap` | Pure static lookup — no MonoBehaviour | Call statically |
| `*Router` | MonoBehaviour that wires events | Must be in scene |
| `MidiEventManager` | Singleton MonoBehaviour | One per scene |
| `UnityMainThreadDispatcher` | Singleton MonoBehaviour | One per scene |

---

## Current Control Mapping

### MIDI Mix

| Control | Action |
|---------|--------|
| **Mute Ch1–8** | (unmapped — was orbit camera switching, now inactive) |
| **Rec Arm Ch1–8** | (removed — was lights + explosions) |
| **Channel faders 1–8** | Particle system emission rate 0→300 |
| **Master fader** | Visual display only |
| **Knob Row 1 Ch1–3** | (unmapped — was camera: speed / pos noise / look wobble) |
| **Knob Row 2 Ch1–5** | MidiMixCloner: count, seed, scale var, rotation, spread |
| **Knob Row 3 Ch1** | TMP label cloud density (0=none → 1=all 51 visible) |

### Midi Fighter 64

| Control | Action |
|---------|--------|
| **Columns 1–7 (56 buttons)** | Hold = show interior prefab instance; release = hide |
| **Column 8, rows 1–8** | Floor navigation: row 1 (top) → floor 7, row 8 (bottom) → floor 0 |

Column 8 buttons use `FloorCameraController` — DOTween `InOutCubic` ease, 0.6s, camera Y only.
Columns 1–7 use `MidiFighterInteriorSpawner` via `MidiGridRouter.OnGridButton`.

### Floor Layout

- 8 floors, each 8 units tall: floor 0 = Y 0, floor 7 = Y 56
- Camera Y offset from floor: 7.7
- Camera X/Z/rotation fixed; only Y moves
- DOTween Pro (`Assets/Plugins/Demigiant/DOTween/`) — DLL, no asmdef, globally accessible

---

## Resources / ScriptableObject Pattern (Build-Safe Assets)

`AssetDatabase.LoadAssetAtPath` is Editor-only. For assets needed in builds:

1. Create a `ScriptableObject` subclass with `public GameObject[]` fields + `public const string ResourceName`.
2. Create an `[InitializeOnLoad]` class (whole file in `#if UNITY_EDITOR`) that auto-creates and populates the `.asset` file in `Assets/.../Resources/`.
3. At runtime: `Resources.Load<MyRefs>(MyRefs.ResourceName)`.

This pattern is used for:
- `MidiMixParticleRefs` — fader particles + explosion prefabs
- `MidiFighterInteriorRefs` — 49 Basic Asset Pack Interior prefabs

---

## Scene Components Built at Runtime

- **1 Main Camera** (scene-placed) — `FloorCameraController` tweens Y between floors via MF64 col 8
- **TMP label cloud** — 51 TextMeshPro world-space labels, billboarding, random size 10–30, random black/white, hidden until Row-3-Ch-1 knob dials them in
- **64 interior prefab instances** — `MidiFighterInteriorSpawner`, 25-unit sphere spread, scale 3–9×, cols 1–7 only, hidden until MF64 button held

---

## Conventions

- **1-based** everywhere user-facing: `row` 1–8, `col` 1–8, `channel` 1–8, knob `row` 1–3.
- **0-based** only in internal arrays: `KnobCC[row, ch]`, `FaderCC[ch]`.
- `MixFader.channel` is 0 for master, 1–8 for strips — use `isMaster` to distinguish.
- All CC/fader values arrive as `float` 0–1 (Minis normalises).
- Velocity on `OnNoteOn` is also 0–1.
- Namespace: `MidiFighter64` for Runtime, `MidiFighter64.Samples` for Samples.
- Files in `MidiFighter64.Samples` can access `MidiFighter64` types via parent-namespace resolution without a `using` directive — but adding `using MidiFighter64;` is safer and avoids hard-to-diagnose compile errors that break the whole assembly.

---

## URP / Unity 6 Gotchas

- **Materials**: Use `"Universal Render Pipeline/Lit"`, `_Smoothness` (not `_Glossiness`), `SetColor("_BaseColor", color)`.
- **Camera post-processing**: Add `UniversalAdditionalCameraData` component and set `renderPostProcessing = true`.
- **RectTransform on bare GameObject**: `new GameObject("Name")` does NOT auto-add `RectTransform` when parented to a Canvas. Use `new GameObject("Name", typeof(RectTransform))` or `AddComponent<Image>()`.
- **AssetDatabase in builds**: Use `Resources.Load` + ScriptableObject pattern instead.

---

## Midi Fighter 64 — Note Layout

```
        Col 1  Col 2  Col 3  Col 4  Col 5  Col 6  Col 7  Col 8
Row 1:  [ 64]  [ 65]  [ 66]  [ 67]  [ 96]  [ 97]  [ 98]  [ 99]
Row 2:  [ 60]  [ 61]  [ 62]  [ 63]  [ 92]  [ 93]  [ 94]  [ 95]
Row 3:  [ 56]  [ 57]  [ 58]  [ 59]  [ 88]  [ 89]  [ 90]  [ 91]
Row 4:  [ 52]  [ 53]  [ 54]  [ 55]  [ 84]  [ 85]  [ 86]  [ 87]
Row 5:  [ 48]  [ 49]  [ 50]  [ 51]  [ 80]  [ 81]  [ 82]  [ 83]
Row 6:  [ 44]  [ 45]  [ 46]  [ 47]  [ 76]  [ 77]  [ 78]  [ 79]
Row 7:  [ 40]  [ 41]  [ 42]  [ 43]  [ 72]  [ 73]  [ 74]  [ 75]
Row 8:  [ 36]  [ 37]  [ 38]  [ 39]  [ 68]  [ 69]  [ 70]  [ 71]
```

The grid is split into two 4-column halves: left (notes 36–67) and right (notes 68–99), each 4 notes per row, bottom to top.
Hardware note 36 = bottom-left. `MidiFighter64InputMap.FromNote()` **inverts Y** so row 1 = top.
`GridButton.linearIndex` is 0–63 (row-major, top-left = 0).

---

## Akai MIDI Mix — CC and Note Map

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
| Solo | 2 | 5 | 8 | 11 | 14 | 17 | 20 | 23 |
| Rec Arm | 3 | 6 | 9 | 12 | 15 | 18 | 21 | 24 |
| Bank Left | 25 | | | | | | | |
| Bank Right | 26 | | | | | | | |

---

## MidiMixRouter Events

```csharp
static event Action<int, int, float> OnKnob          // channel(1-8), row(1-3), value(0-1)
static event Action<int, float>      OnChannelFader   // channel(1-8), value(0-1)
static event Action<float>           OnMasterFader    // value(0-1)
static event Action<int, bool>       OnMute           // channel(1-8), isNoteOn
static event Action<int, bool>       OnSolo           // channel(1-8), isNoteOn
static event Action<int, bool>       OnRecArm         // channel(1-8), isNoteOn
static event Action<int, bool>       OnRecArmShifted  // channel(1-8), isNoteOn
static event Action                  OnBankLeft
static event Action                  OnBankRight
// Raw
static event Action<MixKnob,   float> OnKnobRaw
static event Action<MixFader,  float> OnFaderRaw
static event Action<MixButton, bool>  OnButtonRaw
```

## MidiGridRouter Events

```csharp
static event Action<int>           OnRow1         // col(1-8), note-on only
static event Action<int, int>      OnGridPreset   // row(2-4), col(1-7), note-on only
static event Action<int>           OnGridRandomize// row(2-4), note-on only
static event Action<int>           OnRow5         // col(1-8), note-on only
static event Action<int, bool>     OnSlotToggle   // slot(1-24), isNoteOn
static event Action<GridButton, bool> OnGridButton// every button, both on+off
```

---

## MidiFighterOutput (LED Control)

Windows / Editor only. No-op on other platforms.

```csharp
MidiFighterOutput.Instance.SetLED(noteNumber, velocity); // velocity 0-127
MidiFighterOutput.Instance.ClearLED(noteNumber);
MidiFighterOutput.Instance.ClearAllLEDs();
MidiFighterOutput.Instance.ledChannelIndex = 0; // 0=Blue 1=Purple 2=Red 3=White
```

---

## Known Issues

- **`GridButton.IsValid`** upper bound off-by-one vs `IsInRange()` on note 100 (unreachable from hardware).
- `MidiFighterOutput` silently does nothing on macOS/Linux.
- `CHANGELOG.md` and `package.json` version not bumped.
- WinMM exclusive access: only one app can hold a MIDI port. Close DAWs before Play.
- First MIDI event per channel is always lost (device created on first event, callback subscribed after).
