# midiFighterForUnity — Dev Notes

Internal notes for people modifying **this package** (not for consumers). See `CLAUDE.md` for the integration guide.

---

## Repo state

**Branch layout: work directly on `main`.** As of 2026-07-21 this is a single-developer repo and the feature-branch workflow was retired — it added ceremony without review value. Commit to `main`; don't create a feature branch unless there's a specific reason to, and don't assume the usual "branch before committing" default applies here.

Current: **2.1.0**, tagged `v2.1.0`, on `main`.

**Tag whenever you bump `package.json`.** Consumers install a UPM package from a git URL pinned to a tag (`…midiFighterForUnity.git#v1.3.0`); untagged, that URL resolves to whatever `main` happens to be. The repo went its first three versions with no tags at all, which made 1.0.0–1.2.0 unretrievable after the fact.

`v1.2.0` deliberately does not exist: that work stayed uncommitted until 2026-07-21 and shipped inside `v1.3.0`. See the note in `CHANGELOG.md`.

Version history: see `CHANGELOG.md`.

---

## Local development workflow

The package source lives in `Packages/midiFighterForUnity/`. It's a local UPM package — Unity picks it up directly.

### There are no samples (2.1.0)

The `Samples/` folder is gone and `package.json` declares no `samples` block. Everything ships in `Runtime/` and `Editor/`, always compiled, always reachable.

This removed a standing chore rather than a feature. The folder had to be `Samples~/` (tilde-hidden) for UPM to offer it as an importable sample, but `Samples/` (no tilde) for Unity to compile it during development — so the layout had to be renamed back and forth around every release, and in practice it never was: `package.json` pointed at `Samples~/TestScene` while the folder was `Samples/`, which meant Package Manager's **Import** button never resolved and no consumer could reach the sample anyway.

What was there and where it went:

- **`MidiFighterTestScene`, `MidiDebugUI`, `CreateMidiTestScene`, `QuitOnEscape`** — deleted. They existed to *assemble* a working scene at runtime, which is now the prefab's job, and to visualize MIDI state, which the status drawer does better.
- **`MidiToggle` (+ its editor), `MidiRotator`, `MidiMixCloner`, `MidiNoteLogger`** — promoted to `Runtime/` and `Editor/`. They are worked examples of consuming the routers, and cost nothing to ship.
- **`MidiMixDataVisualizer`** — deleted. It spawned TextMeshPro labels for every mixer control, which is what the drawer already does; keeping it meant retaining `com.unity.ugui` purely for one example. That dependency is now dropped, leaving inputsystem + minis + rtmidi.

**Don't reintroduce a `Samples~/` folder** without deciding first which of those two layouts you want to live with year-round. That tension is what made the old one rot.

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

## Why the bootstrapper was removed (2.0.0)

The package used to ship a `MidiSceneBootstrapper` that spawned every component at runtime and pushed config into them at `Awake`. It carried mirrored copies of eleven drawer settings, the device filter, the latch flags, the pad grid and the LED colors, plus a `_serializedVersion` migration system and a custom editor that mostly drew *other components'* fields.

**None of that was a layering mistake. It all followed from one decision:**

> Components spawned at runtime have no serialized state. Their configuration has to live somewhere persistent. The only persistent thing in the scene was the bootstrapper. So config *had* to be mirrored onto it and pushed inward.

Every symptom came out of that root — the duplication, the rebuild-vs-restyle dispatch question in `ApplyDrawerConfig`, two parallel migration systems, and the "both sets are inspector-visible and read as conflicting" confusion. The four-place tax for adding a setting was a consequence, not a cause.

Shipping a prefab removes the root: each component's own serialized fields become the single source of truth, and the bootstrapper had nothing left to do. The tell that this was right — **every setting it mirrored already existed as a serialized field on the component that owned it.** It was a pure mirror. Deleting it was almost entirely subtraction.

Two things worth knowing if this is ever revisited:

