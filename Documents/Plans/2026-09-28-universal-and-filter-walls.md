# Universal walls and filter walls

Date: 2026-09-28

## 1. Task

Aaron: *"Can you create a new kind of wall, one that conceptually exists above the surface of the paper, and so when the page is folded, the wall still persists. It should still render as if it were limited by the edge of the paper (so when the fold reduces the size of the page, it doesn't extend past the page) but other than that its not affected by a fold. You can call it a universal wall. Then I also want another kind of wall that doesn't block the player, but does block Blocks, and its derivations like pushable paperweights and stuff. Those filter walls can also be universal or not. (so maybe universal is a filter for some collision and stuff, idk, whatever you feel is best)"*

Deliverables:

1. **Universal regions.** A terrain region that sits *above* the sheet rather than on a face. Folds slide under it: it is never lifted, mirrored, covered or carried; a Flap that lands beneath it is still blocked there. Its one fold interaction is a clip to the sheet's current footprint (the union of every layer where it lies), so it never sticks out past a folded-down edge. Off the Screen it is inert like every region. A universal region never holds a fold short (Aaron, Q1).
2. **Filter regions.** A terrain region that blocks every movable object (`PushableBlock` and its variants: Block, Pushable Paperweight; the fixed Paperweight never moves anyway) but never the player.
3. The two are **independent axes** on the one `TerrainRegion` component — *who* it blocks (player, gated by ability as today, and/or blocks) and *where* it lives (on a face, or above the sheet) — so they compose without new code paths.
4. Six prefabs in `Prefabs/Terrain`, box and polygon shapes each (Aaron, Q2): `Universal Wall`, `Universal Wall (Polygon)`, `Filter Wall`, `Filter Wall (Polygon)`, `Universal Filter Wall`, `Universal Filter Wall (Polygon)`. The universal ones' collision fill is teal (Aaron, Q4 asked for blue; after the review noted Water's blue, Q2: *"Make it teal"*).
5. The Sheet gets a third content root, **Above** (Aaron, Q3), and the runtime, the Sheet Studio (both face panes, fold preview, placement, playtest room check) and the Bible know about it.
6. **A fold is refused when its Flap would carry a block under a universal wall** that stops blocks (Aaron, post-review Q1: *"Deny the fold if a block falls under a universal wall, like with folding over the player"*): the drag tints red and release does nothing, exactly the player rule's shape — not a clamp. The Studio's fold preview refuses the same way from the authored blocks and walls.

**Out of scope:** art for universal walls beyond the collision fill (Aaron, Q4: *"art can come later"*); water or any gated region above the sheet (Aaron, Q5: *"don't need it"*); a player-only filter prefab (the flag set makes it data, but no prefab is made and nothing exercises it beyond a rules test); universal versions of Gate / Gate (Off) (an `ObjectPresence` target above the sheet; not asked); universal regions as fold obstacles (Q1: never); wiring plates to universal regions in the Studio's Link mode; a universal region on a sheet whose variant predates the Above root (every authored sheet is a variant of `Sheet.prefab`, so all of them get the root).

## 2. Design references

