# Radial Layouts — Agent Execution Brief

Companion to `PLAN_RADIAL_LAYOUTS.md`. That file is the **design**; this one is the **work breakdown** for delegated execution. Agents read both.

---

## The constraint that shapes everything

Nearly every step of the design plan edits **one file**: `Samples/TestScene/UI/MidiStatusDrawer.cs` (1457 lines). Two agents editing it concurrently will clobber each other — `Edit` matches on exact strings, and a parallel edit invalidates another agent's match or silently lands in the wrong place.

**Therefore this is a mostly-sequential pipeline, not a fan-out.** Real parallelism exists in exactly three places, all of them "different file, no shared symbols":

- `RadialArc.cs` — a brand-new file with zero inbound dependencies. Parallel with anything.
- `MidiSceneBootstrapper.cs` + its custom editor — different files, needs only the enum to exist.
- The six documentation files — different files, but must run *last* so they describe what was actually built.

Anyone proposing to parallelize WP2–WP5 has not read this section.

## File ownership (exclusive locks)

| File | Owned by |
|---|---|
| `Samples/TestScene/UI/MidiStatusDrawer.cs` | WP1 → WP2 → WP3 → WP4 → WP5a, one at a time, never concurrently |
| `Samples/TestScene/UI/RadialArc.cs` (new) | WP0 only |
| `Samples/TestScene/MidiSceneBootstrapper.cs` + editor | WP5b |
| `CLAUDE.md`, `DEVNOTES.md`, `CHANGELOG.md`, `README.md`, `Documentation~/index.html`, `package.json` | WP6 |
| `PLAN_RADIAL_LAYOUTS.md`, `radial_A_centered.svg` | **nobody — read-only reference** |

## Verification protocol — read before claiming anything works

**No agent can compile this project.** Coplay MCP is listed but non-functional here (`DEVNOTES.md`); there is no headless build. Compilation is verified by:

1. The user tabs into the Unity Editor, triggering a recompile.
2. Grep `C:\Users\casey\AppData\Local\Unity\Editor\Editor.log` for `error CS`.

That gate is **operator-run between waves**, not agent-run. Consequences, which are binding on every agent:

- **Never write "verified", "compiles", or "tested" in a report.** You cannot know. Report what you changed and what you are unsure about.
- Prefer the smallest edit that satisfies the acceptance criteria. An agent that also "cleaned up while it was in there" makes a compile failure expensive to attribute.
- If a change requires a symbol another WP owns and it doesn't exist yet, **stop and report** — do not stub it out and do not create it.

## Global rules (every agent, every WP)

1. **`Linear1` output must not change.** It is the shipping layout. Any diff that alters what Linear 1 renders is a bug, including "equivalent" refactors.
2. **No literal `Color` at a build site.** Everything through `Palette.For(theme, opacity)`. This is `DEVNOTES.md`'s most-repeated rule; a literal survives the build and diverges on the first F3.
3. **No literal stroke width.** Everything through `StrokeWidth * StrokeScale`, or the global Stroke Weight slider silently skips it.
4. **Never measure an element and write back a size.** No `GeometryChangedEvent` → `style.height`. This hard-freezes the editor (no exception, no log — just a hang). Sizes are arithmetic from constants.
5. **Never rebuild the tree from `OnValidate`.** It is scoped to `ApplyTheme` + `ApplyPlacement` on purpose.
6. **Don't compute MF64 note numbers.** `MidiFighter64InputMap.ToNote/FromNote` only; the naive `36 + row*8 + col` is wrong for the right-hand half.
7. Match the surrounding comment density. This file explains *why* at every non-obvious constant — a bare magic number will read as unfinished.

---

## Work packages

### WP0 — `RadialArc.cs` (new file) · parallel-safe · ~80 lines
**Depends on:** nothing. **Owns:** `Samples/TestScene/UI/RadialArc.cs`.

Create the Painter2D primitive per `PLAN_RADIAL_LAYOUTS.md` §"three things", item 3 & 4. Model it structurally on the existing `KnobDisplay.cs` (131 lines) — same namespace, same `generateVisualContent` idiom, same `StrokeScale` property pattern.

- Fields: `cx, cy, radius, startDeg, sweepDeg, strokeWidth, trackColor, fillColor`.
- API: `float Value` / `SetValue(float)` → clamp 0–1 + `MarkDirtyRepaint()`; `SetInk(Color fill, Color track)`; `float StrokeScale`; `pickingMode = Ignore`.
- Paints two arcs: faint full-slice track, then fill from `startDeg` to `startDeg + Value * sweepDeg`.
- UI degrees, y-down, clockwise-positive — **same convention as `KnobDisplay`** (270° = up).
- Must render correctly at `sweepDeg = 360` (the master ring) — check the arc call doesn't degenerate to a zero-length path at a full turn.

**Acceptance:** file compiles standalone in principle; no reference to `MidiStatusDrawer` or any symbol outside `KnobDisplay`'s existing imports. **Forbidden:** touching `MidiStatusDrawer.cs`.

### WP1 — Layout enum + `BuildTree` split · foundation
**Depends on:** WP0 not required. **Owns:** `MidiStatusDrawer.cs`.

