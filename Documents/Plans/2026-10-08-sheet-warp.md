# Sheet warp: everything on the sheet follows the wrinkles

## 1. Task

Aaron, 2026-10-08, after the shading pass (`2026-10-07-paper-shading-over-objects.md`) reached the player: *"it doesn't look quit right since the shape/edges of the player aren't distorting like they would if it was actually a 3d wrinkled piece of paper as looked from above, its just changing the color ... its looking at them through a dirty window."*

He was offered three options: (A) a screen-space warp, (B) per-object warp shaders, (C) stronger colour only. Answer: **"Yeah do A"**.

In practice, the Sheet Shading pass stops multiplying over what is already drawn. Instead it re-draws each pixel of the sheet's visible footprint from a copy of the screen, taken just before the pass, at an offset driven by the paper's slope there, times the existing shading ratio. Everything drawn on the sheet bends with the creases (and the grain, if tuned): the faces' drawn content, the player, blocks, paperweights, universal walls and the Seam.

**Out of scope:** the shading maths (unchanged), the crease and grain maps, the Sheet Studio (it has no shading pass), and real 3D geometry.

## 2. Design references

- Design Doc *Art*: *"Everything should feel like it exists on and in the paper ... The elements on the page will follow the wrinkles on the paper."* This task is that sentence, for displacement.
- Bible §4 *Rendering*: render-texture compositing (decision #2) is unchanged. Paper grain (2026-09-21), crease normal maps (2026-10-07), and the 2026-10-07 revision: the shading is the separate `Sheet Shading` pass over everything on the sheet, including universal walls (Aaron). Its authoring rule says main-camera content on the paper stays at sorting order ≤ 0 on Default.
- Bible §1: the WebGL target is [TENTATIVE], and *"expensive render-texture readbacks"* are WebGL-hostile. **Flagged:** this adds one full-screen GPU copy per camera per frame (the 2D renderer's Camera Sorting Layer Texture). That is a GPU-side blit, not a CPU readback. WebGL2 supports it, but it is a real per-frame cost.
- No `[DECIDE]` items are touched.

## 3. Decisions already made by Aaron

1. **Option A** (screen-space warp), 2026-10-08.
2. **Full coding workflow.** I recommended it ("I'd recommend the full workflow again"), and Aaron answered "Yeah do A".
3. **Everything on the sheet gets the effect, including universal walls** (2026-10-07, carried over). **The paper's own drawn art warps too.** I raised this as a consequence of A before he chose it.

## 4. Files

| File | Change |
|---|---|
| `ProjectSettings/TagManager.asset` | Adds the sorting layer **`Sheet Shading`** after Default (through the editor: SerializedObject on the TagManager). |
| `Assets/Settings/Renderer2D.asset` | `m_UseCameraSortingLayersTexture: 1`, `m_CameraSortingLayersTextureBound: 0` (Default's id), `m_CameraSortingLayerDownsamplingMethod: 0` (None: full resolution, point-filtered copy). Set through the editor. |
| `Assets/Papercut/Shaders/Sheet Shading.shader` | Samples `_CameraSortingLayerTexture` at the warped screen position and writes `colour × ratio` (Blend One Zero). It keeps the stencil, adds the warp and the outline fade. |
| `Assets/Papercut/Scripts/Fold/RenderTextureFoldRenderer.cs` | Shading parts go on the `Sheet Shading` sorting layer, with two new warp tunables. The sheet's outline segments are packed for the shader. A check for the sorting layer and the renderer setting reports an error when they are missing. |
| `Assets/Papercut/Scripts/Fold/SheetWarp.cs` | **New**, pure: packs the outline segments (every layer polygon's edges, sheet-local) into a fixed-size array, plus the C# reference for the shader's warp maths (offset, fade). |
| `Assets/Papercut/Tests/EditMode/SheetWarpTests.cs` | **New**: packing, fade, offset reference, and a shader-source check. |
| `Assets/Papercut/Tests/EditMode/CreaseShadingTests.cs` | The stencil and blend source test updated for `Blend One Zero`. |
| `Documents/Claude Bible.md` | §4 Rendering: the warp, the new sorting layer and renderer setting, the WebGL cost, and an updated authoring rule. |

## 5. Components & data

### Sorting layer and renderer setting

- **`Sheet Shading` sorting layer**, after Default. Only the shading parts use it.
- **Camera Sorting Layer Texture bound = Default.** The 2D renderer copies the camera colour into `_CameraSortingLayerTexture` after the Default batch, so the copy holds everything on Default: the Desk, the sheets' faces, the player, blocks, Above content and the Seam. The `Sheet Shading` batch then samples it.
- Every camera using Renderer2D pays this copy, including each sheet's two small face cameras. Their copies are unused but cheap (one blit of a 1408×1088 RT). This is noted in the Bible.

### `Papercut/Sheet Shading` (changed)

- **Blend:** `Blend One Zero`. It replaces the pixel with the warped sample × ratio, with alpha 1. The stencil block is unchanged: topmost part first, one write per pixel.
- **Vertex:** passes `faceUV` (as now), `sheetPos` (object-space xy; the mesh is in sheet-local units under the sheet transform), and the clip position.
- **Fragment:**
  1. `grainN = UnpackNormalScale(grain, _GrainStrength)` and `creaseXY = CreaseNormal(facePoint)`, both as now.
  2. The face → Desk frame (`_GrainFrame`) carries each into sheet-local space separately: `grainDesk.xy`, `creaseDesk.xy`.
  3. Shading normal and `ratio`: unchanged maths (with the clamp to [0, 2] removed, since Blend One Zero has no ceiling).
  4. **Warp offset (sheet units):** `offset = −(_GrainWarp · grainDesk.xy + _CreaseWarp · creaseDesk.xy)`. The sign makes content slide downhill off a ridge, and a negative tunable reverses it.
  5. **Outline fade:** `d` = the distance from `sheetPos` to the nearest packed outline segment. `offset *= saturate(d / max(|offset|, ε))`. In other words the offset is clamped to length ≤ d, so the sample point never crosses an outline. That keeps a piece from pulling the Desk, a neighbouring piece, or a Flap's pixels across its edge.
  6. `warpedClip = TransformObjectToHClip(float3(sheetPos + offset, posOS.z))`, `uv = ComputeScreenPos(warpedClip).xy / w`. Sample `_CameraSortingLayerTexture` with `sampler_LinearClamp`. With a zero offset, uv is the pixel centre, so the copy reads back exactly.
  7. `return half4(sample.rgb × ratio, 1)`.
- **New properties** (`[HideInInspector]`, in the cbuffer): `_GrainWarp`, `_CreaseWarp`, `_OutlineCount`.
- **New array** (outside the cbuffer): `float4 _Outline[MAX_OUTLINE]` (segment a.xy, b.xy, sheet-local), with `#define MAX_OUTLINE 64`.

### `SheetWarp` (new, static, pure)

- `public const int MaxOutlineSegments = 64;` A test checks it matches the shader's define.
- `public static int PackOutline(SheetLayers layers, Vector4[] segments, out bool truncated)`: every edge of every layer's `Desk` polygon, bottom to top, skipping zero-length edges. On overflow it returns −1 and sets `truncated`. **The renderer then sets both warps to 0 for that sheet** (no warp, and no bleed), with a once-per-renderer warning in editor and development builds only.
- `public static Vector2 FadedOffset(Vector2 p, Vector2 offset, Vector4[] segments, int count)`: the C# reference for fragment steps 5 and 6, which the shader mirrors.
- `public static float DistanceToOutline(Vector2 p, Vector4[] segments, int count)`.

### `RenderTextureFoldRenderer` (changed)

**New Inspector tunables** under a new `[Header("Warp")]`. Both are read live, as part of `ShadingSettings`:

| Field | Type | Default | Tooltip gist |
|---|---|---|---|
| `creaseWarp` | `float [Range(-0.25, 0.25)]` | 0.03 | How far a crease's slope moves what is drawn on the sheet, in sheet units at full slope. 0 = none; negative reverses the direction. |
| `grainWarp` | `float [Range(-0.25, 0.25)]` | 0 | The same for the grain. Off by default: real paper grain doesn't visibly move ink, and a strong value reads as rippled glass. |

**New engineering consts:** `ShadingSortingLayerName = "Sheet Shading"`.

**Behaviour:**
- `OnEnable`: resolve `SortingLayer.NameToID(ShadingSortingLayerName)`. If the layer doesn't exist (`!SortingLayer.IsValid(id)`), log an error once. The shading parts stay on Default, so the pass can't sample a valid copy. In that case the warp and shading are off: no shading children are created (as when the material is missing).
- `AddShading` sets `sortingLayerID` on the child. `SetShadingOrder` is unchanged (topmost first).
- `BuildMeshes`: pack the outline for the displayed stack. `BindShading` sets `_Outline`, `_OutlineCount` (as a float), `_GrainWarp` and `_CreaseWarp` (both 0 if the outline overflowed).
- Whether the renderer setting is on can't be read from runtime code (`Renderer2DData`'s field is internal). The editor test (§8) checks the asset instead. If it were off, `_CameraSortingLayerTexture` would be unbound and the sheet would draw black or grey. That's loud, not silent.

## 6. Behaviour

**Each frame:** Default draws everything as now (unshaded). The 2D renderer copies the screen. The `Sheet Shading` batch draws each sheet's shading parts, topmost first, under the stencil. Each pixel of the visible footprint becomes the copy sampled at the warped point × the shading ratio.

**At a remembered crease (mid-piece):** lines crossing it are pushed apart or together across the crease band, so they kink. On the player and blocks it is the same kink.

**At a live fold's folded edge, a Seam, or the sheet's edge:** the offset fades to 0 at the outline, so edges stay crisp and nothing bleeds across. The kink shows inside the band, away from the very edge.

**Off-sheet parts of a sprite:** untouched, as now.

**Performance:**
- One extra full-screen copy per camera per frame.
- Per shading fragment: the crease loop (as now), plus a loop over up to 64 segments and one sample.

**Failure modes:**
- Sorting layer missing: an error, and no shading.
- Shading material missing: an error, as now.
- Renderer setting off: a visibly broken sheet, plus a failing editor test.
- Outline overflow: the warp is off for that sheet, with a dev warning.
- Face cameras: they render their own layers only. The shading parts are on the main camera's layer, so face cameras never draw them.

## 7. Interfaces & seams

All of this stays inside `RenderTextureFoldRenderer`, its shader and the project's renderer setting. The player, blocks and fold model are untouched.

To cut the warp: set both warps to 0, which keeps today's look, though the copy still runs. To remove it entirely: revert the shader to multiply, turn off the renderer setting, and delete the sorting layer.

Updated authoring rule: main-camera content on the paper must stay on the **Default** sorting layer (any order). The `Sheet Shading` layer is reserved for the shading parts. Anything on a later layer draws over the sheet, unwarped and unshaded.

## 8. Testing

**Edit Mode tests:**
- `SheetWarpTests`:
  - `PackOutline`: flat sheet → 4 edges; an edge fold → the Base's and the Flap's edges; overflow → −1 and truncated; short arrays throw.
  - `DistanceToOutline` on known points.
  - `FadedOffset`: zero at the outline; full far from it; the sampled point never crosses the nearest segment (property check over a grid of points and offsets).
- Shader source checks:
  - Sheet Shading declares `Blend One Zero`, the stencil (Ref, ReadMask and WriteMask 128, Comp NotEqual, Pass Replace), and samples `_CameraSortingLayerTexture`;
  - its `MAX_OUTLINE` equals `SheetWarp.MaxOutlineSegments`;
  - it has every property the renderer sets (the property list is extended).
- **Project settings check:**
  - the `Sheet Shading` sorting layer exists and sits after Default (`SortingLayer.layers` order);
  - Renderer2D.asset has `m_UseCameraSortingLayersTexture: 1` with the bound on Default (read through a SerializedObject on the asset).

**Mechanical checks through the Unity CLI** (no Play Mode):
1. Recompile clean.
2. The shader force-imported, with no errors.
3. The TagManager and Renderer2D diffs show only the intended fields.
4. The collision check, then `run_tests`.

**What Aaron needs to play:**
- A remembered crease under Scuffy, a block and the drawn map: lines should kink.
- `creaseWarp` tuning, and the sign.
- `grainWarp` (try a little).
- Live fold edges, Seams and the sheet edge: crisp, with no smear.
- Fold-on-fold.
- Performance feel.
- That a sheet with no creases looks exactly as before (warps 0 → identical).

## 9. Assumptions (engineering)

- **The copy is full resolution and point-filtered** (Downsampling None). Sampling at the pixel centre with zero offset reproduces the pixel exactly, so a flat sheet looks unchanged. Warped samples go through `sampler_LinearClamp`.
- **The offset is clamped to the distance to the nearest layer outline**, over all layers (conservative). Near any outline, including a lower layer's edge hidden under a Flap, the warp shrinks. The reason: the shader can't cheaply know who owns the pixel it samples.
- **No ratio clamp.** The 2× clamp existed only for the multiply blend.
- **The 64-segment cap.** A flat sheet uses 4 segments; each fold adds roughly 4–8. Overflow disables the warp on that sheet, never the shading.
- **The offset direction** (downhill) is a guess at what reads as paper. The sign is tunable.

## 10. Open questions

None.

## Review

Plan review, round 1 (`plan-reviewer`): BLOCK.

- **B1** (the `grainWarp` default 0 overrides what Aaron asked for: "the paper wrinkles"): **escalated to Aaron (Q2).**
- **B2** (warping universal walls was extended from a shading-only answer): **escalated to Aaron (Q1).** `§3 item 3` is withdrawn pending his answer.
- **N1** (the crease warp default is too small and tied to shading strength): **accepted.** The warp uses the *unscaled* map slope × fade × its own values, independent of `creaseStrength`. Remembered creases get their own `rememberedCreaseWarp` multiplier (0–1, default 1). The defaults are raised; final numbers are set after Q2.
- **N2** (inside and outside maps are opposite reliefs, so one sign can't suit both): **accepted.** `creaseInsideWarp` and `creaseOutsideWarp` are separate signed values. Play item: compare a Front crease with a Back crease.
- **N3** (`NameToID` of an unknown name returns 0, so the guard can't fire): **accepted.** The check scans `SortingLayer.layers` for the name and requires a non-zero id. The layer is added through the TagManager's SerializedObject with a fresh random non-zero `uniqueID`. A test asserts it is unique, non-zero, and after Default.
- **N4** (per-part outlines; the overflow estimate was optimistic): **accepted.** Layer k's part gets only the edges of layers k..top, with coincident duplicates removed. `MaxOutlineSegments` = 128. Overflow turns the warp off for that part only, expected under heavy stacking, with a once-per-renderer dev warning.
- **N5** (linear filtering reaches one pixel across the outline): **accepted.** The clamp is `max(d − pixel, 0)`, with `pixel = length(fwidth(sheetPos))`.
- **N6** (runtime-only path): **accepted** into the play list: whole sheet not flipped or shifted; no trail behind Scuffy; neighbour sheets and the slide; the drag preview Flap and red tint; the unfold swing. **Q3** (a one-off probe) is escalated. The `ShadingOrder` test's message is updated (the sorting layer now carries "after Default").
- **N7** (a second renderer for the face cameras): **rejected for now.** It adds a pipeline-asset renderer and per-camera wiring to save a small blit. Noted in the Bible as a known cost, to revisit if profiling shows it.
- **N8** (vocabulary in strings): **accepted.** "grain" / "sheet" in new strings.

## Post-review answers (Aaron, 2026-10-08)

- **Q2, grain warp: "Both, grain weaker".** The grain bends things too, more faintly than creases. Both are tunable.
- **Q1, universal walls: "Warp like the rest".** Above content warps with everything on the sheet (there is no wall art yet; revisit when there is). This replaces the withdrawn §3 item 3.
- **Q3, play probe: "No, I'll playtest".** No Play Mode. Every runtime-only check goes on Aaron's list.

## Revised design (supersedes §5 and §9 where they differ)

**Warp tunables** (`[Header("Warp")]` on `RenderTextureFoldRenderer`, read live through `ShadingSettings`):

| Field | Type | Default | Meaning |
|---|---|---|---|
| `creaseInsideWarp` | float, Range(−0.5, 0.5) | 0.15 | Sheet units that content moves per unit of the inside map's slope (the slope as authored, not scaled by shading strength). Negative reverses. |
| `creaseOutsideWarp` | float, Range(−0.5, 0.5) | 0.15 | The same for the outside map. Kept separate because the two maps are opposite reliefs. |
| `rememberedCreaseWarp` | float, Range(0, 1) | 1 | Multiplier on the warp of a crease left by an undone fold. |
| `grainWarp` | float, Range(−0.5, 0.5) | 0.03 | The same for the grain's slope (the map as authored, not scaled by `grainStrength`). Weaker than creases (Aaron). |

At the measured crease peak slope (~0.4) the crease default moves content about 0.06 sheet units, roughly 7 px at Screen framing.

**Shader:**
- `CreaseNormal` returns the strongest crease's shading offset (as now) **and** its warp offset: the unscaled map slope × fade × the map's warp × (`rememberedCreaseWarp` if remembered), in face space. Both go through `_GrainFrame` to Desk space.
- The grain's warp uses `UnpackNormal` (unscaled) × `grainWarp`.
- The total warp offset is clamped to `max(d − pixel, 0)`, where d is the distance to the part's own outline segments and `pixel = length(fwidth(sheetPos))`.

**Outline (per part):**
- `SheetWarp.PackOutline(SheetLayers layers, int fromLayer, Vector4[] segments, out bool truncated)` takes the edges of layers `fromLayer..top`, removes coincident duplicates (either direction, within 1e-4), and skips zero-length edges.
- `MaxOutlineSegments` = 128, and the shader's `MAX_OUTLINE` matches.
- On overflow it returns −1, and that part's warp is 0, with a once-per-renderer warning in editor and development builds.

**Sorting layer check:** `OnEnable` scans `SortingLayer.layers` for "Sheet Shading". It needs to find the layer, with a non-zero id, after Default. Otherwise it logs an error and creates no shading children (the sheet is unshaded and unwarped). The layer is added through the TagManager SerializedObject with a random non-zero unique `uniqueID`.

**Tests added or changed:**
- `PackOutline` (per-part range, duplicate removal, overflow);
- `FadedOffset` with the pixel margin;
- the project settings check (layer unique, non-zero, after Default; the Renderer2D fields);
- shader source and parity defines;
- the `ShadingOrder` test's message.

**Aaron's play list (final):**
- A remembered crease under Scuffy, a block, and the drawn map: lines kink.
- Front crease vs Back crease (tune the inside and outside warp, including the sign).
- Grain wobble level.
- Live fold edges, Seams, the sheet edge: crisp, with no smear.
- Fold-on-fold.
- Whole sheet not flipped or shifted; no one-frame trail behind Scuffy.
- Neighbour sheets and the slide.
- The drag preview Flap and red tint; the unfold swing.
- Warps all 0 → identical to before.
- Performance feel.

## Review, round 2

Plan review, round 2 (`plan-reviewer`): BLOCK, on two engineering points. Both are accepted and fixed below. This is the workflow's second and final round. Nothing is left contested, and no question for Aaron came out of it. The code review checks the fixes.

- **B1** (face cameras share the one Renderer2D, so the sorting-layer copy forces an intermediate colour texture on them and reallocates the shared copy handle every frame): **accepted; N7 reinstated.**
  - A second renderer asset, `Assets/Settings/Renderer2D Faces.asset` (a copy of Renderer2D with `m_UseCameraSortingLayersTexture: 0`), is added to `UniversalRP.asset`'s `m_RendererDataList` at index 1.
  - New runtime helper `FaceCameraRenderer.Use(Camera)` (static, `Scripts/Fold/FaceCameraRenderer.cs`): `GetUniversalAdditionalCameraData().SetRenderer(1)`. If the active pipeline asset doesn't have that renderer, it logs an error once and leaves the camera on the default (it works, at the old cost).
  - It is used by `RenderTextureFoldRenderer.CreateFaceCamera` and by the Studio's pane, X-ray and fold-preview cameras (N7 below), so every face-texture camera keeps its current path.
  - `Papercut.asmdef` references `Unity.RenderPipelines.Universal.Runtime`. The settings test asserts list[1] is the Faces asset with the copy off, and list[0] has it on.
- **B2** (the warp tied to the strongest-shading pick: zero when the shading strength is 0, and torn where creases cross): **accepted.**
  - The warp is accumulated separately: each crease's warp vector is the unscaled map slope × fade × end taper × map warp × (`rememberedCreaseWarp` if remembered). They combine as `Σ|v|²·v / Σ|v|²`, which is continuous, and a repeated crease averages to itself.
  - **End taper:** at a *closed* crease end (not on the sheet border), the warp tapers to 0 over the last `creaseHalfWidth` of the segment, so a remembered crease ending mid-sheet doesn't tear.
  - A C# reference in `SheetWarp` (`CreaseWarp`, `CombineWarps`) and tests, including continuity across two crossing creases.
- **N1** (the live rebind would bind one scratch outline to every part): **accepted.** Each part's outline segments, count and overflow flag live on its `MeshPart`. Full-length arrays are always passed.
- **N2** (WebGL2's 224 guaranteed fragment uniform vectors): **accepted.** `MaxOutlineSegments` goes back to **64**: 64 + 64 + 64 float4 plus the cbuffer is about 210. Flagged in the Bible beside the copy cost.
- **N3** (grain warp softening and shimmer): **accepted** into the play list: "sharpness of the map art and Scuffy, `grainWarp` 0.03 vs 0; shimmer while sliding".
- **N4** (`rememberedCreaseWarp` default 1 vs shading 0.4): **accepted.** Default 0.4, matching how faintly a remembered crease is drawn. Also on the play list.
- **N5** (removing the ratio clamp breaks "warps 0 → identical"): **accepted.** The [0, 2] clamp is kept.
- **N6** (no reference for slope → offset, and nothing checks the sorting layer of the children): **accepted.** `SheetWarp.FaceToDesk(slope, grainFrame)` is the reference for the `_GrainFrame` carry, tested on a Back-up (mirrored) frame. `RenderTextureFoldRenderer.TryGetShadingSortingLayer(out int id)` is a static, testable layer lookup used by `AddShading`.
- **N7** (Studio pane cameras also share the renderer): **accepted.** They use `FaceCameraRenderer.Use` too. Verification adds "Studio panes look unchanged" to Aaron's list.

## Deviations

- **The warped point maps to the screen through the sheet position's screen derivatives** (`ddx`/`ddy`, solving a 2×2), not through `ComputeScreenPos`. It is exact for the affine orthographic mapping, and makes no assumption about the target's y orientation (round 1, N6). The one-pixel margin uses the same derivatives.
- **Saving `Renderer2D.asset` through the editor rewrote it in the current URP serialization.** Stale fields that URP 17 no longer serializes are gone (`m_LightShader` etc.; the shaders now live in the pipeline's graphics settings), and `m_LayerMask` and `probeVolumeBlendStatesCS` were added. `TagManager.asset` went to serializedVersion 3 and gained `m_RenderingLayers`. Both are Unity's own upgrades on save.

## Verification (step 6)

- Recompile clean (including the new `Unity.RenderPipelines.Universal.Runtime` reference in `Papercut.asmdef`).
- `Sheet Shading` force-imported: `ShaderHasError` False, no messages.
- Settings, made through the editor (backups in the scratchpad):
  - sorting layer `Sheet Shading` added (uniqueID 1923214841, value 1, after Default);
  - `Renderer2D Faces.asset` copied with the copy off;
  - `UniversalRP` renderer list [0] = Renderer2D, [1] = Renderer2D Faces;
  - Renderer2D: copy on, bound 0, Downsampling None.
- `run_tests` (editor): 582 total, 579 passed, 3 failed (the same pre-existing PowerCroissant ×2 and StudioLinks undo). All 14 `SheetWarpTests` passed, including the settings and sorting-layer checks. No assets or settings changed during the run.

## Code review

Round 1 (`code-reviewer`): APPROVE WITH FIXES, nothing must-fix. All fixes applied. They are small (a one-line margin change, references and tests), so there was no second round.

- **S1** (a one-pixel margin leaves 45° edges up to √2 px exposed to the bilinear footprint): **fixed.** The margin is `|ddx| + |ddy|` in the shader. `FadedOffset`'s parameter is now `margin`, documented the same way.
- **S2** (missing or weak tests): **fixed.** Added:
  - `PackEdges_TooManyEdges_IsMinusOne_AndTruncated` (`PackOutline` now wraps `PackEdges`);
  - the Flap test asserts the sample stays ≥ margin inside;
  - `FadedOffset_BasePart_NeverReachesIntoTheFlapAbove`, a corner fold that exercises the per-part outline.
- **S3** (no reference for the slope → offset assembly): **fixed.** `SheetWarp.CreaseWarp` and `SheetWarp.Offset` are added and tested: frame, mirrored repeat, remembered multiplier, taper, the downhill sign, and the Back-up mirror. The shader comments point at them.
- **S4** (stale `shadingMaterial` tooltip and test name): **fixed.**
- **S5** (the settings test read the asset by path): **fixed.** It reads `GraphicsSettings.currentRenderPipeline`.

After the fixes: recompile clean, the shader force-imported with no errors, and `run_tests` (editor) gave 586 total, 583 passed, 3 failed (the same pre-existing three). 18/18 SheetWarpTests passed. No assets or settings changed during the run.

## Post-delivery fix (2026-10-08)

**Aaron's report:** the Screen was all green with thin vertical lines; no player, blocks or walls drawn.

**Diagnosis**, through a Play Mode probe Aaron allowed for this bug: every pixel sampled one row of the screen copy. `GetNormalizedScreenSpaceUV` flips y through `_ScaleBiasRt`, which the 2D renderer's draw pass leaves unset (0), so `uv.y` collapsed to a constant.

**Fix:** `screenUV = pixel position / _ScaledScreenParams.xy`, with no flip. The copy has the target's orientation.

**Verified in Play Mode (Desk scene, Sheet (0,0)):**
- The Screen draws correctly: art, water, walls, blocks and Scuffy, upright.
- A west fold of 2.5 draws its Back-up Flap at x −3…−0.5, with the Desk showing through the lifted strip and a clean Seam.
- After `Reset`, 2 remembered marks, with a faint crease line at x = −3.
- The ink kink at the default remembered warp (0.4 × 0.15) is about 1–2 px: barely visible. That's tuning for Aaron.
- Console: only the pre-existing Power Croissant empty-frame error.
- Play Mode stopped.

`run_tests` (editor) after the fix: 586 / 583 / 3 (the pre-existing three).

## Rework: pinch + soft wrinkles (2026-10-08, lightweight, Aaron)

**Aaron:** *"It kinda feels like a smearing effect instead of the things just following the folds of the paper."* The cause: offsets were proportional to the maps' per-pixel slopes, which are noisy, so neighbouring pixels were shuffled.

**Options offered:** pinch + soft wrinkles / pinch only / remove. **Chosen:** pinch creases + soft wrinkles. Workflow: lightweight, with Play Mode screenshots.

**Crease warp:** a smooth pinch profile across the band, `SheetWarp.Pinch(a, w) = sign(a)·w·x·(1−x)²` along the crease normal. It is independent of the map's pixels (the maps only light). It is one-to-one for pinch ∈ [−0.9, 2.5], and a test checks that.

**Grain warp:** uses the grain map's blurred mip (`grainWarpBlur`, default 4) minus its 1×1 mip (average lean, cancelled automatically, per Aaron's choice). Offset = crease pinch − `grainWarp` × broad slope.

**Tunables** (renamed, because the meaning changed):
- `creaseInsidePinch` and `creaseOutsidePinch`: 1;
- `rememberedCreasePinch`: 0.4;
- `grainWarp`: 0.6;
- `grainWarpBlur`: 4.

The Sheet prefab was updated through the editor (only these fields changed).

**Verification:**
- Recompile clean; shader has no errors.
- `run_tests` gave 588 / 585 / 3 (the pre-existing three); 20/20 SheetWarpTests passed.
- Play Mode, Sheet (0,0):
  - A remembered crease at x = −3 gives a smooth S-step in the river ink and chalk lines at pinch 1. At 2.5 the chalk streaks stretch on the band's outer half, so 1–1.5 is the useful range.
  - Flat sheet, `grainWarp` 0.6 vs 0: a gentle waviness in lines, no smear (mean pixel difference 3/765).
  - Console: only the pre-existing Power Croissant error. Play Mode stopped.
