# Plan — Multiple folds and stacking (toggleable)

Date: 2026-08-26. Follows `2026-08-26-folding.md` and `2026-08-26-folding-adjustments.md`.

## 1. Task

Aaron (verbatim): *"Can we try allowing multiple folds on one sheet?"* — and, asked whether Flaps may only be independent or may also land on / lift other Flaps: *"I do in fact mean stacking is allowed - but also make it a toggle, because I just want to see how it feels and whether or not I want to keep it in or not, you know? (the toggle is for stacking, and then there should be a second toggle allowing multiple folds period, so I can also choose whether or not to have multiple non stacking folds as well)"*

Deliverables:
1. **Two toggles** on `SheetFolds` (Inspector, base Sheet prefab so every sheet inherits): `allowMultipleFolds` and `allowStacking`.
2. **Multiple independent folds** (multiple on, stacking off): any number of folds whose lifted and landed regions are pairwise disjoint.
3. **Stacking** (both on): a fold lifts *whatever paper lies on the Flap side of its crease* — Base, landed Flaps, or both — and lays it over across the crease, on top of whatever is there. This is real fold-on-fold; Aaron will play it and decide.
4. Everything downstream — rendering, walls/exits, terrain occlusion, validity, unfold, creases — correct under all three settings.

**Out of scope (Aaron, second message):** the "universal settings" ScriptableObject — *"don't bother with that - leave those things as is."* Also out: the tape visual, objects on Flaps, anything `[TENTATIVE]`.

## 2. Design references

- Bible §4 Validity `[DECIDE]` "Multiple simultaneous folds per sheet … build the data model to hold a list … implement and expose only one at a time until told otherwise" — **told otherwise now** (Aaron above). The Bible entry is updated: the model exposes N folds behind the two toggles; the playtest is Aaron's.
- Bible §4 `[DECIDE]` "Folding an already-folded sheet (fold-on-fold) — assume no" — **now the `allowStacking` toggle.** Physically, "a Flap lands on a Flap" and "a crease crosses a Flap" are the same thing (the Flap side of a crease is folded over, however many layers deep), so one toggle governs both; stated in §10 below as an interpretation, since Aaron's word was "stacking".
- Bible §4 Geometry: no overhang (Aaron, 2026-08-26) — **kept**: every landed piece must lie within the sheet rect, with the depth limit now computed from the paper actually present (see §5 `MaxDepth`).
- Bible §4 Validity `[LOCKED]`: a fold may not cover the player, wholly or partially; the player is never carried. **Kept and made exact**: refused if any *lifted* piece or any *landed* piece overlaps the player.
- Bible §4 Unfold (decided): click crease or Seam; refused with the player on the landed Flap; animated retreat; remembered creases. **Kept**, plus the rule that a fold cannot be unfolded while a later fold lies on it or through it (§6).
- Bible §5: covered terrain is gone, partial is clipped exactly; occlusion notifies (`IFoldOccludee`). **Kept**; the clip is now against the layer stack.
- Bible §6 authoring convention (Back-space, mirrored at runtime) — kept. The Back root is **no longer posed** (one root cannot be posed for two Flaps); Back content is placed by its clipped collider instead (§5 `SheetOcclusion`).
- Bible §7 exits are any walkable ground reaching a sheet edge — kept; a landed edge lying *inside* the sheet rect over empty desk is a wall, not an exit (§6).
- Bible §11 #1 is answered by this work (provisionally, by playtest); #7 (objects ride the Flap) and #11 (straddling) untouched — no objects on Flaps exist.
- Design Doc, Folding → Undecided "Multiple folds? — need to playtest": this is that playtest build.
- Implementation-guidelines §4a: the two toggles are Inspector values; no other feel value is added.

## 3. Decisions already made by Aaron

- Multiple folds: yes, behind a toggle.
- Stacking (a Flap on/through another Flap): yes, behind a second toggle.
- Shared settings asset: no ("leave those things as is").
- Earlier, still binding: no overhang; unfold on crease or Seam; unfold refused with the player on the Flap; creases remembered until reset; red-tint refusal; drag-from-edge/corner input; render-texture compositing behind `IFoldRenderer`.

## 4. Files

