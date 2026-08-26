# Plan: screen transition slides the sheets, not the camera

## 1. Task

Aaron: *"Can we change the screen transition from moving the camera to moving the levels? Because I have a background that I want to keep in place, and it seems easier to deal with keeping everything I want still still, and then just moving the screens."*

Change the screen transition so the camera and the Desk (surface/background, later UI) stay fixed and the grid of Sheets slides so the destination Sheet lands under the camera. The visual read is unchanged; what moves is different.

Out of scope: any change to when a transition triggers (`Exit`), where the player arrives (`EntryPosition`), the framing size, the zoomed-out map view, or the background art itself.

## 2. Design references

- Design Doc, Map: "Moving between screens shifts all the papers in the grid so that the player is in the new spot." Art: "The whole game will take place on a big desk, with the grid of paper laid out on top of it, and sliding around."
- Bible §2: **Desk** = "The world space containing the grid of sheets." So the Desk does not move; the sheet grid on it does.
- Bible §7: "Moving between screens **slides the sheets** so the new sheet is centred. The camera framing is the sheets moving, not the camera moving. **[DECIDE]** whether that's literally true in implementation or just the visual read." — **Resolved by Aaron in this task: literally true.** The Bible §7 entry is updated to record it (provisional, 2026-08-25), same style as §3/§6.
- Bible §7 "Desk margins hold UI." — a fixed Desk is where that UI and the background live.
- Bible §8 [LOCKED] folds don't persist — untouched; `Sheet.PlayerLeft/PlayerEntered` still fire in the same order.
- Bible §7 [DECIDE] #15 (zoomed-out view) — untouched; a fixed camera makes a later zoom a camera-size change only.
- No [TENTATIVE] items involved.

## 3. Decisions already made by Aaron

- The sheets move; the camera and the background stay still. (This task.)
- Existing tunables (`verticalMargin`, `slideDuration`, `entryInset`) keep their meaning and defaults.

## 4. Files

| File | Change |
|---|---|
| `Assets/Papercut/Scripts/Desk/Desk.cs` | Gains a serialized `sheetGrid` Transform: the child under which sheets are laid out. `GridToLocal`/`GridToWorld`/`LayoutSheets` are relative to it. |
| `Assets/Papercut/Scripts/Desk/DeskSlider.cs` (+ `.meta`) | **New.** Moves the Desk's sheet grid so a given Sheet sits under the view; owns the slide coroutine, `slideDuration`, `slideEase`. |
| `Assets/Papercut/Scripts/Desk/Sheet.cs` | `OnValidate` positions relative to the Desk's sheet grid (unchanged call, `GridToLocal`; only its meaning changes). Remarks: a Sheet is a direct child of the Desk's SheetGrid. |
| `Assets/Papercut/Scripts/Screens/ScreenCamera.cs` | Reduce to framing only. Remove `FrameSheet`, `SlideTo`, `IsSliding`, `slideDuration`. Update remarks. |
| `Assets/Papercut/Scripts/Screens/ScreenNavigator.cs` | Require and use `DeskSlider` instead of a `ScreenCamera` reference; wait on `IsSliding`. |
| `Assets/Scenes/Desk.unity` | New `SheetGrid` empty under Desk; the four Sheet instances and the Player instance reparented under it; `Surface` stays under Desk; `DeskSlider` added to the Desk object. |
| `Documents/Claude Bible.md` | §7: record the decision. |
| `Documents/Plans/2026-08-25-desk-slide.md` | This plan. |

## 5. Components & data

### Hierarchy (scene)

```
Main Camera            (fixed; ScreenCamera = framing only)
Desk                   (fixed; Desk, ScreenNavigator, DeskSlider)
  Surface              (fixed background/desk surface — stays)
  SheetGrid            (the thing that slides)
    Sheet (0,0) … (1,1)
    Player
```

### `Desk` (changed)

- New `[SerializeField] Transform sheetGrid` — *"Root the sheets are laid out under. Sliding between Screens moves this, not the Desk."* Wiring reference, not a tunable. Must be a child of the Desk (validated like `Sheet.ValidateFaceRoot`: null or not a descendant → `LogError`).
- `GridToLocal(gridPosition)` — unchanged maths; now documented as *sheet-grid-local*.
- `GridToWorld` → `sheetGrid.TransformPoint(GridToLocal(...))`.
- `LayoutSheets` — for each child `Sheet`, sets `localPosition` from grid position; logs an error for any Sheet whose parent is not `sheetGrid` (its local position would be meaningless otherwise). Sheets are still discovered by `GetComponentsInChildren<Sheet>(true)` from the Desk.
- New `public Transform SheetGrid` getter.
- When `sheetGrid` is null: error logged in `Awake`/`OnValidate`; `GridToWorld` and `LayoutSheets` log and return (no fallback to the Desk transform — that would hide a mis-wired scene).

