# Plan: Terrain regions for Map 1 on Sheet (0,0), removal of test content

## 1. Task

Aaron: *"Can you now add colliders/terrain where there should be? So collision around the walls and the trees, and water terrain over the lake but not the island? Also remove the test walls, buttons, and stuff from this sheet as well."*

Context: `Map 1.png` (2550×3300, 300 DPI, drawn portrait, placed rotated +90° so it lies landscape) is already the front-side background of `Sheet (0,0)` (added earlier today). It draws gray wall bands, two pine trees, and a lake with an island.

In scope: `TerrainRegion` boxes on Sheet (0,0)'s Front matching the drawing (walls, trees, water-minus-island); removal of all `Test *` content from Sheet (0,0) (Front and Back); fixing the Water prefab's `requiredAbility`.

Out of scope: terrain for any other sheet; changes to `TerrainRegion` or any code; new art; polygon colliders (Aaron: boxes only). (The extraction tooling was originally out of scope; review finding N4 brought it into `Tools/`.)

## 2. Design references

- Bible §3 Spatial model — terrain **[DECIDED provisional 2026-08-25]**: "placed objects with box colliders, over art drawn outside Unity. … A region is a `TerrainRegion` with a `BoxCollider2D`; it is a wall, or it is crossable only with a player `Ability` (water needs Swim) — kinds are prefabs differing in data, not code." This task is exactly that decision exercised for the first real map. Aaron: *"drawing the maps in photoshop, then doing the collision/terrain in Unity for the fine tuning ... I don't know that I need the collision to be more than boxes."*
- Bible §3 **[LOCKED]** not tile-based: the grid used below is an *offline authoring aid* for deriving free-placed boxes from the image; nothing grid-like exists at runtime.
- Bible §3 **[DECIDE]** world unit scale: moot for this task — `SheetGeometry` already fixes the sheet at 11×8.5 units and the map is imported at 300 px/unit (1 unit = 1 inch); regions are placed in those units.
- Bible §5 occlusion: `TerrainRegion` already implements `IFoldOccludee`; nothing new needed for folding.
- Bible §9 **[TENTATIVE]** elements: no new element kinds are built. Water requires `Ability.Swim`, which exists. Removing the test block/plates/gates removes instances only — the prefabs and components stay.
- Design Doc, Toolkit: "covering a rock with basic ground, or water after you get swimming" — water regions gated by Swim are the documented intent.

## 3. Decisions already made by Aaron

- This task's text (§1): walls and trees get collision; lake gets water terrain; island does not; all test content leaves Sheet (0,0).
- 2026-08-25 (Bible §3): terrain = box-collider regions over hand-drawn art, fine-tuned in Unity by Aaron.
- Earlier today: Map 1 is the front background of Sheet (0,0), rotated +90°.

## 4. Files

| File | Change |
| --- | --- |
| `Assets/Papercut/Sheets/Sheet (0,0).prefab` | Remove the 8 `Test *` nested instances (7 Front, 1 Back) and their stripped/added-object entries; add 40 nested `Wall`/`Water` prefab instances (13 walls, 2 trees as walls, 25 water) under Front. |
| `Assets/Papercut/Prefabs/Terrain/Water.prefab` | `requiredAbility: -1` (Everything — needs Swim **and** Push, contradicting Bible §3 "water needs Swim") → `1` (Swim). |
| `Tools/extract_map_regions.py` | New (per review N4): derives region boxes from a map PNG (color classify → occupancy grid → rectangle decomposition), writes a boxes JSON + debug overlay, and asserts player-sized reachability. Checked in because every future map repeats this. |
| `Tools/gen_sheet_regions.py` | New (per review N4): rewrites a Sheet variant prefab from a boxes JSON (removes `Test *` and previously generated region instances, emits nested Wall/Water instances with derived fileIDs). |
| `Documents/Plans/2026-08-31-map1-boxes.json` | The canonical generated box record for Map 1 (code review S4) — the generator's exact input. |

## 5. Components & data

No new or changed components; no new code. All regions are instances of the existing `Wall.prefab` / `Water.prefab` (both are `TerrainRegion` + `BoxCollider2D` + `SpriteRenderer`). Per-instance overrides only:

- `m_Name`: `Wall N` / `Tree N` / `Water N` (trees are Wall instances — same data, named for legibility).
- Transform local position (x, y, z = −0.05, the prefab default).
- `BoxCollider2D.m_Size` and `SpriteRenderer.m_Size` (+ tiling properties) = the box size.
- `SpriteRenderer.m_Enabled = 0`: the map drawing **is** the visual; the flat debug sprite would sit on top of it. Scene-view gizmos in `TerrainRegion.OnDrawGizmos` still show every region for tuning, and re-enabling a sprite in the Inspector brings the debug visual back.

