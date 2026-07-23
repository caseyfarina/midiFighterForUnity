# Changelog

## [2.2.1] — 2026-07-23

### Added
- **`Pad Rotation` slider** *(radial only)* — rotates the MF64 pad rings about the centre, -180 to 180, default -45 (counter-clockwise). Pads only: the knob, fader and toggle bands stay keyed to their channel angles, so rotating them would slide the captions off their bands and break the channel-1-at-top mapping.

## [2.2.0] — 2026-07-22

### Added
- **Radial 1 drawer layout.** `MidiStatusDrawer.Layout` selects between `Linear1` (the existing pad-grid-over-strips arrangement) and `Radial1`, which arranges everything as concentric rings from the centre out: the 64 MF64 pads as four rings, three knob arcs and a fader arc per channel inside each channel's 45° slice, a full-circumference master ring, and an outer ring of Mute / Rec-Arm pairs. Channel 1 sits at the top and channels advance clockwise. **F4** cycles layouts at runtime. Geometry is measured from `radial_A_centered.svg`; the palette is unchanged, since band separation is carried by radius and stroke weight rather than hue.
- **`RadialArc`** — a Painter2D element drawing a faint full-sweep track with a value fill growing along it. One primitive covers every continuous mixer control in radial: knob rows, channel faders, and the master ring differ only in radius, sweep and stroke.
- **`Pad Size` and `Ring Spread` sliders** *(radial only)* — pad diameter as a multiple of the reference layout, and a multiplier on the ring radii. They compete for the same space: tightening the rings also shortens each ring's circumference, and ring 0 crowds first because its 28 pads get the least arc each.
- **`Vertical Offset` and `Readout Padding` sliders** *(radial only)* — move the ring stack within the display, and inset the event readout from the display's bottom-left corner.
- **`MidiStatusDrawerEditor`** — groups the drawer's settings and separates the layout-specific ones. Groups that don't apply are shown disabled rather than hidden, since hiding makes settings look like they've been lost when the layout changes.

### Changed
- **The event readout floats at the display's bottom-left in radial layouts** rather than flowing beneath the widgets, and no longer contributes to the height budget. A disc leaves its corners empty, and a full-width strip underneath was also pushing the rings upward. It attaches to the root rather than the drawer, so `ApplyHiddenState` hides it explicitly — the drawer's slide doesn't reach it.
- **Radial channel captions read `1`–`8`** rather than `ch1`–`ch8`.
- The three MIDI Mix handlers now guard the linear widget and the radial arc **independently**. They previously did `if (widget == null) continue`, which in a radial layout — where the linear widget is null by design — would have skipped the arc update entirely and left the arcs frozen.

## [2.1.0] — 2026-07-22

### Added
- **`UI Opacity` slider on `MidiStatusDrawer`** (0–1, default 1, runtime equivalent `UiOpacity`), sitting below Panel Opacity. It fades everything the drawer draws — pads, knobs, faders, mixer buttons, and all type (captions, bank arrows, the event readout) — where Panel Opacity fades only the backing panels and deliberately leaves that content alone. The two together cover the full range: both at 0 hides the drawer, panels at 0 with widgets at 1 leaves controls floating on the scene. It multiplies with the 40 % dimming applied to controls not yet touched by a MIDI event rather than replacing it, so that cue survives at every setting.

### Removed
- **The `Test Scene` sample, and samples entirely.** `package.json` no longer declares a `samples` block. Nothing reachable was lost: the declared path was `Samples~/TestScene` while the folder on disk was `Samples/`, so Package Manager's **Import** button never resolved and no consumer could reach the sample in the first place. It also ends a standing chore — the folder had to carry the `~` for UPM to offer it and *not* carry it for Unity to compile it during development, so the layout needed renaming around every release and in practice never was.
- **`MidiFighterTestScene`, `MidiDebugUI`, `CreateMidiTestScene`, `QuitOnEscape`** — deleted. They existed to assemble a working scene at runtime, which is now the prefab's job, and to visualize MIDI state, which `MidiStatusDrawer` does better.
- **`MidiMixDataVisualizer`** — deleted. It spawned TextMeshPro labels for every mixer control, duplicating what the drawer already shows.
- **`com.unity.ugui` dependency** — dropped, since nothing in the package uses uGUI or TextMeshPro anymore. Runtime dependencies are now just `com.unity.inputsystem`, `jp.keijiro.minis`, and `jp.keijiro.rtmidi`. The drawer is UI Toolkit, which is an engine module and needs no dependency.

### Changed
- **`MidiToggle`, `MidiRotator`, `MidiMixCloner` and `MidiNoteLogger` moved into `Runtime/`** (and `MidiToggleEditor` into `Editor/`), with their namespace changing from `MidiFighter64.Samples` to `MidiFighter64`. They are worked examples of consuming the routers and cost nothing to ship, so they are always compiled and always available rather than gated behind an import that never worked.
- **`MidiToggleEditor` is no longer a global-namespace type.** It was a `public class` with no namespace, which a package should not put into every consumer's global scope; it now sits in `MidiFighter64.Editor` alongside the other inspectors.
- Docs, `ARCHITECTURE.md` diagram, and the package-structure listing updated throughout: the integration test is "drag in the prefab and press Play", not "import the sample". The diagram also gained a `MidiFighterButtonRouter` node, which it had always been missing.

