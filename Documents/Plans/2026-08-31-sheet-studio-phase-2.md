# Sheet Studio — Phase 2: fold preview

## 1. Task

Add the fold-preview pane to the Sheet Studio (level editor phase 2 of 3): preview folding the sheet any way, right in the editor, without entering Play Mode. Same drag gesture as the game (drag in from an edge/corner, or from a crease to fold further; live preview; red refuse; release commits; seam click unfolds), plus a fold list with editable depths, and a draggable test-player ghost that applies the game's covers-player rule.

**Out of scope (Phase 3):** placing elements through a previewed fold onto the right face, and the x-ray hold. Also out: any change to runtime fold behaviour; preview folds are never saved (folds do not persist — Bible §8 [LOCKED], and a level editor has no business persisting them anyway).

## 2. Design references

- **Bible §4 (fold model):** geometry [LOCKED] (edge/corner crease families); no overhang (landed Flap on the sheet — Aaron 2026-08-26); covers-player invalid [LOCKED] — applied in preview via the test ghost. Decision #10 (continuous vs snapped depth) stays open: the editor drag is continuous; precise values go through the fold list's editable depth fields.
- **Bible §4 input scheme [DECIDED provisional]:** the preview mirrors it — Seam first (unfold), then crease (fold further), then edge/corner (new fold); invalid tints red; release commits; right-click/Escape cancels.
- **Bible §4 rendering [DECIDED]:** render-texture compositing behind `IFoldRenderer`. The editor does **not** drive the runtime renderer (it is scene-coupled: `ScreenCamera`, `Destroy`, coroutines); it rebuilds the same composite in an editor-owned preview scene using the same pure pieces (`SheetLayers`, `PolygonMeshBuilder`, same UV math, same materials and tints read from the sheet's renderer component). The `IFoldRenderer` seam separates *what a fold does* from *how it is drawn*; the editor consumes only the *what* (pure model) and draws its own *how*, which is exactly what the seam is for.
- **Bible §6 (back convention):** exposed Back content in the composite comes through `SheetGeometry.BackToFront` in the UV mapping, identical to the runtime renderer's line.
- **Bible §8 [LOCKED]:** folds do not persist. Preview folds are transient editor state, cleared when the stage changes; nothing is written to the prefab.
- **implementation-guidelines §4a:** no runtime behaviour added → no game tunables. Where a value shapes what the *game* would look/behave like, the editor reads the authored value from the stage's components instead of inventing its own: commit `minDepth` from `SheetFolds`, tints/crease/seam widths/colours and `pixelsPerUnit` from `RenderTextureFoldRenderer` (via SerializedObject; defaults if missing, with a help-box). Grab radii mirror `FoldDragInput`'s defaults as editor constants (that component lives on the Desk scene, not the stage).

## 3. Decisions already made by Aaron (2026-08-31)

- Phase 2 = fold preview, per the accepted three-phase proposal; full workflow per phase.
- **Layout: second row.** Front and Back panes on top; a wide fold-preview pane below (with the fold list beside it).
- **Preview allows everything:** the sheet's `allowMultipleFolds` / `allowStacking` playtest toggles are ignored in the editor — any fold combination previews. (Chosen over "respect the toggles"; a previewed setup may be impossible in-game while a toggle is off, and Aaron picked that trade.) Geometric truths still hold: no overhang (depth clamps), nothing-to-fold shows nothing, TooShallow drops on release.
- **Test-player ghost included:** a draggable player-footprint rect in the preview pane; folds that would cover it preview red and refuse commit, exactly the game's [LOCKED] rule. Toggleable; when off, the player rule is simply not applied (noted in the pane).
- **Fold-list removal refuses with reason** (2026-08-31, from plan-review round-1 Q1): removing a fold that later folds depend on is refused, with the dependency named in the mini-label; remove the later folds first. Mirrors the game's legal unfold ordering.
- **Ghost is a faithful stand-in** (2026-08-31, from plan-review round-2 Q1): the ghost is refused positions the player could not occupy. Interpretation (engineering, stated for review): in-game the player may stand ON a landed Flap (walkable, Bible §5), so the constraint is *the ghost must lie on the sheet's current footprint* (paper beneath it, never bare desk) — drags clamp to the footprint, toggling on finds the nearest footprint spot, and if a fold edit empties the region under it the ghost relocates to the nearest footprint point (mini-label notes it). The covers-player rule applies to **live actions** (new fold previews/commits, seam unfolds via `PlayerOnFlap`) and not to fold-list trial replays — in-game the player moves between folds, so refusing a replay against a static ghost would refuse sequences the game allows; list-edit guards stay geometric (`Overhangs`/`NothingToFold`).

## 4. Files

| File | Purpose |
| --- | --- |
| `Assets/Papercut/Editor/StudioFoldModel.cs` *(new)* | The editor's fold stack: ordered `List<Fold>`, replay through `SheetLayers.Apply`, evaluate/commit/unfold/crease-grab, preview fold + validity, ghost rect. No UnityEditor UI; unit-testable. |
| `Assets/Papercut/Editor/StudioFoldScene.cs` *(new)* | The fold pane's rendering rig: an editor preview scene holding one mesh per layer (built with `PolygonMeshBuilder`, textured from two full-sheet face RTs through each layer's inverse isometry), two face cameras aimed at the Prefab Stage, one composite camera aimed at the preview scene. Disposal-safe. |
| `Assets/Papercut/Editor/StudioFoldPane.cs` *(new)* | The pane: `PaneView` reuse (zoom/pan/frame), the drag gesture, seam/crease/ghost overlays, red-refuse feedback, ghost dragging. |
| `Assets/Papercut/Editor/SheetStudioWindow.cs` *(modified)* | Second-row layout; fold list UI (per-fold row: anchor, editable depth, remove; unfold-all; ghost toggle); fold state lifecycle (cleared on stage change); **Escape routing: an active fold/ghost drag claims Escape before the existing palette/link handling** (today `HandleKeyboard` eats every Escape before the panes see it — round-2 B1). |
| `Assets/Papercut/Tests/EditMode/StudioFoldModelTests.cs` *(new)* | Model tests (see §8). |

