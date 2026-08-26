# Plan — Folding adjustments: no overhang, unfold on the seam, flap edge border

Date: 2026-08-26. Follows `2026-08-26-folding.md`; same architecture, three behaviour changes.

## 1. Task

Aaron, after playing the first fold build (verbatim):

> "First, I think I don't actually want to allow extension past the edge on folds. With the shape of the paper and size of the screen, I think there's just not enough room to make that interesting. Also opens some weird edge cases that I don't want to answer. Second, when unfolding, clicking on the actual crease is fine, so you can leave that in, but I was more imagining that I would click on the seam where the front of the paper when it is unfolded meets the back, which would be in the middle of the paper somewhere, where you would put the tape to hold the fold down if you wanted to do that (I'm thinking I might add a tape visual for this at some point actually, but for now just anywhere on the seam works). Also, if you wouldn't mind adding a little border where the edge of the paper is during a fold, that would be great - its possible to tell where it is, but its hard to tell for sure. That can go away after the fold is confirmed"

Deliverables:
1. **No overhang.** A fold's landed Flap must lie within the sheet. The drag clamps depth so it never leaves the sheet.
2. **Unfold on the seam.** Clicking near the seam — the landed Flap's outline minus the crease, i.e. where the Flap's edge meets the Front — unfolds, in addition to the crease.
3. **Flap edge border** while dragging (preview), gone once committed.

**Out of scope:** the tape visual (Aaron: "at some point"); anything else about folding.

## 2. Design references

- Bible §4 Geometry `[LOCKED]`: *"Flaps may extend past the sheet's boundary. A corner flap can overhang the desk. That's legal and should render."* — **reversed by Aaron's explicit instruction above**; the Bible line is updated to say so. Design Doc "Valid Folds: Folds extending beyond the sides of the screen (large corner folds, for example)" is likewise superseded; noted in the Bible, the Design Doc is Aaron's to edit.
- Consequences: Bible §5 "overhang walkable" note, §11 #18 (arrival from an overhang) and the round-2 review's `PosedBackZ` rationale about overhangs become moot — recorded, not removed (the geometry stays general; only the reachable depths change).
- Bible §4 Unfold (decided 2026-08-26): extended — crease *or* seam.
- Implementation-guidelines §4a: border width/colour and the seam grab distance are Inspector values.

## 3. Decisions already made by Aaron

The three quoted above. Earlier: click-crease unfold stays.

## 4. Files

| File | Action | Purpose |
|---|---|---|
| `Scripts/Fold/FoldGeometry.cs` | modify | `MaxDepth` now = the largest depth whose landed Flap stays on the sheet (edge: half the extent; corner: the sheet height). New `SeamSegments(fold)` and `DistanceToSeam(fold, p)`. |
| `Scripts/Fold/SheetFolds.cs` | modify | `TryUnfoldAt` accepts a click within the grab distance of the crease **or** the seam. |
| `Scripts/Fold/FoldDragInput.cs` | modify | Tunable renamed `creaseGrabDistance` → `unfoldGrabDistance` (applies to crease and seam). |
| `Scripts/Fold/RenderTextureFoldRenderer.cs` | modify | New "Flap Edge" mesh part: the seam drawn as a line for `Preview`/`PreviewInvalid` visuals only. Tunables `flapEdgeWidth`, `flapEdgeColor`. View gating no longer expands the Screen (no overhang). |
| `Scripts/Fold/WalkableOutline.cs` | — | Unchanged (general); with no overhang the mirrored rect is inside the sheet, so the outline is the sheet minus the lifted region. |
| `Tests/EditMode/FoldGeometryTests.cs` | modify | `MaxDepth`/`DepthForDragPoint` clamp tests (edge 2d ≤ extent, corner d ≤ H); `SeamSegments` for an edge fold (one segment at 2d from the edge) and a corner fold (two legs meeting at the reflected corner); `DistanceToSeam`. Existing deep-fold geometry tests stay (geometry is still general) but are labelled as unreachable through input. |
| `Scripts/Fold/Fold.cs` | modify | `Clamped` (depth limited to `MaxDepth`). |
| `Documents/Claude Bible.md` | modify | §2 glossary gains **Seam**; §4 geometry line reversed with Aaron's wording; unfold entry extended; §5/§11 overhang notes marked moot. The Design Doc's "Folds extending beyond the sides of the screen" is Aaron's to edit — flagged in the report (review N6). |

## 5. Components & data