## [2.0.0] — 2026-07-22

The whole package now ships as **one prefab**. Drag `Runtime/MIDI Controller.prefab` into a scene and everything works — no bootstrapper, no runtime assembly, no component whose job is configuring other components.

### Breaking
- **`MidiSceneBootstrapper` and `MidiSceneBootstrapper.EnsureCoreComponents()` are removed.** Scene setup is now "drag in the prefab". The bootstrapper was a pure mirror: every setting it carried already existed as a serialized field on the component that owned it, and it duplicated them only because components spawned at runtime have no serialized state of their own. The prefab removes that reason, and with it the mirrored fields, the four `ApplyX` push methods, the version-stamped migration machinery, and a custom editor that mostly drew other components' fields.
- **`MidiStatusDrawer`, `PadCell`, `KnobDisplay`, `DrawerPlacement` and `DrawerTheme` moved from `MidiFighter64.Samples` to `MidiFighter64`**, and from the Test Scene sample into `Runtime/UI/`. The drawer is part of the package proper now rather than sample code. A prefab shipped in Runtime could not reference a Samples component, so this was a prerequisite for including the visualization in the prefab at all.
- **The inline 8×8 pad grid moved from `MidiSceneBootstrapper` to `MidiFighterButtonRouter`**, the component it actually configures. Existing `MidiFighter64ButtonConfig` assets are unaffected and still win over the inline grid.
- **Per-component configuration.** Device filter on `MidiEventManager`, latch flags on `MidiMixRouter`, pad config and LED colors on `MidiFighterButtonRouter`, drawer settings on `MidiStatusDrawer`. Configure the prefab instance in your scene.

### Added
- **`Runtime/MIDI Controller.prefab`** — all eight components, wired and configured, with the `{ "Fighter", "MIDI Mix" }` device allow list baked in. That last part is load-bearing: `MidiEventManager`'s own default is an empty list meaning "merge every MIDI input port", and the filter previously came from a bootstrapper default. Without it, a monitor or loopback port echoing a controller delivers every message twice and every latch appears dead.
- **`Tools → MidiFighter64 → Regenerate Controller Prefab`** — maintainer tool that rebuilds the shipped prefab deterministically when a component gains a field, so it can't drift from the components it represents.
- **`MidiFighterButtonRouterEditor`** — draws the router's inline pad grid, reusing the existing `MidiFighter64PadGridGUI` shared with the ScriptableObject inspector.

### Changed
- **MIDI Mix Mute and Rec-Arm buttons now latch (press-to-toggle) by default.** `MidiMixRouter.LatchMute` and `LatchRecArm` both default to `true`. Press once to turn on, press again to turn off; the hardware LED stays lit for as long as the button is on, and the status drawer holds the pad filled. Untick either flag for the previous momentary behaviour. `OnSolo` (Mute-while-SOLO) is unchanged and still momentary.
- **Drawer message strip reads `ON` / `OFF` for Mute and Rec-Arm** instead of `DOWN` / `UP`, which only described the momentary case.

### Fixed
- **`DarkGrey` pads read as brown in the drawer.** The LED color table's three neutrals carried small blue biases (`DarkGrey` was `0.25, 0.25, 0.27`), and `Palette` already documents that a channel bias reads as a tint against the semi-transparent panel — a blue-biased dark value comes out brown. The neutrals are now strictly `r == g == b`, and `DarkGrey` was lifted to `0.34` so it clears the dark panel's own lightness; at `0.25` it read as a muddy patch rather than a lit pad, and an ambiguous patch takes its apparent hue from its surround.
- **The status drawer showed the wrong pad colors when the inline grid was in use.** `MidiFighterButtonRouter.Config` returned the raw serialized field, which is null unless a `MidiFighter64ButtonConfig` asset is assigned. The drawer resolves both per-pad LED color and Button/Toggle mode through that property, so it fell back to the router's global colors — the hardware lit correctly while the on-screen mirror did not, which made it look like a drawer bug. `Config` now returns the resolved config, asset or inline.
- **The drawer built every pad as Button mode regardless of configuration.** `OnEnable` called `BuildAllViews()` before locating the button router, so the pad grid was always constructed against a null config. Long-standing — the bootstrapper's `Awake` also ran before the drawer's `OnEnable`, so it was equally broken there; it only became visible once there was per-pad config worth mirroring.
- **Drawer under-filled the display when the MF64 section was hidden.** `DrawerHeight` gated the mix section on `_showMidiMix` but added the pad grid's 600 design units unconditionally, so a mixer-only drawer budgeted ~1082 units for ~452 units of content. `Expand` scales by `min(screenW/refW, screenH/refH)`, so height bound against the phantom units and the mixer rendered at roughly 40% of the display at Screen Fill 1.0. Every term is now conditional on its section, and the standalone message strip (built whenever Mix is hidden) is budgeted too, so all four visibility combinations fill correctly. The Log Layout Report gained a `sections` line listing the budget's terms.
- **Latching buttons appeared dead when a second MIDI port carried a copy of the controller's traffic.** `MidiEventManager` connects to every MIDI input port and merges them into one stream, so a monitor, loopback, or network port echoing the same device delivered every message twice — toggling each latch on and straight back off within a single frame. Momentary consumers couldn't see it (on/on then off/off lands in the same place), which is why it only surfaced once latching became the default.

