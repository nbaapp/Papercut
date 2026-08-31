# Plan: player push animation (and confirm replaced run frames)

## 1. Task
Aaron replaced the run frames under `Assets/Papercut/Sprites/Scuffy Run/PNGSet` and added three push frames under `Assets/Papercut/Sprites/Scuffy Push/PNGSet`. Wire both into the player's sketch animations so they play correctly.

Out of scope: any change to `SketchAnimator`/`SketchAnimation` playback, to `BlockPusher` push mechanics, or to how facing is chosen.

## 2. Design references
- Design Doc, Pillar 3 (world of paper that acts like paper; sketchy art) - the sketch wobble system exists for this.
- Bible §9 `[TENTATIVE]`: pushable blocks (`PushableBlock`, `BlockPusher`) are playtest elements that may be cut. The push animation is *content for that feature* and must be cuttable by deleting `BlockPusher` (and the push asset) without touching core code paths. Aaron asked for this explicitly, so it is built.
- No `[DECIDE]` items are touched. Facing rule and the sketch system are already built and unchanged.

## 3. Decisions already made by Aaron
- "I replaced the frames for the run animation, and I also added some frames for a push animation as well. Can you plop those into the player animations so that they work correctly?" - i.e. run uses the new frames; push frames become the player's push animation.

## 4. Files
- `Assets/Papercut/Animations/Scuffy Run.asset` - **no change needed**: the replaced PNGs kept their `.meta` GUIDs (`8bf79b72...`, `2f383c7a...`), both are still `spriteMode: 1` (single sprite, `fileID 21300000`), and the asset already references them. Verified, not edited.
- `Assets/Papercut/Sprites/Scuffy Run/PNGSet/*.png.meta` and `Scuffy Push/PNGSet/*.png.meta` (5 files) - the replaced/new PNGs re-imported with Unity defaults (`spritePixelsToUnits: 100`, `maxTextureSize: 2048`, `enableMipMap: 0`) while idle is `2000` / `512` / mipmaps on. All frames are 1500x2100 px, so at 100 PPU the run/push drawings would be 20x the idle size. Set the three values to match idle.
- `Assets/Papercut/Animations/Scuffy Push.asset` (+ `.meta`, new GUID) - new `SketchAnimation`: frames PNG_0001, PNG_0002, PNG_0003 (guids `defe1f19...`, `a514fd64...`, `e064bbb2...`), `framesPerSecond: 6` (matches run; adjustable on the asset).
- `Assets/Papercut/Scripts/Player/PlayerSketch.cs` - add a `push` animation reference and the rule that push wins over run while a block is being pushed.
- `Assets/Papercut/Prefabs/Player.prefab` - assign `push` on the `PlayerSketch` component (`&1007`, fileID 11400000 of the new asset).
- `Assets/Papercut/Tests/EditMode/PlayerSketchTests.cs` - tests for the new rule.

## 5. Components & data
### `PlayerSketch` (changed)
- New serialized field: `[SerializeField, Tooltip("Played while the player is pushing a block (needs a BlockPusher on the player). Optional: left empty, pushing shows the run.")] SketchAnimation push;`
- New private: `BlockPusher pusher;` obtained in `Awake` via `TryGetComponent` - **optional**, never required. `PlayerSketch` must not `[RequireComponent(typeof(BlockPusher))]`, so deleting the block feature deletes nothing here.
- New `enum SketchState { Idle, Running, Pushing }`. `SketchChoice` becomes `(SketchState State, bool FacingLeft)` with `Running`/`Pushing` convenience getters. `PlayerSketch` keeps `SketchState? requested` in place of `bool? requestedRunning`.
- `Choose` signature becomes `Choose(Vector2 input, bool movementEnabled, bool pushing, float threshold, bool facingLeft)`. Existing tests updated to pass `pushing: false`.
- Inspector tunables added: none numeric on the component. `Scuffy Push.asset` -> `framesPerSecond` default 6 (feel value, on the asset like run/idle).

