# Sheet Studio — Phase 1: face panes, palette, placement

## 1. Task

Build the first phase of the level editor Aaron asked for: a Unity **Editor** tool (an `EditorWindow`, "Sheet Studio") for creating and editing Sheets — both faces visible side by side, an element palette, click-to-place / drag-to-move / resize / delete with full Undo, a background-art slot per face, and creation of new Sheet prefab variants placed onto the Desk.

**Explicitly out of scope for this phase** (later phases, already agreed with Aaron): fold preview in the editor (Phase 2), placing elements through a previewed fold and the x-ray see-through-to-Back view (Phase 3), any in-game/runtime editor, any changes to runtime behaviour.

## 2. Design references

- **Bible §3 (spatial model, [LOCKED] not tile-based):** free placement; the Studio places at free positions. An *optional* position-snap increment is provided as an editor convenience only (default 0 = off), mirroring the `FoldDragInput.depthSnap` precedent. No grid data structures.
- **Bible §6 (back authoring convention, [DECIDED 2026-08-24]):** Back content is authored flat in Back-space under the sheet's `Back` root. The Back pane displays exactly this authored space (the sheet as physically turned over about its vertical edge). The note "revisit when the level editor exists" is resolved by *keeping* the convention — the side-by-side flat view falls straight out of it. No change to `SheetGeometry.BackToFront`.
- **Bible §7 (sheets/desk):** sheets are prefab variants placed under the Desk's `SheetGrid` with a `gridPosition`; grid dimensions are open (decision #14), so the Studio accepts any grid position and only refuses a *duplicate* one.
- **Bible §9 ([TENTATIVE] elements):** the Studio does not add or commit any element; the palette is discovered from the prefabs that already exist (`Prefabs/Terrain`, `Prefabs/Objects`). Cutting an element later automatically removes it from the palette.
- **Bible §1 (WebGL [TENTATIVE]):** everything here is editor-only code in an editor-only assembly; nothing ships in any build.
- **implementation-guidelines §4a (Inspector tunables):** this phase adds no runtime behaviour, so no game-feel tunables. Editor-ergonomics values (zoom limits, handle sizes, colors of overlays) are editor constants, not game design values; the position-snap increment and overlay toggles are window toolbar controls persisted via `EditorPrefs`.
- **Glossary:** all names use Sheet / Front / Back / Desk vocabulary.

## 3. Decisions already made by Aaron

- Level editor is a **Unity Editor tool**, not an in-game editor (2026-08-31, chose "Unity Editor tool" from the three options).
- Full coding workflow, **one cycle per phase**; this plan is Phase 1 of the agreed three phases (1: studio window + panes + palette + placement; 2: fold preview; 3: place-through-fold + x-ray).
- **"Create + add to Desk"** (2026-08-31): a New Sheet button creates a fresh Sheet prefab variant *and* a helper adds it to the Desk scene at a grid position Aaron picks.
- **"Simple art slot"** (2026-08-31): each pane gets a sprite field for that face's background art; the Studio creates/updates the SpriteRenderer at the right z and sorting order.
- **Back pane mirror toggle** (2026-08-31, from plan-review Q1): default Back pane view is authored Back-space; a toolbar toggle mirrors the Back pane *display only* so the two panes align spatially. Authored data never changes.
- **Plates are resizable** (2026-08-31, from plan-review Q2): pressure-plate trigger area is a per-puzzle design value, dragged out in the Studio like terrain.

## 4. Files

All new files; no runtime file is modified.

