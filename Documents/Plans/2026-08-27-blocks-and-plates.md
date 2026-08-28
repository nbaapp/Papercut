# Plan — Pushable block, pressure plates, gate

Date: 2026-08-27. Follows `2026-08-26-multiple-folds.md` (the layer model this builds on). Revision 2, after plan review round 1 and Aaron's answers (§3).

## 1. Task

Aaron (verbatim): *"I would like to add a pushable block, and button. The block should be pushable in the 4 cardinal directions, with no pulling for now. While pushing, it slows the player by a set amount I choose. There should be 2 kinds of buttons for now - single press, and hold down. Both of them have some kind of effect on the world, but the hold down reverts the effect to how it was before if it is let go, while the single press it just happens and stays. For now the effect can be removing a blocking object, which I guess Also needs to be built."* Later: *"make sure that buttons have the option to have other effects than just removing a Gate, but can have other effects later should I so desire."*

Deliverables:
1. **`PushableBlock`** — a free-placed block on a sheet face, pushed continuously along a cardinal axis by walking into it; the pusher is slowed by a per-block factor. It is content *of the sheet*: folds cover, lift, expose and carry it exactly as they do the paper under it (§6).
2. **`PressurePlate`** — two kinds as data (`Hold`, `Latch`), pressed by the player, by blocks, and by anything that implements `IPlatePresser`. Hold reverts its effects when released; Latch applies once and stays.
3. **Effects** behind a seam (`PlateEffect`, abstract `Apply`/`Revert`); one effect now, `RemoveObjectEffect` (deactivates a target object; revert reactivates).
4. **Gate** — a wall prefab the effect removes.
5. Test content on Sheet (0,0): one block, one Hold plate + gate, one Latch plate + gate.

**Out of scope:** pulling; chain-pushing (block into block stops — Aaron); pushing blocks into water; any UI/visual cue for refused unfolds beyond a log line; a visual for a pressed plate beyond a sprite swap; persistence of effect state (Aaron: gates do not reset; per-effect reset rules are his later call).

## 2. Design references

- Bible §9 — every element here is `[TENTATIVE]`; Aaron's request is the go-ahead. Built isolated: cutting any of them is deleting its component/prefab (§7).
- Bible §4 `[DECIDE]` #7 "objects ride the flap on unfold" — **answered: yes** (§3.1). A block is a rect on the flat sheet plus a side; the layers place it, so it rides with its paper by construction; on unfold a block on moved paper ends up on top (§6).
- Bible §4 `[DECIDE]` #11 straddling at unfold — **answered** (§3.2, §3.9): a block across a *crease* is on one piece of paper and is fine; a block across a Flap's *outer edge* (Seam) is on two pieces and the unfold is refused.
- Design Doc, Valid Folds: "partially covered moveable objects can be pushed fully under, or pulled out then pushed back on top of the fold" — **kept literally** (§3.10, §6 climbing rule).
- Bible §5 — covered Front content is hidden and non-interactive; the block's physical presence is its visible parts, clipped exactly like terrain (it uses the same coverage). Aaron's answer 4: pressure is a fact about the *sheet*, not about visibility — a block under a Flap still holds its plate.
- Bible §8 folds do not persist `[LOCKED]`; **Aaron (§3.11): leaving also resets blocks to their start**; gates/effects are not reset. Bible #8 (object persistence) is thereby answered for blocks only, provisionally.
- Bible §9 structural rule — ability is a flag consulted by an element: `Ability.Push` added; the block consults it. An element is a component; the one abstract class (`PlateEffect`) exists only so an open set of effects can be referenced from the Inspector (§10).
- Bible §3 not tile-based `[LOCKED]` — pushing is continuous.
- Bible §7 exits — a block never leaves its sheet (Aaron, §3.6): the boundary walls already enclose the walkable outline; blocks additionally never move their centre off the flat sheet (§6).
- Bible §2 vocabulary — no "paper" in code: `SheetPoint`, `SheetPlacement`, `flatRect` ("flat" = on the unfolded sheet).
- Implementation-guidelines §4a — every feel value is an Inspector field (§5).

## 3. Decisions already made by Aaron (2026-08-27)

1. Objects ride the paper: *"Yes."*
2. Straddling at unfold: *"you can't unfold if its on the edge, otherwise you have to deal with a block hanging off the edge of the map, and there's no clean way to deal with that."*
3. Plates: *"these are pressure plates, so the player can, these blocks can, and anything else that I want to be able to later should be able to."*
4. Covered plates: *"the buttons and the stuff on them don't disappear, they are just innaccessable because they are covered. So if there is a button held down by a block and its covered, its inaccessable, but its still holding the button down under the page."*
5. Ability: *"you can add the ability, ig, but I won't use it for now."*
6. Stops: *"Sheet edges, walls, and gated terrain should gate for blocks as well, just in case (for now they can't go into water)."*
7. Push-slow lives on the block: *"Block makes sense to me."*
8. Effects stay open: *"buttons have the option to have other effects than just removing a Gate."*
9. A crease through a block (round-1 Q1): *"if there is a block on the backside and you fold, bringing the backside to the front, but stop where the block is partially on the new front but not entirely … that should be fine - it should be partially shown, and still interactable. If you push it off the edge, it just rolls around back to the back of the page. This is fine because they are still fundamentally on the same side of the paper, even if you can only see partially. The issue with unfolding on a seam is when an object is on 2 sides of the paper at once, which causes trouble. But if its just on the crease, that should be fine."*
10. Pushing toward a Flap (round-1 Q2): *"It is allowed, but if its covered at all, it belongs to the side that is covered by the flap - so it should be partially covered, similar to how partially covered walls work. You can still push it, however. It can be pushed back under entirely, and be fully tucked in, or once pulling becomes a thing, it can be pulled out - if its pulled out, then it becomes part of the top, and if you push it over, it now goes on the top."*
11. Leaving the sheet (round-1 Q3): *"for now, leaving fully resets the page, moving the block back to where it was at the start of the screen. Gates shouldn't reset however (other effects of button presses might or might not reset, and that is determined by me.)"*
12. Block into block (round-1 Q4): *"stop"*.