| File | Action | Purpose |
|---|---|---|
| `Scripts/Fold/Isometry2D.cs` | new | Pure 2D rigid transform (2×2 linear + translation): compose, apply, inverse, reflection across a `Crease`, `IsMirror` (det < 0). Managed math only, unit-testable. |
| `Scripts/Fold/SheetLayers.cs` | new | **The sheet as a stack of layers.** Pure. `Layer` = convex piece of the original sheet (Front-space) + `Isometry2D ToDesk` + `FrontUp` + `MovedBy` (index of the last fold that moved it, −1 for the Base). `Apply(fold, index)` folds everything on the Flap side; returns a `FoldEffect`. Also `MaxDepth(anchor)`, `Coverage(footprint, face)`, `Footprint` (all layer polygons, sheet-local). |
| `Scripts/Fold/FoldEffect.cs` | new | What one fold did, in sheet-local space: lifted pieces (before reflection), landed pieces, crease segments through paper, Seam segments, crease marks per face (original coords), and the outcome (`None` / `NothingToFold` / `Overhangs`). |
| `Scripts/Fold/FoldValidity.cs` | modify | Player rule against a `FoldEffect` (lifted ∪ landed vs. player rect); `Independent(a, b)` (regions disjoint) for the stacking-off rule and unfold ordering. |
| `Scripts/Fold/FoldGeometry.cs` | modify | Keeps `CreaseOf`, static `MaxDepth` (the flat-sheet bound, used only as the reference the layer bound is tested against), `DepthForDragPoint(anchor, local, maxDepth)` (bound passed in — review B1), `TryClipLineToRect`, `DistanceToSegment`, rect helpers. **Removes** the single-fold functions now expressed by `SheetLayers`: `FlapRegion`, `LandedRegion`, `ReflectedSheet`, `HiddenRect`, `SeamSegments`, `DistanceToSeam`, `BackPose`, `BackToLanded`, `LandedToBack`, `Coverage`, `TryCreaseSegment`, `DistanceToCrease`. |
| `Scripts/Fold/ConvexPolygon.cs` | modify | `Transform(Isometry2D)`, `Subtract(ConvexPolygon)` (convex difference → disjoint convex pieces), `Overlaps(ConvexPolygon)`, `Contains(Vector2)`, `ContainedIn(Rect)`. |
| `Scripts/Fold/Fold.cs` | modify | `Clamped` and `Overhangs` **removed** (the bound now depends on the paper present — review B1); docs updated. |
| `Scripts/Fold/SheetFolds.cs` | modify | Toggles; replays the fold list into `SheetLayers` after every change (`Layers`, `Effects`); `Evaluate(fold, player)` shared by preview and commit; `TryUnfoldAt` with the ordering rule; remembered creases become `CreaseMark`s. |
| `Scripts/Fold/IFoldRenderer.cs` | modify | `FoldDisplay.RememberedCreases` is `IReadOnlyList<CreaseMark>`. `FoldVisual` unchanged. |
| `Scripts/Fold/RenderTextureFoldRenderer.cs` | modify | Draws layers: one pooled mesh part per layer (its own texture, z and tint), replayed from the display; crease marks from the replay; Seam of previews from the effect. `PoseBack`-era comments removed. |
| `Scripts/Fold/SheetOcclusion.cs` | modify | No Back posing. Coverage from `folds.Layers.Coverage(...)`; parts are sheet-local; occludees get `sheet.transform` as the space. |
| `Scripts/Fold/FoldCoverage.cs` | modify | `CoverageResult.Clipped(parts, coverage)`; doc: parts are **sheet-local, where the piece physically is**. |
| `Scripts/Fold/IFoldOccludee.cs` | modify | Doc: `OnFoldCoverageChanged(coverage, space)` — `space` is the transform the parts are relative to (the Sheet). |
| `Scripts/Fold/OccludedBoxCollider.cs` | modify | Parameter rename only (`space`); maths unchanged (`box.worldToLocal × space.localToWorld`). |
| `Scripts/Fold/WalkableOutline.cs` | modify | `Segments(IReadOnlyList<ConvexPolygon> layers)`: boundary of the union of the layer polygons. `OutlineKind.Crease` → `OutlineKind.Wall` (crease, or landed paper edge over empty desk). |
| `Scripts/Desk/SheetBoundary.cs` | modify | Passes `folds.Layers.Footprint`. |
| `Scripts/Fold/FoldDragInput.cs` | modify | Press: unfold hit-test first (all folds), then edge/corner grab if `folds.CanStartFold`; drag depth clamped by `folds.Layers.MaxDepth(anchor)` only (review B1). |
| `Scripts/Desk/Sheet.cs` | modify | Doc only (Back root not posed). |
| `Tests/EditMode/Isometry2DTests.cs` | new | Compose/inverse/reflection. |
| `Tests/EditMode/SheetLayersTests.cs` | new | The model (§8). |
| `Tests/EditMode/FoldGeometryTests.cs` | modify | Drop tests of removed functions; keep crease/MaxDepth/drag/clip tests. |
| `Tests/EditMode/FoldValidityTests.cs` | modify | Against effects. |
| `Tests/EditMode/WalkableOutlineTests.cs` | modify | Union boundary; `Wall` kind; stacked cases. |
| `Tests/EditMode/ConvexPolygonTests.cs` | modify | `Subtract`, `Transform`. |
| `Documents/Claude Bible.md` | modify | §4 multiple-folds and fold-on-fold entries → toggles (provisional, playtest); §4 Input "only one fold at a time is exposed" line; §5 `HiddenRect` sentence and the new "landed edge over empty desk is a wall" rule; §6 Back root not posed; §7 `WalkableOutline` description; §11 #1 (review N5). |

