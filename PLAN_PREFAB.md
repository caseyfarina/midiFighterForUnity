# Single Drag-In Prefab — Implementation Plan

**Goal:** ship one prefab that carries the entire package — all MIDI input/output routing *and* the on-screen visualization UI. Drag it into any scene, press play, done. No bootstrapper, no runtime assembly, no config mirroring.

**Status: DONE — shipped in 2.0.0.** Kept as the record of why the bootstrapper was removed; the condensed version lives in `DEVNOTES.md`. Steps 3 and 4 were swapped during execution: `MidiFighterTestScene` depended on `EnsureCoreComponents`, so the prefab had to exist before the bootstrapper could be deleted.

**Version:** breaking change to consumer setup → **2.0.0**.

---

## Why the current design generates the problems it does

The duplication that prompted this isn't a layering mistake. It's a direct consequence of runtime assembly:

> Components spawned at runtime have no serialized state. Their configuration has to live somewhere persistent. The only persistent thing in the scene is the bootstrapper. So config **had** to be mirrored onto it and pushed inward at `Awake`.

Every symptom falls out of that one root — the mirrored fields, `ApplyDrawerConfig` and its rebuild-vs-restyle dispatch question, a custom editor that mostly draws *other components'* settings, two `_serializedVersion` migration systems, and the "both inspector-visible, reads as conflicting" confusion in `DEVNOTES.md`.

A prefab removes the root. Each component's own serialized fields become the single source of truth.

## The finding that makes this cheap

**Every setting the bootstrapper mirrors already exists on the component that owns it.**

| Bootstrapper field | Already exists as |
|---|---|
| `_allowedDeviceNames`, `_blockedDeviceNames` | `MidiEventManager._allowedDeviceNames` / `._blockedDeviceNames` |
| `_latchMute`, `_latchRecArm` | `MidiMixRouter._latchMute` / `._latchRecArm` |
| `_mf64ButtonConfig` | `MidiFighterButtonRouter._config` |
| `_toggleOnColor`, `_toggleOffColor`, `_buttonDownColor` | `MidiFighterButtonRouter._toggleOnColor` / etc. |
| all 12 drawer settings | `MidiStatusDrawer`'s own fields |

The bootstrapper is a **pure mirror**. The only thing it owns outright is the inline 8×8 pad grid. So this plan is mostly deletion, and the "move config to the components" step is almost entirely *already done*.

---

## Step 1 — Move the drawer UI into Runtime

**The blocker:** `MidiStatusDrawer` is in the `MidiFighter64.Samples` assembly; the seven MIDI components are in `MidiFighter64.Runtime`. Samples references Runtime, one direction only. **A prefab shipped in Runtime cannot carry a Samples component**, so a single prefab covering MIDI *and* UI is impossible until the drawer moves.

It moves cleanly — `Samples/TestScene/UI/` has **zero** code dependencies on anything Samples-only (verified; only three comments mention the bootstrapper).

```
Samples/TestScene/UI/  →  Runtime/UI/
    MidiStatusDrawer.cs   PadCell.cs   KnobDisplay.cs
    Resources/CossetteTitre-Regular.ttf   Resources/OFL.txt
```

Knock-on edits:
- `Third Party Notices.md` — font paths point at `Samples/`.
- `CLAUDE.md` / `README.md` / `Documentation~/index.html` — the drawer is no longer "the Test Scene sample's overlay", it's part of the package.
- The bundled font still loads: `Resources` folders inside a package work in builds, and `BundledFontResourceName` is unchanged.
- **The drawer becomes public API.** It's a versioning commitment that doesn't exist today. Accepted deliberately — a prefab that doesn't include the visualization isn't the thing being asked for.

`MidiFighter64.Samples.asmdef` keeps existing for the remaining sample scripts (`MidiDebugUI`, `MidiFighterTestScene`, `MidiMixCloner`, …).

## Step 2 — Inline pad grid moves to the router

The inline 8×8 mode/color grid is the one piece of config the bootstrapper genuinely owns. It moves to `MidiFighterButtonRouter`, which is the component it configures.

- Add `_inlineDefaultMode`, `_inlinePadModes[64]`, `_inlinePadColors[64]` to `MidiFighterButtonRouter`.
- Add `Editor/MidiFighterButtonRouterEditor.cs` drawing them with **`MidiFighter64PadGridGUI`** — the shared grid GUI already exists in the `MidiFighter64.Editor` assembly and is already used by both the bootstrapper editor and `MidiFighter64ButtonConfigEditor`. Reuse it; don't write a third grid.
- Keep the existing precedence: an assigned `MidiFighter64ButtonConfig` asset wins over the inline grid. That rule is already documented in `CLAUDE.md` and stays true, just relocated.
- Carry over the array-length guard (`Array.Resize` to 64) — it's still needed for the same reason, just on the router now.

## Step 3 — Gut the bootstrapper

Delete from `MidiSceneBootstrapper`: all mirrored fields, `ApplyDeviceFilter`, `ApplyMf64Config`, `ApplyMixLatchConfig`, `ApplyDrawerConfig`, `MigrateSerializedDefaults`, `CurrentSerializedVersion`, `_serializedVersion`, and `NormalizeInlineArrays`. Delete most of `MidiSceneBootstrapperEditor` with it.