| File | Purpose |
| --- | --- |
| `Assets/Papercut/Editor/Papercut.Editor.asmdef` | Editor-only assembly; references `Papercut`. |
| `Assets/Papercut/Editor/SheetStudioWindow.cs` | The window: menu item, toolbar, layout of the two panes + palette, sheet open/create UI, event routing. |
| `Assets/Papercut/Editor/StudioPane.cs` | One face pane: owns a preview camera, pan/zoom state, renders the face to a `RenderTexture`, draws overlays, translates mouse events into placement-op calls. |
| `Assets/Papercut/Editor/PaneView.cs` | **Pure** coordinate mapping (no UnityEditor state): pane pixel rect + zoom + pan ↔ sheet-local position. Unit-testable. |
| `Assets/Papercut/Editor/StudioPalette.cs` | Discovers placeable prefabs from `Assets/Papercut/Prefabs/Terrain` and `.../Objects`; draws the palette strip; tracks the armed prefab. |
| `Assets/Papercut/Editor/StudioPlacement.cs` | The editing operations, all through `Undo`: place, move, resize (BoxCollider2D + tiled-sprite sync), duplicate, delete, guard rails (only under face roots, never the Surface/roots themselves). |
| `Assets/Papercut/Editor/StudioSheetOps.cs` | Sheet-level operations: enumerate existing sheet variants, create a new variant, add it to the open Desk scene, face-layer normalization, the art-slot logic. |
| `Assets/Papercut/Tests/EditMode/Papercut.Tests.EditMode.asmdef` | *Modified*: add `"Papercut.Editor"` to `references` (assembly is already Editor-only). |
| `Assets/Papercut/Prefabs/Sheet.prefab`, `Assets/Papercut/Sheets/Sheet (*).prefab` | *Modified (one-time layer migration)*: face content moves from layer Default to `SheetFront`/`SheetBack`, matching what `Sheet.Awake` forces at runtime. |
| `Assets/Papercut/Tests/EditMode/PaneViewTests.cs` | Tests for the coordinate mapping. |
| `Assets/Papercut/Tests/EditMode/StudioSheetOpsTests.cs` | Tests for variant creation, art slot, layer normalization (using temp assets, cleaned up). |
| `Assets/Papercut/Tests/EditMode/StudioPlacementTests.cs` | Tests for placement/guard-rail logic on prefab contents loaded in memory. |

## 5. Components & data

No runtime components. Editor classes:

**`SheetStudioWindow : EditorWindow`** — opened via menu `Papercut/Sheet Studio` (and a double-entry point: a button in the window opens a chosen sheet in Prefab Mode). State: the two `StudioPane`s, the `StudioPalette`, toolbar values. Toolbar controls (persisted with `EditorPrefs`, prefixed `Papercut.SheetStudio.`):

- `snapIncrement` (float, default 0 = free placement) — optional editor placement snap; **not** a game value.
- `showColliderOverlays` (bool, default true) — draw collider outlines + labels.
- `showLabels` (bool, default true).
- `mirrorBackPane` (bool, default false) — display-only mirror of the Back pane so it aligns spatially with the Front pane. Authored data is untouched; `PaneView` carries the flag so rendering and input mapping cannot disagree.

**`StudioPane`** — one per face. Owns a hidden editor-only `Camera` GameObject (`HideFlags.HideAndDontSave`), orthographic, `camera.scene` set to the open Prefab Stage's preview scene, `cullingMask` set to exactly one of `FoldLayers.Front` / `FoldLayers.Back`, rendering into a `RenderTexture` sized to the pane. **Render mechanism (this project is URP, where `Camera.Render()` is unsupported):** each repaint submits a `UniversalRenderPipeline.SingleCameraRequest` via `RenderPipeline.SubmitRenderRequest`; this is validated first, before the rest of the pane is built on it, with `PreviewRenderUtility` as the fallback if it fails in practice. Pan with middle-drag, zoom with scroll wheel (clamped); default framing shows the whole 11 × 8.5 sheet plus a margin. Draws, over the rendered texture: the sheet outline, every `Collider2D` outline under its face root (with the object's name when labels are on), and a highlight on the current `Selection` object if it belongs to this face.

**`PaneView` (struct, pure)** — `(Rect paneRect, Vector2 panOffset, float zoom, bool mirrorX)` with `SheetLocalToPane(Vector2)`, `PaneToSheetLocal(Vector2)`, `FitSheet(Rect paneRect)`. `mirrorX` implements the Back pane's display-only mirror (and the pane camera renders through the same flag via a flipped projection). The single place pixel↔sheet math lives; both rendering and input go through it so they cannot disagree.

**`StudioPalette`** — `Refresh()` finds every `.prefab` asset under the two element folders via `AssetDatabase.FindAssets("t:Prefab", …)`; draws them as a horizontal strip of buttons (asset preview thumbnail + name); one may be *armed*. Escape or right-click disarms.