- `FoldGeometry.MaxDepth(anchor)`: edge N/S → `H/2`; E/W → `W/2`; corner → `H` (the landed square of side d at the corner is inside the sheet iff d ≤ H, since H < W). `DepthForDragPoint` clamps to it as before, so dragging past the limit just holds the Flap at the edge.
- `FoldGeometry.SeamSegments(fold)`: **the mirror image of the anchoring sheet edge(s)** — the anchoring edge (edge fold) or the two corner-adjacent edges (corner fold), each clipped to the Flap side of the crease, then reflected. Edge fold → one segment at 2d from the edge; corner fold → the two legs meeting at the reflected corner. (Review B1: a rule based on "off the crease" endpoints excluded the legs.) At `MaxDepth` an edge fold's seam coincides with the far sheet edge and is still returned. `DistanceToSeam` = min distance to those segments.
- `SheetFolds.TryUnfoldAt(local, playerLocal, grabDistance, out rejection)`: hit iff `min(DistanceToCrease, DistanceToSeam) ≤ grabDistance`.
- **The no-overhang rule lives with the data** (review B2): `Fold.Clamped` / `FoldGeometry.MaxDepth`; `FoldDragInput` clamps *after* snapping; `SheetFolds.TryCommit` rejects `Depth > MaxDepth` with `FoldRejection.Overhangs` and `SetPreview` clamps, so no path (snap included) can commit an overhanging fold.
- `FoldDragInput`: `[Header("Grab")] float unfoldGrabDistance = 0.3f` (`[FormerlySerializedAs("creaseGrabDistance")]`, so the scene value carries over without a YAML edit — review N4) — "A press within this distance of a folded sheet's crease or Seam unfolds it."
- `RenderTextureFoldRenderer`: `[Header("Seam")] float seamWidth = 0.08f`, `Color seamColor = (0.15, 0.12, 0.1, 0.9)` (one neutral colour for valid and invalid previews; the Flap tint carries the red — review N7); a fifth `MeshPart` "Seam" at its own `SeamZ = 0.015` (review N2) using `creaseMaterial`; built from `SeamSegments` of every visual whose state is `Preview` or `PreviewInvalid`; empty otherwise (so it vanishes on commit, and is absent during retreat). The border traces the Seam only — the Flap's edge that moved; the strip's sides coincide with the sheet's own edge (review Q1; stated in the report).

## 6. Behaviour

- Drag a south edge up past the middle: depth holds at `H/2`; the Flap's edge sits on the north edge and no further. Drag a corner past the diagonal: holds at `d = H` (the crease reaches the opposite long edge).
- While dragging, the Flap's outline where it meets the Front is drawn as a line (`flapEdgeColor`); it disappears on release whether committed or refused.
- Folded: click within `unfoldGrabDistance` of the crease or of the Flap's edge → unfold (still refused with the player on the Flap).
- Nothing else changes: walls, exits, occlusion, textures.

## 7. Interfaces & seams

No new seams. `SeamSegments` is the one place "the seam" is defined; the future tape visual would use it.

## 8. Testing

- Edit Mode: `MaxDepth` values; `DepthForDragPoint` clamps for a far drag (`H/2` for a south edge — the existing test expecting `H` changes); `SeamSegments` count/positions for edge and corner folds, incl. the corner legs' endpoints; `DistanceToSeam` on / near / far; `LandedRegion` at `MaxDepth` stays inside the sheet for all 8 anchors; `WalkableOutline` at exactly `MaxDepth` for an edge and a corner anchor is closed with no zero-length or duplicate segments (review N1). Existing deep-fold tests (`CornerFold_DeeperThanHeight_*`, `PlayerOnOverhang_*`, `Convexity_Overhang_*`, `HiddenRect` sampling at d = 10) stay as geometry tests, commented as unreachable through input (review N3).
- CLI compile + `check_links.py` (scene field rename).
- Manual: (1) drag every edge and corner to the far side — the Flap stops at the sheet edge; (2) the edge border shows while dragging, gone on release; (3) click the seam → unfold; click the crease → unfold; (4) walls/exits unchanged.

## 9. Assumptions

1. "Extension past the edge" means the *landed* Flap; the lifted region is inside the sheet by definition.
2. The seam of a corner fold is both legs of the landed triangle.
3. `PosedBackZ` stays: it is harmless and still keeps face cameras independent.
4. Existing deep-fold tests remain as geometry tests; the rule lives in `MaxDepth`, not in the geometry.

## 10. Open questions

None.

## Review

Plan-reviewer round 1 (2026-08-26): **BLOCK** — 2 blocking, 7 non-blocking, 1 question.

