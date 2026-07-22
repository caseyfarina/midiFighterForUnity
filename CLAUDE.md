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

## Scene setup

**Drag `Runtime/MIDI Controller.prefab` into the scene.** That is the whole setup — it carries every component, already wired and configured:

| Component | What it does |
|---|---|
| `MidiEventManager` | the only Minis subscriber; merges ports, applies the device filter |
| `UnityMainThreadDispatcher` | marshals MIDI callbacks onto the main thread |
| `MidiGridRouter` | typed MF64 grid events |
| `MidiMixRouter` | typed Akai MIDI Mix events, Mute/Rec-Arm latching |
| `MidiFighterButtonRouter` | Button/Toggle semantics per pad, pad config, LED colors |
| `MidiFighterOutput` | drives the physical MF64 LEDs |
| `MidiMixOutput` | lights MIDI Mix Mute/Rec-Arm (the hardware does NOT self-illuminate — LEDs are host-controlled) |
| `MidiStatusDrawer` | the on-screen overlay mirroring both controllers |

Every setting lives on the component that owns it — select the prefab instance and configure it there. Don't hunt for a separate config object; there isn't one, deliberately (see `DEVNOTES.md` for why the old bootstrapper was removed).

Remove a component you don't need — nothing depends on all eight being present. Deleting `MidiStatusDrawer` drops the overlay; deleting both `*Output` components makes the rig input-only.

**Installed via git URL, the package is read-only**, so the prefab can't be edited in place. Configure the *instance* in your scene — those overrides are stored in the scene, which is normal Unity prefab behaviour.

If a component gains a field and the shipped prefab needs regenerating, `Tools → MidiFighter64 → Regenerate Controller Prefab` rebuilds it (only works from an embedded copy of the package).

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

MIDI Mix Mute and Rec-Arm buttons are momentary in *hardware*, but the router latches them by default: `MidiMixRouter.LatchMute` and `LatchRecArm` are both **true**. Press once to turn on, press again to turn off. `OnMute` / `OnRecArm` then fire only on note-on and pass the *new latched state* (not raw isNoteOn). `MidiMixOutput` subscribes to those router events, so the hardware LED stays lit for as long as the button is latched on, and `MidiStatusDrawer` holds the corresponding pad filled.

Untick the two flags in the "Latching (press-to-toggle) buttons" section on `MidiMixRouter` for the old momentary behaviour, where the events fire on both note-on and note-off with the raw button state.

`OnSolo` (Mute-while-SOLO) is always momentary — the SOLO modifier has to be held for those notes to be emitted at all, so there is no latch to hold.

### Detect SOLO-held mute presses

The MIDImix SOLO button is a modifier — holding it makes the 8 Mute buttons emit a different note set. The router splits these into two events:

```csharp
MidiMixRouter.OnMute += (ch, on) => { /* fires when SOLO is NOT held */ };
MidiMixRouter.OnSolo += (ch, on) => { /* fires when SOLO IS held */ };

// Or query modifier state on demand:
if (MidiMixRouter.IsSoloHeld) { ... }
```

### Show a live status drawer overlay

`MidiStatusDrawer` is a screen-space UI Toolkit overlay that mirrors both controllers in real time. It ships on the MIDI Controller prefab; delete the component from your scene instance to drop the overlay, or add it to any GameObject to use it standalone.