No prefab/scene YAML changes: the new fields on `SheetFolds` are absent from `Sheet.prefab` and therefore take their C# defaults; nothing serialized is renamed.

## 5. Components & data

### `Isometry2D` (struct)
`Vector2 Ax, Ay` (columns), `Vector2 T`. `Apply(p) = Ax*p.x + Ay*p.y + T`. `Identity`, `Reflection(Crease)`, `Then(Isometry2D)` (this, then other), `Inverse` (linear transposed; for an isometry the inverse is the transpose), `IsMirror` (determinant < 0). Managed math only so the CLI runner can execute it (memory: no Unity ECalls in tests).

### `SheetLayers` (sealed class, pure, immutable after construction)
- `Layer { ConvexPolygon Original; Isometry2D ToDesk; bool FrontUp; int MovedBy; ConvexPolygon Desk }` — `Original` in Front-space of the flat sheet (the texture space), `Desk` = `Original.Transform(ToDesk)`, sheet-local. Order = bottom to top.
- `static SheetLayers Flat` — one layer, the sheet rect, identity, Front up, `MovedBy = -1`.
- `static SheetLayers Flat` is also the value of `SheetFolds.Layers` from construction (field initialiser), so `SheetOcclusion.Start`/`SheetBoundary.Start` read a valid stack whatever the execution order (review N7).
- `SheetLayers Apply(Fold fold, int foldIndex, out FoldEffect effect)`:
  1. `crease = FoldGeometry.CreaseOf(fold)`; `mirror = Isometry2D.Reflection(crease)`.
  2. For each layer bottom→top: `stay = Desk ∩ H−`, `lift = Desk ∩ H+` (`ClipToHalfPlane`); drop empty pieces. `stay` keeps `ToDesk`, `Original = ToDesk.Inverse` applied to `stay`; `lift` becomes `Original = ToDesk.Inverse(lift)`, `ToDesk = ToDesk.Then(mirror)`, `FrontUp` flipped, `MovedBy = foldIndex`.
  3. New order: all stays (same order), then the lifted pieces **in reverse order** (the top of the lifted pile lands at the bottom).
  4. Effect: `Lifted` (lift pieces, pre-reflection), `Landed` (reflected), `Outcome`: `NothingToFold` if no lift piece; `Overhangs` if any landed piece is not `ContainedIn(FoldGeometry.Sheet)` (tolerance `DepthEpsilon`); else `None`. `CreaseSegments` = crease line ∩ each lifted piece's boundary… precisely: the segment of the crease line inside each pre-fold layer polygon that the crease cuts (Liang–Barsky against the convex polygon), merged. `SeamSegments` = for each landed piece (bottom to top), its boundary edges that are not on the crease line, minus the interiors of the landed pieces *above* it (review N1: the pile lands reversed, so lower pieces' edges are hidden by upper ones; without this the preview would draw Seam lines across the middle of a stacked Flap). Segment-minus-convex-polygon is one shared helper (`ConvexPolygon.SubtractFromSegment`), also used by `WalkableOutline`. `CreaseMarks` (§ below).
  Returns the new stack; on `NothingToFold`/`Overhangs` the returned stack is still the correct one (callers refuse before using it).
- `float MaxDepth(FoldAnchor anchor)` — the largest depth at which every landed piece stays on the sheet, from the paper actually present: the minimum over all layer vertices `p` of a per-vertex bound. Edge anchor with outward `n`, half-extent `h`, `s = dot(p, n)`: `(3h − s) / 2`. Corner anchor with signs `(sx, sy)`, `u = sx·p.x`, `v = sy·p.y`: `min(2hx + hy − v, hx + 2hy − u)`. Derivation: a vertex lifts at depth `h − s` (edge) / `hx + hy − u − v` (corner), reflects to `2(h−d) − s` (edge) / `(X − v, X − u)` with `X = hx + hy − d` (corner); "inside the sheet" gives the bound, which is ≥ the lift depth for every vertex in the sheet, so "valid ⇔ d ≤ bound" for every vertex; a lifted piece's clipped vertices lie on the crease and reflect to themselves. Convexity of pieces means vertices suffice. Flat, this equals `FoldGeometry.MaxDepth(anchor)` (tested).
- `IReadOnlyList<ConvexPolygon> Footprint` — every layer's `Desk` polygon (the sheet wherever it lies).
- `CoverageResult Coverage(Rect faceFootprint, SheetFace face)`:
  - `q` = the footprint as a polygon in Front-space: Front → as is; Back → `x` mirrored (`SheetGeometry.BackToFront`, still a rect).
  - For each layer `L` with `L.FrontUp == (face == Front)`: `piece = (q ∩ L.Original).Transform(L.ToDesk)`, then subtract every layer above `L` (`ConvexPolygon.Subtract`, each result piece against the next layer) → convex parts, sheet-local.
  - Result: no parts → `None(Front ? Covered : Uncovered)`; exactly one part, from the Base layer (`MovedBy == -1`, identity), area ≈ footprint area → `Whole(Uncovered, footprint)` (Front only — the box can stay as authored); otherwise `Clipped(parts, coverage)` with `coverage` = `Covered` when the total area ≈ footprint area on Back (fully exposed), `Partial` otherwise.
