# MIDI Status Drawer — Implementation Plan

**Goal:** an overlay drawer that shows live state of both MIDI controllers (Midi Fighter 64 + Akai MIDI Mix) on top of any scene, so developers don't need to load a dedicated test scene to verify MIDI is working.

**Status:** planning. Not yet implemented. Pick this up in a fresh session.

---

## Design decisions (already made)

- **Widget approach:** plain **Unity UI Toolkit** — no VJUITK, no external widget library. Two small custom `VisualElement` subclasses (`KnobDisplay`, `PadCell`) plus stock `VisualElement`/`Label` for everything else. Read-only overlay, so we don't need VJUITK's interactive controls; suppressing their events would be wasted work.
- **Styling:** USS only. Shapes via `background-color` + `border-radius` + `rotate` for indicators; value arcs/bars drawn procedurally in `generateVisualContent` using `Painter2D`. No PNGs required to ship — can be added later purely as USS `background-image` swaps.
- **Fader = vertical bar,** knob = circle + rotated indicator (or Painter2D arc). Both driven by a single 0–1 `value` field.
- **MF64 grid:** 8×8 of `PadCell`s. Each cell reads its mode from `MidiFighter64ButtonConfig.GetMode(btn)` and displays either latched-toggle state (`IsToggled`) or momentary-held state (`IsHeld`). No per-cell subclass split needed — one `PadCell` handles both by asking the router.
- **Drawer transitions:** USS `translate` + `transition-property` (200ms).

## Decisions (locked)

