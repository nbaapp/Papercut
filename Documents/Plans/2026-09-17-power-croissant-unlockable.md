# Power Croissant: the first unlockable (Push)

Date: 2026-09-17

## 1. Task

Aaron: *"Can we add power ups now? I have a Power Croissant spritesheet that I want to become the powerup that lets the player push blocks. Can you build that out?"*

Deliverables:

1. A placeable element (`Unlockable` component, `Power Croissant` prefab in `Prefabs/Objects`) that grants the player an `Ability` when they walk over it and then disappears for good.
2. A `Grant` mutator on `PlayerAbilities` (its remark, `PlayerAbilities.cs` lines 11–13, says one is added "with the first pickup" — this is it).
3. The Power Croissant drawing as a `SketchAnimation` (`Power Croissant Idle`, the three frames Aaron imported under `Sprites/Upgrades/PowerCroissant/PNGSet`).
4. Blocks require Push from now on: the Block prefab's *Requires Ability* switched on (so the Pushable Paperweight variant inherits it).
5. Bible entries (§9 unlockables; the `Ability.Push` "unused" notes), tests.

**Out of scope:** any pickup feedback beyond the croissant vanishing (no UI, audio or player animation — UI/audio are outside prototype scope, Bible §12); an "interact" key; a save system (Bible §8 `[DECIDE]`, untouched — collected state lives for the play session); other unlockables (Swim etc. are the same prefab with a different `grants`, but none is authored now); placing a croissant on any authored sheet (level authoring is Aaron's, through the Studio palette); a Studio-side resize of the pickup zone (a hand-drawn Simple sprite is fixed-size, as plates are — Aaron 2026-09-03).

Vocabulary: the Design Doc calls these **Unlockables**; Aaron said "power ups". Code uses `Unlockable` (the Design Doc's word; `PlayerAbilities.cs` already calls them "unlockable pickups"). "Power Croissant" is the prefab/asset name.

## 2. Design references

- Design Doc *Mechanics → Unlockables*: "Scattered around the map are some number of items that the player will collect which grants them new abilities … Push/Pull blocks (if unlockable, an early/tutorial one)". This task makes Push an unlockable; the "if" is answered by Aaron (answer 3 below).
- Bible §9 **[TENTATIVE]** unlockables ("same status, same Checkpoint 1 deadline"): built on Aaron's explicit request (this conversation). Cutting it is deleting `Unlockable.cs`, the prefab and the animation asset, and turning the Block prefab's *Requires Ability* back off. `PlayerAbilities.Grant` may stay (it is the mutator the Bible said the first pickup adds).
- Bible §9 structural rule: "an ability is a flag consulted by an element, not a code branch; an element is a component". Already true for Push (`PushableBlock.requiresAbility` → `PushRules.CanPush`); the croissant only sets the flag. A second unlockable is the same prefab with another `grants` value — no new code.
- Bible §5 *Movable objects* **[DECIDED 2026-08-27]**: "Pressure is a fact about the sheet, not about visibility" (`PressureRules`: same face, centre inside the rect). The pickup uses the same shape of rule on the player's place on the sheet (the point under their centre on the topmost layer there, on that layer's up face — `SheetPlacement.TryGetSheetPoint`, extracted from `PlayerPresser` so plates and unlockables share it and neither owns it), so a croissant on the Back is collected by standing on the landed Flap that shows it, and one under a Flap is unreachable because the player cannot be there.
- Bible §5 occlusion / §9 "occlusion notifies": the croissant is an `IFoldOccludee` like a plate — its trigger box is clipped with the fold and its drawing is face content under the fold renderer. Nothing in the fold or occlusion systems learns about it.
- Bible §6 **[DECIDED]** Back authoring: a Back croissant is authored under the Back root in Back-space; `SheetGeometry.BackToFront` maps its rect to the flat sheet, exactly as `PressurePlate` does.
- Bible §8 **[DECIDE]** object persistence: "blocks reset on leaving, effects do not; other kinds open." A collected croissant staying collected is the only coherent behaviour for an ability pickup (re-collecting would grant nothing new, and an ability that persists while its pickup respawns would read as a bug — pillar 2). Recorded in the Bible §8 and §11.8 as "unlockables: collected for the play session (2026-09-17, by uncorrected assumption)"; the register item stays open for other kinds, and whether a future reset button (§11.16) restores a croissant is left open with that item.
- Bible §10 non-goals: "No inventory beyond ability flags" — the croissant sets a flag and nothing more.
- `[DECIDE]` items touched: none resolved. §11.8 (see above), §11.13 (which unlockables ship — still Checkpoint 1's call; this builds one so it can be played), §11.17 save (untouched).

## 3. Decisions already made by Aaron (2026-09-17)

Pre-plan questions and answers:

1. *Full coding workflow or lightweight?* — **Full workflow.**
2. *How does the player collect the croissant?* — **"Walk over it"**: collected the moment the player's centre is on it, on the same face of the flat sheet — the same rule a pressure plate uses. No new input.
3. *Which blocks should need the Push ability from now on?* — **"All blocks, incl. Pushable Paperweight"**: Requires Ability on in the Block prefab itself, so every block and the Pushable Paperweight variant demand Push. Per-block override stays available.

Post-review question and answer (2026-09-17):

4. *Every block now needs Push, and the Test Desk sheets have blocks but no croissant. How should the Test Desk keep its blocks pushable?* — **"Player starts with Push on Test Desk"**: a per-scene override on the Test Desk's Player instance only (`PlayerAbilities.abilities = Push`); the official Desk stays at None.

Assumptions stated to Aaron with the questions (not corrected):

- The croissant is face content like a pressure plate: it folds, gets covered and clipped by a Flap, and can be authored on the Back so a puzzle can hide it under a fold.
- Once collected it stays gone when the player leaves and returns (like Gates, not like blocks), and the ability persists on the player.
- No pickup feedback beyond the croissant disappearing.

## 4. Files

### Runtime (`Assets/Papercut/Scripts`)

| File | Change | Purpose |
| --- | --- | --- |
| `Objects/Unlockable.cs` (+ .meta) | new | The element: an ability pickup on a sheet face; `IFoldOccludee` over its trigger box. |
| `Player/PlayerAbilities.cs` | modify | `Grant(Ability)` mutator; remark updated (the first pickup exists). |
| `Player/Ability.cs` | modify | `Push` doc: granted by the Power Croissant (was "unused by content"). |
| `Objects/PlayerPresser.cs` | modify | Delegates to `SheetPlacement.TryGetSheetPoint` (the computation moves out; the plate adapter stays plate-only). |
| `Fold/SheetPoint.cs` | modify | `SheetPlacement.TryGetSheetPoint(SheetLayers, Vector2 sheetLocal, out SheetPoint)`: the place on the flat sheet under a sheet-local Desk point — the topmost layer there, on its up face. Pure; the one notion of "where is the player on the sheet" for plates and unlockables. |

### Editor (`Assets/Papercut/Editor`)

| File | Change | Purpose |
| --- | --- | --- |
| `StudioPane.cs` | modify | `ColorFor`: an `Unlockable` element outlines in its own colour (`UnlockableColor`), like plates and blocks do. |

The palette discovers the prefab from `Prefabs/Objects` (`StudioPalette.ElementFolders`); hit-testing, outlining and the fold preview work from its `BoxCollider2D` like a plate's. It is not in the resizable set (`StudioPlacement.IsResizable` keys on region/plate; a Simple sprite is fixed-size anyway).

### Assets

| File | Change | Purpose |
| --- | --- | --- |
| `Animations/Power Croissant Idle.asset` (+ .meta) | new | `SketchAnimation`: the three croissant frames, synced to the clock, 3 fps (the Button/Box convention). |
| `Prefabs/Objects/Power Croissant.prefab` (+ .meta) | new | Root at z −0.05 (element depth): `SpriteRenderer` (frame 1, sprite material), `BoxCollider2D` trigger (pickup zone), `Unlockable` (grants Push), `SketchAnimator` (idle = Power Croissant Idle). Same shape as `Hold Plate.prefab`. |
| `Assets/Scenes/Test Desk.unity` | modify | The Player instance's `abilities` override = Push (answer 4). The scene is not open in the editor; a disk edit of the instance's modification list. |
| `Prefabs/Objects/Block.prefab` | modify | `requiresAbility: 1` — through the editor (`LoadPrefabContents` → `SaveAsPrefabAsset`), never a disk edit (memory: the editor holds this prefab and writes stale copies back). |

Sprite import: Aaron already imported the three PNGs (2100 px canvas, 2000 px/unit, Single sprite, centre pivot, max 512 — identical import settings to the Button frames). Untouched.

### Tests (`Assets/Papercut/Tests/EditMode`)

| File | Change | Purpose |
| --- | --- | --- |
| `SheetPlacementTests.cs` | modify | `TryGetSheetPoint`: flat sheet → Front point; on a landed Flap → Back point at the mirrored flat place; off the sheet → false. |
| `PlayerAbilitiesTests.cs` (+ .meta) | new | `Grant`: adds the flag, raises `Changed` once, a repeat or `None` grant is a no-op and raises nothing. |
| `DrawingAssetsTests.cs` | modify | `Power Croissant Idle` is three frames at the shared scale; the prefab is wired to it (idle, frame 1 shown, Simple, white, trigger box covering the drawing's measured extent and inside the canvas); the Block prefab requires Push (Aaron 2026-09-17). |

### Docs

| File | Change |
| --- | --- |
| `Documents/Claude Bible.md` | §9: an *Unlockables* paragraph (built 2026-09-17, the rule, what cutting it means, blocks require Push); the `Ability.Push` "won't use it for now" note updated; §8 and §11.8: unlockables collected for the session (assumption, uncorrected), reset-button interaction open with §11.16; §12 out-of-scope list: unlockables now have one built element. |

## 5. Components & data

### `Unlockable : MonoBehaviour, IFoldOccludee` — `[RequireComponent(BoxCollider2D)]`, `[DefaultExecutionOrder(10)]` (after the player and blocks have moved, like `PressurePlate`)

Serialized:

| Field | Type | Default | Tunable? |
| --- | --- | --- | --- |
| `grants` | `Ability` | `Ability.Push` | Design data per prefab (Inspector): which ability this pickup gives. A second unlockable is this prefab with another value. |

The trigger `BoxCollider2D` on the same object is the **pickup zone**: the player collects the croissant when their centre is inside it (Inspector-editable per instance, like a plate's footprint). No other numbers. The rule itself is one comparison inline in the component (`point.Side == side && flatRect.Contains(point.Point)`, the `PressureRules` shape) — no rules file for one line (review N6).

Public surface:

- `Ability Grants` — what it gives.
- `bool IsCollected` — true once taken (the object is then inactive).
- `IFoldOccludee`: `FaceLocalFootprint` / `OnFoldCoverageChanged` — identical to `PressurePlate` (`OccludedCollider` over the box).

### `SheetPlacement.TryGetSheetPoint` (static, in `SheetPoint.cs`)

- `static bool TryGetSheetPoint(SheetLayers layers, Vector2 sheetLocal, out SheetPoint point)`: `TopLayerAt` → false if no layer is there; else the layer's `ToDesk.Inverse` of the point, on `UpSide(layer)`. `PlayerPresser.TryGetSheetPoint` becomes: Screen check, `mover.Position - sheet.Centre`, this call. `Unlockable` calls it with the same input (`PlayerMover.Position`), so plates and pickups never disagree about where the player is, and neither depends on the other (review N1).

### `PlayerAbilities` (modify)

- `public void Grant(Ability ability)`: `abilities |= ability`; if the set changed, `Changed?.Invoke()` immediately (a call from `FixedUpdate` may touch physics, unlike `OnValidate`). Granting `None` or an already-held ability is a no-op that raises nothing. The Inspector field keeps showing the live set (it *is* the field), so Aaron can see and toggle what the player holds in Play Mode as before.

### `StudioPane` (modify)

- `static readonly Color UnlockableColor` (a distinct hue, e.g. warm gold `new(0.95f, 0.8f, 0.2f, 1f)`); `ColorFor` returns it for an element with an `Unlockable`. Editor-only colour constant, not a game tunable.

### Assets

- `Power Croissant Idle`: `frames` = PNG_0001/0002/0003 (`fileID 21300000`, the three guids from the metas), `syncToClock: 1`, `framesPerSecond: 3`.
- `Power Croissant.prefab`: measured from frame 1, the drawing spans local x ≈ [−0.43, 0.35], y ≈ [−0.19, 0.32] (the croissant sits a little up-left of the canvas centre). Trigger box authored on the drawing's own bounds: **offset (−0.04, 0.065), size 0.78 × 0.51** — so the player cannot collect it while visibly standing beside it (review N3; `FoldFootprint.FaceLocalRect` and the Studio hit-test both honour the offset). The size is Aaron's to tune. `SketchAnimator.randomiseStart: 1` as elsewhere. Sprite material: the sprite-default material the plates use (`9dfc825aed78fcd4ba02077103263b40`).

## 6. Behaviour

**Awake** (`Unlockable`):

1. Cache the box; build the `OccludedCollider`. If the box is not a trigger: `LogError`, fix at runtime (the plate's pattern — a solid pickup would be a wall).
2. Find the `Sheet` in parents and which face root it is under; compute `flatRect` (`FoldFootprint.FaceLocalRect` then `BackToFront` for Back). Not under a sheet / not under a face root: `LogError`, `enabled = false` (stays visible, never collects — the error names the fix).
3. `grants == None`: `LogError("… grants nothing; set Grants"), enabled = false` — the croissant sits there uncollectable rather than vanishing for nothing.

**OnEnable / Start**: find the player (`FindAnyObjectByType<PlayerAbilities>()`, and its `PlayerMover` via `GetComponent`). Missing: `LogError` once per session (static flag, the plate's pattern) — nothing can collect it.

**FixedUpdate**:

1. Player missing or `IsCollected` → return.
2. `sheet.IsScreen` false → return (cheap early-out on every non-Screen sheet). `SheetPlacement.TryGetSheetPoint(sheet.Folds.Layers, mover.Position - sheet.Centre, out point)` false (no layer under the player) → return.
3. `point.Side != side || !flatRect.Contains(point.Point)` → return.
4. **Collect**: `abilities.Grant(grants)`; `IsCollected = true`; `gameObject.SetActive(false)`.

**Arrival edge (review N5):** on a screen transition the player is placed at the entry strip before the slide and the destination is already the Screen, so a croissant authored in an entry strip is collected during the slide, before the player has walked. Harmless; noted in the component remarks so nobody chases it as a bug.

**After collection**: the object is inactive. `SheetOcclusion` still enumerates it (`GetComponentsInChildren(true)`) and applies coverage to its colliders — harmless on an inactive object (`RemoveObjectEffect` relies on the same). `Sheet.PlayerLeft` resets blocks and folds; nothing re-activates the croissant, so it stays collected for the play session. The granted flag lives on `PlayerAbilities` and follows the player across sheets; `TerrainRegion`s already re-evaluate on `Changed`, `BlockPusher` reads the flags live.

**Folding**: covered by a Flap → `OccludedCollider` clips/disables the trigger, the drawing is hidden by the compositor; the player cannot stand there, so `IsReached` is never true for a covered part. Authored on the Back → the drawing is Back face content, shown mirrored on a landed Flap; the player standing on that Flap has a Back-side `SheetPoint` at the flat point, so it collects. Straddling a crease → whichever part the player stands on collects (it is one rect on the flat sheet, like a plate).

**Blocks** (`Block.prefab` `requiresAbility: 1`): with `abilities == None` a block "is just a wall: no slowdown" (`BlockPusher`, existing). After a croissant: `CanBePushedBy` passes. The fixed `Paperweight` has `pushable` off and is unaffected; the `Pushable Paperweight` inherits the requirement (answer 3). **Consequence for Aaron's authored sheets:** every existing block on the Desk and Test Desk sheets now needs Push; until a croissant is placed on a sheet (Studio palette) or Push is ticked on the Player's `PlayerAbilities` in Play Mode, they don't move. Called out in the report.

**Failure modes summary** (all reported, none silent): non-trigger box (fixed + error), not under a sheet/face (error, disabled), `grants == None` (error, disabled), no player in the scene (error once), idle animation missing/unplayable (`SketchAnimator` already errors).

## 7. Interfaces & seams

- No new interface. The pickup reuses the `IFoldOccludee` seam (the fold system sees it only as a clipped footprint) and the pure `SheetPlacement.TryGetSheetPoint` (the player's place on the flat sheet), which `PlayerPresser` now delegates to. Plates and unlockables are both `[TENTATIVE]`; neither depends on the other, so either can be cut alone (review N1).
- Ability → element coupling stays a flag read (`PushRules.CanPush`). Nothing in fold, occlusion, navigation or the Studio branches on "unlockable".
- Cutting the feature: delete `Unlockable.cs`, the prefab, the animation asset, their tests; remove the `ColorFor` line; flip `requiresAbility` back off on the Block prefab. `PlayerAbilities.Grant` and `SheetPlacement.TryGetSheetPoint` stay (harmless, tested, used by plates).

## 8. Testing

**Edit Mode tests (mechanical, run through the Unity CLI `run_tests {"mode":"editor"}`):**

- `SheetPlacementTests.TryGetSheetPoint_*`: flat sheet → the same point on Front; a point on a landed edge Flap → the mirrored flat point on Back; a point off every layer → false.
- `PlayerAbilitiesTests` (temporary GameObject, destroyed in teardown): `Grant(Push)` sets the flag and raises `Changed` exactly once; `Grant(Push)` again raises nothing; `Grant(None)` raises nothing; `Grant(Swim)` on a holder of Push yields both.
- `DrawingAssetsTests`: `PowerCroissantIdle_IsThreeFramesAtTheSharedScale`; `PowerCroissant_ShowsItsDrawing_AndGrantsPush` (animator idle, renderer frame 1 / Simple / white, trigger box inside the canvas and not far smaller than the drawing, `grants == Push`, root z −0.05, no `TerrainRegion`/`PushableBlock`); `Block_RequiresPush_ByDefault` (Block and Pushable Paperweight both require Push; the fixed Paperweight is not pushable).

**Mechanical checks (Unity CLI):** `recompile` clean; full edit-mode suite (report the `Run finished` line and every failure, pre-existing included); load the new prefab and animation through the editor (`LoadAssetAtPath`, no missing scripts); `editor_play` on the Test Desk, `eval` probes: (a) Front: instantiate the Power Croissant under the Screen's Front, teleport the player's body onto it, step physics, read `PlayerAbilities.Abilities == Push`, `Unlockable.IsCollected`, `activeSelf == false`; a block's `CanBePushedBy` false before the grant and true after; (b) Back (review N4): a croissant under the Back root, commit an edge fold whose landed Flap shows it, teleport the player onto the Flap, step, assert collected; (c) persistence (review N4): after (a), travel to a neighbour and back (`ScreenNavigator`), assert `Abilities == Push` still and the croissant still inactive after `Sheet.PlayerLeft`/`PlayerEntered` re-applied occlusion; console has no errors throughout; `capture_game_view` before and after (a) (describe literally). `editor_stop`.

**What only Aaron can judge (playtest):**

- Whether walking over it *feels* like a pickup with no feedback but the croissant vanishing (a chime/flash/bounce would be a later, separate ask).
- The pickup zone size (0.86 × 0.64 box) — too generous, too tight.
- Whether "all blocks need Push" reads well on the existing test sheets once a croissant is placed — and where the tutorial croissant should go.
- The Studio colour for the croissant outline.

## 9. Assumptions (engineering)

1. The player's place on the sheet for a pickup is the point under their centre on the topmost layer there, on its up face — the same notion plates use (now shared code), so the two never disagree about where the player is.
2. Deactivating the GameObject is the collected state (the same mechanism `RemoveObjectEffect` uses); an inactive occludee receiving coverage is harmless (already relied on by removed Gates).
3. `FixedUpdate` with `DefaultExecutionOrder(10)` sees the player's position after this step's movement, as plates do; one physics step of latency is invisible.
4. The Block prefab edit is done through the editor (memory: stale write-back); new files are written to disk and imported.
5. The sprite import settings Aaron chose (2000 px/unit, 2100 px canvas) are the shared drawing scale; the drawing's extent was measured from frame 1 by eye at ±0.02 units.
6. No `Collected` event on `Unlockable` yet — nothing listens; added with the first listener (feedback/UI), not before (guidelines §3).

## 10. Open questions

Raised by the review (Q1), asked after it and answered (§3, answer 4): how the Test Desk (the mechanics test bed) should keep its blocks pushable now that every block needs Push — a per-scene `abilities = Push` override on the Test Desk's Player instance, a Power Croissant placed on Test Desk Sheet (0,0), or neither (tick it in Play Mode). Answer recorded in §3 once given.

## Review (2026-09-17, plan-reviewer, round 1)

Verdict: approve with changes. No blocking findings.

| # | Finding | Disposition |
| --- | --- | --- |
| N1 | `Unlockable` would depend on `PlayerPresser` (a plate adapter); cutting plates would cut the pickup's dependency. | **Accepted.** The sheet-point computation moves to `SheetPlacement.TryGetSheetPoint`; `PlayerPresser` delegates; `Unlockable` calls it with `PlayerMover.Position`. |
| N2 | Collected-for-the-session resolves §8/§11.8 for unlockables without a Bible line there. | **Accepted.** §8 and §11.8 get the line, marked as an uncorrected assumption; reset-button interaction left open with §11.16. |
| N3 | Centred 0.86 × 0.64 box lets the player collect while visibly beside the croissant. | **Accepted.** Box authored on the drawing's bounds: offset (−0.04, 0.065), size 0.78 × 0.51. |
| N4 | Play Mode probe covers only the Front happy path. | **Accepted.** Back-on-a-Flap and leave-and-return probes added to §8. |
| N5 | A croissant in an entry strip is collected during the slide. | **Accepted** as a remark in the component; no behaviour change. |
| N6 | `UnlockableRules` is a file for one comparison. | **Accepted.** Dropped; the comparison is inline. |
| N7 | Two "Bible §9 says" citations are from `PlayerAbilities.cs`. | **Accepted.** Cited correctly. |
| Q1 | How should the Test Desk keep its blocks pushable? | **Escalated** to Aaron (step 5). |
| Q2 | Is a collected croissant part of a future reset? | **Not asked**: the reset button is itself `[DECIDE]` §11.16 and does not exist; nothing built now depends on the answer. Recorded as open beside §11.16 (N2). |

## Verification (2026-09-17)

- `recompile` → `completed`, `errors: []`.
- `run_tests {"mode":"editor"}` (Desk scene open, clean, no prefab stage): Editor.log `Run finished: 428 total, 428 passed, 0 failed`. New tests included: `SheetPlacementTests.TryGetSheetPoint_*` (3), `PlayerAbilitiesTests` (3), `DrawingAssetsTests.PowerCroissantIdle_*`, `PowerCroissant_ShowsItsDrawing_AndGrantsPush`, `Blocks_RequirePush_ByDefault`.
- Block prefab edited through the editor (`LoadPrefabContents` → `requiresAbility = true` → `SaveAsPrefabAsset`): `before=False after=True`; disk shows `requiresAbility: 1`; the diff is the flag plus the editor's serialization normalisation (`pushable: 1` written explicitly, newer renderer fields). Uncommitted sheet prefabs' checksums unchanged before/after the test run and every Play Mode session.
- New assets through the editor: prefab loads, 0 missing scripts, `Unlockable.Grants = Push`, `SketchAnimator` present, sprite `PNG_0001`, material `Sprite-Unlit-Default`, box offset (−0.04, 0.07) size (0.78, 0.51) trigger; `Power Croissant Idle` 3 frames, playable; `Pushable Paperweight` reports `requiresAbility = True` (inherited).
- Play Mode (Desk scene, Sheet (0,0) as Screen, Player starting at None), eval probes:
  - (a) Front: croissant + Block instantiated under Front; before: `abilities=None`, `blockPushable=False`, `collected=False`; player teleported onto the croissant; after one physics step: `abilities=Push`, `collected=True`, `activeSelf=False`, `blockPushable=True`.
  - (b) Back: croissant under the Back root at Back-space (−1, −3.25), given `grants = Swim` so the grant is observable after (a); south edge fold depth 2 committed (`rejection=None`, 2 layers); `TryGetSheetPoint` at desk (1, −1.25) → `(1, −3.25) on Back`; player teleported there; after a step: `abilities=Swim, Push`, `collected=True`, `activeSelf=False`.
  - (c) Leave and return: `TravelThrough` North to Sheet (0,1) (`transitioning=True`), then South back; on return: `abilities=Push` (fresh session with only (a) collected), the probe croissant `collected=True active=False` after `PlayerLeft`/`PlayerEntered` re-applied occlusion, `blockPushable=True`.
  - Console: only the two pre-existing errors from Aaron's authored Desk sheets (`RemoveObjectEffect on 'Hold Plate'/'Latch Plate' has no target`), nothing from this work.
  - Screenshots (described literally): a session with a croissant placed on the Front at (−2, 1.5) on the face layer and one under Back at (−1, −3.25) with a south fold of depth 2: the Front croissant is drawn on the green map art just above the river bank; the landed Flap is white paper across the lower third of the sheet and the Back croissant appears on it, upside-down (mirrored about the crease), at about (1, −1.25); Scuffy stands at (0, 3) on the Base. The earlier before/after shots of probe (a) were both taken after collection (the physics step ran between eval and capture), so they show Scuffy on empty green where the croissant was and the probe Block to the right; not evidence either way.
  - Play Mode left stopped after every session (`playMode: stopped`).
- Not verified mechanically: the Studio palette entry and outline colour (editor UI; the palette discovers `Prefabs/Objects` by folder scan and `ColorFor` is a one-line addition), the Test Desk scene's Player override (a disk edit of a scene the editor had not loaded; `propertyPath: abilities value: 2` on the Player instance, target the prefab's `PlayerAbilities` component fileID 1006), and everything under "what only Aaron can judge" in §8.

## Deviations

- `Unlockable.IsReachedBy(in SheetPoint)` is public (the plan had the comparison inline): the code reviewer asked for the collection rule to be under test, and a one-line public rule is the honest way to do that. No behaviour change.
- The face-resolution block shared by `PressurePlate` and `Unlockable` (find the Sheet, pick Front/Back root, flat rect through `BackToFront`) moved into `Fold/FaceContent.cs` (`TryResolveFace`, `FlatRect`); both call it. `PushableBlock` and `TerrainRegion` keep their own variants (they resolve more than a face and differ in shape) - out of this task.
- `Unlockable` has no `Start`: one player lookup in `OnEnable` (every scene object exists by then; a pickup instantiated later runs it then); a player without a `PlayerMover` is reported once per session.

## Code review (2026-09-17, code-reviewer, round 1)

Verdict: approve with fixes. No must-fix findings. Reviewer independently re-ran recompile, the suite (428/428 at that point) and its own Play Mode probes (Front pickup, Back pickup from a Flap, and a negative: the player 0.45 to the right of a pickup, inside the sprite canvas but outside the box, is not collected; at 0.30 it is).

| # | Finding | Disposition |
| --- | --- | --- |
| S1 | Third copy of the find-Sheet / pick-face-root / flat-rect block (plate, pickup, block, region). | **Accepted** for the two single-box elements: `FaceContent.TryResolveFace` + `FlatRect`; `PressurePlate` and `Unlockable` use it. Block and region left as they are (see Deviations). |
| S2 | No edit-mode test touches `Unlockable` (Awake refusals, the collection rule, the Back mapping). | **Accepted.** `UnlockableTests` (6): Front reach/miss/other-face, Back mirrored place, not under a Sheet, not under a face root, grants None, non-trigger fixed. Awake invoked by reflection on a minimal Sheet hierarchy. |
| S3 | A player without `PlayerMover` is reported twice (OnEnable and Start) and on every re-enable. | **Accepted.** `Start` dropped; reported once per session (`reportedNoMover`). |
| Q1 | A croissant whose ability the player already holds still vanishes and grants nothing new - wanted? | **Kept as built (consume), flagged to Aaron in the report.** Only reachable with a duplicate pickup or the Test Desk override; either reading is coherent, so it is his call and does not block. |

After fixes: recompile `completed, failed=false`; suite `434 total, 434 passed, 0 failed`; uncommitted sheet prefabs unchanged.

## Code review (2026-09-17, code-reviewer, round 2 - the fixes only)

Verdict: approve. Reviewer confirmed `PressurePlate`'s diff is the extraction alone (messages character-identical), re-ran the suite twice (434/434) and checked the uncommitted assets for write-back (unchanged).

| # | Finding | Disposition |
| --- | --- | --- |
| S1 (nit) | Nothing automated exercises `Collect` (order of `IsCollected`/`Grant`, deactivation). | **Accepted.** `UnlockableTests.Collect_GrantsToThePlayer_AndDeactivatesThePickup` (player field and `Collect` by reflection; the `Changed` listener asserts `IsCollected` is already true). |
| S2 (nit) | `FaceContent`'s summary crefs two `[TENTATIVE]` elements. | **Accepted.** Prose instead. |
| S3 (nit) | Two errors when a pickup is both outside a face root and grants nothing. | **No change** (each names a distinct fix; the plate does the same). |
| S4 (nit) | The three occludee/gizmo one-liners are still duplicated between plate and pickup. | **No change** (reviewer: leave it). |