**Hotkeys**
- **Backtick** (`` ` ``) or **F1** — show/hide the drawer.
- **F2** — cycle `DrawerPlacement` (Right Centered ⇄ Screen Centered).
- **F3** — cycle `DrawerTheme` (Dark ⇄ Light).
- **F4** — cycle `DrawerLayout` (Linear 1 ⇄ Radial 1).

The **F-keys only** are gated by `EnableFunctionKeys` (**Enable Function Keys** on the drawer), on by default. Untick it when the project binds F1–F3 itself. **Backtick is never gated** — it's the show/hide key, and a hidden drawer with no way back is a dead end. F1 is an alias for backtick, so it *is* gated. Every shortcut also has a scripting equivalent.

**Layout modes** — `MidiStatusDrawer.Layout`, or F4.

- **Linear 1** (default) — the 8×8 pad grid above eight vertical mixer strips.
- **Radial 1** — concentric rings, centre outward: the 64 pads as four rings, then three knob arcs and a fader arc per channel inside each channel's 45° slice, a full-circumference master ring, and an outer ring of Mute / Rec-Arm pairs. Channel 1 is at the top, channels advance clockwise.
- **Radial 2** — reserved for the sunburst layout; not built yet, falls back to Linear 1.

All layouts mirror the same live MIDI state. A radial builder populates the *same* `DrawerView` arrays with the same `PadCell` instances, just positioned differently, which is why every event handler works unchanged across layouts. Mixer values additionally drive `RadialArc` widgets stored in `knobArcs` / `faderArcs` / `masterArc`; each handler updates the linear widget and the arc behind **independent** null checks, since each layout populates one and leaves the other null.

`DrawerLayout.Linear1` must stay enum value 0 — a serialized field added later deserializes to zero on existing scenes and prefab instances, so zero has to be the safe default rather than a layout nobody chose.

Things that are deliberately different in radial:

- **Solo modifier and Bank L/R are not built.** Those `DrawerView` fields stay null; the handlers already null-check.
- **Fisheye does nothing.** It scales `mf64Rows` via flex-grow and no radial builder populates those, so `FocusMf64Pad` returns early.
- **The event readout floats** at the display's bottom-left rather than flowing under the square, and so contributes nothing to `DrawerHeight`. It lives on the *root*, not the drawer, which is why `ApplyHiddenState` has to hide it explicitly — the drawer's slide doesn't cover it.
- **Section visibility keeps radii fixed.** Hiding the mixer leaves the pad rings where they are rather than re-flowing the stack, which would need a second radius table per combination.

**Inspector controls** (on the `MidiStatusDrawer` component)
- **Placement** — `Right Centered` (pinned right, centered vertically) or `Screen Centered` (centered both axes). Runtime equivalent: `MidiStatusDrawer.Instance.Placement`. Restyles the root container; no rebuild.
- **Show Midi Fighter 64** / **Show MIDI Mix** — both on by default. Untick one to run a single controller without a dead panel taking up drawer space. Runtime equivalents: `ShowMf64` / `ShowMidiMix`, or `SetVisibleSections(mf64, mix)` to change both with one rebuild.
- **Drawer Font** — optional typeface override. Empty = the bundled `CossetteTitre-Regular.ttf` in `Runtime/UI/Resources/`, loaded via `Resources.Load<Font>(MidiStatusDrawer.BundledFontResourceName)`. Falls back to a dynamic OS font (Arial/Helvetica) if that Resources folder is missing.
- **Enable MF64 Fisheye** — the last-touched pad grows while its row/column neighbors deform to compensate. On by default. Also settable at runtime via `MidiStatusDrawer.Instance.EnableMf64Fisheye`; assigning `false` clears any active focus.
- **Fisheye Scale** — how far the focused pad grows, 1–6 (default 3). Runtime equivalent: `Mf64FisheyeScale`. It's a **flex-grow weight**, not a pixel size or a multiplier: the focused row/column takes N shares against the other seven rows/columns' 1, so the grid stays exactly square and the growth is always paid for by the neighbors. `1` means no visible growth without disabling the feature. Setting it re-applies to a currently focused pad, so the slider is live in play mode.

- **Theme** — `Dark` (default) or `Light`. Runtime equivalent: `MidiStatusDrawer.Instance.Theme`, or F3. The panels are semi-transparent and tint toward whatever is behind them, so pick the theme that *opposes* the scene background, not the one that matches it. Restyles in place; never rebuilds, so widgets keep their "seen" state.
- **Stroke Weight** — global line-weight multiplier for stroked widgets (knob bodies, pad rings), 0.25–4 (default 1). Runtime equivalent: `StrokeWeight`. It multiplies the single `KnobDisplay.StrokeWidth` base that both custom elements share, through their `StrokeScale` property. Radii are all fractions of each element's own size, so thickness is the *only* thing that changes — widget sizes, the pad grid's square, and `MixSectionHeight` are all untouched. Any new stroked drawing must go through `StrokeWidth * StrokeScale` or it won't follow the slider.
- **Panel Opacity** — alpha of the section panels and message strip, 0–1 (default 0.30). Runtime equivalent: `PanelOpacity`. Widget ink is never faded by this, so the readout stays legible even at 0 (widgets floating on the scene with no panel).
- **UI Opacity** — opacity of everything the drawer draws: pads, knobs, faders, mixer buttons, and all type (captions, bank arrows, the event strip), 0–1 (default 1). Runtime equivalent: `UiOpacity`. The complement to Panel Opacity: that fades the backing panels and leaves the ink alone, this fades the ink and leaves the panels alone. Both at 0 and the drawer is invisible; panels at 0 with this at 1 gives widgets floating on the scene. It **multiplies** with the 40 % dimming applied to controls that haven't received a MIDI event yet, rather than replacing it, so "not yet touched" stays readable at any setting. Applied by `ApplyUiOpacity`, which recomputes from the stored `_knobsSeen` / `_fadersSeen` / `_masterFaderSeen` flags — those are the source of truth and the elements only mirror them. Like `ApplyTheme` it restyles in place and never rebuilds, because a rebuild would reset the seen flags and wrongly re-dim everything already touched. **Any new widget opacity must go through `SeenScale()`** or it won't follow the slider.
- **Pad Size** / **Ring Spread** *(Radial only)* — pad diameter as a multiple of the reference layout, and a multiplier on the four ring radii. They compete for the same space: lowering Ring Spread tightens the cluster but also shortens each ring's circumference, so pads crowd sooner as Pad Size rises. **Ring 0 binds first** — its 28 pads get the least arc each despite sitting on the longest ring. Runtime equivalents: `RadialPadScale` / `RadialRingSpread`.
- **Vertical Offset** *(Radial only)* — moves the ring stack up or down within the display, ±1 being half a square. Runtime equivalent: `RadialVerticalOffset`. Applied as a transform on the *section*, deliberately not the drawer: the drawer's own `translate` is owned by the show/hide slide, and a margin would feed back into the size budget.
- **Readout Padding** *(Radial only)* — inset of the floating event readout from the display's bottom-left corner. Runtime equivalent: `RadialMessagePadding`.
- **Screen Fill** — fraction of the display the drawer occupies on whichever axis binds first: height on a landscape display, width on a portrait one. Never crops. Runtime equivalent: `ScreenFraction`.
- **Log Layout Report** — diagnostic, off by default. Dumps one resolved-geometry report to the console ~400 ms after the drawer is first *shown* (press `` ` ``; a hidden drawer is `display:none` and measures `NaN`). Reports screen size, derived reference resolution, drawer/grid/cell sizes, screen coverage, mixer-vs-grid column widths, and the measured mix section height. Use it instead of eyeballing: the grid and cell lines must be square, one coverage axis must equal Screen Fill, and `mix section h` is how you correct `MixChromeHeight`. Reads `resolvedStyle` only, never writes — that distinction is what keeps it from feeding back into layout.

