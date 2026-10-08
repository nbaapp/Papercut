# Paper shading over the player and objects

## 1. Task

Aaron, 2026-10-07: *"I don't think the creases, or the paper wrinkles for that matter, are rendering over the player or any other real time object like boxes. They live on the paper too, so is there a way you can have that happen?"*

The paper's shading (the grain normal map and the crease normal maps) must darken and lighten everything the main camera draws over the sheet. That means the player, blocks and paperweights, and the universal (Above) content too (Aaron, below). Today it shades only the composited face textures.

**Out of scope:**
- Changing the shading itself (its maths, tunables or light). It moves; it does not change.
- The Sheet Studio panes, which never had grain or creases. Unchanged.
- The Seam drag border. See the assumptions.

## 2. Design references

- Bible §4 *Rendering* — Paper grain [2026-09-21]: Aaron chose shading in the fold renderer's face material over an overlay quad per face. The note recorded the consequence: *"Over everything on the face, but not over the player or blocks, which the main camera composites on top."* **This task reverses that consequence on Aaron's request.** The grain stays a shader on the composited sheet, lit in Desk space, rather than becoming a per-face overlay quad. It now runs as a second pass over the same composited pieces instead of inside the face pass.
- Bible §4 *Crease normal maps [2026-10-07]*: the creases shade with the grain and move with it.
- Bible §3 *Universal and filter regions*: Above content sits above the paper conceptually. Aaron still wants it shaded (see §3 below).
- Bible §5 / PushableBlock: a block's drawing is a mesh on the Default layer at the surface depth of the layer it sits on (`IFoldRenderer.SurfaceZ`). A Flap that lands on part of a block draws over that part. The player is a SpriteRenderer on the Default layer. Everything is at sorting layer Default, order 0. The URP 2D Renderer sorts transparents by sorting layer, then order, then distance (orthographic z).
- Design Doc *Art*: *"Everything should feel like it exists on and in the paper ... The elements on the page will follow the wrinkles on the paper."*
- Bible §1: the WebGL target is [TENTATIVE]. Nothing here reads back. The cost is one more mesh pass over the visible sheet.
- No `[DECIDE]` item is touched. Rendering #2 (render-texture compositing) is unchanged.

## 3. Decisions already made by Aaron (this task, 2026-10-07)

1. **Full coding workflow:** yes.
2. **Universal walls (Above content) are shaded too: "Yes, shade them too".** Everything the main camera draws over the sheet's area gets the paper's shading.

## 4. Files

| File | Change |
|---|---|
| `Assets/Papercut/Shaders/Paper Face.shader` | Back to texture × tint only. The grain and crease code moves out. |
| `Assets/Papercut/Shaders/Paper Shading.shader` | **New.** The grain and crease shading, written as a multiplicative overlay. |
| `Assets/Papercut/Materials/Paper Shading.mat` | **New.** Created through the editor (`AssetDatabase.CreateAsset`). |
| `Assets/Papercut/Scripts/Fold/SheetLayers.cs` | Adds `VisiblePieces(int index)`: a layer's piece minus every layer above it. |
| `Assets/Papercut/Scripts/Fold/RenderTextureFoldRenderer.cs` | One shading part per layer, plus the `shadingMaterial` field and the sorting orders. The block binding is split into face (texture, tint) and shading (grain, crease). |
| `Assets/Papercut/Prefabs/Sheet.prefab` | Assigns `shadingMaterial` (through the editor). |
| `Assets/Papercut/Tests/EditMode/SheetLayersTests.cs` | Tests for `VisiblePieces`. |
| `Assets/Papercut/Tests/EditMode/CreaseShadingTests.cs` | The shader tests point at `Papercut/Paper Shading`, and check that Paper Face still has its two properties. |
| `Documents/Claude Bible.md` | §4 Rendering: the shading is now an overlay pass over everything on the sheet. Adds a sorting-order rule. |

## 5. Components & data

### `SheetLayers.VisiblePieces(int index)` (new, public, pure)