**`StudioPlacement` (static)** — operations, each a single Undo step:

- `Place(prefab, faceRoot, sheetLocalPos, snap)` → `PrefabUtility.InstantiatePrefab` under the face root; **sets local x/y only and preserves the prefab root's authored z** (the element z convention lives on prefab roots: Wall/Water/Gate/plates author z −0.05, Block z 0 — forcing z = 0 would put sprites coplanar with the transparent Surface quad and break rendering). Layer is set **recursively** to that face's `FoldLayers` layer (Block has a `Visual` child; mirror `Sheet.SetLayerRecursively`). `Undo.RegisterCreatedObjectUndo`.
- `Move(gameObject, newSheetLocalPos, snap)` → `Undo.RecordObject(transform)`; changes x/y only, z preserved.
- `Resize(gameObject, newRect)` → **only for the explicit resizable set: objects with a `TerrainRegion` (Wall, Water, Gate) or a `PressurePlate` (Aaron: plates are resizable, 2026-08-31)**. Records the root `BoxCollider2D` (+ a same-GameObject `SpriteRenderer` in Tiled draw mode, whose `size` is kept equal to the collider's — the Wall/Water/Hold Plate pattern) and applies size + offset. Everything else — notably `PushableBlock`, whose root `BoxCollider2D` footprint must stay in sync with its `PolygonCollider2D` pusher shape and fixed Visual — gets no handles. The discriminator is the component set, never "has a BoxCollider2D" (Block and both plates have one on their root).
- `Duplicate(gameObject)` (Ctrl+D), `Delete(gameObject)` (Delete key) → `Undo.DestroyObjectImmediate`.
- `CanEdit(gameObject, sheet)` guard: object must be a descendant of `Front` or `Back`, and not the face root itself, not the `Surface` quad, and not the face's background-art object (identified by convention, see `SetFaceArt` — the art slot owns it). Everything else in the panes is inert.
- Hit-testing for selection is **manual geometry**: iterate `GetComponentsInChildren<Collider2D>` under the face root and test the point against each collider's **serialized shape data** (`size`/`offset`/points, transformed by `localToWorldMatrix`), topmost/smallest first. Never `Physics2D` queries and never `Collider2D.bounds` — in a Prefab Stage preview scene the physics world hasn't registered the colliders, so both can be stale or empty.

**`StudioSheetOps` (static)** —

- `FindSheetAssets()` → the variants in `Assets/Papercut/Sheets`.
- `CreateSheet(gridPosition, destinationFolder = "Assets/Papercut/Sheets")` (folder parameterized so tests write under a temp folder) → if the open Desk already has a sheet at that position, refuses (dialog). The duplicate check enumerates `SheetGrid`'s children with `GetComponentsInChildren<Sheet>` directly — never `Desk.TryGetSheet`, whose registry is built once and never invalidated in edit mode. If a variant named `Sheet (x,y)` already exists but is *not* on the Desk, offers to **add the existing variant to the Desk** instead of refusing (recovers cleanly from an undone add, since `SaveAsPrefabAsset` is not undoable). Otherwise instantiates the base `Assets/Papercut/Prefabs/Sheet.prefab`, saves it as a **variant** via `PrefabUtility.SaveAsPrefabAsset` to `Assets/Papercut/Sheets/Sheet (x,y).prefab`, then, if a `Desk` exists in an open scene, instantiates the variant under `Desk.SheetGrid`, sets `gridPosition` via `SerializedObject`, registers Undo, marks the scene dirty (Sheet.OnValidate then snaps it to `GridToLocal`). If no Desk scene is open, it says so and skips that half (the variant still exists). After creation the new sheet is **opened in Prefab Mode** so the Studio lands on it — the natural next step.
- `NormalizeFaceLayers(sheet)` → sets every object under `Front`/`Back` to the matching `FoldLayers` layer (the same thing `Sheet.Awake` does at runtime), so the per-face cull masks work in the editor. Run automatically when the Studio attaches to a stage; changes go through `Undo`, and it is a no-op when layers are already right. **One-time migration, part of this change:** the base Sheet prefab and the four existing variants currently store `m_Layer: 0` on face content, so they are normalized once and those asset changes land with this work — otherwise merely opening a sheet with Auto Save on would silently rewrite it. Safe because `Sheet.Awake` already forces these exact layers at runtime. The migration is performed **through the editor** (load each asset, run `NormalizeFaceLayers`, save via `PrefabUtility`), never by hand-editing YAML — variant face content is nested `PrefabInstance` blocks, and layers land as `m_Layer` modification overrides on derived fileIDs, exactly the minefield the project memory warns about. A large `m_Modifications` diff on the variants is the *expected* git-status result. Known cosmetic consequence to tell Aaron about: after migration, face content no longer draws in the edit-mode **Game view** (the main camera excludes these layers and the fold renderer composites only at runtime); the Scene view and the Studio panes are unaffected.
- `SetFaceArt(faceRoot, sprite)` / `ClearFaceArt(faceRoot)` → the face's art object is identified **by convention, not by name**: a `SpriteRenderer` child directly under the face root at z −0.01 with `sortingOrder 0` (the existing hand-authored art is named `Map 1`, so a name lookup would create a duplicate renderer z-fighting the real one). `SetFaceArt` adopts the existing object if one matches (z compared with an epsilon — serialized floats), else creates a child named `Art`: local position `(0, 0, -0.01)`, `sortingOrder 0`, the URP sprite-unlit material (z sits between the Surface and elements at −0.05, per the established face-content convention; note the Front Surface sits at z 0 but the Back Surface at +0.01 in the base prefab). Clearing deletes the adopted/created object. If several children match the convention, the slot shows the first and warns — that's authored ambiguity worth surfacing, not hiding. Undo-registered.

## 6. Behaviour

1. Aaron opens **Papercut → Sheet Studio**. If no Sheet prefab is open in Prefab Mode, the window lists the sheets in `Assets/Papercut/Sheets` as buttons (open in Prefab Mode via `PrefabStageUtility.OpenPrefab`) plus a **New Sheet…** field (grid x,y + Create).
2. When a Prefab Stage containing a `Sheet` is open, the window shows: toolbar, Front pane (left), Back pane (right, in authored Back-space), palette strip, and per-pane art-slot `ObjectField`s. The Studio subscribes to `PrefabStage.prefabStageOpened/prefabStageClosing`, `Undo.undoRedoPerformed`, and `EditorApplication.update` for repaint; cameras and RTs are torn down on close/domain reload (`OnDisable`).
3. On attach, `NormalizeFaceLayers` runs. If the `SheetFront`/`SheetBack` layers are missing from Tags & Layers (`FoldLayers.Valid` false), the window shows an error help-box naming the fix and disables the panes — no silent failure. The help-box states that a script recompile/domain reload is needed after adding the layers, because `FoldLayers` caches its resolve in statics.
4. **Placing:** click a palette item to arm it; the cursor in either pane shows a ghost preview of the armed prefab's footprint; left-click instantiates it under that pane's face root at the clicked sheet-local position (snapped if snap > 0). The element is immediately selected. Arming stays on for repeat placement; Escape/right-click disarms.
5. **Selecting / moving:** with nothing armed, left-click hit-tests colliders under the pane's face root (manual geometry per §5 — never Physics2D; topmost, smallest first so nested/overlapping things are pickable) and sets `Selection.activeGameObject` — the normal Unity Inspector is the property editor (abilities, plate wiring, etc.; no duplicated inspector UI). Dragging a selected object moves it. Arrow keys nudge by the snap increment (or 0.1 when snap is 0).
6. **Resizing:** a selected object in the resizable set (`TerrainRegion` or `PressurePlate`, per §5) shows corner + edge handles; dragging updates collider size/offset and any same-object tiled `SpriteRenderer` size. Blocks never show handles.
7. **Prefab stage without a `Sheet`** (someone opened e.g. the Block prefab): the window says this isn't a sheet and offers the sheet list. A `Sheet` with missing `Front`/`Back` roots: error help-box, panes disabled.
8. **Saving is Unity's own prefab-mode save** (Ctrl+S / Auto Save). The Studio never writes the asset itself except through ordinary scene-object edits, so there is no custom serialization and no way to lose work Unity's dirty-tracking doesn't know about.
9. Every operation is a single named Undo step ("Place Wall", "Move Block", "Resize Water", "Set Front art"…).
10. Failure modes: base Sheet prefab missing → error dialog, nothing created. Element folders missing/empty → palette shows "no element prefabs found" with the folder paths. RenderTexture creation failure (headless) → panes show a help-box instead of drawing. `camera.scene` unassignable (stage closed mid-frame) → pane skips the render that frame.

## 7. Interfaces & seams

- **Everything is editor-only** in `Papercut.Editor`; the game does not reference it. Cutting the entire editor = deleting `Assets/Papercut/Editor` (and the test files). No runtime file changes in this phase.
- `PaneView` is the seam Phase 2/3 build on: the fold-preview pane will reuse it, and place-through-fold will extend "pane point → face root + authored position" with the fold mapping. Kept pure for that reason.
- The Studio *reads* runtime conventions from their single sources (`SheetGeometry` for dimensions and Back-space, `FoldLayers` for layers) rather than duplicating constants. The face-content z/sorting convention (Surface 0, art −0.01, elements −0.05) currently lives only in authored assets and memory; the art-slot constants are defined once in `StudioSheetOps` with a comment naming the convention.

## 8. Testing

**Mechanical (I run these via the Unity CLI):**

- `recompile` → no errors.
- `run_tests {"mode":"editor"}` — existing suite plus the new tests:
  - `PaneViewTests`: round-trip pane↔sheet-local at several zooms/pans; `FitSheet` shows the whole sheet; y-axis orientation (GUI y-down vs world y-up) is correct — a mapping bug here misplaces every element, so this is tested exhaustively.
  - `StudioSheetOpsTests`: `CreateSheet` produces a variant whose base is the Sheet prefab, with `Sheet`, `SheetFolds`, `IFoldRenderer` present and correct name; refuses a duplicate grid position; `SetFaceArt` creates the renderer at z −0.01/order 0 with the unlit material and `ClearFaceArt` removes it; **`SetFaceArt` against a copy of a real variant with existing `Map 1` art adopts it rather than creating a second renderer**; `NormalizeFaceLayers` sets layers (including children, e.g. Block's Visual) and is a no-op (not dirtying) the second time. Temp assets under `Assets/Temp_StudioTests`, deleted in teardown.
  - `StudioPlacementTests`: `Place` parents under the right root at the right x/y **and preserves the prefab root's authored z (a placed Wall keeps z −0.05, a placed Block z 0)** with the right layer **on every child**; `CanEdit` refuses the roots, Surface, and the adopted art object; **resize eligibility per element prefab: Wall/Water/Gate/plates yes, Block no**; `Resize` syncs a tiled sprite; snap rounds correctly and snap 0 doesn't.
- Window smoke test through the CLI: open the Studio via `eval` (`EditorWindow.GetWindow`), open `Sheet (0,0)` in a prefab stage, place a Wall via the ops API, verify parent/layer/position via `eval`, undo it, close. `get_console_logs` clean.
- **Pane-render check (the highest-risk mechanism):** with `Sheet (0,0)` open, read back a few pixels of each pane's RenderTexture via `eval` and assert they are non-uniform (face content actually drew). A blank pane must fail mechanically, not wait for Aaron to notice.
- `python Tools/check_links.py` after the layer migration and any test that touched assets; `git status` check that nothing *beyond* the intended migration (base Sheet prefab + variants' layer values) was dirtied.

**Aaron needs to play/use (mechanical checks cannot judge):**

- Whether the panes read clearly — art visible, collider overlays legible, Back-space orientation intuitive to *him*.
- Whether placement, dragging, resizing, zoom/pan feel right in the hand.
- Whether the palette-arm/click-place flow matches how he wants to lay out a level.
- Creating a real new sheet and authoring it end to end, then playing it in the game.

## 9. Assumptions

Engineering assumptions only (design questions were asked and answered — §3):

1. A `HideAndDontSave` camera with `camera.scene` set to the Prefab Stage's preview scene, driven by `RenderPipeline.SubmitRenderRequest` + `UniversalRenderPipeline.SingleCameraRequest`, renders stage contents into a RenderTexture under URP. Validated first, before the rest of the pane is built on it (review round 2, N1); if a Unity 6.3 quirk blocks it, the fallback is rendering the loaded contents with the same cameras in a `PreviewRenderUtility` scene — the seam is inside `StudioPane` either way.
2. Authoring-time layer assignment (`NormalizeFaceLayers`) is safe because `Sheet.Awake` already forces exactly these layers at runtime; edit-time state converges to runtime state, changing nothing observable in the game.
3. The palette needs no curation config: everything in the two folders is placeable. If a prefab later shouldn't be, moving it out of those folders is the curation.
4. New sheets always start from the base `Sheet.prefab` as a variant, matching the four existing hand-authored variants.
5. The existing EditMode test assembly can reference `Papercut.Editor` safely because it is already Editor-only.
6. The element z convention lives on prefab **roots** (Wall/Water/Gate/plates at −0.05, Block at 0); `Place` therefore preserves each prefab's authored root z and only ever sets x/y. (Corrected by plan review B1 — the original assumption that children carried the z was wrong.)

## 10. Open questions

None — the two scope questions (sheet creation reach, art slot) were asked and answered before planning; the two review questions (Back-pane mirror, plate resizing) were asked and answered after review round 1.

## Review

**Round 1 (plan-reviewer, 2026-08-31): verdict BLOCK.** Dispositions:

- **B1 (blocking, accepted):** `Place` forced root z = 0, but the z convention is authored on element prefab roots (−0.05 for Wall/Water/Gate/plates); a placed element would z-fight the Surface quad. Fixed: `Place`/`Move` set x/y only, preserve authored z; tests assert it.
- **B2 (blocking, accepted):** "resizable = has root `BoxCollider2D`" wrongly matched Block and plates. Fixed: explicit resizable set — `TerrainRegion` or `PressurePlate` (plates confirmed resizable by Aaron); Block never. Eligibility tested per prefab.
- **B3 (blocking, accepted):** art-slot lookup by child name `Art` missed the existing `Map 1` art objects and would create a duplicate renderer. Fixed: art object identified by convention (SpriteRenderer child at z −0.01, order 0, directly under the face root), adopted if present; multiple matches warn.
- **N1 (accepted):** `Place` sets the face layer recursively (Block's Visual child).
- **N2 (accepted):** layer normalization is a stated one-time migration of the base prefab + variants, landed with this change; the git-status verification expects exactly that and nothing more.
- **N3 (accepted):** `CreateSheet` offers to add an existing off-Desk variant rather than refusing; the created sheet opens in Prefab Mode.
- **N4 (accepted):** hit-testing is manual geometry over `GetComponentsInChildren<Collider2D>`, never Physics2D queries (they run in the wrong physics world).
- **N5 (accepted):** tests extended to cover B1/B2/B3 and recursive layers.
- **N6 (accepted):** missing-layers help-box says a recompile/reload is needed (`FoldLayers` caches statically).
- **Q1 escalated → Aaron:** Back pane gets a display-only mirror toggle (default off).
- **Q2 escalated → Aaron:** plates are resizable.

**Round 2 (plan-reviewer, 2026-08-31): verdict APPROVE WITH CHANGES.** No blockers, no design questions; all eight engineering findings accepted and folded in:

- **N1:** pane render mechanism specified — URP `SubmitRenderRequest`/`SingleCameraRequest` (Camera.Render is unsupported under URP); validated before the pane is built on it; `PreviewRenderUtility` as fallback.
- **N2:** mechanical pane-render check added (RT pixel readback must be non-uniform).
- **N3:** hit-tests use serialized collider shape data, never `.bounds` (stale in preview scenes).
- **N4:** layer migration runs through the editor, never hand-YAML; large `m_Modifications` diff is expected.
- **N5:** noted for Aaron: after migration, face content stops drawing in the edit-mode Game view (expected; Scene view and Studio unaffected).
- **N6:** duplicate-position check enumerates SheetGrid children directly, not `Desk.TryGetSheet`'s stale registry.
- **N7:** `CreateSheet` destination folder parameterized for tests.
- **N8:** art-convention z match uses an epsilon; Back Surface z +0.01 documented.

## Deviations

Engineering deviations from the plan as written, discovered while coding/reviewing:

1. **Repaint subscription (§6.2):** instead of `EditorApplication.update` (a constant-repaint hammer), the window subscribes to `Undo.undoRedoPerformed`, `EditorApplication.hierarchyChanged`, `ObjectChangeEvents.changesPublished` (catches property-only Inspector edits), and the prefab-stage events. Same guarantee (panes never stale), no idle repainting.
2. **Layer normalization is deliberately NOT undoable** (plan said "changes go through Undo"): if normalization sat on the undo stack, Ctrl+Z right after opening a sheet would revert content to layer Default and the panes would silently stop rendering it (code review S8). It marks objects dirty directly; the migration semantics are unchanged.
3. **`CreateSheet` builds its temp instance in a preview scene**, not the active scene, so the Desk scene's dirty flag is never set by a create (code review S7).
4. **`CreateSheet` takes the Desk as a parameter** (window passes `FindOpenDesk()`); lets tests control desk presence without touching real scenes.
5. The `PreviewRenderUtility` fallback for pane rendering was not built: the primary URP `SingleCameraRequest` path was spike-validated first and works (the seam for a future swap remains inside `StudioPane.Render`).

## Code review

**Round 1 (code-reviewer, 2026-08-31): verdict REJECT** (1 must-fix, 8 should-fix, 1 tree-hygiene note). Dispositions — all accepted:

- **M1 (fixed):** `HandleKeyboard` stole Delete/Backspace/arrows from the window's own text fields (editing the Snap value could delete the selected element). Guarded with `EditorGUIUtility.editingTextField`.
- **S1 (fixed):** a pane-placed element is now immediately selected, as §6.4 promised.
- **S2 (fixed):** `ObjectChangeEvents.changesPublished` subscription added so property-only edits repaint the panes (recorded as Deviation 1).
- **S3 (fixed/verified):** blit orientation verified visually — screenshot of the live window shows overlays and labels aligned with the rendered Map 1 art (top walls on top art, water boxes on the drawn pond). No flip.
- **S4 (fixed):** adoption test now also runs against a copy of the real `Sheet (0,0)` variant with its authored `Map 1` art.
- **S5 (fixed):** `Duplicate` records the copy's transform before offsetting (redo kept the offset); `Unsupported` API choice documented.
- **S6 (fixed):** gated-terrain overlay color cached per region, invalidated on change events (was a `SerializedObject` per region per repaint).
- **S7 (fixed):** create-sheet temp instance in a preview scene (Deviation 3).
- **S8 (fixed):** layer normalization made non-undoable (Deviation 2).
- **S9 (noted, no code change):** the working tree also holds unrelated uncommitted work (`Water.prefab` requiredAbility −1→1 and the Map 1 terrain re-authoring from `2026-08-31-map1-terrain.md`); commit that work separately from this task so the layer-migration diff stays auditable. Relayed to Aaron in the report.

After fixes: recompile clean; **198/198** EditMode tests pass (the fix round added one test).

**Round 2 (code-reviewer, 2026-08-31): verdict APPROVE WITH FIXES.** All round-1 fixes verified real and correct (including a live probe confirming the pane camera's own mutations cannot feed back into `ObjectChangeEvents` and defeat the S6 cache). Two minors, both accepted:

- **S1-r2 (relayed to Aaron):** `Assets/_Recovery/` (Unity's crash-rescue folder from the editor crash during verification) is untracked inside Assets/ — inspect/remove it, and keep it and the other unrelated dirty files out of the sheet-studio commit.
- **S2-r2 (fixed):** with an art-slot ObjectField keyboard-focused, Delete deleted the selected element instead of clearing the field. Fixed: element keys are skipped while any control holds keyboard focus, and clicking a pane clears keyboard focus (matching Scene view behaviour). Re-verified: recompile clean, 198/198 tests pass.
