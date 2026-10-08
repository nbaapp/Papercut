# Sheet Studio Playtest button: Play Mode starting on the edited sheet

Date: 2026-09-24

## 1. Task

Aaron: *"I have enough screen now that its annoying to playtest them when the player always starts in the same one. Can you add a playtest button to the editor that runs play mode, but starts the player in the level being edited? I should be able to decide where the player spawns in that case"*

Deliverables:

1. A **Playtest** button in the Sheet Studio's editing toolbar. It saves the open sheet, enters Play Mode, and the game starts with the player on the sheet being edited instead of wherever the Desk scene's Player object is saved.
2. A **spawn point** per sheet that Aaron sets by clicking in the Studio's Front pane (a **Spawn** toolbar mode, like Link). It is remembered per sheet asset in editor prefs (Aaron's choice, see §3), drawn in the Front pane, and defaults to the sheet's centre until set.
3. Refusals with a reason, never a silent no-op or a broken Play Mode: the sheet is not on the open Desk, the spawn point is inside a wall, the editor is compiling or already playing.

**Out of scope:** choosing the player's starting abilities for a playtest (the Desk scene's Player keeps its saved `PlayerAbilities`); a Playtest button in the no-sheet picker view (only the sheet open in the stage); starting on the Back face or with folds applied (a sheet starts flat and the player is always on the Base, Bible §4); moving or saving anything in the Desk scene (the saved Player position stays the ordinary Play start); a spawn marker stored in the sheet variant (Aaron chose editor prefs).

## 2. Design references

- Bible §7 *Sheets, desk, and transitions*: the player is a child of the sliding `SheetGrid`; `ScreenNavigator` picks the starting Screen from where the player stands; arrival needs room (`TravelRules.HasRoom`). The playtest start reuses that room rule so a spawn in a wall is refused exactly as an arrival would be.
- Bible §7 *Two Desk scenes*: the Studio derives the sheet set from the open Desk scene. The playtest plays the **open** Desk; the edited sheet must be an instance on it.
- Bible §4 Validity **[LOCKED]**: the player is always on the Base. The spawn is a Front point on the flat sheet; folds never enter into it.
- Bible §8 **[LOCKED]** folds do not persist; unaffected.
- Bible §3 spatial model: the spawn point is a free position (sheet-local), not a cell.
- No `[DECIDE]` item is touched. Object persistence (§11.8) and the reset button (§11.16) are unaffected: a playtest is an ordinary Play Mode session.
- Not covered anywhere: an editor-side "start here" hand-off into the game. Flagged as a gap and built as an editor-only seam (§7).

## 3. Decisions already made by Aaron (2026-09-24)