**Recommendation: delete the `MidiSceneBootstrapper` MonoBehaviour entirely.** With config on the components and a prefab as the entry point, its only remaining value is `EnsureCoreComponents` — and the sole caller that still needs programmatic setup is the test-scene generator, which runs in the editor and can load the prefab directly via `AssetDatabase.LoadAssetAtPath`. No `Resources/` folder required.

This removes `EnsureCoreComponents` from the documented API. That's a real break, accepted because the package has no external consumers yet. If a code path is wanted later, add a static `MidiControllerRig.Spawn()` in Runtime rather than resurrecting a MonoBehaviour whose job is configuring other MonoBehaviours.

## Step 4 — Build the prefab

`Runtime/MIDI Controller.prefab` — one GameObject, eight components. None need separate transforms, and four are already `Instance` singletons so exactly one copy is required anyway:

```
MIDI Controller
├── MidiEventManager            (device allow/block list)
├── UnityMainThreadDispatcher
├── MidiGridRouter
├── MidiMixRouter               (latch mute / rec-arm)
├── MidiFighterButtonRouter     (pad config asset OR inline grid, LED colors)
├── MidiFighterOutput           (clear on start)
├── MidiMixOutput               (auto-mirror, clear on start)
└── MidiStatusDrawer            (all 12 drawer settings)
```

The drawer spawns `Drawer_Display{n}` children at runtime; those stay runtime-only and must **not** be saved into the prefab.

Prefab defaults should ship as the sensible out-of-box configuration: device allow list `{ "Fighter", "MIDI Mix" }`, latching on, drawer on at its design defaults.

**Consumer note:** installed via git URL the package is read-only, so the prefab can be instantiated but not edited in place. Scene instances carry overrides — normal Unity, worth one line in the docs.

## Step 5 — Scene generator and docs

- `Editor/CreateMidiTestScene.cs` — instantiate the prefab instead of building the hierarchy by hand.
- `CLAUDE.md` — replace "Minimum scene setup" and `EnsureCoreComponents` with "drag in the prefab". The drawer section moves out of the Test Scene sample.
- `README.md`, `Documentation~/index.html`, `CHANGELOG.md`, `package.json` → **2.0.0**, and **tag it**.
- `DEVNOTES.md` — record the causal chain at the top of this plan; it's the reasoning that would otherwise be re-derived.

---

## Explicitly NOT needed

**No migration, no back-compat, no deprecated fields.** The package has one user and no production scenes: the only scene using it is a development test bed whose settings are seven numbers, and its pad config lives in `Assets/MF64ButtonConfig.asset`, referenced by GUID, which survives untouched.

This is why the `DrawerSettings` refactor was reverted rather than kept — it existed to manage mirroring that this plan deletes, and its `ApplySettings` dispatcher and v5 migration block would be machinery maintained for an architecture no longer in use.

The dev scene's non-default values, for retyping after the prefab lands:

| Setting | Value |
|---|---|
| Screen Fill | `1.0` |
| Placement | `ScreenCentered` |
| Panel Opacity | `0` |
| Stroke Weight | `0.41` |
| Fisheye Scale | `4.48` |
| Toggle On / Off color | `White` / `DarkBlue` |
| Button Down color | `White` |

While the existing `_serializedVersion` machinery on the bootstrapper is being deleted with it, **the underlying rule still applies to the surviving components**: a serialized field added later still deserializes to zero on existing prefab instances. The difference is it's now paid once per setting instead of twice. Keep the value guards on `MidiStatusDrawer`'s ranged fields.

## Watch items

- **Component execution order.** With a prefab, `MidiEventManager` reads its own filter before `OnEnable` connects, so the current "apply filter after the fact, then `Reconnect()`" dance disappears rather than being reproduced. Verify `Reconnect()` is genuinely unnecessary before deleting it — it may still be wanted for runtime filter changes.
- **Singleton guards** already handle a second prefab instance; confirm the failure is a clean `Destroy` and not a null-reference cascade.
- **`Samples~` tilde.** `DEVNOTES.md` records that the folder is deliberately `Samples/` (no tilde) for local development. Moving the UI to `Runtime/` means the drawer no longer depends on that decision at all — one less thing coupled to the rename-before-publish dance.
- **`.meta` files.** Moving files inside a Unity project must go through the editor (or move the `.meta` alongside) or every asset reference breaks by GUID.

## Order of work

1. Move `UI/` → `Runtime/UI/` (with `.meta` files). Confirm it compiles and the drawer still renders from the existing scene.
2. Inline pad grid → `MidiFighterButtonRouter` + its editor.
3. Build the prefab, configured to the shipping defaults.
4. Gut/delete `MidiSceneBootstrapper` and its editor.
5. Update the scene generator; regenerate the dev scene from the prefab.
6. Docs + `2.0.0` + tag.

Compile gate after 1, 2, and 4 — Unity recompile, then `Editor.log` for `error CS`.
