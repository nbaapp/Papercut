# Polygon terrain regions (Sheet Studio + fold plumbing)

Date: 2026-09-10

## 1. Task

Aaron: *"So right now, the walls and terrain and stuff can only be boxes in the level editor, which doesn't always fit with my terrain shape easily."* Of the options presented he chose option 3: **any simple polygon, decomposed into convex pieces**, so a terrain region can be drawn to follow the map art. Polygon regions live **alongside** the existing box regions as separate prefabs (his answer to the data-model question, below).

Deliverables:

1. Runtime: a `TerrainRegion` may carry a `PolygonCollider2D` (one path, any simple polygon, concave allowed) instead of a `BoxCollider2D`. Folding clips it exactly like a box; the arrival room test sees its true shape.
2. Studio: two new palette prefabs, `Wall (Polygon)` and `Water (Polygon)`. Arming one starts a click-to-draw outline in a face pane; selecting a placed one gives vertex handles (drag, insert on an edge, remove).
3. The Bible §3 terrain entry records the change.

**Out of scope:** migrating any existing box region; deriving collision from the art (option 4); rotated boxes; polygon shapes for plates, blocks, or the Gate; multi-path polygon colliders; changing how folds render.

## 2. Design references

- Design Doc *Mechanics → Puzzles → Elements*: traversable / untraversable terrain. *Art*: the art is drawn outside Unity; collision follows it.
- Bible §3 *Spatial model*: **not tile-based [LOCKED]** — polygons are free-placed geometry, not cells. Terrain representation **[DECIDED — provisional, 2026-08-25]**: placed objects with box colliders over hand-drawn art, Aaron: *"I don't know that I need the collision to be more than boxes."* This task revises that provisional decision on Aaron's instruction (§3 below). Kinds remain prefabs differing in data.
- Bible §5 *Occlusion & collision* **[DECIDED 2026-08-26]**: covered terrain is gone for collision; a partially covered region is clipped at runtime to its visible part; `SheetOcclusion` notifies `IFoldOccludee`s. Unchanged — the polygon path uses the same notification and the same clip.
- Bible §7: exits are not authored; `TravelRules.HasRoom` uses solid footprints. Extended to polygon footprints.
- Bible §0 / guidelines §4: features are data and components; cutting = deleting prefabs. Polygon regions are two prefabs and one pure decomposition class.
- `[DECIDE]` items touched: none. World unit scale (§11.9) is untouched (sheet-local units as everywhere). No `[TENTATIVE]` item is built.
- Vocabulary: Sheet, Front/Back, Flap, Base, Desk. "Outline" and "vertex" are the polygon words; "path" only where it names Unity's `PolygonCollider2D` API.

## 3. Decisions already made by Aaron

- 2026-09-10, after the options message: *"Yeah, can you implement 3?"* — option 3 was "Any polygon, decomposed into convex pieces … you draw freely and the region splits itself into convex pieces at author or load time".
- 2026-09-10, pre-plan question *"How should polygon terrain relate to the existing box Wall/Water regions?"* — Aaron chose **"Both shapes, separate prefabs"**: *TerrainRegion accepts a BoxCollider2D or a PolygonCollider2D. Existing Wall/Water boxes and every authored sheet stay untouched. New 'Wall (Polygon)' and 'Water (Polygon)' prefabs join the palette; arming one starts a click-to-draw polygon, and selecting one gives vertex handles (drag, insert on an edge, delete). No migration.*
- 2026-09-10: full coding workflow.
- Earlier, still binding: static terrain is bare collision drawn by the map art (2026-09-03) — the polygon prefabs have no renderer; the Studio's Terrain view fills them.

## 4. Files

### Runtime (`Assets/Papercut/Scripts`)

| File | Change | Purpose |
| --- | --- | --- |
| `Fold/FaceFootprint.cs` | new | Value type: an occludee's authored footprint as convex pieces (+ bounds, area). Built from a rect or from a simple polygon outline. |
| `Fold/PolygonDecomposition.cs` | new | Pure: validate a simple polygon; decompose it into convex pieces (ear clipping + merge). |
| `Fold/FoldFootprint.cs` | modify | Add `FaceLocalOutline(PolygonCollider2D, faceRoot, list)` and `Of(Collider2D, faceRoot)` → `FaceFootprint`. |
| `Fold/IFoldOccludee.cs` | modify | `FaceLocalFootprint` returns `FaceFootprint`. |
| `Fold/FoldCoverage.cs` | modify | `CoverageResult.Whole` takes a `FaceFootprint` (visible parts = its pieces). |
| `Fold/SheetLayers.cs` | modify | `Coverage(FaceFootprint, face)`; the `Rect` overload stays as a thin wrapper. |
| `Fold/SheetOcclusion.cs` | modify | `SolidFootprints` yields world-space `ConvexPolygon`s. |
| `Fold/ConvexPolygon.cs` | modify | Add `Translated(Vector2)` and `MirroredX()` (x ↦ −x; the Back-space ↔ Front-space mirror, reverses winding — fine, every operation is winding-aware). `SheetGeometry.BackToFront(ConvexPolygon)` overload delegates to it. |
| `Fold/OccludedBoxCollider.cs` → `Fold/OccludedCollider.cs` | rename + modify | Same behaviour over any authored `Collider2D` (box or polygon). |
| `Terrain/TerrainRegion.cs` | modify | Accept box **or** polygon; validate at Awake; polygon gizmo. |
| `Screens/IArrivalObstacle.cs` | modify | `out FaceFootprint`. |
| `Screens/TravelRules.cs` | modify | `HasRoom(Rect player, IEnumerable<ConvexPolygon> solids)`. |
| `Objects/PressurePlate.cs`, `Objects/PushableBlock.cs` | modify | Footprints wrapped with `FaceFootprint.FromRect`; `OccludedCollider` rename. Behaviour unchanged. |