1. **Trigger:** hotkey — backtick (`` ` ``) primary, F1 secondary. Read via new Input System (`Keyboard.current.backquoteKey.wasPressedThisFrame`), not legacy `Input`.
2. **Location:** `Samples/TestScene/UI/`. Imported on demand alongside the existing Test Scene sample.
3. **Initial state for MIDI Mix widgets:** show 0, rendered at ~40% opacity ("unknown"). On first event for that widget, snap to full opacity and the real value. Track a `bool _seen` per widget. Applies to all 24 knobs + 8 channel faders + master fader.
4. **Solo behavior:** just light the Solo Modifier toggle while SOLO is held; **freeze the Mute row** showing last-known mute state. Per-channel Solo state is not visualized. Simplest option; keeps mute state readable during SOLO holds.
5. **Singleton pattern:** enforce single-instance behavior like `MidiFighterOutput.Instance` — destroy duplicates in `Awake`, expose `public static MidiStatusDrawer Instance`.

**Dependency-scope consequence of the Samples location:**
- **No new external packages.** UI Toolkit ships with Unity 6 as a built-in module (`UnityEngine.UIElements`) — nothing to install, no scoped registry, no manual consumer setup.
- Update `MidiFighter64.Samples.asmdef` to reference `UnityEngine.UIElementsModule` (name may vary; verify — UI Toolkit runtime types may already be transitively available through the sample's existing refs).
- Runtime asmdef stays untouched.

---

## Widget mapping — MF64

| Element | Element type | State source |
|---|---|---|
| 64 pads (8×8) | `PadCell : VisualElement` | `MidiFighter64ButtonConfig.GetMode(btn)` picks Toggle vs Button behavior |
| Pad "lit" state (Toggle mode) | `PadCell` background alpha/tint | `MidiFighterButtonRouter.IsToggled(note)` |
| Pad "held" state (Button mode) | `PadCell` brief flash on press | `MidiFighterButtonRouter.IsHeld(note)` |
| LED tint | inline `style.backgroundColor` | `MidiFighterLEDColor.X.ToUnityColor()` |

## Widget mapping — MIDI Mix

| Element | Element type | State source |
|---|---|---|
| 24 knobs (3 rows × 8 channels) | `KnobDisplay : VisualElement` (rotated indicator or Painter2D arc) | `MidiMixRouter.OnKnob` (channel, row, value) |
| 8 channel faders | vertical bar `VisualElement` (child height = value × parent height) | `MidiMixRouter.OnChannelFader` (channel, value) |
| Master fader | vertical bar `VisualElement` | `MidiMixRouter.OnMasterFader` (value) |
| 8 Mute buttons | `PadCell` in Toggle mode | last `OnMute(ch, on)` |
| 8 Rec-Arm buttons | `PadCell` in Toggle mode | last `OnRecArm(ch, on)` |
| Solo Modifier | `PadCell` in Button mode | `MidiMixRouter.IsSoloHeld` (held while true) |
| Bank Left / Right | `PadCell` in Button mode | brief held-flash on `OnBankLeft` / `OnBankRight` |

Read-only display. Set `pickingMode = PickingMode.Ignore` on all input-mirroring elements so mouse clicks can't inject fake state.

---

## Package changes required

### 1. Declare `com.unity.ugui` in root `package.json`

In Unity 6, `com.unity.ugui` (2.0.0+) is the umbrella package that ships both uGUI **and** UI Toolkit runtime (`UnityEngine.UIElements`). The drawer needs UI Toolkit runtime, so declare it explicitly:

```json
"dependencies": {
  "com.unity.inputsystem": "1.7.0",
  "com.unity.ugui": "2.0.0",
  "jp.keijiro.minis": "1.3.2",
  "jp.keijiro.rtmidi": "2.2.0"
}
```

Rationale: it's a Unity-shipped, no-registry package that's already in most Unity 6 projects. Declaring it in the root avoids silent compile breaks for anyone who imports the Test Scene sample without reading a README first. Not the same kind of imposition as a third-party lib.

### 2. Asmdef updates

`Samples/TestScene/MidiFighter64.Samples.asmdef` — add `UnityEngine.UI` to references if not present (UI Toolkit runtime types come with `com.unity.ugui`). Verify on first compile.

Runtime asmdef stays untouched.

### 3. New files

```
Samples/TestScene/UI/
  MidiStatusDrawer.cs                   MonoBehaviour that owns the UIDocument
  MidiStatusDrawer.uxml                 layout: two sections (MF64 top, MIDI Mix bottom)
  MidiStatusDrawer.uss                  styling: dark theme + drawer slide transition
  MidiStatusDrawer.panelsettings.asset  PanelSettings for the UIDocument
  KnobDisplay.cs                        VisualElement subclass, value 0-1, rotated indicator (or Painter2D arc)
  PadCell.cs                            VisualElement subclass, LED tint + toggle/hold states
  MF64GridElement.cs                    VisualElement subclass; builds 8x8 grid of PadCell from config
```

### 4. Sample bootstrap wiring

Add to `MidiSceneBootstrapper.EnsureCoreComponents`:
```csharp
Ensure<MidiStatusDrawer>();
```
(Fine because both the bootstrapper and the drawer live under `Samples/TestScene/`.)

---

## Implementation order

0. **Prereq:** expose `MidiFighterButtonRouter.Config` publicly (currently private `_config` at `Runtime/MidiFighterButtonRouter.cs:21`). Add `public MidiFighter64ButtonConfig Config => _config;`.
1. **Create PanelSettings asset** and a bare `MidiStatusDrawer` MonoBehaviour + UIDocument. Confirm an empty overlay renders in Play mode.
2. **Static layout first:** write UXML with hard-coded placeholder values (empty PadCells, empty KnobDisplays). Verify it renders correctly.
3. **USS styling:** dark theme (`#0d0d0f` background, `#ff4faf` accent). Drawer slide transition via `translate` + `transition-property: translate` (200ms).
4. **Build `KnobDisplay` and `PadCell` custom elements.**
   - `KnobDisplay`: pure Painter2D in `generateVisualContent`. Draw circle stroke + 24 tick dots around a 270° arc (start 135°, sweep 270°) + inner indicator dot + highlighted "current value" tick.
     - Tick ramp: **fill to current value** — ticks at position ≤ value render at full radius, above value render at ~15% radius. Reads like a fuel gauge.
     - Current value tick: drawn at ~2× radius in accent color.
     - Inner indicator: filled dot at radius `r * 0.7`, angle `arcStart + value * arcSweep`.
     - `value` setter calls `MarkDirtyRepaint()`.
   - `PadCell`: Painter2D circle, **same stroke weight as `KnobDisplay`** (share a constant, e.g. `const float StrokeWidth = 2f`). Empty circle when idle. Inner filled circle appears on activation:
     - **Toggle mode** (config = Toggle): inner filled circle at **2/3 radius** when `IsToggled(note) == true`. Persists until toggled off.
     - **Button mode** (config = Button): inner filled circle at **1/5 radius** while `IsHeld(note) == true`. Disappears on release.
     - Fill color = `MidiFighterLEDColor.ToUnityColor()` for that pad. Stroke = neutral (e.g. `#888`).
     - Both circles drawn in `generateVisualContent`; `MarkDirtyRepaint()` on state change.
     - Also usable as-is for MIDI Mix Mute / Rec-Arm / Solo Modifier / Bank buttons — same visual vocabulary, just different sizes.
   - Both: `pickingMode = PickingMode.Ignore`.
5. **Wire MIDI Mix section:** subscribe in `OnEnable`, unsubscribe in `OnDisable`. Update each `KnobDisplay.value` in the handler; on first event per widget flip its `--unseen` USS class off (opacity 0.4 → 1.0). One `-=` for every `+=` (see CLAUDE.md rule). Avoid per-update allocations (Send All burst hits 33 CCs at once).
6. **Wire MF64 grid section:** read the router's `Config`. Build 64 `PadCell` children in `OnEnable`. Update on `OnToggle` / `OnButtonPress` / `OnButtonRelease`.
7. **Hotkey trigger:** in `Update`, poll `Keyboard.current.backquoteKey.wasPressedThisFrame` (fallback: `f1Key`). Toggle a `--hidden` USS class on the root that flips the `translate`.
8. **Verify no leaks:** on scene reload UIDocument + event subscriptions release cleanly. Cross-check the `OnDisable` unsubscribe convention.
9. **Update docs:**
   - `CLAUDE.md` — "Drawer overlay" section under Common tasks
   - `Documentation~/index.html` — screenshot + paragraph
   - `CHANGELOG.md` — new 1.2.0 entry
10. **Bump `package.json` to 1.2.0.**

---

## Gotchas to avoid

- **UIDocument singleton:** if two `MidiStatusDrawer` components end up in the scene, both try to render — enforce the singleton pattern like `MidiFighterOutput` does.
- **Static event resets on domain reload:** VisualElements are not MonoBehaviours and don't survive domain reloads. Rebuild the tree on `OnEnable`.
- **UIDocument execution order:** UIDocument's root VisualElement isn't available until after `OnEnable`. If you subscribe to MIDI events in `Awake` before the UI exists, the first events will hit null. Subscribe in `OnEnable` *after* the layout is initialized.
- **Bank buttons don't shift ranges:** the drawer should not attempt to "swap banks" visually — the hardware doesn't. Just pulse the button on press.
- **MIDImix Send All burst:** pressing Send All emits 33 CC messages back to back. The drawer will get spammed with updates. That's fine — UI Toolkit handles this cheaply — but don't add any per-update allocations in the handlers.

---

## Reference: existing state accessors

Everything the drawer needs to read is already exposed on the runtime API:

**MF64 (via `MidiFighterButtonRouter`)**
- `IsToggled(int noteNumber) → bool`
- `IsHeld(int noteNumber) → bool`
- `ButtonDownColor` (public property)
- `OnButtonPress` / `OnButtonRelease` / `OnToggle` (static events)
- **TODO:** expose `Config` publicly (currently `[SerializeField] MidiFighter64ButtonConfig _config;` — add `public MidiFighter64ButtonConfig Config => _config;`)

**MIDI Mix (via `MidiMixRouter`)**
- `IsSoloHeld` (static bool)
- `OnKnob`, `OnChannelFader`, `OnMasterFader`, `OnMute`, `OnSolo`, `OnRecArm`, `OnSoloModifier`, `OnBankLeft`, `OnBankRight` (static events)
- No cached last-value getters — the drawer needs to maintain its own state dictionary keyed by (channel, row) or just (CC number)

**Palette (`MidiFighterLEDColor` in `MidiFighterOutput.cs`)**
- `.ToUnityColor()` extension for on-screen tinting

---

## Estimated scope

- ~400 lines C# total (`MidiStatusDrawer.cs` + `KnobDisplay.cs` + `PadCell.cs` + `MF64GridElement.cs`) — a bit more than the VJUITK version to make up for the custom elements, but no external dep and full styling control
- ~150 lines UXML
- ~120 lines USS
- 1 new `package.json` dependency (`com.unity.ugui` 2.0.0 — Unity-shipped, no registry setup)
- ~30 min for docs + changelog

Single afternoon of work assuming the design is settled before starting.
