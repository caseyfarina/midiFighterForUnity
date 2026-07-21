# midiFighterForUnity — Integration Guide for Claude Code

This file gives Claude Code the context it needs when a user asks it to integrate the **Midi Fighter 64 for Unity** package into a project.

If you are working *inside* this package's folder to modify the package itself, see `DEVNOTES.md` (dev history, testing notes, unreleased work). This file focuses on the *consumer's* perspective.

Package: `com.caseyfarina.midifighter64` — MIDI input + LED output bridge for:
- **DJ Tech Tools Midi Fighter 64** — 8×8 button grid (notes 36–99, Channel 3)
- **Akai MIDI Mix** — 8-channel mixer (24 knobs, 8+1 faders, 24 buttons)

Target: **Unity 6** (6000.0+). Depends on `com.unity.inputsystem`, `jp.keijiro.minis`, `jp.keijiro.rtmidi`.

---

## Architecture at a glance

```
Hardware ─→ Minis (RtMidi) ─→ MidiEventManager (static events)
                                     │
             ┌───────────────────────┼───────────────────────────┐
             ↓                       ↓                            ↓
      MidiGridRouter        MidiMixRouter          MidiFighterButtonRouter
      (typed grid events)   (typed mixer events)   (Button vs Toggle + LED feedback)

                                                           ↓ optional LED writes
                                                    MidiFighterOutput
                                                    (RtMidi → hardware LEDs)
```

**Key rule:** *Only* `MidiEventManager` subscribes to Minis. Everything else consumes `MidiEventManager`'s C# events. Don't subscribe to Minis directly from consumer code.

---

## Minimum scene setup

For MF64 grid input only, the scene needs:

1. `MidiEventManager` (singleton)
2. `UnityMainThreadDispatcher` (singleton)
3. `MidiGridRouter` (typed grid events)

Add `MidiFighterButtonRouter` if you want Button/Toggle semantics per pad.
Add `MidiFighterOutput` if you want to drive the physical MF64 LEDs.
Add `MidiMixRouter` if you also want Akai MIDI Mix events.
Add `MidiMixOutput` to light the MIDI Mix Mute/Rec-Arm buttons on press (the hardware does NOT self-illuminate — LEDs are host-controlled).

The sample `MidiSceneBootstrapper` has a public `EnsureCoreComponents(Transform parent = null, bool includeStatusDrawer = true)` static method that spawns all of them on-demand. Consumers can also call it themselves without adding the bootstrapper GameObject; pass `includeStatusDrawer: false` to skip the on-screen overlay.

---

## The single most important pattern

**All router events are static.** Subscribers *must* unsubscribe in `OnDisable` or destroyed objects will keep firing.

```csharp
using MidiFighter64;

void OnEnable()  => MidiFighterButtonRouter.OnButtonPress += HandlePress;
void OnDisable() => MidiFighterButtonRouter.OnButtonPress -= HandlePress;

void HandlePress(GridButton btn, float velocity) { /* ... */ }
```

If Claude is writing code that subscribes to any `Mid*Router.On*` event, it must add the matching `-=` in `OnDisable`.

---

## Grid layout (split-half)

The MF64 does **not** number pads left-to-right, top-to-bottom. It sends two 4-column halves. Corners hardware-confirmed: `64 / 99 / 36 / 71`.

```
        C1  C2  C3  C4   C5  C6  C7  C8
R1  [ 64 65 66 67 ][ 96 97 98 99 ]
R2  [ 60 61 62 63 ][ 92 93 94 95 ]
R3  [ 56 57 58 59 ][ 88 89 90 91 ]
R4  [ 52 53 54 55 ][ 84 85 86 87 ]
R5  [ 48 49 50 51 ][ 80 81 82 83 ]
R6  [ 44 45 46 47 ][ 76 77 78 79 ]
R7  [ 40 41 42 43 ][ 72 73 74 75 ]
R8  [ 36 37 38 39 ][ 68 69 70 71 ]
```

Never compute notes with `36 + row*8 + col`. Always go through `MidiFighter64InputMap.ToNote(row, col)` and `.FromNote(note)`. The naive formula is wrong for the right half.