## 6. Behaviour
Each `Update`:
1. `pushing = pusher != null && pusher.Pushing != null` (BlockPusher already returns null when movement is disabled, so a held player never shows push).
2. `Choose(...)`: `moving` as the old `running`. State = Pushing if `pushing` and movement is enabled and there is any input (BlockPusher's own `pushInputThreshold` is the gate, not `runInputThreshold`, so a moving block always shows the push; the any-input check stops the one-step-stale flag playing a push from a standstill on release). Facing follows horizontal input while moving or pushing.
3. State = Pushing if pushing, else Running if moving, else Idle. When the state differs from the last requested one: Pushing -> `animator.Play(push)` **if `push != null`, else `animator.Play(run)`** (documented fallback so an unassigned push is not a hard error on a cuttable feature); Running -> `Play(run)`; Idle -> `PlayIdle()`.
4. `SketchAnimator.Play` handles an unplayable asset (logs an error, keeps last drawing) - no change.
- Pushing against a wall: `BlockPusher.Pushing` stays set, so the push loop plays while straining. Consistent with the push-slowdown rule already in BlockPusher.
- Push ends (block moves away / input released) -> state returns to Running or Idle next Update; `SketchAnimator` restarts run from a (random) frame as it does today.
- `OnEnable` resets `requested = null` as before.

## 7. Interfaces & seams
- No interface. Cut path: delete `BlockPusher`/`PushableBlock` -> `pusher` is null -> push never chosen; then delete the `push` field/asset at leisure. Nothing in fold/occlusion code changes.
- `PlayerSketch` remains the single place that decides what the player's animator plays.

## 8. Testing
- Edit Mode tests on `Choose`: pushing+input -> Pushing; pushing with movement disabled -> Idle; pushing with zero input -> Idle; pushing left -> faces left; pushing vertically keeps facing; not pushing behaves exactly as before (existing tests updated).
- CLI compile of runtime + editmode test assemblies with the bundled Roslyn (per memory), and run the EditMode test dll with the reflection runner.
- Manual (Aaron, editor open): Desk scene, walk into a block -> push loop plays; release -> run/idle; check `Scuffy Push` asset frames show 3 filled slots. Idle, run and push drawings are the same on-screen size. Hold a push for several seconds (block moving, and block against a wall) - the push loop must not stutter/restart. Clear `push` on the prefab: pushing shows the run with no error logged. (The `Update` dispatch - push plays `push`, null push falls back to run, push-to-run transition - is only covered by these manual steps.)

## 9. Assumptions (engineering)
- Push frame order is PNG_0001 -> 0002 -> 0003.
- Push frames are drawn facing right like the others (viewed by the plan reviewer: all face right; push is a side-view lean), so the existing `flipX` mirroring applies.
- The run asset needs no edit because the GUIDs survived the file replacement (verified in the `.meta` files).
- `PlayerSketch` reads `BlockPusher.Pushing` (set in FixedUpdate, read in Update) - one-frame latency at most, same as BlockPusher's own contact latency; acceptable.

## 10. Open questions
None. The fallback in §6.3 (push unassigned -> run) is engineering, not design: it exists only so `PlayerSketch` never errors because a `[TENTATIVE]` feature's asset is absent.

## Review (plan-reviewer, round 1) - verdict BLOCK, all findings resolved
- **B1 (blocking, accepted):** run/push `.png.meta` import settings were Unity defaults (100 PPU / 2048 / no mipmaps) vs idle's 2000 / 512 / mipmaps, so the drawings would be 20x too big. Fixed: the five metas now match idle (section 4). Manual test added: idle/run/push are the same on-screen size.
- **N1 (accepted as a manual test):** contact flicker could make push/run swap and restart at random frames. Added to section 8 manual steps; no hold-time tunable pre-added.
- **N2 (accepted):** `Update` dispatch (push plays `push`, null push falls back to run, push-to-run transition) is stated explicitly as manual verification in section 8.
- **N3 (accepted):** struct shape fixed in section 5 before coding.
- **N4 (accepted):** texture size/mipmaps matched to idle along with B1; flagged to Aaron (Q2).
- **N5 (accepted):** frames were viewed by the reviewer - all face right; push is a side-view lean. Recorded in section 9.
- **Q1 (push loop while the block is stuck against a wall):** proceeding with the plan's choice (push plays while pushing input is held, matching `BlockPusher`'s slowdown-while-stuck rule); flagged to Aaron in the report as reversible.
- **Q2 (2048/no-mipmap import was probably accidental):** matched everything to idle's deliberate 2000 PPU / 512 / mipmaps; flagged to Aaron in the report.

Second round not run: the only blocking change was asset import values, not a design change.

## Deviations
None.

## Code review (code-reviewer, round 1) - verdict APPROVE WITH FIXES
- **S1 (accepted):** push was gated on `runInputThreshold`, which can disagree with `BlockPusher.pushInputThreshold` (block slides while Scuffy stands). Fixed: push needs only movement enabled + any input; the block feature's own threshold gates it. Test `PushingBelowRunThresholdStillPushes` added.
- **S2 (accepted):** `Choose` doc comment overstated the stale-flag guarantee; reworded to name the redirect-along-face case (covered by the manual hold test).
- **S3 (accepted):** trailing space after `m_EditorClassIdentifier:` in the new asset stripped.
- **Q1 (design, flagged to Aaron):** vertical pushes play the side-view lean mirrored to the last facing. Kept as-is (option a) pending Aaron.
- **Q2:** restated plan Q1/Q2; both flagged in the report.
- Reviewer ran compile + tests: 170 passed. Re-run after fixes: see report.

## Deviations (post-review)
- `Choose` gates push on any input + movement enabled instead of the run threshold (S1).