Returns `IReadOnlyList<ConvexPolygon>`: layer `index`'s `Desk` polygon minus the `Desk` of every layer above it, as disjoint convex pieces (`ConvexPolygon.Subtract`, as `CoverageAbove` already does). It returns an empty list when the layer is wholly covered. Across all layers, the pieces tile the sheet's footprint with no overlaps. It throws `ArgumentOutOfRangeException` for a bad index.

### `Papercut/Paper Face` (changed)

It samples `_BaseMap` and multiplies by `_BaseColor`. That is all. The Properties block keeps only `_BaseMap` and `_BaseColor`. The `Paper Face.mat` asset keeps any stale serialized grain values, which are harmless. `#pragma target 2.0` again.

### `Papercut/Paper Shading` (new)

Its properties and body are exactly what Paper Face has today: grain, frame, light, sheet size, the crease maps and uniforms, the `MAX_CREASES` arrays, `CreaseNormal`, and the `lit / flat` ratio.

Output is `ratio × 0.5` in rgb, with `Blend DstColor SrcColor, Zero One`. The framebuffer's colour becomes `dst × ratio`, and its alpha is untouched.
- The ratio is clamped to [0, 2], the most this blend can express.
- At the current defaults the ratio stays within about [0.65, 1.1].
- Lit-side values above 2 only happen with `grainAmbient` near 0 and a grazing light. There they are capped at 2 (an assumption, §9).

Render settings: `ZWrite Off`, `Cull Off`. The tags are "Transparent".

### `RenderTextureFoldRenderer` (changed)

**New serialized field:**
- `shadingMaterial` (Material): Papercut/Paper Shading. It is drawn over the sheet after everything on it.

A missing `shadingMaterial` is logged as an error, like the face material. The sheet then draws unshaded, and the error says so.

**New engineering consts** (not tunables):
- `ShadingSortingOrder = 1`: the shading parts draw after every Default-layer, order-0 renderer: the sheet's layers, the player, blocks, Above content and neighbouring sheets' layers.
- `SeamSortingOrder = 2`: the Seam border draws over the shading.

A sorting order is not a feel value.

**Shading parts:** a pooled `MeshPart` per layer, on the sheet's own GameObject layer (the main camera's). Each layer part also gets `renderer.sortingOrder`; the layer parts stay at 0. In `BuildMeshes`, for layer k:
- The layer part is built as today and bound with texture and tint only.
- The shading part's mesh is `layers.VisiblePieces(k)`, with the same uv function as the layer: `UvOf(frontUp ? original : BackToFront(original))` through the layer's inverse. It is bound with grain, frame and creases. The grain map and the crease arrays are chosen by the layer's `FrontUp`, as before.
- Unused shading parts (k ≥ layer count) get an empty mesh.

**Binding:**
- `BindLayer` splits into `BindFace(part)` (texture and tint) and `BindShading(part)` (everything else that `BindLayer` sets today).
- `LateUpdate`'s live rebind on a `ShadingSettings` change calls `BindShading` on every shading part.

**Inspector tunables:** none added, removed or renamed. The Paper grain and Crease headers keep every value and default. They now drive the shading pass.

## 6. Behaviour

**Each frame, per sheet in view, in draw order:**
1. Order 0 draws, sorted by z: the Desk, the sheet's layer meshes (face texture × tint, unshaded), the blocks at their surface depths, the player, and Above content.
2. Order 1 draws the shading parts. Every pixel of the sheet's visible footprint is multiplied by the shading of the piece that is topmost there. Because the visible pieces are disjoint, no pixel is shaded twice. A Flap's pixels get the Flap face's grain and creases, and the Base under it gets none (it is hidden anyway).
3. Order 2 draws the Seam border, unshaded, while dragging.

**A partially transparent preview Flap** (tint alpha 0.85): the pixel is 85 % Flap and 15 % Base underneath, and it is shaded by the Flap's shading only. This is invisible at 0.85. The Retreating and red-refusal tints are unchanged.

**Equivalence for the sheet itself:** today the face shader does `colour × ratio` before alpha blending. Now the blend does `(blended colour) × ratio` after it. For an opaque piece (alpha 1 after the tint) the two are identical. So with no player or block on screen, the sheet looks the same as before, apart from the ratio clamp at 2 and the preview-alpha case above.