No Inspector tunables are added or changed (no code). Every box position/size is an ordinary Inspector value on its instance, which is the fine-tuning surface Aaron asked for.

## 6. Behaviour

Runtime behaviour is entirely existing `TerrainRegion` behaviour:

- Wall/tree boxes block the player (`requiredAbility: None` inherited from Wall.prefab).
- Water boxes block until the player holds Swim (`requiredAbility: Swim` inherited from Water.prefab after the fix).
- Folding: each region is an `IFoldOccludee`; covered regions vanish for collision, partially covered ones clip — unchanged, now exercised by 40 regions instead of 3.
- `SheetBoundary`/`ScreenNavigator`: walls reaching a sheet edge remove arrival room there (exits are not authored).
- **Connectivity (corrected after plan review — flood-fill with the player's 0.44×0.7 collider over the box set):** with no abilities and no folding, the player can reach **no sheet edge that has a neighbour**. The top gap in the ink exists, but Tree 1's bounding box (x −0.46..1.26, y 0.89..3.31) pinches shut against both top bands; the south lakeshore corridor is 0.44–0.64u — narrower than the player; only part of the left edge (no neighbour) is reachable. With Swim, the right edge (y −3.0..1.4) and the island are reachable. Folding can always bridge the lake/trees (Design Doc toolkit: cover untraversable with traversable). Which of these connectivity states is intended is **Aaron's question Q1** (see §10) and gates generation.
- Player spawn (−2, −1) is in open ground; verified by the flood-fill (spawn cell free, 12k+ cells reachable).
- Failure modes: none new. A malformed region (trigger collider, not under a Sheet) already logs via `TerrainRegion.Awake`.

### Box derivation (offline, from the PNG)

- Pixel→sheet mapping: sheet X = (py − 1650)/300, Y = (px − 1275)/300 (the +90° placement).
- Color classification per pixel (alpha composited on white): gray fill → wall; strong blue → water; green∪brown → tree/island; near-black → stroke.
- 0.1-unit occupancy grid; closing+opening to bridge/shave 1-cell noise from the sketchy strokes.
- Green blobs merged by dilation (sketch fragments), then classified: a blob whose surrounding ring is >60% water is the island; the others are trees (2 found, matching the drawing). Trees become one padded bounding box each.
- Water = blue fill ∪ the lake's black waterline, minus the island dilated by 3 cells (the island's own outline plus ~0.2u of drawn water stay walkable land, so the island is standable to its edge).
- Greedy maximal-rectangle decomposition, ≥95% coverage (97% water), max 30 boxes per kind; wall/tree boxes padded 0.06u to swallow the black stroke.
- Verified visually against a debug overlay of the boxes on the rotated image before generating YAML.

Resulting set: 13 wall boxes, 2 tree boxes, 25 water boxes (`map1_boxes.json`, reproduced in Appendix A). These are a *starting point*; Aaron owns the numbers from here (Bible §3: "the collision/terrain in Unity for the fine tuning").

## 7. Interfaces & seams

Nothing new. Regions stay data: deleting Map 1's terrain = deleting the instances from the variant. The extraction/generation scripts are checked into `Tools/` (review N4) so future maps rerun them instead of rebuilding them.

## 8. Testing

