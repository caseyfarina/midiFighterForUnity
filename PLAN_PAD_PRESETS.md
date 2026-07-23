# MF64 Pad Presets — Implementation Plan

**Goal:** switch the whole MF64 pad arrangement (Button/Toggle modes + per-pad colours) at runtime during a performance — e.g. arrangement A for one section, B for the next — with the hardware LEDs, the on-screen drawer, and the toggle states all staying correct across the switch.

**Status:** planned, not started. One open decision (toggle-state policy) to confirm before coding.

---

## What already exists — the building block is solid

`MidiFighterButtonRouter.Config` has a public setter, and the router resolves the mode **live on every pad event** (`ActiveConfig.GetMode(btn)` in `HandleNoteOn` / `HandleNoteOff`). So:

```csharp
router.Config = sectionBConfig;   // next press already uses B's modes
```

changes pad *behaviour* instantly — no rebuild, no editor. Create N `MidiFighter64ButtonConfig` assets (one per section) and the behavioural half is done today.

What is **not** handled on a live swap, and what this plan adds, are the three things that keep the swap from being visibly correct.

---

## Problem 1 — LEDs don't reconcile to the new config *(the priority)*

**Requirement:** after a switch, every MF64 LED must reflect that pad's mode-in-the-new-config and its state.

**Today:** `PushToggleLEDs()` exists but only covers pads that are *Toggle in the current config* — it `continue`s past every non-toggle pad. So on a swap:

- A pad that was **Toggle → Button** stays lit with its stale toggle colour. `PushToggleLEDs` skips it because it's now a Button, so nothing turns it off. **This is the visible bug.**
- A pad that stays Toggle gets re-driven to its state's colour — correct.
- A pad that was Button (Off, or lit-while-held) → Toggle picks up its stored state — correct once the method runs.

**Fix — `ResyncAllLEDs()` on the router.** Walk all 64 notes and drive each to the colour its *current* config mode implies:

- Toggle pad → `DriveToggleLED(note, storedState)` (the existing per-pad-or-global colour logic).
- Button pad → `DriveButtonLED(note, false)` = Off (button idle is always Off in current semantics — a held pad during a switch is a rare edge; drive it Off and let the next press re-light it).

`PushToggleLEDs` becomes a special case of this (its toggle branch is identical); either call `ResyncAllLEDs` from it or leave it as-is and have `ResyncAllLEDs` supersede it. Gate on `_driveToggleLEDs` / `_driveButtonLEDs` per branch, and it no-ops cleanly when `MidiFighterOutput.Instance` isn't up yet.

## Problem 2 — the on-screen drawer keeps showing old pad modes

**Today:** the drawer reads `_btnRouter.Config` at **build** time (`BuildMf64Section` and `BuildRadial1Section` both do `cfg.GetMode(btn)` when constructing each `PadCell`). A live swap changes behaviour but the drawer keeps rendering the old Toggle-vs-Button styling and old per-pad colours until a rebuild. Cosmetic, but wrong on screen.

**Fix — `RefreshPadModes()` on the drawer, no rebuild.** A rebuild would reset every widget's "seen" opacity and (in radial) re-run all the geometry, so this must restyle in place, exactly like `ApplyTheme` / `ApplyUiOpacity`. Walk each view's `pads[]`, and for each cell:

- re-set `CellMode` from the new config's `GetMode`,
- re-set `FillColor` (through `Palette.AdaptLed` + `_padRawFill` for the mirrored LED colour, as the build sites do).

Works for both layouts unchanged, since it iterates `pads[]` by `linearIndex` and touches no geometry.

## Problem 3 — toggle states on a switch

**Today:** `_toggleStates` (a `Dictionary<int,bool>` keyed by note) is **not** cleared on a `Config` swap. `SetToggle(note, value, fireEvent)` exists to set one explicitly.