**Universal walls** (Above, order 0) are shaded (Aaron). They are clipped to the sheet's footprint already, so all of their pixels lie under a shading part.

**Neighbouring sheets** do not overlap, so each sheet's shading touches only its own area.

**Failure modes:**
- Shading material missing: an error log, and the sheet is drawn unshaded.
- Face material missing: the existing error.
- Too many layers: the existing error. The shading parts need no z room (they sort by order).
- In the Studio: the panes use the face material through their own meshes and never create shading parts, so they are unshaded. Paper Face no longer shades at all, so the Studio's look is unchanged (it was flat already).

## 7. Interfaces & seams

All of this stays inside `RenderTextureFoldRenderer`, behind `IFoldRenderer`. No other component learns about shading. The player and blocks need no change.

To cut it: clear `shadingMaterial`, which leaves no shading anywhere and logs an error. Or set the strengths to 0. To get the old "face only" look you would need ordering changes, which is not a goal.

New authoring rule (for the Bible): **anything drawn by the main camera that should lie on the paper must stay at sorting order ≤ 0 on the Default sorting layer.** Order ≥ 1 draws over the paper's shading, unshaded.

## 8. Testing

**Edit Mode tests:**
- `SheetLayersTests.VisiblePieces_*`:
  - flat: one piece, the whole sheet;
  - edge fold: the Base's visible area is the sheet minus the landed Flap, and the Flap's is the whole Flap;
  - fold-on-fold: the pieces tile the footprint (summed areas equal the footprint's union area) with no pairwise overlap;
  - a covered layer gives an empty list;
  - a bad index throws.
- `CreaseShadingTests`:
  - the shader-property test targets `Papercut/Paper Shading` (all shading names), with no compile errors;
  - a new test checks `Papercut/Paper Face` has `_BaseMap` and `_BaseColor` and no compile errors;
  - the `MAX_CREASES` define test reads `Paper Shading.shader`.
- `RenderTextureFoldRenderer.FaceShaderProperties` is renamed `ShadingShaderProperties` (minus `_BaseMap` / `_BaseColor`).

**Mechanical checks through the Unity CLI** (no Play Mode):
1. Recompile clean.
2. `ShaderHasError` false, with no messages, for both shaders.
3. `Paper Shading.mat` exists and uses the shader.
4. The Sheet prefab, loaded through the editor, has `shadingMaterial` assigned; the disk diff touches only that field.
5. The collision check, then `run_tests` (editor).

**What Aaron needs to play:**
- The player, a block and a paperweight standing on a crease and on the grain: the shading should cross them.
- The same on a landed Flap.
- A universal wall's collision fill, which is shaded now.
- That the sheet alone looks as before.
- The Seam border while dragging, unshaded.
- Any visible edge where a shading part meets a sheet edge (there should be none).
- A drag preview over the player's area. It can't cover the player, but check near it.

## 9. Assumptions (engineering)

- **The Seam border stays unshaded** (order 2). It is a drag affordance that "marks a position" (Bible §4), not paper.
- **The shading ratio is clamped to 2.** It never reaches that at the current or nearby tunings.
- **The 2D Renderer sorts a MeshRenderer by `sortingOrder` like a SpriteRenderer.** This is standard URP 2D behaviour, and it is checked by Aaron's playtest, since an edit-mode check can't see draw order.
- **No other main-camera content over a sheet uses sorting order ≥ 1 today.** A grep of prefabs, scenes and scripts found none.

## 10. Open questions

None.

## Review

Plan review, round 1 (`plan-reviewer`): APPROVE WITH CHANGES, nothing blocking. Every finding was accepted, and the design is revised as below. **The revision supersedes §4–§9 wherever they disagree.**

- **N1** (subtracted visible pieces leave T-junctions: cracks or double shading along the Seam and creases under a multiplicative blend): **accepted.**
  - `VisiblePieces` is dropped.
  - Each shading part **shares its layer part's mesh**, as a child GameObject of the layer part with a MeshFilter on the same `sharedMesh`, so its coverage is the layer's exactly.
  - Shading parts draw **topmost layer first**: `sortingOrder = ShadingSortingOrder + (layerCount − 1 − k)`, with `ShadingSortingOrder = 1`.
  - A stencil bit stops lower layers shading a pixel a higher layer already shaded: `Stencil { Ref 128 ReadMask 128 WriteMask 128 Comp NotEqual Pass Replace }`. Renderer2D has `m_UseDepthStencilBuffer: 1`, and the camera clears stencil each frame. Sheets don't overlap, so one bit serves them all. Bit 128 was picked to stay clear of low-bit stencil users (sprite masks, shallow UI masks); none exist over the sheet today.
- **N2** (the Seam at order 2 would draw over the player): **accepted.** The Seam stays at order 0, sorted by z as today, so the player draws over it. It is now shaded, which is invisible on a dark 0.9-alpha line. `SeamSortingOrder` is dropped.
- **N3** (a null shading material): **accepted.** If `shadingMaterial` is null, no shading child is created. The error is logged once, and the sheet draws unshaded.
- **N4** (lifecycle): **accepted.** It follows from N1: the shading child lives and dies with its layer part (destroying the layer GameObject destroys the child, and the shared mesh is destroyed once).
- **N5** (duplicated uv mapping): **accepted.** It follows from N1: the meshes are shared, so the uvs are identical by construction. `GrainFrameOf` stays the one source for the frame.
- **N6** (play list): **accepted.** Added: the player standing at a sheet edge (shading stops mid-sprite, and the part over the Desk is unshaded); the player beside a landed Flap (the overhanging part of the sprite takes the Flap's shading); a fold-on-fold drag (look for any cracks or seams in the shading).
- **N7** (vocabulary): **accepted.** The new shader is `Papercut/Sheet Shading`, its file `Sheet Shading.shader` and its material `Sheet Shading.mat`. Paper Face keeps its existing name.
- **N8** (docs): **accepted.** The Bible's *Crease normal maps* note and `CreaseShading.cs`'s summary now say Sheet Shading.

Revised tests:
- The `VisiblePieces` tests are dropped.
- The shader tests: Sheet Shading has every shading property and its `MAX_CREASES` matches; Paper Face has `_BaseMap` and `_BaseColor`; neither has errors.
- New: a test that Sheet Shading's pass declares the stencil block (read from the shader source), since its absence would show up only as double shading.

## Deviations

- The Studio is unaffected as planned: its panes use `faceMaterial` (now unshaded Paper Face, which was already flat there).
- `Paper Face.mat` keeps stale serialized grain values from the old shader. They are unused and harmless.

## Verification (step 6)

- Recompile clean.
- `Papercut/Paper Face` and `Papercut/Sheet Shading` force-imported: `ShaderHasError` False, no messages.
- `Sheet Shading.mat` created (guid 904df3d8…) with the Sheet Shading shader.
- Sheet.prefab, loaded through the editor: `shadingMaterial` assigned. The disk diff against a backup is that one added line.
- `run_tests` (editor): 567 total, 564 passed, 3 failed (the pre-existing PowerCroissant ×2 and StudioLinks undo). The four shader tests passed. No assets changed during the run.

## Code review

Round 1 (`code-reviewer`): APPROVE WITH FIXES, nothing must-fix. All fixes were trivial, so no second round.

- **S1** (`ShadingSortingOrder` comment said "bottom" when it is the topmost layer's order): **fixed.** The comment is corrected, and the formula is now `ShadingOrderOf(layerIndex, layerCount)`.
- **S2** (the stencil test didn't pin the bit, and the draw order was untested): **fixed.** The test asserts `Ref`/`ReadMask`/`WriteMask 128`. A new `ShadingOrder_TopmostLayerFirst_AllAboveOrderZero` test covers the order.
- **S3** (Bible above/below references, and the "Cutting it" sentence): **fixed.** The directions are corrected, and the cut sentence now separates grain from all shading.
- **S4** (stale `ShadingSettings` summary and `frontGrain` tooltip): **fixed.**
- **Q1** (a sprite hanging off the paper is shaded only up to the paper's outline): **reported to Aaron** as current behaviour; any change needs its own design.

After the fixes: recompile clean, and `run_tests` (editor) gave 568 total, 565 passed, 3 failed (the same pre-existing three). All shader and order tests passed. No assets changed.