- Full coding workflow: *"Full workflow"*.
- Spawn storage: *"Remembered per sheet in the Studio"* (click a spot in the Front pane; stored in editor prefs keyed by the sheet asset, not in the prefab; defaults to the sheet's centre until set). The alternatives offered and not chosen: an authored marker saved in the variant; pick each time.
- Play Mode verification: *"No, I'll test it myself"*. I never enter Play Mode for this task.

## 4. Files

Created:

- `Assets/Papercut/Scripts/Screens/PlaytestStart.cs` — runtime-side hand-off: the one-shot "start the player here" request the editor writes and `ScreenNavigator` consumes at Start. Editor-only storage behind `#if UNITY_EDITOR`; in a player build it always reports no request.
- `Assets/Papercut/Editor/StudioPlaytest.cs` — editor side: per-sheet spawn point (editor prefs), the pre-flight checks, saving the stage, writing the request, entering Play Mode; and the edit-mode room test for the spawn marker.
- `Assets/Papercut/Tests/EditMode/PlaytestStartTests.cs` — the request round trip and one-shot consume.
- `Assets/Papercut/Tests/EditMode/StudioPlaytestTests.cs` — spawn pref round trip and the pre-flight/room rule on a built sheet.

Modified:

- `Assets/Papercut/Scripts/Screens/ScreenNavigator.cs` — `Start` honours a pending playtest request before locating the starting Screen.
- `Assets/Papercut/Editor/SheetStudioWindow.cs` — toolbar: **Spawn** mode toggle and **Playtest** button; Escape leaves Spawn mode; Front pane context carries the spawn state.
- `Assets/Papercut/Editor/StudioPane.cs` — `Context` gains the spawn fields; the Front pane draws the spawn marker and, in Spawn mode, a left click sets it.
- `Documents/Claude Bible.md` — one paragraph under §7 recording the Playtest button and the hand-off.

## 5. Components & data

### `PlaytestStart` (runtime, static, `Papercut`)

```csharp
public static class PlaytestStart
{
    public readonly struct Request
    {
        public readonly Vector2Int GridPosition;  // the sheet to start on
        public readonly Vector2 SheetLocal;       // Front-space point on the flat sheet (player centre)
    }
    public static bool TryConsume(out Request request); // true once per request; clears it
#if UNITY_EDITOR
    public static void Set(in Request request);
    public static void Clear();
    public static bool IsPending { get; }
#endif
}
```

Storage: `UnityEditor.SessionState` (survives the domain reload that entering Play Mode performs; the project has domain and scene reload on, `EditorSettings`), keys `Papercut.PlaytestStart.*`. `TryConsume` is `#if UNITY_EDITOR` on the inside and returns false in a build, so the runtime path never references the editor in a player. Precedent for `#if UNITY_EDITOR` in a runtime script: `TerrainFill`.

No Inspector fields; this is an editor tool hand-off, not a feel value.

### `ScreenNavigator` (changed)

`Start` gains one step before the existing "which sheet is the player on" lookup (and `HasRoom` reads the player box from the `BoxCollider2D`'s serialized `size` × `lossyScale` when the collider is a box, falling back to `bounds` otherwise — review N2: `Collider2D.bounds` is physics-populated and may be degenerate before the first physics step, and a zero box passes every overlap test silently; identical for the transition path, which already runs after physics):

```
if (PlaytestStart.TryConsume(out var request))
    TryPlaceForPlaytest(request);   // places the player, or logs why not and falls back to the saved start
```

`TryPlaceForPlaytest`: `desk.TryGetSheet(request.GridPosition)` → `world = sheet.Centre + request.SheetLocal` (the sheet is flat and laid out by `Desk.Awake`, which runs before any Start) → the same room test transitions use (`HasRoom(sheet, world)`, i.e. `TravelRules.HasRoom` over `SheetOcclusion.SolidFootprints(playerAbilities)`) → `player.PlaceAt(world)`. The rest of `Start` is unchanged and now finds the player on that sheet. No new serialized fields.

### `StudioPlaytest` (editor, static, `Papercut.EditorTools`)

- `Vector2 GetSpawn(string sheetAssetPath)` / `SetSpawn(path, Vector2)` / `HasSpawn(path)` / `ClearSpawn(path)`: editor prefs `Papercut.SheetStudio.spawn.<assetPath>`, stored as `"x;y"` with `CultureInfo.InvariantCulture`. Unset → `Vector2.zero` (the sheet's centre). `SetSpawn` clamps the point so the player footprint lies inside the sheet.
- `Vector2 PlayerFootprintSize()`: reuses `StudioFoldSettings.ReadPlayerFootprintSize()` (Player.prefab's serialized `BoxCollider2D` size; the fold pane's test ghost uses the same).
- `bool SpawnHasRoom(Sheet sheet, Vector2 sheetLocal, PlayerAbilities abilities)`: edit-mode room test — `TravelRules.HasRoom` of the player box at the point against every **active `IArrivalObstacle`** under `sheet.Front`, the same set the runtime `SheetOcclusion.SolidFootprints` walks (review B2): a `TerrainRegion` counts when `!IsPassableBy(abilities)`, with pieces from `StudioPlacement.TryGetRegionPieces`; any other obstacle (a `PushableBlock`, so the Block and both Paperweights) counts with `StudioPlacement.AuthoredFootprintPieces` (the fold preview's view of a block). Same rule, edit-mode data, so the marker warns before Play Mode. `abilities` is the open Desk's Player's `PlayerAbilities` (its saved starting set); null counts as no abilities (walls and gated terrain both solid), matching `TerrainRegion.IsPassableBy(null)`. Conservative by design: a resting-absent `Gate (Off)` is an active region and counts as solid here, as it may at runtime (§6, N3).
- `bool TryFindDeskInstance(string stageAssetPath, Desk desk, out Sheet instance, out string reason)` (review B1): a sheet variant's serialized `gridPosition` is **never** set — only the Desk instance carries it as an override (`AddSheetToDesk`/`PlaceInstance`), so the stage's `sheet.GridPosition` is always (0,0). The Desk instance is therefore found **by asset path**: every `Sheet` under `desk.SheetGrid` whose `PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot` equals the stage's asset path. None → `NotOnDesk`; more than one → `Duplicate`. The request carries **that instance's** `GridPosition`, cross-checked against `StudioSheetOps.TryParseGridPosition(stageAssetPath)` (a mismatch is refused as `Mismatch`, the same warning the sheet map shows).
- `enum PlaytestRefusal { None, EditorBusy, CompileErrors, NoDesk, NotOnDesk, Duplicate, Mismatch, NoRoom, SaveFailed }` and `PlaytestRefusal CanPlaytest(PrefabStage stage, Sheet sheet, Desk desk, out Sheet instance, out string reason)`.
- `bool Playtest(PrefabStage stage, Sheet sheet, Desk desk, out string message)`: runs `CanPlaytest`; saves the stage if dirty; writes the request; `EditorApplication.EnterPlaymode()`. Returns false with the reason on refusal.
- Pref keys are prefixed with a hash of `Application.dataPath` (review N5): `EditorPrefs` is per Unity install, and a second checkout of the project must not share spawns with this one.
- `RekeySpawn(oldPath, newPath)` / `SwapSpawns(pathA, pathB)` (review N1): the map's move renames the file and the swap exchanges two files; the prefs follow so a spawn stays with its sheet's content.

Editor prefs, not Inspector: these are editor-tool conveniences (like the Studio's Snap/Overlays), not game tunables.

### `StudioPane.Context` (changed)

New fields: `bool SpawnMode`, `bool ShowSpawn` (Front pane only), `Vector2 SpawnPoint` (sheet-local), `bool SpawnHasRoom`, `Vector2 SpawnSize`, `System.Action<Vector2> SpawnPlaced`. The Back pane's context leaves `ShowSpawn` false and `SpawnMode` false; Spawn mode never reacts to a Back click (a message says so, see §6).

### `SheetStudioWindow` (changed)

- Toolbar, after **Link**: a **Spawn** toggle (`GUILayout.Toggle`, toolbar button) and a **Playtest** button. Spawn mode is exclusive with Link mode and with an armed palette prefab, the way Link is: entering it exits Link and disarms; arming a prefab or entering Link leaves it.
- Hint line while in Spawn mode: *"Spawn mode: click in the Front pane where the player starts a playtest · Esc to leave"*.
- Escape in Spawn mode leaves it (handled in `HandleKeyboard` alongside Link's Escape; a fold-pane drag still claims Escape first).
- Front pane context carries the spawn state; the window reads `StudioPlaytest.GetSpawn(stage.assetPath)` and computes `SpawnHasRoom` once per repaint from the open Desk's player abilities (cheap: a handful of regions; cached with `settingsCache`'s invalidation on edits).
- `Attach` leaves Spawn mode (the new stage's spawn is a different pref).

## 6. Behaviour

**Setting the spawn.** Aaron toggles **Spawn**, clicks in the Front pane. The click's sheet-local point is snapped with the toolbar's Snap increment, clamped so the player footprint stays inside the sheet, and saved to the pref for the open sheet asset. The marker moves immediately. Spawn mode stays on until Esc, the toggle, arming a prefab or entering Link (so several clicks refine the spot). A click in the Back pane while in Spawn mode does nothing but report *"The spawn is a Front point: click in the Front pane."* (the player starts on the Front, Bible §4).

**The marker.** The Front pane always draws the spawn: an outlined box of the player's footprint size centred on the point, labelled *"Spawn"* (miniLabel, like element labels; obeys nothing else — it is not an element, cannot be picked, moved by drag, deleted, or linked). Green outline when `SpawnHasRoom`, red with the label *"Spawn (in a wall)"* when not. Unset sheets show it at the centre; the label reads *"Spawn (default)"* until set. The marker is drawn after elements and overlays so it is never hidden by them; X-ray does not affect it.

**Playtest.** The button calls `StudioPlaytest.Playtest`. In order:

1. `EditorApplication.isPlayingOrWillChangePlaymode` or `isCompiling` → refuse *"The editor is already playing / compiling."* `EditorUtility.scriptCompilationFailed` → refuse *"Fix the compile errors first."* (a request written now would fire on the next ordinary Play, so it is never written).
2. No open Desk (`StudioSheetOps.FindOpenDesk()` null) → refuse *"No Desk scene is open."*
3. `TryFindDeskInstance` (by asset path, §5): none → refuse *"'<sheet>' is not on the open Desk. Add it from the sheet map first."*; several → refuse *"<n> sheets on the Desk are instances of '<sheet>'; remove the extras first."*; the instance's `GridPosition` disagrees with the file name → refuse *"The Desk's '<sheet>' sits at (x,y) but its file says (a,b); fix it on the sheet map first."*
4. `SpawnHasRoom` false → refuse *"The spawn point is inside a wall. Move it (Spawn mode) first."*
5. Stage dirty → `PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath)` and `stage.ClearDirtiness()` (the same save Ctrl+S performs) so Play Mode sees the current edits. If the save throws or returns null → refuse *"Could not save the sheet: …"* and write no request.
6. `PlaytestStart.Set(new Request(sheet.GridPosition, spawn))`, then `EditorApplication.EnterPlaymode()`.

Refusals surface in the toolbar hint (`studioMessage`, the same slot the panes report into) and as a `Debug.LogWarning`, so they are never silent. Whatever Unity does with the prefab stage on entering Play Mode is Unity's default behaviour, untouched.

**In Play Mode.** `ScreenNavigator.Start` consumes the request. If the sheet exists on this Desk and there is room, the player is placed at `sheet.Centre + SheetLocal`; the existing code then finds that sheet as the starting Screen, notifies it, and centres the grid on it. If the sheet is missing (the request was for a Desk that is no longer open — the Studio always writes for the open one, so only a stale request can do this) or there is no room (runtime disagrees with the editor's test: a region that failed `TerrainRegion.Awake` validation and went inert is the only known way, and it errs the safe way) → `Debug.LogError` naming the reason, and Start continues from the saved player position as if no request existed. The request is consumed in every case, so the next ordinary Play is unaffected. A resting-absent `Gate (Off)` (review N3): `ObjectPresence.Start` deactivates it with no ordering guarantee against `ScreenNavigator.Start`, so at runtime its region may still count as solid; the editor test counts every active region too, so both refuse a spawn on a Gate (Off)'s spot — by design for now, and the marker says so (red).

**The Studio while playing** (review N4): Unity keeps the prefab stage open in Play Mode, and the Studio's hierarchy-changed hook would repaint on every runtime change and could dirty scenes. `OnGUI` draws one notice — *"Play Mode is running — stop it to edit the sheet."* — and returns while `EditorApplication.isPlaying`; `OnEditsChanged` only invalidates caches (never touches objects) so it stays as is. The **Playtest** button is drawn disabled while the editor is playing, about to play, or compiling (review N7); the message refusals in step 1 remain as the backstop.

**Stale request guard.** `StudioPlaytest` registers (`[InitializeOnLoadMethod]`) a `playModeStateChanged` listener that clears any pending request on `EnteredEditMode`. Combined with the compile-error refusal, a request can only ever be consumed by the Play Mode session it was written for. A request written and then not consumed (Play Mode entry aborted by Unity for a reason outside the checks above) is cleared the moment the editor is back in edit mode.

**Edge cases.**

- Spawn pref for a sheet later deleted: an orphan pref string; harmless. `StudioSheetOps.DeleteSheet` calls `ClearSpawn` for tidiness (one line).
- Moving a sheet on the map (`MoveSheet`) renames the file (plain move) or exchanges two files (swap). The pref is keyed by path, so `MoveSheet` re-keys the pref on a move and swaps the two entries on a swap, right where it moves the assets; the spawn stays with the sheet's content in both cases (review N1).
- Pref stored with a non-invariant culture: never, `InvariantCulture` both ways; a malformed value is treated as unset (with no log — it can only come from hand-editing the registry).
- The Desk's Player missing (`FindPlayerOnDesk` null): `SpawnHasRoom` runs with null abilities (everything gated counts solid); `Playtest` does not refuse — `ScreenNavigator.Awake` already reports a missing player in Play Mode.
- Spawn set while the sheet's grid position on the Desk differs from the variant's `GridPosition` field: the request carries the stage sheet's `GridPosition`; check 3 already requires the Desk's instance at that position to be this asset, so the two agree.

## 7. Interfaces & seams

- The editor→runtime seam is `PlaytestStart`: the game knows only "there may be a one-shot start request"; who writes it and how it is stored is invisible to `ScreenNavigator`. `SessionState` use is confined to that class.
- Cutting the feature: delete `StudioPlaytest.cs`, `PlaytestStart.cs`, the two tests, the toolbar controls and the `Context` fields, and the three-line `Start` hook in `ScreenNavigator`. Nothing in fold, occlusion, collision, or sheet data changes.
- No interface for storage: one consumer, one writer (`implementation-guidelines` §3, no pre-generalising).

## 8. Testing

**Edit Mode tests (run in the editor suite):**

- `PlaytestStartTests`: `Set` then `TryConsume` returns the same grid position and point; a second `TryConsume` returns false; `Clear` then `TryConsume` false; `IsPending` tracks both. Uses `SessionState` directly, so it exercises the real storage.
- `StudioPlaytestTests`: spawn pref round trip through `SetSpawn`/`GetSpawn` (including negative and fractional values, and that unset returns zero); `SetSpawn` clamps a point outside the sheet inside it; `RekeySpawn`/`SwapSpawns` move the values; `SpawnHasRoom` on a sheet built in an additive test scene (a Sheet with Front/Back roots and a Wall region) is false inside the wall, true beside it, true inside water when the abilities hold Swim and false when they don't, and **false on a Block placed on the Front** (review B2/N6). `TryFindDeskInstance` (review B1/N6): a Desk built in an additive scene with the `StudioSheetOpsTests` pattern, a variant created at (2,1) through `CreateSheet` in a temporary set folder → the finder given that asset path reports the instance at grid position (2,1); with the instance removed → none; with a second instance → duplicate. Prefs are written under a test key prefix and cleaned in TearDown.
- `ScreenNavigator`'s Start path is runtime-only (Awake-built registries; Bible memory: Awake data is invisible to edit-mode tests) and is not unit-tested; it is on Aaron's play list.

**Mechanical checks (Unity CLI, edit mode only):**

1. `recompile` → no errors.
2. Collision check (not playing, no test run, not compiling, scene not dirty), then `run_tests {"mode":"editor"}`; report the Summary and every failure. Re-open the prefab stage the runner closes.
3. `eval`: open the Studio, confirm the toolbar draws the two controls (reflect `RepaintImmediately` and read no exceptions in the console); `StudioPlaytest.CanPlaytest` on the open stage returns `None` or the expected refusal; `PlaytestStart.IsPending` is false afterwards (never enter Play Mode).

**Aaron plays it** (I cannot judge these):

- Press **Playtest** on a sheet: Play Mode starts with the player on that sheet at the marker, the grid centred on it, ordinary movement and folding work, leaving to a neighbour works.
- Ordinary Play (the toolbar ▶) still starts where the Player object is saved.
- A spawn set in a wall is refused before Play Mode with the message, and the marker reads red.
- Unsaved Studio edits are in the playtest (the save-before-play).
- Whether the marker's size/colour/label reads well and whether Spawn mode's click feel (snap, staying in the mode) is right.
- What Unity does with the open prefab stage on entering Play Mode; the Studio shows its "Play Mode is running" notice while playing and comes back to the sheet cleanly on stop.
- A spawn on a block or paperweight is refused (red marker) before Play Mode.

## 9. Assumptions (engineering)

- `SessionState` survives the Play Mode domain reload (documented Unity behaviour; the project has domain reload on).
- `Desk.Awake` lays sheets out before any `Start`, so `sheet.Centre` is valid in `ScreenNavigator.Start` (already relied on by the existing `TryGetSheetAt`).
- `TerrainRegion.IsPassableBy` and `StudioPlacement.TryGetRegionPieces` read authored (serialized) data and work in edit mode without Awake (`TryGetRegionPieces` is what the fold preview already uses on stage objects).
- `PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath)` + `ClearDirtiness()` is equivalent to the stage's own Ctrl+S save (memory `unity-cli-pipeline` records this working).
- Sheet asset paths are stable identities within a set (the map's move/swap renames files), so a path-keyed pref is the right key.

## 10. Open questions

None blocking. Two notes surfaced by the review go in the final report for Aaron rather than as stops, because each has a working path today:

- Spawn points in editor prefs are per machine and not versioned (they do not travel with a clone or a second machine). Aaron chose editor prefs when offered "in the prefab" as the alternative; if versioned spawns turn out to matter, a `playtestSpawn` field under `Sheet`'s existing *Testing* header is the swap.
- A playtest starts with the Desk Player's saved abilities (none on the official Desk). `PlayerAbilities` is editable in Play Mode by design, so a sheet needing Swim can still be tested; a per-playtest pick is a follow-up if that gets old.

## Review (round 1)

Reviewer verdict: BLOCK.

- **B1** (stage sheet's `GridPosition` is always (0,0); the variant never carries it) — **accepted**, verified: no `gridPosition` in `Assets/Papercut/Sheets/**`; only `AddSheetToDesk`/`PlaceInstance` write it on the Desk instance. Fix: find the Desk instance by asset path and put its grid position in the request; cross-check the file name (§5 `TryFindDeskInstance`, §6 step 3).
- **B2** (blocks and paperweights are `IArrivalObstacle`s the edit-mode room test ignored) — **accepted**, verified in `PushableBlock.TryGetSolidFootprint`. Fix: `SpawnHasRoom` walks every active `IArrivalObstacle` under Front (§5).
- **N1** (move orphans the pref) — **accepted**: re-key on move, swap on swap.
- **N2** (`bounds` may be degenerate at Start) — **accepted**: box size from `BoxCollider2D.size` × `lossyScale`.
- **N3** (Gate (Off) ordering; wrong parenthetical) — **accepted**: text corrected; refusal on a Gate (Off) spot is by design and the marker shows it.
- **N4** (Studio running during play) — **accepted**: `OnGUI` early-outs with a notice while playing.
- **N5** (`EditorPrefs` is per install) — **accepted**: key prefixed with a hash of the project path.
- **N6** (tests for B1/B2) — **accepted** (§8).
- **N7** (disable the button while busy) — **accepted**.
- **Q1** (editor prefs are machine-local) — **not re-asked**: Aaron chose editor prefs over "saved in the variant" with that framing; the consequence is recorded in §10 and will be in the report.
- **Q2** (starting abilities) — **not asked**: out of the task's scope; `PlayerAbilities` is Play-Mode-editable by design, so nothing is untestable; noted in §10 and the report.

## Review (round 2)

Reviewer verdict: APPROVE WITH CHANGES. No blocking findings; every non-blocking one accepted.

- **N1** (re-deriving the Screen from `player.Position` after `PlaceAt` may read stale) — **accepted**: the playtest branch sets `currentScreen` from the sheet it resolved; the lookup-by-position path is only for the ordinary start.
- **N2** (box from `size × lossyScale` drops the collider offset) — **accepted**: the room box is centred at `point + offset × lossyScale`, in the navigator and in the Studio's marker/room test alike (the marker is drawn with the same offset so it matches the physics shape).
- **N3** (another sheet may claim the same grid position; `Desk.EnsureRegistry` keeps the first) — **accepted**: the Desk instance is resolved through `StudioSheetOps.BuildSlots` for the open Desk's folder; refuse on `Duplicate`, `Mismatch`, or `InstanceAssetPath != stage.assetPath`, and refuse when the slot's asset path is not the stage's (the stage is from another set). `TryFindDeskInstance` is dropped in favour of that.
- **N4** (a request written and then not consumed if Play Mode entry aborts before leaving edit mode) — **accepted**: the Studio's `OnGUI` clears a pending request whenever `!EditorApplication.isPlayingOrWillChangePlaymode`; the `EnteredEditMode` clear stays.
- **N5** (hold movement before `PlaceAt`, as `Transition` does) — **accepted**.
- **N6** (footprint from the prefab, not the Desk Player instance) — **accepted**: the Studio reads the open Desk Player's `BoxCollider2D` serialized size and offset, falling back to `Player.prefab` when there is no player.
- **N7** (contradictory caching text) — **accepted**: computed per repaint, no cache.
- **N8** (extract the runtime placement decision for unit tests) — **accepted**: `PlaytestStart.TryResolve(Desk, Request, Func<Sheet, Vector2, bool> hasRoom, out Sheet, out Vector2 world, out string reason)` is static and Awake-free (`Desk.TryGetSheet` builds its registry from children on demand); `ScreenNavigator` calls it and places the player. Tested in `PlaytestStartTests` with a Desk built in the test scene.
- **Q1** (starting abilities) — **asked; Aaron: "As planned: saved set"**.
- **Q2** (separate Spawn marker vs the fold pane's test ghost) — **asked; Aaron: "Separate Spawn marker"**.

## Deviations

- The stale-request guard drops a request only once it is older than 5 s (`StudioPlaytest.StaleRequestSeconds`) and the editor is not playing or about to: `EditorApplication.EnterPlaymode()` takes effect on a later tick, and a guard with no grace period would erase the request it had just written. It runs from `EditorApplication.update` (every tick, two `SessionState` reads while nothing is pending), not from the Studio's OnGUI, so closing the window cannot leave a request behind (code review S3).
- `SheetStudioWindow.RefreshSpawn` runs on the Layout event only, so the Desk lookup and obstacle walk happen once per frame rather than on every mouse event (code review S2).

## Code review

Reviewer verdict: REJECT (one must-fix), all findings resolved:

- **M1** (test desks' sheets logged the missing-root errors twice — once on AddComponent, again when the grid position was applied — which the real runner fails on; my reflection runner does not enforce unexpected logs) — **fixed**: the test sheets get Front/Back children, assigned in the same apply as the grid position, so the second validation is quiet.
- **S1** (`ReadPlayerFootprint` duplicated `StudioFoldSettings.ReadPlayerFootprintSize`) — **fixed**: the fold pane's reader now delegates to it; one prefab-path constant. Side effect: the fold pane's test ghost is sized from the open Desk's Player instance first, like the spawn marker.
- **S2** (spawn state recomputed on every OnGUI event; a missing-footprint warning would spam) — **fixed**: Layout pass only; warned once.
- **S3** (stale-request guard only ran while the Studio window repainted; 5 s window undocumented) — **fixed**: `EditorApplication.update`; recorded under Deviations.
- **S4** (pre-flight room-tests the stage sheet, the game tests the Desk instance, which may carry overrides) — **rejected for now**: the stage holds the edits about to be saved and played, so it is the more faithful shape; a Desk-instance override on a sheet's content is rare (the map places instances, the Studio edits the variant), and the runtime check catches it with a logged reason and a fallback start. Noted in the report.
- **S5** (a Spawn-mode click is not undoable) — **noted in the report**: a consequence of the editor-prefs storage Aaron chose; the click re-sets it.