- Design Doc *Puzzles → Elements*: "Traversable/Untraversable terrain", "Pushable Blocks". A wall that blocks blocks but not the player, and a wall that folds cannot remove, are terrain kinds for puzzle design — the same category as Wall and Water.
- Bible §3 *Terrain* **[DECIDED, provisional]**: a region is a `TerrainRegion` with a box or single-outline polygon collider; kinds are prefabs differing in data, not code; the Studio draws and edits them. This task keeps that: two more serialized fields, six more prefabs.
- Bible §3 *Collision view* [2026-09-14]: `TerrainFill` draws a region's collider as face content so it folds with the sheet. A universal region's fill must **not** fold with the sheet, so for it the fill is main-camera content clipped to the live parts (§6.5).
- Bible §4 *Validity* **[LOCKED]**: a fold never covers the player; the player is always on the Base. Unaffected. *Paperweights hold a fold short* [DECIDED 2026-09-14]: universal regions are **not** obstacles (Aaron, Q1) — `IFoldObstacle` is untouched.
- Bible §5 *Occlusion & collision* [DECIDED 2026-08-26]: `SheetOcclusion` notifies every `IFoldOccludee` with what is left of it and where; `TerrainRegion` swaps its collider for a clipped polygon. A universal region goes through the **same** interface with a different clip (the sheet footprint instead of a face's coverage), so nothing under a Flap is blanket-disabled and the fold system gains no branch on "universal".
- Bible §5 *Movable objects* [DECIDED 2026-08-27]: blocks stop at walls, gated terrain, sheet edges and other blocks. A filter region is a wall to blocks and nothing to the player; a player-only region (not shipped as a prefab) would be the reverse. Blocks judge obstacles by a cast (`PushableBlock.TryPush`), so the "blocks" flag is consulted there; the player's passability is a `Physics2D.IgnoreCollision` pair, as for Water.
- Bible §5 **[DECIDE]** *layering rule (flat world)*: narrowest-open in code stays — the universal region is a wall wherever it is, on Base or landed Flap alike; nothing about heights.
- Bible §6 *Backside model*: Above content is authored in **sheet space** (= Front-space, flat), never in Back-space, and never mirrored. The Back root convention is untouched.
- Bible §7 *Exits are not authored*: `TravelRules.HasRoom` with `SheetOcclusion.SolidFootprints` — extended to Above content so a universal wall at a sheet edge refuses arrival there like any wall. Studio playtest spawn room check likewise.
- Bible §9 **[TENTATIVE]** elements: terrain kinds are prefabs; cutting one is deleting it. Universal/filter prefabs are cut the same way; the two fields default to today's behaviour so deleting the prefabs leaves nothing behind but two inert fields (§7).
- Bible §0 *Robust ≠ pre-generalized*: the "who it blocks" flag set is two bits because both bits are needed today (walls block both; filters block blocks only). No third axis, no ScriptableObject.
- Not covered anywhere (gap, flagged): a third content root on the Sheet. Built as the narrowest thing that works: one optional root, validated when assigned, enumerated by occlusion and the Studio's Front pane; no new interface.
- No `[DECIDE]` item is resolved by this task. World-unit scale (§11.9), movement scheme (§11.12), persistence (§11.8) untouched.

## 3. Decisions already made by Aaron (2026-09-28)

Asked with recommendations; answers verbatim:

1. *Universal walls never hold a fold short; the Flap goes under.* — **"Yes"**
2. *Prefab set: three kinds × box and polygon = six prefabs.* — **"Yup!"**
3. *Name the third root "Above" (Front, Back, Above).* — **"sure"**
4. *Only the collision fill for now, no sprite slot.* — **"Yeah, art can come later, just make these walls blue to stand out."**
5. *No water above the sheet.* — **"Nope, don't need it."**
6. Workflow: **"full workflow, yes"**. Play Mode: not requested; never entered.

Post-review (plan review round 1 escalated two questions; answers verbatim, 2026-09-28):

7. *A Flap carrying a block under a universal wall — authoring rule / clamp / block inert?* — **"Deny the fold if a block falls under a universal wall, like with folding over the player."**
8. *Blue clashes with Water's blue — teal, recolour Water, or judge at playtest?* — **"Make it teal"**.
9. *Back pane: hide universal regions / draw them read-only mirrored / editable from both panes?* (round-2 review Q1) — **"number 2"**: drawn read-only in the Back pane, mirrored through `SheetGeometry.BackToFront`.

Earlier decisions this rests on: terrain as placed regions (2026-08-25, 2026-09-10); covered terrain gone for collision, partial clipped (2026-08-26); blocks stop at terrain (2026-08-27); collision view (2026-09-14); the Studio's face panes and fold preview (2026-08-31 onward).

## 4. Files

Runtime (`Assets/Papercut/Scripts/`):

- `Terrain/TerrainBlocks.cs` — **new**: `[Flags] enum TerrainBlocks { None = 0, Player = 1, Blocks = 2 }` — who a region is solid to.
- `Terrain/TerrainRules.cs` — add the "who" rule: `IsPassable(TerrainBlocks, Ability owned, Ability required)` and `StopsBlocks(TerrainBlocks)`. The existing two-argument ability rule stays (it is the gate rule and is still called through the new one).
- `Terrain/TerrainRegion.cs` — two serialized fields (`blocks`, `universal`); root validation for universal; footprint against its own root; `StopsBlocks`, `IsUniversal`; arrival footprint for Above; gizmo colours.
- `Terrain/TerrainFill.cs` — for a universal region, build the mesh from the sheet's displayed layers (`SheetLayers.CoverageAbove(folds.DisplayLayers)`), rebuilt on `SheetFolds.Displayed`/`Changed`; with no `SheetFolds` component (edit mode), the authored shape as today.
- `Desk/Sheet.cs` — `above` serialized root + `Above` property; validated when assigned; Awake forces its subtree onto the Default layer (mirror of Front/Back onto the face layers).
- `Fold/SheetLayers.cs` — `CoverageAbove(FaceFootprint)`: the parts of a sheet-space footprint that lie on the sheet's footprint, disjoint, bottom layer first minus layers above; Whole when every piece is intact on the Base.
- `Fold/SheetOcclusion.cs` — notify the Above root's occludees with `CoverageAbove` (live) / None (not the Screen); include Above in `ReportUnseenColliders` and `SolidFootprints`.
- `Objects/PushableBlock.cs` — `TryPush` ignores hits on a region that does not stop blocks; implements the new `IFoldConstraint.RefuseFold` (the carried-under-a-wall rule) with a lazily gathered list of the sheet's block-stopping universal walls.
- `Fold/FoldLandingRules.cs` — **new**, pure: `CarriesUnderWall(Rect flatRect, SheetFace side, SheetLayers after, int foldIndex, IReadOnlyList<ConvexPolygon> walls)` — true when any piece of the content that the fold at `foldIndex` carries (a visible piece on a layer moved by that fold, per `SheetPlacement.VisiblePieces` on the stack after the fold) shares area with a wall piece.
- `Fold/IFoldConstraint.cs` — gains `FoldRejection RefuseFold(in FoldEffect effect, SheetLayers after, int foldIndex)`; `Paperweight` answers `None` (a weight is clamped short of, never carried).
- `Fold/SheetFolds.cs` — `FoldRejection.CarriesBlockUnderWall`; `EvaluateGathered` keeps the stack `Apply` returns and asks every gathered constraint (gathered per frame with the obstacles) after the obstacle check.
- `Editor/StudioFoldModel.cs` — `SetBlocks` (authored flat rects + sides) and `SetBlockWalls` (sheet-space wall pieces); `Evaluate` and the fold-list replay apply the same pure rule ⇒ `CarriesBlockUnderWall`; a list-edit refusal reason for it.
- `Editor/SheetStudioWindow.cs` — `RefreshObstacles` also gathers the sheet's blocks (`PushableBlock` under a face root: its root `BoxCollider2D` rect in that face's space, converted to a flat Front-space rect for Back content) and its block-stopping universal walls (Above root, serialized `universal` and `blocks` read) and hands them to the model.

Prefabs and data (`Assets/Papercut/`):

- `Prefabs/Sheet.prefab` — new `Above` root (GameObject + Transform, layer Default, local z 0.3), listed in the Sheet's `above` field and the root's children. Edited **through the editor** (the asset is loaded and uncommitted; see memory `unity-cli-pipeline` on stale write-back).
- `Prefabs/Terrain/Universal Wall.prefab`, `Universal Wall (Polygon).prefab`, `Filter Wall.prefab`, `Filter Wall (Polygon).prefab`, `Universal Filter Wall.prefab`, `Universal Filter Wall (Polygon).prefab` — **new**, copies of Wall / Wall (Polygon) with the two fields and the fill colour set. Created through the editor (`AssetDatabase.CopyAsset` + `LoadPrefabContents` edits), verified with `Tools/check_links.py`.

Editor (`Assets/Papercut/Editor/`):

- `StudioPane.cs` — `Context.AboveRoot` (Front pane only); overlays, terrain fills, labels, pick, select, move, resize, vertex edit, delete and duplicate cover elements under both roots; placement routes a universal prefab to the Above root (refused with a message in the Back pane); new overlay colours for universal (teal) and filter (violet) regions.
- `StudioPlacement.cs` — `IsUniversal(GameObject prefabOrElement)`; `Place`/`PlacePolygon` accept the Above root and apply the Default layer for it; `CanEdit` and `ElementRootOf` callers accept Above descendants; `PickElement` unchanged in signature (called per root); `MoveMapped` refuses to move a universal element between roots (it is never mapped through a fold).
- `StudioPolygonDraft.cs` — `TryFinish` routes a universal polygon prefab to the Above root (Front pane only).
- `SheetStudioWindow.cs` — `ContextFor` passes the Above root to the Front pane; `SelectedEditableElement` (keyboard nudge/delete) includes it; the missing-root help box does not demand Above (optional root; a message when a universal prefab is armed on a sheet without it).
- `StudioSheetOps.cs` — `NormalizeFaceLayers` also normalizes the Above subtree to Default.
- `StudioFoldPane.cs` — press/drag/selection/placement ghost for universal elements in **sheet space** (no fold mapping; picked before the face roots since it is on top); terrain-view fill of universal regions drawn over the composite, clipped through `CoverageAbove(DisplayLayers)`; `Context.AboveRoot` and `Context.ShowTerrain`.
- `StudioPlaytest.cs` — `SpawnHasRoom` also walks the Above root's arrival obstacles.

