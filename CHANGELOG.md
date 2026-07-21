# Changelog

## [1.3.0] - 2026-07-21

### Added
- **Status Drawer section on `MidiSceneBootstrapper`** — the drawer is now configurable without selecting the spawned component:
  - **Spawn Status Drawer** — skip creating the overlay entirely. `EnsureCoreComponents` gained an optional `includeStatusDrawer` parameter (defaults true, so existing callers are unaffected).
  - **Placement** — new `DrawerPlacement` enum: `RightCentered` (pinned right, centered vertically) or `ScreenCentered` (centered both axes). Vertical centering now holds at any aspect ratio.
  - **Screen Fill** — fraction of the display the drawer occupies on whichever axis binds first (height on landscape, width on portrait). Default 0.90.
  - **Show Midi Fighter 64 / Show MIDI Mix** — run a single controller without a dead panel. With the mix section hidden the message strip is rebuilt as its own panel, and the pad grid grows to reclaim the freed height.
  - **Enable MF64 Fisheye** — was only reachable on `MidiStatusDrawer`.
  - **Drawer Font** — optional typeface override.
- **`Third Party Notices.md`** at the package root, plus the upstream `OFL.txt` beside the font. Cossette Titre is SIL OFL 1.1 (Copyright 2025 The Cossette Project Authors); the package's own MIT licence does not cover it.
- **Bundled font** — `CossetteTitre-Regular.ttf` ships in `Samples/TestScene/UI/Resources/`, loaded via `Resources.Load<Font>(MidiStatusDrawer.BundledFontResourceName)`. Falls back to a dynamic OS font if the Resources folder is stripped.
- **`MidiStatusDrawer` public API** — `Placement`, `ScreenFraction`, `ShowMf64`, `ShowMidiMix`, `SetVisibleSections(mf64, mix)`, `FontOverride`, `EnableMf64Fisheye`, `LogLayoutDiagnostics`.
- **Log Layout Report** (bootstrapper, off by default) — dumps one resolved-geometry report to the console after the drawer is first shown: screen size, derived reference resolution, drawer/grid/cell dimensions, screen coverage, mixer-vs-grid column widths, measured mix section height. Reads `resolvedStyle` only, so it cannot affect layout.

### Changed (drawer visuals)
- **Knob diameter now tracks the MF64 pad cell** (~64 design units against a 73 cell, up from a fixed 28) so the mixer and pad grid read as one instrument. Derived from `GridSideDesign`, so it stays proportional.
- **Type sizes doubled** — message strip 10 → 20, captions (MASTER, SOLO, ◄ ►) 7 → 14. They were authored against a much smaller drawer.
- **`MixSectionHeight` is now derived** from the mixer's widget constants rather than hand-summed. Resizing a mixer widget corrects the drawer's height budget automatically; previously it left the budget stale and skewed Screen Fill.

### Fixed
- **Editor hard-freeze when the drawer was visible.** The pad grid's square-lock guarded on a height mismatch that a shrinking flex parent could never satisfy, so it re-set the style on every layout pass and each set scheduled another. The square is now derived arithmetically with no `GeometryChangedEvent`.
- **Drawer stretched to the full screen width**, rendering pads as ellipses. `width: 100%` on the grid resolved against a shrink-to-fit parent; sizes are now definite.
- **Drawer ignored resolution and overflowed small Game views.** Runtime-created `PanelSettings` defaulted to `ConstantPixelSize`; now `ScaleWithScreenSize` + `Expand` with a derived reference resolution.
- **Portrait displays badly under-filled.** The reference resolution was screen-shaped (16:9), so width always bound. It is now derived from the drawer's own aspect, making the binding axis land on `Screen Fill` at any orientation.
- **MIDI Mix columns didn't line up with the MF64 pad columns.** Strips used a fixed width with `SpaceBetween`; they now share the pad cells' flex and margin values, and the section's `minWidth` was removed.
- **Drawer stayed visible when toggled off under `ScreenCentered`.** The hide slide is a percentage of the drawer's own width, which only clears the viewport when flush right; hiding now also sets `display: none`.
- **`MidiStatusDrawer` cleanup** — the fisheye focus timer is now paused and cleared in `TearDownAllViews` rather than only on rebuild, so disabling the component no longer leaves a callback scheduled against destroyed elements.
- **Duplicated section headings** in the bootstrapper Inspector — the custom editor drew manual labels alongside the fields' `[Header]` attributes.

