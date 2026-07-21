# midiFighterForUnity — Dev Notes

Internal notes for people modifying **this package** (not for consumers). See `CLAUDE.md` for the integration guide.

---

## Repo state

**Branch layout:** `main` = shipped, feature branches merged with tagged releases.

Current release candidate: **1.1.0** on `feat/midimix-and-button-modes`. Merges to `main` and tags `v1.1.0` when ready.

Version history: see `CHANGELOG.md`.

---

## Local development workflow

The package source lives in `Packages/midiFighterForUnity/`. It's a local UPM package — Unity picks it up directly.

### Working on sample scripts

The `Samples~/TestScene/` folder is hidden from Unity by the `~` suffix (UPM convention). To iterate on sample scripts, temporarily **rename `Samples~/` → `Samples/`** so Unity compiles them in place, then rename back before committing a release.

If you keep the tilde and edit source, changes won't be picked up until the user re-imports the sample via Package Manager — which is fine for consumers but slow for development.

**Deliberate current state: the folder stays `Samples/` (no tilde).** This repo has one user, who works in the editor, so the sample is kept always-compiled rather than being renamed back and forth. Don't "fix" it.

`package.json` still declares the sample path as `Samples~/TestScene`. That mismatch is knowingly tolerated — its only effect is that Package Manager's **Import** button for the Test Scene sample won't resolve, which nobody here uses. Everything else (compilation, `Resources.Load` for the bundled font, the Tools menu scene generator) works from `Samples/`.

**Before publishing to any consumer**, restore the UPM layout:
1. Rename `Samples/` → `Samples~/`.
2. Confirm `package.json`'s `samples[0].path` reads `Samples~/TestScene`.
3. Update the paths in `Third Party Notices.md`, which currently point at `Samples/`.

---

## Testing

EditMode tests live in `Tests/Editor/MidiFighter64InputMapTests.cs`. Run via **Window → General → Test Runner → EditMode → Run All**.

Current test count: 22 (14 corners + 8 `ToNote`/round-trip).

Adding tests: any new public method on a static/pure class should have a corner case + a round-trip test.

---

## LED palette calibration

The `MidiFighterLEDColor` enum only names velocities we personally verified on hardware. Firmware **24 Jul 2017** is the target. Older firmware (e.g. 20 Jun 2017 or the initial 01 Jun 2017 release) uses a completely different palette — do not try to make one enum cover both.

To add new colors: use the `OnValidate` preview in `MidiFighterButtonRouter` (drag the Toggle On Color slider in Play mode → all 64 pads take that velocity → identify color visually) and add to the enum.

DJ TechTools firmware history: https://techtools.zendesk.com/hc/en-us/articles/115003584986

---

## Verifying changes (no Coplay in this project)

The `coplay-mcp` tools are listed but the Unity-side package **is not installed here** — every call fails with "Unity Editor is not running at the specified project root", even though `list_unity_project_roots` reports the project as open. Don't chase it.

To check whether a change compiled, read Unity's editor log:

```
C:\Users\casey\AppData\Local\Unity\Editor\Editor.log
```

`grep "error CS"` for compile failures; the tail carries runtime `Debug.Log` output and stack traces from the last play session, which is enough to trace a bad code path. The log reflects Unity's **last** compile, so trigger a recompile (tab into the editor) before trusting it.

---

## UI Toolkit drawer — hard-won constraints

`CLAUDE.md`'s Layout section states the rules. This is why they exist — each was a real, reproduced failure during the drawer rework.