`GridButton.linearIndex` (0–63, row-major, top-left = 0) is the correct index for flat arrays. Don't recompute `(row-1)*8 + (col-1)` — use `linearIndex` directly.

---

## LED palette

Requires firmware **24 Jul 2017** or newer. Older firmware uses a totally different color mapping and lacks white via MIDI. If a user reports wrong colors, tell them to update firmware in the Midi Fighter Utility first.

Palette (hardware-confirmed):

| Enum         | Velocity |
|--------------|----------|
| `Off`        | 0        |
| `DarkGrey`   | 1        |
| `Grey`       | 2        |
| `White`      | 3        |
| `BrightBlue` | 37       |
| `DarkBlue`   | 39       |
| `BrightPink` | 56       |
| `DarkPink`   | 59       |

All 128 raw velocities also work via `SetLED(int note, int velocity)`. The enum names only the confirmed values.

For on-screen visual sync, use `MidiFighterLEDColor.X.ToUnityColor()` — returns an approximate RGB match.

---

## Common tasks

### Subscribe to a specific grid row

```csharp
MidiGridRouter.OnRow1 += col => { /* col 1-8 */ };  // row 1
```

### Handle every pad as a raw press/release

```csharp
MidiGridRouter.OnGridButton += (btn, isDown) => {
    if (isDown) Debug.Log($"pressed R{btn.row}C{btn.col}");
};
```

### Add per-pad Button/Toggle behavior

1. In Project window: **Create → MidiFighter64 → Button Config**
2. In the config asset's Inspector, tick pads you want as toggles
3. Drag the asset onto a `MidiFighterButtonRouter`'s `Config` field
4. Subscribe to `OnButtonPress` / `OnButtonRelease` / `OnToggle`

### Light a pad

```csharp
MidiFighterOutput.Instance.SetLED(64, MidiFighterLEDColor.BrightPink);
```

### Read a MIDI Mix knob

```csharp
MidiMixRouter.OnKnob += (channel, row, value) => {
    // channel 1-8, row 1-3, value 0-1
};
```

### Latch Mute / Rec-Arm buttons (press-to-toggle)

MIDI Mix Mute and Rec-Arm buttons are momentary hardware buttons by default. Turn them into press-to-toggle latching buttons via `MidiMixRouter.LatchMute` / `LatchRecArm` (or the "MIDI Mix — Latching Buttons" section on `MidiSceneBootstrapper`). When latching is on, `OnMute` / `OnRecArm` fire only on note-on and pass the *new latched state* (not raw isNoteOn). `MidiMixOutput` subscribes to those router events, so hardware LEDs stay lit while latched. `OnSolo` (Mute-while-SOLO) is always momentary.

### Detect SOLO-held mute presses

The MIDImix SOLO button is a modifier — holding it makes the 8 Mute buttons emit a different note set. The router splits these into two events:

```csharp
MidiMixRouter.OnMute += (ch, on) => { /* fires when SOLO is NOT held */ };
MidiMixRouter.OnSolo += (ch, on) => { /* fires when SOLO IS held */ };

// Or query modifier state on demand:
if (MidiMixRouter.IsSoloHeld) { ... }
```

### Show a live status drawer overlay (Test Scene sample)

`MidiStatusDrawer` is a screen-space UI Toolkit overlay that mirrors both controllers in real time. Add the component to any GameObject (or let `MidiSceneBootstrapper.EnsureCoreComponents()` spawn it).

