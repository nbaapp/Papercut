# Gates that start off, plates that drive several gates, and a Toggle plate

Date: 2026-09-21

## 1. Task

Aaron: *"Is there a way to have gates in the level editor that start off, and turn on on a button press? If there's not, can you make one? I want to have the ability to have multiple gates tied to one button, and pressing the button swaps them and stuff"*

Today a Gate starts present, a plate's only effect removes one object, each plate prefab carries one target slot, and the Studio's Link mode wires one plate to one gate. Deliverables:

1. A **Gate (Off)** element: a gate that rests absent and is present while a wired plate's effect is on it.
2. A plate effect that drives **any number of targets**, each switched away from its own resting state — so one plate turns Gate A off and Gate B on together ("swaps them").
3. A **Toggle** plate mode: each press flips the plate's effects on or off, and they hold until the next press (Hold and Latch stay as they are).
4. Studio Link mode that wires a plate to several targets (and unwires one), and shows a resting-absent gate as such.
5. The Bible, tests, migration of the four plate wires already authored on sheets.

**Out of scope:** a different in-game drawing for a Toggle plate or a present Gate (Off) (placeholder art; the Hold and Latch plates already share one drawing, and a present gate is a gate); ghosting the Gate (Off) in the Studio's fold-preview pane (the face panes carry the overlay; the fold pane renders content as the game does); wiring anything but GameObject targets; a plate pressed by anything new; the reset button / persistence questions (Bible §8, §11.16 — untouched: plate effects keep not resetting).

## 2. Design references

- Design Doc *Puzzles → Elements*: "Pushable Blocks/elements and Buttons", "Switches (instant effects) — Opens doors", "Switches (on/off effects)". The Toggle plate is the on/off switch; the Gate (Off) is a door that opens *and* a wall that appears.
- Bible §9 **[TENTATIVE]** puzzle elements: plates and the Gate were "built on Aaron's request (2026-08-27), as isolated playtest elements"; this extends them on Aaron's explicit request (2026-09-21). Cutting stays "deleting those components and prefabs": `Gate (Off)`, `Toggle Plate`, `ObjectPresence`, `SwitchPresenceEffect` — nothing in fold, occlusion or collision code learns of any of it.
- Bible §9 structural rule: "an element is a component, not a subclass"; "Features are data and components, not branches inside core systems". Toggle is a third `PlateMode` value on the one `PressurePlate` component (Hold and Latch are already "one component differing in data"); the resting state is data on the target; the effect is one component with a list.
- Bible §9 shared-target rule (fixed 2026-09-16): "A target shared by several effects … is removed while **any** of them removes it (`ObjectRemoval`)". Generalised, not changed: a target is *switched from its resting state* while any effect on it is applied. For a resting-present gate that is exactly today's rule.
- Bible §8 **[DECIDE]** object persistence, partly decided: "Gates shouldn't reset however (other effects of button presses might or might not reset, and that is determined by me.)" Plate effects are untouched by `Sheet.PlayerLeft` today and stay so; a Toggle plate's on/off state is therefore kept across leaving and returning, like a fired Latch. Not a new decision — the existing one applied to the new mode — but recorded in §8 so Aaron can reverse it per element later. A Toggle plate held by a block that resets on leaving is *released*, and a release does nothing to a Toggle plate (only presses flip it), so a block-driven Toggle keeps its state where a block-driven Hold plate lets go (the flagged Hold case, §8).
- Bible §5 *Movable objects* / `PressureRules`: pressing is unchanged. A Toggle plate flips on the **rising edge** of `IsPressed` (released → pressed), judged on the flat sheet like everything else.
- Bible §5 occlusion: a resting-absent gate is an inactive `TerrainRegion`; `SheetOcclusion` already notifies inactive occludees (`GetComponentsInChildren(true)`) and `RemoveObjectEffect`'s remark relies on "a deactivated `TerrainRegion` keeps receiving fold coverage, so it returns with the current clip". The one new wrinkle — a region inactive *from the start* has not run `Awake` — is handled in §6 (`ObjectPresence` applies the resting state in `Start`, after every `Awake`).
- Bible §7 arrival room test: `SheetOcclusion.SolidFootprints` walks active `IArrivalObstacle`s only, so an absent gate never blocks arrival and a present one does. No change.
- Bible §3 collision view: the Gate "keeps its own sprite" (no `TerrainFill`). The Gate (Off) is a variant of Gate and inherits that.
- Glossary: "Flip" is reserved (viewing the Back). The effect *switches* a target's *presence*; the plate mode is *Toggle*. Nothing here is called flip.
- `[DECIDE]` items touched: §11.8 persistence — not resolved; the existing "effects do not reset" behaviour is applied to Toggle and written down. §11.13 (which elements ship) — Checkpoint 1's call; this builds two more so they can be played. No `[LOCKED]` item is touched.

