# Crease normal maps

## 1. Task

Aaron: *"I added a normal map for creases, one for the front side, one for the back. Can you use these instead of the black line placeholder we have?"*

The new assets are `Assets/Papercut/Sprites/Crease Texture Frontside.png` and `Crease Texture Backside.png` (both 1426 × 1103 px, about 11 : 8.5, the sheet's aspect). Each shows a whole sheet with one horizontal crease across its middle. Today a crease is a flat-coloured line strip (`Crease.mat`, URP/Unlit) drawn as face content into each face's render texture. This task replaces those strips with normal-map shading of the paper along every crease, live and remembered.

**Out of scope:** the Seam overlay (the dragged Flap's border line), which stays a coloured line. Also out of scope: crease drawing in the Sheet Studio panes, which Aaron turned off on 2026-08-31 and which stays off. Wrinkle art away from creases is out too, and the paper grain stays as it is.

## 2. Design references

- Bible §4 *Rendering [DECIDED — provisional, 2026-08-26]*: render-texture compositing behind `IFoldRenderer`, with crease lines as face content. *"Wrinkle/crease art later is a shader on these textures."* The 2026-09-21 paper grain is the `Papercut/Paper Face` shader. It shades each composited piece through its face's normal map, using a fixed Desk-space light, and draws over everything on the face but not over the player or blocks. This task follows that pattern exactly.
- Bible §4 *Unfolding*: *"Creases left by undone folds are drawn faintly and clear with the fold reset when the player leaves"* (Aaron: *"creases clear on reset."*). This stays true: remembered creases get a lower strength.
- Design Doc *Art*: *"After folding and unfolding, the paper will have a crease. The elements on the page will follow the wrinkles on the paper."* Shading over everything on the face serves this.
- Bible §1: the WebGL target is [TENTATIVE]. Nothing here adds readbacks or threads. The shader moves to `#pragma target 3.0` for a dynamic loop, which WebGL2 supports.
- This task touches no `[DECIDE]` item. Rendering approach #2 is already decided, and this stays inside `RenderTextureFoldRenderer`.

## 3. Decisions already made by Aaron (this task, 2026-10-07)

1. **Full coding workflow:** yes.
2. **Which map a crease uses: "By fold direction".** The Frontside map shades the face that was **up** when the fold was made (the inside of the fold). The Backside map shades the face that was **down** (the outside). For every ordinary fold this means Front gets Frontside and Back gets Backside. The two rules only differ when a Back-up Flap is folded again (stacking on).
3. **Orientation across the crease: "Inspector flip toggle".** By default the top of the image faces the side that lifted. A per-map flip checkbox swaps it.

## 4. Files

| File | Change |
|---|---|
| `Assets/Papercut/Scripts/Fold/FoldEffect.cs` | `CreaseMark`: replace `Shift` with `Lifted` (unit vector toward the side that lifted, in the mark's face space) and `Inside` (this face was up when the fold was made). |
| `Assets/Papercut/Scripts/Fold/SheetLayers.cs` | `AddMarks` fills `Lifted` / `Inside` instead of `Shift`. |
| `Assets/Papercut/Scripts/Fold/CreaseShading.cs` | **New.** Pure static packer: turns a face's live and remembered marks into the shader's fixed-size uniform arrays. |
| `Assets/Papercut/Scripts/Fold/RenderTextureFoldRenderer.cs` | Removes the crease line meshes. Packs creases per face and binds them in each layer's property block. Adds the new tunables, and renames `creaseMaterial` to `seamMaterial`. |
| `Assets/Papercut/Shaders/Paper Face.shader` | Adds the crease normal sampling and blends it with the grain normal before lighting. |
| `Assets/Papercut/Prefabs/Sheet.prefab` | Assigns the two maps (done through the editor, not as a YAML edit). |
| `Assets/Papercut/Materials/Crease.mat` → `Seam.mat` | Rename (`AssetDatabase.MoveAsset`; the guid is kept). It now only draws the Seam. |
| `Assets/Papercut/Sprites/Crease Texture Frontside.png.meta`, `…Backside.png.meta` | Reimport as Normal map: sRGB off, clamp wrap (through `TextureImporter` in the editor). |
| `Assets/Papercut/Editor/StudioFoldPane.cs` | Deletes the dead `CreaseColor` / `RememberedCreaseColor` / `CreaseWidth` settings. They are read but never used, and their fields are going away. |
| `Assets/Papercut/Tests/EditMode/SheetLayersTests.cs` | The two crease-mark tests assert `Lifted` / `Inside` instead of `Shift`. |
| `Assets/Papercut/Tests/EditMode/StudioFoldModelTests.cs` | Drops the three deleted field names from the field-resolves test. |
| `Assets/Papercut/Tests/EditMode/CreaseShadingTests.cs` | **New.** Tests for the packer. |
| `Documents/Claude Bible.md` | §4 Rendering: creases are now shading in Paper Face, not line content. |

## 5. Components & data

### `CreaseMark` (FoldEffect.cs)

`readonly struct CreaseMark { SheetFace Face; Vector2 A; Vector2 B; Vector2 Lifted; bool Inside; }`

- `A`, `B` are unchanged: the crease segment in the face's authored space (Front-space or Back-space).
- `Lifted` is a unit vector, perpendicular to AB in the same face space, pointing to the side of the crease that lifted. For a Back mark it is the Front-space vector through `SheetGeometry.BackToFront`, which is a mirror in x and applies the same way to a direction.
- `Inside` is true if this face was the up face of the cut layer when the fold was made: Front mark = `layer.FrontUp`, Back mark = `!layer.FrontUp`.
- `Shift` is removed. The renderer was its only consumer, for the old half-width offset. A normal-mapped crease is centred on the fold line: each piece shows its own half. Under a landed Flap the Base's half is hidden, and the Flap shows its own half at its folded edge, as real paper does.

### `CreaseShading` (new, static, pure)

- `public const int MaxCreases = 32;` The shader's array length. Each face gets its own arrays.
- `public static int Pack(IReadOnlyList<FoldEffect> effects, IReadOnlyList<CreaseMark> remembered, SheetFace face, Vector4[] lines, Vector4[] info, out bool truncated)`
  - `lines[i] = (mid.x, mid.y, lifted.x, lifted.y)`, in the face's authored space, sheet units.
  - `info[i] = (halfLength, map, remembered, 0)`: `map` is 0 for the inside map and 1 for the outside map; `remembered` is 0 or 1.
  - Order: live marks first (every effect's `CreaseMarks` for `face`, in fold order), then remembered marks, **newest first**.
  - Marks with a length under `SheetLayers.MinSegmentLength` (or under a local epsilon if that constant is private) are skipped.
  - A remembered mark that duplicates an already-packed mark is skipped. Duplicate means same face, same `Inside`, and endpoints within 1e-3 in either order. This happens when Aaron folds and unfolds at the same snapped depth, and without the skip the strip would be packed twice for nothing.
  - On overflow, live marks are kept first, then the newest remembered ones. `truncated = true` reports the overflow, and the oldest remembered creases are the ones that disappear.
  - Unused array slots are left untouched. The shader reads only `count` entries.
  - Throws `ArgumentException` if an array is shorter than `MaxCreases` (a programming error).

### `RenderTextureFoldRenderer`

Removed: `creaseWidth`, `creaseColor`, `rememberedCreaseColor`, the four crease `MeshPart`s, and `FaceCreaseZ`.

Renamed: `creaseMaterial` → `seamMaterial` with `[FormerlySerializedAs("creaseMaterial")]`. Tooltip: material for the Seam border.

New **Inspector tunables** under `[Header("Crease")]`. All are read live, the way grain is: a change in Play Mode rebinds the blocks.

| Field | Type | Default | Tooltip gist |
|---|---|---|---|
| `creaseInsideMap` | `Texture2D` | none (prefab: *Crease Texture Frontside*) | Normal map of a crease on the face that was up when folded (the inside of the fold). It lies along every crease, at sheet scale (the image spans the sheet). None means no crease shading. |
| `creaseOutsideMap` | `Texture2D` | none (prefab: *Crease Texture Backside*) | The same for the face that was down (the outside of the fold). |
| `creaseInsideLine` | `float [Range(0,1)]` | 0.5 | Height of the crease line in the inside image (0 = bottom, 1 = top). |
| `creaseOutsideLine` | `float [Range(0,1)]` | 0.5 | The same for the outside image. |
| `flipCreaseInside` | `bool` | false | Off: the image's top faces the side that lifted. On: turned half round. |
| `flipCreaseOutside` | `bool` | false | The same for the outside map. |
| `creaseHalfWidth` | `float [Min(0.01)]` | 0.5 | How far either side of a crease its map is used, in sheet units. It fades out over the outer half. |
| `creaseStrength` | `float [Range(0,4)]` | 1 | Strength of a live crease (0 = invisible, 1 = as authored). |
| `rememberedCreaseStrength` | `float [Range(0,4)]` | 0.4 | Strength of a crease left by an undone fold. |

New private state:
- Per face: `Vector4[MaxCreases]` lines and info arrays, plus a count.
- `reportedTooManyCreases`, so the overflow warning is logged once.

The existing `GrainSettings` change-detection struct becomes `ShadingSettings` and also covers every crease tunable.

### `Papercut/Paper Face` shader

New properties, all `[HideInInspector]` except the maps:
- `_CreaseInsideMap`, `_CreaseOutsideMap`: `[Normal] 2D = "bump"`
- `_CreaseCount`, `_CreaseHalfWidth`, `_CreaseStrength`, `_RememberedCreaseStrength`
- `_CreaseLineV` (x = inside, y = outside), `_CreaseFlip` (x = inside, y = outside, ±1), `_SheetSize` (x = W, y = H)

All of these go in `UnityPerMaterial`. The arrays `float4 _CreaseLines[32]` and `float4 _CreaseInfo[32]` are declared outside the cbuffer, since arrays can't be material properties. Their defaults are zero, and `_CreaseCount` defaults to 0, so the Studio's block-less use draws no creases (Aaron's 2026-08-31 choice holds).

## 6. Behaviour

**Packing** (`BuildMeshes`, every `Draw`): pack the Front and Back crease arrays from `replayEffects` and `shownCreases`, before the layer loop. Then each layer part is bound (`BindLayer`) with the arrays of the face it shows (`part.FrontUp`) as well as the existing grain values. `SetVectorArray` always receives the full 32-length arrays, because a property block fixes an array's size the first time it is set. If either face reports `truncated`, log one `Debug.LogWarning` per renderer: "N creases on face F exceed the shader's 32; the oldest remembered creases are not drawn". If both maps are null, the count is bound as 0, so nothing is drawn and nothing is sampled.

**Live tuning** (`LateUpdate`): if `ShadingSettings` changed, rebind every layer part. The arrays do not depend on tunables (strengths, flips and line heights are uniforms), so no repacking is needed.

**Shader, per fragment.** The face position is `p = (uv − 0.5) · _SheetSize`. A piece's uv is its up face's authored space, so the marks for that face line up with it. For each `i < _CreaseCount`, with `MAX = 32` and a `break` at the count:

- `n = lines[i].zw · flip(map)` and `t = (n.y, −n.x)`. The frame (t, n) has the image's handedness (u right, v up), so a flip is a 180° turn, never a mirror. `rel = p − lines[i].xy`, `along = dot(rel, t)`, `across = dot(rel, n)`.
- If `|along| > info.x` (past the crease's ends) or `|across| > _CreaseHalfWidth`, skip.
- Along the crease, `x = 0.5 + along / W`. The image is centred on the crease's midpoint. A 45° corner crease can be up to 8.5·√2 ≈ 12.0 units long, more than the image's 11, so the image is mirror-repeated: `m = fmod(|x|, 2)`, `u = m ≤ 1 ? m : 2 − m`, `alongSign = (m ≤ 1 ? 1 : −1) · sign(x)`.
- Across the crease, `v = line(map) + across / H`.
- Sample the map at (u, v) and at the two band edges (u, line ± halfWidth/H) with `SAMPLE_TEXTURE2D_LOD`, lod 0, through a linear-clamp sampler. Unpack each (`UnpackNormal`) and take `c = s.xy − ½(edgeTop.xy + edgeBottom.xy)`. This subtracts the map's own baseline. Aaron's maps are not flat away from the crease: the whole Frontside map leans about 11° in x and the Backside about 7°. Without the subtraction every crease would sit in a visibly tinted stripe.
- Then `c.x *= alongSign`, `c *= fade · strength`, where `fade = 1 − smoothstep(0.5, 1, |across| / halfWidth)` and strength is live or remembered per `info.z`.
- Keep the contribution with the largest |c| (`creaseXY = t · c.x + n · c.y`, in face space). Taking the strongest instead of a sum keeps a duplicate, or a live crease over a remembered one, from doubling, and at crossings one crease draws over the other.
- Blend with the grain: `normalFace = normalize(half3(grain.xy + creaseXY, grain.z))`. From there the existing `_GrainFrame` → Desk → light path is unchanged. A Back-up piece's frame already contains the Back-space mirror, so creases on the Back are shaded exactly like the Back grain.

**Edge cases**
- No maps: shading is skipped.
- Crease at the sheet edge: the band is clipped by the piece's own mesh, so nothing draws past the paper.
- Partially covered crease: each piece shades only what it shows.
- Fold-on-fold: one mark per cut layer per face. The inside/outside choice follows that layer's up face at fold time (Aaron's decision 2).
- The Seam overlay still uses `seamMaterial` and `PolygonMeshBuilder.AddLine`.
- `seamMaterial` or `faceMaterial` missing: the existing error log stays.

## 7. Interfaces & seams

Everything stays behind `IFoldRenderer`. The fold model only gains two fields on `CreaseMark`, which are geometric facts (which side lifted, which face was inside).

To cut the feature: clear both maps on the Sheet prefab, or set the strengths to 0. Deleting it outright means removing `CreaseShading.cs` and the shader block. Creases then become invisible: the old line meshes don't come back.

## 8. Testing

**Edit Mode tests (written by me):**
- `SheetLayersTests.EdgeFold_CreaseMarks_…`: the south edge fold's Front mark has `Lifted = (0, −1)` and `Inside = true`; its Back mark has `Lifted = (0, −1)` (a mirror in x leaves y unchanged) and `Inside = false`. The test is renamed to match.
- `SheetLayersTests.FoldOnFold_CreaseMarks_…`: on the Back-up north Flap cut by the west fold, Front is `Inside = false` and Back is `Inside = true`. `Lifted` points toward desk −x (the west side lifted), which is Front-space −x and Back-space +x.
- `CreaseShadingTests`: midpoint, normal and half-length packing; face filtering; live before remembered, remembered newest first; map index from `Inside`; the remembered flag; duplicate remembered marks (in reversed endpoint order) skipped; overflow keeps all live marks plus the newest remembered and sets `truncated`; a zero-length mark is skipped; short arrays throw.
- `StudioFoldModelTests`: the field-resolve test without the three deleted names.

**Mechanical checks through the Unity CLI** (no Play Mode):
1. `recompile`, with no errors.
2. `ShaderUtil.ShaderHasError(Shader.Find("Papercut/Paper Face"))` is false, and `ShaderUtil.GetShaderMessages` is empty.
3. The importer of each crease PNG reads `textureType == NormalMap` and `sRGBTexture == false`.
4. The Sheet prefab, loaded through the editor, has both maps assigned and `seamMaterial` pointing at `Seam.mat` (same guid).
5. The collision check (not playing, no test run, not compiling, scene not dirty), then `run_tests {"mode":"editor"}`.

**What Aaron needs to play** (none of this can be judged from the CLI):
- How a live crease looks on the Flap edge and on the Base after unfold, for edge and corner folds.
- The remembered crease strength.
- Whether the flip toggles need turning on (which way the asymmetric Frontside map should face).
- Whether `creaseInsideLine` / `creaseOutsideLine` need nudging to sit on the fold edge. The measured crease rows are about 0.504 (Frontside) and about 0.50 (Backside) from the bottom.
- The band width.
- Any visible repeat on long corner creases.
- Crossing creases.

## 9. Assumptions (engineering)

- The images are authored at sheet scale: 1426 px = 11 units along the crease and 1103 px = 8.5 units across. This is inferred from their 11 : 8.5 aspect.
- Mirror-repeating along a crease longer than the image is acceptable. Only corner creases between 11 and about 12 units long reach it.
- A 32-per-face cap, dropping the oldest remembered creases with a one-time warning, is acceptable. Folding and unfolding at a continuous depth more than about 30 times on one sheet before leaving would reach it.
- Baseline subtraction (§6) is a correction for the maps as delivered. If Aaron re-exports flat maps it does nothing harmful.
- Renaming `Crease.mat` to `Seam.mat` keeps its guid, so the prefab reference holds.

## 10. Open questions

None.

## Review

Plan review, round 1 (`plan-reviewer`): APPROVE WITH CHANGES, nothing blocking.

- **N1** (edge-row baseline subtraction streaks perpendicular to the crease): **escalated to Aaron (Q1).** If the automatic correction is kept, it changes to one constant offset per map: the map's whole-image average, read from its smallest mip (mipmaps on at import). The same-u edge samples go.
- **N2** (`sampler_LinearClamp` is already declared by URP's GlobalSamplers.hlsl): **accepted.** Use the global sampler, without redeclaring it.
- **N3** (put the measured line heights on the prefab): **accepted.** Measured at the slope sign change, which is the fold's apex: Frontside 0.509, Backside 0.5045 (from the bottom). The code default stays 0.5.
- **N4** (fade start and image scale are feel values): **accepted.** New tunables: `creaseFadeStart` [Range(0,0.99)], default 0.5, and `creaseMapWidth` [Min(0.1)], default 11 (`SheetGeometry.Width`): the sheet units the image's width spans. The scale across follows the texture's aspect (`_TexelSize`).
- **N5** (NPOT normal map can't compress): **accepted.** Import with `npotScale = ToLarger` (2048², BC5, ~5.3 MB with mips) rather than ToNearest (1024², which would blur a 1–2 px crease line). Check #3 also reads the format.
- **N6** (uniform binding unspecified, `_SheetSize` zero default, raw uv): **accepted.** `BindLayer` sets every crease uniform. `_SheetSize` defaults to (11, 8.5). `p` comes from the raw `input.uv`. `_CreaseCount` is a Float, set with `SetFloat` and cast in HLSL.
- **N7** (cap reachable in normal play): **escalated to Aaron (Q2).** If a cap stays, the warning is editor/development-build only.
- **N8** (shader frame math untested): **accepted.** `CreaseShading.MapPoint(...)` is a pure C# reference (face point → u, v, alongSign, inBand) that the shader mirrors line for line, and it is unit-tested: edge, corner, Back-space, flip, both mirror zones, x = 0. `sign()` is replaced by `x >= 0 ? 1 : -1`. Also added: a corner-fold `LiftedSide` test, a live-vs-remembered duplicate test, and a test that every C# property name exists on the shader.
- **N9** (band end cut and collinear restarts in stacked mixed folds): **accepted** into §9 and the play list.
- **N10** (retreat swing, shallow-Flap poke-out, preview alpha): **accepted** into the play list.
- **N11** (`CreaseMark.Lifted` collides with `FoldEffect.Lifted`): **accepted.** Renamed to `LiftedSide`.
- **N12** (the Studio does set a block): **accepted.** Wording fixed here and in the shader header: the Studio's block sets only `_BaseMap`, so the material defaults (`_CreaseCount` 0) apply.

Play list additions (N9/N10):
- A retreating fold's crease sweeps at live strength during the swing.
- A Flap shallower than `creaseHalfWidth` shows the Base's crease half past its Seam.
- The 0.85-alpha preview Flap shows the Base's crease half through it.
- Stacked edge + corner folds: crease ends cut square, and collinear pieces restart the image.

## Post-review answers (Aaron, 2026-10-07)

- **Q1, map lean: "I'll re-export flat maps".** No correction in the shader: a crease's normal is the map's sample, used as authored, times fade and strength. No baseline samples, and no mipmaps (lod 0 sampling only; import stays mip-less). The current maps will show a faintly shaded band until they are re-exported, and that is expected.
- **Q2, cap: "Cap 64, drop oldest".** `CreaseShading.MaxCreases = 64` per face. Live marks are kept first, then the newest remembered. The overflow warning is logged once per renderer, only in the editor or a development build.

These supersede the baseline-subtraction text in §6 and the 32 in §5/§6/§9.

## Deviations

- The image's height in sheet units is `creaseMapWidth × H / W` (the sheet's aspect), not read from the texture. The ToLarger NPOT import stretches both maps to 2048², so the texture no longer knows its source aspect. This matches the stated assumption that the image is a picture of a whole sheet.
- Bible §4 Rendering gained a dated "Crease normal maps [2026-10-07]" note.

## Verification (step 6)

- `recompile`: completed, `errors: []`.
- `ShaderUtil.ShaderHasError(Papercut/Paper Face)` = False, with no shader messages.
- Both crease PNGs import as NormalMap, sRGB off, 2048×2048, DXT5 (DXT5nm).
- `Crease.mat` was moved to `Seam.mat` with the guid kept.
- The Sheet prefab, loaded through the editor: both maps assigned, `seamMaterial` = `Seam.mat`, lines 0.509 / 0.5045. The disk diff against a backup shows only the renderer's fields.
- `run_tests` (editor): 556 total, 553 passed, 3 failed. All 3 are pre-existing and unrelated: `DrawingAssetsTests.PowerCroissant_ShowsItsDrawing_AndGrantsPush`, `DrawingAssetsTests.PowerCroissantIdle_IsThreeFramesAtTheSharedScale`, `StudioLinksTests.Wire_AppendsTargets_ARepeatIsOneEntry_AndUndoRevertsIt`. All 19 `CreaseShadingTests` and the 3 `SheetLayersTests` crease-mark tests passed. No asset other than Sheet.prefab changed during the run.

## Code review

Round 1 (`code-reviewer`): REJECT, with one must-fix.

- **M1** (a corner crease's band is cut square at segment ends lying on the sheet border, leaving an unshaded wedge): **fixed.** `Pack` writes per-end open flags in `info.w` (`OpenStartFlag` = the −t end, `OpenEndFlag` = the +t end, relative to the unflipped frame) for ends on the sheet border. `MapPoint` and the shader skip the along-cut at an open end, and the piece's mesh clips anything off the sheet. Tests added: corner-fold wedge point, only the flagged end open (with and without flip), and pack flags for corner and inner creases.
- **S1** (the Bible still describes crease lines): **fixed.** The older sentences are reworded, with a strike-through pointing to the 2026-10-07 note.
- **S2** (a remembered crease folded the other way counts as a repeat): **fixed.** A repeat now also needs the same `LiftedSide`, and a test was added.
- **S3** (`MAX_CREASES` and `MaxCreases` tied only by a comment): **fixed.** A test reads the shader's `#define` and compares the two.

After the fixes: recompile clean, the shader force-imported with no errors or messages, and `run_tests` (editor) gave 561 total, 558 passed, 3 failed (the same pre-existing three). 24/24 CreaseShadingTests passed. No assets changed during the run.

Round 2 (`code-reviewer`): APPROVE WITH FIXES, nothing must-fix. All three should-fixes applied:
- the `SameMarkDistance` comment now says what it measures;
- a test builds a real `SheetLayers` corner fold and checks both ends are open on both faces;
- the Bible's "Wrinkle/crease art later" now reads "Wrinkle art later … (crease art now is)".

Final run: recompile clean; `run_tests` (editor) 562 total, 559 passed, 3 failed (the same pre-existing three); 25/25 CreaseShadingTests passed; no assets touched.