**Hotkeys**
- **Backtick** (`` ` ``) or **F1** — show/hide the drawer.
- **F2** — cycle `DrawerPlacement` (Right Centered ⇄ Screen Centered).

**Bootstrapper controls** (Status Drawer section)
- **Spawn Status Drawer** — untick to keep `EnsureCoreComponents()` from creating the overlay at all.
- **Placement** — `Right Centered` (pinned right, centered vertically) or `Screen Centered` (centered both axes). Runtime equivalent: `MidiStatusDrawer.Instance.Placement`. Restyles the root container; no rebuild.
- **Show Midi Fighter 64** / **Show MIDI Mix** — both on by default. Untick one to run a single controller without a dead panel taking up drawer space. Runtime equivalents: `ShowMf64` / `ShowMidiMix`, or `SetVisibleSections(mf64, mix)` to change both with one rebuild.
- **Drawer Font** — optional typeface override. Empty = the bundled `CossetteTitre-Regular.ttf` in `Samples~/TestScene/UI/Resources/`, loaded via `Resources.Load<Font>(MidiStatusDrawer.BundledFontResourceName)`. Falls back to a dynamic OS font (Arial/Helvetica) if that Resources folder is missing.
- **Enable MF64 Fisheye** — the last-touched pad grows while its row/column neighbors deform to compensate. On by default. Also settable at runtime via `MidiStatusDrawer.Instance.EnableMf64Fisheye`; assigning `false` clears any active focus.

- **Screen Fill** — fraction of the display the drawer occupies on whichever axis binds first: height on a landscape display, width on a portrait one. Never crops. Runtime equivalent: `ScreenFraction`.
- **Log Layout Report** — diagnostic, off by default. Dumps one resolved-geometry report to the console ~400 ms after the drawer is first *shown* (press `` ` ``; a hidden drawer is `display:none` and measures `NaN`). Reports screen size, derived reference resolution, drawer/grid/cell sizes, screen coverage, mixer-vs-grid column widths, and the measured mix section height. Use it instead of eyeballing: the grid and cell lines must be square, one coverage axis must equal Screen Fill, and `mix section h` is how you correct `MixChromeHeight`. Reads `resolvedStyle` only, never writes — that distinction is what keeps it from feeding back into layout.

Section visibility is baked into the UI tree, so toggling it rebuilds all views. Widget "seen" opacity resets on rebuild; the hidden state survives. The event message strip normally lives in the MIDI Mix utility row — with Mix hidden it's rebuilt as its own panel so the readout survives. Hiding the mix section also reclaims its height: `DrawerHeight` drops, the derived reference shrinks, and the pad grid grows to fill the same `ScreenFraction`.

**Adding a serialized field to `MidiSceneBootstrapper`?** Add a guard to `NormalizeInlineArrays`. Scenes saved before the field existed deserialize it to zero — field initializers do **not** re-run for already-serialized components — so a new `Screen Fill` arrives as `0` and collapses the drawer. This has bitten twice.

**Layout** — read this before changing any drawer sizing. Every rule below fixed a specific bug; the obvious "simplification" for each is the bug.