Section visibility is baked into the UI tree, so toggling it rebuilds all views. Widget "seen" opacity resets on rebuild; the hidden state survives. The event message strip normally lives in the MIDI Mix utility row — with Mix hidden it's rebuilt as its own panel so the readout survives. Hiding the mix section also reclaims its height: `DrawerHeight` drops, the derived reference shrinks, and the pad grid grows to fill the same `ScreenFraction`.

Hiding the MF64 section reclaims its height the same way. **Every term in `DrawerHeight` is conditional on its section, and must stay that way** — a term that outlives the section it measures leaves phantom units in the derived reference, and because `Expand` scales by `min(screenW/refW, screenH/refH)` the drawer then under-fills by exactly that ratio. `Screen Fill` still hits its target; the target is just mostly empty space. This was a real bug: `GridSide` was unconditional, so a mixer-only drawer rendered at ~40% of the display. The Log Layout Report's `sections` line prints the budget's terms for this reason — read it before suspecting `ScreenFraction`.

**Adding a serialized field to any rig component?** Field initializers do **not** re-run for an already-serialized component, so on scenes and prefab instances saved before the field existed it arrives at **zero**. If zero is outside the field's legal range, guard it (`if (_screenFraction <= 0f) _screenFraction = 0.90f;`) — a `Screen Fill` of `0` collapses the drawer, and this has bitten twice. `MidiFighterButtonRouter.NormalizeInlineArrays` does the same job for the inline pad arrays, which arrive at length 0. If zero is a *legitimate* value (any bool, or `Panel Opacity` where 0 means "no panel"), a value guard would clobber a deliberate choice and you need a version stamp instead.