### Editor (`Assets/Papercut/Editor`)

| File | Change | Purpose |
| --- | --- | --- |
| `StudioPolygonDraft.cs` | new | The in-progress outline while drawing: face, points, add/remove-last/clear, validity + message. Pure. |
| `StudioPlacement.cs` | modify | `IsPolygonTerrain`, `PlacePolygon`, `GetOutline`, `SetVertex`, `InsertVertex`, `TryRemoveVertex`, `AuthoredFootprintPieces`. |
| `StudioPane.cs` | modify | Draw mode (armed polygon prefab), draft overlay, vertex/midpoint handles, vertex drag, right-click vertex removal, Terrain-view fill for polygons. |
| `SheetStudioWindow.cs` | modify | Owns the draft (shared like `links`); keyboard: Enter finishes, Backspace removes last, Escape cancels; toolbar hint while drawing. |
| `StudioFoldPane.cs` | modify | Selection highlight through the fold uses the element's true pieces; refuses to place a polygon prefab (draw it in a face pane). |
| `StudioFoldMapping.cs` | modify | `AuthoredPiecesToDeskPieces(layers, face, pieces, results)` — the rect overload delegates to it. |

### Assets

| File | Change | Purpose |
| --- | --- | --- |
| `Prefabs/Terrain/Wall (Polygon).prefab` (+ .meta) | new | `TerrainRegion` (Ability.None) + `PolygonCollider2D` (one 4-point path, unit square), root z −0.05, no renderer. |
| `Prefabs/Terrain/Water (Polygon).prefab` (+ .meta) | new | Same with `requiredAbility = Swim` (1). |

### Tests (`Assets/Papercut/Tests/EditMode`)

| File | Change |
| --- | --- |
| `PolygonDecompositionTests.cs` | new |
| `FaceFootprintTests.cs` | new |
| `SheetLayersTests.cs` | add multi-piece coverage cases |
| `TravelRulesTests.cs` | polygon solids |
| `StudioPolygonDraftTests.cs` | new |
| `StudioPlacementTests.cs` | polygon place / vertex ops / footprint pieces; new prefabs in the resizable-set test |
| `StudioFoldMappingTests.cs` | pieces overload |

### Docs

- `Documents/Claude Bible.md` §3 terrain entry and §11 item 6: append the 2026-09-10 revision. §5's "swaps its box for a clipped `PolygonCollider2D`" becomes "swaps its authored collider (box or polygon) for …". `TerrainRegion`'s remarks likewise.

## 5. Components & data

### `FaceFootprint` (readonly struct, `Papercut`)

- `IReadOnlyList<ConvexPolygon> Pieces` — disjoint convex pieces in the face's authored space. Empty = nothing.
- `Rect Bounds`, `float Area` (sum of piece areas), `bool IsEmpty`.
- `static FaceFootprint Empty`; `static FromRect(Rect)` (one piece; empty if width/height ≤ 0); `static FromPieces(IReadOnlyList<ConvexPolygon>)`; `static TryFromOutline(IReadOnlyList<Vector2> outline, out FaceFootprint, out string reason)` → `PolygonDecomposition`.
- **`default` is empty** (review N3): `Pieces` returns an empty array when the backing list is null, and `IsEmpty`/`Area`/`Bounds` are null-safe, so a `default(FaceFootprint)` handed out by any `out` failure path behaves exactly like `Empty`. Documented on the type.
- `FaceFootprint Translated(Vector2)` and `Transformed(Func<Vector2,Vector2>)` are **not** added — callers translate pieces directly (`ConvexPolygon.Translated`).

### `PolygonDecomposition` (static, `Papercut`, pure)

- `static bool Validate(IReadOnlyList<Vector2> outline, out string reason)`: ≥ 3 points after dropping consecutive duplicates (within 1e-5) and collinear points; area > `ConvexPolygon.AreaEpsilon`; no two non-adjacent edges intersect (O(n²) segment test; adjacent edges may only share their vertex). `reason` is a sentence for the Studio/Awake message.
- `static List<ConvexPolygon> Decompose(IReadOnlyList<Vector2> outline)`: precondition `Validate`. Normalise to counter-clockwise; ear-clipping triangulation; then merge adjacent pieces across a shared diagonal whenever the union is convex (Hertel–Mehlhorn). Returns disjoint convex pieces whose areas sum to the polygon's area. A convex input returns one piece with the input's vertices (after cleaning).
- `static List<Vector2> Clean(IReadOnlyList<Vector2>)`: the duplicate/collinear removal, shared by `Validate` and `Decompose`. "Collinear" means **strictly between its neighbours**: Q is dropped only when cross(Q−P, R−Q) ≈ 0 **and** dot(Q−P, R−Q) > 0. A spike (R doubling back along QP) is left in place so `Validate` rejects it as self-touching instead of silently reshaping the outline (review N4).
- Engineering constants (not tunables): the epsilons above.