No runtime file changes. Cutting Phase 2 = deleting the three new files and the window's fold-row code.

## 5. Components & data

**`StudioFoldModel`** (plain class) — state: `folds` (ordered), `layers`/`effects` (replayed), `preview` (+valid flag), `ghost` (`Rect?`; null = rule off). Public surface:

- `Layers`, `DisplayLayers` (committed + preview replayed, mirroring `SheetFolds.Draw`'s ordering), `DisplayEffects`, `Folds`.
- `Evaluate(Fold) → FoldRejection` — `layers.Apply` outcome checks (`NothingToFold`, `Overhangs`) + `FoldValidity.IsValid(effect, ghostRect)` when the ghost is on. **No** `AlreadyFolded`/`OverlapsFold` checks (Aaron: allow everything).
- `SetPreview(Fold?)`, `TryCommit(Fold, minDepth) → FoldRejection` (TooShallow below the sheet's authored `minDepth`), `TryUnfoldAt(point, grabDistance)` via `FoldHitTest.SeamAt` (ghost as the player rect; a far-off-sheet rect when the ghost is off — `PlayerOnFlap` then never fires), `TryGrabCreaseAt` via `FoldHitTest.CreaseAt`, `TryRemoveAt(index, out reason)` (**guarded**: a trial replay without that fold must leave every remaining fold legal — no `Overhangs`/`NothingToFold`/covers-ghost — else the remove is refused and `reason` names the first dependent fold; Aaron 2026-08-31: refuse with reason. `MaxDepth` is stack-dependent, so an unguarded remove could leave a fold overhanging — a state the game can never produce), `Clear()`, `SetDepth(index, depth) → bool` (same trial-replay guard **plus the sheet's authored `minDepth` floor** — the field must not commit a depth the game would drop as TooShallow; round-2 N2 — returning the old value on refusal). List-edit guards are geometric only; the ghost rule applies to live actions (§3).
- `MaxDepth(anchor)` → `layers.MaxDepth` for drag clamping.

**`StudioFoldScene`** — owns: preview scene (`NewPreviewScene`), two face RTs sized from the renderer's `pixelsPerUnit` (capped at an editor const, 96 px/unit, to bound editor VRAM), two face cameras (`camera.scene` = stage scene, cull one `FoldLayers` layer each, full-sheet framing — same parameters as the runtime's face cameras), composite mesh objects (one per layer + a seam/crease overlay handled by the pane instead — see Behaviour 7), one composite camera (`camera.scene` = preview scene). `Render(stage, model, paneRect, view)`: render face RTs → rebuild layer meshes from `model.DisplayLayers` (UV = inverse isometry, `BackToFront` for back-up layers; tint from fold state using the renderer's authored tints) → render composite into the pane RT. `Dispose()` closes the preview scene and releases everything; called from window `OnDisable` and stage detach.

**`StudioFoldPane`** — same view mechanics as `StudioPane` (reuses `PaneView`; zoom/pan/frame). Input state machine: idle / dragging-fold (anchor + grabDepth) / dragging-ghost. Grab constants mirroring `FoldDragInput` defaults: corner 0.6, edge margin 0.35, seam 0.3, crease 0.3 (sheet units).

**Window additions** — fold row below the face panes (row split: faces 55% / fold row 45% of pane space; fold list column 240 px on the right). Fold list rows: `#, anchor name, depth (float field), ✕`; buttons: `Unfold all`, ghost toggle (`Test player`); a mini-label stating "preview ignores multiple-fold/stacking toggles; player rule via ghost only". Ghost size comes from `Player.prefab`'s `BoxCollider2D` **serialized `size`/`offset`** (asset load — never `Collider2D.bounds`, which is physics-populated and degenerate on an uninstantiated asset; a zero-size ghost would silently disable the covers-player rule). Fallback 0.5 × 0.5 with a warning if the prefab or collider is missing; a test asserts the ghost rect is non-degenerate. Ghost position: transient, starts at sheet centre.

## 6. Behaviour

1. The fold pane draws the sheet exactly as the game's composite would: base + landed flaps stacked bottom-to-top, Back content where flaps expose it (mirrored through the fold, Front-up-again where folded twice), desk colour where folds emptied regions. Content (art, elements) appears because it is in the face textures.
2. **New fold:** press within the edge margin / corner radius (same resolution rule as the game: nearest corner wins, then nearest edge), drag; depth = `FoldGeometry.DepthForDragPoint`, clamped to `MaxDepth` (never overhangs); preview live with the game's preview tint; red (`invalidTint`) when `Evaluate` refuses (covers ghost). Release commits unless refused or below the sheet's `minDepth`; right-click/Escape cancels.
3. **Fold further:** press near an existing crease grabs that fold line (grabDepth = that fold's depth), same as the game.
4. **Unfold:** press near a Seam unfolds that fold (respecting `CoveredByLaterFold` order and, with the ghost on, `PlayerOnFlap`). No swing-back animation in the editor (that is feel, not layout truth); the mini-label under the list shows the last refusal reason instead of a silent no-op.
5. **Fold list:** shows committed folds in order; editing a depth replays and refuses impossible values (message in the mini-label, value snaps back); ✕ removes a fold only when the trial replay without it leaves every remaining fold legal, else refuses and names the dependency (Aaron: refuse with reason); Unfold all clears.
6. **Ghost:** toggle on → rect at the sheet centre or, if that spot has no paper under it, the nearest footprint point; dragged by pressing inside it (takes priority over edge grabs only when the press is inside the rect and not within seam/crease distance), the drag clamping to the sheet's current footprint (§3 — never on bare desk); rendered as the player-ish outline; while dragging a fold, the covers-ghost rule live-tints. The ghost being on a landed flap refuses seam-unfolds under it (`PlayerOnFlap`), matching the game. If a fold edit empties the paper under the ghost, it relocates to the nearest footprint point and the mini-label says so.
7. **Creases and seams** are drawn as pane overlays (GUI lines over the composite), and both come from **desk-space** effect data so the drawn line and the clickable line can never disagree: crease lines from each replayed effect's desk-space `CreaseSegments` — the exact segments `FoldHitTest.CreaseAt` tests — never from `CreaseMark`s, which are in face-authored space (Back marks mirrored) and would draw in the wrong place. **Both crease and seam overlays draw only for unpinned folds** (`FoldHitTest` skips pinned folds for both, and a pinned fold's segments are stale — recorded against the stack at its apply time — so drawing them would offer dead, misplaced click targets; round-2 B2). The fidelity note records that pinned folds' creases are therefore not overlaid (the runtime shows them via face textures). Remembered creases: on an editor unfold the model captures that fold's desk-space `CreaseSegments` into a remembered list, drawn faint (the game leaves faint marks after unfolding — glossary; cleared by Unfold all/stage change). Colours/widths from the renderer's authored values. *Fidelity note:* the runtime draws creases as face content so later flaps cover and carry them; the overlay draws them on top, and remembered marks stay where they were at unfold time. Accepted for Phase 2 (visual-only, editor-only); revisit if it misleads.
7a. **Layer draw order and cap:** composite meshes take one z step per layer, bottom to top, exactly the runtime's scheme (`BaseZ`/`LayerZStep` equivalents), with the same style of cap: past the maximum representable layers, upper layers share a depth and a visible warning is shown in the pane (allow-everything makes deep stacks easy to build; they must not silently z-fight).
7b. **Emptied regions** show the pane's background colour — an editor constant, not the game's Desk surface; the pane's note names this so an emptied region isn't mistaken for authored content.
8. **Lifecycle / failure modes:** fold state clears on stage change (and the scene rig re-targets); everything disposes on window close and domain reload. Missing `RenderTextureFoldRenderer` on the sheet → defaults + help-box note. RT/preview-scene creation failure → the pane shows a help-box, never silently blank. `FoldLayers` invalid is already handled window-wide.
9. Nothing in Phase 2 writes to the prefab: committing a preview fold changes only editor state. Undo is deliberately **not** wired into preview folds (they are not asset edits; Ctrl+Z stays reserved for real content edits) — the fold list's ✕/Unfold-all are the undo.

## 7. Interfaces & seams

- The editor consumes only pure runtime pieces (`Fold`, `FoldAnchor`, `FoldGeometry`, `SheetLayers`, `FoldValidity`, `FoldHitTest`, `PolygonMeshBuilder`, `SheetGeometry`); no runtime file changes, no new runtime surface. If `SheetFolds.Evaluate` semantics change later, the editor's `Evaluate` is one small function to update — flagged by a comment cross-referencing it.
- **Phase 3 hook:** `StudioFoldModel.DisplayLayers` is exactly the data place-through-fold needs (topmost layer at a point → `ToDesk.Inverse` → face + authored position). The model is built now so Phase 3 only adds the placement path.
- Cutting: delete the three new files and the fold-row block in the window.

## 8. Testing

**Mechanical (Unity CLI):**

- `recompile` clean; full EditMode suite.
- `StudioFoldModelTests`: replay matches `SheetLayers` truth (an edge fold's layer count, back-up layer where expected); `Evaluate` refuses covers-ghost exactly when `FoldValidity.IsValid` does and allows overlapping folds regardless of toggle semantics; depth clamp at `MaxDepth` (no `Overhangs` from drags); `TryCommit` drops below-minDepth; `TryUnfoldAt` honours Seam distance, `CoveredByLaterFold`, and ghost-on-flap; `SetDepth` refuses impossible values and keeps state consistent; **`TryRemoveAt` refuses the stacked-then-remove case (deep second fold legal only on the stack — B1's scenario) and names the dependent; allows removal when the remainder is legal**; `Clear` replays correctly; ghost-off never yields `CoversPlayer`/`PlayerOnFlap`; **the ghost rect built from Player.prefab is non-degenerate (B2)**; remembered creases are captured on unfold and cleared on `Clear`.
- Live checks with the window open on `Sheet (0,0)`: commit an edge fold via the model API → fold pane RT non-uniform and *changed* from the flat render; `eval` the model's layer count; screenshot the window (flat vs folded) and describe what is actually visible — the flap must show mirrored Back content in the right place. **This check specifically validates the chained render requests** (two face renders then a composite sampling their RTs in one repaint — Phase 1 validated only a single request; if ordering is not synchronous the composite samples stale/blank textures and the folded screenshot will show it; round-2 N4).
- `git status`: no asset changes at all from using the preview.

**Aaron needs to play/use:** whether the preview matches what the game actually does on the same folds (fold something in the Studio, then fold the same in Play Mode and compare); whether the drag feel is close enough to the game's that previews are trustworthy; whether creases/seams-as-overlays mislead; whether the second-row layout and list/ghost interactions work in the hand.

## 9. Assumptions

1. Reading authored values (minDepth, tints, widths, pixelsPerUnit) from stage components via SerializedObject is the right "truth" source; missing components fall back to the runtime defaults with a visible note.
2. The composite camera + preview-scene approach reuses the Phase 1 URP `SingleCameraRequest` mechanism, already validated; meshes in a preview scene render the same way (they are ordinary renderers in a scene the camera targets).
3. Grab radii — and `depthSnap` (Bible decision #10 is open precisely for Aaron to playtest; if he snaps the game, the preview must snap too — round-2 N1) — mirror `FoldDragInput`: when a Desk scene is open, the pane reads the authored values from its `FoldDragInput` component (SerializedObject); the defaults (0.6/0.35/0.3/0.3, snap 0) are the fallback when no Desk scene is loaded.
3a. Fallback materials: if the sheet's renderer has no `faceMaterial`/`creaseMaterial` assigned, the scene rig creates its own URP/Unlit transparent materials (there is no meaningful "default Material"; unlit magenta is not a fallback — round-2 N5). Every SerializedObject field read (`minDepth`, tints, widths, materials, radii, `depthSnap`) is name-coupled, so a test asserts each `FindProperty` name resolves on the real components (a rename must fail a test, not silently degrade to defaults).
4. `PolygonMeshBuilder`/`SheetLayers` allocation per rebuild is fine at editor interaction rates (rebuild only when the model changes or a drag updates, not every repaint).
5. Preview shows authored content folded via textures; it does **not** simulate object behaviour (blocks riding flaps, plate presses, occlusion clipping of colliders). That is Play Mode's job; the pane states it.

## 10. Open questions

None — layout, rule-scope, and ghost were asked and answered pre-plan; fold-removal semantics asked and answered after review round 1 (§3).

## Review

**Round 1 (plan-reviewer, 2026-08-31): verdict BLOCK.** Dispositions — all accepted:

- **B1 (blocking, accepted):** free fold-list removal could leave a replayed stack the geometry forbids (stack-dependent `MaxDepth` means a dependent fold can overhang once its support is removed), contradicting the no-overhang rule. Fixed: `TryRemoveAt` trial-replays and refuses with the dependency named — the design choice escalated to Aaron (**Q1 → refuse with reason**). Test added for the stacked-then-remove case.
- **B2 (blocking, accepted):** ghost size from `Collider2D.bounds` on a prefab asset is degenerate (physics-populated), silently disabling the covers-player rule. Fixed: read the `BoxCollider2D`'s serialized `size`/`offset`; non-degeneracy tested.
- **B3 (blocking, accepted):** crease overlay was sourced from face-space `CreaseMark`s (Back mirrored, stale under stacking) while the hit test uses desk-space `CreaseSegments` — drawn line and clickable line could disagree. Fixed: overlays draw from the same desk-space `CreaseSegments` the hit test uses.
- **N1 (accepted):** remembered creases added to the model (desk-space segments captured at unfold, drawn faint), matching the game's persists-after-unfold behaviour; divergences noted.
- **N2 (accepted):** composite layer meshes get the runtime's z-step ordering scheme and a visible layer-count cap warning.
- **N3 (accepted):** seam borders drawn only for unpinned folds (no dead click targets from stale pinned seams).
- **N4 (accepted):** grab radii read from the open Desk scene's `FoldDragInput` when available; defaults as fallback.
- **N5 (accepted):** emptied-region background named as an editor constant in the pane note.

**Round 2 (plan-reviewer, 2026-08-31): verdict BLOCK.** Two rounds is the workflow cap; every finding was accepted (nothing contested), folded into the plan above, and coding proceeds:

- **B1 (accepted):** the window's existing `HandleKeyboard` eats every Escape before the panes see it, killing the drag-cancel. Plan now names the routing change: an active fold/ghost drag claims Escape first.
- **B2 (accepted):** crease overlays restricted to unpinned folds (same dead-target/stale argument as round-1 N3 for seams — `FoldHitTest.CreaseAt` skips pinned folds); pinned-crease omission recorded in the fidelity note; stacked-fold overlay-source test added.
- **N1 (accepted):** `depthSnap` read from `FoldDragInput` alongside the grab radii.
- **N2 (accepted):** `SetDepth` also refuses below the authored `minDepth`.
- **N3 → Q2 escalated → Aaron:** ghost is a **faithful stand-in** — footprint-constrained placement, live-action-only covers rule; interpretation recorded in §3.
- **N4 (accepted):** the chained-render validation is named as the specific target of the first live check.
- **N5 (accepted):** fallback materials specified; `FindProperty` name-resolution test added.

## Code review

**Round 1 (code-reviewer, 2026-08-31): verdict REJECT** (2 must-fix, 9 should-fix, 2 questions). Dispositions — all accepted:

- **M1 (fixed):** middle-mouse during a fold drag left the preview stranded in the model (phantom flap, uncancellable). Pan/right-click presses now cancel the drag properly.
- **M2 (fixed):** a list-edit refusal message never cleared and shadowed later pane messages. Successful list edits now clear it (or report a ghost relocation — also S4).
- **S1 (fixed):** face cameras get `camera.scene` set inside `Render` every submit, so the first composite can never sample the main scenes.
- **S2 (fixed):** missing `SheetFolds`/`RenderTextureFoldRenderer` now surfaces as a visible warning (`StudioFoldSettings.MissingNote`), never a silent default.
- **S3 (fixed):** the fold rig disposes on stage change, not just window close.
- **S4 (fixed):** list edits report ghost relocation (the only path that can cause one).
- **S5 (fixed):** the pane note now states the editor background and the no-object-simulation limits.
- **S6 (fixed):** right-click cancels ghost drags too; release commits the fold at the cursor's release position, like the game.
- **S7 (fixed):** the sheet's authored `rememberCreases` toggle is read and honoured (+ name test).
- **S8 (accepted, comments):** the `TryResolveAnchor` copy and the defaults table now carry explicit mirror-of-runtime cross-references; a value-level tie to the runtime is impossible without instantiating the Desk component (noted limitation).
- **S9 (fixed):** UI wording uses "Sheet", not "paper".
- **Q1 escalated → Aaron: whole-rect ghost clamp.** Each footprint polygon is eroded by the ghost's half-size (exact per convex polygon; conservative across polygon boundaries — noted); fallback to centre-clamp only if the ghost fits nowhere.
- **Q2 escalated → Aaron: pinned folds refuse ✕**, exactly mirroring the game's unfold ordering; the geometric trial-replay guard remains as a safety net.
- Also corrected from round 1: the model test count is 15 (16 after the fix round), not the 18 previously reported.

After fixes: recompile clean; **218/218** EditMode tests pass.

**Round 2 (code-reviewer, 2026-08-31): verdict REJECT** — round-1 fixes all verified real (including the erosion math, winding-aware and exact), but the M2 message fix was only half-done. Dispositions — all accepted and fixed:

- **M1-r2 (fixed):** the two message channels (list edits vs pane) had no recency ordering — a stale refusal could shadow a newer one in either direction. The pane now reports through the window's single message slot (`StudioFoldPane.Report`); the newest message always wins, and a new press clears it.
- **S1-r2 (fixed):** layer meshes refresh their material every rebuild, so re-assigning `faceMaterial` on the sheet's renderer takes effect live.
- **S2-r2 (fixed):** the pinned-✕ refusal names the pinning fold ("Fold 1 is pinned by fold 2; remove that first").
- **S3-r2 (fixed):** the ghost-relocation test asserts the whole rect fits (`xMax` against the eroded footprint), so a centre-clamp regression fails.
- **S4-r2 (fixed):** "paper" removed from comments/test names (glossary).
- **S5-r2 (fixed):** middle-click cancel clears the message like right-click.
- **S6-r2 (fixed):** this section's test count corrected to 16.
- Observation (accepted, no change): cancelling a ghost drag keeps the ghost where it was dragged to (moves commit live); noted as intended.

After round-2 fixes: recompile clean; **218/218** EditMode tests pass.

**Post-phase change (Aaron, 2026-08-31, lightweight):** crease lines (live and remembered) are no longer drawn in the fold preview — Aaron doesn't need them for level editing. The crease-grab gesture (fold further) is unchanged; seam overlays stay. The model still tracks remembered creases (honouring the sheet's toggle) in case the visuals return.
