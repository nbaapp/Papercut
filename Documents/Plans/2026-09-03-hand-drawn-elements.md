# Hand-drawn Block, Button and Tree (line-boil art for elements)

Date: 2026-09-03. Workflow: full (Aaron: *"1. Full"*).

## 1. Task

Aaron (verbatim): *"I imported new visual assets for the block, the button, and the tree. (I also swapped out the map I have for one without trees). Can you set those assets up, giving them all the right settings, and their line boil animations are set up and stuff? After you do that, can you make sure that the level editor uses those visuals instead of whatever placeholders were there before?"*

In scope:

- Import settings for the nine new frames (`Assets/Papercut/Sprites/{Box,Button,Tree}/PNGSet/PNG_000{1,2,3}.png`), matching the convention the Scuffy frames established.
- Three `SketchAnimation` assets (Box, Button, Tree idles) and the `SketchAnimator` wiring on each element.
- The Block draws its hand-drawn frames through its existing runtime-clipped mesh (a plain sprite cannot be clipped by folds or ride a Flap), and shows the drawing in the editor (Scene view, Sheet Studio panes, palette icon).
- Both plate prefabs (Hold, Latch) show the Button drawing.
- A new `Tree` element prefab: an untraversable, fixed-size Wall-kind terrain with a hand-drawn tree and a collider "slightly thinner and shorter than the shape of the image" built from a few boxes.
- The Sheet Studio resize rule changes so fixed drawings are not resizable (plates, Tree, Block), while tiled placeholders (Wall, Water, Gate) still are.
- Two Trees placed on `Sheet (0,0)` where the old Map 1 art had trees (engineering assumption — see §9; trivially deletable).

Out of scope: new art for Wall/Water/Gate (still placeholder tiles); a pressed-state Button drawing (none exists — pressed is a tint); distinct Hold vs Latch looks; any change to fold, occlusion or push rules; scene-view hiding of Back content (separate question from earlier today).

## 2. Design references