**Layout** — read this before changing any drawer sizing. Every rule below fixed a specific bug; the obvious "simplification" for each is the bug.

- MF64 8×8 pad grid on top, MIDI Mix (8 channel strips + horizontal master + SOLO/message/bank utility row) below.
- **Panel scaling is mandatory.** `PanelSettings` are created in code, so they default to `ConstantPixelSize` — every px a literal screen pixel, drawer overflowing small Game views, resolution ignored. `BuildView` must set `scaleMode = ScaleWithScreenSize` and `screenMatchMode = Expand`. `Expand` (never `Shrink`) is what guarantees the UI is never cropped.
- **`referenceResolution` is derived, never authored.** Sizes are design units (`GridSideDesign` 600, paddings, `MixSectionHeight` 301); the reference is the drawer's *own design size ÷ `ScreenFraction`*. `Expand` scales by `min(screenW/refW, screenH/refH)`, so giving the reference the drawer's aspect makes the binding axis land exactly on `ScreenFraction` — height on landscape, width on portrait — with no orientation branch. Two failure modes to avoid: a screen-shaped reference (1920×1080) makes portrait displays badly under-fill, and setting it to the *actual* display resolution pins the scale at 1:1, which is `ConstantPixelSize` again.
- **Screen Fill** (default 0.90) is the only size knob. `GridSideDesign` sets internal proportions only — scaling it changes nothing on screen, because the derived reference scales with it.
- **The pad square is arithmetic, never measured.** `aspect-ratio` is not a USS property in Unity 6000.0 (added later) — don't reach for it unless the package's `unity` minimum rises. Two earlier approaches both failed: `width: 100%` resolved against a shrink-to-fit parent and stretched the drawer to the screen edge (pads rendered as ellipses), and a `GeometryChangedEvent` height-lock guarded on a height mismatch that a shrinking flex parent could never satisfy, re-setting the style every layout pass and **hard-freezing the editor**. If a measured aspect is ever genuinely required, follow Unity's own aspect-ratio custom control: adjust *padding* behind a tolerance threshold, never set `height`.
- **Column alignment is a coupling.** MIDI Mix channel strips and MF64 pad cells must share identical flex + margin values (`flexGrow:1`, `flexBasis:0`, `marginRight: CellMargin`). Change one, change the other. Neither section may set a `minWidth` — that lets them resolve to different content widths and the 8 columns drift apart.
- **Widget sizes are derived, not literal.** `KnobSize` tracks the MF64 pad cell (`(GridSideDesign − 8×CellMargin) / 8 × 0.88`) so both sections read as one instrument, and `MixSectionHeight` = `StripHeight` (computed from `KnobSize`, `MixPadSize`, `FaderHeight`, `KnobGap`) + `MixChromeHeight`. Resizing a mixer widget therefore corrects the height budget automatically. Putting a literal size in `BuildMixSection` instead leaves the budget stale and silently throws off Screen Fill. `MixChromeHeight` is the one estimated number — it depends on label metrics, so type-size changes move it.
- **Padding constants are referenced by the size math**, not just applied. `DrawerPadX/Y`, `SectionPad`, `CellMargin` appear both where they're set and in `DrawerWidth` / `DrawerHeight`. Inlining a literal in either place silently breaks the square.
- Positioned by flow layout (root flex row, `alignItems:Center`), not absolute offsets, so vertical centering holds at any aspect. `RightCentered` = `justifyContent:FlexEnd`, `ScreenCentered` = `justifyContent:Center`.
- **Hiding sets `display: none`** after the 200 ms slide — that, not the slide, is what guarantees the drawer is gone. The slide is `translate: 120%` of the drawer's *own width*, which clears the viewport only when it's flush right; under `ScreenCentered` it stops mid-screen and stays visible. Don't "fix" that with a larger percentage — no fixed value is correct for every placement and display size. Showing re-enters layout still offset and slides home on the next frame, because a transition cannot animate from a `display:none` element.
- Transparent background — only the section panels render.
- **Every color goes through `Palette.For(theme, opacity)`.** Don't add a literal `Color` at a build site — a theme switch calls `ApplyTheme`, which repaints in place and would skip it. Elements the `DrawerView` class doesn't retain (section panels, captions) are found by USS class (`SectionClass` / `LabelClass` / `MessageClass`), so anything new that needs theming must either be stored on the view or carry one of those classes. `BuildAllViews` ends with an `ApplyTheme()` pass, so a build site that forgets the palette still lands correctly — but only for properties `ApplyTheme` actually touches.
- **MF64 pad fills are exempt from the theme** — they mirror real hardware LED colors. `Palette.AdaptLed` darkens only the near-white end of the LED palette (White/Grey/DarkGrey), and only in Light theme, because those are invisible on a light panel; saturated colors pass through untouched. `_padRawFill` keeps the pre-adaptation color per pad so a theme switch re-adapts from the original instead of compounding.
- Widgets render at 40 % opacity until they receive their first MIDI event, then snap to full. Distinguishes "not-yet-touched" from "value is zero".
- Bottom message strip shows the most recent MIDI event in uppercase (e.g. `MIX FADER CH7  0.66`).

