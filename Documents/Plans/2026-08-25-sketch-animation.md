# Plan — Sketch animation (looping hand-drawn frames) and the player's idle

Date: 2026-08-25

## 1. Task

Aaron's request (verbatim):

> "Okay, I also have a vision for the artstyle for things that live on the paper, which will be sketchy childrens drawings type thing. For the player, I drew 3 drawings that I would like to loop like an animation type thing over time, which will give it that sketchy wobble effect. There will be a lot of images like this, hopefully. The player will eventually have other animations as well, but for now its just the idle animation of these 3 looping frames. Can you add this, making sure that the same technique can be applied to other objects once they exist?"

Deliverable:

- A reusable **sketch animation** mechanism: an asset holding an ordered list of hand-drawn frames plus a frame rate, and a component that loops it on any object's `SpriteRenderer`.
- The player's three drawings (`Assets/Papercut/Sprites/Scuffy animated/PNGSet/PNG_0001..3.png`) wired up as the player's idle animation, replacing the placeholder blue sphere.
- Import settings for those three frames fixed for their actual on-screen size.

**Out of scope:** any animation other than idle (walk, push…); an animation state machine; directional facing; animating anything other than the player; art for sheets/terrain; anything `[TENTATIVE]`.

## 2. Design references

- Design Doc — *Art*: "very sketchy and rough, like a children's drawing. Everything should feel like it exists on and in the paper." The frame-loop "boil" is the direct expression of that for drawn objects.
- Design Doc — *Pillars* 3: "art should feel like sketchy and stuff".
- Bible §1 **`[DECIDE]` #4 2D vs 3D scene setup** — *touched, not resolved.* The player's visual becomes a `SpriteRenderer`. Sprites render correctly in either a 2D or a 3D orthographic scene, and the scene already uses `Rigidbody2D`, so this closes nothing: the sheets stay meshes, and if the fold renderer later needs on-sheet objects as meshes, the seam is the `SpriteRenderer` on each object (see §7).
- Bible §4 Rendering `[DECIDE]` — untouched. The player is always on the base (`[LOCKED]`), so its rendering is never deformed by a fold.
- Bible §0 / implementation-guidelines §3 — simple correct implementation; no state machine, no plugin surface.
- implementation-guidelines §4a — frame rate and start-phase randomisation are feel values → Inspector.
- Glossary — nothing conflicts. "Sketch" is the chosen vocabulary for this art technique (asset `SketchAnimation`, component `SketchAnimator`).

## 3. Decisions already made by Aaron

- **Q1 (this task):** "What should drive the frame loop: a small custom Papercut component (A), or Unity's Animator/AnimationClip (B)?" — **Aaron: "A".** So: `SketchAnimation` ScriptableObject + `SketchAnimator` MonoBehaviour, no Animator/AnimatorController.
- Stated as engineering calls in the same message and not objected to: replace the player's sphere mesh with a `SpriteRenderer`; fix the frame imports (max size, mipmaps, pixels-per-unit so the character keeps roughly its current ~0.6-unit width).

## 4. Files

Create:

| File | Purpose |
|---|---|
| `Assets/Papercut/Scripts/Sketch/SketchAnimation.cs` (+ .meta) | ScriptableObject: ordered frames + frames-per-second. Also holds `SketchPlayhead`, the pure frame-timing struct. |
| `Assets/Papercut/Scripts/Sketch/SketchAnimator.cs` (+ .meta) | Component that loops a `SketchAnimation` on the sibling `SpriteRenderer`. |
| `Assets/Papercut/Animations/Scuffy Idle.asset` (+ .meta, folder .meta) | The player's idle: the three frames. |
| `Assets/Papercut/Tests/EditMode/SketchPlayheadTests.cs` (+ .meta) | Edit Mode tests for the frame-timing struct. |

Modify:

| File | Change |
|---|---|
| `Assets/Papercut/Prefabs/Player.prefab` | `Visual` child: remove `MeshFilter` + `MeshRenderer`, add `SpriteRenderer` (material: built-in `Sprites-Default`, `{fileID: 10754, guid: 0000000000000000f000000000000000}` — unlit, because the Desk scene has no `Light2D` and URP's `Sprite-Lit-Default` would render black) + `SketchAnimator` (idle = Scuffy Idle). Scale 0.6/0.6/0.1 → 1/1/1 (size now comes from pixels-per-unit). |
| `Assets/Papercut/Sprites/Scuffy animated/PNGSet/PNG_000{1,2,3}.png.meta` | `maxTextureSize` 2048 → 512 on every platform entry; `enableMipMap` 0 → 1; `spritePixelsToUnits` 100 → 2000. |

Delete:

| File | Reason |
|---|---|
| `Assets/Papercut/Materials/Player.mat` (+ .meta) | Only referenced by the removed `MeshRenderer`. Hand-generated placeholder from the previous session; untracked in git. |

The scene (`Desk.unity`) needs no edit: its Player instance only overrides root transform/name, so the prefab's new `Visual` flows through.

## 5. Components & data

### `SketchAnimation : ScriptableObject` — `[CreateAssetMenu(menuName = "Papercut/Sketch Animation")]`

Responsibility: one named loop of hand-drawn frames. Pure data.

| Field | Type | Tunable? | Default | Notes |
|---|---|---|---|---|
| `frames` | `Sprite[]` | — | empty | `[Tooltip]`: "Hand-drawn frames, in loop order. Two or more give the sketch wobble; one is a static drawing." |
| `framesPerSecond` | `float` | **Inspector tunable** | `6` | `[Min(0)]`. "How fast the drawing cycles through its frames. 0 holds the first frame." |

Public surface: `int FrameCount`, `Sprite GetFrame(int index)`, `float FramesPerSecond`, `bool IsPlayable` — true iff `FrameCount >= 1` and every frame slot is non-null.

No `OnValidate` error: resizing the array in the Inspector creates empty slots before sprites are dragged in, so validating there would spam false errors. An unplayable asset is reported, naming the asset, when something tries to `Play` it (§6).

### `SketchPlayhead` (struct, same file, `public`)

Pure timing, no Unity references beyond nothing — fully testable from Edit Mode without a scene.

- `int Frame`, `float Elapsed` (seconds into the current frame).
- `void Advance(float deltaSeconds, float framesPerSecond, int frameCount)`:
  - `frameCount <= 0` → throws `ArgumentOutOfRangeException` (caller's bug; the animator never calls it in that state).
  - `framesPerSecond <= 0` → hold: `Frame` unchanged, `Elapsed` reset to 0.
  - Otherwise `Elapsed += delta`; while `Elapsed >= 1/fps`: subtract, `Frame = (Frame + 1) % frameCount`. Handles a delta spanning several frames (hitch) without drift, and a live change of `framesPerSecond` without resetting.
  - `Frame` is clamped into range first (`Frame % frameCount`) so a shrunk frame list can't index out of bounds.
- `void Reset(int frame, float elapsedSeconds)` — sets `Frame` and `Elapsed`. The playhead stores seconds, not a 0..1 phase, because fps can change live; the animator computes any random phase from the fps at play time.

### `SketchAnimator : MonoBehaviour` — `[DisallowMultipleComponent]`, `[RequireComponent(typeof(SpriteRenderer))]`

Responsibility: drive a `SpriteRenderer` from a `SketchAnimation`. Owns the playhead. Does **not** decide *which* animation should play beyond the idle default — future gameplay code calls `Play`.

| Field | Type | Tunable? | Default | Notes |
|---|---|---|---|---|
| `idle` | `SketchAnimation` | — | null (must be set) | `[Header("Animation")]`. "Played on start and whenever nothing else is playing." |
| `randomiseStart` | `bool` | **Inspector tunable** | `true` | "Start each animation at a random frame and phase so many sketched objects don't wobble in lockstep." |

Public surface:

- `SketchAnimation Current { get; }` — what is playing (null if nothing).
- `void Play(SketchAnimation animation)` — switch to `animation` from its first frame (or random frame/phase if `randomiseStart`). If `animation` is already `Current`, no-op (re-requesting the idle each frame must not restart the wobble). Null or unplayable animation → error logged with object name, `Current` set to null, renderer sprite left as-is; nothing throws in Update.
- `void PlayIdle()` — `Play(idle)`; returns silently if `idle` is null, because `Awake` already reported that fault.

## 6. Behaviour

1. **Awake** — cache `SpriteRenderer`. If `idle` is null → `Debug.LogError("SketchAnimator on '{name}' has no idle animation.", this)`. (Not disabling the component: a later `Play(x)` from gameplay code still works.)
2. **OnEnable** — if `Current == null`, `PlayIdle()`; else re-apply `Current`'s current frame (so a disabled/re-enabled object shows the right drawing immediately). Using OnEnable rather than Start so re-enabling behaves the same as first enable.
3. **Play(animation)** — validation per §5; on success set `Current`, `playhead.Reset(frame, elapsed)` with `frame = randomiseStart ? Random.Range(0, count) : 0`, `elapsed = randomiseStart && fps > 0 ? Random.value / fps : 0`; then apply the frame to the renderer.
4. **Update** — if `Current == null` return. Read `Current.FramesPerSecond` and `FrameCount` **live** (asset edits in Play Mode take effect). `playhead.Advance(Time.deltaTime, fps, count)`. If the frame index changed, `spriteRenderer.sprite = Current.GetFrame(frame)`. Scaled time is used deliberately: there are no timers in this game (Bible §10), and a paused game should freeze the wobble.
5. **Asset edited to have zero frames while playing** — `Update` checks `Current.IsPlayable` each frame; if it stops being playable, log an error once and set `Current = null` (renderer keeps its last sprite). Restored playability requires a `Play` call — acceptable, it's an authoring mistake path, and the error says so.
6. **Sorting / depth** — the project runs URP's **2D Renderer** (`Assets/Settings/UniversalRP.asset` → `Renderer2D.asset`), which orders by sorting layer, then order-in-layer, then distance from the camera. Everything is on the Default layer at order 0, so the player at z −0.5 draws over the sheets at z 0 by camera distance. No sorting layers are introduced; that decision belongs to whoever builds the fold renderer. The `SpriteRenderer` uses built-in `Sprites-Default` (unlit) — the scene has no `Light2D`, and a lit sprite material would render black.
7. **Size** — PPU 2000 makes the 1500×2100 texture 0.75 × 1.05 units; the drawn character (~1135 × 1709 px) is ~0.57 × 0.85 units, matching the previous 0.6-wide placeholder. Adjust by editing PPU on the three textures (or the `Visual` scale) — noted in the report as the place to tune.
8. **Import** — `maxTextureSize 512` gives a 366×512 texture; the character is drawn at ~100 px tall on a 1080p screen (camera shows ~10 units tall), so 512 leaves 5× headroom for zoom-in and mipmaps handle the downscale. 366 is not a multiple of 4, so block compression cannot apply and the texture stays uncompressed RGBA32: ~0.75 MB per frame plus mipmaps, down from ~12 MB. Fine for the WebGL target (Bible §1); do not "fix" this by forcing compression, it cannot take effect at this size.

## 7. Interfaces & seams

- **No interface is introduced.** The seam between *what to play* and *how it plays* is `SketchAnimator.Play(SketchAnimation)`: gameplay code that later owns player state (moving, pushing) calls `Play` with another asset; a state machine, if ever wanted, would be a separate component that calls `Play` — `SketchAnimator` never grows conditions.
- **Rendering seam:** `SketchAnimator` only touches `SpriteRenderer.sprite`. If the fold renderer (Bible §4 `[DECIDE]`) ends up needing objects on the flap to be rendered some other way, that is a change to how the renderer is fed, isolated to the one line that assigns the sprite.
- **Cutting the feature:** delete `Scripts/Sketch/`, `Animations/`, the test file, and put any renderer back on `Visual`. Nothing else references these types.
- **Applying to a new object:** add `SpriteRenderer` + `SketchAnimator`, create a `SketchAnimation` asset via *Create → Papercut → Sketch Animation*, drag frames in, assign as `idle`.

## 8. Testing

- **Edit Mode tests** (`SketchPlayheadTests`): wraps at the end; multi-frame delta advances several frames without drift; fps 0 holds; fps change mid-loop keeps the frame; `frameCount` shrink clamps `Frame`; `frameCount <= 0` throws. Run with the CLI reflection runner per the compile-check memory.
- **CLI compile** of `Papercut.asmdef` sources and the test assembly with Unity's bundled Roslyn (editor is open).
- `python Tools/check_links.py` after the prefab / asset YAML edits (sprite refs are `fileID: 21300000` on each texture GUID; script refs by new script GUIDs).
- **Manual, in Desk scene:** play — Scuffy is visible at the player position in the drawing's own colours (not black), wobbling through three drawings at 6 fps; change `framesPerSecond` on `Scuffy Idle` in Play Mode and see the rate change; move between screens and confirm the sprite follows; toggle `randomiseStart` off, stop/play twice and confirm it always starts on frame 1. Second-animation path: create a throwaway `SketchAnimation` with the frames reversed, call `Play` on it (e.g. from a temporary test component), confirm it switches, then `PlayIdle` and confirm the idle resumes; calling `PlayIdle` again must not restart it.

## 9. Assumptions (engineering)

1. `SpriteRenderer` is the right renderer for hand-drawn objects on the sheet (see §2 on decision #4); the sheets themselves remain meshes.
2. Scaled `Time.deltaTime` drives the loop (see §6.4).
3. `Play` of the already-current animation is a no-op rather than a restart.
4. Frame order is sequential. If the loop ever reads as too regular, a random-order option is a small addition to `SketchPlayhead`, not a redesign.
5. Import: the leftover auto-slice entries in the three `.png.meta` files (`internalIDToNameTable` sub-sprites from an earlier Multiple-mode attempt) are harmless under `spriteMode: 1` and are left alone to keep the diff readable.
6. Default `framesPerSecond` 6 and `randomiseStart` true are starting values only; both are Inspector tunables.
7. The player's `CircleCollider2D` (radius 0.3, centred) is unchanged; whether the collider should sit at the feet of a taller drawing is a gameplay-feel question for when movement/collision is tuned, not part of this task.

## 10. Open questions

None.

## Review

Round 1 (plan-reviewer, isolated): **APPROVE WITH CHANGES**, no blocking items.

| # | Finding | Disposition |
|---|---|---|
| N1 | Project uses URP 2D Renderer; `SpriteRenderer` material unspecified, lit material would render black (no `Light2D`). | **Accepted.** §4 specifies built-in `Sprites-Default`; §6.6 rewritten for the 2D Renderer; manual check for "not black" added. |
| N2 | `SketchPlayhead` internal but tested from the test assembly; no `InternalsVisibleTo`. | **Accepted.** Struct made `public` (one small timing type; not worth an AssemblyInfo). |
| N3 | `IsPlayable` defined two ways. | **Accepted.** Now: `FrameCount >= 1` and all frames non-null. |
| N4 | `OnValidate` error spams while authoring (array resize creates null slots). | **Accepted.** `OnValidate` dropped; the `Play`-time error names the asset. |
| N5 | Null idle logs twice (Awake + PlayIdle). | **Accepted.** `PlayIdle` returns silently when idle is null. |
| N6 | 366×512 can't block-compress; say "uncompressed". | **Accepted.** §6.8 wording. |
| N7 | `randomiseStart` only affects the player today; note for Aaron. | **Noted** for the report; field kept. |
| N8 | No manual check of the second-animation `Play` path. | **Accepted.** Added to §8. |

## Code review

Round 1 (code-reviewer, isolated): **APPROVE WITH FIXES**, nothing must-fix.

| # | Finding | Disposition |
|---|---|---|
| S1 | `SketchPlayhead.Advance` drained `Elapsed` one period per loop iteration; an absurd `framesPerSecond` (e.g. 1e8) could make the loop never terminate. | **Fixed.** Whole frames are stepped in one division; O(1). |
| S2 | `spriteRenderer` cached only in `Awake`; `Play` from another component's `Awake` would NRE. | **Fixed.** `ApplyFrame` fetches the renderer lazily if `Awake` hasn't run. |
| S3 | No test for very high frame rates. | **Fixed.** Added `VeryHighFrameRateTerminatesAndStaysInRange` and `ManyStepsWrapCorrectly`. |
| Q1 | Sprite pivot is the texture centre; drawing's feet sit ~0.4 units below the 0.3-radius collider. | **Escalated to Aaron** in the report (was already plan assumption 7). |

Verification after fixes: CLI compile of all `Assets/Papercut/Scripts` OK (only CS0649 warnings); `SketchPlayheadTests` 11 passed, 0 failed; `Tools/check_links.py` ALL LINKS OK.