Mechanical (Unity CLI, editor open):
1. `recompile` — must stay clean (no code changed, but the C# fix gate is free).
2. `run_tests {"mode":"editor"}` — full suite; report the summary and any failures verbatim.
3. `python Tools/check_links.py` — every fileID/guid in the hand-authored YAML resolves; no ambiguous derived IDs.
4. Editor eval: load `Sheet (0,0).prefab`; assert 0 objects named `Test *`; count `TerrainRegion`s = expected; assert every region's `BoxCollider2D` is non-trigger and its footprint lies within the sheet rect; assert the player spawn (−2, −1) is not inside any solid box; assert Water.prefab's `requiredAbility` = Swim.
4b. Offline reachability assertion (added per review N3): flood-fill the sheet rect minus solid boxes inflated by the player half-extents (0.22, 0.35), from the spawn; assert the connectivity state Aaron chose in Q1 (which edge strips are reachable without abilities, with Swim, and that the island is water-locked).
5. Play Mode: enter, screenshot, describe; console must show no errors; eval `FindObjectsByType<TerrainRegion>()` count on the live sheet.

For Aaron (feel/intent — cannot be checked mechanically):
- Walk the map: do the collision edges sit where the drawing says they should? (Boxes are snapped to a 0.1u grid off a wobbly sketch; edges will be within ~0.1u of the ink but only eyes can say if it *feels* right.)
- Is the ~0.2u of standable drawn-water around the island rim acceptable, or should water hug the island outline tighter?
- Blocked at the lake reads as "need swimming"? Trees blocking as full rectangles (including under the canopy) feel right?
- Fold the sheet across walls/lake: regions clip as expected over the new map.
- Walk off the top gap and right side: screen transitions to (0,1)/(1,0) land where expected.

## 9. Assumptions

Engineering assumptions (design intent is settled by the task text + Bible §3):

1. Collision boundary follows the drawing's black stroke: wall/tree boxes are padded 0.06u to *include* their stroke; water includes the lake's waterline stroke. The player walks up to the ink, not onto it.
2. The blue "fuzz" outside the waterline is style, not water; boxes stop near the stroke (±0.1u).
3. Trees are solid rectangles over their full drawn footprint (canopy + trunk), as Wall instances named `Tree N`.
4. The island stays walkable to ~0.2u past its outline (see §6) rather than risking water boxes clipping the island itself.
5. Region debug sprites are disabled per instance because the map is the visual; gizmos remain for editing. (If Aaron prefers the sprites on, it's one checkbox per instance.)
6. "Remove the test … stuff from this sheet" includes `Test Back Wall` on the Back face — "this sheet" reads as the whole sheet, and a leftover invisible wall on the Back would be a trap later.
7. Water.prefab's `requiredAbility: -1` is an authoring slip. Precisely (review N2): `TerrainRules.IsPassable` requires `(owned & required) == required`, and −1 sets *every* bit including undefined ones — the region is permanently impassable unless the player's abilities are also set to Everything. Bible §3 says "water needs Swim"; fixing to `Swim` (1). This propagates to the one existing Water instance (Sheet (1,0), no override); today the player has no abilities, so behaviour is unchanged until abilities are granted.
8. 40 regions on one sheet is within what the occlusion path already handles per fold (it iterates occludees; no per-frame cost when not folding).

## 10. Open questions

**Q1 (from plan review B1, asked 2026-08-31): what should Map 1's on-foot connectivity be, given the player (0.44×0.7) vs the ink?** Flood-fill facts: boxes hugging the ink seal every neighbouring exit without Swim — Tree 1's rectangle pinches both routes to the top gap, and the drawn south lakeshore (~0.65u) is narrower than the player. Folding can always bridge (cover water/trees with a flap). Options put to Aaron:
- (a) Sealed as drawn — reaching Sheet (0,1)/(1,0) requires folding (or Swim for the east). Boxes stay exactly on the ink.
- (b) Trees get snugger multi-box (pyramid) colliders so the drawn gaps left/right of Tree 1 open the top exit on foot; the lake still seals the east until Swim/folding. (Recommended: follows the ink most faithfully — the ink has real gaps around the canopy, but the south shore is genuinely narrower than the player.)
- (c) As (b), plus pull the south-shore water boxes back to guarantee a ≥0.9u walkable corridor to the right edge.

**Answer (Aaron, 2026-08-31): "Sealed as drawn."** Single-rectangle trees and ink-hugging water stay; without Swim, leaving Sheet (0,0) requires folding (a flap over the lake/trees), and the east side additionally opens with Swim. The Appendix A box set is generated unchanged. §8's reachability assertion (4b) now expects: spawn free; no neighbouring edge strip reachable on foot without abilities; right edge and island reachable with Swim.

## Review (round 1 — verdict BLOCK)

- **B1 (connectivity claims contradicted by box set): accepted + escalated.** §6 corrected with flood-fill facts; the design half is Q1 for Aaron; generation gated on the answer.
- **N1 (top-gap description ignored stroke posts): accepted.** Appendix A corrected (gap is x −0.94..1.04).
- **N2 (−1 semantics imprecise): accepted.** §9.7 now states the exact `IsPassable` behaviour; Sheet (1,0) side-effect kept for the report.
- **N3 (mechanical checks can't catch pinches/seals): accepted.** §8 gains the flood-fill reachability assertion (4b), run as part of extraction.
- **N4 (tools not persisted for a repeating task): accepted.** Both scripts are checked into `Tools/` (§4, §7).

## Code review (round 1 — verdict APPROVE WITH FIXES; all four accepted and applied)

- **S1 (reachability only printed, not asserted):** accepted — `extract_map_regions.py` now hard-asserts the Q1 connectivity (no-ability edges = {left}, with-Swim = {left, right}, island water-locked) and refuses to write the JSON on mismatch.
- **S2 (generator writes silently on unexpected layout):** accepted — it now aborts without writing if the outer PrefabInstance / stripped Front transform is missing or the added-object splice fails. Also made re-runs replace previously generated regions instead of duplicating them (deviation, noted below).
- **S3 (stale out-of-scope line):** accepted — §1 corrected.
- **S4 (boxes JSON not persisted):** accepted — checked in as `Documents/Plans/2026-08-31-map1-boxes.json`; the generator header names it as the canonical record.

## Deviations

- During verification the open editor wrote its stale in-memory import back over the regenerated prefab (triggered somewhere around the editor test run / play transition — the file reverted to the pre-generation state). Recovery procedure that held: `AssetDatabase.SaveAssets()` first to flush editor dirty state, then regenerate, then `ImportAsset(ForceUpdate)`. The final prefab on disk and in the import both verified correct afterwards, including across a play/stop cycle.
- `gen_sheet_regions.py` gained region-replacement on re-run (see S2) beyond the plan's description — engineering hardening for the tool's stated reuse, no behaviour change for this map.

## Appendix A — generated boxes (sheet-local units, center x, center y, width, height)

40 boxes: 13 wall, 2 tree, 25 water. Orientation notes for checking against the drawing (landscape): the bottom band is the full-width wall at y −3.82; the left band is at x −5.12; the top-left L is the y 3.82 band plus its 3.4 stroke; the top-edge gap in the ink runs x −0.94..1.04 between the two stroke posts (review N1) — whether the player can actually reach it depends on Q1; the lake is centred around (3.2, −0.6) with the island near (3.3, −0.9). This table is the pre-Q1 set; Q1's answer may adjust the tree and south-shore water boxes.

| kind | cx | cy | w | h |
| --- | --- | --- | --- | --- |
| wall | 0.0 | -3.82 | 11.0 | 0.86 |
| wall | -3.37 | 3.82 | 4.26 | 0.86 |
| wall | -5.12 | 1.6 | 0.76 | 3.82 |
| wall | 2.75 | 3.82 | 3.02 | 0.86 |
| wall | 5.07 | 3.87 | 0.86 | 0.76 |
| wall | -3.7 | 3.4 | 2.32 | 0.22 |
| wall | -1.15 | 3.92 | 0.42 | 0.66 |
| wall | 4.35 | 3.97 | 0.42 | 0.56 |
| wall | 1.2 | 3.92 | 0.32 | 0.66 |
| wall | 5.27 | 3.4 | 0.46 | 0.42 |
| wall | 3.45 | 3.4 | 1.22 | 0.22 |
| wall | -2.65 | -3.35 | 0.62 | 0.32 |
| wall | -4.75 | 2.75 | 0.22 | 0.92 |
| water | 2.6 | 0.95 | 3.2 | 2.0 |
| water | 1.5 | -0.9 | 1.0 | 1.7 |
| water | 5.15 | -0.6 | 0.7 | 2.1 |
| water | 4.5 | 0.45 | 0.6 | 1.6 |
| water | 2.85 | 2.15 | 2.3 | 0.4 |
| water | 2.9 | -2.55 | 1.8 | 0.4 |
| water | 4.4 | -2.0 | 1.0 | 0.5 |
| water | 2.3 | -0.4 | 0.6 | 0.7 |
| water | 1.8 | -2.05 | 0.6 | 0.6 |
| water | 4.05 | -2.45 | 0.5 | 0.4 |
| water | 2.9 | -2.85 | 0.8 | 0.2 |
| water | 4.6 | -1.55 | 0.4 | 0.4 |
| water | 5.0 | 0.65 | 0.4 | 0.4 |
| water | 0.95 | -0.2 | 0.1 | 1.5 |
| water | 3.45 | -2.25 | 0.7 | 0.2 |
| water | 3.85 | -0.15 | 0.7 | 0.2 |
| water | 2.15 | -0.95 | 0.3 | 0.4 |
| water | 2.6 | 2.4 | 1.2 | 0.1 |
| water | 4.35 | 1.45 | 0.3 | 0.4 |
| water | 0.9 | 1.2 | 0.2 | 0.5 |
| water | 2.85 | -0.15 | 0.5 | 0.2 |
| water | 1.35 | -1.9 | 0.3 | 0.3 |
| water | 1.55 | 2.05 | 0.3 | 0.2 |
| water | 2.2 | -2.2 | 0.2 | 0.3 |
| water | 5.05 | -1.75 | 0.3 | 0.2 |
| tree | 0.4 | 2.1 | 1.72 | 2.42 |
| tree | 4.45 | 2.95 | 1.82 | 2.32 |