**Multi-display**
- Automatically renders on every active display (`Display.displays[i].active`). Call `Display.displays[i].Activate()` in your startup code before the drawer's `OnEnable` for secondary displays to appear.

**Read-only**
- All widgets have `pickingMode = Ignore` so scene clicks pass through.
- Painter2D-drawn `KnobDisplay` and `PadCell` custom `VisualElement`s — no PNGs, no external widget library.
- `KnobDisplay` draws a 31-dot ring on a 270° arc (gap at the bottom), a stroked body, and a pointer dot. **All radii are fractions of the element's own size**, never literals, so one knob size constant (`KnobSize`) drives the whole thing. Angles use UI coordinates — y grows *downward*, so 135° is bottom-left, 270° is straight up, 405° is bottom-right. The pointer and the ring share one angle mapping, so the pointer always lands on the last lit dot; changing one without the other breaks that.

### Configure MF64 pad modes and per-pad LED colors

`MidiFighterButtonRouter`'s Inspector shows an embedded 8×8 grid: each cell has a **mode checkbox** (Toggle vs Button) and a **color dropdown** (per-pad active LED color). "All Button / All Toggle / All Colors → Off" quick-fill buttons above the grid.

Alternatively, create a `MidiFighter64ButtonConfig` asset (Create → MidiFighter64 → Button Config) with the same grid, and drag it into the router's **Pad Config Asset** slot — an assigned asset wins over the inline grid.

Priority order at runtime for LED colors:
1. Per-pad color from config (unless `Off` = "use fallback").
2. Router's global `_toggleOnColor` / `_buttonDownColor`.
3. `_toggleOffColor` / `Off` for the release/off state (no per-pad off color).

---

## Gotchas