- MF64 8×8 pad grid on top, MIDI Mix (8 channel strips + horizontal master + SOLO/message/bank utility row) below.
- **Panel scaling is mandatory.** `PanelSettings` are created in code, so they default to `ConstantPixelSize` — every px a literal screen pixel, drawer overflowing small Game views, resolution ignored. `BuildView` must set `scaleMode = ScaleWithScreenSize` and `screenMatchMode = Expand`. `Expand` (never `Shrink`) is what guarantees the UI is never cropped.
- **`referenceResolution` is derived, never authored.** Sizes are design units (`GridSideDesign` 600, paddings, `MixSectionHeight` 301); the reference is the drawer's *own design size ÷ `ScreenFraction`*. `Expand` scales by `min(screenW/refW, screenH/refH)`, so giving the reference the drawer's aspect makes the binding axis land exactly on `ScreenFraction` — height on landscape, width on portrait — with no orientation branch. Two failure modes to avoid: a screen-shaped reference (1920×1080) makes portrait displays badly under-fill, and setting it to the *actual* display resolution pins the scale at 1:1, which is `ConstantPixelSize` again.
- **Screen Fill** (bootstrapper, default 0.90) is the only size knob. `GridSideDesign` sets internal proportions only — scaling it changes nothing on screen, because the derived reference scales with it.
- **The pad square is arithmetic, never measured.** `aspect-ratio` is not a USS property in Unity 6000.0 (added later) — don't reach for it unless the package's `unity` minimum rises. Two earlier approaches both failed: `width: 100%` resolved against a shrink-to-fit parent and stretched the drawer to the screen edge (pads rendered as ellipses), and a `GeometryChangedEvent` height-lock guarded on a height mismatch that a shrinking flex parent could never satisfy, re-setting the style every layout pass and **hard-freezing the editor**. If a measured aspect is ever genuinely required, follow Unity's own aspect-ratio custom control: adjust *padding* behind a tolerance threshold, never set `height`.
- **Column alignment is a coupling.** MIDI Mix channel strips and MF64 pad cells must share identical flex + margin values (`flexGrow:1`, `flexBasis:0`, `marginRight: CellMargin`). Change one, change the other. Neither section may set a `minWidth` — that lets them resolve to different content widths and the 8 columns drift apart.
- **Widget sizes are derived, not literal.** `KnobSize` tracks the MF64 pad cell (`(GridSideDesign − 8×CellMargin) / 8 × 0.88`) so both sections read as one instrument, and `MixSectionHeight` = `StripHeight` (computed from `KnobSize`, `MixPadSize`, `FaderHeight`, `KnobGap`) + `MixChromeHeight`. Resizing a mixer widget therefore corrects the height budget automatically. Putting a literal size in `BuildMixSection` instead leaves the budget stale and silently throws off Screen Fill. `MixChromeHeight` is the one estimated number — it depends on label metrics, so type-size changes move it.
- **Padding constants are referenced by the size math**, not just applied. `DrawerPadX/Y`, `SectionPad`, `CellMargin` appear both where they're set and in `DrawerWidth` / `DrawerHeight`. Inlining a literal in either place silently breaks the square.
- Positioned by flow layout (root flex row, `alignItems:Center`), not absolute offsets, so vertical centering holds at any aspect. `RightCentered` = `justifyContent:FlexEnd`, `ScreenCentered` = `justifyContent:Center`.
- **Hiding sets `display: none`** after the 200 ms slide — that, not the slide, is what guarantees the drawer is gone. The slide is `translate: 120%` of the drawer's *own width*, which clears the viewport only when it's flush right; under `ScreenCentered` it stops mid-screen and stays visible. Don't "fix" that with a larger percentage — no fixed value is correct for every placement and display size. Showing re-enters layout still offset and slides home on the next frame, because a transition cannot animate from a `display:none` element.
- Transparent background — only the section panels render.
- Widgets render at 40 % opacity until they receive their first MIDI event, then snap to full. Distinguishes "not-yet-touched" from "value is zero".
- Bottom message strip shows the most recent MIDI event in uppercase (e.g. `MIX FADER CH7  0.66`).

**Multi-display**
- Automatically renders on every active display (`Display.displays[i].active`). Call `Display.displays[i].Activate()` in your startup code before the drawer's `OnEnable` for secondary displays to appear.

**Read-only**
- All widgets have `pickingMode = Ignore` so scene clicks pass through.
- Painter2D-drawn `KnobDisplay` and `PadCell` custom `VisualElement`s — no PNGs, no external widget library.

### Configure MF64 pad modes and per-pad LED colors

`MidiSceneBootstrapper`'s Inspector shows an embedded 8×8 grid: each cell has a **mode checkbox** (Toggle vs Button) and a **color dropdown** (per-pad active LED color). "All Button / All Toggle / All Colors → Off" quick-fill buttons above the grid.

Alternatively, create a `MidiFighter64ButtonConfig` asset (Create → MidiFighter64 → Button Config) with the same grid, and drag it into the bootstrapper's **Pad Config Asset** slot — an assigned asset wins over the inline grid.

Priority order at runtime for LED colors:
1. Per-pad color from config (unless `Off` = "use fallback").
2. Router's global `_toggleOnColor` / `_buttonDownColor`.
3. `_toggleOffColor` / `Off` for the release/off state (no per-pad off color).

---

## Gotchas