### `DeskSlider` (new, on the Desk object, `[RequireComponent(typeof(Desk))]`)

Responsibility: translate `Desk.SheetGrid` so that a Sheet's centre coincides with the view centre. No knowledge of the player, exits, or which sheet is current.

Serialized:
- `Transform view` — *"Transform whose X/Y is where the current Screen is centred. Normally the Main Camera."* Wiring reference. Null → `LogError` in `Awake`; `Centre`/`SlideTo` log and do nothing.
- **Tunables** (`[Header("Slide")]`):
  - `[Min(0)] float slideDuration = 0.4f` — *"Seconds the slide between screens takes."* Moved from `ScreenCamera`.
  - `AnimationCurve slideEase = AnimationCurve.EaseInOut(0,0,1,1)` — *"Progress of the slide over normalised time (0..1 → 0..1)."* Replaces the hard-coded `SmoothStep` (default is the same shape).

Public surface:
- `bool IsSliding`
- `void Centre(Sheet sheet)` — snaps; cancels any slide in progress.
- `void SlideTo(Sheet sheet)` — animates from the grid's current position to the target over `slideDuration`, evaluating `slideEase`; cancels any slide in progress. Callers wait on `IsSliding` (not on a Coroutine handle — see §6.4).

Private: `TargetPosition(Sheet)` = `grid.position + (view.xy − sheet.Centre)`, z preserved. Tunables are read at slide start (duration) and per frame (curve) — Inspector edits during a slide apply to the next slide, which is the live-enough behaviour.

### `ScreenCamera` (changed)

Keeps `verticalMargin` tunable (default 0.75) and `ApplyFraming` in `Awake`/`OnValidate`. Loses everything about position. Remarks updated: camera is fixed; the sheet grid moves (`DeskSlider`).

### `ScreenNavigator` (changed)

- `[RequireComponent(typeof(Desk), typeof(DeskSlider))]`; `slider = GetComponent<DeskSlider>()` in `Awake`. The `screenCamera` serialized field is removed.
- `Start`: `slider.Centre(currentScreen)`. Comment: `PlayerMover.Position` (the body) is stale until the next physics step after this move; nothing reads it in that window.
- `Transition`: `slider.SlideTo(destination); while (slider.IsSliding) yield return null;`.
- `entryInset` unchanged.

### Scene (`Desk.unity`) — hand-authored YAML edits

- New GameObject `SheetGrid` (fileIDs 400/401): Transform at local `(0,0,0)`, `m_Father: 201`. Added to Desk transform's `m_Children` (replacing the four sheet transform entries, which move to 401's `m_Children` along with `9001`).
- Four Sheet prefab instances: `m_TransformParent: {fileID: 401}`.
- Player instance (`&9000`): `m_TransformParent: {fileID: 401}`. Local position stays `(-2,-1,-0.5)`; SheetGrid and Desk are at the origin so the world position is unchanged.
- Desk GameObject 200: `m_Component` gains `{fileID: 204}`; new `MonoBehaviour &204` = DeskSlider with `view: {fileID: 101}`, `slideDuration: 0.4`, `slideEase` = EaseInOut keys (serialised as Unity's standard two-key curve).
- `&202` (Desk) gains `sheetGrid: {fileID: 401}`. `&203` (ScreenNavigator) loses the `screenCamera:` line.
- Run the existing link checker / a YAML sanity check over the result (all referenced fileIDs exist; every transform appears in exactly one `m_Children` list).

## 6. Behaviour

1. **Awake/Start**: `Desk.Awake` lays out sheets under SheetGrid (unchanged maths). `ScreenNavigator.Start` finds the player's sheet by world position, then `slider.Centre(sheet)` moves SheetGrid so that sheet's centre is at the camera's X/Y. The Player, a child of SheetGrid, moves with it and stays on the same sheet. Camera and Desk never move.
2. **Transition**: same sequence as today — hold movement, `PlaceAt` the entry position on the destination (world coordinates, computed *before* the slide), `PlayerLeft`/`PlayerEntered`, then slide and wait for `IsSliding` to clear. During the slide SheetGrid translates each frame in `Update`; the Player is carried as a child with velocity held at zero. Afterwards movement resumes.
3. **Rigidbody2D under a moving parent**: Unity moves a Rigidbody2D with its parent Transform. The Player body is interpolated (`m_Interpolate: 1`); with movement held (velocity zero) the body's interpolated pose and the parent-driven transform can disagree by up to one physics step, which *may* show as jitter against the sheet during the slide. §8 checks this explicitly. If it hitches, the fix is: while `MovementEnabled` is false, `PlayerMover` sets `body.interpolation = None` and restores it after — a hold means "the world is carrying me", so per-body interpolation is off by definition. That generalises to any future body under SheetGrid. Not built until the check shows it is needed (a change to the feel is not made blind).
4. **Cancel/disable**: `DeskSlider.OnDisable` stops the coroutine, **snaps the grid to the slide's target**, and clears the handle, so `IsSliding` goes false and a waiting navigator finishes normally (it polls `IsSliding` rather than yielding a Coroutine, so a stopped coroutine can't strand it). `ScreenNavigator.OnDisable` unchanged.
5. **Failure modes**: missing `view` or missing `sheetGrid` → errors in `Awake`; `Centre`/`SlideTo` log and return with `IsSliding` false, so the navigator's wait ends immediately and movement resumes — no hang, nothing silent. Sheets not directly under SheetGrid → error from `LayoutSheets`.
6. **What moves**: only SheetGrid's subtree — sheets, their content, exits, terrain, the player. `Surface`, and anything else placed under Desk or at scene root (the background), stays put.