### `FoldFootprint` (static, existing)

- `FaceLocalRect(BoxCollider2D, faceRoot)` unchanged.
- new `FaceLocalOutline(PolygonCollider2D polygon, Transform faceRoot, List<Vector2> outline)`: path 0 + `offset`, each point through `faceRoot.worldToLocal × collider.localToWorld`. Returns false if `pathCount != 1` or fewer than 3 points.
- new `FaceFootprint Of(Collider2D authored, Transform faceRoot, out string error)`: box → `FromRect(FaceLocalRect)`; polygon → `TryFromOutline`; anything else → `Empty` + error text.

### `OccludedCollider` (was `OccludedBoxCollider`)

- ctor `(Collider2D authored)`. `Apply(coverage, space)`: none → authored off (+ runtime polygon off); whole → authored on, runtime polygon off; partial → authored off, a runtime `PolygonCollider2D` (added once, `HideFlags.DontSave`, `isTrigger` copied) carries one path per visible part. `LiveColliders` as before. The runtime polygon is held by reference so it is never confused with an authored `PolygonCollider2D` on the same object.

### `TerrainRegion` (existing)

- Serialized: `requiredAbility` (unchanged). **No new Inspector tunables** — nothing here is a feel value.
- `[RequireComponent(typeof(BoxCollider2D))]` removed. `Awake` resolves the authored collider: exactly one of `BoxCollider2D` / `PolygonCollider2D` on this object. Errors (logged with context, region disabled and inert = no live collider): none found; both found; polygon with `pathCount != 1`; polygon that fails `PolygonDecomposition.Validate` (the reason is in the message). A trigger collider is fixed and reported as today.
- `FaceLocalFootprint(faceRoot)` → `FoldFootprint.Of(authored, faceRoot)`; `Empty` while invalid.
- `OnFoldCoverageChanged` **returns early while invalid** (review N2) — inertness is stated, not a side effect of `Whole(empty)` having no parts. Belt and braces: `CoverageResult.Whole(coverage, footprint)` with an empty footprint returns `None(coverage)` (`IsWhole` false).
- `TryGetSolidFootprint(player, out FaceFootprint)`.
- The authored collider is cached at Awake and every later read (gizmo, `Apply`, footprint) uses that reference, never `GetComponent<PolygonCollider2D>()` — after the first partial clip there are two polygon colliders on the object (review N8). Studio and asset-check code ignore any collider with `HideFlags.DontSave` for the same reason.
- Gizmo: box as today; polygon as a closed line loop through the cached authored path, in the kind colour.

### `IFoldOccludee`, `IArrivalObstacle`, `TravelRules`, `SheetOcclusion`

- `FaceFootprint FaceLocalFootprint(Transform faceRoot)`.
- `bool TryGetSolidFootprint(PlayerAbilities, out FaceFootprint sheetLocal)`.
- `TravelRules.HasRoom(Rect playerBox, IEnumerable<ConvexPolygon> solids)`: false if any solid's `Overlaps(playerBox)` (shared area above `AreaEpsilon`; touching edges are fine, as today).
- `SheetOcclusion.SolidFootprints` yields each piece `Translated(sheet.Centre)`.

### `SheetLayers.Coverage(FaceFootprint, face)`

A Back-face footprint is taken into Front-space first, piece by piece, through `ConvexPolygon.MirroredX` (the polygon form of `SheetGeometry.BackToFront`; review N5). Then, per piece: the existing per-rect algorithm (intersect with each layer of the wanted orientation, transform, subtract higher layers). `IsWhole` = every piece produced exactly one part, all from the Base, and the parts' total area ≥ footprint area − tolerance → `CoverageResult.Whole(Uncovered, footprint)`. Otherwise `Clipped(parts, coverage)` with coverage `Uncovered`/`Covered`/`Partial` computed from total area exactly as today. Empty footprint → `None(Uncovered)`.

### Studio: `StudioPolygonDraft` (class, pure)

- `SheetFace Face`, `IReadOnlyList<Vector2> Points`, `bool IsActive` (Points.Count > 0), `GameObject Prefab` (the armed prefab it was started for).
- `Start(prefab, face)`, `bool TryAdd(Vector2 p, out string reason)` (refuses a point within 1e-4 of the last point), `bool RemoveLast()`, `Clear()`.
- `bool CanFinish(out string reason)` → `Points.Count >= 3 && PolygonDecomposition.Validate`.
- `bool IsCurrentlyValid` → for the red-outline feedback (only meaningful at ≥ 3 points).

### Studio: `StudioPlacement` additions

