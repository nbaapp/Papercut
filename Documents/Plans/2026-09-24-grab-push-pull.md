# Hold to push and pull blocks (hold Space)

Date: 2026-09-24

## 1. Task

Aaron: *"Can you change the control scheme for pushing instead of being automatic recquire pressing down and holding space? When you do that, there should be a different animation, not the push one (since that one is for moving), which for now you can just use the first frame of the push animation as a placeholder. Then when the player starts pushing the block, the normal animation plays. The reason for this change is I want to allow pulling as well, so while the player is holding space, they should be able to pull the block as well. Pushing and pulling means only two directions, perpendicular to the side of the block the player is on."*

Deliverables:

1. Walking into a pushable block no longer moves it: a block is a wall until the player **holds Space** (the `Player` map's existing `Interact` action: Space / gamepad South) while touching one of its faces. That **holds** the block. (One word for it throughout: *hold* — the Fold map already has a `Grab` action for the mouse.)
2. While holding, the player moves only along the axis perpendicular to the held face: **push** (toward the block) moves the block ahead of them, **pull** (away from it) drags the block after them, at the block's push speed. Input along the face is ignored (Aaron, answer 2).
3. A **Hold** drawing (placeholder: the first frame of `Scuffy Push`, as a one-frame `SketchAnimation` asset `Scuffy Hold`) plays while a block is held and not moving; the **push loop** plays while the held block moves, pushed or pulled; the drawing faces the block the whole time (Aaron, answer 3).
4. Tests for the new pure rules; Bible §5 updated.

**Out of scope:** a pull drawing (Aaron: push loop for both until one exists); a hold reach from a distance beyond touching (asked, §10 Q2); a drawing for the block itself while held; gamepad tuning beyond the binding that already exists; any change to what a block does under folds (climb, descend, ride, reset — all unchanged, a pull just moves the rect the other way); the Sheet Studio.

## 2. Design references

- Design Doc *Mechanics → Folding → Valid Folds*: "partially covered moveable objects can be pushed fully under, or **pulled out** then pushed back on top of the fold". Pulling has been in the design from the start; this builds it. *Unlockables*: "Push/Pull blocks" — one ability, `Ability.Push`, already gates every block (Bible §9, 2026-09-17); it gates the hold too (a block the player may not push cannot be held and stays a wall). No new ability.
- Bible §9 **[TENTATIVE]** pushable blocks (`PushableBlock`, `BlockPusher`): built on Aaron's request 2026-08-27; this changes how the player drives them, on Aaron's request today. Cutting is unchanged: delete the block components and prefabs; `BlockPusher` and the Hold animation go with them. Nothing in fold, occlusion or collision code learns about holding.
- Bible §5 *Movable objects* **[DECIDED 2026-08-27]**: a block partly under a Flap belongs under it and can be pushed fully under; one wholly clear pushed into a Flap's edge climbs on top; blocks stop at walls, gated terrain, sheet edges and other blocks; they roll across creases. A pull is the same move with the block's cardinal reversed, so every one of these rules applies unchanged through `PushableBlock.TryPush` (which already ignores the pusher's body as an obstacle and already accepts any cardinal). Pulling a partly covered block out from under a Flap is the Design Doc's "pulled out"; pushing it back in then climbs, as today.
- Bible §4 *Validity* **[LOCKED]**: the player is always on the Base (or a landed Flap's walkable surface), never carried. Unchanged; the hold is released the moment the block stops being pushable (covered, gone, sheet no longer the Screen) or the player's movement is held (screen transition).
- Bible §1 Input **[DECIDED provisional]**: Unity Input System, `Player` map. The `Interact` action (Space, gamepad South) exists in the asset and was unused by code; it becomes the hold key. No new binding. Its `initialStateCheck` is off: Space already down when the map enables is not seen until it is pressed again (acceptable).
- Bible §3 movement scheme **[DECIDE] §11.12** (analog / 8-way): untouched. While holding, movement is the input's component along one axis; the free-movement rule is whatever `PlayerMover` does when nothing is held.
- `[DECIDE]` items resolved: none. Gaps: the Bible does not say what sideways input does while holding, or which way the drawing faces while pulling — both asked (§3).

## 3. Decisions already made by Aaron (2026-09-24)

Pre-plan questions and answers:

1. *Full coding workflow or lightweight?* — **Full workflow.**
2. *While Space is held on a block, what does input along the block's face (sideways) do?* — **"Ignored, stay grabbed."** Only the perpendicular component moves anything; a sideways press does nothing until Space is released.
3. *Which way does the drawing face while pulling, and what plays?* — **"Face the block, push loop."** Grabbing a block on the left faces left the whole time, pulling included; the push loop plays whenever the block moves, push or pull, until a pull drawing exists.

Post-review questions and answers (2026-09-24):

4. *While Space is held and the player pushes into a block that cannot move, which drawing plays?* — **"Push loop as a strain."** Pressing into a stuck block plays the push loop anyway, as the old automatic push did. So the loop follows the *pressing*, not the block's motion: it plays whenever the input along the hold axis is past the threshold, pushing or pulling, whether or not the block moved.
5. *Touching only, or a short reach?* — **"Touching only."** Walk into the block, hold Space; the contact-offset probe covers standing still against it. A reach may come later as an Inspector distance.

From the task text: the hold drawing is, for now, the first frame of the push animation; the push loop is "for moving" (plays while the block moves); the hold is Space held, not tapped.

## 4. Files

### Runtime (`Assets/Papercut/Scripts`)

| File | Change | Purpose |
| --- | --- | --- |
| `Objects/PushRules.cs` | modify | Two new pure rules: `HoldInput` (input projected onto the hold axis with the threshold; push / pull / none), `FollowInput` (the move input that carries the player exactly as far as the block went) and `HoldBroken` (the separation-drift release rule). Class summary updated (no longer "the pusher's walk"). |
| `Objects/BlockPusher.cs` | rewrite (same component, same prefab slot) | The hold: reads the hold key from `PlayerMover`, holds the touched block, moves it along its axis both ways, drives the player's movement override and speed scale, reports its state to `PlayerSketch`. |
| `Objects/PushableBlock.cs` | doc only | Class summary ("by walking into it" → held and pushed or pulled) and `TryPush` remarks: a cardinal pointing at the pusher is a pull; the pusher's body is already ignored as an obstacle. No code change. |
| `Player/PlayerMover.cs` | modify | `InteractHeld` input state next to `MoveInput` (so the input layer never names the block feature); `MoveOverride` state (like `SpeedScale`): while set, this step moves by it instead of the player's input; `EffectiveMoveInput` for readers. `SpeedScale` remarks updated. |
| `Player/PlayerControls.cs` | modify | Reads the `Interact` action every frame into `PlayerMover.InteractHeld`. Knows nothing about blocks. |
| `Desk/SheetEdge.cs` | modify | Travel decided from `PlayerMover.EffectiveMoveInput`, not the raw input, so a sideways press while holding a block beside a sheet edge does not travel (Aaron: sideways does nothing). |
| `Player/PlayerSketch.cs` | modify | New `Holding` sketch state and `hold` animation field; `Choose` takes the hold state; facing follows the held block. Class summary updated. |

### Assets

| File | Change | Purpose |
| --- | --- | --- |
| `Assets/Papercut/Animations/Scuffy Hold.asset` (+ .meta, new guid) | new | One-frame `SketchAnimation`: the first frame of `Scuffy Push` (sprite guid `defe1f191792f0341b4263f0ba17e1e8`), `syncToClock` off, 4 fps (irrelevant with one frame). Placeholder until Aaron draws a hold. |
| `Assets/Papercut/Prefabs/Player.prefab` | modify (through the editor, `SerializedObject` on the prefab asset — see memory `unity-cli-pipeline`) | `PlayerSketch.hold` = `Scuffy Hold`. The Test Desk and Desk Player instances inherit it. |
| `Assets/InputSystem_Actions.inputactions` | none | `Interact` already bound to Space and gamepad South. |

### Tests (`Assets/Papercut/Tests/EditMode`)

| File | Change |
| --- | --- |
| `PushRulesTests.cs` | `HoldInput` (push / pull / dead band / sideways-only / negative axis: block on the left, input left ⇒ push, input right ⇒ pull), `FollowInput` (magnitude, round-trip `FollowInput(d, moved, speed, dt) * speed * dt == moved`, clamp to 1, zero speed / dt / moved), `HoldBroken` (inside / outside the tolerance, both signs). |
| `PlayerSketchTests.cs` | Existing tests moved to the new `Choose` signature (not holding ⇒ never Pushing); new: Holding when held and still, Pushing when pressing along the axis (push or pull, block moved or not), stale-flag guard, sideways-only input ⇒ Holding, facing the block left/right, vertical hold keeps facing, movement held ⇒ Idle even when holding. |

### Docs

| File | Change |
| --- | --- |
| `Documents/Claude Bible.md` | §5 *Movable objects*: the hold scheme (hold Space; push/pull along the face axis; sideways ignored; face the block; hold vs push drawing) with Aaron's words and the date. §1 Input: `Interact` is the hold key. §9: `BlockPusher` is the hold. |

## 5. Components & data

### `PushRules` (static, pure) — additions

```csharp
/// Input along the hold axis. `axis` is the unit cardinal from the player into the block. Returns the input to
/// move by: axis * a where a = Dot(input, axis), if |a| >= threshold; otherwise zero (the dead band: the player
/// stands still and the block does not move). `sense` is +1 pushing, −1 pulling, 0 in the dead band.
public static Vector2 HoldInput(Vector2 input, Vector2 axis, float threshold, out int sense);

/// The move input that carries the player exactly `moved` units along `direction` this step at `speed` units
/// per second over `dt`: direction * (moved / (speed * dt)), clamped to unit length. Zero if speed or dt is
/// not positive or moved is not positive.
public static Vector2 FollowInput(Vector2 direction, float moved, float speed, float dt);

/// True when the separation between player and block along the hold axis has drifted more than `tolerance`
/// from what it was when the hold began: something other than the hold moved one of them, so the hold lets go.
public static bool HoldBroken(float gapNow, float gapAtHold, float tolerance);
```

`TryCardinal`, `IntoBlock`, `PushDistance`, `ClampToHit`, `CanPush` unchanged.

### `BlockPusher` (on the Player; `RequireComponent(PlayerMover, Rigidbody2D, PlayerAbilities)`, execution order −10, runs before `PlayerMover`)

Responsibility: while the hold key (`PlayerMover.InteractHeld`) is down and the player touches a pushable block's face, hold that block and move it and the player together along the face's axis.

Serialized (Inspector tunables, `[Header("Hold")]`):

| Field | Default | Meaning |
| --- | --- | --- |
| `pushInputThreshold` (kept) | 0.3 | Move input component along the hold axis, toward or away from the block, needed to move it. Below it the player and block stand still. |
| `pushFaceAxisFraction` (kept) | 0.9 | How axis-aligned the contact must be to count as a block face. |
| `holdBreakDistance` (new, `[Min(0)]`) | 0.1 | If the block ends up this much nearer or farther along the hold axis than when it was taken hold of (something moved one of them — a fold re-placed the block, the player was stopped by a crease the block rolled across), the hold lets go instead of dragging across the gap. |

Derived constant, not a tunable: `PullSkin = 2f * Physics2D.defaultContactOffset` (the block stops a pull that far short of where physics stops the player, so a pull into a wall never wedges the block into them). The block's own Inspector `skin` is the gap *it* keeps from what it is pushed against — an authoring-facing value, since blocks must be authored at least that far from walls; the pull skin is a physics-engine fact, hence derived.

Public surface (read by `PlayerSketch`):

- `PushableBlock Held` — the held block, or null.
- `Vector2 HeldDirection` — unit cardinal from the player into the held block (desk space). Zero when nothing is held.
- `int HeldSense` — this step's input along the hold axis: +1 pressing into the block (push), −1 away (pull), 0 in the dead band. From `PushRules.HoldInput`. 0 when nothing is held.

Private state: `PlayerMover mover`, `Rigidbody2D body`, `PlayerAbilities abilities`, `List<ContactPoint2D> contacts`, `List<RaycastHit2D> hits`, `Rigidbody2D heldBody`, `float holdGap` (separation along the axis when the hold began).

### `PlayerMover` — additions

```csharp
/// The interact key, held or not, as last read by PlayerControls. Kept even while movement is held.
public bool InteractHeld { get; private set; }
public void SetInteractInput(bool held);

/// State, not a tunable: while set, this step moves by this input (magnitude 0..1) instead of the player's own.
/// Set each physics step by whatever steers the player (BlockPusher while a block is held: the input along the
/// block's axis, or nothing) and cleared to null by the same thing.
public Vector2? MoveOverride { get; set; }

/// What this step actually moves by: the override if one is set, else the player's input.
public Vector2 EffectiveMoveInput => MoveOverride ?? moveInput;
```

`FixedUpdate` uses `EffectiveMoveInput`. `MoveInput` (the raw input) stays for readers that want what the player is pressing (`PlayerSketch`'s stale-flag guard). `SheetEdge` switches to `EffectiveMoveInput`: travel is decided by what the player is actually moving by, so a sideways press while holding does not travel.

### `PlayerControls` — additions

`const string InteractActionName = "Interact"`; `InputAction interact`. `Update`: `mover.SetInteractInput(interact.IsPressed())`. `OnDisable`: `mover.SetInteractInput(false)`. A missing `Interact` action is an error like a missing `Move` (throwIfNotFound), because the asset ships with it. `PlayerControls` never names the block feature.

### `PlayerSketch` — changes

- `SketchState` gains `Holding` (between Running and Pushing).
- New serialized field under *Drawing*: `hold` (`SketchAnimation`, tooltip: "Played while the player holds a block that is not moving (needs a BlockPusher). Optional: left empty, holding shows the idle."). `push`'s tooltip becomes "Played while a block the player holds is moving, pushed or pulled. Optional: left empty, shows the run."
- `Choose` signature:

```csharp
/// holding: a block is held (BlockPusher.Held != null). heldPressing: the player is pressing along the hold axis past the threshold this step (BlockPusher.HeldSense != 0), whether or not the block could move (Aaron, answer 4: the loop is a strain too).
/// heldX: sign of the held direction's x (−1 block on the left, +1 on the right, 0 above/below).
public static SketchChoice Choose(Vector2 input, bool movementEnabled, bool holding, bool heldPressing, float heldX, float threshold, bool facingLeft);
```

Rule: movement held ⇒ Idle, facing kept. Else if holding: facing = heldX < 0 ? left : heldX > 0 ? right : kept; state = (heldPressing && input.sqrMagnitude > 0) ? Pushing : Holding (the flags are a physics step old; on releasing the move key the loop must not play a frame from a standstill — the same guard as before; a sideways-only input is Holding, since `heldPressing` is false). Else: Running / Idle exactly as now, facing from the horizontal input while moving. `Pushing` never arises without `holding`. A blocked push or pull (block or player stopped) still plays the loop, as a strain (Aaron, answer 4).

`Update`: `Holding` ⇒ `animator.Play(hold != null ? hold : idle)` via `PlayInIdle`-style fallback (`hold` null ⇒ `animator.PlayIdle()`); `Pushing` ⇒ `push ?? run` as now.

## 6. Behaviour

Each physics step, `BlockPusher.FixedUpdate` (before `PlayerMover`):

1. `HeldSense = 0`. If `!mover.MovementEnabled` (screen transition): release (below) and return.
2. **Not holding:** `mover.MoveOverride = null`, `mover.SpeedScale = 1`. If `mover.InteractHeld`: scan `body.GetContacts` as today for the first contact whose collider's rigidbody has a `PushableBlock` that `IsVisible` and `CanBePushedBy(abilities)`, whose contact normal oriented into the block (`IntoBlock`) is a face (`TryCardinal`). No move input is needed. **Fallback when no contact qualifies** (a resting contact may have lapsed while the player stood still): `body.Cast` of length `Physics2D.defaultContactOffset` along each of the four cardinals, the direction of the player's move input first (else right, left, up, down); a hit whose rigidbody is a qualifying block whose hit normal is a face counts the same way. On a hit: `Held = block`, `heldBody`, `HeldDirection = cardinal`, `holdGap = Dot(heldBody.position − body.position, cardinal)`. Then fall through to *holding* this same step. Touching only (Aaron, answer 5).
3. **Holding:** release and return (to step 2's not-holding reset) if any of: `!mover.InteractHeld`; `Held == null` (destroyed) or `!Held.isActiveAndEnabled` or `!Held.IsVisible` or `!Held.CanBePushedBy(abilities)` or `!Held.Sheet.IsScreen`; `PushRules.HoldBroken(Dot(heldBody.position − body.position, HeldDirection), holdGap, holdBreakDistance)`.
4. `mover.SpeedScale = Held.PushSpeedFactor`. `var move = PushRules.HoldInput(mover.MoveInput, HeldDirection, pushInputThreshold, out sense)`.
   - `sense == 0`: `mover.MoveOverride = Vector2.zero` (stand still, hold). `HeldSense = 0`. Done.
   - `sense == +1` (push): `wanted = PushDistance(move * speed * scale, HeldDirection, dt)`; `Held.TryPush(HeldDirection, wanted, abilities, body, out moved)`.
   - `sense == −1` (pull): the player must be able to walk back that far first — `body.Cast(−HeldDirection, filter{useTriggers=false}, hits, wanted)`, ignoring hits on `heldBody` **and hits on terrain the player may pass** (`hit.collider.GetComponentInParent<TerrainRegion>()` with `IsPassableBy(abilities)` true — passability is applied with `Physics2D.IgnoreCollision` pairs, which casts do not honour, so without this a swimmer pulling a block back into Water would be stopped as if by a wall while walking back into it works); `wanted = ClampToHit(wanted, nearest, PullSkin)`. Then `Held.TryPush(−HeldDirection, wanted, abilities, body, out moved)`. The block's own cast inside `TryPush` is unchanged: blocks stop at gated terrain by decision (Bible §5), so a block is never pulled into Water.
   - Either way: `HeldSense = sense`; `mover.MoveOverride = PushRules.FollowInput(sense * HeldDirection, moved, mover.MoveSpeed * mover.SpeedScale, dt)`. The player goes exactly as far as the block did, so a block stopped by a wall stops the player too (no pressing into it) and a pull never opens a gap.
5. **Release:** `Held = null`, `heldBody = null`, `HeldDirection = zero`, `HeldSense = 0`, `mover.MoveOverride = null`, `mover.SpeedScale = 1`. `OnDisable` releases as well.

Without the hold key a block is a wall to the player with no slowdown (as a block the player may not push is today). The `Pushing` slowdown now applies only while holding.

`PlayerControls.Update` reads `Interact.IsPressed()` each frame into the mover; a release is seen by the next physics step. `PlayerSketch.Update` reads `Held`, `HeldSense`, `HeldDirection.x` and the raw `MoveInput`, one frame behind physics like today.

Edge cases:

- **Two blocks touched:** the first qualifying contact wins, as pushing did.
- **Sideways press beside a sheet edge:** `SheetEdge` reads the effective input (zero while holding still), so the player does not travel mid-hold. Pulling *along* the edge axis into the edge strip travels as walking there would (the player moves as far as the block did, and the block can follow); the hold releases with the transition (movement held) and the block resets with the sheet, as any block does. Pushing a block into the sheet edge does not travel: the block cannot leave the sheet, so `moved` is 0 and the player stands still, held (code-review Q1 for Aaron: stay held as now, or release and travel?).
- **Push across a crease:** blocks roll across creases but the crease is a wall to the player on the lifted side, so a push whose block crosses one while the player is stopped by it opens a gap each step until `holdBreakDistance` releases the hold (up to 0.1 past where the player stopped). Same order of effect as the old pusher, which pushed from behind a stopped player too; stated so it is not read as a bug.
- **Diagonal contact (corner):** not a face (`TryCardinal`) ⇒ no hold.
- **Block covered mid-hold / fold lifts it away:** `IsVisible` false or the body re-placed ⇒ released next step; the player stands where they are.
- **Block on a Flap pulled toward the player on the Base:** `TryPush`'s existing descend rule (centre leaves the Flap ⇒ reglued to what is under it).
- **Player on a landed Flap pulling a clear block toward it:** the existing climb rule (`ClimbOnto`), as pushing it there would.
- **Pull with a wall behind the player:** the player cast clamps the block; both stop, hold stays. Passable terrain behind the player (Water with Swim) does not clamp.
- **Block can move less than the player could (flat-space bound at a crease from inside a fold, sheet edge):** `FollowInput` moves the player only as far as the block went; hold stays.
- **Push speed factor 0:** `FollowInput` returns zero (speed 0), player and block still. Not an error.
- **Missing `Interact` action:** `FindAction(throwIfNotFound: true)` in `PlayerControls.Awake`, as for `Move`. **Missing `hold` animation:** holding shows the idle (optional, like `push`). **No `BlockPusher` on the player:** `PlayerControls` and `PlayerSketch` skip it (both already treat the block feature as optional).
- **Hold key down while touching nothing:** nothing happens; free movement.

## 7. Interfaces & seams

No new interface. The seam is unchanged: `PushableBlock.TryPush(cardinal, distance, pusher, pusherBody, out moved)` is the only way a block moves, and it already accepts a cardinal pointing at the pusher and ignores the pusher's body. `BlockPusher` is the one place that turns input into block moves; `PlayerMover.MoveOverride` and `SpeedScale` are the two pieces of per-step steering state anything on the player may set, and `InteractHeld` is plain input state like `MoveInput`. Cutting the block feature: delete `PushableBlock`, `BlockPusher`, `PushRules`, the Block prefabs, `Scuffy Hold`/`Scuffy Push`; `PlayerMover` and `PlayerSketch` keep their optional hooks (an unused override, an unread key, an empty `hold` slot), which is how they already treat `push`; `PlayerControls` needs no change at all.

## 8. Testing

Edit Mode (pure, `Assets/Papercut/Tests/EditMode`):

- `PushRulesTests`: `HoldInput` — input into the block above threshold ⇒ axis component, sense +1; away ⇒ sense −1; sideways-only ⇒ zero, sense 0; diagonal keeps only the axis component; below threshold ⇒ zero. `FollowInput` — moved 0.04 at speed 2 over 0.02 s ⇒ magnitude 1 along direction; smaller moves scale down; speed 0 / dt 0 / moved 0 ⇒ zero; never above 1.
- `PlayerSketchTests`: existing cases rewritten with `holding: false` (unchanged outcomes; `Pushing` no longer reachable without holding — the old `PushingWith…` tests become hold tests); new: holding + not pressing ⇒ Holding; holding + pressing + input ⇒ Pushing (the block need not have moved); holding + pressing + zero input ⇒ Holding (stale flag); holding + sideways-only input (not pressing) ⇒ Holding; holding a block on the left with input right (pulling) ⇒ FacingLeft; holding a block above ⇒ facing kept; movement held + holding ⇒ Idle, facing kept.

Mechanical, through the Unity CLI (no Play Mode — not opted in): `recompile` clean; collision check (not playing, no test run, not compiling, scene not dirty); `run_tests {"mode":"editor"}` with the summary and every failure reported; `Scuffy Hold` loaded through `AssetDatabase` and its frame confirmed as `Scuffy Push`'s first; `Player.prefab`'s `PlayerSketch.hold` read back through `get_serialized_fields`/eval; both Desk scenes' Player instances checked for a stale override on `hold`.

**Aaron plays it** (nothing above can judge these):

- Hold feel: does holding Space against a block read as taking hold of it; is the one-step contact latency noticeable; does the hold take when standing still against a block after walking into it (the cast fallback should make it).
- Push and pull along the face axis; sideways input truly inert; releasing Space frees the player at once.
- Pull out from under a Flap and push back on top (the Design Doc's sequence); pull a block off a Flap; pull one onto a Flap while standing on it.
- Pull with a wall behind: both stop cleanly, no jitter, no gap.
- Hold frame vs push loop, facing the block while pulling; whether `pushInputThreshold` 0.3 and `holdBreakDistance` 0.1 feel right.
- Swim + Water: pull a block backwards while standing at Water's edge; the player wades back, the block follows to the water's edge and stops there (blocks never enter gated terrain).
- Standing in a sheet edge strip beside a block and pressing sideways: no travel.
- Plates: a block pulled off / onto a plate presses and releases as before.

## 9. Assumptions

- Taking hold uses the physics contact, with a contact-offset-length cast as the fallback for a lapsed resting contact. Any reach beyond touching is Q2.
- The player's movement while holding is derived from what the block actually moved (`FollowInput`), rather than from the raw input with physics stopping the player: this keeps them locked together in both directions and avoids pressing into a stopped block.
- `PullSkin` is derived (`2f * Physics2D.defaultContactOffset`), an engineering constant, not a feel value.
- The hold placeholder is a separate one-frame asset rather than a "first frame of push" rule in code, so Aaron replaces it by dropping in a drawing.
- `Interact` keeps its name in the actions asset (the generic use key; only `PlayerControls` names it). If a later interaction (a text box) shares the key, that is a one-line change either way, so no separate action is added now.
- The `push` slowdown (`PushSpeedFactor`) applies to pulls too. Same block field.

## 10. Open questions

None. Q1 and Q2 from the review were asked and answered (§3, answers 4 and 5).

## Review (round 1, plan-reviewer, 2026-09-24)

| # | Finding | Disposition |
| --- | --- | --- |
| B1 | Pull pre-cast treats passable terrain (Water with Swim) as a wall: passability is `Physics2D.IgnoreCollision`, which casts ignore. | **Accepted.** Skip hits on a `TerrainRegion` that `IsPassableBy(abilities)`; §6 step 4. |
| B2 | `SheetEdge` travels on the raw `MoveInput`, so a sideways press beside an edge travels mid-hold, against Aaron's answer 2. | **Accepted.** `SheetEdge` reads `EffectiveMoveInput`; added to Files. |
| N1 | Resting contact may lapse at a standstill; design the fallback now. | **Accepted.** Contact-offset-length cardinal cast fallback, §6 step 2. |
| N2 | `PullSkin` literal → derive from `Physics2D.defaultContactOffset`; explain vs the block's `skin`. | **Accepted.** §5. |
| N3 | `PlayerControls` should not name `BlockPusher`; publish the key state instead. | **Accepted.** `PlayerMover.InteractHeld` next to `MoveInput`; `initialStateCheck` noted. |
| N4 | "grab" and "hold" both used; Fold map already has `Grab`. | **Accepted.** "hold" throughout. |
| N5 | Stale summaries in `PushableBlock`, `PushRules`, `PlayerMover.SpeedScale`, `PlayerSketch`. | **Accepted.** Files table. |
| N6 | More tests; lift the break rule into a pure `HoldBroken`. | **Accepted.** §5, §8. |
| N7 | Push across a crease with the player stopped opens a gap until release; state it. | **Accepted.** §6 edge cases. |
| Q1 | Blocked push: hold frame or push loop? | **Escalated.** Aaron: push loop as a strain (§3.4); `HeldSense` replaces `HeldMoved`. |
| Q2 | Touching only, or a reach? | **Escalated.** Aaron: touching only (§3.5). |
| Q3 | Give the block hold its own action now, ahead of a December interact key? | **Rejected:** a later interaction sharing Space, or a new action, is a one-line change to a const either way; nothing is cheaper now than later. Noted in §9. |

## Code review (code-reviewer, 2026-09-24) — approve with fixes

| # | Finding | Disposition |
| --- | --- | --- |
| S1 | `pushFaceAxisFraction` (contact flatness) reused to pick the standstill probe's first direction. | **Accepted.** The probe prefers the input's dominant axis (`TryCardinal(input, 0f)`); a diagonal press has no preference. |
| S2 | The solid contact filter built in two places. | **Accepted.** One `static readonly SolidFilter`. |
| S3 | Plan/Bible claimed a push into the sheet edge travels; the block cannot leave, so the player stands still. | **Accepted.** Plan §6 and Bible §5 corrected; the behaviour raised as Q1. |
| S4 | Two pre-existing `CanPush` tests overlap. | **Accepted.** Merged. |
| Q1 | Holding a block pushed against the sheet edge: stay held with no travel (as coded), or release and travel? | **For Aaron** (final report); coded as stay held, the narrowest behaviour. |
| — | Compile clean; `PushRulesTests` + `PlayerSketchTests` 31/31 through the eval runner. | — |