Tests (`Assets/Papercut/Tests/EditMode/`):

- `TerrainRulesTests.cs` — the "who" rule.
- `SheetLayersTests.cs` — `CoverageAbove` cases.
- `TerrainFillTests.cs` — a universal region's fill from live parts; unchanged face fills.
- `StudioPlacementTests.cs` — universal placement goes to Above on the Default layer; refused without an Above root; `MoveMapped` refuses a universal element; `PickElement` over the Above root.
- `StudioPlaytestTests.cs` — a universal wall at the spawn refuses; a filter wall there does not.
- `StudioFoldPaneTests.cs` (exists? — if the fold pane has a headless press test seam, one case: a press over a universal element on a folded sheet selects it without mapping; else skipped and said so).

Docs:

- `Documents/Claude Bible.md` — §3 terrain: a dated paragraph for universal and filter regions and the Above root; §5 a sentence on blocks-only terrain; §9 the six prefabs and how to cut them.

## 5. Components & data

### `TerrainBlocks` (new enum, `[Flags]`)

`None = 0`, `Player = 1`, `Blocks = 2`. Serialized on the region; shown as a flags mask in the Inspector.

### `TerrainRules` (pure, extended)

- `bool IsPassable(TerrainBlocks blocks, Ability owned, Ability required)` — true when the region is not solid to the player at all (`Player` bit clear), else the existing ability rule `IsPassable(owned, required)`.
- `bool StopsBlocks(TerrainBlocks blocks)` — `Blocks` bit set.
- Existing `IsPassable(Ability owned, Ability required)` unchanged.

### `TerrainRegion` (changed)

Serialized fields (Inspector, per prefab — these are design data, not feel tunables, but they are what makes each prefab a kind):

- `[Header("Blocking")] TerrainBlocks blocks = Player | Blocks` — *"Who this region is solid to. Player: the player, unless they hold Required Ability. Blocks: every pushable block and paperweight. Both is an ordinary wall; Blocks only is a filter wall the player walks through."*
- `Ability requiredAbility = None` — unchanged (tooltip gains: only meaningful while Player is set).
- `[Header("Placement")] bool universal = false` — *"Sits above the sheet (under the Sheet's Above root) instead of on a face: folds slide under it, it is never lifted, mirrored, covered or carried, only clipped to the sheet's footprint. A universal region must be a child of the Above root; the Sheet Studio places it there."*

Public surface added:

- `bool IsUniversal`, `bool StopsBlocks`.
- `IsPassableBy(PlayerAbilities)` now goes through the three-argument rule; a null `PlayerAbilities` (no Player on the Desk; the Studio's "no abilities") is passable when the `Player` bit is clear and impassable otherwise (the ability gate fails solid, as today).

Behaviour changes: root resolution (universal ⇒ Above root, else Front/Back; the wrong root refuses the region, inert, with an error naming the fix); `FaceLocalFootprint(root)` unchanged in body (the Above root is at identity, but the matrices are used regardless); `TryGetSolidFootprint` uses the region's own root (`sheet.Above` for a universal region) so a universal wall counts for arrival; gizmos: universal fill/outline teal, blocks-only violet, else as today.

### `TerrainFill` (changed)

No new serialized fields (the colour and material already exist per prefab; the six prefabs set the colour). For a region with `IsUniversal` (revised after plan review B1/N1):

- the mesh is derived from the **sheet's layers as displayed**, not from the collider: `SheetLayers.CoverageAbove(folds.DisplayLayers)` over the authored footprint, rebuilt on `SheetFolds.Displayed` and `SheetFolds.Changed` (the same signals `PushableBlock` redraws on), so a drag preview and the animated unfold clip the fill live, and a sheet that is not the Screen (flat display layers) shows the authored shape — a neighbour peeking in, the zoomed-out view and the frame after leaving all keep their blue walls, as face fills and blocks do;
- when there is no `SheetFolds` (edit mode fixtures, the Studio stage): the authored shape as today;
- parts are sheet-local; the mesh is built in region-local space through `transform.worldToLocalMatrix * sheet.transform.localToWorldMatrix` for x/y only, z fixed at 0 (the region's own depth), so the fill never moves in front of the Seam overlay;
- the part's layer follows the region's (`gameObject.layer`, Default under Above) so the main camera draws it; z is the region's (Above root 0.3 minus the prefab's −0.05 = 0.25 sheet-local: in front of any layer stack up to 20 layers and of every block (≈0.495), behind the player at −0.5);
- the fill subscribes/unsubscribes to the `SheetFolds` events in `BindSheet`/`UnbindSheet`, finding the component with `sheet.GetComponent<SheetFolds>()` (never `sheet.Folds`, which is null until `Sheet.Awake`, and Awake order across objects is not guaranteed); the edit-mode branch is keyed on there being no `SheetFolds` component.

`TerrainRegion.LiveParts` / `CoverageApplied` from the first draft are dropped: nothing needs them.

### `Sheet` (changed)

- `[SerializeField] Transform above` — *"Root for content that sits above the sheet (universal regions), in sheet space. Never moved; folds slide under it. Optional on old sheets; the base Sheet prefab has one."*
- `Transform Above => above`.
- `ValidateFaceRoots`: Above, **when assigned**, must be a descendant and not Front/Back/self (error otherwise). A null Above is not an error (nine test fixtures build Sheets with only Front/Back and assert the exact error set; and a sheet without universal content needs no root).
- Awake: `SetLayerRecursively(above, DefaultLayer)` with `const int DefaultLayer = 0` (the same constant `PushableBlock` uses).

### `SheetLayers.CoverageAbove(FaceFootprint footprint)` (new, pure)

For each authored piece (sheet space): for each layer `i` bottom to top, `piece ∩ layer.Desk` minus every `layers[j>i].Desk`; pool the survivors (disjoint by construction). Above content never moves, so (plan review N2) **Whole** (`CoverageResult.Whole(Uncovered, footprint)`) iff the survivors' total area ≥ the footprint's area within `ContainmentTolerance` — a Flap landing on an intact universal region leaves the authored collider in place, no multi-path runtime polygon; **None** (`Covered`) when nothing survives; else `Clipped(parts, Partial)`. The flat stack returns Whole for anything inside the sheet rect and a clip for a footprint hanging past it (a universal region authored past the sheet edge is trimmed to the sheet even when flat: the same rule as when folded, and the Studio magnets vertices to the edge already). This differs from face content, which is not trimmed when flat (plan review N6) — stated in the Bible paragraph.

### `SheetOcclusion` (changed)

`Apply()` also runs `NotifyAbove(sheet.Above)`: not the Screen ⇒ `None(Covered)` (a universal wall of a non-Screen sheet is inert like every region — collider only; its fill is drawn from the display layers, see `TerrainFill`); Screen ⇒ `folds.Layers.CoverageAbove(footprint)` (flat or not — flat is the trivial case of the same function). `ReportUnseenColliders` and `SolidFootprints` walk Above too. Non-universal regions under Above and universal ones under a face are refused by the region itself (§6.1); an occludee under Above that is **not** a universal `TerrainRegion` (a plate, block or pickup hand-parented there) is reported by `SheetOcclusion` and skipped, never handed a sheet-space result it was not written for (plan review N4).

### `PushableBlock.TryPush` (changed)

In the obstacle loop, skip a hit whose collider has a `TerrainRegion` in its parents with `StopsBlocks == false`. Everything else unchanged. (`BlockPusher.PlayerFreeDistance` already skips regions the player may pass, which now includes filter walls through `IsPassableBy`; hold detection ignores non-block rigidbodies already.)

### Fold refusal: a Flap may not carry a block under a wall (post-review Q1)

- `FoldLandingRules` (new, pure, `Scripts/Fold`): `bool CarriesUnderWall(Rect flatRect, SheetFace side, SheetLayers after, int foldIndex, IReadOnlyList<ConvexPolygon> walls)`. `after` is the stack once the fold is applied; the carried pieces are `SheetPlacement.VisiblePieces(flatRect, side, after)` restricted to pieces whose `LayerIndex` layer has `MovedBy == foldIndex` (what this fold lifts and lands, face-up), including the overhang part of a climbed rect; true when any of them overlaps any wall piece (`ConvexPolygon.Overlaps`). Pieces the fold covers (on the Base under the landing) are not carried and do not count; a block already overlapping a wall before the fold counts only if the fold carries it. Sheet-local throughout; no Unity objects.
- `IFoldConstraint.RefuseFold(in FoldEffect effect, SheetLayers after, int foldIndex)` — new member beside `RefuseUnfold`. `PushableBlock`: `None` unless `ok && isActiveAndEnabled` (pre-fold visibility is irrelevant: a Back-authored block on a flat sheet is invisible, yet the fold that turns it face-up under a wall is exactly the case to judge; `SheetPlacement.VisiblePieces` on `after` decides); else `CarriesBlockUnderWall` when `FoldLandingRules.CarriesUnderWall(flatRect, side, after, foldIndex, walls)`, where `walls` is gathered lazily on first use (after every Awake): every `TerrainRegion` under `sheet.Above` with `IsValid && IsUniversal && StopsBlocks`, each `FaceLocalFootprint(sheet.Above).Pieces`. Universal walls are static authored content, so one gather per block per session is exact. `Paperweight.RefuseFold` returns `None`.
- `FoldRejection.CarriesBlockUnderWall` — *"The Flap would carry a block under a universal wall (an `IFoldConstraint`): the block would end inside the wall, so the fold is refused like one that covers the player."*
- `SheetFolds`: `GatherObstacles` also gathers the constraints (`GetComponentsInChildren(false, constraints)`, the list that `TryUnfold` already fills per call). `EvaluateGathered` keeps `var after = layers.Apply(fold, folds.Count, out effect)` and, after `CoversObstacle` and before `OverlapsFold`, returns the first non-`None` `RefuseFold(effect, after, folds.Count)`. The drag preview goes red (`previewValid`) and `TryCommit` refuses, with no other change — `FoldDragInput` keys on `previewValid`/the rejection generically (checked at coding; if it special-cases kinds, the new one joins `CoversPlayer`'s branch).
- Studio mirror: `StudioFoldModel.SetBlocks(IReadOnlyList<(Rect flatRect, SheetFace side)>)` and `SetBlockWalls(IReadOnlyList<ConvexPolygon>)` (both ignore an unchanged hand-off, like `SetObstacles`); `Evaluate` applies `FoldLandingRules.CarriesUnderWall` for every block against the stack after the fold with the fold's index; the fold-list replay (`TryRemoveAt`/`TrySetDepth`) uses the same check per replayed fold with its index and refuses with *"a block would be carried under a universal wall"*. Blocks in the Studio are authored at rest: their flat rect is the root `BoxCollider2D`'s face-local rect, mirrored through `SheetGeometry.BackToFront` for Back content; their side is the face they are under. Walls: Above-root `TerrainRegion`s whose serialized `universal` is on and `blocks` has `Blocks`, as `TryGetRegionPieces` pieces. Gathered in `SheetStudioWindow.RefreshObstacles` (already re-run on every edit). Editor limit, stated in the model's class remarks beside the paperweight note: blocks are judged where they are authored; in the game a block pushed elsewhere first may make a stack reachable that the preview refuses, or the reverse.

### Prefabs

| Prefab | Shape | `blocks` | `universal` | Fill colour (`TerrainFill.color`) | Root z |
|---|---|---|---|---|---|
| Universal Wall | box 2×2 | Player, Blocks | on | teal `(0.1, 0.75, 0.7, 1)` | −0.05 |
| Universal Wall (Polygon) | polygon (Wall (Polygon)'s square) | Player, Blocks | on | teal `(0.1, 0.75, 0.7, 1)` | −0.05 |
| Filter Wall | box 2×2 | Blocks | off | violet `(0.6, 0.35, 0.85, 1)` | −0.05 |
| Filter Wall (Polygon) | polygon | Blocks | off | violet | −0.05 |
| Universal Filter Wall | box 2×2 | Blocks | on | light teal `(0.45, 0.85, 0.8, 1)` | −0.05 |
| Universal Filter Wall (Polygon) | polygon | Blocks | on | light teal | −0.05 |

Wall, Wall (Polygon), Water, Water (Polygon), Gate, Gate (Off) are untouched: their serialized `blocks` defaults to Player | Blocks and `universal` to off, so a prefab without the fields deserializes to today's behaviour. Colours are Inspector values on each prefab; teal is Aaron's (post-review Q2), the violet for filter walls and the exact values are mine, listed in the report.

### `Sheet.prefab`

New objects: `Above` GameObject (fileID 4000, layer 0) with Transform (fileID 4001, local position (0, 0, 0.3), parent 1001), appended to the Sheet transform's `m_Children`; `above: {fileID: 4001}` on the Sheet component. Every sheet variant inherits it. No existing fileID changes, so no variant override is disturbed.

### Studio

- `StudioPane.Context.AboveRoot` (Transform; both panes) and `Context.AboveEditable` (true in the Front pane only). In the Back pane Above content is drawn only (mirrored x); `Roots(ctx)` yields Above for editing purposes only when `AboveEditable`. A private `Roots(ctx)` enumerator yields FaceRoot then AboveRoot; `RootOf(ctx, element)` returns the root an element lives under. Every call that today pairs `ctx.FaceRoot` with an element (outline, footprint, rect, vertex ops, `CanEdit`, `ElementRootOf`) uses `RootOf`; every enumeration of `ctx.FaceRoot`'s colliders/regions/children for drawing or picking runs over `Roots(ctx)`. Link drawing and Link mode stay FaceRoot-only (plates and their targets are face content; out of scope).
- Picking across roots (plan review N3a): `StudioPlacement.PickElement(IReadOnlyList<Transform> roots, sheet, point)` — the smallest hit outline across every root wins; on an equal area the **later root wins**, and Above is listed last, so a universal region exactly over a Front region of the same shape is picked (it is on top). The same overload serves the fold pane (Above at the unmapped desk point, the mapped face root at its authored point; areas compared, tie to Above).
- Magnet (plan review N3b): `MagnetTargets` takes the pane's roots, so in the Front pane universal vertices magnet to Front regions' vertices and vice versa.
- New pane colours: `UniversalColor`/`UniversalFill` (teal, matching the prefab hue), `FilterColor`/`FilterFill` (violet). `ColorFor(element)`: universal ⇒ teal; blocks-only ⇒ violet; else as today. Labels already print the element's name (`Universal Filter Wall`), so the two teals need no extra text.
- Placement: `StudioPlacement.IsUniversal(prefab)` (serialized `universal` on the root's `TerrainRegion`). In `OnLeftMouseDown` with a prefab armed: universal ⇒ `ctx.AboveRoot == null` ⇒ `Report("Universal regions sit above the sheet: place them in the Front pane")` (Back pane) or `Report("This sheet has no Above root; re-create it from the Sheet prefab")` (old sheet) and `e.Use()`; else `Place(prefab, ctx.AboveRoot, face, …)`. `Place`/`PlacePolygon` take the target root; the layer applied is `FoldLayers.LayerOf(face)` for a face root and Default for the Above root (decided by `root == sheet.Above`; `Place` gains a `Sheet` parameter or a `bool above` — chosen at coding time, noted in Deviations if it changes).
- `StudioPolygonDraft.TryFinish(sheet, …)`: universal prefab ⇒ Above root (Front face only; a draft started in the Back pane with a universal polygon armed is refused at the first click with the same message).
- `MoveMapped`: an element under Above ⇒ moved in sheet space if `face == Front` (the fold pane hands the Front face for the Base), never reparented; a request to move it "to the Back" is refused with a reason (the fold pane never makes one, see below).
- Fold pane: `Context.AboveRoot`, `Context.ShowTerrain`. `BeginElementPress`: candidates are the Above hit at the unmapped desk point and the mapped face root's hit at its authored point; the smaller outline wins, tie to Above (N3a). A universal winner starts a sheet-space drag (offset = element position − cursor; `StudioPlacement.Move`, never `MoveMapped`); a face winner takes today's mapped path. `DrawSelectionThroughFold`: a universal element's authored pieces through `CoverageAbove(DisplayLayers)` drawn in the selection colour (no dimming: nothing of it is ever face-down). `DrawPlacementGhost`: universal armed ⇒ the footprint at the snapped cursor, clipped through `CoverageAbove`, red where nothing remains (over bare desk). `DrawOverlays`: when `ShowTerrain`, every universal region's pieces through `CoverageAbove(DisplayLayers)` filled in its pane colour, drawn after the composite and before the selection, so the preview shows the clip the game will make.
- `SheetStudioWindow`: `ContextFor` sets `AboveRoot = face == Front ? sheet.Above : null`; the fold pane context likewise; `SelectedEditableElement` walks Front, Back, Above. `RefreshObstacles` unchanged (universal regions are not obstacles, and it already reports anything not under a face root — that message must not fire for Above content: skip Above's subtree explicitly).
- `StudioSheetOps.NormalizeFaceLayers`: `+= NormalizeLayers(sheet.Above, 0)`.
- `StudioPlaytest.SpawnHasRoom`: walk `sheet.Above` too, with `sheet.Above` as the root for `TryGetRegionPieces`.

## 6. Behaviour

### 6.1 Region start-up (`TerrainRegion.Awake`)

1. Find the Sheet in parents; resolve exactly one authored collider (unchanged).
2. Root: if `universal`, the region must be under `sheet.Above`; if `sheet.Above` is null ⇒ refuse: *"is marked Universal but the sheet has no Above root; re-create the sheet from the Sheet prefab"*; under Front/Back ⇒ refuse: *"is marked Universal but is under a face root; it belongs under the sheet's Above root"*. If not universal and under Above ⇒ refuse: *"is under the Above root but is not marked Universal"*. Refused = error logged, colliders disabled, region inert (the existing `Refuse` path). Every other case as today.
3. `occluded = new OccludedCollider(authored)`.

### 6.2 Coverage (`SheetOcclusion.Apply`)

- Front/Back: unchanged.
- Above: for each occludee under the root, `footprint = FaceLocalFootprint(sheet.Above)`; result = not the Screen ⇒ `None(Covered)`; else `Layers.CoverageAbove(footprint)`. The occludee applies it exactly as face content does (`OccludedCollider.Apply` with parts in sheet space) and then `Apply()`s the player ignore-pairs.
- Consequences: flat sheet ⇒ the authored collider is live (Whole). Edge fold of depth d from the south ⇒ the sheet's footprint loses the strip y < −h + 2d; a universal region across that strip is clipped to y ≥ −h + 2d; a universal region wholly inside the remaining sheet, even where the Flap landed, is Whole and blocks the player standing on the Flap. Corner fold ⇒ the lifted triangle is gone from under the region. Stacked folds ⇒ the same, from the union of layers.

### 6.3 Who it blocks

- Player: on enable and after every coverage, `Physics2D.IgnoreCollision(live collider, player collider, passable)` with `passable = TerrainRules.IsPassable(blocks, player.Abilities, requiredAbility)`. A filter wall is always passable to the player; a wall never; Water with Swim.
- Blocks: `PushableBlock.TryPush` casts along the push; a hit whose parent region has `StopsBlocks == false` (a player-only region) is skipped; a filter wall stops the block. A player holding a block into a filter wall: the block stops, `PushRules.FollowInput` moves the player exactly as far as the block, so the player stops too — the existing hold behaviour, no new rule. Pulling a block through a filter wall: `PlayerFreeDistance` ignores the wall for the player, the block's own `TryPush` stops at it.
- Arrival (`TravelRules.HasRoom`) and the Studio spawn check: a region counts when `!IsPassableBy(player)`, so a filter wall never refuses an arrival and a universal wall does.
- Pressure plates, unlockables, paperweights: no change; a filter wall over a plate lets the player press it and keeps blocks off it.

### 6.3a A fold that would carry a block under a universal wall (post-review Q1)

1. Drag: each frame `SheetFolds.SetPreview` evaluates the previewed fold; if any block's carried pieces on the resulting stack overlap a block-stopping universal wall, the preview is invalid (red) exactly as for `CoversPlayer`.
2. Release: `TryCommit` refuses with `CarriesBlockUnderWall`; nothing changes.
3. Not affected: a fold whose Flap merely lands on a wall (nothing carried into it); a fold that covers a block on the Base (the block goes under the Flap, not into the wall); a universal *player-only* region (does not stop blocks); a block on the face that is down (not visible, not carried face-up — it rides underneath, and a later fold that turns it face-up is judged then); the fixed Paperweight (its pieces are obstacles, the drag clamps before it could be carried).
4. Unfold: unchanged. A block never reaches the inside of a wall by folding, and an unfold carries content back to where it was authored, which the Studio judges at authoring time (a block authored under a universal wall is an authoring error the Studio draws — the wall is over it — and nothing else checks, as with plate targets).
5. Studio: the preview's drag turns red and refuses on release for the same geometry, from the authored blocks (never moved by preview folds) and the authored walls; a fold-list edit that would make a fold carry a block under a wall is refused with a reason.

### 6.4 Rendering

- Face regions: unchanged (`TerrainFill` on the face layer, rendered into the face texture).
- Universal region fill: a mesh on the region's own (Default) layer, in front of the composited sheet and blocks, behind the player. In play it is the authored footprint clipped through `CoverageAbove(folds.DisplayLayers)`: committed folds, the drag preview and the unfold swing all clip it live; off the Screen the display layers are flat, so the authored shape shows. When the sheet's `showCollision` is off, nothing is drawn (as today).
- Studio Front pane: the pane's camera culls SheetFront only, so an Above fill never appears in the pane texture; the pane's **Terrain** toggle draws universal regions as Handles fills (teal) like it draws walls — the view Aaron uses to see collision in the Studio. Fold preview pane: the same fills clipped through `CoverageAbove`, drawn over the composite. Back pane (Aaron, §3 item 9): universal regions are drawn read-only, mirrored through `SheetGeometry.BackToFront` (Back point (x, y) lies beneath sheet point (−x, y)), as terrain fills and outlines with their labels, so Back content can be authored around a wall that will still be there when the Flap lands; they are never picked, moved, resized or deleted there (the Front pane edits them), and the ghost/placement of a universal prefab is refused there.

### 6.5 Failure modes (no silent failures)

- Universal prefab armed in the Back pane, or on a sheet with no Above root ⇒ refused with the toolbar message; nothing placed.
- A universal region moved by hand in the hierarchy under Front/Back, or a Wall dragged under Above ⇒ error at Awake, region inert (never a wall by accident, never a fold-proof wall by accident); the Studio's overlay draws it in `InvalidColor` if its root and flag disagree (cheap serialized check in `ColorFor`, cached like `gatedCache`).
- `Sheet.above` assigned to something outside the sheet ⇒ error at validate/Awake (as Front/Back).
- A universal region with an invalid polygon ⇒ the existing refusal.
- Missing `PlayerAbilities` ⇒ existing error; a filter wall then blocks the player (fail solid, as Water does today — logged).
- The Above root present but empty ⇒ nothing happens; no cost beyond one `GetComponentsInChildren` per coverage.

## 7. Interfaces & seams

- No new interface. `IFoldOccludee` carries universal content unchanged; the clip differs only in the function `SheetOcclusion` calls. `IArrivalObstacle` unchanged.
- The "who" and "where" of a region are two serialized fields on the one component; kinds remain prefabs.
- Cutting **filter walls**: delete the two Filter prefabs and the two Universal Filter prefabs; `TerrainBlocks` may stay (every remaining prefab is Player | Blocks) or be removed with the `blocks` field and the one skip in `TryPush`.
- Cutting **universal walls**: delete the four Universal prefabs; the Above root and `universal` field may stay inert, or be removed with `CoverageAbove`, the Above branch in `SheetOcclusion`, the fill's live-parts path and the Studio's `AboveRoot` plumbing. Neither cut touches fold geometry, occlusion of face content, or block rules beyond the one skip.
- The fill for universal content is still `TerrainFill`: removing the component from the prefabs removes the collision view for them as for every region.

## 8. Testing

Mechanical (Unity CLI, edit mode only — Play Mode was not requested and is not entered):

1. `recompile` → `errors[]` empty.
2. Collision check (editor not playing, no test run, not compiling, scene not dirty — the Desk scene was clean at 18:10; Aaron has `Sheet (2,0)` open in a prefab stage, which the runner closes: I reopen it afterwards with `PrefabStageUtility.OpenPrefab`). If the scene is dirty at test time, stop and ask; fallback is the per-class eval runner (memory `editmode-tests-via-eval-runner`), reported as a partial run.
3. `run_tests {"mode":"editor"}` — summary and every failure verbatim, pre-existing included. The suite is ~400 tests and can exceed the 30 s command timeout; the truth is the `Run finished` line in `Temp/pipeline_console_log.json` / Editor.log.
4. New/extended Edit Mode tests:
   - `TerrainRulesTests`: Player-bit clear ⇒ passable regardless of ability; Player set ⇒ existing rule; `StopsBlocks` per bit; `None` blocks nobody.
   - `SheetLayersTests.CoverageAbove_*`: flat ⇒ Whole; flat, footprint past the edge ⇒ Clipped to the rect; south edge fold, region across the removed strip ⇒ Partial with the exact area; region inside the landed area ⇒ Whole (unmoved); corner fold, square in the lifted corner ⇒ area check; region entirely in an emptied corner ⇒ None/Covered; fold-on-fold ⇒ parts disjoint (areas sum to the intersection with the union — checked against the flat rect minus the removed strips).
   - `TerrainFillTests`: a universal region (Sheet built with an Above root, no `SheetFolds`) with `ShowCollision` on draws the authored shape; with a `SheetFolds` on the sheet (its `Layers` is `Flat` by field initializer, no Awake needed) and a stack scripted through an `internal` test seam on the fill (`RebuildFrom(SheetLayers)`), the mesh area equals the footprint clipped to that stack's footprint; a face region's fill is unchanged by the same stack.
   - `StudioPlacementTests`: placing a universal prefab lands under Above on layer 0; placing a Wall lands under Front on SheetFront (regression); `IsUniversal` on the six prefabs and false on Wall/Water/Gate; `MoveMapped` of a universal element to the Back face refuses with a reason; `PickElement(sheet.Above, …)` finds it; `CanEdit` true for it.
   - `StudioPlaytestTests`: a universal wall covering the spawn ⇒ no room; a filter wall covering it ⇒ room.
   - `FoldLandingRulesTests` (new): edge fold carries a block on the Flap side into a wall at the landing spot ⇒ true; the same wall away from the landing ⇒ false; block on the Base under where the Flap lands ⇒ false (covered, not carried); a wall with no block-stopping is the caller's filter (the rule takes pieces); corner fold carrying a block partly ⇒ true if the carried part overlaps; a Back-side block carried face-up by a fold ⇒ judged; fold index mismatch (a piece on an earlier fold's Flap) ⇒ false.
   - `StudioFoldModelTests`: with a block and a universal wall set, the carrying fold is `CarriesBlockUnderWall` (preview invalid, commit refused); without the wall it commits; a list-edit that deepens a fold into the wall is refused with the reason.
   - `TerrainRegionTests` (new, edit mode): Wall, Water, Wall (Polygon), Water (Polygon), Gate, Gate (Off) deserialize to `blocks = Player | Blocks`, `universal = false` (plan review N5 regression); the six new prefabs carry their intended values.
   - **Untested mechanically** (plan review N5, stated plainly): `PushableBlock.TryPush` stopping at a filter wall and passing a player-only region, and `SheetOcclusion` handing Above `None` off-Screen — both need `Awake`-built runtime state and Play Mode, which is not opted in. They are on Aaron's play list.
   - Prefab checks through eval: the six prefabs load, have exactly one authored collider, `TerrainRegion` + `TerrainFill`, the intended field values and colours; `Sheet.prefab` has the Above root wired; two sheet variants load with `Above` resolved; `Tools/check_links.py` passes.
5. Edit-mode eval probes: instantiate `Sheet.prefab` in a preview scene, add a Universal Wall under Above, call `SheetOcclusion.Apply` by reflection with a scripted `SheetLayers` (or use `SheetLayers.CoverageAbove` directly) — the collider's live path matches the expected clip. Whatever needs Awake is stated as not covered.

Aaron plays (feel and intent the CLI cannot judge):

- A universal wall stays put and keeps blocking while a Flap slides under it; it ends exactly at the folded-down edge, never past it, during the drag preview and the unfold swing too; on a landed Flap the player is blocked by it.
- A neighbouring sheet's universal wall while it peeks in, in the zoomed-out view, and on the sheet just left: the blue fill stays (it is drawn from the display layers, not the collider).
- Its fill reads as teal and "above the paper" in the game; whether the two teals and the violet read distinctly from Water's blue at a glance, and whether the fill should sit above or below Scuffy (it is behind the player now).
- A Back-authored block that a fold would turn face-up under a universal wall: the drag is red and refused.
- Dragging a fold that would carry a block under a universal wall: the preview turns red and release does nothing; a fold that only covers the wall's region with a Flap carrying nothing is fine; the Studio's preview refuses the same drag.
- A filter wall: the player walks through, a pushed block stops at it, a held block stops the player; pulling a block back into a filter wall stops the block and the player.
- Whether a universal filter wall composes as expected on a folded sheet.
- Studio: placing, moving, resizing, vertex-editing, deleting, duplicating universal regions in the Front pane; the Back pane not showing them; the fold preview clipping them; the refusal messages.
- Undo/redo behaviour of universal placement in the Studio (hooked through the same Undo calls; not probed headlessly).

## 9. Assumptions (engineering)

1. Sheet-local z 0.3 for the Above root is in front of every composited layer (BaseZ 0.5 minus 0.01 per layer; 20 layers is the renderer's practical cap) and every block (SurfaceZ ≈ 0.495), and behind the player (world z −0.5, sheets at 0). Verified by probe today.
2. The Above root at identity ⇒ sheet space = Front-space for authored positions, so Above content shares the Front pane's coordinates and the existing outline/footprint helpers work with the Above root as the "face root" argument.
3. `PolygonCollider2D` paths from `CoverageAbove` are disjoint (subtraction of upper layers), so overlapping-path behaviour of Unity's polygon collider never arises. Same guarantee `Coverage` gives today.
4. Blocks are kinematic bodies moved by `MovePosition`; their only terrain interaction is the cast in `TryPush`, so skipping a hit there is the whole "blocks pass" rule. `BlockPusher`'s casts qualify hits by `PushableBlock` rigidbody and are unaffected.
5. A null `Sheet.above` is legal (old fixtures and any sheet without universal content); every authored sheet inherits the root from `Sheet.prefab`.
6. Regions that predate the fields deserialize to `Player | Blocks` and `universal = false` (field initializers; no migration needed).
7. Prefab creation, `Sheet.prefab` editing and verification go through the open editor (`AssetDatabase.CopyAsset`, `LoadPrefabContents`/`SaveAsPrefabAsset`, `ImportAsset`) because `Sheet.prefab` is loaded and modified-uncommitted; disk-written files are then re-read to confirm.
8. Colour constants for the Studio overlays live in editor code (as today's Wall/Gated colours); the game-side fill colours are the prefabs' Inspector values.

## 10. Open questions

None. Remaining stated assumption for Aaron to overrule at playtest: the exact teal/violet values (violet for filter walls is mine). The Back-pane question was asked and answered (§3 item 9).

## Review (round 1, plan-reviewer, 2026-09-28) — verdict BLOCK

- **B1** (blocking) — the universal fill was to be built from the collider's live parts, which are empty off-Screen, so blue walls would vanish on every sheet but the Screen. **Accepted**: the fill is derived from `SheetFolds.DisplayLayers` through `CoverageAbove`, rebuilt on `Displayed`/`Changed`; `LiveParts`/`CoverageApplied` dropped (§5 `TerrainFill`).
- **N1** — fill followed committed folds only, not the drag preview / unfold swing. **Accepted**, same fix.
- **N2** — `CoverageAbove` returned a multi-path clip for an intact region under a landed Flap. **Accepted**: Whole iff total surviving area ≥ footprint area (§5).
- **N3** — cross-root pick rule and magnet targets unspecified. **Accepted**: smallest outline across roots, tie to Above (listed last); magnet over the pane's roots (§5 Studio).
- **N4** — non-universal occludees under Above would receive a sheet-space result. **Accepted**: reported and skipped by `SheetOcclusion` (§5).
- **N5** — no mechanical test of the block rule / off-Screen Above. **Accepted in part**: prefab-default regression test added; the runtime rules are stated as untested and put on Aaron's play list (Play Mode not opted in; `Physics2D.Simulate` on Awake-built blocks in edit mode is not something the fixtures support).
- **N6** — flat trim differs from face content. **Accepted**: stated in the Bible paragraph.
- **Q1, Q2** — escalated to Aaron (step 5); answered: deny the fold like the player rule (§1 item 6, §5 *Fold refusal*, §6.3a); teal (prefab table).

## Review (round 2, plan-reviewer, 2026-09-28) — verdict BLOCK

- **B1** (blocking) — `RefuseFold` guarded on `IsVisible` would skip a Back-authored block turned face-up by the fold. **Accepted**: guard on `ok && isActiveAndEnabled` only; `VisiblePieces` on the stack after the fold decides.
- **N1** — stale `LiveParts`/`CoverageApplied` references and a fill test of the dead path. **Accepted**: struck everywhere; fill test rewritten against a `SheetFolds` fixture and a scripted stack.
- **N2** — `sheet.Folds` is null until Awake. **Accepted**: `GetComponent<SheetFolds>()` in `BindSheet`.
- **N3** — a null `PlayerAbilities` made a filter wall solid to arrival and the Studio spawn check. **Accepted**: passable when the Player bit is clear.
- **N4** — the transformed z put the fill in front of the Seam overlay. **Accepted**: z fixed at 0.
- **N5** — the Studio judges blocks at their authored places. **Accepted**: stated as an editor limit in the model's remarks.
- **N6** — stale "blue"/"paper" wording. **Accepted**.
- **N7** — play-list case for the Back-authored block. **Accepted**.
- **Q1** (Back pane display of universal regions) — escalated to Aaron.

Two review rounds done; per the workflow there is no third round. Anything still contested is reported with the code review.

## Deviations (engineering, noted while coding)

- `StudioPlacement.Place` takes the target root (face root or Above) and derives the layer from it (`LayerFor`); `TargetRoot(prefab, sheet, faceRoot, aboveEditable, out reason)` is the one place that decides where a prefab goes and why it cannot. `PlacePolygon` and the draft go through it.
- Pick tie rule: implemented as a last-to-first search over the roots with the strict smaller-area test kept, so in-root behaviour (colliders before props) is unchanged and Above, listed last, wins a tie. The fold pane compares the Above hit (unmapped) and the face hit (mapped) by outline area through `TryPickElement`.
- The Back pane draws Above content (Aaron's answer, §3 item 9) through `AboveToPane` (x mirrored) and marks a region whose root and Universal flag disagree in the invalid colour; `Context.AboveEditable` is the Front-pane-only edit switch.
- Fold pane: placing a universal prefab needs the cursor over the sheet's footprint (like face placement needs a layer under it); dragging an Above element is a plain sheet-space `Move`.
- `TerrainFill.RefreshWith(SheetLayers)` is the internal test seam for clipping to a scripted stack; `Sheet.AboveLayer` (0) names the Above layer.
- `SheetOcclusion.NotifyAbove` reports and skips any occludee under Above that is not a universal `TerrainRegion` (review N4).
- The landing rule's semantics, confirmed by the tests: a Front-face block that a Flap lifts rides face-down under the Flap and never lands inside a wall; only content the fold turns face-up (a Back-side block, or a block on a landed Flap folded again) is carried onto the surface. `FoldLandingRules` is as planned; two of my first test cases assumed a Front block lands face-up and were corrected.
- A fold's landed strip is still sheet footprint: `CoverageAbove` after a west fold of depth 1 keeps x ≥ −4.5, not x ≥ −3.5. One test expectation was corrected.

## Verification (2026-09-28)

- `recompile`: completed, no errors (after every change).
- Editor edit-mode suite through the pipeline (`run_tests {"mode":"editor"}`, results from the Editor.log `Run finished` line and a one-off Test Runner API reporter, since the pipeline's own status file carries no per-test results): **535 total, 532 passed, 3 failed**. The three failures are not from this task: `DrawingAssetsTests.PowerCroissant_ShowsItsDrawing_AndGrantsPush` and `PowerCroissantIdle_IsThreeFramesAtTheSharedScale` (the Power Croissant animation asset has an empty frame slot; `Tools/check_links.py` reports the same missing sprite guid on that prefab), and `StudioLinksTests.Wire_AppendsTargets_ARepeatIsOneEntry_AndUndoRevertsIt` (a MissingReferenceException after two `Undo.PerformUndo` calls: the placements are undone with the wiring; the test and `StudioLinks` are uncommitted in-progress work outside this task and none of the touched code is on that path).
- Every class this task changed or added passed in the real runner: TerrainRulesTests, TerrainRegionTests (new), SheetLayersTests, FoldLandingRulesTests (new), TerrainFillTests, StudioPlacementTests, StudioPlaytestTests, StudioFoldModelTests.
- Assets: `Sheet.prefab` diff against a backup is exactly the added Above root; the six prefabs load with the intended fields and colours; `check_links.py` has only the pre-existing Power Croissant failure. Aaron's open prefab stage (Sheet (2,0)) was closed by the runner and reopened; its file on disk is byte-identical before and after.
- Not run: Play Mode (not opted in). Runtime-only behaviour is on the play list.
- Skipped, as §8 allowed: a `StudioFoldPaneTests` case (there is no such test class and the pane's press chain is not driven headlessly for elements). Untested mechanically, Awake-built: `TerrainRegion.RootOf`'s four refusal messages, `PushableBlock.RefuseFold`/`BlockWalls`, `SheetOcclusion.NotifyAbove`.
- Intended: keyboard nudge/delete (`SelectedEditableElement`) acts on a selected universal region whichever pane was last clicked - the Back pane's read-only rule is about placing and picking there, not about the selection (code review S6).
- Corrected after code review S7: on the Studio stage the sheet does carry `SheetFolds` (flat), so a universal fill there is the authored shape trimmed to the sheet rect, as the game shows a flat sheet; only a bare fixture without `SheetFolds` shows the untrimmed authored shape.

## Code review (code-reviewer, 2026-09-28) — verdict APPROVE WITH FIXES

- **S1** face fills refreshed on every fold event — **fixed**: fold events refresh only universal fills (`OnFoldsChanged`).
- **S2** stray occludee under Above reported on every Apply — **fixed**: reported once.
- **S3** teal duplicated across the two Studio classes — **fixed**: `StudioPane.UniversalFill` is shared (the prefab value and the gizmo colour are separate by design: Inspector data and a runtime gizmo).
- **S4** Bible §5 sentence missing — **fixed**.
- **S5** skipped/untested items not stated — **fixed** in Verification.
- **S6** keyboard edits of a universal region from the Back pane — **noted as intended** in Verification.
- **S7** plan/comment said "edit mode: authored shape" — **fixed** (comment and plan).
- **S8** `FoldDragInput` summary — **fixed**.
- **Q2** (face panes: Collision on but Terrain overlay off hid universal fills) — resolved in code without a question: the panes fill universal regions whenever the sheet shows collision, matching what the game draws.
- **Q1** (Front block carried face-down under a wall) and **Q3** (fill depth relative to blocks and Scuffy) — put to Aaron.