## 3. Decisions already made by Aaron (2026-09-21)

Asked before planning, with the current state explained (no start-off gate; one plate → one gate in the Studio):

1. *Full coding workflow or lightweight?* — **Full workflow.**
2. *Where should "starts off" live?* — **On the gate**: "A 'Gate (Off)' prefab in the palette carries the resting state. Any plate wired to it flips it from rest. One slot per plate, no popup, and two plates can never disagree about a gate's start state." (The alternative offered — two effect kinds per plate, "remove while pressed" / "present while pressed" — was declined.)
3. *Also a Toggle plate mode (each press flips its gates and they stay that way until the next press)?* — **Yes, add Toggle.**

Stated to Aaron with the questions, not corrected (treated as accepted):

- Link mode: click a plate, then click gates one after another to add them; the plate stays armed until Escape or a click on empty space; clicking an already wired gate unwires it.
- A gate wired to several plates is flipped while **any** of them is pressed, matching today's shared-gate rule.
- A resting-absent gate is drawn ghosted in the Studio panes. (Review N3: the **fold-preview pane** is the exception — it renders the stage's content through cameras as the game does, so a Gate (Off) shows there as a present gate. To be stated to Aaron in the report.)

Post-review questions and answers (2026-09-21, after plan review round 1):

4. *A Toggle plate with a block already on it at scene start, or a block that resets back onto it when you leave the sheet: should those count as presses?* — **"No, seed without flipping"**: at scene start and after a sheet reset the plate just records that it is pressed; only a new step-on flips it. A block authored on a Toggle plate leaves the puzzle as authored, and leaving never changes a Toggle.
5. *When a gate would appear where the player or a block stands, what should happen?* — **"Authoring rule only"**: as today's Hold plates — place things so it cannot happen; the tooltip states the rule; no new gameplay rule, no code.
6. *Should a Toggle plate's on/off state survive leaving and returning to the sheet?* — **"Persists, like a fired Latch."**

Post-review question and answer (2026-09-21, after plan review round 2):

7. *Two Toggle plates (or a Toggle and a Hold) wired to the same gate: how do they combine?* — **"Every press switches it"**: like two light switches in a hallway — each press on any wired Toggle switches the gate again, whatever the other plates are doing. (The offered alternative, "any plate on keeps it switched" — the OR rule stated before planning — was declined for Toggle.)

Engineering consequence of answer 7, stated to Aaron in the report as an assumption: a Toggle press is a *switch* delivered to the target, not a held state on the plate. So a Toggle plate has no on/off state of its own — the state lives on the gate — and its drawing is tinted with `pressedColor` only **while pressed** (momentary, like a Hold plate's look), which replaces the round-1 N4 note. A gate held by a Hold or Latch plate stays switched whatever the Toggle presses do to it meanwhile; when the hold lets go, the gate shows the toggled state (Hold/Latch "hold", Toggle "switches"; the two never cancel each other). Round-1 stated-to-Aaron bullet 2 ("switched while any of them is pressed") now applies to Hold and Latch only.

Round-2 N1/Q2 (left-click on empty space in Link mode): what was stated to Aaron — **it disarms the plate** — is what is built; §5 and the hint text say so. Not re-asked: Aaron accepted that description.

## 4. Files

### Runtime (`Assets/Papercut/Scripts/Objects`)

| File | Change | Purpose |
| --- | --- | --- |
| `RemoveObjectEffect.cs` → `SwitchPresenceEffect.cs` (rename file + `.meta`, GUID kept) | rename + modify | The plate effect: `GameObject[] targets`; Apply switches each target from its resting state, Revert withdraws. Keeping the GUID keeps the Hold/Latch plate prefabs and every authored sheet pointing at the same script. |
| `ObjectRemoval.cs` → `ObjectPresence.cs` (rename file + `.meta`) | rename + modify | The target's presence: an authored `startsAbsent` (the Gate (Off)'s only data) plus the runtime set of effects currently switching it. Present ⇔ (rest present) XOR (any switcher). |
| `PressurePlate.cs` | modify | `PlateMode.Toggle`; the mode state machine moves into `PlateRules` (pure) so it is testable; `IsApplied` replaces `IsLatched`; seeds on the first tick, on enable and after the sheet reset; tooltips name Toggle (review N3). |
| `PlateEffect.cs` | modify | Third operation, `Toggle()`: a press on a Toggle plate; summary names all three modes (review N3). |
| `PlateRules.cs` (+ `.meta`) | new | Pure: given the mode, the previous state, whether the plate is pressed now and whether this tick is a seed, the next state and the action to take (none / apply / revert / toggle). |

### Editor (`Assets/Papercut/Editor`)

| File | Change | Purpose |
| --- | --- | --- |
| `StudioLinks.cs` | modify | A slot may be a **list** of GameObject references (`targets`) as well as a single one; `Wire` appends (no duplicates), `Unwire` removes one, `Clear` empties. Discovery stays generic (any `PPtr<$GameObject>` field or array of them on any `PlateEffect`). |
| `StudioPane.cs` | modify | Link mode: the source stays armed after a wire; clicking a wired target unwires it; the right-click menu lists one *Clear* per wired target plus *Clear all*. `DrawLinks` draws one wire per target. A resting-absent element (`ObjectPresence.startsAbsent`) outlines dotted with an "(off)" label suffix, cached like the gated-terrain read. |
| `SheetStudioWindow.cs` | modify | Link-mode hint text. |

### Assets

| File | Change | Purpose |
| --- | --- | --- |
| `Prefabs/Terrain/Gate (Off).prefab` (+ `.meta`) | new | Variant of `Gate` with an added `ObjectPresence` (`startsAbsent` = true). Made through the editor (`InstantiatePrefab` in a preview scene → `AddComponent` → `SaveAsPrefabAsset`), the 2026-09-14 way. |
| `Prefabs/Objects/Toggle Plate.prefab` (+ `.meta`) | new | A copy of `Hold Plate` with `mode` = Toggle (Hold and Latch are separate copies, not variants; same shape). Made through the editor. |
| `Sheets/Desk/Sheet (-1,1).prefab`, `Sheets/Desk/Sheet (0,0).prefab`, `Sheets/Test Desk/Sheet (0,0).prefab` | migrate | The four authored `target` overrides become `targets[0]`. Through the editor (`LoadPrefabContents` → `SerializedObject` → `SaveAsPrefabAsset`), after backing the files up to the scratchpad and confirming the editor's view of each matches disk (memory: stale write-back). Snapshot of the wires taken before the recompile (scratchpad `wires_before.txt`): Desk (-1,1) Front/Hold Plate → Front/Gate and Back/Latch Plate → Front/Gate; Desk (0,0) Front/Hold Plate → Front/Gate; Test Desk (0,0) Front/Hold Plate → Front/Gate. Desk (1,0) (open in Aaron's prefab stage) has an unwired Hold Plate and is not touched. |
| `Prefabs/Objects/Hold Plate.prefab`, `Latch Plate.prefab` | re-save | Through the editor, so the stale `target: {fileID: 0}` line becomes `targets: []` (review N5; harmless data otherwise). |
| `Tools/check_links.py` | modify | The sheet glob becomes recursive (`Sheets/**/*.prefab`): it currently checks no sheet at all since they moved into per-scene folders (review N2). |