### Added
- **Fisheye scale slider.** `MidiStatusDrawer.Mf64FisheyeScale` and a **Fisheye Scale** field on `MidiSceneBootstrapper`, 1–6 (default 3 — the previous fixed value). It's the focused row/column's flex-grow weight against the other seven, so the grid stays square at any setting; `1` disables the visible growth without turning the feature off. Assigning it re-applies to a pad that's focused at the time, so the slider is live in play mode.
- **`EnableFunctionKeys`** on `MidiStatusDrawer` and **Enable Function Keys** on `MidiSceneBootstrapper`, on by default. Gates F1–F3 for projects that bind those keys themselves. Backtick is deliberately never gated — it's the show/hide key, and a hidden drawer with no way back is a dead end; F1, being an alias for it, is gated. Every shortcut also has a scripting equivalent.
- **Drawer theme (Dark / Light).** New `DrawerTheme` enum and `MidiStatusDrawer.Theme`, plus a **Theme** field on `MidiSceneBootstrapper` and **F3** to cycle at runtime. Light mode flips to light panels with dark ink so the drawer stays readable over bright scenes. Restyles in place — no rebuild, so widgets keep their "seen" state.
- **Global stroke weight.** `MidiStatusDrawer.StrokeWeight` and a **Stroke Weight** slider on `MidiSceneBootstrapper`, 0.25–4 (default 1). Multiplies `KnobDisplay.StrokeWidth` for every stroked widget — knob bodies and pad rings — via a `StrokeScale` property on `KnobDisplay` and `PadCell`. Radii stay proportional to each element's own size, so only line thickness changes; widgets keep their size, position, and the square pad grid.
- **Drawer panel opacity.** `MidiStatusDrawer.PanelOpacity` and a **Panel Opacity** slider on `MidiSceneBootstrapper` (0–1, default 0.30 — the previous fixed value). Applies to the section panels and message strip only; widget ink is never faded, so the readout stays legible at any opacity, including 0.
- **`MidiStatusDrawer.OnValidate`** applies theme and placement live, so both can be dialed in from the component's Inspector during play mode. Scoped to restyle-only settings — anything that rebuilds would destroy and create GameObjects, which `OnValidate` forbids.
- **Device filtering on `MidiEventManager`** — `AllowedDeviceNames` / `BlockedDeviceNames` (case-insensitive substring match against the port name), `SetDeviceFilter(allowed, blocked)`, and `Reconnect()`. Both lists default to empty, so the runtime component's behaviour is unchanged: every port is still connected unless you narrow it. Skipped ports are logged.
- **Allowed / Blocked Device Names on `MidiSceneBootstrapper`**, applied to the spawned `MidiEventManager`. The allow list defaults to `{ "Fighter", "MIDI Mix" }` — the two controllers this sample is built around — so the test scene is immune to echoing ports out of the box. Clear it to accept every port.
- **Duplicate-delivery warning.** `MidiEventManager` warns once per session when the same note arrives from two different ports in the same frame, naming both. The symptom (a latch that never holds) points nowhere near the cause.
- **`MidiSceneBootstrapper` serialized-default migration.** A new hidden `_serializedVersion` stamp and `MigrateSerializedDefaults()` upgrade instances saved against older defaults. Needed because a bool whose default flips can't be repaired by sniffing its value — a serialized `false` from an older scene is indistinguishable from one the user deliberately unticked — so scenes saved before this release pick up latching instead of silently keeping the old momentary behaviour. Now at version 2: the device allow list needs the same treatment, since an empty array is a legitimate "accept every port" setting.

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

### Changed (this release)
- **Redrawn `KnobDisplay`** to match a hardware synth knob: a ring of 31 uniform dots (previously 12 with radii ramping small→large), a stroked knob body inside the ring, and a filled pointer dot on the body that rotates to the value. The inner value arc is gone — the pointer replaces it. Dots still light up to the current value, so the reading survives at sizes where the pointer is only a few pixels.
- **`PadCell.StrokeColor` and `KnobDisplay.SetInk(on, off, outline)` are now instance state**, not private statics, so the theme can repaint them. The knob body outline and ± marks take the palette's pad-stroke color, so every stroked circle in the drawer matches.

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