### Changed
- **F2 now cycles `DrawerPlacement`** instead of resizing the drawer between 100 % and 65 % display height. The portrait/landscape compact mode is removed; the drawer is vertically centered in both placements at any aspect ratio.

## [1.2.0] - 2026-07-20

> **Never released.** This work stayed uncommitted until 2026-07-21, when it was
> committed together with the 1.3.0 changes. There is no commit or tag that
> represents 1.2.0 on its own — it reached users as part of `v1.3.0`. The section
> is kept because it documents a coherent batch of features.

### Added
- **MidiStatusDrawer** (Test Scene sample): screen-space UI Toolkit overlay mirroring both controllers in real time. Bottom-right anchored, transparent background, section panels only.
  - **Hotkeys:** `` ` `` / F1 = show/hide, F2 = resize (100 % ↔ 65 % display height).
  - **Multi-display:** automatically renders on every active display via `Display.displays[i].active`. One `UIDocument` + `PanelSettings` per display, state kept in sync across views.
  - **MF64 section:** 8×8 pad grid that stretches to a square filling the drawer width. Each pad shows an outer stroked circle; Toggle pads fill a 2/3-radius inner circle when on, Button pads fill a 1/5-radius inner circle while held.
  - **MIDI Mix section:** 8 vertically-aligned channel strips (3 knobs + mute + rec-arm + fader per channel), horizontal master fader spanning the full section width, utility row with SOLO on the left, a live message display in the middle, and Bank ◄/► on the right.
  - **KnobDisplay:** Painter2D-drawn — 12 tick dots on a 270° arc with radii ramping small→large, coloured white below the current value and dark grey above, plus a white value arc drawn just inside the tick ring.
  - **Message display:** left-aligned, uppercased, updates on every MIDI event (e.g. `MIX FADER CH7  0.66`, `MF64 R3C5 PRESS  V=0.87`).
  - **"Unknown" state:** widgets render at 40 % opacity until the first event on that control arrives, then snap to full — distinguishes untouched from zero.
  - **SOLO behaviour:** the SOLO modifier lights while held; the Mute row is frozen (last-known values) during SOLO holds.
- **Per-pad LED colors** on `MidiFighter64ButtonConfig` — new `MidiFighterLEDColor[64]` grid alongside the mode grid. `Off` sentinel means "use the router's global color for this mode".
- **Embedded 8×8 config grid on `MidiSceneBootstrapper`** — new inline mode + LED-color arrays with a custom Inspector so pad configuration works without creating a separate ScriptableObject asset. A ScriptableObject assigned to the Pad Config Asset slot still wins if you prefer that workflow. Bootstrapper's Inspector also exposes global LED colors (toggle-on / toggle-off / button-down).
- **`MidiFighter64PadGridGUI`** — shared IMGUI helper drawing the pad grid; used by both `MidiFighter64ButtonConfigEditor` and `MidiSceneBootstrapperEditor`.
- **`MidiFighterOutput.IsReady`** — true once the MIDI output port is open. Used by `MidiFighterButtonRouter` to defer the initial LED push until writes will actually reach the hardware.
- **`MidiFighterButtonRouter.Config` / `ToggleOnColor` / `ToggleOffColor` / `ButtonDownColor`** — public get/set properties so the bootstrapper can apply Inspector-configured values to the spawned router at runtime.
- **`MidiSceneBootstrapper.EnsureCoreComponents(Transform parent)`** — optional parent parameter parents spawned MIDI components under the bootstrapper's transform for a clean hierarchy.

- **Latching Mute / Rec-Arm buttons on the MIDI Mix.** New `MidiMixRouter.LatchMute` / `LatchRecArm` bools (also exposed on `MidiSceneBootstrapper`). When on, the corresponding buttons toggle on press (note-off ignored); `OnMute` / `OnRecArm` fire with the new latched state. `MidiMixOutput` now subscribes to router events instead of raw MIDI note-ons so LEDs follow the router's semantics — momentary or latched — automatically.

### Fixed
- **Toggle-mode pads no longer stay dark at launch.** `MidiFighterButtonRouter.PushToggleLEDs` now pushes an initial color for every pad set to Toggle mode in the config (previously only touched pads received their state). The push is retried in `Update` until `MidiFighterOutput.IsReady` returns true, fixing a race where `Router.OnEnable` ran before `MidiFighterOutput.Start` had opened the port.

### Changed
- **`package.json`** now declares `com.unity.ugui 2.0.0` — required for the drawer's UI Toolkit runtime. It is Unity-shipped and already present in most Unity 6 projects.

## [1.1.0] - 2026-07-20

### Added
- **MidiFighterButtonRouter**: per-pad Button vs Toggle behaviour, driven by an optional `MidiFighter64ButtonConfig` ScriptableObject asset. Fires `OnButtonPress` / `OnButtonHold` / `OnButtonRelease` / `OnToggle` events.
- **MidiFighter64ButtonConfig**: ScriptableObject with an 8×8 pad-mode grid inspector (Editor script `MidiFighter64ButtonConfigEditor`).
- **MidiFighterLEDColor** enum: 8 hardware-confirmed colors from the 24 Jul 2017 firmware palette (Off, DarkGrey, Grey, White, BrightBlue, DarkBlue, BrightPink, DarkPink).
- **LED feedback in MidiFighterButtonRouter**: automatic toggle-state + button-hold LED mirroring via `MidiFighterOutput`, with three color-picker dropdowns in the Inspector and a live `OnValidate` all-pads preview in Play mode.
- **MidiMixRouter / MidiMixInputMap**: full Akai MIDI Mix integration (knobs, faders, mute/solo/rec-arm buttons, bank shift, SOLO modifier).
- **MidiMixOutput**: echoes button presses back to light the MIDI Mix's Mute (amber) and Rec-Arm (red) LEDs. Auto-mirrors on press/release by default; manual control via `SetMuteLED` / `SetRecArmLED` / `SetSoloLED` for toggle-style behavior. The hardware does not self-illuminate — this component is required for any LED feedback on the mixer.
- **MidiMixRouter.OnSoloModifier(bool)** event and **`IsSoloHeld`** static property — the SOLO button emits note 27 on press/release and is now handled explicitly. While held, Mute presses fire `OnSolo` instead of `OnMute`.
- **MidiFighter64InputMap.ToNote(row, col)**: split-half-aware inverse of `FromNote`.
- **Editor menu** `Tools → MidiFighter64 → Create Test Scene`: one-click generator for `MidiControllersTestScene.unity`.
- **EditMode tests**: 40+ tests covering MF64 corners, `FromNote`/`ToNote` round-trip, split-half boundary cases, and every documented MIDImix CC and note number.

### Changed
- **MidiFighterOutput** rewritten to use `jp.keijiro.rtmidi` instead of `winmm.dll` — now cross-platform (Windows / macOS / Linux).
- **MidiFighter64InputMap.FromNote** corrected to the confirmed split-half layout (left half = cols 1–4 / notes 36–67; right half = cols 5–8 / notes 68–99).
- Sample bootstrapper (`MidiSceneBootstrapper`) now also spawns `MidiFighterButtonRouter` and `MidiFighterOutput`.

### Fixed
- **MIDImix master fader CC corrected from 127 → 62.** The previous value did not match hardware and master fader events were never firing.
- **Removed spurious `OnRecArmShifted` / `RecArmShiftedNotes` / `MidiMixButton.RecArmShifted`** — the hardware does not shift Rec Arm notes on bank change. These were fictional.

## [1.0.0] - 2026-03-06

### Added
- MidiEventManager: Minis MIDI device bridge with note on/off events
- MidiFighter64InputMap: Note-to-grid coordinate conversion
- MidiGridRouter: Default 8-row routing with typed events and virtual RouteButton for customization
- UnityMainThreadDispatcher: Thread-safe action dispatch to main thread
- GridButton struct with row, col, linearIndex, noteNumber
