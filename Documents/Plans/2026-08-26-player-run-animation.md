# Plan — Player run animation

Date: 2026-08-26

## 1. Task

Aaron's request (verbatim):

> "I added a pair of images for a run animation for the player character. Can we add these in to the player animations? (Without messing with the ability to make other objects have the line wobble thing that I have going). However we achiev that."

Deliverable:

- The two run drawings (`Assets/Papercut/Sprites/Scuffy Run/PNGSet/PNG_0001..2.png`) become a `Scuffy Run` sketch animation.
- The player plays it while moving and drops back to `Scuffy Idle` when standing still.
- The player mirrors horizontally to face the way he is moving (Aaron's decision, §3).
- `SketchAnimation` / `SketchAnimator` are **not modified**: the generic wobble mechanism stays exactly as it is for other objects.

**Out of scope:** an animation state machine; any other player animation (push, etc.); animating other objects; up/down facing (there is no art for it); anything `[TENTATIVE]`.

## 2. Design references

- Design Doc — *Art* / Pillar 3: sketchy, drawn-on-paper feel. Run frames are the same loop-of-drawings technique as idle.
- Previous plan `2026-08-25-sketch-animation.md` §7: "gameplay code that later owns player state (moving, pushing) calls `Play` with another asset; a state machine, if ever wanted, would be a separate component that calls `Play` — `SketchAnimator` never grows conditions." This task is exactly that separate component.
- Bible §3 movement scheme `[DECIDE]` #12 (analog / 8-way) — *touched, not resolved.* "Moving" is read from `PlayerMover.MoveInput`, which is what any quantising step would also feed, so this closes nothing.
- Bible §4 `[LOCKED]` player is always on the base — the player's sprite is never fold-deformed; flipping it is a plain `SpriteRenderer.flipX`.
- implementation-guidelines §4a — run-detection threshold and the mirror toggle are feel values → Inspector. Frame rate of the run lives on the `Scuffy Run` asset (already an Inspector tunable).
- Glossary — no conflicts.

## 3. Decisions already made by Aaron

- **Q1 (this task, 2026-08-26):** "Should Scuffy mirror horizontally to face the direction he's moving?" — **Aaron: "Mirror when moving left"** (the option text: flip the sprite when horizontal input is left; keep the last facing while idle or moving straight up/down; a flip-on-left toggle is an Inspector setting).
- From the sketch-animation task: the frame loop is driven by the custom `SketchAnimator`, not Unity's Animator.

## 4. Files

Already done (engineering, before planning): `Scuffy Run/PNGSet/PNG_000{1,2}.png.meta` — `enableMipMap` 0→1, `maxTextureSize` 2048→512 (all platform entries), `spritePixelsToUnits` 100→2000, matching the idle frames so the character keeps the same on-screen size and memory footprint. Leftover auto-slice entries left alone, as for the idle frames.

Create:

| File | Purpose |
|---|---|
| `Assets/Papercut/Animations/Scuffy Run.asset` (+ .meta, guid `8aacb55578884382b663af616e74fbbb`) | `SketchAnimation`: the two run frames, `framesPerSecond` 6. |
| `Assets/Papercut/Scripts/Player/PlayerSketch.cs` (+ .meta, guid `3de38a6621d54ed4b1ae32cd357df345`) | Component: picks idle vs run from `PlayerMover` and mirrors the sprite. |

Modify:

| File | Change |
|---|---|
| `Assets/Papercut/Prefabs/Player.prefab` | Add `PlayerSketch` (fileID `1007`) to the root `Player` object, referencing the `Visual` child's `SketchAnimator` (`2003`), `run` = Scuffy Run. |

Not touched: `SketchAnimation.cs`, `SketchAnimator.cs`, `PlayerMover.cs`, `Desk.unity` (the Player instance there only overrides root transform/name).

## 5. Components & data

### `Scuffy Run.asset`

`frames` = [`PNG_0001` (guid `8bf79b72511d5f246afc9490b79d2f3a`), `PNG_0002` (guid `2f383c7a78b19c14691b7a01a81d76bb`)], each `fileID: 21300000`; `framesPerSecond: 6` (Inspector tunable on the asset — idle uses 4; the run has only two frames so a faster rate reads as a run rather than a slow wobble. Starting value only).

### `PlayerSketch : MonoBehaviour` — `[DisallowMultipleComponent]`, `[RequireComponent(typeof(PlayerMover))]`

Responsibility: decide which of the player's sketch animations should be playing and which way the drawing faces. Owns no timing; that stays in `SketchAnimator`.

| Field | Type | Tunable? | Default | Notes |
|---|---|---|---|---|
| `animator` | `SketchAnimator` | — | null (must be set) | `[Header("Drawing")]`. "The SketchAnimator on the player's Visual child. Its idle is the standing animation." |
| `run` | `SketchAnimation` | — | null (must be set) | "Played while the player is moving." |
| `runInputThreshold` | `float` | **Inspector tunable** | `0` | `[Header("Feel")]`, `[Range(0,0.95)]`. "Move input magnitude above which the run plays. At 0 any input that moves the player runs (the Input System stick deadzone is the gate). Raise it to add a slow-stick band where Scuffy drifts without running." |
| `mirrorToFaceLeft` | `bool` | **Inspector tunable** | `true` | "Flip the drawing horizontally when moving left. The drawings are made facing right. Off: always face right." |

Public surface: `bool FacingLeft { get; }` — the component's one piece of persistent state.

The decision rule is a pure `public static SketchChoice Choose(Vector2 input, bool movementEnabled, float threshold, bool facingLeft)` returning `readonly struct SketchChoice { bool Running; bool FacingLeft; }` — same pattern as `SketchPlayhead.Advance` / `ScreenNavigator.EntryPosition`, so it is Edit Mode tested without a scene.

No new interface. `PlayerMover` is read via its existing `MoveInput` and `MovementEnabled`.

## 6. Behaviour

1. **Awake** — cache `PlayerMover`. If `animator` is null → `Debug.LogError("PlayerSketch on '{name}' has no SketchAnimator assigned.", this)` and `enabled = false` (nothing it could do). If `run` is null → `Debug.LogError(... "has no run animation.")` and `enabled = false`. `SketchAnimator` itself already reports an unplayable asset at `Play` time, naming the asset; not duplicated here.
2. **Update** (every frame, after `PlayerControls.Update` has set the input — same phase; a one-frame lag either way is invisible at 6 fps and avoids adding a script-execution-order dependency):
   - `input = mover.MoveInput`.
   - `choice = Choose(input, mover.MovementEnabled, runInputThreshold, FacingLeft)`: `Running = movementEnabled && input.sqrMagnitude > threshold²`. `MovementEnabled` is included so the player does **not** run on the spot while being carried by a screen transition (`PlayerMover` deliberately keeps `MoveInput` while held). Facing: only while running, `input.x < 0` → left, `input.x > 0` → right, `x == 0` (pure vertical) keeps the previous facing.
   - The animation is **requested only when `Running` changes** (tracked in a `bool? requestedRunning`, null until the first Update so the first frame always requests): running → `animator.Play(run)`; else `animator.PlayIdle()`. Requesting on change rather than every frame means an unplayable asset is reported once per state change, not once per frame, and the player's error behaviour stays identical to every other sketched object's.
   - `FacingLeft = choice.FacingLeft`; `spriteRenderer.flipX = mirrorToFaceLeft && FacingLeft`, applied every frame. The renderer is fetched with `animator.GetComponent<SpriteRenderer>()` once in Awake (`SketchAnimator` requires it, so it exists).
   - Reading `runInputThreshold` and `mirrorToFaceLeft` live each frame: Play Mode Inspector edits take effect immediately (§4a).
3. **OnDisable** — nothing. `SketchAnimator` keeps playing whatever was last requested; when `PlayerSketch` is disabled the animator is on its own, which is the same situation as any non-player sketched object. (Not forcing idle on disable: if a future component disables this one to take over the drawing — e.g. a push animation — it must not have its choice overridden.)
4. **Transition edge:** during `ScreenNavigator`'s slide, `MovementEnabled` is false → idle plays and facing is frozen. Movement resumes → the next `Update` picks the run back up.
5. **Failure modes:** missing references — errors in Awake, component disabled, idle keeps playing via `SketchAnimator.OnEnable`. Unplayable `run` (or idle) asset — `SketchAnimator.Play` logs the asset name once per request and sets `Current = null`; the renderer keeps its last drawing until the state next changes. Same behaviour as any other sketched object.
6. **Mirroring consequence (accepted):** `flipX` mirrors about the sprite pivot (texture centre). The two run drawings have different horizontal extents and the root `BoxCollider2D` has `offset.x = -0.03`, so when facing left the drawing sits a few hundredths of a unit differently relative to the collider and the frame-to-frame shimmy runs the other way. At ~100 px on screen this is expected to be invisible; if it reads badly the fix is the sprite pivot / collider offset, not code.

## 7. Interfaces & seams

- The seam is unchanged: `SketchAnimator.Play` / `PlayIdle`. `PlayerSketch` is the "whatever owns the object's state" the previous plan named. It is player-specific by design; a sketched block or NPC would get its own tiny state component (or none), never a shared state machine.
- **Cutting this feature:** delete `PlayerSketch.cs` and `Scuffy Run.asset`, remove the component from the prefab. The player goes back to idle-only; nothing else references either.
- **Facing** is done with `SpriteRenderer.flipX`, not a negative scale, so the `Visual` transform stays clean for whatever the fold renderer later needs.

## 8. Testing

- **CLI compile** (`compile.sh` in the session scratchpad — Unity's bundled Roslyn; the editor is open).
- **Edit Mode tests** (`PlayerSketchTests`, on `PlayerSketch.Choose`): idle when input is zero; runs on cardinal input; runs on a clamped diagonal; `MovementEnabled` false → not running and facing preserved; left input → facing left; right input → facing right; pure vertical keeps the previous facing (both values); input at exactly the threshold does not run. Run with the CLI reflection runner alongside the existing tests.
- `python Tools/check_links.py` after the prefab/asset YAML edits.
- **Manual, Desk scene:** (a) stand still → idle wobble at 4 fps; (b) hold right → two run frames alternating at 6 fps, facing right; (c) hold left → mirrored; (d) release → idle, still mirrored; (e) hold up only → run, facing unchanged from (d); (f) walk into an Exit → during the slide Scuffy idles, and resumes running on arrival if the key is still held; (g) toggle `mirrorToFaceLeft` off in Play Mode while facing left → immediately un-mirrored; (h) set `runInputThreshold` to 1 → a single cardinal key (magnitude exactly 1, not > 1) never runs, confirming the threshold is live (diagonals are clamped and may round above 1; the Edit Mode test covers the boundary exactly).

## 9. Assumptions (engineering)

1. "Moving" is defined from **input**, not from `Rigidbody2D` velocity. Pushing against a wall therefore shows the run (Scuffy tries to go, legs move). Velocity-based would make him stand still against walls; input-based is the usual choice and the seam to change it is the one `running =` line.
2. The two run frames alternate as drawn (frame order 1, 2). `SketchAnimator.randomiseStart` on the player is left `true`, so the run may start on either frame — at two frames that is invisible.
3. Facing is stored on `PlayerSketch` and applied to the renderer every frame, rather than stored on the renderer, so toggling `mirrorToFaceLeft` is reversible and the Visual can be replaced without losing state.
4. Animations are requested on state change, not every frame (§6.2), and re-asserted on the first Update after enable (see Deviations). If another component ever calls `SketchAnimator.Play` directly on the player while this one is enabled, this component will not re-assert its choice until running/idle next flips.
5. `framesPerSecond: 6` for the run and `runInputThreshold: 0` are starting values only; both are Inspector tunables.

## 10. Open questions

None.

## Review

Round 1 (plan-reviewer, isolated): **APPROVE WITH CHANGES**, no blocking items.

| # | Finding | Disposition |
|---|---|---|
| N1 | Manual threshold test flaky on clamped diagonals (sqrMagnitude can round to 1.0000001); default 0.1 sits below the Input System's 0.125 stick deadzone so it is inert. | **Accepted.** Test uses a cardinal key; boundary covered by an Edit Mode test; default 0.2; tooltip says keyboard is always 1. |
| N2 | Every-frame `Play`/`PlayIdle` turns a once-logged unplayable asset (idle or run) into a per-frame error on the player only. | **Accepted.** Animation requested only when running/idle changes (§6.2). |
| N3 | Rejecting a pure `Decide` function as a "config surface" was wrong; it is the project's existing testable-rule pattern. | **Accepted.** `Choose` added with Edit Mode tests; plan text cleaned up. |
| N4 | `flipX` about the centre pivot + differing frame extents + collider `offset.x` -0.03 -> small shift when facing left. | **Accepted** as a noted art/pivot consequence (§6.6). |
| N5 | `IsRunning` has no consumer. | **Accepted.** Dropped; `FacingLeft` kept. |
| N6 | Run PNG_0001 meta carries ~21 KB of leftover auto-slice entries; harmless. | **Noted** for the report. |
| Q1 | Input- vs velocity-driven "running" is player-facing feel once pushables exist. | **Flagged in the report** as a one-line reversible assumption (§9.1) rather than blocking: input-driven is the conventional answer and nothing pushable exists yet. |

## Deviations

- `OnEnable` resets `requestedRunning` to null so the first Update after a re-enable re-requests the right animation. The plan (§6.3, §9.4) implied the choice would only be re-asserted on the next running/idle flip; re-asserting on enable is the safer contract (a re-enabled component immediately owns the drawing again) and §9.4 now says so.
- `runInputThreshold` default 0.2 → 0 and range capped at 0.95 (code review S1/S3).

## Code review

Round 1 (code-reviewer, isolated): **APPROVE WITH FIXES**, nothing must-fix.

| # | Finding | Disposition |
|---|---|---|
| S1 | Threshold 0.2 vs the 0.125 stick deadzone leaves a band where the player moves at up to 20% speed while idling. | **Fixed.** Default 0; tooltip explains the deadzone is the gate and what raising it does. |
| S2 | `OnEnable` reset not in the plan; contradicts §9.4. | **Fixed.** Recorded under Deviations; §9.4 corrected. |
| S3 | `Range(0,1)` lets the slider top silently disable running (strict `>` at 1). | **Fixed.** `Range(0, 0.95)`. |
| S4 | Leftover auto-slice entries in `PNG_0001.png.meta` should be cleared while touching the file. | **Rejected.** Harmless under `spriteMode: 1`; hand-editing an importer's sub-sprite table risks a reimport diff for no functional gain, and the idle metas carry the same entries — one deliberate cleanup pass in the editor later. |
| Q1 | Input- vs velocity-driven running. | **Flagged in the report** (same as plan review Q1). |
| Q2 | Should any stick band move-without-running exist? | **Answered by S1's fix:** none by default; the tunable remains for Aaron. |

Verification after fixes: CLI compile OK; `PlayerSketchTests` 10/10 pass (other failures belong to the concurrent Fold task); `Tools/check_links.py` — Player.prefab clean, the only failures are the Fold task's missing script guid in the Sheet prefabs.