- `CreaseMark { SheetFace Face; Vector2 A, B; Vector2 Shift }` — one crease line segment on one face of the paper, in that face's authored space (Front-space or Back-space), plus the unit direction the renderer offsets it by (so the half of the line under the landed Flap is not lost — the existing "shift the Front copy onto the Base side, the Back copy onto the Flap side" rule, generalised: on the face currently *up* the mark shifts to the staying side, on the face currently *down* to the lifted side). Produced per pre-fold layer the crease cuts: `desk segment → ToDesk.Inverse → Front-space`; the Front mark is that, the Back mark is `BackToFront` of it; shift normals are mapped the same way (linear part only).

### `FoldEffect` (readonly struct)
`Fold Fold; FoldOutcome Outcome; IReadOnlyList<ConvexPolygon> Lifted, Landed; IReadOnlyList<(Vector2 a, Vector2 b)> CreaseSegments, SeamSegments; IReadOnlyList<CreaseMark> CreaseMarks`. `float DistanceTo(Vector2 p)` = min distance to any crease or Seam segment. `Rect`-overlap helpers: `OverlapsRect(Rect)` (any lifted or landed piece), `Overlaps(FoldEffect other)` (any piece of either vs any of the other).

### `FoldValidity` (static)
- `PlayerOverlapsFlap(in FoldEffect, Rect player)` = `effect.OverlapsRect(player)`.
- `Independent(in FoldEffect a, in FoldEffect b)` = `!a.Overlaps(b)`.

### `SheetFolds` (MonoBehaviour) — new/changed surface
Inspector (new): `[Header("Multiple folds")] bool allowMultipleFolds = true` — *"Allow more than one fold on the sheet at a time. Off: the sheet must be unfolded before it can be folded again."*; `bool allowStacking = true` — *"Allow a fold to lift or land on another fold's Flap (the Flap side of the crease is folded over however many layers deep). Off: every fold must be independent of the others — its lifted and landed regions may not touch theirs. Ignored when multiple folds are off."* Existing tunables unchanged (`minDepth`, `unfoldDuration`, `unfoldEase`, `rememberCreases`).