**Decision needed — see open decisions.** The recommendation is **states persist per note**: switch to B and back to A and A's pads are where you left them. `ResyncAllLEDs` then just reflects `mode-in-current-config × stored state`, which is exactly "the LEDs reflect the current state" the requirement asks for. The alternative (independent state per preset) is a bigger change — per-preset state storage — and is probably not what a performer wants, but it's the one thing to confirm.

---

## Proposed architecture

**Put the reconcile in the `Config` setter, not in the preset component.** Any code that assigns `Config` — the preset switcher, a user script, a future feature — should get correct LEDs and a correct drawer for free. So:

```
MidiFighterButtonRouter.Config setter:
    _config = value;
    if (playing) { ResyncAllLEDs(); OnConfigChanged?.Invoke(); }
```

- **`OnConfigChanged`** — an instance `Action` on the router (config is per-router, so not one of the package's static events). The drawer subscribes in `OnEnable` / unsubscribes in `OnDisable` (mirroring its existing `+=`/`-=` discipline) and calls `RefreshPadModes()`.
- Guard the setter's side effects on `Application.isPlaying` and a real value change, so edit-time / `OnValidate` assignment stays inert — matching how the router's `OnValidate` already gates its LED preview.

**The preset component is then thin.** New `MidiFighterPadPresets` (Runtime), holds `MidiFighter64ButtonConfig[] _presets` and drives selection:

- `SelectPreset(int)`, `NextPreset()`, `PreviousPreset()` → set `_router.Config = _presets[i]`. Everything else cascades from the setter.
- Optional: trigger presets from MF64 pads or MIDI Mix buttons (subscribe to a router event), or expose the methods for the user's own binding. Keep the *binding* out of scope v1 — ship the switching API and let the performer wire the trigger, or add a simple keyboard/`InputAction` hook behind a serialized field.
- Add it to the `MIDI Controller` prefab (disabled or empty preset list by default, so it's inert until configured), and regenerate the prefab.

This keeps each piece at its natural home: the router owns config + LED reconcile, the drawer owns its own refresh, the preset component owns only *which* config and *when*.

---

## Open decisions

1. **Toggle-state policy on switch** — persist per note (recommended; "back to A leaves A as it was") vs independent state per preset (needs per-preset state storage, more surprising). Confirm before coding Problem 3.
2. **Preset trigger** — ship only the API (`SelectPreset` / `Next` / `Previous`) and let the user bind it, or include a built-in binding (a keyboard key, an `InputAction`, or a designated MF64/MIDI-Mix note)? Recommend API-only for v1, binding as a fast follow.
3. **`OnConfigChanged` shape** — instance `Action` (recommended, config is per-router) vs a static event like the rest of the package (consistent but semantically odd for per-instance state).

---

## Implementation order

1. **`ResyncAllLEDs()`** on `MidiFighterButtonRouter` (Problem 1). Verify a manual `router.Config = other` in play mode lights the hardware correctly, including formerly-lit toggle pads that became buttons going dark.
2. **`Config` setter side effects** — resync + `OnConfigChanged`, guarded on `Application.isPlaying` and a real change.
3. **`RefreshPadModes()`** on `MidiStatusDrawer`, subscribed to `OnConfigChanged` (Problem 2). Verify the drawer restyles in place with no rebuild — "seen" opacity and radial geometry survive.
4. **Toggle-state policy** (Problem 3) — with "persist" this is likely zero code beyond confirming `ResyncAllLEDs` reads stored state; with "per-preset" it's the state-storage work.
5. **`MidiFighterPadPresets`** component + prefab slot + regenerate the prefab.
6. Docs: `CLAUDE.md` (a "switch pad arrangements live" task), `DEVNOTES.md` (the setter-side-effect + resync reasoning), `CHANGELOG.md`, `package.json` **minor** bump, tag.

## Scope

- `ResyncAllLEDs` ~20 lines; setter side effects + `OnConfigChanged` ~15; `RefreshPadModes` + subscription ~30; `MidiFighterPadPresets` ~50. No new packages. Reuses `DriveToggleLED` / `DriveButtonLED`, `PadCell.CellMode`, `Palette.AdaptLed`, and the existing config resolver.