Earlier, still binding: covered content is gone for collision and partial coverage is clipped exactly (2026-08-26); no overhang; multiple folds/stacking toggles; render-texture compositing behind `IFoldRenderer`.

## 4. Files

| File | Action | Purpose |
|---|---|---|
| `Scripts/Fold/SheetPoint.cs` | new | `SheetPoint` (Front-space point on the flat sheet + `SheetFace` side) and `SheetPlacement`, the pure rules that place sheet-resident movable content from `SheetLayers` (§5). |
| `Scripts/Fold/IFoldConstraint.cs` | new | Components under a sheet that may refuse an unfold. Keeps block rules out of `SheetFolds`. |
| `Scripts/Fold/SheetFolds.cs` | modify | Consults `IFoldConstraint`s in `TryUnfoldAt`; raises `Unfolding(int)` before a fold is removed; `FoldRejection` gains `ObjectOnEdge`. |
| `Scripts/Desk/SheetBoundary.cs` | modify | Tags walls that lie along a crease (`CreaseWall` marker) so blocks can roll across a crease. |
| `Scripts/Desk/CreaseWall.cs` | new | Marker component on a boundary wall that stands on a crease line. |
| `Scripts/Fold/FoldDragInput.cs` | modify | Logs `ObjectOnEdge` like `PlayerOnFlap`. |
| `Scripts/Fold/IFoldRenderer.cs` | modify | `float SurfaceZ(int layerIndex)`: sheet-local z at which something resting on that layer is drawn above it and below the next. |
| `Scripts/Fold/RenderTextureFoldRenderer.cs` | modify | Implements `SurfaceZ`; its private `MeshBuilder` moves out to `PolygonMeshBuilder` (with a per-polygon z) so the block can build the same kind of mesh. |
| `Scripts/Fold/PolygonMeshBuilder.cs` | new | The extracted builder. Behaviour unchanged for the renderer. |
| `Scripts/Screens/IArrivalObstacle.cs` | new | "Solid where the player would arrive" — replaces the `TerrainRegion` special case in `SheetOcclusion.SolidFootprints`. |
| `Scripts/Fold/SheetOcclusion.cs` | modify | `SolidFootprints` iterates active `IArrivalObstacle`s under Front. |
| `Scripts/Terrain/TerrainRegion.cs` | modify | Implements `IArrivalObstacle` (same answer as today). |
| `Scripts/Player/Ability.cs` | modify | `Push = 1 << 1`. |
| `Scripts/Player/PlayerMover.cs` | modify | `SpeedScale` (0..1, default 1) applied to the velocity; `MoveSpeed` getter. |
| `Scripts/Objects/PushRules.cs` | new | Pure: cardinal from a contact, push distance, hit clamp, ability check. |
| `Scripts/Objects/PushableBlock.cs` | new | The block: flat rect + side, placement from the layers, colliders, clipped mesh visual, `TryPush`, unfold constraint, arrival obstacle, reset. |
| `Scripts/Objects/BlockPusher.cs` | new | On the player: finds the block it walks into, pushes it, scales the player's speed. |
| `Scripts/Objects/IPlatePresser.cs` | new | `bool TryGetSheetPoint(Sheet sheet, out SheetPoint point)`. |
| `Scripts/Objects/PlayerPresser.cs` | new | The player as a presser: the top layer under the player's centre. |
| `Scripts/Objects/BlockPresser.cs` | new | A block as a presser: its flat centre and side, wherever the paper is. |
| `Scripts/Objects/PressureRules.cs` | new | Pure: pressed iff some presser's point is on the plate's flat rect and side. |
| `Scripts/Objects/PressurePlate.cs` | new | The plate: mode, effects, polling, sprite swap, occludee for its trigger. |
| `Scripts/Objects/PlateEffect.cs` | new | Abstract `Apply()` / `Revert()`. |
| `Scripts/Objects/RemoveObjectEffect.cs` | new | Deactivates a target GameObject; revert reactivates. |
| `Prefabs/Objects/Block.prefab` | new | Kinematic `Rigidbody2D`, `BoxCollider2D` (authoring footprint only; disabled at runtime), `PolygonCollider2D` (the live shape), `PushableBlock`, `BlockPresser`, `Visual` child (MeshFilter + MeshRenderer). |
| `Prefabs/Objects/Hold Plate.prefab`, `Latch Plate.prefab` | new | Trigger `BoxCollider2D`, `PressurePlate` (mode differs), SpriteRenderer. |
| `Prefabs/Terrain/Gate.prefab` | new | `TerrainRegion` wall (like Wall.prefab, different tint). |
| `Prefabs/Player.prefab` | modify | Adds `BlockPusher`, `PlayerPresser`. |
| `Sheets/Sheet (0,0).prefab` | modify | Adds one Block, one Hold Plate + `RemoveObjectEffect` → Gate A, one Latch Plate + effect → Gate B, on the Front. |
| `Tests/EditMode/SheetPlacementTests.cs` | new | Placement rules (§8). |
| `Tests/EditMode/PushRulesTests.cs` | new | Direction/clamp/ability rules. |
| `Tests/EditMode/PressureRulesTests.cs` | new | Pressed rule. |
| `Documents/Claude Bible.md` | modify | #7, #11 decided (provisional, quotes); #8 answered for blocks; §9 note that block/plate/gate exist as playtest elements; `Ability.Push`; §5 "pressure is a sheet fact"; the climb/under rule. |

All hand-authored YAML is checked with `python Tools/check_links.py`.

## 5. Components & data

### `SheetPoint` (readonly struct)
`Vector2 Point` — Front-space coordinates on the flat sheet; `SheetFace Side`. Two things share a surface iff sides agree and their points lie in the same layer's `Original`.