- Design Doc, Art: *"The art style will be very sketchy and rough … Everything should feel like it exists on and in the paper."* The line boil (`SketchAnimation`/`SketchAnimator`, plan 2026-08-25) is how that is done for Scuffy; elements get the same.
- Bible §3 terrain **[DECIDED 2026-08-25]**: placed objects with box colliders over hand-drawn art; *"kinds are prefabs differing in data, not code."* A Tree is a Wall-kind `TerrainRegion` prefab — data, no new terrain code.
- Bible §5 movable objects **[DECIDED 2026-08-27]** and the `PushableBlock` remarks: the block is drawn as a mesh on the Default layer, clipped to visible pieces, at each piece's surface depth. Unchanged; only where the texture comes from changes.
- Bible §9 **[TENTATIVE]**: blocks and plates are playtest elements; a tree is just terrain. Cutting any of them is still deleting a prefab (§7 of this plan).
- Bible §4 rendering **[DECIDED]**: face content renders through per-face cameras into textures, so a sprite on a face layer is clipped/folded by the compositor for free. This is why the Tree and the plates need no special code and why the Block (which moves between layers) does.
- No `[DECIDE]` item is touched. World unit scale (§3 decision #9) is already fixed in code at 1 unit = 1 inch (`SheetGeometry`); the sprite scale below only follows it.

## 3. Decisions already made by Aaron (2026-09-03)

1. Workflow: *"Full"*.
2. Sizes: one shared pixels-per-unit for all hand-drawn art (the same 2000 as Scuffy, so drawings keep the relative scale of the art files), and the block and plate colliders shrink to match their drawings. Aaron: *"sure, I'll adjust it if it becomes necessary."* Resulting sizes: Box ≈ 0.72 × 0.68, Button ≈ 0.66 × 0.66, Tree ≈ 0.63 wide × 0.75 tall (Scuffy is 0.57 × 0.85).
3. Tree: a Wall-kind terrain with a collider. Aaron: *"Yes, it has a collider - it should be slightly thinner and shorter than the shape of the image, so the player can go behind it a bit. Feel free to use a few boxes to get the shape right."*
4. Button look: Aaron: *"No, they don't need different tints, but yes on the darker tint for pressed."* Hold and Latch look identical; pressed = darker tint of the same drawing.
5. Resizing: Studio resize becomes "resizable only when the drawing tiles" (Wall, Water, Gate stay resizable; plates, Tree, Block are fixed-size). Aaron: *"sure"*. This supersedes the 2026-08-31 phase-3 decision that plates are resizable.

## 4. Files

Created:

| File | Purpose |
|---|---|
| `Assets/Papercut/Animations/Box Idle.asset` (+ `.meta`) | `SketchAnimation`: Box frames 1–3, 4 fps. |
| `Assets/Papercut/Animations/Button Idle.asset` (+ `.meta`) | `SketchAnimation`: Button frames 1–3, 4 fps. |
| `Assets/Papercut/Animations/Tree Idle.asset` (+ `.meta`) | `SketchAnimation`: Tree frames 1–3, 4 fps. |
| `Assets/Papercut/Prefabs/Terrain/Tree.prefab` (+ `.meta`) | The Tree element (§5). Lives in the Terrain folder so the Studio palette lists it. |
| `Assets/Papercut/Scripts/Sketch/SpriteMapping.cs` | Pure helper: where a flat-sheet point lands on a sprite's texture (UV), given the sprite's rect, pivot and pixels-per-unit. Used by the block's mesh. |
| `Assets/Papercut/Tests/EditMode/SpriteMappingTests.cs` | Tests for the UV mapping. |
| `Assets/Papercut/Tests/EditMode/DrawingAssetsTests.cs` | Tests that the three animations are playable at the shared scale and that the four element prefabs are wired to them. |

Modified:

| File | Change |
|---|---|
| `Assets/Papercut/Sprites/{Box,Button,Tree}/PNGSet/PNG_000{1,2,3}.png.meta` (9 files) | Single sprite on the full canvas, PPU 2000, mipmaps on, max size 512, leftover auto-slice entries removed (§6.1). |
| `Assets/Papercut/Scripts/Objects/PushableBlock.cs` | `Texture texture` → `SketchAnimator drawing`; the mesh takes its texture from the animator's current frame at the frame's own scale, centred on the block; the drawing's own renderer is switched off at runtime. |
| `Assets/Papercut/Prefabs/Objects/Block.prefab` | New `Drawing` child (SpriteRenderer + SketchAnimator, Box Idle); collider 0.74 × 0.70 (was planned 0.68; frame 1 reaches +0.349, code review S2's relationship test caught the clip); polygon paths to match; tint white; `drawing` assigned; `texture` removed. |
| `Assets/Papercut/Prefabs/Objects/Hold Plate.prefab`, `Latch Plate.prefab` | Button drawing (Simple draw mode, white), SketchAnimator (Button Idle), collider 0.66 × 0.66, released tint white / pressed tint darker. |
| `Assets/Papercut/Editor/StudioPlacement.cs` | `IsResizable`: terrain/plate **and** a same-object SpriteRenderer in a non-Simple draw mode. |
| `Assets/Papercut/Editor/StudioPane.cs` | Element outline colour looks for `TerrainRegion` in children (the Tree's regions are children of its root); the name label is drawn once per element at the top of its authored footprint, so an element whose colliders are on children (Tree) is labelled too (review N7). |
| `Assets/Papercut/Tests/EditMode/StudioPlacementTests.cs` | Resizable-set test updated to the new rule; Tree footprint/pick tests. |
| `Assets/Papercut/Sheets/Sheet (0,0).prefab` | Two Tree instances at the old map's tree positions. |

## 5. Components & data

### 5.1 Sprite import convention (data)

Per frame `.meta`, matching Scuffy (plan 2026-08-25 §7–8): `spriteMode: 1` (Single — the whole 2100 × 2100 canvas is the sprite, so frames line up by canvas and the wobble is the artist's, not a slicing artefact), `spritePixelsToUnits: 2000` (canvas = 1.05 units; drawn extents above), `enableMipMap: 1`, `maxTextureSize: 512` on the default and every platform entry, `filterMode: 1` (bilinear, already), `spriteMeshType: 1` (Tight, already), pivot centre (already). The importer's leftover auto-slice fragments (`spriteSheet.sprites`, `internalIDToNameTable`, `nameFileIdTable`) are cleared: in Single mode they are dead data, and nothing references them. (The Scuffy metas kept theirs; clearing is the tidier choice and changes nothing functional.)

Size arithmetic at 512: a 2100 canvas becomes 512 px, so the Box drawing is ~350 px across 0.72 units ≈ 490 px/unit; the camera shows ~10 units on 1080 px ≈ 108 px/unit, so ~4.5× headroom, same reasoning as Scuffy. Not a multiple of 4 → stays uncompressed RGBA32 like Scuffy (~1 MB/frame with mipmaps); 9 frames ≈ 9 MB. Fine for WebGL.

Measured drawn extents (alpha > 32, all three frames, units at PPU 2000, from the canvas centre):

| Drawing | x | y | Note |
|---|---|---|---|
| Box | −0.34 … +0.38 | −0.32 … +0.35 | frame 2 has a stray stroke to +0.38 |
| Button | −0.33 … +0.32 | −0.31 … +0.33 | |
| Tree | −0.33 … +0.30 (frame 3 stray to +0.48) | −0.40 … +0.40 | canopy widest at y ≈ −0.2; trunk y −0.36 … −0.23, x −0.21 … +0.19 |

### 5.2 `SketchAnimation` assets

`Box Idle`, `Button Idle`, `Tree Idle`: `frames` = the three PNGs' sprites (`fileID 21300000` — Unity's default single-sprite id, as the Scuffy assets reference), `framesPerSecond: 4` (Scuffy Idle's rate). The rate is the tunable, on the asset.

### 5.3 `PushableBlock` (modified)

Serialized fields, `[Header("Drawing")]`:

- `SketchAnimator drawing` (new, replaces `Texture texture`). Tooltip: *"The block's drawing: a SketchAnimator on a child whose SpriteRenderer shows the block in the editor. In play that renderer is switched off and the frame it shows is drawn on the clipped mesh instead, at the frame's own scale, centred on the child's position."*
- `MeshFilter visual`, `Material material` — unchanged.
- `Color tint` — unchanged tunable; prefab value becomes white (the drawing carries its own colour).

Behaviour: §6.3. Public surface unchanged.

### 5.4 `SpriteMapping` (new, `Papercut` namespace, pure)

```csharp
public readonly struct SpriteFrame   // everything the mapping needs, so it can be tested without a Sprite
{
    public readonly Rect Rect;        // the sprite's rect on its texture, pixels
    public readonly Vector2 Pivot;    // pivot in pixels from Rect.min
    public readonly float PixelsPerUnit;
    public readonly Vector2 TextureSize;
    public static SpriteFrame Of(Sprite sprite);   // sprite.rect, sprite.pivot, sprite.pixelsPerUnit, texture width/height
}
public static class SpriteMapping
{
    /// UV of flat point `point` when the frame's pivot sits at `anchor` (flat units) and the frame is drawn at its own scale.
    public static Vector2 Uv(in SpriteFrame frame, Vector2 anchor, Vector2 point)
        => (frame.Rect.position + frame.Pivot + (point - anchor) * frame.PixelsPerUnit) / frame.TextureSize;
}
```

`sprite.rect`, `sprite.pivot` and `sprite.pixelsPerUnit` are all in the imported texture's pixels (Unity rescales PPU with the max-size reduction, so world size is preserved). Points outside the canvas give UVs outside 0…1; the textures are clamp-wrapped and their borders transparent, so nothing shows there.

### 5.5 `Block.prefab`

- Root: `BoxCollider2D` 0.74 × 0.70 (covers every frame's strokes but the one stray at +0.38; 0.68 was planned, but frame 1 reaches +0.349); `PolygonCollider2D` authored path ±0.37/±0.35 (runtime replaces it anyway); `PushableBlock.drawing` → the `Drawing` child's animator; `tint` white; `texture` line removed.
- `Visual` child: unchanged (MeshFilter + MeshRenderer, URP Unlit material) — the runtime mesh, parts[0].
- New `Drawing` child at local (0, 0, **−0.05**): `SpriteRenderer` (sprite = Box frame 1, material = URP `Sprite-Unlit-Default` like every other face content, Simple draw mode, white) + `SketchAnimator` (idle = Box Idle, randomiseStart on). This is what the Scene view, the Studio panes and `AssetPreview` (palette icon) show. The Block root stays at z 0 (its runtime mesh depth comes from the fold renderer, not the root), so the child carries the element z: at z 0 the sprite would sit on the opaque Surface quad and behind the face art at −0.01, invisible in every editor view (review B1).

### 5.6 `Hold Plate.prefab`, `Latch Plate.prefab`

- `SpriteRenderer`: sprite = Button frame 1, `m_DrawMode: 0` (Simple), colour white, `m_Size` irrelevant.
- `SketchAnimator` added on the root (it requires the SpriteRenderer, which is on the root): idle = Button Idle, randomiseStart on.
- `BoxCollider2D`: 0.66 × 0.66, `m_AutoTiling: 0`, still a trigger.
- `PressurePlate`: `releasedColor` (1, 1, 1, 1), `pressedColor` (0.6, 0.6, 0.6, 1) — both tunables, existing fields. Same values on Hold and Latch (Aaron: no different tints).

### 5.7 `Tree.prefab`

```
Tree                (z −0.05, the element z; SpriteRenderer: Tree frame 1, Sprite-Unlit-Default, Simple, white; SketchAnimator: Tree Idle)
├─ Canopy           (TerrainRegion wall; BoxCollider2D 0.52 × 0.44, local centre (0, −0.02))   → y −0.24 … +0.20, x ±0.26
└─ Trunk            (TerrainRegion wall; BoxCollider2D 0.32 × 0.13, local centre (0, −0.30))   → y −0.365 … −0.235, x ±0.16
```

The drawing spans y −0.40 … +0.40 and x −0.33 … +0.30; the canopy box stops 0.20 below the tip and is ~0.1 narrower than the drawing on each side, so the player overlaps the top and the outer fronds ("go behind it a bit"). The trunk box is inside the drawn trunk. Both boxes are ordinary prefab data — adjust in the Inspector; the Studio does not resize Trees.

Why children rather than one region: `TerrainRegion` is one `BoxCollider2D` per component (`OccludedBoxCollider`), so "a few boxes" = a few regions. Everything that finds regions does so recursively (`SheetOcclusion.Notify`, `SolidFootprints`, Studio hit-testing/footprints), and `TerrainRegion.Awake` finds its Sheet by `GetComponentInParent`, so grandchildren work without code changes — except the Studio's outline colour (§4, `StudioPane.ColorFor`), which looked only at the root.

### 5.8 `StudioPlacement.IsResizable` (modified)

```csharp
public static bool IsResizable(GameObject element)
    => element != null
    && (element.GetComponent<TerrainRegion>() != null || element.GetComponent<PressurePlate>() != null)
    && element.GetComponent<BoxCollider2D>() != null
    && element.TryGetComponent(out SpriteRenderer sprite) && sprite.drawMode != SpriteDrawMode.Simple;
```

Doc comment: resizable = a terrain region or plate whose drawing tiles, i.e. whose sprite is in a non-Simple draw mode — the only case where `Resize` can keep the picture and the collider together. Wall/Water/Gate (tiled placeholder) yes; plates and Tree (fixed drawings) no; Block no (as before, and it also has no root SpriteRenderer). Stated consequence: when Wall/Water/Gate get hand-drawn Simple-mode art they stop being resizable too (review N7). `Resize`'s doc comment loses the stale 'Hold Plate tiled pattern' wording (review N6).

## 6. Behaviour

### 6.1 Import

Editing the nine `.meta` files on disk, then `AssetDatabase.ImportAsset(path, ForceUpdate)` through the CLI. Each texture becomes one sprite (`21300000`) 512 × 512 with PPU 2000·(512/2100) ≈ 487.6 — Unity keeps `sprite.pixelsPerUnit` consistent with the resized texture so the world size stays 1.05 units. Failure mode: if a meta is malformed Unity re-imports with defaults and logs; the test in §8 (PPU/size check) catches a frame that came in wrong.

### 6.2 Plates and Tree at runtime

Nothing new: the `SketchAnimator` cycles the SpriteRenderer's sprite; the sprite is face content on the face layer, rendered into the face texture by the fold renderer's camera and therefore folded, covered and exposed exactly like the map art under it. `PressurePlate.ShowPressed` tints the same renderer (white ↔ 0.6 grey). `TerrainRegion` children clip their boxes under folds as before.

### 6.3 Block at runtime

1. `Awake`: after the existing checks, `drawing == null` → error, `ok = false` (like a missing Visual). Else `drawingRenderer = drawing.GetComponent<SpriteRenderer>()` (guaranteed by `SketchAnimator`'s RequireComponent) and `drawingRenderer.enabled = false` — done whether or not `ok`, so a misconfigured block never shows a flat sprite that the face camera would render as Sheet content. The `drawing` must be a direct child of the block, unrotated and unscaled: otherwise error and `ok = false` — its local x/y offset is the drawing's anchor, and a grandchild's or a rotated/scaled child's offset would be silently wrong (review N3a).
2. `Start`: unchanged — everything under the block goes to the Default layer (the disabled sprite included; harmless).
3. `DrawVisual` (every `Place`/`Redraw`): `sprite = drawingRenderer.sprite`. Null → one error (`"…: its Drawing shows no sprite; the block is invisible"`, latched so it is not per frame), all parts disabled, return. Else `block.SetTexture(BaseMap, sprite.texture)`, `frame = SpriteFrame.Of(sprite)`, anchor = `flatRect.center + drawing.localPosition.xy`, and each mesh vertex's UV = `SpriteMapping.Uv(frame, anchor, flat)` instead of the old stretch-to-rect mapping. The mesh polygons are still the visible pieces of the block's flat rect, so the drawing is clipped to the collider rect (which is why the collider was sized to the drawing) and by folds exactly as before.
4. `LateUpdate` (new): if `drawingRenderer.sprite != shownSprite`, `Redraw()`. `DrawVisual` records `shownSprite` itself, so a fold-driven redraw is not followed by a redundant one (review N3b). `SketchAnimator.Update` changes frames at 4 fps; `Redraw` rebuilds a few small polygons — trivial. `LateUpdate` rather than `Update` because `PushableBlock` runs at execution order −5, before the animator, and would otherwise lag a frame.
5. `OnDisable`/`OnEnable`, reset, push, climb: unchanged.

Editor (not playing): the `Drawing` sprite is visible and the mesh is empty (as today), so the Studio and Scene view show the drawing at the authored place, folded by the Studio fold preview like any face content.

### 6.4 Sheet Studio

- Palette: `Tree` appears automatically (Terrain folder); every icon is `AssetPreview` of the prefab, which now renders the sprite for Block, plates and Tree (before, Block's icon was its name — empty mesh).
- Placing/moving/duplicating/deleting a Tree: unchanged code paths; the Tree root is the element, its regions come along.
- Resize handles: shown only for Wall/Water/Gate now (§5.8). Existing `Sheet (0,0)` instances: the one Hold Plate has no size override, so it takes the new 0.66 collider; the Gate keeps its size overrides (still tiled/resizable).
- Hit-testing picks by collider outline, so a Tree is selectable on its trunk and canopy boxes, not on the uncovered top 0.2 units of the drawing. Known limitation, noted for Aaron; extending picking to sprite bounds is a separate small change if it annoys.
- Fold pane: `AuthoredFootprint` of a Tree is the union of its child boxes (existing fallback path) — mapping/highlighting use that.

### 6.5 Sheet (0,0)

Two `Tree` prefab instances under Front, layer SheetFront, at sheet-local (0.40, 2.10) and (4.45, 2.91): the centres of the two trees painted on the previous Map 1 (measured from the old PNG in git LFS: image pixels → sprite-local at 300 PPU → through the art's +90° rotation). The painted trees were ~2.2 units tall; the new ones are 0.75, placed at the same centres.

## 7. Interfaces & seams

- No new interfaces. `SpriteMapping` is a pure function so the block's UV rule is testable and could serve any other mesh-drawn object.
- Cutting: Tree = delete `Tree.prefab` (and its two instances); the plates/Block = delete their prefabs as before. The three animation assets and the nine textures are data. Nothing in fold/occlusion/collision code changed.
- Reverting the Studio resize rule = one expression in `IsResizable`.

## 8. Testing

Edit Mode tests (run through `run_tests {"mode":"editor"}`):

- `SpriteMappingTests`: the anchor maps to the pivot's UV (centre pivot → (0.5, 0.5) for a full-texture sprite); a point one canvas-width right of the anchor maps to u + 1; a sprite rect offset inside a larger texture shifts UVs by the rect origin; a drawing-child offset moves the anchor.
- `DrawingAssetsTests`: each of the three animations `IsPlayable` with 3 frames; every frame sprite covers its whole texture (single-sprite import: `rect == (0,0,w,h)`) and has the shared scale (`rect.width / pixelsPerUnit ≈ 1.05`); Block's `PushableBlock.drawing` (via SerializedObject) is a child animator whose renderer shows a Box frame; both plates have a `SketchAnimator` with `idle` = Button Idle and a Simple-mode sprite; Tree has a `SketchAnimator` with Tree Idle and ≥ 1 child `TerrainRegion` with `requiredAbility == None`.
- `StudioPlacementTests`: resizable set = Wall, Water, Gate; not Hold Plate, Latch Plate, Block, Tree. Tree: `AuthoredFootprint` is the union of its child boxes; `PickElement` at the trunk returns the Tree root; `CanEdit` true.
- Existing suites (fold, placement, plates, block push tests) must stay green — the block's push/placement code is untouched.

Mechanical checks (Unity CLI): `recompile` → no errors; `run_tests editor` → report Summary; `python Tools/check_links.py` → every reference in the prefabs/sheet resolves; `ImportAsset` on the nine textures then `eval` reading `sprite.rect`/`pixelsPerUnit`/texture size for one frame; Play Mode on the Desk scene: `get_console_logs` clean, `capture_game_view` (describe: the Hold Plate on Sheet (0,0) shows the grey button; the two trees at the top), `eval` on a Block placed for the test (there is none on any sheet — I will place one on Sheet (0,0) only inside the play session via eval, next to Scuffy so the scale is visible in the capture, not in the asset): its `Drawing` renderer disabled, `Visual` renderer enabled, property-block texture = the current frame's texture, the texture changing over ~1 s, and — review N4 — the mesh's UV at a known vertex equal to `SpriteMapping.Uv` for that vertex's flat point, checked flat and again with a fold committed over part of the block. Then `editor_stop`. Sheet Studio: `capture` of the Studio window is not available; instead `eval` `AssetPreview.GetAssetPreview` non-null for the four prefabs and `StudioPalette.Refresh` listing Tree, plus a render of the Front pane sampled at the Block's position to show non-art pixels there (review B1).

Aaron must play/look at:

- Whether the sizes feel right against Scuffy (block 0.74, button 0.66, tree 0.75 tall) — he said he'll adjust; the knobs are the PPU on the textures (all nine together to keep one scale) and the collider sizes.
- The boil at 4 fps for static objects (Scuffy's rate; may want slower for a tree).
- The pressed tint (0.6 grey) — visible enough?
- Tree collider: can you walk behind the top and past the fronds the right amount? Adjust the two boxes.
- Block drawing while pushing, climbing a Flap, partly under a Flap, and after an unfold onto the Back — the frames should clip and fold exactly like the old placeholder did.
- The Studio: icons, the Tree in the palette, plates no longer resizable, trees at the old spots on Sheet (0,0) (delete/move if unwanted).

## 9. Assumptions (engineering)

1. The two Trees are placed on `Sheet (0,0)` at the old map's tree centres. Aaron did not answer that half of question 3; the map was changed precisely so trees become elements, and two objects are trivial to delete or move in the Studio. **Escalated after review (Q2):** the placement waits for Aaron's answer; everything else is built first.
1a. Baseline (review N2): the working tree is ahead of HEAD — the Sheet Studio phase-3 code is uncommitted, `Map 1.png` is an uncommitted LFS swap, and `Sheet (0,0).prefab` has uncommitted edits (made in the Studio, matching Aaron's message) that remove two Wall instances named "Tree 1"/"Tree 2" at (0.4, 2.1)/(4.45, 2.95) and a Block at (−2.75, 1.15). The plan builds on that working-tree state and does not commit.
2. Collider sizes: Block 0.74 × 0.68 and Button 0.66 × 0.66 are the drawings' measured extents rounded up to 0.02 (Aaron: colliders shrink to match drawings). Tree boxes as in §5.7 — my reading of "slightly thinner and shorter".
3. Pressed tint 0.6 grey (tunable).
4. Leftover auto-slice data in the new metas is cleared (functionally inert either way).
5. Block drawings are clipped to the collider rect (as the placeholder was); a drawing larger than its collider would be cut, so the collider is sized to the drawing rather than the other way round.
6. The `Drawing` child's local x/y offset is honoured as the drawing anchor; it must be a direct, unrotated, unscaled child (else error, block disabled).
6a. "Go behind it a bit" (Aaron, Tree) is read as collision only: the collider is inset so the player can overlap the top and outer fronds; the player is still drawn over the tree, because with render-texture compositing every face sprite is part of the Sheet texture and the player is always on top. Drawing the canopy over the player would need an above-player pass that is also folded and clipped — a separate task. **Escalated after review (Q1).**
7. Tree children carry the regions (one box per `TerrainRegion`); the Tree root itself has no collider.

## 10. Open questions

Asked after the plan review (see the Review section, Q1/Q2). Neither changes the code built here; Q2 gates only the two Tree instances on Sheet (0,0).

## Review (plan-reviewer, round 1) — verdict BLOCK

| # | Finding | Disposition |
|---|---|---|
| B1 | `Drawing` child at z 0 is coplanar with the opaque Surface quad and behind the face art (−0.01): invisible in every editor view. | **Accepted.** `Drawing` child at local z −0.05 (§5.5); test asserts it; Studio-pane pixel check added (§8). |
| N1 | "Go behind it a bit" silently read as collision-only; drawing the canopy over the player is not possible with the compositor as is. | **Escalated (Q1)**; interpretation stated in §9.6a. Nothing built here changes with the answer. |
| N2 | Working tree is not HEAD: uncommitted Studio phase-3 code, map swap, and Sheet (0,0) edits removing "Tree 1"/"Tree 2" walls and a Block. | **Accepted** as a baseline note (§9.1a); Aaron confirms in Q2. No commit is made by this task. |
| N3 | (a) anchor from `drawing.localPosition` is wrong for a grandchild/root animator; (b) `shownSprite` only set in `LateUpdate`, so a redundant redraw follows every fold redraw. | **Accepted**, both (§6.3). |
| N4 | Nothing checks the mesh UVs place the drawing right. | **Accepted**: Play Mode eval compares a mesh vertex UV with `SpriteMapping.Uv`, flat and folded; capture with Scuffy beside the block (§8). |
| N5 | Block drawing is clipped to its collider rect, so the collider can never be inset from the picture. | **Accepted** as a stated consequence for the report; the seam (a separate draw rect into `VisiblePieces`) is noted, not built. |
| N6 | "painted on the paper" wording; stale `Resize` doc comment. | **Accepted.** |
| N7 | Tree gets no Studio name label (label only when the collider is on the element root); `IsResizable` keyed on draw mode silently changes when terrain gets Simple art. | **Accepted**: one label per element at its footprint top; consequence stated in the doc comment. |
| N8 | Tree drawing is ~3× smaller than the painted trees it replaces. | **Accepted** for the report; Aaron already chose the shared scale and said he will adjust. |

Questions for Aaron raised by the review:

- **Q1** — When the player overlaps the top of a tree, is it enough that the collider is inset and the player is drawn over the canopy (what is built), or should the canopy draw over the player (a new above-player pass, separate task)?
- **Q2** — Place the two Trees on Sheet (0,0) at the old painted-tree spots (0.40, 2.10) and (4.45, 2.91), or leave placement to you? And the uncommitted Studio edits to Sheet (0,0) (the "Tree 1"/"Tree 2" walls and the Block removed) are intended and stay?

No second review round: B1 is a one-value fix that does not change the design.

## Deviations (while coding)

1. **Sheet (0,0) placement of Trees not done yet** — waits for Q2 (see Review). Everything else in §4 is built.
2. **Sheet (0,0).prefab was clobbered by the editor and restored.** The verification pass began with `AssetDatabase.SaveAssets()` (the memory's "flush first" step). The editor's in-memory `Sheet (0,0)` was stale — HEAD-era content: the "Tree 1"/"Tree 2" walls and a Block still present (a Wall in the Block's place), and the *old* Map 1 sprite guid — and at 12:44:54 it was written over Aaron's newer on-disk working copy (trees/Block removed, new map guid; the 2-insertion/234-deletion diff seen at session start). Symptom: the Game view showed a blank sheet (art sprite null). Fix: rebuilt that working copy from `git show HEAD:` by removing the three prefab instances, their stripped transforms and their `m_AddedGameObjects` entries, and swapping the guid (1 insertion / 228 deletions vs HEAD — the original diff had 6 more deleted lines and one more inserted line I could not identify, presumed reserialisation noise), then `ImportAsset(ForceUpdate)`. The editor now loads it with the art resolved and no Tree/Block objects. The clobbered version is kept at the scratchpad `sheet00_clobbered.prefab` for Aaron. Lesson recorded in memory (`unity-cli-pipeline`): never SaveAssets while the working tree has uncommitted asset edits the editor may not have imported.
3. Pre-existing, not touched: the Hold Plate on Sheet (0,0) logs `RemoveObjectEffect … has no target` (no Gate linked); the Wall/Water placeholder sprites on Sheet (0,0) are invisible in play (41 terrain renderers, 1 visible — the Gate), presumably Aaron's Studio settings since the map art shows the terrain.

## Verification (mechanical, 2026-09-03)

- `recompile` → `up_to_date`, `errors: []`. New type `Papercut.SpriteMapping` and the `PushableBlock.drawing` field (type `SketchAnimator`) confirmed by eval.
- `run_tests {"mode":"editor"}` → **Total 250, Passed 250, Failed 0** (12 new tests: 7 `DrawingAssetsTests`, 5 `SpriteMappingTests`; `StudioPlacementTests` 12 including the rewritten resizable-set test and the new Tree test).
- `python Tools/check_links.py` → ALL LINKS OK (Tree.prefab: 12 objects, 6 external refs).
- Import: Tree frame 3 → sprite rect (0,0,512,512), PPU 487.6 (= 2000 · 512/2100), 512² texture, Clamp, 10 mips; a Box frame has 2 sub-assets (texture + one sprite). Prefabs Tree/Block/Hold Plate: 0 missing scripts. `StudioPalette.Refresh` lists Block, Gate, Hold Plate, Latch Plate, Tree, Wall, Water.
- Play Mode (Desk scene): a Block instantiated on Sheet (0,0) flat → `Drawing` renderer disabled (layer Default), `Visual` mesh renderer enabled, 4 verts, property-block texture = the animator's current frame texture, **worst UV error 0.00000** against `SpriteMapping.Uv`; UV range (0.148…0.852, 0.176…0.824) = the 0.74 × 0.68 rect inside the 1.05 canvas. Capture 1: Scuffy left, the crate at the sheet centre a little wider than Scuffy, the grey button at right. Then `TryCommit(EdgeEast, 3.0)` → committed, 2 layers; a Front block on the lifted strip rides under (invisible, parts 0 — correct); a Back-face block authored under it is exposed on the reflected flap layer: 1 part, layer 1, **UV error 0.00000**, texture = current frame. Capture 2: the flap landed on x ∈ [−0.5, 2.5], the crate drawn on it mirrored (its diagonal brace flipped), Scuffy unaffected. Console: only the pre-existing plate error. `editor_stop` → success.
- Edit mode face-camera probe (what the Studio panes show): a Block placed under Sheet (0,0)'s Front rendered through a SheetFront-layer camera → crate brown at its centre (0.42, 0.27, 0.18), dark outline at its edge, white beside it — the `Drawing` child at z −0.05 draws over the art (review B1).

## Code review (code-reviewer, round 1) — verdict REJECT

| # | Finding | Disposition |
|---|---|---|
| M1 | The mesh mapped UVs in flat (Front-space) coordinates whatever `side` is, so a Back-side block read mirrored against every other Back content and against the Studio's Back pane; a Front crate climbing a Flap flipped (Capture 2 showed it and the plan wrongly accepted it). | **Accepted.** `PushableBlock.DrawingUv` (pure, public static): for `side == Back` the anchor and the point go through `SheetGeometry.BackToFront`, i.e. the drawing is laid out in Back-space like a Back sprite and the layer's reflection carries it with the paper (Bible §6). Three tests in `SpriteMappingTests`. Re-verified in Play Mode (Verification round 2). Raised with Aaron as Q3 (paint-with-the-paper vs always-upright) since it is now a visible choice. |
| S1 | Plan §5.1 claimed the frames stay RGBA32; 512 × 512 is a multiple of 4, so they came in DXT5 (Scuffy's 366 × 512 is RGBA32 by accident of size). | **Accepted**: `textureCompression: 0` on all nine metas; re-import confirmed `512x512 RGBA32` (≈1.3 MB per frame with mips, ~12 MB for nine). Ink lines stay crisp; trivially reversible if memory ever matters. Noted for Aaron. |
| S2 | Tests pinned the exact collider sizes Aaron is told to tune. | **Accepted**: Block test asserts the rect holds the measured drawing and fits the canvas; plate test asserts a sane range. Doing so caught that 0.68 clipped frame 1's top by 0.009 — Block collider is now 0.74 × 0.70. |
| S3 | Tree inset assertions too loose. | **Accepted**: each box is now checked against the drawn extents (tip +0.40, half-width 0.30, bottom −0.40). |
| S4 | "tiles"/"tiled" wording in `StudioPlacement` doc comments. | **Accepted**: worded as Unity's Tiled/Sliced draw mode. |

Questions for Aaron raised by the code review:

- **Q3** — A block on a landed Flap (climbed on, or authored on the Back and exposed) is now painted *with the paper*: laid out in the space of the face it is on and carried by the fold's reflection, exactly like a plate or tree authored on the Back. Concretely: on an East/West Flap it reads upright, on a North/South Flap it reads **upside-down**, on a corner Flap it is turned — so a crate pushed onto a North Flap is drawn upside-down, as any Back-authored drawing on that Flap would be. The alternative is a block that always reads upright as an object sitting on top of the paper (a block-only mapping, which would then differ from how the same block looks authored on the Back). Built: paint-with-the-paper (recommended; it is what Bible §4/§6 say about content of the sheet). Corrected after code review round 2 (its S1).
- **Q4** — The nine frames are now imported uncompressed (RGBA32) like Scuffy's; fine to keep, or compress?

## Verification round 2 (after the code-review fixes)

- `recompile` → completed, no errors. `run_tests editor` → **Total 253, Passed 253, Failed 0** (three new `BlockDrawing_*` tests; the relationship tests for Block/plates/Tree).
- Import: Box frame 1 → `512x512 RGBA32`; Block collider (0.74, 0.70).
- Play Mode: same sequence as round 1. Front block on the lifted strip: invisible (rides under). Back-face block exposed on the reflected flap layer: 1 part, layer 1, **worst UV error 0.00000 against `DrawingUv`** (Back-space mapping). Capture 3: the crate on the landed flap now reads with its brace running the same way as a Front crate (compare Capture 1). Console: only the pre-existing plate error. `editor_stop` → success. Sheet (0,0).prefab on disk unchanged since the restore (12:52:56), 39 terrain regions (the two "Tree" walls gone).

## Code review (round 2) — verdict APPROVE WITH FIXES (no must-fix)

| # | Finding | Disposition |
|---|---|---|
| S1 | Q3's wording understated paint-with-the-paper: on a North/South Flap a climbed crate reads upside-down (y-mirror crease ∘ x-mirror Back-space = 180°), not upright; only East/West folds were probed. | **Accepted**: Q3 corrected; class remarks say so; North-fold Play Mode probe added (Verification round 3). |
| S2 | `StudioPlacement.Place` doc comment stale (transparent Surface, Block child). | **Accepted**, reworded. |
| S3 | Plan §5.5 still said 0.68 / ±0.34. | **Accepted**, fixed. |
| S4 | A `Drawing` x/y offset would be applied in the current side's space, unlike the child's real transform. | **Accepted**: the offset is gone — the Drawing child must sit at the block's centre (checked in Awake, error + disabled otherwise); `DrawingUv` lost the offset parameter and its test. |
| S5 | Test pinned `releasedColor == white`, a tunable. | **Accepted**, only "pressed darker than released" remains. |
| S6 | `BoxDrawn.yMax` equals the collider's top bit-exactly. | **Accepted**, commented as intentional. |

## Verification round 3 (after the round-2 fixes)

- `recompile` → completed, no errors. `run_tests editor` → **Total 252, Passed 252, Failed 0** (one test removed with the offset feature).
- Play Mode: `TryCommit(EdgeNorth, 2.0)` committed (2 layers); a Back-face block authored under Front (1.0, 3.2) lands on the flap at (1.0, 1.3): 1 part, layer 1, **UV error 0.00000 against `DrawingUv`**, texture = the current frame. Capture 4: the crate on the North flap reads upside-down (the 180° case Q3 now describes). Console: only the pre-existing plate error. `editor_stop` → success. Sheet (0,0).prefab on disk unchanged (12:52:56).