- `static bool IsPolygonTerrain(GameObject)`: root has `TerrainRegion` **and** `PolygonCollider2D` **and no** `BoxCollider2D`. (Works on prefab assets and instances.)
- `static GameObject PlacePolygon(GameObject prefab, Transform faceRoot, SheetFace face, IReadOnlyList<Vector2> faceLocalPoints, float snapIncrement)`: refuses (error log, null) unless `IsPolygonTerrain(prefab)` and the snapped points validate. Instantiates like `Place` at the outline's bounds centre (prefab z preserved, layer set recursively), then sets path 0 to the points relative to that centre, offset zero. One undo step ("Draw {prefab.name}").
- `static bool GetOutline(GameObject element, Transform faceRoot, List<Vector2> outline)`: face-local vertices (via `FoldFootprint.FaceLocalOutline`).
- `static void SetVertex(GameObject element, int index, Vector2 faceLocal, float snap)`: `Undo.RecordObject(polygon)`; converts through the element's world→local matrix; writes path 0.
- `static void InsertVertex(GameObject element, int afterIndex, Vector2 faceLocal, float snap)`: inserts after `afterIndex` (the edge's start), same recording.
- `static bool TryRemoveVertex(GameObject element, int index, out string reason)`: refuses below 4 vertices ("a region needs at least three vertices") or if the result would fail `Validate`.
- `static bool IsValidOutline(GameObject element, Transform faceRoot, out string reason)`.
- `static void AuthoredFootprintPieces(GameObject element, Transform faceRoot, List<ConvexPolygon> pieces)`: every collider under the element → `FoldFootprint.Of(...)` pieces (box → one rect piece; polygon → decomposition; an invalid polygon contributes nothing). Replaces `AuthoredFootprint`'s role for the fold pane highlight; `AuthoredFootprint` (rect) stays for labels and the placement ghost.
- `IsResizable` unchanged (requires a `BoxCollider2D`, so polygon regions are not box-resizable). `TryGetFaceLocalOutline` unchanged (already reads path 0).

### Studio: `StudioPane` behaviour additions

- Context gains `StudioPolygonDraft Draft`.
- Overlays: Terrain-view fill for a polygon region = its decomposition pieces (each `DrawFill`); outline in the kind colour as today (concave polyline is fine). When the selected element `IsPolygonTerrain`: vertex handles (filled squares, `HandleDrawPixels`) at every vertex and hollow midpoint handles on every edge; the outline draws in **red** (`InvalidColor`) instead of the selection colour while `IsValidOutline` is false. The draft: its points and the open polyline in the ghost colour, the closing edge dashed-ish (thin), red when ≥ 3 points and invalid; the first vertex drawn larger (the "close here" target); a rubber-band line from the last point to the cursor.
- Editor constants (`const`, engineering): `HandleHitPixels` (existing 7), `MidpointHandlePixels` = 3.

### Studio: `SheetStudioWindow`

- Owns `readonly StudioPolygonDraft draft = new()`; cleared on `Attach`, on link-mode entry, and when the palette disarms (`palette.Armed == null` and `draft.IsActive` → clear).
- **Keyboard precedence** (review N1), in `HandleKeyboard`: (1) an active fold/ghost drag claims Escape; (2) while a polygon prefab is armed, the draft keys — Enter finishes, Backspace removes the last point, Escape cancels the draft (or, with no draft, disarms); (3) link mode; (4) palette; (5) element keys. While a polygon prefab is armed **Backspace never deletes the selected element** (it is the draft's key even when the draft is empty, so the reflex Backspace after a finish does nothing); **Delete** still deletes the selected element. The toolbar hint says so.
- Toolbar hint while a polygon prefab is armed: "Draw: click to add points · click the first point or Enter to finish · Backspace removes the last point · Esc/right-click cancels".

## 6. Behaviour

### Runtime

1. **Awake** (`TerrainRegion`): find the authored collider. Box → as today. Polygon → `FoldFootprint.Of` runs `Validate`; on failure log `TerrainRegion '{name}': {reason}; the region is inert until fixed.`, set `enabled = false`, and leave the collider **disabled** (an invalid region must not block anything by accident — silent-failure rule: it is loud, and it is inert rather than half-working).
2. **Occlusion** (`SheetOcclusion.Notify`, unchanged flow): `FaceLocalFootprint` → `FaceFootprint`. Not the Screen → `None`. Flat Front → `Whole(Uncovered, footprint)`: `OccludedCollider` enables the authored `PolygonCollider2D` (Unity's own convex decomposition serves physics for the unclipped shape). Folded → `Coverage(footprint, face)`: for a Front polygon straddling a crease the runtime polygon gets one path per visible convex part, exactly where each lies; a Back polygon exposed on a landed Flap appears through its mirrored parts. Multi-fold and stacking need nothing new — the per-piece loop is the existing per-rect loop.
3. **Ability gating**: `Apply()` runs `Physics2D.IgnoreCollision` on `LiveColliders` — the authored polygon or the runtime one — unchanged.
4. **Travel** (`ScreenNavigator.HasRoom`): `SolidFootprints` yields world-space convex pieces; `TravelRules.HasRoom` rejects if the player's arrival rect shares area with any piece. A polygon wall touching the sheet edge blocks arrival only where it actually is.
5. **Pushable blocks vs polygon walls**: `PushRules`/`PushableBlock` stop against walls through Physics2D contacts (unchanged); no rect assumption about walls there (verified: `PushableBlock` only reads its own rect).
6. **Failure modes**: missing `PlayerAbilities` → as today. `pathCount != 1` → invalid (message says "one outline"). Runtime edits to the polygon (none exist) are not supported — the footprint is computed on demand from the collider each notify, as it is for boxes today.

### Studio

1. **Arm** `Wall (Polygon)`/`Water (Polygon)` in the palette. Panes show a small cursor marker and the toolbar hint. No square ghost (the prefab's default square is never what gets placed).
2. **Click** in a face pane: `draft.Start(prefab, face)` on the first click; each click `TryAdd(snap(point))`. Clicking in the **other** face pane while a draft is active is refused with a hint message ("Finish or cancel the polygon on the Front first") — never silently discarded.
3. **Finish**: click within `HandleHitPixels` of the first vertex (when ≥ 3 points), or press **Enter**. `CanFinish` false → message with the reason (e.g. "the outline crosses itself"); the draft stays so Aaron can Backspace. Success → `PlacePolygon`, select it, clear the draft, **stay armed** (draw the next one; Escape/right-click disarms as today).
4. **Backspace** removes the last point; when the last point goes, the draft ends (still armed). **Escape**: active draft → cancel it (still armed); no draft → disarm (existing). **Right-click**: active draft → cancel; else existing behaviour (disarm / link menu).
5. **Select** a placed polygon region (click inside — `PickElement` already handles concave outlines by even-odd test): handles appear. **Drag a vertex handle** → `SetVertex` each drag event inside one undo group (as Resize does). **Mouse-down on a midpoint handle** → `InsertVertex` then continue as a vertex drag of the new vertex. **Right-click a vertex handle** → `TryRemoveVertex`; refusal shows its reason. Dragging the interior moves the whole region (existing Move; the path is relative to the transform).
6. **Invalid shape during a drag** (self-crossing): outline turns red. On **mouse-up**, if `IsValidOutline` is false the path is restored to its pre-drag value inside the same undo group and a message says why — the Studio never leaves a region that would refuse itself at Awake. (Same rule for an insert-drag.)
7. **Duplicate / Delete / arrow nudge / cross-face MoveMapped** work unchanged (they act on the transform or the whole object).
8. **Fold pane**: the selection highlight maps the region's true pieces through the folds. While a polygon prefab is armed the placement ghost is the cursor marker plus the hint "Draw polygon terrain in a face pane" — never the prefab's default square (review N6) — and a click is refused with the same message.
9. **Inspector** edits to the `PolygonCollider2D` (Unity's own polygon edit tool) remain possible; overlays refresh via the existing `ObjectChangeEvents` hook. If Aaron makes it invalid there, the outline is red in the pane and Awake reports it.

## 7. Interfaces & seams

- `FaceFootprint` is the one seam: *what an occludee occupies* (pieces) is separated from *what its collider is* (box/polygon). `SheetLayers.Coverage` and `TravelRules` know only pieces.
- `PolygonDecomposition` is pure and lives in `Fold/` next to `ConvexPolygon`; nothing else in the fold system knows about concave polygons.
- **Cutting the feature**: delete `Wall (Polygon).prefab`, `Water (Polygon).prefab`, `StudioPolygonDraft.cs`, the draw/handle code in the Studio, and `PolygonDecomposition.cs`; `FaceFootprint` can stay (a one-piece footprint is a rect). No fold, occlusion, or collision logic has a polygon branch.
- No new abstraction beyond that: no shape interface, no per-shape strategy classes, no ScriptableObject.

## 8. Testing

### Edit Mode tests (mechanical, run via the Unity CLI)

- `PolygonDecompositionTests`: convex square → 1 piece, same area; L-shape (6 vertices) → ≥ 2 pieces, every piece convex (`Contains` of each vertex midpoint-check / all cross products same sign), areas sum to the L's area, at most 2 pieces after merging; a 10-vertex star → pieces convex, area sum; clockwise input equals counter-clockwise result areas; collinear/duplicate points removed; `Validate` false with a reason for: < 3 points, bow-tie (self-intersecting), zero-area (all collinear).
- `FaceFootprintTests`: `FromRect` bounds/area; `TryFromOutline` invalid → false with reason; L-outline → pieces disjoint (pairwise `Overlaps` false).
- `SheetLayersTests`: an L-shaped footprint clear of the fold → `IsWhole`; straddling the crease → `Partial`, total part area equals the visible area computed from the rect pieces; entirely on the lifted strip → `Covered`, no parts; **a Back-face L on the lifted strip** → exposed (`Covered` for Back = fully exposed), parts' total area = the L's area, every part vertex inside the landed Flap's desk polygon (review N5); `CoverageResult.Whole(_, FaceFootprint.Empty)` is `None`.
- `OccludedColliderTests` (Edit Mode, temporary GameObject; review N7): over an authored `PolygonCollider2D` — whole → authored on, no runtime polygon; partial → authored off, a second `PolygonCollider2D` with one path per part and `DontSave`; none → both off; the runtime polygon is never the authored one; and the same three states over a `BoxCollider2D`.
- `TravelRulesTests`: triangle overlapping the player → no room; triangle touching an edge → room; triangle beside → room.
- `StudioPolygonDraftTests`: add/refuse duplicate/remove-last/clear; `CanFinish` false at 2 points and for a bow-tie, true for a triangle.
- `StudioPlacementTests`: `IsPolygonTerrain` true for both new prefabs, false for Wall/Water/Gate/Tree/Block/plates; `PlacePolygon` places at the bounds centre with z −0.05, face layer set, `GetOutline` returns the snapped input points (order preserved); `PlacePolygon` refuses a bow-tie (null + error expected); `SetVertex` moves one vertex; `InsertVertex` adds one at the given edge; `TryRemoveVertex` refuses at 3 vertices and succeeds at 4; `AuthoredFootprintPieces` of an L region = decomposition pieces; of the Tree = two rect pieces; `IsResizable` false for polygon regions; `PickElement` inside the concave notch of an L picks nothing, inside the arm picks the region.
- `StudioFoldMappingTests`: pieces overload agrees with the rect overload for a rect piece.
- `DrawingAssetsTests`-style asset checks: both polygon prefabs have no renderer, one `PolygonCollider2D` with `pathCount == 1`, a `TerrainRegion`, root z −0.05, Water variant's `requiredAbility == Swim`.

### Unity CLI mechanical pass (step 6)

1. `recompile` → `errors[]` empty.
2. `run_tests {"mode":"editor"}` — full suite; report Summary and any failure verbatim. (Probe scene dirtiness first per memory.)
3. Play Mode on the open Desk scene: via `eval`, instantiate `Wall (Polygon)` under the Screen's Front with an L outline, call `SheetOcclusion.Apply()`, read the live colliders (`authored enabled, no runtime polygon`); commit an edge fold through `SheetFolds` that cuts the L, re-read: authored off, runtime `PolygonCollider2D.pathCount` ≥ 1 with vertices inside the visible area; console has no errors; `capture_game_view` and describe literally; `editor_stop`; destroy nothing persistent (Play Mode state is discarded).
4. Load both new prefabs through `PrefabUtility.LoadPrefabContents` in `eval`: no missing scripts; components as specified; `!u!` class IDs audited (60 = PolygonCollider2D).

### What Aaron must play/use to judge (not mechanical)

- Whether click-to-draw, first-vertex-to-close, and the vertex/midpoint handles feel right in the Studio, and whether the red-and-revert rule for self-crossing shapes is the behaviour he wants versus a warning that lets him keep editing.
- Whether Scuffy slides cleanly along a polygon wall. The player's collider is a 0.44 × 0.7 `BoxCollider2D`, and Box2D can snag a box on the internal edges between convex pieces; boxes cut by folds already have this exposure, and note the flat shape uses Unity's decomposition while the clipped shape uses ours, so the internal edges differ flat vs folded. If it snags, the first mitigation to try is `BoxCollider2D.edgeRadius` on the player (review N9); fewer pieces is the second. Both are follow-ups.
- Whether a polygon region under a fold, and a Back polygon exposed by a Flap, read correctly in play.

## 9. Assumptions (engineering)

1. Authored content is never rotated or scaled relative to its face root (existing convention in `FoldFootprint`); vertex conversions still go through the full matrix for exactness.
2. One outline per polygon region (`pathCount == 1`). Several outlines = several regions.
3. Decomposition runs at load (Awake/notify), never stored; the authored data stays the single outline.
4. Ear clipping + convex merge is sufficient quality; optimal decomposition is not needed.
5. Winding of the authored path is whatever Aaron drew; the decomposition normalises internally. Unity's `PolygonCollider2D` accepts either.
6. The vertex-removal gesture is right-click on a vertex handle; the finish gesture is first-vertex click or Enter. (Editor UX call; easy to change.)
7. Drawing is per face pane only; the fold pane refuses polygon placement rather than dropping the prefab's default square.
8. `HandleHitPixels` (7 px) and the midpoint handle size are editor engineering constants, not Inspector tunables (nothing about play feel).
9. The `Rect` overload of `SheetLayers.Coverage` stays (wrapper) so existing tests and the pressure plate's rect path remain readable.
10. Existing authored sheets are untouched; the Bible §3 entry is appended, not rewritten.

## 10. Open questions

None. (No `[DECIDE]` item is involved; the data-model question was answered in step 1.)

## Review

Round 1 (plan-reviewer, 2026-09-10): **APPROVE WITH CHANGES**, no blocking findings.

| # | Finding | Disposition |
| --- | --- | --- |
| N1 | Backspace collides with delete-selected-element; the reflex Backspace after a finish deletes the new region. | **Accepted.** Keyboard precedence stated in §5; Backspace is the draft's key whenever a polygon prefab is armed; Delete still deletes. |
| N2 | "Inert while invalid" was accidental (relied on `IsNone` being tested before `IsWhole`). | **Accepted.** Early return in `OnFoldCoverageChanged`; `Whole(empty)` → `None`; tested. |
| N3 | `default(FaceFootprint)` has null `Pieces`. | **Accepted.** Null-safe accessors; documented. |
| N4 | Naive collinear removal would silently reshape a spike. | **Accepted.** Strictly-between rule in `Clean`. |
| N5 | No polygon-level Back→Front mirror listed; no Back-face test. | **Accepted.** `ConvexPolygon.MirroredX` + `SheetGeometry.BackToFront(ConvexPolygon)`; Back-face L test. |
| N6 | Fold pane's placement ghost would show the default square. | **Accepted.** Cursor marker + hint instead. |
| N7 | `OccludedCollider` untested in Edit Mode. | **Accepted.** `OccludedColliderTests`. |
| N8 | Two `PolygonCollider2D`s on one object after a clip. | **Accepted.** Cached authored reference; Studio/asset code skips `DontSave` colliders. |
| N9 | Player is a box; snagging is real; flat vs clipped decompositions differ. | **Accepted** as a playtest note with `edgeRadius` as the first mitigation. |
| N10 | Bible §5 and TerrainRegion remarks still say "box". | **Accepted.** Both patched. |
| Q1 | Holes (islands / ring walls)? | **Escalated** to Aaron. |
| Q2 | Revert-on-release vs warn-and-allow for a self-crossing edit. | **Escalated** to Aaron. |
| Q3 | Vertex magnet to other regions' vertices and the sheet edge. | **Escalated** to Aaron. |

### Post-review answers (Aaron, 2026-09-10)

- **Q1 holes:** *Single outline only.* Islands are separate regions; a ring is a C or two pieces. (Plan unchanged.)
- **Q2 self-crossing edit:** *Revert on release.* (Plan §6.6 stands.)
- **Q3 magnet snap:** *Add the magnet.* New in this task: while drawing a point or dragging a vertex, after the grid snap the point magnets to the nearest vertex/corner of any **other** element's collider outline on the same face, or to the sheet edge (x or y clamped to ±half extent), when within the handle hit radius (`HandleHitPixels` converted to sheet units through the pane's zoom). Pure helper `StudioMagnet.Snap(point, targets, radius)` in `StudioPlacement`/own file; targets from `StudioPlacement.MagnetTargets(faceRoot, exclude)`. Tested: nearest target wins inside the radius, outside the radius the grid-snapped point is returned, an edge magnet clamps one axis.

## Deviations

Engineering-only departures from the plan as reviewed (no design change):

- **Tests live in new files** rather than being appended to `SheetLayersTests`, `StudioPlacementTests`, and `StudioFoldMappingTests`: `PolygonCoverageTests.cs` (multi-piece coverage, Front and Back, plus the rect-overload agreement), `StudioPolygonTests.cs` (prefabs, PlacePolygon, the draft's finish, vertex ops, undo, footprint pieces, concave picking, magnet, fold-mapping overload), `OccludedColliderTests.cs`, `PolygonDecompositionTests.cs`, `FaceFootprintTests.cs`, `StudioPolygonDraftTests.cs`. `TravelRulesTests.cs` was rewritten for polygon solids. The existing files are untouched, so their history stays readable.
- **`StudioPlacement.SetVertex` / `InsertVertex` / `TryRemoveVertex` / `IsValidOutline` take the face root** (needed to express vertices face-locally); `SetOutline(element, faceRoot, points, undoName)` is public so the pane's revert-on-release goes through one undo-recorded path.
- **The magnet lives in `StudioPlacement`** (`MagnetTargets`, `Magnet`) rather than a separate `StudioMagnet` class — two static methods did not justify a file.
- **`TerrainRegion.IsValid`** is exposed (read by the Play Mode probe and useful to any future editor check); `Refuse` disables both candidate colliders so an invalid region never blocks by accident.
- **`PolygonDecomposition.Validate` checks self-crossing before area**: a bow-tie's signed area cancels to zero, and "crosses itself" is the message Aaron needs. Three collinear points collapse to two in `Clean` and are refused as "too few vertices"; the "no area" branch stays as a safety net.
- **`StudioFoldPane.PressOutcome.PlaceRefusedPolygonTerrain`** added for the fold pane's refusal (the press chain is enum-reported for its headless tests).
- **`StudioPane.DrawHint`** (toolbar text) and `StudioFoldPane.PolygonTerrainHint` are public consts so the window and the pane show one wording.
- **Editor-run tests were blocked by Aaron's dirty Desk scene** (the runner's save prompt is modal; see the unity-cli memory). The pure tests (decomposition, footprint, coverage, travel, and the pre-existing geometry suites) were run with the Roslyn/dotnet fallback runner instead; the editor-dependent tests (`StudioPolygonTests`, `StudioPolygonDraftTests`' finish case, `OccludedColliderTests`) await a run once the scene is saved.

## Code review

Round 1 (code-reviewer, 2026-09-10): **APPROVE WITH FIXES**, no must-fix items.

| # | Finding | Disposition |
| --- | --- | --- |
| S1 | An invalid polygon region is red only while selected. | **Accepted.** `DrawOverlays` draws any polygon region with an invalid outline in red, selected or not. |
| S2 | `AuthoredFootprintPieces` / `MagnetTargets` took every collider, so the Block's pusher polygon became a highlight piece and eight magnet corners. | **Accepted.** `IsAuthoredShape` skips runtime clip polygons and a polygon beside a box on the same object; magnet targets are terrain regions only (below). |
| S3 | Arming a different prefab mid-draft discarded the outline silently. | **Accepted.** Arming the other polygon kind re-targets the draft (`Retarget`); anything else cancels it with "Polygon cancelled." |
| S4 | Awake refusal paths had no recorded verification. | **Accepted.** The Play Mode probe now instantiates a bow-tie region under the Screen's Front: `IsValid == false`, component disabled, zero live colliders, one error logged with the reason. Result recorded under Verification. A Play Mode test assembly was not added (none exists; out of scope). |
| S5 | A region under the Sheet but outside Front/Back passed validation with a live collider. | **Accepted.** A null face root is a refusal ("must be under the sheet's Front or Back root"). |
| S6 | Revert-on-release left a no-op undo entry. | **Accepted.** `Undo.RevertAllDownToGroup(dragUndoGroup)`; the pre-drag outline copy is gone. |
| S7 | A first-vertex click with fewer than three points added a phantom point. | **Accepted.** `TryAdd` refuses a point on any existing vertex. |
| S8 | Bible said "other regions' vertices" while the code magneted to every element. | **Accepted by fixing the code, not the sentence** — see Q1. |
| Q1 | Magnet targets: regions only (as Aaron answered) vs every element (as built). | **Resolved from Aaron's existing answer** (*"other regions' vertices and the sheet edge"*): terrain regions only (box corners, polygon vertices, the Tree's region boxes) plus the sheet edge. Not re-asked. |
| Q2 | A midpoint press released without dragging leaves a collinear vertex. | **Kept** (engineering call): "insert now, drag later" is a legitimate gesture; `Clean` drops a collinear vertex at load, and the pane shows the handle so it can be dragged or removed. |

## Verification (2026-09-10)

- `recompile` → `{"status":"completed","failed":false,"errors":[]}` after every change.
- Editor `run_tests {"mode":"editor"}` (after Aaron saved the Desk scene): **Total 346, Passed 346, Failed 0, Skipped 0** — includes the seven new/changed test classes.
- Fallback Roslyn/dotnet runner on the pure suites (decomposition, footprint, coverage, travel, convex polygon, sheet layers, walkable outline, fold geometry, sheet geometry): **100 passed, 0 failed**.
- Play Mode probe on `Assets/Scenes/Desk.unity` (Screen = Sheet (0,0), player at sheet-local (0, −0.11)): a `Wall (Polygon)` instantiated under Front at (2, 1.5) with an L outline (y 0..3) → flat: `IsValid` true, authored polygon enabled, one `PolygonCollider2D`. `SheetFolds.TryCommit(EdgeNorth, depth 1)` accepted (crease y 3.25, Flap lands over 2.25..3.25) → authored off, runtime `PolygonCollider2D` (`DontSave`) enabled with **two paths**: the foot (0.5,0)(3.5,0)(3.5,1)(1.5,1) and the arm (0.5,2.25)(0.5,0)(1.5,1)(1.5,2.25) — the arm ends exactly at the landed Flap's edge. A bow-tie region added the same way: `IsValid` false, component disabled, zero live colliders, one error "the outline crosses itself; the region is inert until the asset is fixed". Console otherwise clean. Two Game View captures show Sheet (0,0) with the north Flap down and Scuffy below it (the region itself is bare collision, so nothing of it is visible).
- `Tools/check_links.py`: ALL LINKS OK (both new prefabs resolve their `TerrainRegion` script GUID).

Round 2 (code-reviewer on the round-1 fixes): **APPROVE WITH FIXES**, no must-fix items. All accepted:

| # | Finding | Fix |
| --- | --- | --- |
| S1 | A region not under any Sheet was marked valid before its outline was checked (a bow-tie went live). | `sheet == null` is a refusal ("is not under a Sheet"); the separate Awake log is gone (it was the same message). |
| S2 | The phantom-point guard was an epsilon, but the gesture is a 7-px near-miss. | `OnDrawClick` refuses a click within `HandleHitPixels` of any draft vertex with a message ("at least three points before it can close" / "already a vertex"). |
| S3 | The pane still restarted the draft when the armed prefab changed. | `Start` only when inactive, else `Retarget`. |
| S4 | With Overlays off and Terrain on, an invalid polygon region vanished. | The terrain loop draws an undecomposable polygon region's outline in red. |
| S5 | "Polygon cancelled." set on entering link mode showed up stale afterwards. | Message only when the cancel is not link mode's. |
| S6 | Revert-on-release had no test. | `RevertAllDownToGroup_UndoesAnInsertAndEveryMoveOfOnePress` added. |

These fixes are small and local; no third review round.