| # | Finding | Disposition |
|---|---|---|
| B1 | Seam rule excluded a corner fold's legs. | **Accepted** — seam = mirror image of the anchoring edge(s) clipped to the Flap. |
| B2 | Snap applied after the clamp; model never checked `MaxDepth`. | **Accepted** — clamp after snap; `TryCommit` rejects `Overhangs`; `SetPreview` clamps. |
| N1 | Test at exactly `MaxDepth`. | **Accepted**. |
| N2 | Seam shared z with creases. | **Accepted** — `SeamZ`. |
| N3 | Existing tests exercising unreachable depths. | **Accepted** — one expectation changed, the rest labelled. |
| N4 | Scene YAML rename vs `FormerlySerializedAs`. | **Accepted** — `FormerlySerializedAs`. |
| N5 | "Seam" naming/glossary. | **Accepted** — glossary + `Seam*` names. |
| N6 | Design Doc still lists overhang folds as valid. | **Accepted** — flagged to Aaron in the report. |
| N7 | One border colour for valid/invalid. | **Accepted** — stated; Flap tint carries the red. |
| Q1 | Seam only, or the whole landed outline? | Seam only, stated to Aaron in the report (the strip's sides coincide with the sheet edge). |

## Code review

Code-reviewer round 1 (2026-08-26): **APPROVE WITH FIXES** — 0 must-fix, 5 should-fix, 2 questions.

| # | Finding | Disposition |
|---|---|---|
| S1 | `SetPreview` validated the unclamped fold. | **Accepted** — clamp once, use for both. |
| S2 | Bible §4 Input said "crease click" only. | **Accepted**. |
| S3 | Bible §7 overhang clause left dangling. | **Accepted** — removed. |
| S4 | Bare `1e-5f` tolerance in two places. | **Accepted** — `FoldGeometry.DepthEpsilon`. |
| S5 | `SeamSegments` doc overstated generality. | **Accepted** — documented as exact up to `MaxDepth`. |
| Q1 | At exactly `MaxDepth` an edge fold covers the whole sheet, so it is always refused as `CoversPlayer`; effective max is just under `H/2`. | To Aaron in the report; no change (the rules are exact). |
| Q2 | Border traces the Seam only. | To Aaron in the report. |

Also: the CLI test runner (`Runner.cs`, scratchpad) did not execute `[TestCase]` methods; extended to run them.

## 11. Addendum — creases are part of the sheet (2026-08-26)

Aaron: *"the crease lines, when you make another fold that moves the paper away from where the crease was, just remains as a straight line, instead of working with the paper … the crease lines should instead be a part of the paper basically, so that if a fold brings part of a crease line around, it follows - like a piece of paper do"*

**Cause:** `RenderTextureFoldRenderer` draws creases (live and remembered) as composite overlays at sheet z, so a later fold that lifts or covers that part of the sheet leaves the line on the Desk.

**Change (renderer only, behind `IFoldRenderer`):** crease lines become face content. The renderer keeps its crease mesh parts but parents them under the face roots, on the face layers, so the face cameras bake them into the textures and compositing carries them with the paper:
- Under `Front` (Front-space): "Creases" and "Remembered Creases" at face-local z `−0.1` — in front of every authored face element (surface 0, terrain sprites −0.05), as a mark on the sheet should be (review B1). `CreateMeshPart` gains parent and layer parameters; Base/Flap/Seam stay on the sheet root (review N4). Missing face root → no parts for that face (review N3); missing layers → parts keep the root's layer and `FoldLayers` logs once (review N2).
- Under `Back` (Back-space, so every point is `BackToFront`-mirrored: `(x, y) → (−x, y)`): the same two parts — a crease shows on both faces of the sheet, so a crease brought around on a landed Flap is visible.
- The composite-level "Creases"/"Remembered Creases" parts are removed. The Seam border (preview only) stays a composite overlay: it marks where the Flap's edge is, not a mark on the paper.
- Live creases (committed, preview and retreating folds) are drawn on the faces too: the crease line sits on the Base side of the fold, so it shows on the Base and — on the Back copy — on the landed Flap's edge.
- Tunables unchanged (`creaseWidth`, `creaseColor`, `rememberedCreaseColor`). `CreaseZ` is replaced by the face-local constant `FaceCreaseZ = −0.1`.
- A live crease drawn centred on the fold line would show at half width (the Flap covers the far half with the mirror image of the near half — code review S1). The Front copy is therefore shifted wholly onto the Base side and the Back copy wholly onto the Flap side, which reflects onto the same strip: one full-width line.
- Sheet layers: `Sheet.Awake` assigns face layers to the subtrees at start; parts created later set their layer explicitly (`FoldLayers.LayerOf(face)`).

**Behaviour:** fold south, unfold (crease at y = c remembered on both faces), fold north deep enough to cover y = c → the crease disappears under the Flap; fold a corner that lifts the end of the crease → that part is gone and, if the landed Flap exposes the Back at the mirrored place, the crease appears there, mirrored, as a real sheet does.

**Testing:** renderer-only, no pure logic to unit-test; manual: the sequence above, a remembered crease crossing a Test Wall/Water sprite stays visible over it, the preview two-tone, folding with the layers removed does not throw, plus: creases never draw on the Desk; the Back copy's mirroring (a crease near the east edge on the Front shows near the west edge in Back-space, i.e. under the same spot when the sheet is turned over — verify by a west edge fold revealing it). CLI compile.

**Bible:** §4 rendering note: "Creases are face content (drawn under Front and mirrored under Back), so they fold with the sheet."

Plan-reviewer on §11 (2026-08-26): **APPROVE WITH CHANGES** — B1 crease z below terrain sprites (accepted: −0.1); N1 preview two-tone (accepted, stated); N2 layer guard (accepted); N3 null face root (accepted); N4 `CreateMeshPart` signature + stale text (accepted); N5 "paper" in prose (accepted); N6 manual steps (accepted).

Code-reviewer on §11 (2026-08-26): **APPROVE WITH FIXES** — S1 live crease half-width (accepted: strips shifted to the Base/Flap sides so the line reads full width; Q1 thereby moot); S2 missing-layers path (note only; matches design). Trivial fix, no re-review.