### Tests (`Assets/Papercut/Tests/EditMode`)

| File | Change | Purpose |
| --- | --- | --- |
| `RemoveObjectEffectTests.cs` → `ObjectPresenceTests.cs` (rename file + `.meta`) | rename + modify | The seven existing cases against `targets`; new: a resting-absent target is present only while switched; several targets on one effect; a null entry is reported and skipped; **Toggle** (answer 7): one Toggle switches a resting-present target away and a second Toggle brings it back, on a resting-absent target the mirror; two Toggle effects on one target alternate it whichever presses; a target held by a Hold stays switched through Toggle presses and shows the toggled state when the Hold lets go; a fired Latch masks toggles for good. |
| `PlateRulesTests.cs` (+ `.meta`) | new | Hold: Apply on press, Revert on release, nothing while held; Latch: Apply on the first press, nothing ever after; Toggle: Switch on each rising edge, nothing on hold or release; **seeding** (answer 4): a seed tick never produces Switch (a Toggle seeded pressed does nothing; the next unchanged pressed reading is not an edge; a release then press after the seed is), a Hold seeded pressed applies, a Latch seeded pressed fires (today's start behaviour). |
| `StudioLinksTests.cs` | modify | The slot is a list on all three plates; Wire appends, a repeat is one entry, Unwire removes, Clear empties, undo; Gate (Off) and Toggle Plate prefabs exist, Gate (Off) rests absent, Toggle Plate's mode is Toggle; Gate (Off) is a target, not a source. |

### Docs

| File | Change |
| --- | --- |
| `Documents/Claude Bible.md` | §9 first paragraph: plate modes Hold/Latch/Toggle; `SwitchPresenceEffect` with targets; `ObjectPresence` and the resting state; `Gate (Off)`; the shared-target rule restated as "switched while any"; cut = delete which files. §8: Toggle state kept across leaving (effects untouched by reset, as before). §11.8 note. Link mode sentence under §3 Studio mentions? — the Studio's link behaviour is described where plate link mode already is (§9 paragraph). |

## 5. Components & data

### `PlateMode` (in `PressurePlate.cs`)

```
Hold,   // Pressed while something is on it; its effects revert when it is released.
Latch,  // The first press applies its effects for good.
Toggle, // Each press switches its effects the other way; nothing happens on release.
```

### `PlateEffect` (modify)

A third abstract operation beside `Apply` / `Revert`: **`Toggle()`** — a press on a Toggle plate: switch the effect's outcome the other way and leave it there. The summary names the three modes: a Hold plate applies when pressed and reverts when released, a Latch plate applies once, a Toggle plate toggles on every press. Base stays empty of behaviour.

### `PlateRules` (static, pure)

```csharp
public enum PlateAction { None, Apply, Revert, Toggle }
public readonly struct PlateState { public readonly bool Pressed; public readonly bool Applied; }
public readonly struct PlateTick { public readonly PlateState State; public readonly PlateAction Action; }
public static PlateTick Step(PlateMode mode, PlateState previous, bool pressedNow, bool seed);
```

`Pressed = pressedNow` in every mode. `seed` is the first tick, the first after the plate is enabled, and the first after the sheet reset (answer 4): the plate takes the pressed state as its condition, not as a press.

- Hold: `Applied = pressedNow`; `Apply` when it becomes applied, `Revert` when it stops being applied (a Hold plate has no edges; a block resting on it holds it, seed or not — as today).
- Latch: `Applied = previous.Applied || pressedNow`; `Apply` once, when it first becomes applied (a block authored on a Latch fires it at start — today's behaviour, unchanged).
- Toggle: `Applied = false` always (the plate has no state of its own — the state lives on the target, answer 7); `Toggle` on a rising edge (`pressedNow && !previous.Pressed`) **unless `seed`**. Holding, releasing and seeding do nothing; after a seed the next *change* to pressed is the first edge.

### `PressurePlate` (modify)

Serialized: unchanged fields. Tooltips (review N3): `mode` gains the Toggle sentence; `pressedColor` reads "Colour while pressed (a Latch plate keeps it once fired; a Toggle plate shows it only while pressed — its state is on what it switches)". No new tunables (nothing here is a feel value: a press is a press).

Public surface: `IsPressed`, **`IsApplied`** (true while the plate holds its effects applied: Hold → pressed; Latch → fired; Toggle → never) replacing `IsLatched` (its only users are inside the plate), `PressedChanged` unchanged. `ShownPressed` = Latch → `IsApplied`; Hold and Toggle → `IsPressed`.

State: `PlateState state`, `bool seedNext`. `OnEnable` sets `seedNext = true` (scene start, and a plate that returns after being absent — review N6) and subscribes `sheet.PlayerLeft += RequestSeed` (`seedNext = true`); `OnDisable` unsubscribes. `PushableBlock.ResetToStart` runs synchronously in its own `PlayerLeft` handler, so by the plate's next `FixedUpdate` every block is back at its start and the seed reads the authored arrangement; handler order between the block and the plate does not matter.

`FixedUpdate`: compute `pressed` as now; `tick = PlateRules.Step(mode, state, pressed, seedNext)`; `seedNext = false`; act on `tick.Action` (`Apply` → `effect.Apply()` in order; `Revert` → `effect.Revert()` in reverse order; `Toggle` → `effect.Toggle()` in order); if `Pressed` changed → `PressedChanged`; repaint if `ShownPressed` changed; store `tick.State`. Behaviour for Hold and Latch is identical to today.

### `SwitchPresenceEffect : PlateEffect` (renamed from `RemoveObjectEffect`)

Serialized:

| Field | Type | Default | Notes |
| --- | --- | --- | --- |
| `targets` | `GameObject[]` | empty | Each is switched from its resting state while this effect is applied: a resting-present object (a Gate) is removed, a resting-absent one (a Gate (Off)) is present. Tooltip states the authoring rule, generalised (review N1, answer 5): a target must never be switched into a place where the player or a block can stand — a Gate (Off) appears where it is authored on any press, a Gate comes back on a Hold release or a Toggle's second press; nothing checks for overlap. |

`Awake`: no targets → `LogError("… has no targets; it will do nothing")`; a null entry → `LogError` naming the index, skipped forever after. `Apply`: `ObjectPresence.Of(t).Hold(this)` per non-null target. `Revert`: `Release(this)`. `Toggle`: `ObjectPresence.Of(t).Toggle()`.

### `ObjectPresence : MonoBehaviour` (renamed from `ObjectRemoval`)

Serialized:

| Field | Type | Default | Notes |
| --- | --- | --- | --- |
| `startsAbsent` | `bool` | false | The resting state. True on the Gate (Off) prefab; false (and never serialized) on the component `Of` adds at runtime. Design data per prefab, not a feel tunable. |

Runtime: `HashSet<Object> holders` (Hold and Latch effects currently applied) and `bool toggled` (switched by Toggle presses). **`IsSwitched => holders.Count > 0 || toggled`**; **`IsPresent => startsAbsent == IsSwitched`** (rest present & unswitched → present; rest absent & switched → present). `Of(GameObject)` as today. `Hold(Object)` / `Release(Object)` (were Remove/Restore), each counting once; `Toggle()` inverts `toggled`; each calls `Sync` only when `IsPresent` changed. `Sync() => gameObject.SetActive(IsPresent)`.

Combination rules this encodes (answer 7 and its stated consequence): every Toggle press on any wired plate switches the target again; a target held by a Hold or Latch stays switched through Toggle presses and shows the toggled state when the hold lets go; a fired Latch masks toggles for good. Nothing resets `toggled` (effects are untouched by the sheet reset — §8, answer 6).

`Start`: `Sync()` — applies the resting state once every component on the object has run `Awake` (a `TerrainRegion` deactivated before its `Awake` would never cache its collider, so fold coverage could not reach it; `SheetOcclusion` makes the same choice for the same reason). Start runs before the first physics step and the first rendered frame, so nothing ever touches or sees the one-Awake-old present gate. `IsRemoved` is gone; tests read `IsPresent`. Remark in code (review N6): for the component `Of` adds at runtime, `Start` may run only when the object is next active (a `Switch` may deactivate it the same frame); `Sync` is idempotent, so that late `Start` is a redundant `SetActive(true)`, never a second toggle — do not "fix" it into one.

### `StudioLinks` (modify)

- `Slot`: `Effect`, `PropertyPath`, `DisplayName`, **`IReadOnlyList<GameObject> Targets`**, **`IsList`**. Discovery: a visible property of type `PPtr<$GameObject>` is a single slot; a visible array property whose element type is `PPtr<$GameObject>` is a list slot (the iterator sees `targets` as `isArray`; element checks go through `GetArrayElementAtIndex(0)`'s type when size > 0, else the `arrayElementType` string `"PPtr<$GameObject>"`).
- `Wire(slot, target)`: list → append unless already present (a repeat is a no-op, reported nowhere — it is what a second click means); single → set. Undoable through `SerializedObject`, then `StudioEdits.Edited`.
- `Unwire(slot, target)`: list → remove that entry (`DeleteArrayElementAtIndex` after nulling, so the array shrinks rather than leaving a null); single → clear if it is the target.
- `Clear(slot)`: list → `arraySize = 0`; single → null.
- `Contains(slot, target)` helper for the pane.

### `StudioPane` (modify)

- Link left-click on a non-plate with a source armed: if `slots.Count == 1` → `Contains` ? `Unwire` : `Wire`; source **stays armed**. Several slots → the existing popup, each item toggling that slot. Left-click on empty space → the source **disarms** (what Aaron was told; Escape and right-click-empty disarm too). The hint: "Link mode: click a plate, then click each thing it affects (click a wired one to unwire it) · click empty space or Esc to release the plate · right-click a plate to clear its links".
- Link right-click on a plate: one *Clear <slot> → <target>* per wired target, plus *Clear all* when there is more than one.
- `DrawLinks`: one wire/marker per target of every slot.
- Resting-absent overlay: `RestsAbsent(GameObject element)` — cached `SerializedObject` read of an `ObjectPresence.startsAbsent` on the element (pattern of `HasRequiredAbility`; dropped by `InvalidateOverlayCache`). Such an element's collider outline is drawn with `Handles.DrawDottedLine` segments in its usual colour, and its label reads `"<name> (off)"`. Editor-only look, not a game tunable.

### Prefabs

- `Gate (Off)`: variant of `Gate`; added component `ObjectPresence { startsAbsent: 1 }`. Everything else inherited (Tiled sprite, solid box, `TerrainRegion` wall, z −0.05), so the Studio resizes and previews it exactly like a Gate.
- `Toggle Plate`: copy of `Hold Plate` with `mode: 2`, name "Toggle Plate". Same drawing and animation (Aaron's plate art is one drawing for all modes today).

## 6. Behaviour

**Authoring (Studio).** Aaron places a Gate (Off) from the palette; it draws with a dotted outline and "(off)". Link: toggle Link mode, click a plate (armed, highlighted), click Gate A (wired), click Gate B (wired), click Gate A again (unwired). Escape disarms; right-click the plate → clear one or all. Wires draw from the plate to every target.

**Play, resting state.** `ObjectPresence.Start` deactivates every Gate (Off) before the first frame. An inactive `TerrainRegion` stays a known occludee (`SheetOcclusion` collects inactive children), so its authored/clip colliders' enabled flags track the folds while it is away, and it is not an arrival obstacle (`GetComponentsInChildren(false)`).

**Play, scene start.** Every plate's first `FixedUpdate` is a seed: a block authored on a Hold plate holds it (as today), on a Latch fires it (as today), on a Toggle leaves it **off** and merely pressed (answer 4). Plates on every sheet keep evaluating whether or not the sheet is the Screen (`BlockPresser` answers for any sheet; `PlayerPresser` only for the Screen), as today.

**Play, a press.** `PressurePlate.FixedUpdate` → `PlateRules.Step`. Hold/Latch `Apply`: every target's `ObjectPresence` gains this effect as a holder; a Gate goes inactive, a Gate (Off) goes active (`OnEnable` re-pairs the player-ignore rule; its coverage is whatever the last notification left). Hold `Revert`: the holder is withdrawn. Toggle press: every target's `toggled` inverts. A target is present iff its resting state XOR (any holder or toggled).

**Several plates on one target.** Holders: any holder keeps the target switched — a Latch + Hold on a Gate is today's rule, unchanged; on a Gate (Off), present for good once the Latch fires. Toggles: every press on any wired Toggle switches the target again (answer 7) — two Toggles on one Gate alternate it whichever is pressed. Mixed: a held target ignores toggles visibly until the hold lets go, then shows the toggled state. Stated in the Bible.

**Leaving the sheet.** `PlayerLeft`: blocks teleport to their start rects (`PushableBlock.ResetToStart`), the player stops counting as a presser (not the Screen), and every plate on the sheet requests a seed. The next `FixedUpdate` reads the authored arrangement: a Hold plate is pressed iff a block rests on it (as today), a Latch stays fired, and a Toggle plate records the reset arrangement as its condition without a press (answer 4) — so a block pushed off a Toggle plate and reset onto it does not switch anything, and leaving never changes what a Toggle switched (answer 6: the toggled state lives on the target and nothing resets it). A genuine step-on after returning is a new edge as usual.

**Failure modes (none silent).** No targets → error at Awake, effect does nothing. Null target → error naming the index. A `PressurePlate` with no effects → warning (as today). A Gate (Off) not under a face root → `TerrainRegion` already reports and goes inert; `ObjectPresence` still applies the rest state (it is about the object, not the sheet). `StudioLinks.Wire/Unwire` on a missing property → error (as today).

## 7. Interfaces & seams

- `PlateEffect` (Apply/Revert) is unchanged: Toggle is a mode of the plate, invisible to effects. A future effect kind still just subclasses `PlateEffect`.
- `ObjectPresence` is the one place that knows what "present" means for a target; effects only switch. A future effect that needs a different notion of presence would be a different component, not a flag here.
- `PlateRules` separates *when a plate's effects are on* from `PressurePlate`'s scene plumbing, so modes are tested as a table.
- Cutting: Toggle → delete `Toggle Plate.prefab` and the enum value (one `PlateRules` case); Gate (Off) → delete the prefab, `startsAbsent` and the Studio's dotted overlay; the whole plate system → delete the `Objects` components and prefabs as before. Fold/occlusion/collision code is untouched by this task.

## 8. Testing

**Edit Mode tests (mechanical, run through `run_tests {"mode":"editor"}`):** `ObjectPresenceTests`, `PlateRulesTests`, `StudioLinksTests` as listed in §4. The whole suite runs; every pre-existing result is reported, failures verbatim.

**CLI checks:** `recompile` clean; `run_tests` editor; prefab checks by eval (`Gate (Off)` is a Variant of Gate with `ObjectPresence.startsAbsent`; `Toggle Plate` mode 2; the three migrated sheets' effects report the right `targets` and no `target` override remains — and re-grep disk after every play/stop); `python Tools/check_links.py` with its sheet glob made recursive so it actually covers the migrated sheets (review N2). Play Mode probe on the Test Desk: instantiate a Toggle Plate, a Gate and a Gate (Off) under the Screen's Front (inactive holder → position → reparent, the 2026-09-14 pattern), wire through `SerializedObject`, `SheetOcclusion.Apply()`, then read: Gate (Off) inactive after one frame; teleport the player onto the plate → next FixedUpdate: Gate inactive, Gate (Off) active; off and on again → both back; fold a Flap over the Gate (Off) while it is absent, press → it comes back clipped (`RuntimePolygon` present). **Seed path (review N4):** with the Gate switched by one Toggle press, push/teleport a block onto the Toggle plate (its start is elsewhere), then leave the sheet and come back (drive `ScreenNavigator`, or `Sheet.NotifyPlayerLeft`/`NotifyPlayerEntered` by reflection) — the block resets off the plate, and after two FixedUpdates the Gate is still switched (no extra toggle) and the plate is released. **Scene-load ordering (review N5):** a runtime-instantiated Gate (Off) runs `Start` after that frame's `FixedUpdate`, so the probe checks the mechanism (absent after `Start`), not the claim that a scene-loaded one is never present during a physics step; that claim rests on Unity's documented order (Awake/OnEnable/Start of scene objects complete before the first FixedUpdate) and is stated as untested in the report. `get_console_logs` clean, `capture_game_view` described literally. Before `run_tests` and any prefab save: probe the active scene's `isDirty` and the open prefab stage (review N7: if the stage is dirty — Aaron's Desk (1,0) holds a Hold Plate instance and re-saving `Hold Plate.prefab` reloads it — stop and ask Aaron to save or close it before the plate re-saves; the sheet migrations do not touch that sheet); back up modified sheets.

**Aaron's playtest (what the CLI cannot judge):** whether a Toggle plate reads as a toggle when pressed by walking over it repeatedly (a walk-over is one press; walking back off and on is the next — is that the feel, or should a Toggle need a fresh step?); whether "swap" puzzles read at a glance with both gates drawn the same; whether a Gate (Off) appearing on a fold-clipped region looks right; whether a Toggle state surviving a screen exit is what he wants per element (§8).

## 9. Assumptions (engineering)

- `SerializedProperty` iteration with `NextVisible` reaches the `targets` array property; a `GameObject[]` array's `arrayElementType` is `"PPtr<$GameObject>"`. Verified at code time against the real component; a test covers discovery on the real prefabs.
- `PrefabUtility.GetPropertyModifications` on a sheet variant still returns the stale `target` overrides after the field is renamed (Unity keeps modifications whose property no longer exists until `RemoveUnusedOverrides`). If it does not, the migration falls back to a snapshot taken by eval *before* the recompile (sheet path → effect hierarchy path → target hierarchy path).
- Renaming a script file together with its `.meta` keeps the GUID, so no prefab or sheet reference breaks; the class name matches the new file name.
- `Start` on an object under a sheet runs before the first `FixedUpdate` and before the first render (standard Unity order), so the resting-absent gate is never seen or collided with while present-by-default.
- A dotted outline is drawn with `Handles.DrawDottedLine` per edge, in the pane's IMGUI space like `DrawAAPolyLine`.
- Vocabulary (review N2): code, comments, tooltips, test names and Bible lines say *toggle* / *switch*, never *flip* (reserved for viewing the Back); "flip" survives only inside verbatim quotes.
- Hold/Latch and Toggle combine as "held OR toggled" (§5 `ObjectPresence`). Answer 7 settles Toggle-with-Toggle; the mixed case is my reading of it (a hold is a hold; a toggle switches what is underneath) and is stated in the report for Aaron to correct.

## 10. Open questions

None at plan time. (The one candidate — whether a Toggle plate should need the presser to step fully off before it counts as a new press — is exactly what "rising edge of `IsPressed`" gives, and anything else would be a new pressing rule; listed for Aaron's playtest instead.) The plan review's three questions were asked and answered — §3, answers 4–6.

## Review

Round 1 (plan-reviewer, 2026-09-21) — verdict BLOCK.

| # | Finding | Disposition |
| --- | --- | --- |
| B1 | Toggle on every rising edge would flip at scene start with a block authored on the plate, and again when a block resets onto it on `PlayerLeft`; §6 misdescribed leaving; the "starts pressed" test had no stated expectation. | **Escalated → answered** (Q1 = answer 4: seed without flipping). Plan: `PlateRules.Seed`, `seedNext` on the first tick and on `PlayerLeft`; §6 rewritten; tests state the expectation. |
| N1 | The authoring-rule tooltip ("a Hold plate's target must not come back on top of…") is too narrow now that a Gate (Off) appears on any press and a Gate returns on a Toggle's second press. | **Accepted** — tooltip generalised (Q2 = answer 5: authoring rule only, no overlap code). |
| N2 | `Tools/check_links.py` globs `Sheets/*.prefab` non-recursively; the sheets live in per-scene folders, so the migrated sheets would not be checked. | **Accepted** — glob made recursive (in Files). |
| N3 | Aaron was told the Gate (Off) is ghosted "in the Studio panes"; the fold-preview pane renders it as a present gate. | **Accepted** — recorded in §3 as something to state to Aaron in the report. |
| N4 | Toggle plate tinted `pressedColor` while on is an unstated player-facing choice. | **Accepted** — recorded in §3 for the report. |
| N5 | Hold/Latch plate prefabs keep a stale `target: {fileID: 0}` line after the rename. | **Accepted** — both re-saved through the editor (in Files). |
| N6 | `ObjectPresence.Start` on the runtime-added component may run late and redundantly. | **Accepted** — remark in code; `Sync` is idempotent. |
| Q3 | Does Toggle state persist across leaving? | **Asked** — answer 6: persists, like a fired Latch. |

Round 2 (plan-reviewer, 2026-09-21) — verdict APPROVE WITH CHANGES.

| # | Finding | Disposition |
| --- | --- | --- |
| N1 / Q2 | Aaron was told left-click on empty space disarms the plate; §5 kept it armed. | **Accepted** — left-click-empty disarms (what was stated); §3, §5 and the hint agree. Not re-asked. |
| N2 | "flip" used for the Toggle throughout; the glossary reserves it. | **Accepted** — code/comments/tooltips/tests/Bible say toggle/switch (assumption list). Plan prose left as is where it quotes. |
| N3 | `pressedColor` tooltip and `PlateEffect` summary would be wrong with Toggle. | **Accepted** — both updated (§5). |
| N4 | The `PlayerLeft` → seed path is only table-tested. | **Accepted** — Play Mode probe leaves and returns with a block on a Toggle plate (§8). |
| N5 | The probe cannot confirm scene-load ordering for `Start`-time deactivation. | **Accepted** — stated as resting on Unity's documented order, untested, in §8 and the report. No Gate (Off) is authored on Aaron's sheets for it. |
| N6 | A plate that returns after being absent would treat what stands on it as an edge. | **Accepted** — `seedNext = true` in `OnEnable`. |
| N7 | Re-saving `Hold Plate.prefab` reloads Aaron's open, possibly dirty, stage. | **Accepted** — stage dirtiness probed first; if dirty, ask Aaron before the plate re-saves (§8). |
| Q1 | Two Toggles on one gate: OR or every-press-switches? | **Asked** — answer 7: every press switches it. Model changed: `PlateEffect.Toggle()`, `ObjectPresence` holders + toggled bit, Toggle plates stateless and momentary-tinted (§3, §5, §6). |

## Deviations

- The Play Mode probe's block step needed the plate re-enabled after the block was spawned: a `PressurePlate` collects the sheet's pressers when it is enabled (existing behaviour; blocks are authored, never spawned at play), so a runtime-spawned block is invisible to it until then. Re-enabling is also exactly the "block authored on the plate at its first tick" case, so the probe covered that too.
- Probe positions moved above Sheet (0,0)'s water region after the first run landed the plate under an authored Block (held from tick one, no edges possible).

## Code review

Round 1 (code-reviewer, 2026-09-21) — verdict APPROVE WITH FIXES; no must-fix.

| # | Finding | Disposition |
| --- | --- | --- |
| S1 | `StudioPlacement.Delete` never unwires a deleted target; `Slot.Targets` hides the null, so the plate reports "target 0 is not assigned" at Play and the Studio cannot show or clear it. | **Fixed** — `Delete` unwires the element from every slot on both faces (same undo group); `Wire`/`Unwire` also compact null entries. Tests: `Delete_UnwiresTheElementFromEveryPlate_OnBothFaces`, `Wire_DropsAStaleNullEntry`. |
| S2 | A duplicate entry in `targets` (Inspector-authored) toggles the target twice per press, i.e. does nothing. | **Fixed** — `Awake` reports a repeat and drives a deduplicated list. Test: `DuplicateTarget_IsReportedAndCountedOnce`. |
| S3 | `Slot.Target` and `Wire(slot, null)` had no production callers. | **Fixed** — both removed; `Wire(null)` is an error pointing at `Clear`; tests use `Targets`/`Clear`. |
| S4 | Dotted outline used a 2-px dash (reads as solid); the `DrawDottedLine` assumption was unverified. | **Fixed** — `DottedDashPixels = 6f`. The pane's look is not screenshot-verified (an editor-window capture needs Unity brought to the front, which would steal Aaron's focus); stated in the report. |
| S5 | The seed plumbing is only table-tested; the probe result must be reported literally. | **Accepted** — the probe's literal reads are in the report. No PlayMode test assembly exists in the project; adding one is outside this task and noted for Aaron. |
| Q1 | Hold/Latch + Toggle on one target: hold wins while held, toggled state shows on release (as built) vs always-visible toggle vs ignored. | **Reported to Aaron as the assumption it is** (plan §3, §9); not re-asked. |
| Q2 | Arriving on a Toggle plate authored on the arrival strip is a rising edge every return. | **Kept as built** — the player walked onto it from the neighbouring sheet; a step-on. Reported to Aaron as an authoring note. |

Reviewer's test run (Aaron declined the editor suite because the Desk scene was unsaved): `PlateRulesTests`, `ObjectPresenceTests`, `StudioLinksTests` through a reflection runner in an additive empty scene — pass 31, fail 0, skipped 1 (the undo test, to keep out of Aaron's undo history).

After the fixes, the same runner: `PlateRulesTests` 9/9, `ObjectPresenceTests` 17/17, `StudioLinksTests` 8/8 + 1 skipped (`Wire_AppendsTargets_ARepeatIsOneEntry_AndUndoRevertsIt`, undo). Two rounds were needed: the S2 fix first built the deduplicated target list only in `Awake`, which Edit Mode never calls, so 14 `ObjectPresenceTests` failed until the list resolves lazily on first use as well; and the resting-absent test helper's `SendMessage("Start")` logged Unity's "ShouldRunBehaviour()" assertion in Edit Mode (the real runner would count it as an unexpected error) — it now invokes `Start` by reflection. Console after the final run: only the errors the tests expect (`LogAssert.Expect`). The full editor suite was not run (Aaron's call); the Desk scene reads clean now, so it can be run any time.
