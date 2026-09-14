# Sheet Studio — deleting and rearranging sheets (the sheet map)

## 1. Task

Aaron (2026-09-09): *"Can you create a way to delete and rearrange sheets in the level editor."*

Add to the Sheet Studio's no-sheet view (the picker) a **sheet map** of the open Desk's sheet set: a small grid drawn in the window, one cell per grid position, in which a sheet can be **dragged to another cell** (moving it, or swapping it with the sheet already there), and a selected sheet can be **removed from the Desk** (undoable, the variant file stays) or **deleted** (confirmed; the variant file is deleted too).

**Out of scope:** any runtime change; editing a sheet's content (unchanged, in the Prefab Stage); moving sheets of a set other than the open Desk's (other sets stay a read-only open-list); a Desk-scene "sheet map" in the Scene view; renaming a set / scene.

## 2. Design references

- **Bible §7 (Desk, sheet grid).** *"The Desk accepts any set of grid positions"*; grid dimensions are **[DECIDE] #14** (locked at Checkpoint 1). The map therefore imposes no bounds: it draws the occupied cells plus one ring of empty cells around them, and a sheet may be dragged to any cell in that ring. Nothing here decides a grid size.
- **Bible §7 "Two Desk scenes" (2026-09-09).** Each Desk scene owns a sheet set in `Assets/Papercut/Sheets/<scene name>/`; the Studio derives the set from the open Desk. Delete/move act on the open Desk's set only.
- **`Sheet.GridPosition` convention (code).** A sheet's grid position lives in two places: the variant's **file name** `Sheet (x,y).prefab` (the Studio's `SheetAssetPath` derives paths from it; `CreateSheet` treats an existing file at that name as "this position's variant") and the scene instance's serialized `gridPosition` override (the variant asset itself keeps the base prefab's (0,0)). A move must therefore rename the asset **and** update the instance, or the two drift and `CreateSheet` starts adopting the wrong file.
- **Bible §3 [LOCKED] not tile-based.** Unaffected: the sheet *grid* is the Desk's own layout (Bible §7), not a movement grid.
- **implementation-guidelines §4a.** Editor-only UI with no runtime behaviour → no Inspector tunables. Cell size, drag threshold and colours are editor constants.
- Nothing `[TENTATIVE]` is built.

## 3. Decisions already made by Aaron (2026-09-09, pre-plan)

- **Workflow:** full.
- **Delete:** *"Both buttons"* — **Remove from Desk** (undoable; the variant file stays in the set and `Create Sheet` at that position re-adds it, as today) **and** **Delete** (confirm dialog; removes the instance from the Desk *and* deletes the variant file; not undoable).
- **Rearrange UI:** *"Grid map with drag-and-drop"* — the picker draws the set as a small grid; drag a cell onto another cell to move the sheet.
- **Occupied target:** *"Swap"* — dropping a sheet on an occupied cell exchanges the two sheets (assets renamed through a temporary name, both instances updated).

Post-review (2026-09-09, after plan review round 1):

- **Far moves:** *"Add a Move-to field"* — the action row under the map gains a grid position field and a **Move to** button for far jumps (same `MoveSheet` operation; swap rule applies there too).
- **Player:** *"Stay at the cell"* — moving a sheet never moves the player; the player keeps its world position, and whatever sheet now sits at that cell is the start. If the cell is left empty, the result message warns that the player stands on empty desk.

## 4. Files

| File | Purpose |
| --- | --- |
| `Assets/Papercut/Editor/StudioSheetOps.cs` *(modified)* | Dialog-free sheet-set operations: parse a grid position from a variant path; build the set's **slots** (asset ↔ Desk instance per position, mismatches flagged); `MoveSheet` (rename + instance update, with swap); `RemoveSheetFromDesk`; `DeleteSheet`; the player-follows-its-sheet rule. |
| `Assets/Papercut/Editor/StudioSheetMap.cs` *(new)* | The map widget: pure layout (which cells are drawn, cell rect ↔ grid position) plus IMGUI drawing and the select / double-click-open / drag-to-move interaction. Reports through callbacks; no dialogs, no asset access. |
| `Assets/Papercut/Editor/SheetStudioWindow.cs` *(modified)* | Picker: the open Desk's set is drawn as the map with an action row (Open · Remove from Desk · Delete…); dialogs and result messages live here; clicking an empty cell primes the New Sheet position. Other sets keep the existing button list. |
| `Assets/Papercut/Tests/EditMode/StudioSheetOpsTests.cs` *(modified)* | Tests for parsing, slots, move/swap/rename, player follow, remove, delete (temp folder + temp Desk as today). |
| `Assets/Papercut/Tests/EditMode/StudioSheetMapTests.cs` *(new)* | Tests for the pure map layout (cell bounds with ring, rect ↔ position). |

No runtime file changes. No prefab or scene changes are committed by this task (the Desk scene is only dirtied when Aaron uses the tool).

## 5. Components & data

### `StudioSheetOps` additions (static, dialog-free)

```csharp
public static bool TryParseGridPosition(string assetPath, out Vector2Int gridPosition);
```
Inverse of `SheetAssetPath`: file name `Sheet (x,y).prefab` with integer x, y (negative allowed, no spaces inside the parens). Anything else → false. Regex anchored on the whole file name.

```csharp
public sealed class SheetSlot
{
    public Vector2Int GridPosition;
    public string AssetPath;          // the set's variant named for this position, or null
    public Sheet Instance;            // the Desk's sheet at this position, or null
    public string InstanceAssetPath;  // the instance's own prefab asset path, or null
    public int InstanceCount;         // Desk sheets claiming this position (>1 = Duplicate)
    public bool Mismatch => Instance != null && InstanceAssetPath != AssetPath;
    public bool Duplicate => InstanceCount > 1;
    public bool Movable => AssetPath != null && !Mismatch && !Duplicate;   // also the delete rule and the drag affordance
    public bool IsEmpty => AssetPath == null && Instance == null;
}
public static List<SheetSlot> BuildSlots(string folder, Desk desk, List<string> unmappedAssetPaths);
```
One slot per position that has either a parseable variant in `folder` or a `Sheet` under `desk` (`desk` may be null → assets only). A variant whose name does not parse goes to `unmappedAssetPaths` (still openable from a list under the map). `InstanceAssetPath` = `PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.gameObject)` (null for a non-prefab sheet). A `Mismatch` slot (e.g. the instance at (0,1) is an instance of `Sheet (2,2).prefab`, or a variant from another set) is drawn with a warning and cannot be moved or deleted — the mismatch is authored ambiguity to surface, not to paper over. A **`Duplicate`** slot (two Desk sheets claim one position — reachable by undoing a Remove after a Move into that cell; `Desk.EnsureRegistry` errors on it in Play) is drawn with a warning, `Instance` is the first found, and it is refused for move/delete; Remove from Desk still works and, repeated, clears the duplicates (review N2). **`Movable`** is the one rule used for the drag affordance, `MoveSheet`, and `DeleteSheet`, so the map never offers a drag the op will refuse (review N7). Sorted by position for determinism.

```csharp
public enum MoveSheetResult { Moved, Swapped, NoChange, Refused, Failed }
public static MoveSheetResult MoveSheet(Vector2Int from, Vector2Int to, Desk desk, string folder, out string message);
```
See §6.

```csharp
public static bool RemoveSheetFromDesk(Sheet sheet, out string message);   // Undo.DestroyObjectImmediate
public enum DeleteSheetResult { Deleted, DeletedAssetOnly, Refused, Failed }
public static DeleteSheetResult DeleteSheet(Vector2Int gridPosition, Desk desk, string folder, out string message);
```

```csharp
public static PlayerMover FindPlayerOnDesk(Desk desk);            // the PlayerMover under desk.SheetGrid, or null
public static bool IsPlayerOnSheet(PlayerMover player, Sheet sheet); // sheet.Bounds.Contains(player.transform.position)
```
`transform.position`, not `PlayerMover.Position` — that reads the Rigidbody2D, which is not simulated in edit mode. Used only for warnings (the Remove/Delete dialogs and the Move message); the player is never moved (Aaron, post-review: *"Stay at the cell"*).

### `StudioSheetMap` (new, editor UI)

Pure part (static, testable):

```csharp
public readonly struct MapLayout
{
    public readonly RectInt Cells;      // grid-position bounds actually drawn (occupied bounds + Ring)
    public readonly Rect Area;          // GUI rect of the whole grid
    public readonly Vector2 CellSize;   // pixels; aspect = SheetGeometry.Size (11 : 8.5)
    public Rect CellRect(Vector2Int gridPosition);      // GUI rect of a cell; y is flipped: north (+y) is up
    public bool TryCellAt(Vector2 guiPoint, out Vector2Int gridPosition);
}
public static MapLayout Layout(Rect available, IReadOnlyList<Vector2Int> occupied);
```
Bounds = min/max of `occupied` (or (0,0) when empty) expanded by `Ring = 1` cell on every side, so a sheet can always be dragged one cell outward; the grid grows as sheets are placed. Cell size = the largest that fits `available` at the sheet aspect, clamped to `[MinCellWidth = 40, MaxCellWidth = 110]` px; the grid is centred horizontally in `available`. With a wide set the cells shrink; if even `MinCellWidth` does not fit, the area scrolls (an `EditorGUILayout.ScrollViewScope`).

Editor constants: `Ring = 1`, `DragThreshold = 6f` px, `CellGap = 3f` px, colours for on-Desk / off-Desk / mismatch / selected / drag-source / drop-target / empty cells.

Instance part:

```csharp
public Vector2Int? Selected { get; set; }
public event Action<Vector2Int> OpenRequested;                // double-click on an occupied cell
public event Action<Vector2Int> EmptyCellClicked;             // click on an empty cell
public event Action<Vector2Int, Vector2Int> MoveRequested;    // drag drop: (from, to)
public bool IsDragging { get; }
public void CancelDrag();
public void Draw(Rect rect, IReadOnlyList<StudioSheetOps.SheetSlot> slots);
```
`Draw` computes the layout, draws every cell (label: `(x,y)`; second line: `off Desk` when the asset has no instance, `no variant` when the instance has no asset, `⚠ mismatch` when both disagree, `⚠ duplicate` when two Desk sheets claim the cell), and handles the mouse (see §6). Only `Movable` cells are draggable (review N7). `OpenRequested` fires only for a cell with an `AssetPath` (review N5); the window guards it again. The map holds no asset or scene references beyond the slots passed in each frame. **Selection after an operation** (review N5): the window sets `Selected = to` after a successful move/swap (the action row keeps acting on the sheet Aaron dragged), leaves it after Remove (the slot still exists, off Desk), and clears it after Delete.

### `SheetStudioWindow` changes

- Fields: `StudioSheetMap sheetMap`, `List<SheetSlot> slots` (rebuilt every picker frame — cheap: one `FindAssets` on one folder plus one `GetComponentsInChildren`), `string pickerMessage`.
- `DrawSheetPicker`: for the open Desk's set → heading, the map, an action row, the message line, a list of unmapped variants (if any), then the existing New Sheet row. Other sets → unchanged.
- Action row (enabled only with a selection): **Open** (`PrefabStageUtility.OpenPrefab`; disabled without an asset), **Remove from Desk** (disabled without an instance), **Delete…** (disabled unless `Movable`), and — for far moves (Aaron, post-review) — a grid position field with a **Move to** button (disabled unless `Movable`; same `MoveSheet`, so dropping on an occupied cell swaps here too; the field is primed with the selected cell when the selection changes). Dialogs:
  - Remove with the player on the sheet: *"The player stands on Sheet (x,y). Removing it leaves the player on empty desk, and Play Mode will fail to pick a starting Screen until the player is moved. Remove anyway?"* Otherwise no dialog (it is undoable).
  - Delete: *"Delete Sheet (x,y)? This deletes '<path>' from the project and removes it from the Desk. Only the variant file is deleted — its art sprites and the element prefabs it uses stay in the project. This cannot be undone, and the editor's Undo history is cleared so an older undo cannot bring back a sheet whose file is gone."* plus the player sentence when it applies (review N2, N8).
- Map events: `OpenRequested` → open (guarded: only with an `AssetPath`); `EmptyCellClicked` → `newSheetPosition = cell` (the New Sheet row now says which cell it will create); `MoveRequested` → `MoveSheet`, message from the result, `Selected = to` on success.
- **After any operation or dialog inside `OnGUI`** (`MoveSheet`, `RemoveSheetFromDesk`, `DeleteSheet`, `DisplayDialog`): `Repaint()` then `GUIUtility.ExitGUI()` (review N4) — the slot list and layout computed at the top of the pass are stale once assets or instances have changed, and finishing the pass with a different control count is the classic IMGUI Layout/Repaint mismatch. `slots` is rebuilt only at the top of the next pass.
- Escape while the map is dragging cancels the drag (the picker branch of `OnGUI` gets a small key handler, since `HandleKeyboard` runs only in the editing view).

## 6. Behaviour

### Map interaction (`StudioSheetMap.Draw`)

1. **MouseDown** (left) on a cell: occupied non-mismatch → becomes `Selected`, drag *candidate* recorded (cell + mouse position); mismatch cell → selected only (still openable, so the ambiguity can be inspected); empty → `Selected = null`, `EmptyCellClicked(cell)`. `clickCount == 2` on an occupied cell → `OpenRequested` (and no drag). Event used; keyboard focus cleared (`GUIUtility.keyboardControl = 0`) like the panes.
2. **MouseDrag** with a candidate, once the cursor has moved ≥ `DragThreshold` px → dragging. Every drag frame repaints; the source cell is drawn hollow, a ghost cell follows the cursor, and the cell under the cursor (if inside the grid) is outlined as the drop target — green over an empty cell ("move"), amber over an occupied one ("swap"), red over a mismatch cell (refused).
3. **MouseUp**: if dragging and the cursor is over a cell other than the source → `MoveRequested(from, to)`; over the source or outside the grid → nothing. Drag state cleared either way. A right-click or Escape (`CancelDrag`) during a drag cancels it.
4. Dropping outside the grid, or on the source, never fires an event — the sheet stays where it was. **Far moves** (review N1): a drag reaches only the drawn grid (occupied bounds plus the ring). Dragging *through* occupied cells does not move a sheet far — each drop on an occupied cell is a swap, so (0,0) dragged to (1,0) then (2,0) rotates the row rather than moving one sheet. A true far move is a route around the empty ring, one rename per drop. How far moves should work is open — §10 Q-A.

### `MoveSheet(from, to, desk, folder)`

1. `from == to` → `NoChange`. Null/missing folder → `Failed`.
2. Build slots. Source slot must have an `AssetPath` and not be a `Mismatch` (an instance without a variant in this set cannot be renamed; a mismatched one would be moved under the wrong name) → else `Refused` with the reason. The target slot, if occupied, must satisfy the same rule → else `Refused` ("swap would move a sheet whose variant isn't in this set").
3. Rename on disk (the only non-transactional part, so it goes first, with rollback):
   - Move: `AssetDatabase.MoveAsset(pathFrom, pathTo)`; a non-empty return string → `Failed` with that text.
   - Swap: `temp = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Sheet ({from.x},{from.y}) swapping.prefab")`; `pathFrom → temp`, `pathTo → pathFrom`, `temp → pathTo`. If step 2 or 3 fails, the completed steps are reversed (best effort; each reversal's error, if any, is appended) and the result is `Failed` with the full message. The temp file never survives a successful swap (asserted in tests).
4. Scene instances, for each moved sheet that has one: `SerializedObject(sheet).FindProperty("gridPosition") = new position` (`ApplyModifiedPropertiesWithoutUndo`), `gameObject.name = Path.GetFileNameWithoutExtension(newPath)`. Then `desk.LayoutSheets()` (it re-derives every sheet's local position from its grid position — the same thing `Desk.OnValidate` does), and `MarkSceneDirty`.
5. **The player stays at its cell** (Aaron, post-review): the player is never moved. Before step 4 the player (if any) is tested against the moved sheet's `Bounds`; if it stood on a sheet that is moved away and nothing is swapped in, the message warns.
6. Message: `"Moved Sheet (a,b) to (c,d)."` / `"Swapped Sheet (a,b) and Sheet (c,d)."`, with `" The player now stands on empty desk at (a,b); Play Mode cannot pick a starting Screen until it is moved."` appended when step 5 applies. Returned `Moved` / `Swapped`.
7. **Undo:** none. Asset renames are not undoable, and an undone half (instance back at the old position, file under the new name) would be exactly the drift §2 warns about — visible as a mismatch cell, but avoidable. The window logs the move to the console with "(not undoable)". The "Add to Desk" undo entries of older creates stay valid (they reference the same instance objects).

### `RemoveSheetFromDesk(sheet)`

`Undo.DestroyObjectImmediate(sheet.gameObject)`; marks the scene dirty; message `"Removed Sheet (x,y) from the Desk; its variant is still in the set — Create Sheet (x,y) puts it back."`. Returns false (with a message) only for a null sheet. Undoable, symmetrical with the undoable add in `CreateSheet`.

### `DeleteSheet(gridPosition, desk, folder)`

1. Slot must be `Movable` (asset present, no mismatch, no duplicate) → else `Refused`.
2. The instance (if any) is destroyed with plain `Object.DestroyImmediate` (**not** `Undo`): once the asset is gone, an undo would resurrect a "missing prefab" instance — a known-broken object, which the standard forbids. Scene marked dirty.
3. `AssetDatabase.DeleteAsset(path)`; false → `Failed` (the instance is already gone; the message says the file is still there and that `Create Sheet` will re-add it — no silent half-state).
4. **`Undo.ClearAll()`** after a successful delete (review N2): entries already on the stack — the undoable Create of this sheet, or an earlier undoable Remove of it — could otherwise re-create an instance of the deleted file through Ctrl+Z/Ctrl+Y. Losing the session's undo history on a confirmed, file-deleting operation is the lesser cost; the confirm dialog says it will happen. (`Undo.ClearUndo(instance)` alone would leave the Remove-then-Delete case open.)
5. `Deleted` with instance, `DeletedAssetOnly` without.

### Failure modes summarised

- No Desk scene open, or the scene unsaved: there is no "open Desk's set", so no map is drawn (as today, the own-set section needs `deskFolder != null`); every set is listed with Open buttons and the existing hint lines explain what is missing (review N6). `MoveSheet`/`DeleteSheet` still accept a null Desk (rename/delete files only) for tests and for any future caller, but the window never reaches them without a Desk.
- Unparseable variant names: listed under the map with Open buttons, never moved or deleted.
- Mismatch cells: drawn with a warning, selectable and openable, refused for move/delete with a message naming both paths.
- `MoveAsset` errors (locked file, VCS) are reported verbatim; a partial swap is rolled back and reported.

## 7. Interfaces & seams

No runtime interface changes. The map is a self-contained editor widget talking to the window through three events; `StudioSheetOps` stays the dialog-free, testable operations layer and the window stays the only place that shows dialogs. Cutting this feature = deleting `StudioSheetMap.cs` and its test file, removing the new ops from `StudioSheetOps`, and restoring the picker's button list.

## 8. Testing

**Edit Mode tests (mechanical, run through the Unity CLI):**

`StudioSheetOpsTests` (temp folder `Assets/Temp_StudioTests`, temp Desk, both torn down as today):
- `TryParseGridPosition`: `Sheet (0,0)`, `Sheet (-1,2)`, `Sheet (10,-3)` round-trip `SheetAssetPath`; `Sheet Copy`, `Sheet (1, 2)`, `Sheet (a,b)`, a nested-folder path with the right name (still parses — only the file name matters) as appropriate.
- `BuildSlots`: asset-only slot (`off Desk`), instance-only slot, matching slot, mismatch slot (instance at (0,1) whose asset is the set's `Sheet (2,2)`), unparseable names reported separately, output sorted.
- `MoveSheet` to an empty cell (with Desk): old file gone, new file present, instance `GridPosition`, name and `localPosition` updated, result `Moved`.
- `MoveSheet` swap: both files exchanged (contents verified by an authored marker — a child object created in one variant before the swap), both instances updated, no `swapping` file left, result `Swapped`.
- `MoveSheet` without Desk: rename only.
- `MoveSheet` swap where the target variant has no instance (off Desk): files exchanged, the one instance updated, the target stays off Desk under its new name (review N9).
- `MoveSheet` refuses: `from == to` (NoChange), a mismatch source, a mismatch target, a duplicate source, a source with no variant.
- `BuildSlots` flags a duplicate position (two instances at (0,0)) and `MoveSheet`/`DeleteSheet` refuse it (review N2).
- Player: a `PlayerMover` child of the grid placed inside sheet A's bounds does **not** move when A is moved or swapped; when A's cell is left empty the message carries the empty-desk warning, and on a swap it does not.
- `RemoveSheetFromDesk`: instance gone, file present, `CreateSheet` afterwards returns `AddedExistingToDesk`; `Undo.PerformUndo` brings the instance back.
- `DeleteSheet`: file and instance gone; asset-only delete; refused on a mismatch; after Create → Delete, `Undo.PerformUndo` + `Undo.PerformRedo` leave no sheet on the Desk (the history was cleared) (review N2).
- **Not tested:** the swap rollback path (§6 step 3) — forcing a mid-swap `MoveAsset` failure needs a locked file; stated rather than implied (review N9).
- Existing `EverySheetSetBelongsToADeskSceneInTheBuild` asserted the test Desk set has exactly four sheets; the first Move/Delete on that set would turn the suite red. Relaxed to: every variant in every set parses with `TryParseGridPosition`, and the test Desk set is non-empty (review N3).

`StudioSheetMapTests`: `Layout` with no occupied cells gives a 3×3 ring around (0,0); bounds expand by the ring; `CellRect` of the north-most row is topmost; `TryCellAt(CellRect(p).center) == p` for every drawn cell and false outside; cell aspect equals `SheetGeometry.Size`; clamping at min/max width.

**CLI pass:** `recompile` clean; `run_tests editor` with the Summary and every failure reported verbatim; `eval` to open the Sheet Studio window with the Desk scene loaded and confirm the console has no exceptions from the picker's `OnGUI`. No Play Mode (nothing runtime changed). No real set or scene is modified during verification.

**What Aaron needs to try (feel/intent — not answerable by the CLI):** whether the map reads at a glance with the real sets; whether one-ring drags are an acceptable way to move a sheet far; whether the drop-target colours and swap behaviour feel right; whether click-select / double-click-open is the right split (versus single-click open as before); whether the player-follows-sheet rule matches expectation when rearranging the Desk around the start sheet.

## 9. Assumptions (engineering)

1. **Click selects, double-click opens.** The old list opened on a single click; the map needs a selection for the action row. Double-click is the Project-window convention. Stated for Aaron to veto.
2. **The map shows the set's variants keyed by their file name, merged with the Desk's instances by grid position.** The file name is the set's record of position (see §2); the instance override is the Desk's. Disagreement is shown, not hidden.
3. **One-ring drag range.** The drawn grid is the occupied bounds plus one empty cell on each side. Far moves are a design question (§10 Q-A) because of the swap-through effect the review pointed out.
4. **Move is not undoable; Remove from Desk is; Delete is not and clears the Undo history.** Asset operations cannot be undone; making the scene half undoable would create the exact inconsistency the file-name convention exists to prevent, and leftover undo entries could resurrect an instance of a deleted file. Each operation says so in its console log line; Delete says so in its dialog.
5. **The player is never moved** (Aaron's answer to Q-B). Moving, removing or deleting the sheet under the player is allowed; the operation warns (dialog for Remove/Delete, result message for Move) when it leaves the player on empty desk.
6. **`Desk.LayoutSheets` is the re-layout**, not manual position writes, so the Studio and `Desk.OnValidate` cannot disagree about where a grid position is.
7. Renaming a variant with `AssetDatabase.MoveAsset` keeps its GUID, so existing scene instances (this Desk's, or in any other scene) stay linked; only their `gridPosition`/name need updating, which is done for the open Desk only (another scene using this set's variant would be a mismatch there — sets are per scene by the 2026-09-09 convention, so this does not arise in practice).
8. Other sets are not drawn as maps: their instances are in a scene that is not open, so a move there could only rename files and would leave that scene's instances mismatched.

## 10. Open questions (raised by the plan review; asked of Aaron 2026-09-09)

- **Q-A — far moves.** With swap-on-occupied, a drag can only move a sheet one ring outward, or swap it with a neighbour. Options: (a) add a small "Move to (x,y)" field + button to the action row (reuses `MoveSheet`; Aaron declined a per-row field earlier, but that was as the *only* way to move, not as a complement to the map); (b) draw a wider ring (2 cells) and accept several drags; (c) accept it as is. Recommendation: (a). **Aaron: (a), "Add a Move-to field."**
- **Q-B — the player.** When the sheet the player stands on is moved, should the player ride with it (keeps the same start spot on the same sheet; the scene stays runnable) or stay at its world position (the cell is the start, whatever sheet lands there)? Recommendation: ride with it. **Aaron: "Stay at the cell."** The player is never moved; the operation warns when it leaves the player on empty desk.
- Assumption 1 (click selects, double-click opens) stays stated, not asked.

## Review (plan-reviewer, round 1 — approve with changes, no blocking findings)

| # | Finding | Disposition |
| --- | --- | --- |
| N1 | Far moves are misdescribed: dragging through occupied cells swaps every intermediate sheet. | **Accepted** (§6 now states the swap-through effect) and **escalated** as Q-A for the remedy. |
| N2 | Non-undoable Delete/Move mixed with undoable Create/Remove can resurrect an instance of a deleted file or create duplicate positions. | **Accepted:** `Duplicate` slots flagged and refused (§5); `Undo.ClearAll()` after a successful Delete, stated in the dialog (§6); tests added (§8). The reviewer's Q2 (clear history vs keep a known-broken path) is decided by the Bible's standard — no known-broken paths — so it is not put to Aaron. |
| N3 | Existing test pins the test Desk set at exactly four sheets. | **Accepted:** relaxed to "every variant parses; set non-empty" (§8). |
| N4 | Operations inside `OnGUI` leave the pass with stale slots/layout (IMGUI control-count mismatch). | **Accepted:** `Repaint()` + `GUIUtility.ExitGUI()` after every operation/dialog (§5). |
| N5 | Selection after an operation unspecified; `OpenRequested` on an asset-less cell. | **Accepted** (§5). |
| N6 | "No Desk open: the map still draws" contradicts the picker's own-set condition. | **Accepted:** sentence corrected (§6). |
| N7 | Instance-only sheets were draggable but always refused. | **Accepted:** one `Movable` rule for affordance and ops (§5). |
| N8 | Delete dialog should say only the variant file goes. | **Accepted** (§5). |
| N9 | Swap-with-off-Desk path untested; rollback path implied covered. | **Accepted:** test added; rollback stated as untested (§8). |
| Q1 | Player rides with its sheet or stays put? | **Escalated** as Q-B. |
| Q2 | Clear Undo history on Delete, or keep it? | **Resolved by the standard** (see N2); noted in the dialog text. |

## Deviations

- None of design. Engineering: the map's mouse handling switches on `Event.rawType` for drag/release (a release outside the scroll view's clip must still end the drag) but requires `Event.type == MouseDown` for a press (code review S1). The scroll view lays the grid out inside the width left by a vertical scrollbar (S2).

## Code review (code-reviewer, round 1 — reject; all findings fixed, 306/306 tests after)

| # | Finding | Disposition |
| --- | --- | --- |
| M1 | A swap with an off-Desk variant (either direction) moved the player's sheet away with nothing arriving, and did not warn. | **Fixed:** the warning fires for whichever moved instance the player stood on when nothing arrives at its cell, naming that cell; two tests added. |
| S1 | MouseDown handled on `rawType`: with the map scrolled, a click on the buttons below it hit a hidden cell and was swallowed. | **Fixed:** a press requires `e.type == MouseDown`; drag/release keep `rawType`. |
| S2 | Content width ignored the vertical scrollbar, forcing a horizontal one. | **Fixed:** content width is the rect minus the scrollbar when vertical scrolling is needed. |
| S3 | Tests missing for DeleteSheet-refuses-duplicate and the two M1 swap cases. | **Fixed:** three tests added. |
| S4 | Move to the sheet's own cell returned an empty message; the button looked dead. | **Fixed:** `NoChange` reports "Sheet (x,y) is already there." (test added). |
| nit | `sets.Find` on a struct returns default, giving NUnit's argument exception instead of the intended message. | **Fixed:** an `Exists` assertion first. |
| nit | Duplicate cell label read as a status. | **Fixed:** "⚠ duplicate ×N". |
| nit | Map events never unsubscribed in `OnDisable`. | **Fixed** for symmetry. |
| nit | `FindSheetSets` + `BuildSlots` run every OnGUI event (every mouse move over the picker). | **Rejected (no change):** accepted as cheap in plan §5; a handful of assets and sheets today. Noted as the first place to look if the picker ever feels sluggish. |

## Code review (round 2 — approve with fixes; round-1 findings confirmed fixed)

| # | Finding | Disposition |
| --- | --- | --- |
| S1 | A drag *released* outside the scrolled map's clip still mapped the content-space point to a hidden cell and fired a non-undoable move. | **Fixed:** the drop is computed only when the release is inside the clip (`e.type == MouseUp`); an outside release ends the drag and drops nowhere. |
| S2 | Non-canonical names (`Sheet (01,0)`, `Sheet (-0,0)`) parsed to a position another file owns; the later file silently overwrote the slot. | **Fixed:** the name pattern accepts canonical integers only (exactly what `SheetAssetPath` writes), so two distinct names cannot parse to one position; other spellings go to the "not on the map" list. Reject cases added to the test. |
| S3 | DeleteSheet tests run `Undo.ClearAll()` in the open editor. | **Accepted:** stated in the test class header. Inherent to the operation. |
| S4 | The scrollbar fix is one-directional (a wide grid's horizontal bar could force a vertical one). | **Rejected (no change):** unreachable below roughly 17 columns; the world is 25 sheets at most (Design Doc). |
| S5 | Double-click invoked `OpenRequested` before using the event. | **Fixed:** state cleared and event used first, invoke last. |
| S6 | Slots/sets rebuilt on every mouse move. | **Rejected (no change):** rebuilding on every call keeps Layout and Repaint consistent (the map's height depends on the slots); cost is negligible at this scale. |
| S7 | Refusals and failures shown in an Info box read like successes. | **Fixed:** Warning box for Refused/Failed results. |
| Q1 | A grey (off-Desk) cell had no way back onto the Desk from the map besides typing its position into New Sheet. | **Fixed as an engineering call, not asked:** an **Add to Desk** button in the action row (enabled for a variant with no instance) runs the existing `CreateSheet` path, which adds an existing variant without creating anything. It is the undo-pair of Remove from Desk and introduces no new semantics. Aaron can veto. |
