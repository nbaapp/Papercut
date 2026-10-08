# Paperweights: objects a fold cannot pass

Date: 2026-09-14

## 1. Task

Aaron, while designing a level: *"I need an object that is placed on a sheet somewhere that blocks a fold from folding past that point. So if I put it in the middle of the page, no folds can go beyond that middle point. This prevents massive folds from happening, which can invalidate a lot of potential puzzles. Sometimes I want those folds, but sometimes not. There will be 2 varieties of this object. One is a static thing that can be placed anywhere, including on top of walls and stuff. The other is a pushable version, which acts like the pushable block that we have, except that it also blocks folds. This version is so I can use this mechanic to design puzzles revolving around it."*

Both varieties are **paperweights** (Aaron's name). Deliverables:

1. Runtime: a fold's drag depth is clamped so its Flap never lifts or lands on a face-up paperweight; the drag stops at the contact depth and follows the cursor back. Commit still checks exactly and refuses (red) as a safety net.
2. Two palette prefabs under `Prefabs/Objects`: `Paperweight` (fixed in place) and `Pushable Paperweight` (a block that also blocks folds). Both are solid to the player and to pushed blocks.
3. The Sheet Studio's fold preview clamps and refuses exactly as the game does, from the paperweights authored on the open sheet.
4. Bible entries (§4 validity, §9 built elements).

**Out of scope:** paperweights affecting unfolds (Aaron: not for now); a paperweight on the face-down side blocking anything (Aaron: no); art beyond a placeholder (a tinted copy of the block's drawing; Aaron draws the real one later); a visual cue for the clamp beyond the flap stopping; resizable paperweights; multi-path or polygon paperweight footprints.

## 2. Design references

- Design Doc *Mechanics → Folding*: valid folds partially cover objects; invalid folds cover the player. A paperweight adds a second fold constraint, requested by Aaron, of the same shape as the player rule. *Pillar 2*: the flap visibly stopping at the weight is the clearest possible signal; nothing is hidden behind a refusal.
- Bible §4 *Validity* **[LOCKED]**: the player rule and "the player is always on the base". Untouched. *Geometry* **[LOCKED]**: crease families; the drag clamp already exists for overhang (`FoldGeometry.MaxDepth` / `SheetLayers.MaxDepth`, Aaron 2026-08-26); the paperweight clamp composes with it as a second `min`. **[DECIDE]** continuous vs snapped depth (§11.10): untouched — the snap is applied before the clamp, as it is for the overhang clamp today.
- Bible §4 *Unfolding* **[DECIDED]**: objects ride the flap around to the Back. A pushable paperweight is a block, so it rides; Aaron confirmed unfolds are unaffected.
- Bible §5 *Movable objects* **[DECIDED 2026-08-27]**: a `PushableBlock` is sheet content — a rect on the flat sheet plus a side, present where it lies on a visible surface (`SheetPlacement.VisiblePieces`). "Pressure is a fact about the sheet, not about visibility" — **not** the rule here: Aaron chose face-dependence for paperweights (answer 2 below), so a paperweight blocks only through the parts of it that are up.
- Bible §6 *Backside model* **[DECIDED]**: Back content authored in Back-space, mirrored at runtime. A Back-authored paperweight is placed by the block machinery exactly like a Back block.
- Bible §9 **[TENTATIVE]** puzzle elements: pushable blocks are built on Aaron's request; this task adds a new element on Aaron's request (this conversation). Built to full standard, isolated: cutting it is deleting `Paperweight.cs`, `FoldObstacles.cs`, `IFoldObstacle.cs`, the two prefabs, and the `IFoldObstacle` hook lines in `SheetFolds`/`StudioFoldModel` (same shape as the existing `IFoldConstraint` hook).
- Bible §0 / guidelines §4: features are data and components; an element is a component. The paperweight is one component (`Paperweight`) on two prefabs that differ in data (`pushable` on/off, tint).
- `[DECIDE]` items touched: none resolved. §11.9 world unit scale untouched (sheet-local units as everywhere). §11.8 object persistence: the pushable paperweight resets on leaving exactly like a block (it *is* a block); nothing new decided.
- Vocabulary: Sheet, Front/Back, Flap, Crease, Base, Desk. "Obstacle" is the fold system's word for "something a Flap may neither lift nor land on" (it does not know paperweights); "Paperweight" is the element.

## 3. Decisions already made by Aaron (2026-09-14)

Pre-plan questions and answers, verbatim:

1. *Lifted only, or lifted and landed?* — **"When a flap is dragged across the blocker, it just stops getting pulled. Like it doesn't go red and be invalidated, it just can't stretch beyond that point, and when the mouse drags past it, just doesn't follow. It still doesn't get placed until the player releases the mouse, though. But yes, other than that, its the player rule."** (So: lifted **and** landed, like the player; the interaction is a clamp, not a red refusal.)
2. *Does it pin the sheet regardless of face?* — **"No. If its not on the same side, it doesn't block it."**
3. *Does the static blocker have collision of its own?* — **"Yes, the player can collide with both versions."**
4. *Does it also refuse unfolding?* — **"yeah, for now, unfolds aren't affected."**
5. *Name and metaphor.* — **"Lets have both be paperweights."**
6. Full coding workflow: **"yes"**.

My restatement of answer 1, sent to Aaron before he said yes to the workflow (he did not correct it): the drag depth is clamped to the deepest fold whose Flap would not touch a visible paperweight; past that the cursor keeps moving and the flap sits at the contact depth; drag back and it follows again; no red tint; release commits the clamped fold. A paperweight at the sheet edge means that edge's clamp is zero — the drag does nothing. Commit still runs the exact polygon check as a safety net and refuses red if a fold somehow reaches a paperweight. With stacked folds, "contact" is the first depth at which any lifted paper or the landing flap would touch the weight; a deeper technically-legal depth past that is unreachable, as a real flap cannot pass through a weight. Also stated then: the pushable paperweight is a Block variant plus the weight component; the static paperweight uses the same sheet-content placement with pushing disabled, so a Back-authored one appears when exposed and blocks only while face-up; both are solid to the player and to pushed blocks, and both count as obstacles for screen-arrival room.

## 4. Files

### Runtime (`Assets/Papercut/Scripts`)

| File | Change | Purpose |
| --- | --- | --- |
| `Fold/IFoldObstacle.cs` | new | Something under a Sheet a Flap may neither lift nor land on: its face-up pieces, sheet-local. |
| `Fold/FoldObstacles.cs` | new | Pure rules: is a fold clear of the obstacles; the first-contact depth (the clamp) for an anchor on the sheet as it lies. |
| `Fold/FoldEffect.cs` | modify | `Overlaps(ConvexPolygon)` made public (was the private `OverlapsPolygon`). |
| `Fold/SheetFolds.cs` | modify | `FoldRejection.CoversObstacle`; gathers `IFoldObstacle`s under the sheet; `MaxDepth` = min(overhang bound, obstacle bound); `Evaluate` refuses `CoversObstacle`. |
| `Fold/FoldDragInput.cs` | modify | Doc comment (the drag also holds at a paperweight); `CoversObstacle` joins the quiet-refusal list. |
| `Objects/PushableBlock.cs` | modify | `pushable` Inspector toggle (off = fixed sheet content); public `VisiblePieces`; `CanBePushedBy` honours it. Doc remark. |
| `Objects/PushRules.cs` | modify | `CanPush(bool pushable, Ability owned, bool requiresAbility, Ability required)`. |
| `Objects/Paperweight.cs` | new | The element: `IFoldObstacle` over the sibling `PushableBlock`'s visible pieces. |

### Editor (`Assets/Papercut/Editor`)

| File | Change | Purpose |
| --- | --- | --- |
| `StudioFoldModel.cs` | modify | `SetObstacles(...)` (authored footprint + face per obstacle); obstacle desk pieces rebuilt with the stack; `Evaluate` refuses `CoversObstacle`; `MaxDepth` includes the obstacle clamp. |
| `StudioFoldPane.cs` | modify | `CoversObstacle` joins the quiet-refusal filter on release. Doc line. |
| `SheetStudioWindow.cs` | modify | Re-gathers the stage sheet's `IFoldObstacle`s into the model on attach and on every edit (`OnEditsChanged` sets a flag; `OnGUI` refreshes when set, since only it has the sheet). |

### Assets

| File | Change | Purpose |
| --- | --- | --- |
| `Prefabs/Objects/Paperweight.prefab` (+ .meta) | new | **Variant of `Block.prefab`**: `pushable = false`, placeholder tint, `Paperweight` component added. |
| `Prefabs/Objects/Pushable Paperweight.prefab` (+ .meta) | new | **Variant of `Block.prefab`**: placeholder tint, `Paperweight` component added. |
| `Sheets/Test Desk/Sheet (0,1).prefab` | modify (through the editor) | One of each placed on this **empty** Test Desk sheet (checked: it holds nothing but the sheet; Sheet (0,0) holds the Map 1 authoring and is untouched) so the feature can be exercised in Play Mode and Aaron can play it at once; he moves or deletes them in the Studio. |
| `Documents/Claude Bible.md` | modify | §4 Validity: the paperweight clamp; §9: the element, with Aaron's quotes. |

The variants are created **through the open editor** (`unity command eval`: instantiate `Block.prefab`, set the overrides, `AddComponent<Paperweight>()`, `PrefabUtility.SaveAsPrefabAsset`, which yields a variant), not by hand-written YAML — the derived-fileID rules in memory are the fallback only.

### Tests (`Assets/Papercut/Tests/EditMode`)

| File | Change |
| --- | --- |
| `FoldObstaclesTests.cs` | new — clear/refuse; the clamp for edge and corner anchors, flat and stacked, against a brute-force scan oracle; zero clamp at the edge; no obstacle → +∞. |
| `PushRulesTests.cs` | add `CanPush` with `pushable` off. |
| `StudioFoldModelTests.cs` | add: obstacle clamps `MaxDepth` and refuses `Evaluate`; a Back-authored obstacle is inert flat and active once exposed; obstacles follow the committed stack. |
| `DrawingAssetsTests.cs` | add: both prefabs are Block variants carrying `Paperweight`, wired to the drawing; `Paperweight.prefab` has `pushable` off, the pushable one on. |

## 5. Components & data

### `IFoldObstacle` (interface, `Papercut`)

```csharp
public interface IFoldObstacle
{
    /// Appends the sheet-local convex pieces of this object that a Flap may neither lift nor land on: the parts of it
    /// that are face-up on the sheet as it lies now (SheetFolds.Layers). Adds nothing while none of it is up.
    void AddObstaclePieces(List<ConvexPolygon> sheetLocal);
}
```

Same pattern as `IFoldConstraint`: `SheetFolds` finds implementations with `GetComponentsInChildren(false, list)`; being an active component under the Sheet is the registration. Runtime-only (plan review N6): the Studio's view of an obstacle is the element's authored colliders through the existing `StudioPlacement.AuthoredFootprintPieces` (for a Block variant: its `BoxCollider2D`; the pusher polygon is excluded as today), so no edit-time member is needed.

### `FoldObstacles` (static, pure, `Papercut`)

```csharp
public static bool Clear(in FoldEffect effect, IReadOnlyList<ConvexPolygon> obstacles);   // no lifted/landed piece shares area with an obstacle piece
public static float MaxDepth(FoldAnchor anchor, SheetLayers layers, IReadOnlyList<ConvexPolygon> obstacles); // first-contact depth; +∞ if none
```

`MaxDepth` math, in the anchor's frame — `u` = distance inward from the anchor line, `t` = along it:

- Edge anchor with outward `o`, half-extent `h`: `u = h − p·o`, `t = p·(−o.y, o.x)`; the crease at depth `d` sits at `u = c = d`.
- Corner anchor with signs `s`: `u = (hx − s.x·p.x + hy − s.y·p.y)/√2`, `t = (−s.y·p.x + s.x·p.y)/√2`; the crease sits at `u = c = d/√2` (`FoldGeometry.CreaseOf`: mid = corner − s·d/2, normal s/√2).
- A fold at crease `c` lifts every layer piece `P` where `u ≤ c` and lands it at `u' = 2c − u`. First contact of the fold with obstacle piece `B` is the least `c` such that some `p ∈ P`, `b ∈ B` with `t_p = t_b`, `u_p ≤ u_b` satisfy `u_b = 2c − u_p`, i.e. `c = (u_p + u_b)/2`. (`p = b` covers the lifted case: `c = u_b`.) Per `t`: `f(t) = (pLo(t) + max(bLo(t), pLo(t)))/2` where `pLo(t) ≤ bHi(t)`, else no contact — `pLo/bLo/bHi` are the lower/upper extents of the convex pieces at `t`.
- `f` is piecewise linear with breakpoints only at vertex `t`'s of `P` and `B` and at crossings of their edges, so the minimum is at one of those: gather the candidate `t`'s (all vertices of both, plus every pairwise edge-intersection `t`), evaluate `f` at each (extents by scanning the edges spanning `t`; a vertical edge contributes both endpoints; a `t` outside either piece is skipped), take the least over every (layer piece, obstacle piece) pair. Convert: edge `d = c`; corner `d = c·√2`; clamp at 0. This is the same shape of argument as `SheetLayers.MaxDepth` (vertex bounds suffice).
- Every layer of `SheetLayers.Layers` is a `P` (a fold lifts every layer on the Flap side, `SheetLayers.Apply`). On a flat sheet the landed contact always binds first (the `p = b` lifted case can only win on stacked layers). The result is the first depth at which `Clear` fails; a test scans depths against `Clear` to confirm.
- Contact tolerance: at exactly the contact depth the landed edge coincides with the weight's edge, and float error in `Apply` (a few ulp at sheet scale, ~1e-6) could leave a sliver whose area exceeds `ConvexPolygon.AreaEpsilon` along a sheet-long edge. So `MaxDepth` returns the contact depth minus `ContactTolerance = 1e-4` sheet units (the same magnitude as `SheetLayers.ContainmentTolerance`; invisible), and the tests assert `Clear` at the returned depth and not-clear at `+2e-4`. The clamp is therefore always a fold `Evaluate` accepts.
- Why analytic rather than bisection on `Clear` (plan review N3): bisection assumes `Clear(d)` is monotone in `d`, which holds when the stack has no gap along any line perpendicular to the crease; I could not prove that for arbitrary stacks mixing edge and corner folds, and a wrong assumption would let a flap pass through a weight with no error. The analytic form computes the first contact directly, whatever the stack, and the scan oracle in §8 is the source of truth for it.

### `SheetFolds` (modified)

- `FoldRejection.CoversObstacle` — "The Flap would lift or land on an `IFoldObstacle`" (generic: the fold system does not name paperweights).
- `readonly List<IFoldObstacle> obstacles; readonly List<ConvexPolygon> obstaclePieces;` — `GatherObstacles()` clears both, `GetComponentsInChildren(false, obstacles)`, asks each to add its pieces. Called at the top of `MaxDepth` and `Evaluate` (a drag calls each once per frame on one sheet; unmeasurable, and no cache to go stale when a block is pushed). No serialized fields added.
- `MaxDepth(anchor)` → `Mathf.Min(layers.MaxDepth(anchor), FoldObstacles.MaxDepth(anchor, layers, obstaclePieces))`, and **0 if that is below `minDepth`** (plan review N2): a weight closer than `2 × minDepth` to an edge would otherwise let a sliver of flap lift during the drag and vanish as `TooShallow` on release, which reads as a bug; instead the edge is inert, as it is for a weight touching it (§6 step 5). The overhang bound is never below `minDepth`, so existing behaviour is unchanged. `SetPreview` and `FoldDragInput.DragFold` already clamp to `MaxDepth`, so the drag behaviour Aaron described falls out with no input change: depth from the cursor, snapped, then held at the clamp; the cursor coming back brings the flap back.
- `Evaluate`: after the player rule, `if (!FoldObstacles.Clear(effect, obstaclePieces)) return FoldRejection.CoversObstacle;`. Preview red + commit refusal, as for the player. Reached only if a caller bypasses the clamp (the safety net Aaron agreed to).

### `PushableBlock` (modified)

- New Inspector tunable, `[Header("Pushing")]`: `[SerializeField, Tooltip("Off: the block never moves - fixed content of the sheet that still folds, covers, exposes and resets like any block, and is a wall to the player and to other blocks.")] bool pushable = true;` Default **true** (existing blocks unchanged).
- `CanBePushedBy(pusher)` → `PushRules.CanPush(pushable, held, requiresAbility, pushAbility)`. With it false, `BlockPusher` already treats the block as a wall with no slowdown, and `TryPush` returns false.
- `public IReadOnlyList<SheetPlacement.Piece> VisiblePieces => pieces;` — the parts on a visible surface from the last `Place` (committed layers). The paperweight reads this.
- No other behaviour change.

### `Paperweight` (component, `Papercut`)

```csharp
[DisallowMultipleComponent]
[RequireComponent(typeof(PushableBlock))]
public sealed class Paperweight : MonoBehaviour, IFoldObstacle
```

- No serialized fields (everything design-relevant is on the block: `pushable`, `tint`, footprint size on the `BoxCollider2D`).
- `AddObstaclePieces(list)`: if `isActiveAndEnabled && block.isActiveAndEnabled && block.IsVisible`, append `piece.Desk` for each of `block.VisiblePieces`. A weight authored on the Back under the flat sheet has no visible pieces → adds nothing (Aaron's answer 2); once a Flap exposes it, its pieces are on that Flap → blocks further folds that would lift or land there.
- Why over a block rather than a second static implementation: one component, one placement rule (Bible §5 movable-object decision), two prefabs differing in data. The static prefab is the same content with `pushable` off. Cutting = delete the component and the prefabs.

### Prefabs

Both are prefab **variants** of `Block.prefab` (so drawing, material, physics, `BlockPresser` and any future block fix are inherited):

| Prefab | Overrides | Added |
| --- | --- | --- |
| `Paperweight` | name; `pushable: 0`; `tint` = placeholder slate `(0.45, 0.47, 0.55, 1)` | `Paperweight` component |
| `Pushable Paperweight` | name; `tint` = placeholder lighter slate `(0.62, 0.64, 0.72, 1)` | `Paperweight` component |

The tint values are Inspector tunables on the variant (existing `PushableBlock.tint`); they exist only so the placeholders read differently from a Box until Aaron draws paperweights. Both keep `BlockPresser` (a paperweight on a plate presses it, like a block — nothing in Aaron's answers says otherwise, and removing it would be a silent design choice).

### `StudioFoldModel` (modified)

- `public void SetObstacles(IReadOnlyList<(FaceFootprint footprint, SheetFace face)> authored)` — copies the list; rebuilds `obstaclePieces` (see below); `Changed`.
- `obstaclePieces` rebuilt inside `Replay()`: for each authored obstacle, `layers.Coverage(footprint, face).VisibleParts` — the runtime's own coverage rule, which for an un-pushed block equals `SheetPlacement.VisiblePieces` (no climb, no overhang in the editor: nothing is pushed there).
- `Evaluate`: after the ghost rule, `CoversObstacle` via `FoldObstacles.Clear`. `MaxDepth`: min with `FoldObstacles.MaxDepth`. The pane's `ComputeDragFold` already clamps to `Model.MaxDepth`, so the preview drag stops exactly where the game's does.
- `Clear()` keeps the obstacles (they are authored content, not preview state).
- `public float MinDepth { get; set; }` (set by the window from settings, like `RememberCreasesEnabled`), so `MaxDepth` can apply the same below-`minDepth` → 0 rule as the game.
- Class remarks updated (plan review N4): obstacles differ from the ghost. The ghost applies to live actions only because the player moves between folds; obstacles are authored content that the editor cannot move between folds, so they also guard the fold-list trial replays: `TryRemoveAt`/`TrySetDepth` refuse when a replayed fold is not `Clear` of the obstacles as they lie at that point of the replay, with the reason "would cover a paperweight where it is authored". Stated limit: in the game a pushable paperweight can be pushed away between folds, so the list may refuse a stack the game could reach after a push; the game itself is never wrong, and the editor errs on showing only stacks it can vouch for.
- `SetObstacles` is a no-op (no `Changed`) when the gathered list equals the current one, so unrelated edits do not force a composite re-render (plan review N5).

### `SheetStudioWindow` (modified)

- `bool obstaclesDirty` set in `Attach` and `OnEditsChanged`; in `OnGUI`, once `sheet` is resolved, if dirty: gather `sheet.GetComponentsInChildren<IFoldObstacle>(false)` (interface, not `Paperweight`; active only, as the runtime gathers), for each resolve its face root (under `sheet.Front` → Front, under `sheet.Back` → Back, else skip with a one-line message), take `StudioPlacement.AuthoredFootprintPieces(gameObject, root, pieces)` as a `FaceFootprint`, `foldModel.SetObstacles(list)`, clear the flag. Also passes `settings.MinDepth` into `foldModel.MinDepth` where `RememberCreasesEnabled` is set.

## 6. Behaviour

**Runtime, dragging a fold (Screen sheet with a face-up paperweight):**

1. Press near an edge/corner (or a crease). `FoldDragInput.DragFold` computes depth from the cursor, snaps, then `Mathf.Min(depth, dragTarget.MaxDepth(anchor))`.
2. `SheetFolds.MaxDepth` gathers obstacle pieces (the paperweights' visible desk-space pieces from their blocks), takes the overhang bound and `FoldObstacles.MaxDepth`, returns the smaller.
3. The preview shows the fold at the clamped depth; not red (`Evaluate` on the clamped fold is `None`). Dragging further changes nothing; dragging back brings the flap with the cursor (the clamp is a `min`, not a latch).
4. Release: `TryCommit` with the clamped fold — `TooShallow` check, then `Evaluate` (which re-checks `Clear`) — commits. `Changed` → occlusion → blocks re-place → the paperweight's pieces update for the next drag.
5. A paperweight touching the anchoring edge: clamp 0 → the preview lifts nothing (`NothingToFold`, not red) and release is `TooShallow` (quiet). Nothing happens, per Aaron's restatement.
6. A crease grab (fold further) is a new fold on the committed stack; the same clamp applies with `grabDepth` in the cursor math, unchanged.
7. Unfolds: untouched (Aaron). A pushable paperweight pushed onto a Flap rides around to the Back on unfold like a block, and stops blocking until exposed again (not on the same side, Aaron's answer 2).

**Pushable paperweight:** everything a block does (push, climb onto a Flap, be pushed under a Flap, stop at walls/blocks/edges, press plates, reset on leaving). While parts of it are face-up they are obstacles; a part pushed under a Flap is not (it is covered; a later fold that lifts the covering Flap and the Base under the weight lifts the weight with the paper — it rides, consistent with "not on the same side, doesn't block"; listed under assumptions).

**Fixed paperweight:** a block with `pushable` off: the player walks into a wall (no slowdown), other blocks stop at it, it never moves, it resets to itself, it counts as an arrival obstacle on the Front (inherited). Authored over a Wall region it simply overlaps it — both are solid; the block's "author at least `skin` from walls" note does not apply since it never moves.

**Studio:** placing either prefab (any pane) or moving one re-gathers obstacles; the fold-pane drag clamps at the same depth the game would; a stack the list edits would make impossible is refused with a reason.

**Failure modes (no silent failures):**

- `Paperweight` without a `BoxCollider2D` — impossible via `RequireComponent(PushableBlock)` which requires the box; if the block is invalid (`ok == false`, already logged by the block), the weight adds nothing and says nothing more (one error per cause).
- `Paperweight` not under a Sheet: the block already logs and disables itself; `GetComponentsInChildren` from a Sheet never finds it.
- Obstacle gathering finds none: `MaxDepth` returns `+∞`, `Clear` is true — the existing behaviour exactly.
- A fold reaching an obstacle despite the clamp (numerical, or a caller that skips `MaxDepth`): `Evaluate` → `CoversObstacle`, red preview, refused commit; `FoldDragInput`/Studio treat it as quiet-because-already-red.
- Studio: an `IFoldObstacle` under neither face root is reported in the fold message and skipped.

## 7. Interfaces & seams

- `IFoldObstacle` is the seam between the fold system ("what may a Flap not touch") and elements ("a paperweight's visible parts"). The fold system never names paperweights; `SheetFolds` gets one generic gather, like `IFoldConstraint`.
- `FoldObstacles` is pure managed math (unit-testable, no Unity objects) mirroring `FoldValidity`/`SheetLayers.MaxDepth`.
- The clamp lives in `SheetFolds.MaxDepth` / `StudioFoldModel.MaxDepth`, which both drag paths already honour — no new coupling into `FoldDragInput` or `StudioFoldPane`.
- **Cutting the feature:** delete `Paperweight.cs`, the two prefabs, the Test Desk placements. `IFoldObstacle`/`FoldObstacles` and the two hook sites may stay (inert, like `IFoldConstraint` would be without blocks) or go with them; `PushableBlock.pushable` stays (a harmless block option).
- No rendering, occlusion or collision code changes: the paperweight is drawn and clipped by the block's existing machinery.

## 8. Testing

**Edit Mode (pure, `Papercut.Tests.EditMode`):**

- `FoldObstaclesTests`
  - `Clear`: lifted overlap → false; landed overlap → false; clear → true; empty obstacle list → true.
  - Flat sheet, edge anchor: obstacle rect at inward distance `x0` → `MaxDepth == x0/2`; at that depth `Clear` is true, at `+0.01` false.
  - Flat sheet, corner anchor: obstacle whose nearest corner is `(a, b)` in from the corner → `MaxDepth == max(a, b)`: the landed triangle's outer edges are the axis-aligned legs `x = hx − d`, `y = hy − d`, so the first leg to reach the weight decides (plan review B1; the per-`t` formula in §5 gives the same). Same clear/not-clear check.
  - Obstacle touching the anchor edge → `0`. No obstacles → `+∞`. Obstacle entirely beyond a corner fold's reach still yields a finite bound ≥ the overhang bound (the caller takes the min).
  - **Scan oracle:** for a set of configurations (flat; after `EdgeWest(1)`; after `EdgeEast(1)` with the weight in the emptied strip's mirror; after `EdgeNorth(2)` with a corner anchor; a weight on a landed Flap), scan `d` from 0 to the overhang bound in 0.005 steps, find the first `d` where `Clear` fails, assert `|MaxDepth − first| ≤ 0.005` (or both infinite).
- `PushRulesTests`: `CanPush(false, …)` is false whatever the abilities; `CanPush(true, …)` unchanged.
- `StudioFoldModelTests`: with an obstacle at the centre, `MaxDepth(EdgeEast)` is the analytic value and `Evaluate` of a deeper fold is `CoversObstacle`; a Back-authored obstacle does not clamp the flat sheet; after a fold exposes its region it clamps a following fold; `TrySetDepth` refuses a depth that would put a fold over an obstacle with a reason mentioning the paperweight; `Clear()` keeps obstacles.
- `DrawingAssetsTests`: both prefabs load, are variants of `Block.prefab` (`PrefabUtility.GetCorrespondingObjectFromSource`), carry `Paperweight`, `drawing` resolves to the Box Idle animator child, `pushable` is 0 / 1 respectively.

**Mechanical checks through the Unity CLI (step 6):** `recompile` clean; `run_tests {"mode":"editor"}` with every failure reported verbatim; create the variants via `eval` and verify them by `LoadAssetAtPath` (component list, `pushable`, tint, variant parent) and `python Tools/check_links.py`; place one of each on a Test Desk sheet through the editor (`LoadPrefabContents`/`SaveAsPrefabAsset`, or the open stage per the memory rules); `editor_play`, walk nothing — instead `eval`: read `SheetFolds.MaxDepth` for the anchors facing the weights and compare with the analytic value; `TryCommit` a fold at the clamp (expect true) and past it (expect `CoversObstacle`); then (plan review N7) `TryPush` the pushable weight onto a landed Flap and read `MaxDepth` for a crease grab / a second anchor over it (the climb-target pieces feed the clamp); and fold so a Back-authored weight (placed for the probe under `Back`) is exposed, then read `MaxDepth` for an anchor whose fold would land on it (finite) versus the flat sheet (unaffected); `capture_game_view` and describe the frame literally; `get_console_logs` clean; `editor_stop`.

**What only Aaron can judge:** whether the flap "stopping" at the weight feels like the paper hitting something or like a stuck drag (no cue beyond the stop is planned); whether the weight footprint size (block-sized, 0.74 × 0.7) is right for level design; whether the pushable variant's puzzle uses work (push it aside to open up a fold; push it onto a Flap so no later fold can lift that Flap — unfolding is still free, per answer 4); whether a weight on a plate should press it; whether a weight pushed fully under a Flap and then lifted with the Base feels right; the placeholder tints; whether the Studio's refusal wording on list edits reads well.

## 9. Assumptions (engineering)

1. The static paperweight is `PushableBlock` with `pushable` off, not a separate static component — one placement rule, one drawing path; the Bible's movable-object decision already models sheet content this way.
2. The first-contact clamp is computed analytically (§5), not by bisection, so it is exact and pure-testable; with stacked folds, depths past the first contact are unreachable by design (Aaron's restatement).
3. Obstacle gathering per `MaxDepth`/`Evaluate` call (a few per frame, one sheet, during a drag only) rather than a cache — no stale-cache path when a block is pushed.
4. The Studio computes obstacle pieces with `SheetLayers.Coverage` of the authored rect, which equals the runtime's `VisiblePieces` for un-pushed content (nothing is pushed in the editor).
5. Placeholder art: the block's Box Idle drawing with a slate tint per prefab (Inspector `tint`); Aaron's later drawing replaces the `drawing` child's animation on the variants.
6. Prefab names `Paperweight` and `Pushable Paperweight` (Aaron: "both be paperweights"); trivially renamable.
7. A paperweight pushed wholly under a Flap does not block while covered and rides the paper if that region is later lifted (it is not "on the same side" while covered). If Aaron wants covered weights to pin the paper, that is a one-line change in `Paperweight.AddObstaclePieces` (use all pieces on the flat sheet regardless of coverage) plus Bible text.
8. `BlockPresser` stays on both variants (inherited): a paperweight presses plates like a block.
9. The Test Desk placements go on Test Desk Sheet (0,1), which is empty (Sheet (0,0) holds Aaron's Map 1 authoring; (1,0) holds a test Water region).
10. `SheetFolds.MaxDepth` returns 0 when the obstacle bound is below `minDepth` (plan review N2): the edge is inert rather than lifting a sliver that vanishes on release.

## 10. Open questions

None. Everything design-relevant was answered in step 1; the items above are engineering calls, listed so Aaron can veto any.

## Review (round 1, plan-reviewer, 2026-09-14)

Verdict: BLOCK (one blocking finding). Dispositions:

- **B1** (corner clamp expectation `(a+b)/2` is wrong; it is `max(a,b)`) — **accepted.** §8 corrected with the leg argument; §5 notes that on a flat sheet the landed contact binds first. The §5 per-`t` formula was right; the test expectation was the error, which is exactly why the scan oracle stays the source of truth.
- **N1** ("push it onto a Flap to lock that Flap" overstates answer 4) — **accepted**, reworded.
- **N2** (obstacle bound below `minDepth` lifts a sliver that vanishes on release) — **accepted**: `MaxDepth` returns 0 below `minDepth` in the game and the Studio model (assumption 10).
- **N3** (use bisection on `Clear` instead of the analytic first contact) — **rejected**: bisection is only correct if `Clear(d)` is monotone, i.e. the stack has no gap along any line perpendicular to the crease; not proven for arbitrary edge/corner stacks, and a wrong assumption fails silently (a flap passing through a weight). The analytic form computes the first contact directly; the oracle test pins it. Accepted the sub-point: the oracle keeps corner and stacked configurations. Also added a `ContactTolerance` so the clamp is always a fold `Evaluate` accepts (the reviewer's "consistent by construction" concern, met differently).
- **N4** (Studio remarks contradict themselves on trial replays) — **accepted**: obstacles guard trial replays because authored content cannot move between folds in the editor; the pushable-weight limit is stated; refusal reads "would cover a paperweight where it is authored".
- **N5** (gather with `true`; `SetObstacles` re-renders on every edit) — **accepted**: `false`, and `SetObstacles` is a no-op when unchanged.
- **N6** (`AuthoredFootprint` duplicates `StudioPlacement.AuthoredFootprintPieces`) — **accepted**: interface is runtime-only; the Studio uses the existing helper.
- **N7** (Play Mode probe covers a flat sheet only) — **accepted**: push-onto-Flap and exposed-Back-weight `eval` steps added.
- **N8** (enum doc names paperweights) — **accepted**.
- **Q1** (does a paperweight press a plate?) — **escalated** to Aaron (step 5).
- **Q2** (a pushable weight pushed fully under a Flap: inert while covered, or still pins?) — **escalated** to Aaron (step 5).
- **Q3** (resizable static paperweight?) — **escalated** to Aaron (step 5).
- **Q4** (where do the test placements go?) — **resolved without asking**: Test Desk Sheet (0,1) is empty (verified on disk); Sheet (0,0)'s Map 1 authoring is untouched. Recorded in §4 and assumption 9.

## Post-review answers (Aaron, 2026-09-14, step 5)

- Q1 plates: **"Both press"** — both variants keep `BlockPresser`; a fixed paperweight authored on a plate holds it permanently.
- Q2 covered pushable weight: **"Inert while covered"** — a covered weight blocks nothing and rides the paper if that region is later lifted (assumption 7 stands as the rule).
- Q3 size: **"Fixed block size"** — one footprint, the block's; resizing may come later.

## Deviations (implementation, 2026-09-14)

- **Test oracle tolerance:** the scan oracle's first failing sample can be a full step past the true contact while the clamp sits `ContactTolerance` under it, so the assertion tolerance is two scan steps (was one step + tolerance, which failed by 1e-7 on one corner case). No behaviour change.
- **Studio gather uses the window's existing message slot** (`foldMessage`) for an obstacle under neither face root; no new UI.
- **Prefab variants** were created through the editor in a preview scene (`InstantiatePrefab` → `SaveAsPrefabAsset`), as planned; `Tools/check_links.py` reports ALL LINKS OK.
- **Test placements** went on Test Desk Sheet (0,1): `Paperweight` at (0, 1.5), `Pushable Paperweight` at (−2, −1.5), Front face, through `LoadPrefabContents`/`SaveAsPrefabAsset`.
- **Play Mode probe learning (probe, not code):** a block reads its footprint in `Awake`, so a probe must position an instance before it activates (inactive holder, then reparent); the first probe placed every weight at the sheet centre and its numbers matched *that* placement exactly. Pushing a weight 3.35 units carried it over the Flap's crease onto the underside of the Base (the existing block rule: pushed across a crease it rolls to the Back), where it went inert — consistent with "not on the same side"; the corrected push (2.4 units) left it on the Flap.
- **Placeholder tint:** in the Game view the pushable variant's lighter slate tint is hard to tell from an ordinary Block; the fixed one reads darker. Aaron's drawing replaces both anyway; the tints are Inspector values on the variants.
- **Filtered `run_tests`** still ran the whole suite (398) and exceeded the command's 30 s cap; results were read from `Editor.log` (`Run finished: 398 total, 398 passed, 0 failed`).

## Code review (code-reviewer, 2026-09-14)

Verdict: APPROVE WITH FIXES (no must-fix). Dispositions:

- **S1** (missing test: a weight beyond a corner fold's reach yields a bound ≥ the overhang bound) — **accepted**: `MaxDepth_WeightBeyondACornerFoldsReach_DoesNotBindBeforeTheOverhangBound` added.
- **S2** ("paper" in code comments) — **accepted**: "sheet".
- **S3** (fold-system doc comments name paperweights) — **accepted**: `IFoldObstacle` says "an element such as a paperweight" once; `FoldObstacles`, `SheetFolds` and `FoldDragInput` say "fold obstacle".
- **S4** (per-frame allocations in `FoldObstacles.MaxDepth`; `SetPreview` gathers twice) — **accepted**: static scratch lists (pure, single-threaded); `SetPreview` gathers once and uses `MaxDepthGathered`/`EvaluateGathered`; the public `MaxDepth`/`Evaluate` still gather themselves.
- **Q1** (`Sheets/Desk/Sheet (0,0).prefab` is modified in the working tree) — not this task's file: it is Aaron's own Map 1 authoring from before this session (backed up and verified byte-identical throughout). Nothing is committed by this task; reported to Aaron.
- **Q2** (a pushable weight hanging past a Flap's Seam contributes its overhanging part as an obstacle) — **kept as built** and reported to Aaron as an edge case: the overhang is face-up, so "face-up parts block" already covers it, and physically the weight sits above that paper. Excluding it is a one-line filter on `SheetPlacement.Piece` if he prefers.

Fixes were small and mechanical; no second review round.