- **Never set an element's `height` from a `GeometryChangedEvent` guarded on a height mismatch.** If a flex parent can shrink the element, `resolvedStyle.height` never reaches the target, so the style is re-set every layout pass and each set schedules another pass. This **hard-freezes the editor** — no exception, no console output, just a hang. Unity's own aspect-ratio custom control avoids it by adjusting *padding* behind a `0.01f` tolerance and never touching `height`.
- **Percentage sizes need a definite parent.** `width: 100%` inside a shrink-to-fit (`position:absolute` with only `right`/`top`/`bottom`) parent resolves against the full viewport, dragging the panel to the screen edge. Symptom was pads rendering as ellipses, because `PadCell` deliberately draws an ellipse inscribed in its box — a non-round pad means a non-square cell, not a `PadCell` bug.
- **Runtime-created `PanelSettings` default to `ConstantPixelSize`.** Easy to miss because the UI still renders; it just ignores resolution. Anything created via `ScriptableObject.CreateInstance<PanelSettings>()` needs its scale mode set explicitly.
- **`aspect-ratio` is not in USS on Unity 6000.0.** It's in later docs, so search results will suggest it. The package's declared minimum is 6000.0.
- **Don't push drawer config from `OnValidate`.** Some setters rebuild, and rebuilding destroys and creates GameObjects — illegal from `OnValidate` and a route to editor deadlock. `MidiSceneBootstrapper.OnValidate` deliberately only normalizes fields.
- **New serialized fields on `MidiSceneBootstrapper` need a `NormalizeInlineArrays` guard.** Field initializers don't re-run for already-serialized components, so scenes saved before the field existed deserialize it to zero. Has caused two separate "why is the drawer tiny" investigations.

---

## Known issues / future work

- **First MIDI event per channel is often lost.** Minis creates the device on first event and subscribes callbacks *after*, so event #1 slips through. Would need a fix in Minis or a warmup message. Workaround: doc note tells users to press twice on startup.
- **RtMidi cross-platform** — package works on Windows. macOS/Linux paths through RtMidi should work but haven't been hardware-tested.
- **MIDI Mix bank/shift note remapping** — the mixer sends different notes when Bank Left/Right is active. Currently the router just fires the raw event; a "bank-aware" mode could remap them.
- **Consumer-facing MidiSceneBootstrapper** — currently in `Samples~/`. Consider moving `MidiSceneBootstrapper.EnsureCoreComponents` (or a subset) to Runtime so consumers can bootstrap without importing the sample.
- ~~Bundled font has no license file.~~ **Resolved.** Cossette Titre is a Google Font under SIL OFL 1.1 (Copyright 2025 The Cossette Project Authors). The upstream `OFL.txt` now sits beside the font in `Samples/TestScene/UI/Resources/`, and `Third Party Notices.md` at the package root records it. No Reserved Font Names, so the font may be renamed or modified. Redistribution inside a larger work is expressly permitted; selling the font on its own is not.
- **"No Theme Style Sheet set to PanelSettings" warning** on every drawer build. Accurate — `BuildView` only assigns `themeStyleSheet` when one is supplied. Harmless here because every element is styled explicitly, but it's console noise. Fix: ship a `.tss` theme asset in the sample's `Resources` and load it alongside the font.
- **`MixChromeHeight` is the last estimated number in the layout.** `MixSectionHeight` is now derived (`StripHeight` from the widget constants + `MixChromeHeight`), but the chrome — master row, utility row, section padding — depends on label metrics, so the bundled font and type sizes shift it. It only affects how exactly `ScreenFill` is hit; it can never make the pad grid non-square. Correct it from the `mix section h` line of the Log Layout Report.
- **Drawer fields are duplicated** on `MidiStatusDrawer` and `MidiSceneBootstrapper` (`Placement`, `ScreenFraction`, `ShowMf64`, `ShowMidiMix`, fisheye, font). The bootstrapper wins at `Awake`, but both sets are inspector-visible and read as conflicting in edit mode. `[HideInInspector]` on the drawer's copies would settle it.
- **Fisheye proportions were tuned against a stretched grid.** `FocusScale = 3f` predates the square fix; it may read stronger than intended now.

---

## Assets/ folder note

The consuming Unity project this package lives in (`F:\Unity Projects 2026\midiControllerPackage\`) has `Assets/` as a bare Unity URP template. All package code lives under `Packages/midiFighterForUnity/`. The test scene generated by the menu command lands in `Assets/`.

---

## Files that must stay in sync

- **`CHANGELOG.md`** — bump alongside `package.json` version
- **`package.json`** — `version`, `description`, sample descriptions
- **`README.md`** — outward-facing quick-start (kept minimal; deep docs live in `Documentation~/index.html`)
- **`Documentation~/index.html`** — the "View documentation" link target in Package Manager
- **`CLAUDE.md`** — integration guide for Claude Code
- **`DEVNOTES.md`** — this file