- **A second MIDI port carrying a copy of the same traffic breaks every latch.** `MidiEventManager` connects to *every* MIDI input port and merges them, so a MIDI monitor, loopback (loopMIDI), or network port that echoes a controller delivers each message twice in one frame. Momentary handlers can't tell — on/on then off/off lands in the same place — but anything press-to-toggle toggles on and straight back off, so the button looks dead while the raw events look perfect. `MidiEventManager` warns once per session when it sees the same note from two ports in one frame. Fix with `AllowedDeviceNames` / `BlockedDeviceNames` (substring match on the port name) on the `MidiEventManager` component. The shipped prefab sets that allow list to `{ "Fighter", "MIDI Mix" }`; note the component's own default is *empty*, meaning "accept every port", so a hand-built rig has no protection until you fill it in. **When a latch "doesn't work", check the device list before reading the latch code** — the latch code is usually fine.
- **First MIDI event per session is often swallowed.** Known Minis quirk. Tell users to press a pad twice on startup.
- **USB hubs cause detection issues.** Prefer a direct port.
- **The MF64 Utility must be set to Base Channel 3, Type = Notes, Corner Button Bank Change = disabled.** These are the defaults but check first if events look wrong.
- **MIDImix master fader is CC 62, not 127.** (127 is a common wrong value online.) Master fader always routes through `OnMasterFader` regardless.
- **MIDImix Send All button** does not emit its own message — pressing it just triggers a burst of all 33 CC values (24 knobs + 8 faders + master). If you need a hook for "user pressed Send All", listen for the burst pattern; there's no dedicated event.
- **MIDImix Bank Left/Right** buttons emit notes 25 and 26 but do NOT shift the CC or note ranges of other controls on stock firmware. They're just notification notes.
- **MIDImix Mute/Rec-Arm LEDs do NOT self-illuminate on press.** The hardware requires the host to echo Note On (velocity 127) back to light them, Note Off / velocity 0 to clear. Add `MidiMixOutput` to the scene and it will auto-mirror the router's state — which, with the default latching, means the LED stays lit until the button is pressed again. Manual control via `MidiMixOutput.Instance.SetMuteLED(ch, lit)` etc.
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

## Checking it works

There is no sample to import — the package ships no samples at all. The integration test is the prefab itself:

1. Drag `Runtime/MIDI Controller.prefab` into any scene.
2. Press Play, then press a pad or move a knob.

The status drawer mirrors both controllers live — all 64 pads, 24 knobs, 9 faders, and the mute / rec-arm buttons — with the most recent MIDI event named in the strip at the bottom. If a control moves on screen, the whole chain from hardware through `MidiEventManager` to the routers is working. Press `` ` `` to hide the drawer once you're satisfied.

If nothing moves, check the device list first (see Gotchas) — a duplicate MIDI port is the most common cause and it does not look like a device problem.

---

## Package structure

```
Runtime/                        (always compiled, autoRef'd)
  MIDI Controller.prefab        — THE entry point: all 8 components, wired
  MidiEventManager              — Minis subscriber, static C# events
  UnityMainThreadDispatcher     — thread-safe action queue
  MidiFighter64InputMap         — pure static: note ↔ (row, col)
  MidiGridRouter                — MF64 grid routing (typed events)
  MidiFighterButtonRouter       — Button/Toggle layer + LED feedback,
                                  inline 8×8 pad grid
  MidiFighter64ButtonConfig     — ScriptableObject: 8×8 mode grid
  MidiFighterButtonMode         — enum { Button, Toggle }
  MidiFighterOutput             — MF64 LED output via RtMidi
    MidiFighterLEDColor         — enum: hardware-confirmed palette
    MidiFighterLEDColorExtensions — .ToUnityColor()
  MidiMixInputMap               — pure static: CC/note ↔ struct
  MidiMixRouter                 — MIDI Mix routing (typed events)
  MidiMixOutput                 — MIDI Mix LED echo (Mute/Solo/Rec-Arm)
  MidiToggle                    — example: pad toggles a GameObject
  MidiRotator                   — example: spin a transform
  MidiMixCloner                 — example: mixer drives cloned objects
  MidiNoteLogger                — example: log raw notes
  UI/
    MidiStatusDrawer            — live on-screen overlay (UI Toolkit)
    PadCell / KnobDisplay       — Painter2D custom VisualElements
    RadialArc                   — Painter2D arc: track + value fill
    Resources/                  — bundled font + its OFL license

Editor/                         (Editor-only)
  MidiFighter64ButtonConfigEditor — 8×8 grid inspector (SO asset)
  MidiFighterButtonRouterEditor   — 8×8 grid inspector (inline arrays)
  MidiFighter64PadGridGUI         — shared grid drawing, used by both
  MidiStatusDrawerEditor          — grouped drawer inspector (linear vs radial)
  MidiToggleEditor                — inspector for the MidiToggle example
  CreateMidiControllerPrefab      — Tools menu → regenerate the prefab

Tests/Editor/                   (EditMode)
  MidiFighter64InputMapTests    — corner + round-trip asserts

(no samples — the prefab is the entry point)
```
