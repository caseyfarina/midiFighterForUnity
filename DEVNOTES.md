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

EditMode tests live in `Tests/Editor/`: `MidiFighter64InputMapTests.cs` (grid note ↔ row/col) and `MidiMixInputMapTests.cs` (MIDI Mix CC/note maps). Run via **Window → General → Test Runner → EditMode → Run All**, or headlessly through the Unity CLI (see *Verifying changes* below).

Current test count: 50, across both suites.

Adding tests: any new public method on a static/pure class should have a corner case + a round-trip test.

---

## LED palette calibration

The `MidiFighterLEDColor` enum only names velocities we personally verified on hardware. Firmware **24 Jul 2017** is the target. Older firmware (e.g. 20 Jun 2017 or the initial 01 Jun 2017 release) uses a completely different palette — do not try to make one enum cover both.

To add new colors: use the `OnValidate` preview in `MidiFighterButtonRouter` (drag the Toggle On Color slider in Play mode → all 64 pads take that velocity → identify color visually) and add to the enum.

DJ TechTools firmware history: https://techtools.zendesk.com/hc/en-us/articles/115003584986

---

## Verifying changes (Unity CLI)

Verification goes through the **Unity CLI** (`unity`, global — works in any Unity project on this machine) driving the **already-open editor** via the **Pipeline package** (`com.unity.pipeline`, in this project's manifest). This replaced the old Coplay MCP / Editor.log-grep approach.

Run everything from the project root:

```bash
# compile check — a CS error lands in .errors[], no log grepping
unity command recompile
unity command recompile_status          # { failed, errors[] }

# tests (50 EditMode)
unity command list_tests
unity command run_tests
unity command test_status

# read the console directly
unity command get_console_logs

# confirm the editor is connected
unity status                            # state: ready
unity pipeline list                     # Server Reachable: true
```

Gotchas:

- **Don't use batch `unity test` / `unity build` while the editor is open** — they spawn their own editor and abort on the project lock (`another Unity instance is running`). The `unity command …` path talks to the warm editor instead, so no lock fight.
- After `unity pipeline install`, the editor's HTTP server (port ~7800) only starts once the editor **regains focus** and imports the package (Unity defers import while unfocused). Click into Unity once; `Server Reachable` flips to true.
- `unity list` / `unity command` with no args enumerate every command the live editor exposes; `unity --help` lists the CLI's own commands (far more than the published web reference).
- Unity also ships an official editor MCP (`unity mcp configure claude`) — a fuller Coplay replacement — but this project stays pure-CLI by choice.

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

## The MIDI hot-plug crash (native, upstream, unfixed)

Filed from `pincushionedReloaded`, Unity 6000.3.7f1, Windows, D3D11, package v2.3.0.
Reproduced twice reliably on a machine with several virtual MIDI drivers installed.

**This is not a bug in this package and cannot be fixed here.** The user-facing writeup
is in `README.md` ("MIDI hot-plug hazard"); this section is the engineering detail.

### Signature

```
Begin MonoManager ReloadAssembly
MidiInWinMM::openPort: error closing Windows MM MIDI input port (midiInUnprepareHeader).
terminate called after throwing an instance of 'rt::midi::RtMidiError'
  what():  MidiInWinMM::openPort: error closing Windows MM MIDI input port (midiInUnprepareHeader).
RtMidi.MidiIn:FreeDeviceHandle (intptr) (at ./Library/PackageCache/jp.keijiro.rtmidi@.../Runtime/MidiIn.cs:32)
```

While hung: **100% of one core, memory flat, `Responding=False`, no recovery after 60s.**
Flat memory plus a pegged core means an abort loop, not slow work — don't wait it out.

### Second capture: the runtime path, reproduced here (2026-09-04)

Reproduced locally on `midiControllerProject`, Unity 6000.3.7f1, with an MF64 + MIDI Mix
connected, by unplugging and replugging the MF64 **during play mode**. This capture is
more useful than the one above, because it is **not** on a domain reload:

```
MidiInWinMM::openPort: error closing Windows MM MIDI input port (midiInUnprepareHeader).
terminate called after throwing an instance of 'rt::midi::RtMidiError'
  what():  MidiInWinMM::openPort: error closing Windows MM MIDI input port (midiInUnprepareHeader).

RtMidi.MidiIn:FreeDeviceHandle (intptr)   (at .../jp.keijiro.rtmidi@aa0dd76dac7e/Runtime/MidiIn.cs:32)
RtMidi.MidiBase:ReleaseHandle ()          (at .../jp.keijiro.rtmidi@aa0dd76dac7e/Runtime/MidiBase.cs:20)
System.Runtime.InteropServices.SafeHandle:DangerousReleaseInternal (bool)
System.Runtime.InteropServices.SafeHandle:InternalDispose ()
System.Runtime.InteropServices.SafeHandle:Dispose ()
Minis.MidiPort:Dispose ()                 (at .../jp.keijiro.minis@755ff0ba722e/Runtime/Internal/MidiPort.cs:86)
Minis.MidiDriver:CloseAllPorts ()         (at .../jp.keijiro.minis@755ff0ba722e/Runtime/Internal/MidiDriver.cs:24)
Minis.MidiDriver:Update ()                (at .../jp.keijiro.minis@755ff0ba722e/Runtime/Internal/MidiDriver.cs:39)
Minis.MidiSystemWrangler:<.cctor>b__3_0 ()
UnityEditor.EditorApplication:Internal_CallUpdateFunctions ()
```

**Why this one matters:** the entry point is `MidiDriver.Update()` line 39 — the
port-count-mismatch branch — not `ReloadAssembly`. That is the method that runs **every
frame, in players as well as the editor**. The domain-reload capture left room to argue
this was an editor teardown problem; this stack closes that off. It is the same abort,
reached from the frame loop.

It also walks the full predicted chain link by link, exactly as analysed below:
`Update()` (39) → `CloseAllPorts()` (24) → `MidiPort.Dispose()` (86) →
`SafeHandle.ReleaseHandle` → `FreeDeviceHandle` (32) → `terminate()`.

**This package was not on the stack.** The last frame from our code is a normal
`MidiEventManager.ConnectAllDevices()` log line that had already returned; the abort
happens inside Minis' `Update`, after our code is done. Confirmation that no change here
could have intervened.

Process signature while hung, measured: **101% of one core sustained, working set flat at
3855 MB across a 6 s sample (0.0 MB delta), `Responding=False`.** Force-kill is the only
exit. Because the process is killed rather than shut down, `MidiFighterOutput.OnDestroy`
never runs, so `ClearOnStart` never sends its blanking burst and the MF64 is left showing
its Utility default state — a useful tell that you hit *this* failure and not a hang.

### Root cause chain

1. **Minis opens every MIDI port on the system.** `Runtime/Internal/MidiDriver.cs:18`
   loops `for (var i = 0; i < _probe.PortCount; i++)` unconditionally;
   `Runtime/Internal/MidiPort.cs:78` calls `_rtmidi.OpenPort(portNumber)`.
2. **rtmidi frees those handles through an unguarded P/Invoke.**
   `Runtime/MidiIn.cs:32`: `protected override void FreeDeviceHandle(IntPtr ptr) => _InFree(ptr);`
   When the native side throws `RtMidiError` — which WinMM does when a port can't be
   closed cleanly — the C++ exception escapes RtMidi's C API wrapper, the runtime aborts
   during unwinding, and the process dies.
3. **This package sits downstream of both** and never touches those handles.

### Not editor-only

`Minis.MidiDriver.Update()` runs every frame in players too, and responds to *any* port
count mismatch by calling `CloseAllPorts()` then `OpenAllAvailablePorts()`. So every open
handle is disposed on any count change, in either direction, from any source — including
another application creating or destroying a virtual port. For a live-performance tool
this is the most important fact about the bug.

### RESOLVED (project-locally): abandon the handle instead of freeing it

**The crash is avoidable in pure C#**, which contradicts the original analysis below and
this file's earlier claim that only a native fork could fix it. Both crash paths now
survive on hardware.

The insight: you cannot *catch* the C++ throw, but nothing forces you to *call* the
function that throws. `SafeHandle.SetHandleAsInvalid()` marks a handle unused and
suppresses finalization, so `ReleaseHandle` — and therefore `rtmidi_in_free` — never
runs. `ReleaseHandle` is also what unregisters the two native callbacks, so those must be
cancelled explicitly first.

Applied to a patched, embedded copy of Minis at `Packages/minis` (project-local; Minis is
Unlicense/public domain). Full instructions, portable to any Minis project:
**`MINIS_HOTPLUG_PATCH.md`** in the project root.

**Verified 2026-09-04**, MF64 + MIDI Mix connected:

| Path | Before | After |
|---|---|---|
| Unplug/replug during play | hard kill | survives; input resumes; LEDs re-push |
| Domain reload (script edit) with devices connected | hard kill | survives |

**Cost:** a deliberate, permanent leak — a few KB per abandoned port, bounded by how
many times the port set changes in a session. It leaks on every domain reload too, so
watch for memory creeping across a long editor session with many recompiles.

**This is not shipped by this package.** The fix lives in a *dependency*, so consumers
installing `com.caseyfarina.midifighter64` still get stock Minis and still crash. Making
it real for them means forking `keijiro/Minis` and repointing the dependency — a
deliberate maintenance decision, deferred for now.

#### The failed first attempt — don't repeat it

`SetHandleAsInvalid()` **alone**, without cancelling the callbacks, is *worse than
nothing*. It does stop the `terminate()` on free; the replug then succeeds; and the
process dies anyway a moment later:

```
0x00007FFDB4D75E5B (RtMidi)    rt::midi::RtMidiIn::RtMidiIn(...)   <- nearest symbol
0x00007FFEE2C9CE1A (winmmbase) DriverCallback
```

Windows calling back into a port whose device is gone, because `ReleaseHandle` — the
thing that unregisters the callbacks — was skipped. A crash on free traded for a crash
on callback. Both halves of the patch are required.

### Why the obvious fixes don't work

- **The device filter is downstream of port opening.** `AllowedDeviceNames` filters
  `InputSystem.devices`, which only exists after Minis has already opened everything.
  It selects subscriptions, not ports. **This is the key correction** to any assumption
  that tightening the allow-list helps.
- **A managed `try`/`catch` around `MidiIn.FreeDeviceHandle` catches nothing.**
  `terminate()` fires inside C++ before control returns to managed code. Wrapping the
  P/Invoke is the single most tempting dead end here.

Note what does *not* follow from that second point, and was wrongly concluded here at
first: that no managed change can help. Catching the throw and declining to make the call
are different things, and the second is achievable — see the RESOLVED section above.

### What we did instead

Reduce exposure and stop the user walking into it blind — nothing more:

- Documented the hazard in `README.md` and `CLAUDE.md`, including the counter-intuitive
  parts (count-change trigger, delayed failure, affects builds, don't unplug for LEDs).
- `MidiEventManager.WarnOnPortClutter()` names duplicated and virtual ports at connect
  time, turning an invisible risk into a console line.
- `MidiEventManager.DisconnectAllDevices()` is exception-guarded — **hygiene, not a fix**,
  and commented as such in the source so nobody later mistakes it for one.
- LED state is re-asserted after a reconnect (`OnDevicesReconnected` /
  `OnOutputPortOpened`), so the recovery path that already exists actually leaves the
  hardware correct.

### Upstream: the two changes worth proposing

**Neither has been filed yet.** Both are MIT and forkable. Attach the runtime-path stack
above — a frame-loop reproduction is a materially stronger report than a teardown one.

**1. `thestk/rtmidi` — catch `RtMidiError` in the C wrapper on the close/free path.**
A C API allowing a C++ exception to escape is a genuine bug, it's reproducible, and the
log signature above is precise enough to act on. This is the one that matters, because
everything downstream of the abort already works:

| Step | Today | With RtMidi catching |
|---|---|---|
| `CloseAllPorts()` hits a bad handle | **`terminate()`, process dies** | logs, continues |
| `OpenAllAvailablePorts()` | never runs | ports reopen |
| InputSystem device-change fires | never runs | fires |
| `MidiEventManager.Reconnect()` | never runs | **re-subscribes, MIDI resumes** |

That converts a hard process kill into a self-healing hiccup, and nothing else in the
chain has to change for it to work.

Forking is possible but expensive: `jp.keijiro.rtmidi` ships **prebuilt binaries only**
(`Runtime/Plugins/{Windows/RtMidi.dll, Linux/libRtMidi.so, macOS/RtMidi.bundle,
Android/libRtMidi.so}`) with no C++ source, so it means a native build toolchain per
shipping platform, then forking `jp.keijiro.rtmidi` to swap the binary and repointing the
manifest. Filing upstream is far cheaper and probably better.

**2. `keijiro/Minis` — close only the port that actually changed.**
`MidiDriver.Update()` currently closes *all* ports and reopens them on any count change.
Closing just the departed port would shrink the crash surface from "every open handle" to
"the one that changed", and would stop MIDI dropping across every device when one is
touched. Contained C# change, no native rebuild, and it makes hot-plug seamless rather
than merely non-fatal. **If only one contribution gets made, this is arguably the higher
value one** — and it must be proposed as close-only-the-changed-port, not merely as a
port allow-list. (An allow-list in Minis would also help, since ports could be filtered
*before* opening, but it is a larger API change.)

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
