# Changelog

## [1.1.0] - 2026-07-17

### Added
- **MidiFighterButtonRouter**: per-pad Button vs Toggle behaviour, driven by an optional `MidiFighter64ButtonConfig` ScriptableObject asset. Fires `OnButtonPress` / `OnButtonHold` / `OnButtonRelease` / `OnToggle` events.
- **MidiFighter64ButtonConfig**: ScriptableObject with an 8×8 pad-mode grid inspector (Editor script `MidiFighter64ButtonConfigEditor`).
- **MidiFighterLEDColor** enum: 8 hardware-confirmed colors from the 24 Jul 2017 firmware palette (Off, DarkGrey, Grey, White, BrightBlue, DarkBlue, BrightPink, DarkPink).
- **LED feedback in MidiFighterButtonRouter**: automatic toggle-state + button-hold LED mirroring via `MidiFighterOutput`, with three color-picker dropdowns in the Inspector and a live `OnValidate` all-pads preview in Play mode.
- **MidiMixRouter / MidiMixInputMap**: full Akai MIDI Mix integration (knobs, faders, mute/solo/rec-arm buttons, bank shift).
- **MidiFighter64InputMap.ToNote(row, col)**: split-half-aware inverse of `FromNote`.
- **Editor menu** `Tools → MidiFighter64 → Create Test Scene`: one-click generator for `MidiControllersTestScene.unity`.
- **EditMode tests**: 20+ tests covering grid corners, `FromNote`/`ToNote` round-trip, and split-half boundary cases.

### Changed
- **MidiFighterOutput** rewritten to use `jp.keijiro.rtmidi` instead of `winmm.dll` — now cross-platform (Windows / macOS / Linux).
- **MidiFighter64InputMap.FromNote** corrected to the confirmed split-half layout (left half = cols 1–4 / notes 36–67; right half = cols 5–8 / notes 68–99).
- Sample bootstrapper (`MidiSceneBootstrapper`) now also spawns `MidiFighterButtonRouter` and `MidiFighterOutput`.

## [1.0.0] - 2026-03-06

### Added
- MidiEventManager: Minis MIDI device bridge with note on/off events
- MidiFighter64InputMap: Note-to-grid coordinate conversion
- MidiGridRouter: Default 8-row routing with typed events and virtual RouteButton for customization
- UnityMainThreadDispatcher: Thread-safe action dispatch to main thread
- GridButton struct with row, col, linearIndex, noteNumber