### `SheetPlacement` (static, pure)
- `int LayerContaining(SheetLayers layers, Vector2 flatPoint)` — index of the layer whose `Original` contains the point (ties on a crease line → highest index); −1 if off the sheet.
- `int TopLayerAt(SheetLayers layers, Vector2 deskPoint)` — highest-index layer whose `Desk` contains the point; −1 if no paper there.
- `bool IsUp(in Layer layer, SheetFace side)` = `layer.FrontUp == (side == Front)`; `SheetFace UpSide(in Layer layer)`.
- `Rect DeskRect(Rect flatRect, Isometry2D toDesk)` / `Rect FlatRect(Rect deskRect, Isometry2D toDesk)` — bounds of the transformed corners. Every fold isometry maps axes to axes (edge mirror, 45° swap, compositions), so the bounds are the transformed rect; asserted with a tolerance, error-logged once if violated.
- `struct Piece { ConvexPolygon Desk; int LayerIndex; }`
- `List<Piece> VisiblePieces(Rect flatRect, SheetFace side, SheetLayers layers, int climbTarget = -1)` — **the same computation as `SheetLayers.Coverage`** (for each layer showing `side` up: `(flatRect ∩ Original) → Desk`, minus every layer above), but keeping the layer index per piece, **plus the air pieces**: `(flatRect − sheet rect)` (the part of a climbed block that hangs past its Flap's Seam), transformed by the *glue layer's* `ToDesk`, minus layers above it, only if the block is up on the glue layer. Glue layer = `LayerContaining(flatRect.center)` (the centre is always on the sheet — §6). `climbTarget` (a layer the block is in the act of climbing onto, §6) is not subtracted from the glue layer's pieces: the block is on top of it, not under it; layers above `climbTarget` still cover.
- `bool OverhangsSheet(Rect flatRect)` — some of the rect is outside the sheet rect (the Seam-straddle state).
- `int PushLayer(Rect flatRect, SheetFace side, SheetLayers layers)` — the layer on which the block is up and present (the one its visible pieces are on; with one crease through a block exactly one of the two pieces is up). The glue layer if it qualifies, else the first qualifying layer; −1 if the block is not visible anywhere.
- `int HigherLayerOverlapping(Rect deskRect, SheetLayers layers, int glueLayer)` — the topmost layer above `glueLayer` whose `Desk` overlaps the rect; −1 if none. `bool CentreOn(Vector2 deskCentre, in Layer layer)` — the layer's `Desk` contains the point. Climbing (§6) uses both: the approach is detected by overlap, the transition by the centre entering the layer, so a re-glued block's flat centre is always inside that layer's `Original` — on the sheet.

### `IFoldConstraint`
```csharp
public interface IFoldConstraint
{
    /// Why committed fold `foldIndex` may not be undone now (Layers/Effects still hold it), or None.
    FoldRejection RefuseUnfold(int foldIndex);
}
```
Folds are never refused because of blocks (Aaron, §3.9), so there is no `RefuseFold`. `SheetFolds.TryUnfoldAt` asks every active constraint under the sheet after `FoldHitTest.SeamAt` finds the fold. New `FoldRejection.ObjectOnEdge`: *"an object riding the Flap hangs past its edge; unfolding would carry it off the sheet."*

### `CreaseWall` (marker) / `SheetBoundary`
`SheetBoundary.Rebuild` adds `CreaseWall` to a wall whose outline segment lies along a crease segment of any committed fold's `FoldEffect` (collinear within `1e-3` and overlapping). Blocks ignore these walls when cast (§6): paper continues across a crease, so a block rolls around it; the player still cannot (empty desk beyond).

`SheetFolds` new event: `event Action<int> Unfolding` — raised just before fold `index` is removed, while `Layers` still holds it; **not** raised by `Reset` (blocks reset themselves to their start on `Sheet.PlayerLeft`, §6).

### `IFoldRenderer.SurfaceZ(int layerIndex)`
Sheet-local z for content resting on layer `layerIndex`: above that layer's mesh, below the next. `RenderTextureFoldRenderer`: `BaseZ − layerIndex × LayerZStep − LayerZStep × 0.5f`.

### `PolygonMeshBuilder`
The renderer's `MeshBuilder`, extracted unchanged, plus `AddPolygon(polygon, uvOf, float z)` (the old overload passes 0). The renderer keeps using it exactly as before.

### `IArrivalObstacle`
`bool TryGetSolidFootprint(PlayerAbilities player, out Rect sheetLocal)`. `TerrainRegion`: its face rect if not passable. `PushableBlock`: its flat rect if `side == Front` (a sheet that is not the Screen is flat, so flat = desk). `SheetOcclusion.SolidFootprints` iterates `Front.GetComponentsInChildren<IArrivalObstacle>(includeInactive: false)` — the `false` also stops a removed (inactive) gate counting as solid at arrival.

### `Ability.Push = 1 << 1`

### `PlayerMover`
`float SpeedScale { get; set; }` clamped 0..1, default 1, multiplies the velocity (state, not serialized). `float MoveSpeed => moveSpeed`.

### `PushRules` (static, pure)
- `bool TryCardinal(Vector2 v, float minAxisFraction, out Vector2 cardinal)` — dominant axis as a unit vector if it holds ≥ `minAxisFraction` of the magnitude.
- `Vector2 IntoBlock(Vector2 contactNormal, Vector2 pusherCentre, Vector2 blockCentre)` — the normal oriented from the pusher into the block.
- `float PushDistance(Vector2 velocity, Vector2 direction, float dt)` = `max(0, dot) × dt`.
- `float ClampToHit(float wanted, float hitDistance, float skin)` = `min(wanted, max(0, hitDistance − skin))`.
- `bool CanPush(Ability owned, bool requiresAbility, Ability required)` = `!requiresAbility || (owned & required) == required`.

### `PushableBlock` (`[RequireComponent(Rigidbody2D, BoxCollider2D)]`, `[DefaultExecutionOrder(-5)]`) — implements `IFoldOccludee`, `IFoldConstraint`, `IArrivalObstacle`
Inspector:
- `[Header("Pushing")] [Range(0, 1)] float pushSpeedFactor = 0.5f` — *"The pusher's speed while pushing this block, as a fraction of their normal speed. 1 = no slowdown."*
- `bool requiresAbility = false` — *"Only a pusher holding Push Ability can move this block."*; `Ability pushAbility = Ability.Push` — *"The ability needed when Requires Ability is on."*
- `[Header("Drawing")] MeshFilter visual` — *"Child that draws the block, clipped to the parts of it that are visible; posed by the block."*; `Material material` — *"URP/Unlit transparent; _BaseMap is replaced by Texture and _BaseColor by Tint."*; `Texture texture`; `Color tint = white`.
- `[Header("Physics")] [Min(0)] float skin = 0.01f` — *"Gap kept between the block and what it is pushed against, in world units. Author blocks at least this far from walls."*

State: `Rect startFlatRect; SheetFace startSide;` (authored); `Rect flatRect; SheetFace side;` (current); `int glueLayer; int climbTarget; List<Piece> pieces; bool ridingUnfold;` plus `Sheet sheet; SheetFolds folds; IFoldRenderer renderer; Rigidbody2D body; BoxCollider2D authored; PolygonCollider2D shape; ScreenNavigator navigator;` and the mesh builder. The block's transform is never rotated or scaled (every desk footprint is an axis-aligned rect; the polygon paths and the mesh are computed explicitly), so local = sheet-local − desk centre.

Public: `SheetPoint Centre` (flat centre + side), `Rect FlatRect`, `bool IsVisible` (any piece), `bool TryPush(Vector2 cardinal, float distance, PlayerAbilities pusher, Rigidbody2D pusherBody, out float moved)`, `float PushSpeedFactor`.

### `BlockPusher` (on the player, `[RequireComponent(PlayerMover, Rigidbody2D, PlayerAbilities)]`, `[DefaultExecutionOrder(-10)]` — before `PlayerMover.FixedUpdate`)
Inspector: `[Range(0, 1)] float pushInputThreshold = 0.3f` — *"Move input component into the block needed to count as pushing."*; `[Range(0.5f, 1)] float pushFaceAxisFraction = 0.9f` — *"How axis-aligned the contact must be to count as a block face."*

### `IPlatePresser` / `PlayerPresser` / `BlockPresser`
`bool TryGetSheetPoint(Sheet sheet, out SheetPoint point)`. `PlayerPresser` (`[RequireComponent(PlayerMover)]`): false unless `sheet.IsScreen`; else `TopLayerAt` the player's sheet-local centre → `(ToDesk.Inverse(centre), UpSide(layer))`; false if no paper there. `BlockPresser` (`[RequireComponent(PushableBlock)]`): false unless the block is under `sheet` and active; else `block.Centre`.

### `PressureRules` (static, pure)
`bool IsPressed(Rect plateFlatRect, SheetFace plateSide, IReadOnlyList<SheetPoint> pressers)` — any presser with the same side whose point is inside the rect.

### `PressurePlate` (`[RequireComponent(BoxCollider2D)]`, `[DefaultExecutionOrder(10)]`) — implements `IFoldOccludee`
Inspector: `PlateMode mode = Hold` (`{ Hold, Latch }`, tooltips: Hold *"pressed while something is on it; effects revert when it is released"*, Latch *"the first press applies the effects for good"*); `PlateEffect[] effects` — *"Applied when pressed (in order); Hold plates revert them (reverse order) when released."*; `[Header("Drawing")] SpriteRenderer spriteRenderer; Sprite releasedSprite; Sprite pressedSprite` — *"Optional; if both are set the renderer swaps between them."*
Public: `bool IsPressed`, `bool IsLatched`, `event Action<bool> PressedChanged`. The trigger box is clipped by the occludee like terrain (so anything physics-driven sees only the visible part); pressing itself is geometric (§6).

### `PlateEffect` (abstract MonoBehaviour)
`public abstract void Apply(); public abstract void Revert();` — `Revert` restores what `Apply` changed. New effects are new subclasses; nothing branches on effect kinds.

### `RemoveObjectEffect : PlateEffect`
`GameObject target` — *"Deactivated when the effect applies, reactivated when it reverts."* Error at Awake if null (then no-ops).

## 6. Behaviour

### The block is sheet content
Authored like any content: under Front (or Back) root at its place on that face. At `Awake`: `startFlatRect` = its `BoxCollider2D` through the face root (`FoldFootprint.FaceLocalRect`, then `SheetGeometry.BackToFront` for Back), `startSide` = that face; the face root must be at identity relative to the sheet (asserted; error otherwise) so sheet-local = face-local numerically. From then on the transform is posed on the desk wherever the block's paper lies; the authored pose is only `startFlatRect`/`startSide`.

`Place(bool pose)` — run from `OnFoldCoverageChanged` (the occludee notification: fold change, Screen enter/leave, start), from `OnEnable`, from the reset handler, and after every push (`pose = false`, §pushing). The `CoverageResult` argument of the notification is ignored (it is computed for the authored face and the block may have changed side) — documented on the method; `FaceLocalFootprint` returns the current `flatRect` in the authored root's face space so the report is at least meaningful when side matches. Any fold-driven placement clears `climbTarget`.
1. `glueLayer = LayerContaining(layers, flatRect.center)`; −1 cannot happen (the centre never leaves the sheet, §pushing) — error and go inert if it does.
2. `pieces = VisiblePieces(flatRect, side, layers, climbTarget)`; `deskCentre = glue.ToDesk(flatRect.center)`.
3. Pose (when `pose`): `body.position = world(deskCentre)` **and** `transform.position` likewise (a `Rigidbody2D.position` write reaches the Transform only at the next step — `PlayerMover.PlaceAt` does the same). Interpolation: `Interpolate` while `sheet.IsScreen && !navigator.IsTransitioning`, else `None` (checked in `Update`, set on change).
4. Colliders: `shape` (the `PolygonCollider2D`) gets one path per piece in block-local space = piece vertices − `deskCentre` (sheet-local; the block sits unrotated under an identity face root, asserted at Awake). Enabled iff `sheet.IsScreen` and there are pieces. The authored `BoxCollider2D` is disabled at Awake and only ever read.
5. Visual: rebuild the mesh from `pieces`: each polygon in block-local space, UV of a desk vertex = its flat coordinates through that piece's layer inverse (air pieces: the glue layer), normalised within `flatRect`; z per piece = `renderer.SurfaceZ(piece.LayerIndex)` − the block's own local z. Material property block: `_BaseMap = texture`, `_BaseColor = tint`. The visual is on `Default` (drawn by the main camera over the composited sheet; a Flap mesh above a piece's layer draws over it). `Sheet.Awake` puts the subtree on `SheetFront`/`SheetBack`; the block resets its subtree to `Default` in `Start` (always after every `Awake`) — commented.

### Folds and blocks
- **Folding never refuses because of a block.** A crease through a block splits it like terrain: one part on the lifted piece, one on the staying piece; exactly one of the two is up (they differ by one flip). For a Back block that is the Flap part — "partially shown and still interactable" (Aaron, §3.9). For a Front block on the Base both parts end up out of sight (the lifted part face-down, the staying part under the landed Flap) until unfolded — the same as a wall cut by a crease. A block wholly on lifted paper flips with it: Front-up → face-down (hidden, no colliders, still holding any plate under it); face-down or Back-authored → exposed on the landed Flap, mirrored. A block under a *landing* Flap keeps its flat rect; its visible parts shrink to what is not covered ("belongs to the side covered by the flap", §3.10). No block ever changes `flatRect`/`side` because of a fold.
- **Unfold refusal** (`RefuseUnfold(i)`): `layers[glueLayer].MovedBy == i && OverhangsSheet(flatRect)` → `ObjectOnEdge` (a climbed block hanging past its Flap's Seam — "on 2 sides of the paper at once"). A crease straddle is not refused.
- **`Unfolding(i)`** → `ridingUnfold = layers[glueLayer].MovedBy == i`. Then on the notification, if `ridingUnfold`: first `side = UpSide(layers[new glueLayer])`, then `Place()` — a block on paper the fold moved ends up on top where that paper returns to (pushed onto a Flap → comes back on the Front at the mirrored place, the Design Doc's "push a block across a fold, unfold, now it's on the other side"; lifted under a Flap → back up where it was; Back-authored and exposed → arrives on the Front). **Contested by review round 2 (B1)** — the alternative, "a block keeps its face", would leave a block pushed onto a Flap hidden under the sheet after the unfold, which the Design Doc toolkit rules out; the Back-authored-block consequence is flagged to Aaron (§10). `Changed` fires before the retreat animation, so the block appears at its new place at the start of the swing-back, under the retreating mesh until it passes — accepted (manual test 6).
- **Reset** (`Sheet.PlayerLeft`, Aaron §3.11): `flatRect = startFlatRect; side = startSide; climbTarget = -1;` then `Place()` itself (event order between subscribers is unspecified). Effects are untouched; a Hold plate the block was holding releases as a consequence of the block leaving it (hold semantics, not reset) — flagged to Aaron.

### Pushing
`BlockPusher.FixedUpdate` (before `PlayerMover`):
1. If `!mover.MovementEnabled`: `SpeedScale = 1`, clear, return.
2. `body.GetContacts(contacts)`; for each contact whose collider's `attachedRigidbody` has a visible `PushableBlock`: `d = IntoBlock(normal, playerCentre, block desk centre)`; require `TryCardinal(d, pushFaceAxisFraction, out cardinal)` and `dot(MoveInput, cardinal) ≥ pushInputThreshold`. First match wins.
3. Pushing (matched, whether or not the block can move): `SpeedScale = block.PushSpeedFactor`; `velocity = MoveInput × MoveSpeed × SpeedScale`; `distance = PushDistance(velocity, cardinal, dt)`; `block.TryPush(cardinal, distance, abilities, body, out _)`. A blocked block (moved 0) still counts as pushing: the slow stays (N3). If `CanPush` fails, the block is a wall: no slow.
4. No match → `SpeedScale = 1`.

`PushableBlock.TryPush`:
1. Refuse (false, 0) if not visible, not on the Screen, or `!CanPush`.
2. `pushLayer = PushLayer(flatRect, side, layers)`; the flat direction `f = layers[pushLayer].ToDesk.Inverse.ApplyVector(cardinal)` (a unit axis vector again).
3. Obstacles: `body.Cast(cardinal, filter{useTriggers=false}, hits, distance + skin)`; ignore hits whose `rigidbody` is the pusher's, hits on a `CreaseWall` (paper continues across a crease — §3.9 "rolls around"), and hits with `distance ≤ 0` (an overlap at the start — only by authoring flush against something, or a Hold gate reverting onto the block: both are authoring rules, stated in the `skin` and `RemoveObjectEffect` tooltips); `distance = ClampToHit(distance, nearest, skin)`. Boundary walls (sheet edges, landed edges over empty desk), terrain (gated too: it only ignores the player), gates and other blocks are solid; the player's exit trigger and plates are triggers. If `distance ≤ 0` → false.
4. Centre rule: the new flat centre must stay inside the sheet rect (a block never leaves the paper); otherwise shorten `distance` to where it would leave. If that gives `≤ 0` → false.
5. **Climbing and descending** (Aaron §3.10, made symmetric so no state is a dead end): with `newDesk` the moved desk rect —
   - if `climbTarget < 0` and the block was **wholly clear** of every layer above `glueLayer` before the move and `HigherLayerOverlapping(newDesk) = M ≥ 0`: `climbTarget = M` (the block is sliding *onto* M; its overlap is drawn and collides on top of M, not under it).
   - if `climbTarget = M ≥ 0` and `CentreOn(newDesk.center, M)`: **re-glue** onto M — `side = UpSide(M)`, `flatRect = FlatRect(newDesk, M.ToDesk)` (the part past M's Seam maps off the sheet: the air piece), `climbTarget = -1`. The flat centre is inside M's `Original` by construction.
   - if `climbTarget = M ≥ 0` and the moved rect no longer overlaps M: `climbTarget = -1` (backed off).
   - if the block is up on `glueLayer` = G, G is not the Base (`MovedBy ≥ 0`), `OverhangsSheet(flatRect)` (it hangs past G's Seam) and after the move `!CentreOn(newDesk.center, G)`: **descend** — re-glue to `TopLayerAt(newDesk.center)` (the layer under the centre now) with `climbTarget = G` (so the part still over G is drawn on top of G while it slides off), `side = UpSide(that layer)`, `flatRect = FlatRect(newDesk, its ToDesk)`.
   - otherwise `flatRect.position += f × distance` — a partly covered block slides further under; a block on a Flap pushed across the crease rolls onto the underside and hides.
6. `Place(pose: false)` (the body must not be teleported in the step it is moved); `body.MovePosition(world(new desk centre))` so the physics moves the kinematic body this step and the player follows it; `moved = distance`.

### Plates
`PressurePlate.FixedUpdate` (after blocks): collect `SheetPoint`s from every `IPlatePresser` under its sheet (cached at `Start`/`OnEnable`) plus the `PlayerPresser` (found once); `pressed = PressureRules.IsPressed(flatRect, side, points)`.
- **Hold**: false→true `Apply` all effects in order; true→false `Revert` in reverse order.
- **Latch**: first false→true `Apply` all, `IsLatched = true`; nothing after.
- `PressedChanged` raised, sprite swapped. Geometric, so a covered plate with a block on it stays pressed (§3.4); the player presses only a plate on the surface they stand on. "On" = the presser's centre inside the plate's rect (stated in the plate's tooltip; flagged §10).

### Gate
`Gate.prefab` is a `TerrainRegion` wall with its own tint. Authoring rule (tooltip on `RemoveObjectEffect`): a Hold gate must not be able to close onto the player or a block — place plates so nothing can stand in the gate while it is open. Removed = inactive GameObject: collider gone, sprite gone (face content), `SheetOcclusion` keeps notifying it (`GetComponentsInChildren(true)`) so it has the current clip when reactivated; `TerrainRegion.OnEnable` re-establishes the ability ignore-pair.

### Failure modes
- Block not under a Sheet / face root not at identity / no `visual` / no `material`/`texture` / sheet without `IFoldRenderer`: error, block disabled.
- Block centre finds no layer: error, inert until the next placement finds one.
- Non-axis-aligned desk rect (impossible by construction): error once, bounds used.
- Plate with no effects: warning once; null entry: error, skipped. `RemoveObjectEffect` without target: error, no-op.
- `BlockPusher` missing `PlayerAbilities`: error, disabled.

## 7. Interfaces & seams

- `IFoldConstraint` — the only touch on `SheetFolds`; delete the block and there are no constraints. `Unfolding` is a general event.
- `IFoldOccludee` — the block is an occludee that responds by placing itself (the interface remark names this as where a moving object hooks in).
- `IFoldRenderer.SurfaceZ` — the renderer stays the only thing that knows z layout.
- `PolygonMeshBuilder` — shared geometry→mesh helper; no behaviour change for the renderer.
- `IArrivalObstacle` — generalises the terrain special case.
- `IPlatePresser` / `PlateEffect` — the two open sets Aaron asked for.
- Cutting: blocks = delete `PushableBlock`, `BlockPusher`, `BlockPresser`, `PushRules`, `Block.prefab`, `Ability.Push`; plates = delete `PressurePlate`, `PlateEffect`, `RemoveObjectEffect`, `PressureRules`, `IPlatePresser`, `PlayerPresser`, `BlockPresser`, the plate prefabs; gate = `Gate.prefab`. `SheetPoint`/`SheetPlacement`, `IFoldConstraint`, `IArrivalObstacle`, `SurfaceZ`, `SpeedScale`, `PolygonMeshBuilder` stay as small general seams.

## 8. Testing

Edit Mode (managed math only):
- `SheetPlacementTests`: `LayerContaining`/`TopLayerAt` on `Flat`, after an edge fold and a corner fold; `DeskRect`/`FlatRect` round-trip and axis alignment; `VisiblePieces`: block under a landed Flap (partial, on the base layer), wholly under (none), on the Flap (whole, on the Flap layer), crease through a Front block (none), crease through a Back block (one piece, on the Flap), climbed block past the Seam (one sheet piece + one air piece, both on the Flap layer), a climbing block (`climbTarget` set: its overlap with the Flap is a piece on the base layer, not subtracted); `OverhangsSheet`; `PushLayer` for the straddle cases; `HigherLayerOverlapping`/`CentreOn`; a re-glue after climbing keeps the flat centre on the sheet; the side-after-unfold rule through a real sequence: fold → block "climbs" (state set as after a climb) → unfold → `UpSide` gives Front and `DeskRect` is the mirrored place; `IsUp`/`UpSide`.
- `PushRulesTests`: `TryCardinal`, `IntoBlock` both orientations, `PushDistance`, `ClampToHit`, `CanPush`.
- `PressureRulesTests`: same side inside → pressed; other side → not; outside → not; empty → not.
- CLI compile of runtime + tests; existing 131 tests still pass (the renderer refactor must not change them — they do not touch the renderer, so a compile is the check there).

Manual, Desk scene:
1. Push the block from each side; slowed; diagonal slides; stops at walls, the gate, the sheet edge, another block (if two are placed); no slow when leaning on a block through a wall.
2. Hold plate: gate A opens while stood on, closes off it. Latch: gate B opens and stays.
3. Push the block onto the Hold plate; walk away: gate A stays open.
4. Fold a Flap over the block-on-plate: block hidden, gate A still open. Unfold: block back.
5. Author (temporarily) a Back block; fold so the crease splits it: the Flap part is shown and pushable; push it across the crease: it rolls under and hides. Fold a Front block's crease: it disappears (lifted half face-down, other half under the Flap) and returns on unfold.
6. Push the block into a landed Flap's edge from clear: it slides on top of the Flap continuously; half on: unfold refused (log); push it fully on; unfold: the block appears at the mirrored place on the Front at the start of the swing-back (expected). Push a climbed block back over the Seam: it slides down onto the Base and, once clear, can be pushed under-or-onto again.
7. Fold a Flap onto part of the block: partially covered; push it further under until fully covered; unfold: it is back.
8. Fold so the block's own paper lifts: it goes under; unfold: back where it was.
9. Leave the sheet with the block moved (and half on a Flap): return — block at its start, gates as they were, hold gate closed.
10. Corner fold with the block on the Flap: drawing rotated with the paper; pushing works along desk axes.

## 9. Assumptions (engineering)

- Every fold isometry is axis-preserving; a block's desk footprint is always an axis-aligned rect. Asserted.
- Face roots are at identity relative to the sheet. Asserted.
- `Rigidbody2D.GetContacts` reflects the previous step; one step of first-contact latency is imperceptible.
- A kinematic body moved by `MovePosition` in the step the player's velocity is set moves in lockstep with the player.
- Blocks do not spawn or reparent at runtime: constraint/presser lists are collected at `Start`/`OnEnable` (plates) and per call (`SheetFolds`).
- Two blocks cannot both be visible pieces of one crease-split block; with stacking a block split by a crease could in principle have pieces on layers that differ by two flips — `PushLayer` takes the glue layer if up, else the first up layer; accepted rough edge.
- Placeholder art: block texture = `Placeholder Square.png` tinted; plate sprites the same square in two tints; gate = wall square tinted.

## 10. Open questions / flags (non-blocking; proceeding as stated)

- **Face after unfold** (review round 2, B1 — contested): a block on paper a fold moved surfaces to the face that is up after the unfold. Consequence Aaron has not explicitly ruled on: a Back-authored block that is exposed by a fold and then unfolded *untouched* becomes a Front block. The alternative (keep the face) would hide a block pushed onto a Flap after the unfold, against the Design Doc toolkit. Built as stated; Aaron can flip it.
- **Seam crossing is symmetric**: a block climbs onto a Flap when its centre crosses the Seam inward and slides back down when it crosses outward. Not asked; chosen so no state is a dead end.
- **Crease walls are ignored by blocks in every direction** (the sheet continues across a crease). Consequence (code review Q1, flagged): a Front block that climbs onto a landed Flap and is pushed on across the Flap's crease rolls onto the Base's underside — it vanishes (a hidden Back block) until that region is folded again or the sheet resets. That is the literal reading of "rolls around back to the back of the page"; Aaron may prefer the crease to stop blocks that are on top of a Flap.
- **"On" a plate = the presser's centre inside the plate.** In the tooltip.
- **Hold gate release on reset** and **gates never closing onto something** are consequences/authoring rules, flagged.
- `PlateEffect` is an abstract MonoBehaviour rather than an interface, solely for Inspector references.

## Review (round 1, plan-reviewer, 2026-08-27)

Verdict: BLOCK. Dispositions:

- **B1** (`CutsObject` was an unasked rule) — escalated; Aaron: folds are never refused for blocks (§3.9). **Removed.**
- **B2** (re-glue-by-centre contradicted "pushed fully under") — escalated; Aaron §3.10. **Model replaced** by the flat-content model with the climbing rule (§6).
- **B3** (`Reset` bypassed the unfold refusal → block off the sheet) — escalated; Aaron §3.11: blocks reset to their start. **Fixed.**
- **N1** vocabulary — accepted: `SheetPoint`, `SheetPlacement`, `flatRect`, `TryGetSheetPoint`.
- **N2** transform writes vs `MovePosition` — accepted: `body.position/rotation` for placement, `MovePosition` for pushes.
- **N3** blocked push reset the slow — accepted.
- **N4** interpolation — accepted (interpolate while Screen and not transitioning).
- **N5** zero-distance overlaps in the cast — accepted (ignored; authoring rule in the tooltip).
- **N6** double placement path — accepted: occludee notification only; `FaceLocalFootprint` returns the flat rect in the authored face's space.
- **N7** teleport at unfold start — accepted, documented.
- **N8** `pushDelay` — dropped.
- **N9** tests for the chains — accepted (§8).
- **N10** `MovementEnabled` guard — accepted.
- **N11** `None` inversion — accepted: `requiresAbility` + `pushAbility`.
- **Q4** — Aaron: stop.

## Review (round 2, plan-reviewer, 2026-08-27)

Verdict: BLOCK. Two rounds is the workflow limit; dispositions and what stays contested:

- **B1** (face after unfold is an inference) — **rejected, flagged**: Aaron answered "Yes" to the pre-plan question that asked exactly whether a block pushed onto a Flap "travels to the other side of the sheet" on unfold, and the Design Doc toolkit says the same. The reviewer's Back-block consequence is real and is flagged in §10 and the report. Contested.
- **B2** (crease walls stop the block) — **accepted**: `CreaseWall` marker from `SheetBoundary`; the block's cast ignores them. Q4 taken as symmetric (flagged).
- **B3** (`body.position` does not reach the Transform before the polygon paths are built) — **accepted**: transform written alongside; the block builds its own polygon paths in block-local space (unrotated), no `OccludedBoxCollider`.
- **B4** (climb re-glue could put the flat centre off the sheet) — **accepted**: approach by overlap (`climbTarget`), transition by the centre entering the layer.
- **N1** (`Place` pose + `MovePosition` in one step) — accepted: `Place(pose: false)` on the push path.
- **N2** (side set after `Place`) — accepted.
- **N3** (Front block cut by a crease is fully hidden) — accepted; text and test 5 fixed.
- **N4** (reset relies on subscriber order) — accepted: the reset handler places.
- **N5** (gate reverting onto a block) — accepted as an authoring rule (tooltip); no runtime rule.
- **N6** (plate trigger + occludee is dead physics) — **rejected**: the box is how every element's footprint is authored here (Bible §3 terrain decision); the clip is three lines and keeps `ReportUnseenColliders` honest.
- **N7** (no rotation/scale on the body) — accepted.
- **N8** (Bible "decided" wording) — accepted: recorded as provisional with the open flags listed.
- **N9** (centre-on-plate rule unstated) — accepted: tooltip; flagged.
- **Q2** (block pushed toward the Seam from on top) — taken as symmetric descend (§6), flagged. **Q3** — centre rule, flagged. **Q4** — symmetric crease crossing, flagged.

## Code review (code-reviewer, 2026-08-27)

Verdict: REJECT (two must-fix). Dispositions:

- **M1** (derived fileID collision between the plates' Transforms and the gates' GameObjects — the `target` overrides would resolve to the wrong object) — **accepted**: nested instance IDs regenerated as multiples of 100000 (pairwise XORs far above any source fileID), the existing wall instances renumbered the same way (same latent collision), and `Tools/check_links.py` now reports duplicate derived IDs.
- **M2** (no `PlayerPresser` found is silent) — accepted: error once.
- **S1** (unpushable block still slows) — accepted: `CanBePushedBy`; the pusher only slows for a pushable block.
- **S2** (sprite swap with identical sprites) — accepted: pressed/released are colour tunables on the plate.
- **S3** (`BackToFront(Rect)` copied thrice) — accepted: `SheetGeometry.BackToFront(Rect)`.
- **S4** (one mesh at two depths sorts wrongly mid-climb) — accepted: one renderer per surface layer (pooled children of the Visual).
- **S5** ("paper" in comments) — accepted.
- **S6** (bare layer 0) — accepted: named constant.
- **S7** (wrong claim about crease reach) — accepted; §10 corrected.
- **Q1** (climbed Front block across the crease vanishes to the Back) — kept as built (it is Aaron's "rolls around" reading); flagged in the report.
- **Q2** (block art mirrors with the sheet) — kept as built for placeholder art; flagged.

### Re-review (code-reviewer, 2026-08-27)

Verdict: APPROVE WITH FIXES. All four should-fixes accepted and applied: a fired Latch plate keeps its pressed colour across disable/enable (`ShownPressed`); a disabled block hides its drawing as well as its collider; `SheetGeometry.BackToFront(Rect)` has a direct test; pooled drawing parts copy the Visual's rotation and scale. Final: CLI compile OK, 159 EditMode tests pass, `check_links.py` ALL LINKS OK.

## Follow-up (Aaron's playtest, 2026-08-27)

Two issues reported after playing:

1. *"if I put the block near the edge of the unfolded paper, then fold that edge just a little bit so it partially covers the block ... then I push the block down under the paper. This pushes the block past where the edge of the whole sheet would be ... locks an invisible collision box in place ... and I am unable to unfold that fold."* — Cause: blocks ignored crease walls in every direction, so a block *inside* a fold could be pushed across the crease in flat space, past the sheet edge (its overhang was then drawn/solid as an "air" piece) and re-glued to the Flap (refusing the unfold). Fix: a block with a layer over it stays within its own piece of the sheet (`SheetPlacement.MaxTravelInside` against the push layer's Original — the crease is a dead end from inside); a block on the Base stays within the sheet rect; air pieces exist only for a block glued to a Flap. Aaron's roll-around still applies to a block on *top* of a Flap.
2. *"When the block is pushed from off a folded flap to on it, then I unfold ... It needs to ride the flap all the way around to the back when that unfold happens."* — The contested "surfaces to the up face on unfold" rule (review round 2 B1) is removed: the flat rect and side never change because of a fold or unfold. `SheetFolds.Unfolding` and the block's riding flag are deleted. Bible §4 #7 updated. Tests updated (`UnfoldRide_BlockOnTheFlap_KeepsItsFace_...`, `MaxTravelInside_*`).

### Follow-up review (code-reviewer, 2026-08-27)

Verdict: REJECT → fixed. **M1** (the climb trigger could fire from the rolled-around state, mirroring the block back onto the Flap every push) — accepted: the climb decision is now `SheetPlacement.ClimbOnto` (requires the block to be up on its glue layer and wholly clear before the move) and the bound choice is `SheetPlacement.PushBound`, both pure and tested (163 EditMode tests pass). **S1** (stale class remark) — fixed. **S2** (decision logic untested) — accepted via the extraction. Q1 (a block taller than a thin Flap cannot be fully tucked), Q2 (a crease-straddling covered block cannot move toward the crease), Q3 (a block on top of a Flap rolls under across the crease) — reported to Aaron.

## Follow-up 2 (Aaron's playtest, 2026-08-27): the block follows the displayed Flap

Aaron: *"Instead of appearing as the fold happen, it should follow the flap as it is dragged across."* The block only knew the committed folds, so during a drag preview (and the unfold retreat) it stayed put and hidden, then popped in on commit. Fix: `SheetFolds.Draw` now replays the displayed folds once (committed + retreating + preview) and publishes them — `FoldDisplay` carries `Layers`/`Effects` (the renderer no longer replays on its own), `SheetFolds.DisplayLayers` / `DisplayIsCommitted` / `event Displayed`. `PushableBlock.DrawVisual` draws from `DisplayLayers` on every `Displayed` while its colliders and body stay on the committed `Layers`; the mesh is built relative to the body's committed place. A mid-climb block (`climbTarget`) is drawn without the climb adjustment while something is previewed (layer indices only agree with the committed stack then). The block is not tinted with the preview/invalid tints (the Flap under it is) — a possible later polish.

### Follow-up 2 review (code-reviewer, 2026-08-27)

Verdict: APPROVE WITH FIXES; all accepted. **S1** stale `FoldDisplay` doc — fixed. **S2** the climb adjustment vanished during any preview — the climbed Flap is now found again in the displayed stack (`SheetPlacement.SameLayer`, by fold, face, piece and pose; tested). **S3** per-frame allocations — `PolygonMeshBuilder.AddPolygon` takes an origin. **S4** wasted mesh build between `Changed` and `Draw` — `Place(draw: false)` on the coverage notification; the `Displayed` that follows draws. **Q1** (block untinted during a red preview) — left as later polish, reported to Aaron.

## Follow-up 3 (Aaron's playtest, 2026-08-27): plates did not remove their gates

Cause: the plates' `RemoveObjectEffect.target` overrides in Sheet (0,0) referenced the gates' derived GameObject fileIDs without a `stripped` declaration; Unity resolves such references to null (it declares a stripped block for every referenced object of a prefab instance — see `&9002 stripped` in Desk.unity), so both effects logged "no target" at Awake and did nothing. Fix: the generator emits `--- !u!1 &<id> stripped` GameObject blocks for the gates; `Tools/check_links.py` now fails on a locally referenced derived id with no stripped block. No code change.