Public: `IReadOnlyList<Fold> Folds` (unchanged), `IReadOnlyList<FoldEffect> Effects` (one per fold, from the replay), `SheetLayers Layers` (after all committed folds), `bool CanStartFold => allowMultipleFolds || folds.Count == 0`, `FoldRejection Evaluate(Fold fold, Rect playerLocal)`, `SetPreview(Fold?, Rect)` (clamps depth to `Layers.MaxDepth`, not a static bound — review B1), `TryCommit(Fold, Rect, out FoldRejection)` (overhang is judged by the effect's `Outcome`, never by a static bound), `TryUnfoldAt(Vector2, Rect, float grabDistance, out FoldRejection)`, `Reset()`, `event Changed`.

`FoldRejection` gains `NothingToFold` (*"no paper on the Flap side of the crease"*), `OverlapsFold` (*"would lift or land on another fold's Flap; stacking is off"*), `CoveredByLaterFold` (*"a later fold lies on or through this one; unfold that first"*). `AlreadyFolded` is kept for multiple-off.

### `SheetOcclusion`
No `PoseBack`, no `PosedBackZ`. `Apply()` → `Notify(sheet.Front, Front)` / `Notify(sheet.Back, Back)`, each occludee gets `folds.Layers.Coverage(occludee.FaceLocalFootprint(faceRoot), face)` when the sheet is the Screen (the not-Screen and flat shortcuts stay as they are), and `OnFoldCoverageChanged(result, sheet.transform)`. The Back root is at identity for good: Back-space coordinates coincide numerically with sheet-local, and a Back object's collider is the only thing that has to be somewhere — its polygon path is set in the object's local space to the landed shape. (Consequence recorded in §9: a *dynamic* Back object would need to pose itself from its coverage; none exists.)

### `RenderTextureFoldRenderer`
- Replays the display's folds through `SheetLayers` (visuals in display order: committed, retreating, preview). Per layer one pooled `MeshPart` under the sheet: texture = Front or Back by `FrontUp`; UV of a desk vertex = `UvOf(ToDesk.Inverse(v))` for Front-up, `UvOf(BackToFront(ToDesk.Inverse(v)))` for Back-up; z = `BaseZ − order × LayerZStep` (constants `BaseZ = 0.5`, `LayerZStep = 0.01`, `SeamZ = 0.01`; ≥ 48 layers is reported once as an error and drawn clamped — far beyond anything reachable); tint = `previewTint` / `invalidTint` / `retreatingTint` / white by the state of the visual whose index is `MovedBy` (Base and committed-moved layers: white). Unused pooled parts get an empty mesh.
- Crease marks: from each replay effect, per face mesh (live) offset by `Shift × creaseWidth/2`; remembered marks unshifted, as now. Same four face crease parts as today.
- Seam: `SeamSegments` of every `Preview`/`PreviewInvalid` visual's effect, as now.
- `backCamera.enabled` = any Back-up layer exists.

### `WalkableOutline`
`Segments(IReadOnlyList<ConvexPolygon> paper)`: for every edge of every polygon, subtract the open interior of every other polygon **and** any collinear opposite-direction edge overlap of another polygon (paper continues across); the surviving pieces are boundary. Kind: `SheetEdge` with its `GridDirection` when the piece lies on that side of the sheet rect (same outward), else `Wall`. Then the existing collinear `Merge` (which also de-duplicates the same edge contributed by two stacked layers). `IsConvexAt` unchanged. Outward of an edge = the polygon's outward (winding-aware: computed from the signed area).

### `FoldDragInput`
Press (not dragging): `screen.Folds.TryUnfoldAt(...)` first — hit → return (log the rejection if any). Else if `folds.CanStartFold` and an anchor resolves → begin drag. `DragFold` = `new Fold(anchor, Mathf.Min(Snap(FoldGeometry.DepthForDragPoint(anchor, local, bound)), bound))` with `bound = folds.Layers.MaxDepth(anchor)` — the only clamp (review B1: the layer bound is always ≥ the flat one, so a static clamp anywhere on the path would make the deeper folds unreachable). The rest is unchanged; no new tunables.

## 6. Behaviour

**Replay.** `SheetFolds` keeps the `folds` list as the data model; after every commit/unfold/reset it rebuilds `Layers`/`Effects` by applying the folds in order to `SheetLayers.Flat`. All physics (occlusion, boundary) and rendering derive from that.

**Evaluate(fold, player)** — in this order, first failure wins: `AlreadyFolded` (multiple off and a fold exists) → `TooShallow` (`Depth < minDepth`, commit only; the preview shows a shallow fold as-is like today) → apply to `Layers` → `NothingToFold` → `Overhangs` → `CoversPlayer` (lifted or landed overlaps the player) → `OverlapsFold` (stacking off and the effect is not independent of every committed effect) → `None`. `SetPreview` uses it to pick `Preview`/`PreviewInvalid` (`NothingToFold` draws nothing and is not red — there is nothing to show; the release then does nothing, logged like `TooShallow` is not).

**Commit.** Append, replay, `Changed`, draw.

**Unfold at p.** Iterate folds last→first; the first whose `Effects[i].DistanceTo(p) ≤ grabDistance`: if any later effect is not independent of it → `CoveredByLaterFold`, refused; if `FoldValidity.PlayerOverlapsFlap(effect, player)` (lifted ∪ landed — the one rule, review N8) → `PlayerOnFlap`, refused; else remove it, remember its crease marks (if `rememberCreases`), replay, `Changed`, and animate the retreat: the retreating visual is that fold at shrinking depth appended *after* the committed folds in the display — legitimate because it was independent of every later fold (independent folds commute — pinned by a test, review N2), and at intermediate depths it is drawn on top, visual only. A preview begun during a retreat is drawn on top of the retreating visual but evaluated against the committed stack only; transient and visual-only, said in the code.

**Multiple off.** Exactly today's behaviour: one fold, then only crease/Seam clicks; a press elsewhere does nothing (no drag begins).

**Stacking off, multiple on.** Every fold lifts Base only and lands on Base only, never touching another fold's regions: N+S strips, E+W, opposite corners, corner + far edge… all legal; a crossing drag tints red. Unfold any of them in any order.

**Stacking on.** A crease crosses whatever paper is there: the Flap side of *every* layer lifts and lands reversed on top. Double-reflected pieces are Front-up again (correct paper behaviour: fold the north strip down, then fold the west edge over across it — the corner of the north Flap comes around showing Front art again). Landing into a region emptied by an earlier fold is legal (paper on desk); its far edge, inside the sheet rect, is a wall, not an exit. A fold whose Flap side holds no paper (dragging a sheet edge that has already been folded away, at a depth short of the remaining paper) is `NothingToFold`; drag further and it becomes a fold of the remaining paper. Depth is limited live to the exact `MaxDepth` for the present paper, so a south fold after a south fold can go deeper than half the sheet when the paper there is shorter. Unfold order: a fold with a later fold on/through it is refused with `CoveredByLaterFold`; unfold the later one first.

**Player.** Unchanged rule, exact: red and refused if any lifted or landed piece touches the player; unfold refused with the player on the landed pieces.

**Terrain.** For each occludee: the parts of its footprint that are on a top-visible layer of its face, placed where that layer lies — hidden under a landed Flap, exposed on a Back-up Flap, brought back Front-up by a fold-on-fold. Polygon collider paths as today (`OccludedBoxCollider` unchanged in substance).

**Walls & exits.** From the union of all layers: sheet-rect edges of paper are exits; creases and landed edges over empty desk are walls. Concave corners (possible now) are handled by `IsConvexAt` as before.

**Failure modes.** Missing renderer/roots: as today (reported by `Sheet`). Replay never throws: empty pieces are dropped, an effect with `Outcome ≠ None` is never committed, `Layers` is always consistent with `Folds`. `MaxDepth` with no paper on that side returns the bound of the remaining vertices (never negative: every vertex is inside the sheet, so every bound ≥ 0); a 0 depth is `TooShallow`. Pool overflow in the renderer: error once, clamp z. `WalkableOutline` given an empty footprint (cannot happen — the Base can be entirely lifted? No: a fold lifts the Flap side only, so paper always remains on the other side — asserted by the `NothingToFold`/`Overhangs` rules) returns no segments.

## 7. Interfaces & seams

- `IFoldRenderer` unchanged in role; `FoldDisplay` carries folds + remembered marks. The renderer does its own replay (it must never read the model).
- `IFoldOccludee` unchanged in role; parts are now sheet-local. Any future occludee that must *move* (a Back pushable) reads its parts the same way — that is the seam, and it is where "objects ride the Flap" (#7) would be implemented.
- `SheetLayers` is the one place the fold-on-fold physics lives; `FoldGeometry` keeps the crease families and static limits. Cutting stacking = set the toggle off; cutting multiple folds = the other toggle. Removing the feature for good = deleting the two fields and the `OverlapsFold`/`CoveredByLaterFold` branches — the layer model stays (it is also the single-fold model).

## 8. Testing

Edit Mode (pure, run through the CLI runner from memory; no Transform/physics):
- Commutation: for the independent pairs N+S, E+W, NE+SW corners, NE corner + W edge, replaying `[a, b]` and `[b, a]` gives the same set of layer polygons and faces (review N2).
- `CreaseMark.Shift`: a Back-up layer cut by a second crease — the Back-space mark shifts toward the staying side and the Front-space mark toward the lifted side, checked numerically on N d=2 then W d=3 (review N6). `FoldEffect.DistanceTo` is computed at commit time; a later dependent fold pins the earlier one, so the hit-test on the moved Seam gives `CoveredByLaterFold` (tested through `SheetFolds`? no — pure: the earlier effect's segments are unchanged, asserted).
- `Isometry2D`: reflection is an involution; `Then` composes; `Inverse` round-trips; `IsMirror` flips on odd reflections.
- `SheetLayers`: (a) flat + south edge fold d=3: two layers; lifted = strip y<−1.25, landed = [−1.25, 1.75] full width, top layer Back-up, `Original` of the Flap = the strip, `Desk` = the landed strip; crease segments = the full crease; Seam = the reflected south edge at y=1.75; crease marks: Front mark shifted north (staying side), Back mark shifted south in Back-space. (b) corner NE d=2: triangle lifted, landed triangle with the reflected corner inside. (c) `MaxDepth` flat = `FoldGeometry.MaxDepth` for all 8 anchors; after south d=2, `MaxDepth(EdgeSouth) = 5.25`; corner bound after a same-corner fold. (d) `NothingToFold`: north d=2 then north d=1. (e) `Overhangs`: a depth just above `MaxDepth`. (f) stacking N d=2 then S d=4: three layers, footprint union = y∈[−0.25, 3.75]; top layer at y∈[2.25,3.75] is over empty desk. (g) fold-on-fold N d=2 then W d=3: four layers; the piece that was the north Flap's west end is Front-up again, `MovedBy = 1`, with its `ToDesk` a rotation (two mirrors); layer order = stays then lifted reversed. (h) `Coverage`: Front rect fully under a landed Flap → `None/Covered`; partially → `Clipped` pieces equal to the old rect-subtraction; Front rect on the Base clear of everything → `Whole`; Back rect under the lifted strip → one exposed piece whose vertices equal the old `BackToLanded` mapping (checked numerically); after fold-on-fold, a Front rect in the double-reflected piece appears at the rotated position.
- `FoldValidity`: player on the strip, under the landing, clear; independence of N+S vs N+W.
- `WalkableOutline`: flat 4 edges; edge fold (crease `Wall`, far/side edges); corner pentagon; closed loop and no degenerate/duplicate segments at `MaxDepth`; stacked (f): four segments, N/S are `Wall`, E/W `SheetEdge`; fold-on-fold (g): closed, every `SheetEdge` piece lies on the sheet rect, every `Wall` piece has no paper on its outward side (sampled), convexity counts.
- `ConvexPolygon.Subtract`: rect minus overlapping rect = the old `FoldGeometry.Subtract` pieces (area and count); minus a disjoint polygon = itself; minus a containing polygon = empty; total area = A − |A∩B|.
- Existing `FoldGeometry` tests for kept functions stay; the removed-function tests go — explicitly including the three overhang tests (`FoldValidityTests.PlayerOnOverhang_CountsAsOnFlap`, `WalkableOutlineTests.Convexity_Overhang_*`, `CornerFold_DeeperThanHeight_HasWalkableOverhang`): those depths are `Overhangs` in the model and cannot exist (review N6).

Compile via the CLI Roslyn check; `python Tools/check_links.py` is not needed (no YAML), but run anyway — cheap.

Manual, Desk scene (I cannot run Play Mode; listed for Aaron): (1) toggles off/off → exactly the old build; (2) on/off → N then S fold, crossing E fold tints red; unfold either first; (3) on/on → N d≈2 then W across it: the corner comes around Front-up; terrain there is Front terrain at the rotated spot; walls follow; unfold W then N; clicking N's seam under W refuses (console: `CoveredByLaterFold`); (4) S then S deeper than half; (5) player on a landed Flap, fold across it → red; unfold under the player → refused.

## 9. Assumptions

1. Both toggles default **on** (Aaron asked to try them; the base prefab carries the defaults — nothing serialized changes).
2. One toggle covers "land on a Flap" and "lift through a Flap": physically the same act (§2). If Aaron wants them separated, `SheetLayers.Apply` can refuse lifting a `MovedBy ≠ −1` layer under a third flag — a one-line seam, not built now.
3. Edge/corner grabs stay at the **sheet rect's** edges and corners (where the drag input already looks), not at the current paper outline; a grab on an emptied edge is a fold of whatever paper the crease reaches (`NothingToFold` until it does).
4. Unfold order rule: a fold under or cut by a later fold cannot be unfolded first (`CoveredByLaterFold`). Physically obvious — the paper on top pins it — so not asked.
5. Unfold hit-test takes precedence over an edge/corner grab on the same press.
6. `Clicking` a covered/pinned fold's Seam gives a console log only, like `PlayerOnFlap` today (no visual cue yet — same status).
7. The Back root stays at identity; Back content's physical presence is its clipped collider (already true for terrain). No dynamic Back objects exist.
8. Layer z spacing 0.01 under the Base at z 0.5, Seam at 0.01: engineering constants.
9. Retreat animation appends the retreating fold last (independent folds commute).
10. `SheetLayers` replays on every change rather than caching incrementally: at most a handful of folds; simplicity wins.

## 10. Open questions

**Answered by Aaron (step 5):** Q1 → *(b)*: *"just remove the unfold trigger there and replace it, since we can already unfold at the inner edge of the fold where the tape goes which is the intent."* Q2 → *"one toggle."*

Consequences folded into the plan:
- **Unfold is Seam-only.** `FoldEffect.DistanceTo` → `DistanceToSeam`; the crease no longer unfolds. Bible §4 Unfold entry updated (crease click removed, Aaron 2026-08-26).
- **Crease grab.** On a press, after the Seam hit-test: if the press is within `creaseGrabDistance` (new Inspector tunable on `FoldDragInput`, default 0.3, *"A press within this distance of an existing fold's crease grabs that fold line to fold it further"*) of the crease segments of an unpinned fold (no later fold on/through it — the same set that can be unfolded), a drag starts with `anchor = fold.Anchor` and `grabDepth = fold.Depth`; otherwise the sheet-rect edge/corner grab as before with `grabDepth = 0`. `FoldGeometry.DepthForDragPoint(anchor, local, grabDepth)` = the old value + `grabDepth / 2`: the grabbed fold line (at `g` in from the edge) reflects across the new crease at `d` to `2d − g` in, so putting it under the cursor at `c` in gives `d = (c + g)/2` — for corners the same with the mean of the two inward distances (the old crease's midpoint at `(g/2, g/2)` maps to `(d − g/2, d − g/2)`). Dragging back out (`d < g`) lifts nothing → `NothingToFold`, nothing drawn. With stacking off a crease grab always overlaps the fold it came from → red; consistent, no special case.
- `FoldEffect.DistanceToCrease` kept for the grab. Scene: `Desk.unity` gets no edit if the old `creaseGrabDistance` name is not present (checked below); the `[FormerlySerializedAs("creaseGrabDistance")]` on `unfoldGrabDistance` is dropped so the new field can own the name — see Deviations if the scene still carries the old name.
- Q2: one toggle, as planned.

No further open questions. Noted for the report: assumption 2 (one toggle for both stacking senses) is the interpretation of Aaron's "stacking"; if he wants "land on but never lift through" as a third mode, it is a small addition.

## Deviations

Engineering-only deviations found while coding (no design change):

1. **Seam excludes landed edges on the sheet's border.** A landed piece's edge that lies along the sheet's own edge meets nothing there (the previous build did not draw or hit-test it either); `SheetLayers.SeamOf` skips such edges. Also, a lower landed piece's edge shared with an upper piece is subtracted (closed subtraction) so a stacked landing draws each visible edge once.
2. **`FoldGeometry.DepthForDragPoint` has no upper clamp** and takes `grabDepth`; the caller clamps to `SheetFolds.MaxDepth(anchor)` (= `Layers.MaxDepth`). `TryClipLineToRect`, `Subtract(Rect, Rect)` and the other single-fold helpers were removed with their tests (replaced by `ConvexPolygon` equivalents and `SheetLayersTests`).
3. **`ConvexPolygon` grew** `Intersect`, `Subtract`, `Transform`, `Contains`, `ContainedIn`, `OutwardNormal`, `TryClipLine`, `SubtractFromSegment(closed)`; `WalkableOutline` uses the last two rather than a bespoke rect subtraction.
4. **Renderer z:** `BaseZ = 0.5`, `LayerZStep = 0.01`, `SeamZ = 0.005` (49 layers fit before the one-time error).
5. **Preview validity:** `NothingToFold` previews are drawn as nothing and not red (nothing to tint); `TooShallow` is only a commit rejection, as before.
6. `FoldRejection.CoveredByLaterFold` logs to the console like `PlayerOnFlap` (no visual cue yet — same status as before).

## Review

Round 1 (plan-reviewer, isolated). Verdict: BLOCK (one blocking finding).

| # | Finding | Disposition |
|---|---|---|
| B1 | Every depth path still clamped to the static half-extent, so the promised deeper fold-after-fold was unreachable; `Layers.MaxDepth` ≥ static bound always. | **Accepted.** `DepthForDragPoint` takes the bound as a parameter; input, `SetPreview` and commit use `Layers.MaxDepth` / the effect's `Outcome` only; `Fold.Clamped`/`Overhangs` removed. |
| N1 | Seam segments of stacked landed pieces would draw along hidden edges. | **Accepted.** Non-crease edges of each landed piece minus the landed pieces above it. |
| N2 | "Independent folds commute" asserted, not tested; preview-during-retreat ordering. | **Accepted.** Commutation test added; code comment on the transient. |
| N3 | Dead drag zone when grabbing an edge that was folded away. | **Escalated** → Q1 to Aaron. |
| N4 | "paper" in identifiers/docs. | **Accepted.** Renamed. |
| N5 | Three Bible passages made false and not listed. | **Accepted.** Listed. |
| N6 | Overhang tests' removal implicit; `Shift` and moved-Seam untested. | **Accepted.** Tests added / removals listed. |
| N7 | `Layers` before `Start` under unknown execution order. | **Accepted.** Field initialiser = `Flat`. |
| N8 | Unfold player rule narrower than the existing one for no gain. | **Accepted.** One rule: lifted ∪ landed. |
| Q1 | How to grab a sheet edge that has been folded away. | **Asked Aaron** (step 5). |
| Q2 | One toggle for "land on" and "lift through", or a third toggle. | **Asked Aaron** (step 5). |

## Code review

Round 1 (code-reviewer, isolated). Verdict: REJECT (one must-fix).

| # | Finding | Disposition |
|---|---|---|
| M1 | A pinned fold's Seam (frozen at commit time) can lie on a later fold's crease; `TryUnfoldAt` returned `CoveredByLaterFold` there and the input never tried the crease grab. | **Accepted.** `TryUnfoldAt` skips pinned Seams (remembering the rejection); `FoldDragInput` tries the crease grab and the edge/corner grab before logging it. Test added (`AnEarlierFoldsEffect_IsUnchangedByALaterFold_...`). |
| S1 | `IsFolded`, `Preview` unused. | **Accepted.** Removed. |
| S2 | `Crease.AngleDegrees` unused. | **Accepted.** Removed. |
| S3 | Corner `MaxDepth` untested after a fold. | **Accepted.** `MaxDepth_Corner_AfterAnEdgeFold_FollowsTheNarrowerSheet` (10.5 after a west fold of 2; 10.6 overhangs). |
| S4 | Moved-Seam / pinned path untested. | **Accepted.** Test above asserts an earlier effect is unchanged and its Seam lies on the later crease. |
| S5 | Bible §11 #3 still said click-crease unfold. | **Accepted.** Updated. |
| S6 | No `.meta` for the five new files. | **Accepted.** Hand-written metas, GUIDs `f01d0030..34…`, checked unique. |
| Q1 | A crease grab makes a second fold (two creases, two unfolds) rather than moving the first crease. | **Reported to Aaron** in the final report as the behaviour built; a "move the crease" variant is a small change if wanted. |

Round 2: focused re-review of `SheetFolds.cs` / `FoldDragInput.cs` after M1 — see below.

Round 2 (focused, code-reviewer). Verdict: APPROVE WITH FIXES.

| # | Finding | Disposition |
|---|---|---|
| S1 | The pinned/unpinned press precedence lived in `SheetFolds` (a MonoBehaviour) and was untestable by the CLI runner. | **Accepted.** Lifted into pure `FoldHitTest` (`IsPinned`, `SeamAt`, `CreaseAt`); `SheetFolds` delegates. `FoldHitTestTests` covers scenarios (a)–(e): unpinned Seam, pinned Seam on a later crease → crease grab wins, pinned Seam alone → `CoveredByLaterFold`, coincident pinned/unpinned Seams, player on the fold, independent folds. |

Scenarios (a)–(e) were walked by the reviewer and found correct; no design decision taken.