## 7. Interfaces & seams

No interface: one way to slide, one consumer. Seam = `DeskSlider.Centre/SlideTo/IsSliding`; `ScreenNavigator` doesn't know what moves. Reverting to a moving camera = delete `DeskSlider`, restore `ScreenCamera.FrameSheet/SlideTo`, repoint the navigator. `SheetGrid` stays a meaningful grouping either way.

## 8. Testing

- CLI compile of runtime + EditMode tests with Unity's Roslyn; existing `ScreenNavigatorTests` (pure `EntryPosition`) must still pass.
- No new unit test: the slide target is one vector subtraction.
- Scene YAML: link/fileID sanity check after editing.
- Manual, in the Desk scene (Aaron): play; Main Camera and Desk transforms must stay at their authored positions throughout; `Surface` does not move; walk through each exit in all four directions; **watch the Player against the sheet edge during the slide for jitter** (§6.3); Inspector `Slide Duration` / `Slide Ease` on Desk › DeskSlider affect the next slide.

## 9. Assumptions (engineering)

- The Player belongs under SheetGrid. It is what rides the sheets; parenting is the least-coupled way to carry it, and future sheet-bound bodies (blocks) will sit under sheets anyway. No script reads the Player's parent.
- The camera's own X/Y (currently `0,0`) is the view centre. A UI/desk-margin offset later means pointing `view` at an empty child of the camera, not code.
- `Desk.GetComponentsInChildren<Sheet>` now also walks the Player subtree; trivial cost, noted.
- Stale serialized `screenCamera` (navigator) and `slideDuration` (camera) values are dropped when the fields disappear; the navigator line is removed from the YAML anyway.

## 10. Open questions

None.

## Review (round 1)

- **B1** Surface is a child of Desk, so sliding Desk drags the background — **accepted**. Restructured: Desk fixed, new `SheetGrid` child slides (reviewer's option b). Q1 answered from Aaron's own words and Bible §2/Design Doc Art; not escalated.
- **N1** `OnDisable` stopping the coroutine strands the navigator — **accepted**: navigator polls `IsSliding`; `OnDisable` snaps to target.
- **N2** interpolation jitter — **accepted** as an explicit manual check with the interpolation-off-while-held fix recorded as the fallback (not built blind).
- **N3** ease curve tunable — **accepted**: `slideEase` AnimationCurve.
- **N4** `m_Component` entry for the new component — **accepted**, listed in §5.
- **N5** child walk includes Player — noted in §9.
- **N6** stale body position after `Centre` — **accepted**, comment in `Start`.

## Review (round 2)

- **N1** interpolated body fights the parent move every frame — **accepted**: `PlayerMover` switches `body.interpolation` to `None` while `MovementEnabled` is false and restores the prior mode after. §6.3 fallback is now built; the manual check verifies rather than discovers.
- **N2** remove stale `slideDuration` from `&105`; `&401` gets `m_RootOrder: 1`; child order sheets then Player — **accepted**.
- **N3** `Sheet.OnValidate` should catch a sheet parented directly under Desk — **accepted**: `Desk.IsUnderSheetGrid(Sheet)` shared by `LayoutSheets` and `Sheet.OnValidate`.
- **N4** static colliders on a moving transform; do not add a Rigidbody2D to SheetGrid — **accepted**, one sentence in `DeskSlider` remarks.
- **N5** `slideDuration = 0` must snap; evaluate curve with `LerpUnclamped` — **accepted**.

## Code review

- **S1** `Desk.GridToWorld` unused — **accepted**, removed.
- **S2** null `sheet` in `DeskSlider.TryGetTarget` — **accepted**, explicit error.
- **S3** `Centre` at `Start` moves an interpolating body — **accepted**, wrapped in the movement hold.
- **S4** mis-parented sheet logs twice (Desk and Sheet `OnValidate`) — **rejected**: only fires on an authoring mistake, where two pointers to the same sheet are fine.

## Deviations

- `Desk.IsUnderSheetGrid` in the plan is `Desk.ValidateSheetParent` in code (naming only).
- `Desk.GridToWorld` removed rather than reworked (no callers).