Plan step 1. Add `DrawerLayout { Linear1 = 0, Radial1, Radial2 }`, the serialized field, the `Layout` property with `RebuildIfLive()`. Extract the current `BuildTree` body into `BuildLinear1(view)` and branch on `_layout`. Add `BuildRadial1`/`BuildRadial2` as stubs that build only the empty square container (explicit `RadialSideDesign` side, `flexShrink = 0`).

Comment *why* `Linear1 = 0` (no serialization migration needed — see plan).

**Acceptance:** Linear 1 renders byte-identically. Radial stubs produce an empty square, no exceptions.

### WP2 — Size budget + message strip + fisheye guards
**Depends on:** WP1. **Owns:** `MidiStatusDrawer.cs`.

Plan steps 2, 3, 5 — three surgical edits, bundled because they are small and all in this file.

- Branch `DrawerWidth`/`DrawerHeight` on layout; add `RadialSectionHeight`. **Keep every term conditional on the section it measures** — this is the ~40%-under-fill bug.
- Add the layout mode to the Log Layout Report `sections` line.
- Route the message strip to its own panel in radial modes (`!_showMidiMix || _layout != Linear1`).
- Guard `FocusMf64Pad`/`ClearMf64Focus` to no-op off Linear 1; reset `_focusRow/_focusCol` and cancel `_focusClearTimer` on layout switch.

**Acceptance:** with a stub radial layout, the Log Layout Report shows one coverage axis equal to Screen Fill.

### WP3 — `RadialArc` wiring
**Depends on:** WP0 **and** WP2. **Owns:** `MidiStatusDrawer.cs`.

Plan step 4's wiring half. `DrawerView` gains `RadialArc[8,3] knobArcs`, `RadialArc[8] faderArcs`, `RadialArc masterArc`. Add the `Query<RadialArc>()` line to `ApplyTheme` (**type query, not an array walk** — matches the existing `Query<KnobDisplay>` idiom and covers arcs added later). Add additive null-guarded `SetValue` lines plus the **seen-opacity flip** to `HandleMixKnob`, `HandleMixChannelFader`, `HandleMixMasterFader`.

The seen-opacity flip is the easy miss: handlers currently raise opacity on `faderTracks`/`masterFaderTrack`, which are null in radial. Without a per-arc flip every radial control stays at 40% forever.

**Acceptance:** Linear 1 unaffected (all new lines are null-guarded no-ops there).

### WP4 — `BuildRadial1` · the big one · ~200 lines
**Depends on:** WP3. **Owns:** `MidiStatusDrawer.cs`.

Plan step 6. A `PolarPlace(el, r, θ, size)` helper, then: 4 pad rings (ring index `min(row-1, col-1, 8-row, 8-col)`, clockwise walk from each ring's top-left, `θ_i = -90° + (i/count)*360°`), 24 knob arcs, 8 fader arcs, the 360° master ring, 16 toggles at `θ_c ∓ 7°`, channel labels.

**All radii and sizes come from the geometry table in the design plan**, declared as named constants, as fractions of `R = RadialSideDesign / 2`. Channel angle `θ_c = -90° + c*45°`; slice `θ_c ± 19.5°` (39° sweep, 6° gap).

**Colour: no new colours.** `p.Ink` on `p.Track` throughout; pads keep `Palette.AdaptLed` + `_padRawFill`. The SVG's hues are diagram-only — read it for geometry, never for colour.

**Acceptance:** all 64 pads present and keyed by `linearIndex`; live MIDI moves the correct widget; visually matches `radial_A_centered.svg` in geometry.

### WP5a — F4 hotkey · **WP5b — bootstrapper + editor** (5b parallel with 5a)
**Depends on:** WP4. **5a owns** `MidiStatusDrawer.cs`; **5b owns** `MidiSceneBootstrapper.cs` + its custom editor.

Plan step 8. 5a: F4 cycles layout in `Update()`, gated by `_enableFunctionKeys` beside F2/F3. 5b: serialized layout field, `ApplyDrawerConfig` wiring, custom-editor entry — the four-place tax `DEVNOTES.md` describes. 5b must **not** add a `MigrateSerializedDefaults` block (enum 0 is the correct default).

### WP6 — Documentation · last
**Depends on:** WP5. **Owns:** the six sync-files.

Plan step 9. `CLAUDE.md` (layout modes + F4 in the hotkey list), `DEVNOTES.md` (any new constraint discovered during execution), `CHANGELOG.md`, `README.md` if the feature list changes, `Documentation~/index.html`, `package.json` **minor** bump. Operator tags the commit afterward — untagged bumps are unretrievable via the git-URL install.

---

## Execution order

```
WP0 ──────────────┐
                  ├─→ WP3 ─→ WP4 ─→ ┬─→ WP5a ─┬─→ WP6
WP1 ─→ WP2 ───────┘                 └─→ WP5b ─┘
```

Wave 1: WP0 ‖ WP1 · Wave 2: WP2 · Wave 3: WP3 · Wave 4: WP4 · Wave 5: WP5a ‖ WP5b · Wave 6: WP6.
Operator compile-gate after every wave.

## Not scheduled: Radial 2 (plan step 7)

**Deliberately excluded.** It carries five unresolved design decisions — master fader home, spoke spacing (270°/7 vs /8), column↔channel orientation, fan orientation, and fader element type. Building it on guesses produces something that has to be rebuilt once the answers arrive. `Radial2` ships as an enum value whose builder falls back to Linear 1 with a `Debug.LogWarning`, and gets its own pass.
