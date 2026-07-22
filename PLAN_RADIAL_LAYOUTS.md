# Drawer Layout Modes — Implementation Plan

**Goal:** add two selectable *radial* layouts to the existing `MidiStatusDrawer`, alongside the current linear one. All three mirror the same live MIDI state; only the geometry changes.

**Status:** planning. Design settled for Radial 1 (a reference render exists — see below). A handful of Radial 2 decisions in "Open decisions" must be confirmed before coding.

**Repo:** `caseyfarina/midiFighterForUnity` · **Files:** `Runtime/UI/`

---

## Naming (canonical — use these names everywhere)

| Name | Enum | Was called | What it is |
|---|---|---|---|
| **Linear 1** | `DrawerLayout.Linear1` | "the existing layout" | Today's shipping drawer: 8×8 flex pad grid over 8 vertical mixer strips. Unchanged. |
| **Radial 1** | `DrawerLayout.Radial1` | "Layout A — Centered Radial" | Concentric rings, centre outward. Rendered in `radial_A_centered.svg`. |
| **Radial 2** | `DrawerLayout.Radial2` | "Layout B — Radial Columns" | Sunburst: the 8 channel columns fanned around a 270° arc. |

The numeric suffix is deliberate — it leaves room for `Linear2`, `Radial3` without renaming anything. Use these names in the enum, the inspector, `CLAUDE.md`, and the changelog. `radial_A_centered.svg` keeps its filename (it's referenced from here); its **title text should be updated to "Radial 1"** when it's next regenerated.

---

## Read this first — how the existing drawer is built

`MidiStatusDrawer.cs` (~1450 lines) owns one `UIDocument` per active display and builds its tree with **UI Toolkit flexbox**:

- `BuildAllViews()` → per display → `BuildTree()` → `BuildMf64Section()` (8×8 flex grid of `PadCell`) + `BuildMixSection()` (flex row of 8 channel strips, each strip = 3 `KnobDisplay` + 1 fader bar; then master fader + util row).
- Every widget instance is stored **by reference** on the per-display `DrawerView` object:
  - `PadCell[64] pads` (indexed by `linearIndex`)
  - `KnobDisplay[8,3] knobs`
  - `VisualElement[8] faderBars` / `faderTracks`, `masterFaderBar` / `masterFaderTrack`
  - `PadCell[8] mutes`, `PadCell[8] recArms`, `PadCell soloModifier`, `bankLeft`, `bankRight`
  - `Label messageLabel`
- **The event handlers are layout-agnostic.** `HandleMixKnob`, `HandleToggle`, `HandleMixMute`, etc. just walk `_views` and set state on those stored references (`k.Value = value`, `cell.Active = isOn`). They never touch geometry.

**Consequence — the core of this task:** a radial layout is *a different builder that populates the same `DrawerView` arrays with the same `PadCell` instances, positioned differently.* If you register the same references, every handler works with **zero changes**. Do not rewrite the event wiring.

### Five things the existing drawer forces you to handle

1. **Positioning switches from flex to absolute polar.** `PadCell` and `KnobDisplay` are self-contained `Painter2D` elements that draw inside their own rect, so they position fine with `style.position = Position.Absolute` + `left/top/width/height`. Compute those from polar coordinates: `x = cx + r*cos(θ)`, `y = cy + r*sin(θ)`, then `left = x - size/2`, `top = y - size/2` to centre the element. Use **UI degrees throughout** — y grows *downward*, which is `KnobDisplay`'s existing convention (its ring spans 270° with the gap at the bottom, `ArcStartDeg = 135`, `ArcSweepDeg = 270`). Under that convention **270° is straight up**, and increasing θ runs **clockwise**.

2. **The radial container's size is a constant, never a measurement.** ⚠️ *Corrected from the previous draft, which said "side = min(width, height)".* That is the exact pattern `DEVNOTES.md` records as an editor **hard-freeze**: measuring a flex-shrinkable element in a `GeometryChangedEvent` and writing back a size re-triggers layout every pass. Do it the way `BuildMf64Section` already does it — an explicit arithmetic side in design units:

   ```csharp
   const float RadialSideDesign = 920f;   // matches the SVG's 920×920 viewBox
   square.style.width = RadialSideDesign;
   square.style.height = RadialSideDesign;
   square.style.flexShrink = 0;
   ```
   `cx = cy = RadialSideDesign / 2`. Every radius below is expressed as a fraction of that half-side, so the number itself only sets internal proportions — `ScreenFraction` still does all the on-screen sizing.

3. **Knobs, faders, and the master all become one Painter2D primitive: `RadialArc`.** In radial modes the circle-plus-pointer `KnobDisplay` and the percent-height fader bar are both replaced by a single `RadialArc : VisualElement` that fills its square container (`position:absolute`, inset 0), draws in container-local coords, and paints two arcs in `generateVisualContent`: a faint full-slice **track** and a **fill** arc from the slice's start edge to `start + value*sweep`.
   Fields: `cx, cy, radius, startDeg, sweepDeg, strokeWidth, trackColor, fillColor`; API: `float Value` / `SetValue(v)` → clamp + `MarkDirtyRepaint()`; `pickingMode = Ignore`; **`StrokeScale`** (see #4).
   Store in new `DrawerView` fields — `RadialArc[8,3] knobArcs`, `RadialArc[8] faderArcs`, `RadialArc masterArc` — and add **one additive, null-guarded line** to each handler alongside the existing linear update:
   `HandleMixKnob` → `v.knobArcs?[ch,r]?.SetValue(value)`; `HandleMixChannelFader` → `v.faderArcs?[ch]?.SetValue(value)`; `HandleMixMasterFader` → `v.masterArc?.SetValue(value)`.
   The linear `knobs` / `faderBars` fields stay untouched, so Linear 1 is unaffected. (Bipolar/pan knobs could optionally fill from slice-centre outward; edge-fill is the default and matches the drawer's current 0–1 treatment.)

4. **`RadialArc` must join the theme and stroke-weight systems, or it will look correct until the user presses F3.** This is the documented failure mode in `DEVNOTES.md` — a literal colour at a build site survives the build and is then skipped by `ApplyTheme`. Three concrete requirements:
   - All colours come from `Palette.For(theme, opacity)`: fill = `p.Ink` (master gets its own accent, see Radial 1), track = `p.Track`.
   - `ApplyTheme` gets one line in the same idiom as the existing `Query<KnobDisplay>` / `Query<PadCell>` blocks:
     ```csharp
     v.root.Query<RadialArc>().ForEach(a => { a.SetInk(p.Ink, p.Track); a.StrokeScale = _strokeWeight; });
     ```
     A **type query**, not a walk over the new arrays — it then covers any arc added later for free, which is why the existing code queries by type.
   - Stroke width must resolve as `strokeWidth * StrokeScale` so the global **Stroke Weight** slider reaches it. Any stroked drawing that doesn't go through `StrokeScale` silently ignores the slider.

5. **"Seen" opacity is state on the drawer, not on the widget, and radial must honour it both ways.** `_knobsSeen[8,3]`, `_fadersSeen[8]`, `_masterFaderSeen` persist across rebuilds; build sites read them (`opacity = seen ? SeenOpacity : UnseenOpacity`) and handlers flip them. The handlers currently raise opacity on `v.faderTracks[ch]` / `v.masterFaderTrack`, which are **null in radial modes** — so each arc needs its own read at build time *and* its own flip line in the handler. Skip this and every radial control stays permanently at 40%, or permanently at 100%; both read as a rendering bug.

6. **Fisheye focus is flex-grow based and won't work in radial modes.** `FocusMf64Pad` / `ClearMf64Focus` scale `mf64Rows[8]` via flex-grow, and `mf64Rows` is not populated by a radial builder. Guard both to no-op when `_layout != Linear1`, and reset `_focusRow/_focusCol` plus cancel `_focusClearTimer` on a layout switch. (A later pass could reimplement focus as a transform scale on the absolutely-positioned pad; out of scope here.)

---

## Layout selection

Add an enum and a serialized field on `MidiStatusDrawer`:

```csharp
public enum DrawerLayout { Linear1 = 0, Radial1 = 1, Radial2 = 2 }
[SerializeField] DrawerLayout _layout = DrawerLayout.Linear1;
public DrawerLayout Layout { get => _layout; set { if (_layout == value) return; _layout = value; RebuildIfLive(); } }
```

**`Linear1` must be enum value 0.** Serialized fields added after a scene was saved deserialize to zero, and `DEVNOTES.md` records two separate investigations caused by getting this wrong. With `Linear1 = 0`, zero *is* the correct default, so this field needs **no `MigrateSerializedDefaults` block and no `CurrentSerializedVersion` bump** — the only new drawer setting so far that gets away with that. Say so in a comment, or someone will add a migration later "for consistency" and break nothing loudly but confuse everyone.

Branch in `BuildTree()` on `_layout` to call `BuildLinear1` (the existing body, extracted), `BuildRadial1`, or `BuildRadial2`.

### Switching at runtime — not via `OnValidate`

`MidiStatusDrawer.OnValidate` is deliberately scoped to `ApplyTheme` + `ApplyPlacement`, both of which restyle live elements and touch neither the tree nor any GameObject. **A layout change rebuilds the tree, so it must not be pushed from `OnValidate`** — that route is a documented editor deadlock. Provide instead:

- **F4 cycles the layout** (Linear 1 → Radial 1 → Radial 2 → Linear 1), gated by `EnableFunctionKeys` alongside F2/F3, added in `Update()`.
- A **bootstrapper field** applied at `Awake` via `ApplyDrawerConfig`, like every other drawer setting.
- The scripting equivalent, `MidiStatusDrawer.Instance.Layout = …`.

Document F4 in `CLAUDE.md`'s hotkey list.

### The size budget must branch too — this is the highest-risk part

`DrawerWidth`, `DrawerHeight`, and `DerivedReferenceResolution` currently sum the *linear* sections. Left alone, a radial drawer inherits a budget describing content it doesn't build: `Expand` scales by `min(screenW/refW, screenH/refH)`, so **Screen Fill still "works" while filling a budget that is mostly empty space** — the exact ~40%-of-display bug `DEVNOTES.md` records for the unconditional `GridSide` term. Every term must describe what is actually laid out:

```csharp
float RadialSectionHeight => (2f * SectionPad) + RadialSideDesign;

float DrawerWidth => _layout == DrawerLayout.Linear1
    ? GridSide + (2f * SectionPad) + (2f * DrawerPadX)
    : RadialSideDesign + (2f * SectionPad) + (2f * DrawerPadX);
```

and `DrawerHeight`'s radial branch = `2*DrawerPadY + RadialSectionHeight + MessageSectionHeight` (see the message-strip note below). Radial is square, so both axes come from the same constant — the "`DrawerWidth` is unconditional because the mix section wants the grid's width" reasoning is linear-only and does not carry over.

Also: add the layout mode to the **Log Layout Report**'s `sections` line. That line exists precisely so this class of bug is readable from the log instead of guessed at.

### The message strip needs a home in radial modes

It normally lives in the MIDI Mix utility row, which no radial builder constructs. `BuildTree` already has the fallback — the `if (!_showMidiMix)` branch that gives it its own `MakeSection("Message")` panel. **Radial modes always take that branch** (change the condition to `if (!_showMidiMix || _layout != DrawerLayout.Linear1)`), and `DrawerHeight` must include `MessageSectionHeight` accordingly. Otherwise the drawer silently loses its event readout in both radial modes.

### `ShowMf64` / `ShowMidiMix` in radial modes

**Decision: radial builders honour the flags by skipping bands, and radii stay fixed.** Hiding the mixer in Radial 1 leaves the pad rings in place with empty space around them rather than re-flowing the ring stack. Re-flowing would mean a second radius table per visibility combination, for a case nobody asked for. The height budget uses the full `RadialSideDesign` regardless — the square is the square. Note this in a comment; it's the kind of thing that later reads as an oversight.

---

## Radial 1 — Centered Radial

Concentric rings, centre outward. All angles evenly distribute each ring's element count over 360°. **`radial_A_centered.svg` in the package root is the authoritative reference render** — every number below is measured from it.

> ⚠️ **The SVG is authoritative for geometry only — never for colour.** Its hues (blue pads, amber knob arcs, green faders, violet master, red/pink toggles) exist to make eleven concentric bands distinguishable in a flat static diagram. They are **not** a UI palette and must not be transcribed into the builder. In the live drawer every colour comes from `Palette.For(theme, opacity)` and is repainted by `ApplyTheme`; a literal `Color` at a radial build site is the exact defect `DEVNOTES.md` warns about, and it survives the build only to diverge the first time the user presses F3. Read the SVG for radii, angles, counts, and relative stroke weights. Nothing else.

### Ring stack (inner → outer)

| Band | Contents | Count | Angular step |
|---|---|---|---|
| MF64 ring 3 (centre) | centre 2×2 pads | **4** | 90° |
| MF64 ring 2 | 4×4 border pads | **12** | 30° |
| MF64 ring 1 | 6×6 border pads | **20** | 18° |
| MF64 ring 0 | 8×8 border pads | **28** | ~12.857° — **not 36** |
| Knob arc, row 1 | MIDI Mix knob row 1 | 8 | one arc per channel, in its 45° slice |
| Knob arc, row 2 | MIDI Mix knob row 2 | 8 | concentric with row 1 |
| Knob arc, row 3 | MIDI Mix knob row 3 | 8 | concentric with rows 1–2 |
| Channel fader arcs | 8 channel faders | 8 | thicker arc, same 45° slices |
| Master fader | 1 arc over the whole ring | 1 | **full 360°**, value = sweep |
| Toggle ring (outer) | 8 Mute + 8 Rec-Arm | **16** | per-channel pairs at ±7° |

`4 + 12 + 20 + 28 = 64` pads. `3 × 8 = 24` knob arcs. `8` channel-fader arcs + `1` master. `16` toggles. All hardware-exact.

### Geometry table (measured from the SVG)

Radii and sizes as fractions of `R = RadialSideDesign / 2`. Declare these as named constants; they are the tuning surface for the whole layout.

| Band | radius / R | element size / R | notes |
|---|---|---|---|
| Pad ring 3 | 0.0957 | 0.0543 dia | pad rings are evenly spaced, +0.10 R each |
| Pad ring 2 | 0.1957 | 0.0522 dia | |
| Pad ring 1 | 0.2957 | 0.0500 dia | |
| Pad ring 0 | 0.3957 | 0.0457 dia | |
| Knob arc r1 | 0.4870 | 0.0152 stroke | knob rings +0.0609 R apart |
| Knob arc r2 | 0.5478 | 0.0152 stroke | |
| Knob arc r3 | 0.6087 | 0.0152 stroke | |
| Channel fader arc | 0.6870 | 0.0239 stroke | visibly thicker than a knob arc |
| Master ring | 0.7652 | 0.0283 stroke | thickest |
| Toggle ring | 0.8522 | 0.0261 dia | |
| Channel labels | 0.9043 | — | `ch1`…`ch8` |

Pad diameters shrink slightly inward — inner rings have less circumference to share, and that's expected, not a bug to correct.

### Angles

- **Channel angle:** `θ_c = -90° + c * 45°` for `c = 0..7`. Channel 1 is at the **top**, and channels advance **clockwise** (ch2 upper-right, ch3 right, … ch8 upper-left). Confirmed by the SVG's label placement.
- **Slice:** each channel's arcs span `θ_c ± 19.5°` — a **39° sweep** inside the 45° slice, leaving a **6° gap** between channels. The fill grows from the slice's start edge (`θ_c - 19.5°`) clockwise.
- **Master:** `startDeg = -90°` (top), `sweepDeg = 360°`, clockwise. Track is a full circle; fill is `value × 360°`.
- **Toggles:** Mute at `θ_c - 7°`, Rec-Arm at `θ_c + 7°`. This locks Open Decision #1 — the reference render uses per-channel pairs, and it reads by channel far better than 16 interleaved dots at 22.5°.

### MF64 square-ring → circle mapping

For pad at 1-based `(row, col)`, ring index = `min(row-1, col-1, 8-row, 8-col)` → `0` (outer) … `3` (centre). Ring 3 → smallest radius, ring 0 → largest MF64 radius. Within a ring, order pads by walking the square ring **clockwise from that ring's top-left cell** and place them at `θ_i = -90° + (i / count) * 360°`. This preserves ring-neighbour adjacency so the circle still "reads" like the grid. Use `MidiFighter64InputMap.FromNote` for `row`/`col`/`linearIndex` — never compute notes by hand — and key `DrawerView.pads[]` by `linearIndex`.

### Colours

Everything comes from `Palette.For` — see the warning above. The default is that **Radial 1 introduces no new colours at all**; it is the existing drawer palette in a different geometry.

- Pads: unchanged — they mirror hardware LED colours and go through `Palette.AdaptLed` + `_padRawFill` exactly as in Linear 1. This carries over for free because the pads are the same `PadCell` instances.
- Knob arcs, fader arcs, toggles, master ring: `p.Ink` fill on a `p.Track` track — the same treatment mixer chrome gets today.

Band separation is carried by **geometry**, not hue: the bands sit at distinct radii, and stroke weight already steps up from knob (0.0152 R) to fader (0.0239 R) to master (0.0283 R). The master ring is additionally the only continuous 360° arc among segmented 39° ones, which reads as different on its own.

*Open question, not a decision:* if the master ring turns out not to read apart from the fader arcs once it's on screen, the fix is a new accent field on `Palette` — added to **both** the Dark and Light branches of `Palette.For`, or Light theme gets a default-black ring. Don't add it pre-emptively, and don't reach for violet just because the diagram is violet.

### Omitted controls

Solo-modifier and Bank L/R are **not built** in either radial layout (locked decision). Leave `soloModifier` / `bankLeft` / `bankRight` null; the existing handlers already null-check (`FlashPad(v.bankLeft)` no-ops on null, `HandleMixSoloModifier` iterates safely).

---

## Radial 2 — Radial Columns (sunburst)

Take the existing per-channel **vertical column** and fan the 8 columns around an arc. This is Linear 1's strip layout bent into a 270° sunburst.

### Structure

- **8 spokes**, one per channel, distributed across **270°** (90° gap, gauge-cluster style). Spoke angle `θ_c = fanStart + c * (270° / 7)` for `c = 0..7` (or `/8` for 8 equal sectors — confirm).
- Each spoke, **inner → outer radius** (MIDI Fighter inside, MIDI Mix outside, column flipped so index grows outward):
  1. 8 MF64 pads from that column (innermost),
  2. 3 knobs (rows 1–3),
  3. 1 channel fader (outermost).
- **Column ↔ channel pairing is an assumption:** MF64 column `c` is paired with MIDI Mix channel `c`. They're different instruments; this 8-to-8 pairing is a design choice, not a hardware fact. State it in a comment.

### Fader in this layout

Here the fader is radial-*outward*, so it can stay a bar: a thin radial bar pointing outward from the spoke, fill length = `value`. Reuse the same `faderArcs[8]` field and additive handler line from the architecture section — `RadialArc` with a tiny sweep degenerates badly, so this is likely a separate small element type or a plain `VisualElement` with a rotation transform. Decide when it's on screen.

### Collision handling ("elements need to be scaled")

Available tangential room shrinks toward the centre: arc length at radius `r` for one spoke's slot = `r · (270° in rad / 8)`. Size each element as `min(radial slot height, that arc length)`, so inner pads auto-shrink and outer knobs/faders can be larger. Expose a per-band scale multiplier for hand-tuning once it renders. Reuse `PadCell.StrokeScale` / `KnobDisplay.StrokeScale` to keep stroke weight uniform as sizes vary.

---

## Implementation order

1. Add the `DrawerLayout` enum (`Linear1 = 0`) + serialized field + `Layout` property + `RebuildIfLive()`. Extract today's `BuildTree` body into `BuildLinear1` and branch. **Confirm Linear 1 still builds pixel-identically before writing any radial code.**
2. Branch `DrawerWidth` / `DrawerHeight` on layout; add `RadialSideDesign` and `RadialSectionHeight`; add the layout mode to the Log Layout Report's `sections` line. Verify with the report that a stub radial (empty square) hits Screen Fill exactly.
3. Route the message strip to its own panel in radial modes.
4. Add `RadialArc : VisualElement` — Painter2D; fields `cx, cy, radius, startDeg, sweepDeg, strokeWidth, trackColor, fillColor`; `float Value` / `SetValue`; `SetInk(fill, track)`; `StrokeScale`; `pickingMode = Ignore`. Add `knobArcs` / `faderArcs` / `masterArc` to `DrawerView`, the `Query<RadialArc>` line to `ApplyTheme`, and the additive + seen-opacity lines to `HandleMixKnob`, `HandleMixChannelFader`, `HandleMixMasterFader`.
5. Guard `FocusMf64Pad` / `ClearMf64Focus` to no-op when `_layout != Linear1`; reset focus state on layout switch.
6. **Radial 1** — `BuildRadial1()`: a `PolarPlace(element, r, θ, size)` helper, then the four pad rings (ring membership + clockwise walk), the 24 knob arcs, 8 fader arcs, the 360° master ring, the 16 toggles, and the channel labels. Populate `pads`, `knobArcs`, `faderArcs`, `masterArc`, `mutes`, `recArms`. Compare against `radial_A_centered.svg` side by side, then verify live MIDI moves the right widgets.
7. **Radial 2** — `BuildRadial2()`: 8 spokes across 270°, inner-out stacking, per-band scaling. Populate the same arrays.
8. F4 hotkey + bootstrapper field + `ApplyDrawerConfig` + custom-editor entry (the four-place tax `DEVNOTES.md` describes). Confirm clean rebuild and no leaked subscriptions across switches (mirror the `OnEnable`/`OnDisable` `+=`/`-=` discipline).
9. Docs, all six files `DEVNOTES.md` lists as "must stay in sync": `CLAUDE.md` (layout modes + F4 under the drawer section), `DEVNOTES.md` (any new hard-won constraint), `Documentation~/index.html`, `README.md` if the feature list changes, `CHANGELOG.md`, and a `package.json` **minor** bump. **Then tag the commit** — untagged bumps make the version unretrievable via the git-URL install. Commit directly to `main`.

---

## Decisions (locked)

- **Naming:** Linear 1 / Radial 1 / Radial 2, with `Linear1 = 0` in the enum.
- **Solo-modifier + Bank L/R:** omitted in both radial layouts. Linear 1 only.
- **Radial 1 knob + fader representation:** arcs, not dials. Each knob is a `RadialArc` filling its channel's 39° slice; the 3 rows are concentric arcs per channel. Channel faders are thicker arcs in the same slices.
- **Master fader home (Radial 1):** a single full-circumference `RadialArc` at 0.7652 R, just outside the channel-fader arcs.
- **Colour:** no new colours. Radial modes reuse the existing `Palette`; the reference SVG's hues are diagram-only and are never transcribed. Bands are separated by radius and stroke weight.
- **Toggle-ring arrangement (Radial 1):** per-channel Mute/Rec-Arm pairs at `θ_c ∓ 7°`. *(Was Open Decision #1; settled by the reference render.)*
- **Channel 1 at top, clockwise;** 39° arc sweep, 6° inter-channel gap. *(From the reference render.)*
- **Radial container sizing:** constant `RadialSideDesign`, never a measured `min(width, height)`.
- **Section visibility in radial:** flags honoured by skipping bands; radii fixed; budget unchanged.
- **Layout switching:** F4 + bootstrapper field + scripting property. Never `OnValidate`.

## Open decisions (Radial 2 only — confirm before coding step 7)

1. **Master fader home:** outermost spoke-end ring? a short arc in the 90° gap? or omit and keep master in Linear 1 + Radial 1 only?
2. **Spoke spacing:** 270°/7 (spokes at both ends of the arc) vs 270°/8 (8 equal sectors, gap split at the ends).
3. **Column orientation:** which MF64 column maps to each channel, and whether the physical top of the column faces the hub or the rim.
4. **Fan orientation:** where the 90° gap sits (bottom, like a speedometer?).
5. **Fader element type:** a degenerate-sweep `RadialArc` won't read at these proportions; likely a separate thin radial bar. Decide on screen.

---

## Scope estimate

- `RadialArc.cs` ~80 lines (knob arcs, channel faders, master ring; incl. `StrokeScale` + `SetInk`).
- `BuildRadial1` ~200 lines; `BuildRadial2` ~150 lines.
- Enum/field/property/F4/guards/handler edits ~60 lines.
- Size-budget branch + message-strip routing + Log Layout Report line ~30 lines.
- Bootstrapper + custom editor ~40 lines.

No new packages, no new external deps. Reuses `PadCell` for pads + toggles and every existing event handler (radial arc updates are additive lines).