- **First MIDI event per session is often swallowed.** Known Minis quirk. Tell users to press a pad twice on startup.
- **USB hubs cause detection issues.** Prefer a direct port.
- **The MF64 Utility must be set to Base Channel 3, Type = Notes, Corner Button Bank Change = disabled.** These are the defaults but check first if events look wrong.
- **MIDImix master fader is CC 62, not 127.** (127 is a common wrong value online.) Master fader always routes through `OnMasterFader` regardless.
- **MIDImix Send All button** does not emit its own message — pressing it just triggers a burst of all 33 CC values (24 knobs + 8 faders + master). If you need a hook for "user pressed Send All", listen for the burst pattern; there's no dedicated event.
- **MIDImix Bank Left/Right** buttons emit notes 25 and 26 but do NOT shift the CC or note ranges of other controls on stock firmware. They're just notification notes.
- **MIDImix Mute/Rec-Arm LEDs do NOT self-illuminate on press.** The hardware requires the host to echo Note On (velocity 127) back to light them, Note Off / velocity 0 to clear. Add `MidiMixOutput` to the scene and it will auto-mirror presses. Manual control via `MidiMixOutput.Instance.SetMuteLED(ch, lit)` etc. for latched (toggle) behavior.
- **`MidiFighterOutput.ClearOnStart`** is on by default. It sends velocity 0 to all 64 pads on Start and OnDestroy. Turn it off only if you're intentionally preserving LED state across sessions.
- **`OnValidate` on `MidiFighterButtonRouter`** sends the current Toggle-On color to all 64 pads whenever the Inspector value changes in Play mode. This is a feature for previewing colors; if it surprises a user, that's why.
- **URP shader magenta on generated primitives:** in URP projects, do not `Shader.Find("Standard")` — `CreatePrimitive` already assigns a working pipeline material; modify that instead of replacing it.

---

## What NOT to do

- Don't subscribe to `Minis.MidiDevice` directly — go through `MidiEventManager`.
- Don't call `AssetDatabase` at runtime — use `Resources.Load` + a ScriptableObject with a known `ResourceName`.
- Don't invent LED palette values — the enum entries are the confirmed set. For anything else use the raw `int velocity` overload and let the user experiment.
- Don't compute MF64 note numbers manually — always use `MidiFighter64InputMap.ToNote/FromNote`.
- Don't forget `-=` in `OnDisable` for any `+=` in `OnEnable` on a router event.

---

## Test scene

The `Test Scene` sample includes a menu item `Tools → MidiFighter64 → Create Test Scene` that generates a scene with an 8×8 sphere grid, a wave-animation UI button, and a raw MIDI log overlay. Use it as a quick integration test.

The scene requires importing the sample first via **Package Manager → Samples → Test Scene → Import**.

---

## Package structure

```
Runtime/                        (always compiled, autoRef'd)
  MidiEventManager              — Minis subscriber, static C# events
  UnityMainThreadDispatcher     — thread-safe action queue
  MidiFighter64InputMap         — pure static: note ↔ (row, col)
  MidiGridRouter                — MF64 grid routing (typed events)
  MidiFighterButtonRouter       — Button/Toggle layer + LED feedback
  MidiFighter64ButtonConfig     — ScriptableObject: 8×8 mode grid
  MidiFighterButtonMode         — enum { Button, Toggle }
  MidiFighterOutput             — MF64 LED output via RtMidi
    MidiFighterLEDColor         — enum: hardware-confirmed palette
    MidiFighterLEDColorExtensions — .ToUnityColor()
  MidiMixInputMap               — pure static: CC/note ↔ struct
  MidiMixRouter                 — MIDI Mix routing (typed events)
  MidiMixOutput                 — MIDI Mix LED echo (Mute/Solo/Rec-Arm)

Editor/                         (Editor-only)
  MidiFighter64ButtonConfigEditor — 8×8 toggle grid inspector

Tests/Editor/                   (EditMode)
  MidiFighter64InputMapTests    — corner + round-trip asserts

Samples~/TestScene/             (import via Package Manager)
  MidiFighterTestScene          — self-building demo scene
  MidiDebugUI                   — status bar + MIDI log overlay
  MidiSceneBootstrapper         — spawns core components
  Editor/CreateMidiTestScene    — Tools menu → scene generator
```