- **The device-filter timing hazard disappeared rather than being fixed.** `MidiEventManager` connects to ports in its own `OnEnable`, which had already happened by the time `EnsureCoreComponents` returned — so the bootstrapper had to apply the filter afterwards and call `Reconnect()`. On a prefab the field is present before `OnEnable` runs. `Reconnect()` is still there for runtime filter changes.
- **A prefab does not remove per-component serialization discipline.** A field added later still deserializes to zero on existing prefab instances. The difference is you pay it once per setting instead of twice.

---

## UI Toolkit drawer — hard-won constraints

`CLAUDE.md`'s Layout section states the rules. This is why they exist — each was a real, reproduced failure during the drawer rework.

- **Never set an element's `height` from a `GeometryChangedEvent` guarded on a height mismatch.** If a flex parent can shrink the element, `resolvedStyle.height` never reaches the target, so the style is re-set every layout pass and each set schedules another pass. This **hard-freezes the editor** — no exception, no console output, just a hang. Unity's own aspect-ratio custom control avoids it by adjusting *padding* behind a `0.01f` tolerance and never touching `height`.
- **Percentage sizes need a definite parent.** `width: 100%` inside a shrink-to-fit (`position:absolute` with only `right`/`top`/`bottom`) parent resolves against the full viewport, dragging the panel to the screen edge. Symptom was pads rendering as ellipses, because `PadCell` deliberately draws an ellipse inscribed in its box — a non-round pad means a non-square cell, not a `PadCell` bug.
- **Runtime-created `PanelSettings` default to `ConstantPixelSize`.** Easy to miss because the UI still renders; it just ignores resolution. Anything created via `ScriptableObject.CreateInstance<PanelSettings>()` needs its scale mode set explicitly.
- **`aspect-ratio` is not in USS on Unity 6000.0.** It's in later docs, so search results will suggest it. The package's declared minimum is 6000.0.
- **Don't push *rebuilding* drawer config from `OnValidate`.** A rebuild destroys and creates GameObjects — illegal from `OnValidate` and a route to editor deadlock. `MidiStatusDrawer.OnValidate` exists but is scoped to `ApplyTheme` + `ApplyPlacement`, both of which restyle live elements and touch neither the tree nor any GameObject. Anything you add there must be in that class, or the freeze comes back.
- **New serialized fields need a migration, and there are two kinds.** Field initializers don't re-run for an already-serialized component, so scenes and prefab instances saved before the field existed deserialize it to zero. If zero is outside the field's legal range (`_screenFraction`, `_mf64FisheyeScale`, `_strokeWeight`, and the router's inline pad arrays which arrive at length 0) a value guard is enough. If zero is a *legitimate* setting — any bool, and `_panelOpacity`, where 0 means "no panel" — a guard would clobber a deliberate choice and it needs a version stamp instead. Picking the wrong one is silent. Has caused two separate "why is the drawer tiny" investigations.
- **Every term in `DrawerHeight` must be conditional on the section it measures.** `GridSide` was unconditional while `MixSectionHeight` was gated on `_showMidiMix`, so a mixer-only drawer carried 600 phantom design units — reference ~1082 units tall against ~452 of content, and under `Expand`'s `min()` the whole thing rendered at ~40% of the display at Screen Fill 1.0. The tell is that `Screen Fill` appears broken while being exactly correct: it fills a budget that is mostly empty space. Note the message strip becomes its own panel when Mix is hidden, so all four visibility combinations have content and the budget has to cover each. Fixed 2026-07-21; the Log Layout Report now prints a `sections` line showing the budget's terms.
- **Every drawer color goes through `Palette.For(theme, opacity)`; every stroke through `StrokeWidth * StrokeScale`.** A literal at a build site survives the initial build and then gets skipped by `ApplyTheme`, so it only diverges once the user changes theme — long after the change that caused it.

---

## Duplicate MIDI delivery — read this before debugging any latch

Confirmed on this rig, 2026-07-21. `MidiEventManager` connects to **every** MIDI input port and merges them. The dev machine had a `MidiView` port alongside `MIDI Mix`, both carrying the same stream, so every note arrived twice in one frame — latch on, latch off, LED flashes and dies.

What makes this expensive to diagnose:

- **Momentary mode looks perfect under the same fault.** on/on then off/off lands in the same place. So it presents as "latching is broken", and the latch code is where you'll look. It's fine.
- The router, the LED output, and the drawer all looked correct on inspection, because they were. Reading source cannot find this — only logging raw arrivals can. `[MixDiag]`-style temporary logging in `MidiMixRouter.HandleNoteOn` showing note + frame + device is what caught it.

Mitigations now in place: `AllowedDeviceNames` / `BlockedDeviceNames` on `MidiEventManager`, an allow-list default of `{ "Fighter", "MIDI Mix" }` baked into the shipped **MIDI Controller prefab** (the component's own default is an empty list, i.e. accept every port — so a hand-built rig has no protection until it is filled in), and a once-per-session warning naming both offending ports.

---

## Known issues / future work

- **First MIDI event per channel is often lost.** Minis creates the device on first event and subscribes callbacks *after*, so event #1 slips through. Would need a fix in Minis or a warmup message. Workaround: doc note tells users to press twice on startup.
- **RtMidi cross-platform** — package works on Windows. macOS/Linux paths through RtMidi should work but haven't been hardware-tested.
- **MIDI Mix bank/shift note remapping** — the mixer sends different notes when Bank Left/Right is active. Currently the router just fires the raw event; a "bank-aware" mode could remap them.
- ~~Bundled font has no license file.~~ **Resolved.** Cossette Titre is a Google Font under SIL OFL 1.1 (Copyright 2025 The Cossette Project Authors). The upstream `OFL.txt` now sits beside the font in `Runtime/UI/Resources/`, and `Third Party Notices.md` at the package root records it. No Reserved Font Names, so the font may be renamed or modified. Redistribution inside a larger work is expressly permitted; selling the font on its own is not.
- ~~**"No Theme Style Sheet set to PanelSettings" warning** on every drawer build.~~ **Resolved.** `BuildView` now assigns an empty runtime `ThemeStyleSheet` (`FallbackTheme`) when the author supplies none. The earlier note suggested shipping a `.tss` asset, but a hand-authored theme file has a version-specific import syntax and a GUID to keep stable, none of which is verifiable without Unity; the empty sheet is zero-dependency, works in builds, and is enough because the drawer reads no theme USS variables (it styles everything inline and draws with Painter2D — which is why it rendered correctly *with* the warning). One shared instance, `HideFlags.DontSave`, destroyed in `OnDestroy`.
- **`MixChromeHeight` is the last estimated number in the layout.** `MixSectionHeight` is now derived (`StripHeight` from the widget constants + `MixChromeHeight`), but the chrome — master row, utility row, section padding — depends on label metrics, so the bundled font and type sizes shift it. It only affects how exactly `ScreenFill` is hit; it can never make the pad grid non-square. Correct it from the `mix section h` line of the Log Layout Report.
- ~~Drawer fields are duplicated on `MidiStatusDrawer` and `MidiSceneBootstrapper`.~~ **Resolved in 2.0.0** by deleting the bootstrapper — see "Why the bootstrapper was removed" above. A new drawer setting now costs one field and one property on the drawer itself.

---

## Assets/ folder note

The consuming Unity project this package lives in (`F:\Unity Projects 2026\midiControllerPackage\`) has `Assets/` as a bare Unity URP template. All package code lives under `Packages/midiFighterForUnity/`. The dev scene under `Assets/Scenes/` is hand-made: an empty scene with the MIDI Controller prefab dragged in.

---

## Files that must stay in sync

- **`CHANGELOG.md`** — bump alongside `package.json` version
- **`package.json`** — `version`, `description`, `dependencies`
- **`README.md`** — outward-facing quick-start (kept minimal; deep docs live in `Documentation~/index.html`)
- **`Documentation~/index.html`** — the "View documentation" link target in Package Manager
- **`CLAUDE.md`** — integration guide for Claude Code
- **`DEVNOTES.md`** — this file
