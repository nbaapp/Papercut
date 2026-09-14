# Sheet Studio — Phase 3: place-through-fold and x-ray

## 1. Task

The final phase of the level editor: (a) **full editing through the fold preview pane** — place armed elements, select, move, and delete in the folded view, with everything landing on the correct face at the correct authored position; (b) the **x-ray hold** — hold a key over a face pane to see the other face's content ghosted through the paper, mirrored as it physically lies beneath.

**Out of scope:** any runtime change; simulating occlusion/objects in the preview (unchanged from Phase 2); resizing through the fold pane (resize handles stay in the face panes — the fold view's mapping would make handle math ambiguous across creases for marginal benefit).

## 2. Design references

- **Bible §6 (back-authoring convention, [DECIDED]):** chosen by Aaron explicitly "so a future level editor can show both faces flat and preview a fold from the same authored data" — this phase completes that intent. The pane→authored mapping is the fold renderer's UV mapping inverted: topmost layer at desk point p → `original = layer.ToDesk.Inverse.Apply(p)`; `FrontUp` → (Front root, original), else (Back root, `SheetGeometry.BackToFront(original)`).
- **Bible §6 [TENTATIVE] Flip mechanic:** the x-ray hold is **not** it and must not be conflated with it — it is an editor-only visibility aid (Aaron's original ask: "hold down a button to see through to the back of the sheet"); nothing runtime is built.
- **Bible §3 [LOCKED] not tile-based:** free placement continues; the optional snap applies in authored space.
- **Bible §4/§8 [LOCKED]:** fold geometry untouched; preview folds still never saved. Element edits through the fold pane are ordinary authored-content edits (the same Undo-tracked operations as the face panes), which is the point.
- **implementation-guidelines §4a:** no runtime behaviour → no game tunables. X-ray key (Tab) and overlay alpha (0.45) are editor constants.

## 3. Decisions already made by Aaron (2026-08-31)

- **Full editing** through the fold pane (place + select + move + delete), chosen over place-only and place+select.
- **Follow-the-cursor drags:** an element dragged in the folded view lands on whatever paper is visibly under the cursor — crossing onto a flap moves it to that flap's face at the mirrored authored spot, live.
- **Fold gestures first:** fold-pane click priority is armed-palette place → ghost → Seam unfold → crease grab → edge/corner fold → element select/move. Consequence, stated fully (round-1 N6): elements inside the fold-gesture grab zones — within `edgeGrabMargin` (default 0.35 u) of a sheet edge, `cornerGrabRadius` (0.6 u) of a corner, or `creaseGrabDistance`/`unfoldGrabDistance` (0.3 u) of a crease/Seam — are not selectable in the fold pane; use the face panes there. The hint line says so.
- **Hidden pieces are marked, dimmed** (2026-08-31, plan-review Q1): where a selected element lies face-down in the folded view, the highlight shows that piece in a distinct dimmed style, so the element's whole physical extent reads through the fold.
- **Link mode stays face-pane-only** (2026-08-31, plan-review Q2): while Link mode is on, the fold pane's element branch is inert (clicks past the fold gestures do nothing), and the hint says wiring happens in the face panes.
- Phases and full-workflow-per-phase as before.

## 4. Files

| File | Purpose |
| --- | --- |
| `Assets/Papercut/Editor/StudioFoldMapping.cs` *(new)* | **Pure** mapping between the folded view and authored space: topmost-layer lookup, desk point → (face, authored position), and an element's authored box → per-layer clipped desk-space outline pieces (for highlights/ghosts). Unit-testable. |
| `Assets/Papercut/Editor/StudioPlacement.cs` *(modified)* | New op `MoveMapped(element, sheet, face, authoredPos, snap)`: moves an element and, when the target face differs from its current parent, reparents it under the other face root (`Undo.SetTransformParent`) and re-applies that face's layer recursively; z preserved. |
| `Assets/Papercut/Editor/StudioFoldPane.cs` *(modified)* | Element interactions appended after the fold gestures (Aaron's priority); mapped move drag; armed-prefab ghost with paper/no-paper feedback; selection highlight drawn through the fold (per-layer clipped outline). |
| `Assets/Papercut/Editor/StudioPane.cs` *(modified)* | X-ray: while held, a second camera pass renders the *other* face (mirrored about the sheet's vertical centreline — camera centred at (−cx, cy), blitted x-flipped) into a lazily created RT, overlaid at the editor alpha. Composes correctly with the Back pane's mirror toggle. Extra camera/RT disposed with the pane. |
| `Assets/Papercut/Editor/SheetStudioWindow.cs` *(modified)* | Tracks the x-ray hold (Tab KeyDown/KeyUp, consumed so IMGUI focus-cycling doesn't fire) and passes it to the face panes; fold-pane context gains what element editing needs. |
| `Assets/Papercut/Tests/EditMode/StudioFoldMappingTests.cs` *(new)* | Mapping truth tests (see §8). |
| `Assets/Papercut/Tests/EditMode/StudioPlacementTests.cs` *(modified)* | `MoveMapped` tests. |

Cutting Phase 3 = deleting the mapping file + its tests and reverting the pane/window additions; Phases 1–2 stand alone.

## 5. Components & data

**`StudioFoldMapping` (static, pure)** —

- `TryMapToAuthored(SheetLayers layers, Sheet sheet, Vector2 deskPoint, out Transform faceRoot, out SheetFace face, out Vector2 authoredLocal)`: iterates `layers.Layers` topmost-first; the first layer whose `Desk` polygon contains the point wins; false over empty desk. Uses the inversion in §2. `sheet` only supplies the two roots.
- `AuthoredBoxToDeskPieces(SheetLayers layers, SheetFace face, Rect authoredRect, List<(ConvexPolygon piece, int layerIndex, bool faceUp)> results)`: the element's authored-space rect (Back rects taken through `BackToFront` into flat sheet space first), intersected with each layer's `Original` region and transformed by that layer's `ToDesk`. **Each piece carries whether the element's face is up there** — `faceUp = (layer.FrontUp == (face == SheetFace.Front))`, the same orientation test as `SheetLayers.Coverage` (round-1 B1). Face-up pieces highlight in `SelectionColor`; face-down pieces in a distinct dimmed style (Aaron, Q1). Pieces under higher layers are still returned (the highlight shows through; exact occlusion subtraction is not worth its complexity for a selection cue — stated approximation).

**`StudioPlacement.MoveMapped`** — same-face: delegates to `Move`. Cross-face: `Undo.SetTransformParent(element.transform, targetRoot)`, then position (x/y snapped, z preserved), and the re-layer to the target face's layer goes through **`Undo.RegisterFullObjectHierarchyUndo` before the raw layer writes** (round-1 B2 — undo-group membership does not capture unrecorded field writes; without this an undone cross-face move leaves the element under one root but on the other face's layer, silently invisible in its pane and wrong in both renderers). All in the drag's collapsed undo group.

**`StudioFoldPane` additions** — drag kind `Element` (grabbed element + authored-space grab offset per current face); press handler appended after the fold gestures per §3; each drag update maps the cursor and calls `MoveMapped` — when the cursor is over empty desk the element simply stays where it last was (never dropped onto desk). Armed-prefab ghost: cursor mapping decides the target; footprint outline drawn at the cursor, `GhostColor` over paper, `InvalidTint`-red over empty desk, with the eventual face shown as a small label ("→ Back"). Selection highlight: `AuthoredBoxToDeskPieces` outlines in `SelectionColor`.

**`StudioPane` x-ray** — `Context` gains `XRayHeld`; on Repaint with it set, ensure the second camera/RT (other face's cull mask, centre mirrored, **clearing to `Color.clear`** like the fold rig's face cameras — an opaque clear would wash the whole pane with tint, round-1 N2), submit its render request before the main one, then draw it over the pane with x-flipped texcoords at `XRayAlpha = 0.45f` (via `GUI.color`). The overlay flip composes with the pane's own mirror coords (Back pane mirrored: flips cancel) — **all four combinations are probed in §8** (round-1 N3). Failure to create the RT reports through the pane's existing help-box path — never silent.

**Window** — `xrayHeld`: Tab **KeyUp clears the hold unconditionally, before any guard**; Tab KeyDown sets it only when `EditorGUIUtility.editingTextField` is false (and is then consumed to suppress IMGUI focus cycling) — otherwise field navigation breaks or the hold sticks (round-1 N1); also cleared on `OnLostFocus`. Passed in both face-pane contexts; the toolbar hint mentions "hold Tab: see through the Sheet". **Fold-pane `Context` gains real plumbing** (round-1 N8 — today it carries only Stage/Model/Scene/Settings): `Sheet`, `Palette`, `SnapIncrement`, and `LinksActive` (so the element branch can go inert in Link mode per §3).

## 6. Behaviour

1. **Place through the fold:** with a prefab armed, click the folded view → the point maps through the topmost layer to (face, authored position) → placed with the existing `Place` op (authored z, recursive layer, Undo, selected). Over empty desk: refused with a message ("No Sheet under the cursor."). The element immediately appears in the composite (it is face content, rendered through the face textures) — visibly on the flap where it was dropped, at the mirrored spot on its face root in the face panes. This is Aaron's original ask verbatim.
2. **Select:** click (after fold gestures decline) maps the point and hit-tests that face's colliders at the authored position with the existing `PickElement` — same guard rails (`CanEdit`), same Inspector sync. Highlight per §5.
3. **Move:** dragging a selected element remaps every update (follow-the-cursor): on the same face it is a normal `Move`; crossing onto the other face's content it reparents live (`MoveMapped`), so the element slides continuously through the folded landscape and lands exactly where dropped. Undo collapses to one step per drag. **The grab offset is carried in desk space** and mapped through the current layer each update (round-1 N4 — an authored-space offset would mirror when crossing onto a mirrored flap and make the element jump under the cursor).
4. **Delete / duplicate / nudge:** unchanged window-level keyboard ops on the selection — they already work regardless of which pane selected the element. (Nudge moves in authored space; its arrows are face-pane-oriented — stated, not remapped.)
5. **X-ray:** hold Tab over a face pane → the other face's content appears through the paper at 45 % alpha, mirrored about the sheet's vertical centreline (what is physically beneath point (x, y) is Back-space (−x, y) — `SheetGeometry.BackToFront`, the same single source as everything else). Works in both panes; composes with the Back-mirror toggle; released or window unfocused → gone. Editor visibility aid only — explicitly not the [TENTATIVE] Flip mechanic.
6. **Failure modes:** mapping over empty desk refuses with a message (place) or holds position (move); x-ray RT creation failure surfaces in the pane; everything else inherits Phase 1/2 behaviour (no silent failures).

## 7. Interfaces & seams

- `StudioFoldMapping` is pure and the single home of view→authored inversion; the pane, ghost, and highlight all go through it, so they cannot disagree with each other or with the renderer's UV math (same isometries, same `BackToFront`).
- No runtime file changes. All new behaviour is editor-only and additive; the existing ops (`Place`, `Move`, `PickElement`, `CanEdit`) are reused rather than duplicated, with `MoveMapped` the one new op.

## 8. Testing

**Mechanical (Unity CLI):**

- `recompile` clean; full EditMode suite.
- `StudioFoldMappingTests`: flat sheet → identity mapping to Front; after an east edge fold, a point on the landed flap maps to Back at the reflected-then-mirrored authored position (checked against `Isometry2D`/`BackToFront` truth); a twice-folded region maps Front-up-again; topmost layer wins where layers stack; empty desk → false; `AuthoredBoxToDeskPieces` for a Front element straddling a crease after one fold returns **one face-up piece (the base) and one face-down piece (on the flap), correctly flagged and placed** (round-1 B1's corrected expectation), and a Back element's rect goes through `BackToFront` first.
- `StudioPlacementTests` additions: `MoveMapped` same-face moves like `Move`; cross-face reparents under the other root, keeps z, re-layers every child, and one Undo restores parent/position/layers.
- `StudioPlacementTests` additions include: undoing a cross-face `MoveMapped` restores parent, position, **and every child's layer** (the B2 regression case).
- Live checks on `Sheet (0,0)`: fold via the model, place a Wall through the flap via the ops (verify parent = Back root, authored position mirrored correctly via eval), drag-move it across the crease via `MoveMapped` calls, undo everything; screenshot the folded view with the placed element visible on the flap; **x-ray probed in all four states** — Front pane, Back pane with Mirror Back off, Back pane with Mirror Back on (hold state set via reflection for the captures), and released (overlay gone) — the mirror-composition claim is exactly the kind of double negation that ships wrong (round-1 N3); `git status` clean of asset changes afterwards (all edits undone).

**Aaron needs to play/use:** whether follow-the-cursor dragging through folds feels right; whether fold-gestures-first click priority works in the hand on busy sheets; whether the x-ray overlay reads clearly (alpha, mirroring intuition); end-to-end: author a real puzzle plan using fold + place-through + x-ray, then verify in Play Mode.

## 9. Assumptions

1. X-ray key is Tab (consumed to suppress IMGUI focus cycling) and alpha 0.45 — editor constants, trivially changeable on request.
2. Placement/move through the fold maps the element's **centre point**; an element dropped near a crease may straddle it in authored space — that is authorable reality (the game clips at runtime; Bible §4 straddle rules apply in play, not authoring).
3. The selection highlight shows pieces even where a higher flap covers them (§5 approximation, stated in code).
4. Reparenting mid-drag via `Undo.SetTransformParent` collapses cleanly with the existing per-drag undo-group pattern; the layer change is hierarchy-undo-recorded (B2).
5. The second x-ray camera per face pane reuses the Phase 1 render mechanism (validated); it renders only while held, so idle cost is zero.
6. Highlight geometry is `Rect`-based from the root `BoxCollider2D`; an element without one (none of the current palette prefabs) falls back to the bounding rect of its collider outlines, so it never silently loses its highlight (round-1 N8).
7. "paper" stays out of identifiers, comments, and UI strings (glossary; round-1 N7).

## 10. Open questions

None — scope, drag semantics, and click priority were asked pre-plan; the two review questions (hidden-piece highlighting, Link mode reach) were asked and answered after round 1 (§3).

## Review

**Round 1 (plan-reviewer, 2026-08-31): verdict BLOCK.** Dispositions — all accepted:

- **B1 (blocking, accepted):** `AuthoredBoxToDeskPieces` lacked the face-orientation filter (`layer.FrontUp == wantFrontUp`, mirroring `SheetLayers.Coverage`), so highlights would mark face-down pieces indistinguishably and the test expectation baked the bug in. Fixed: pieces carry a `faceUp` flag; the escalated display question (**Q1 → Aaron: mark hidden pieces, dimmed**) decides the styling; tests assert the corrected one-up-one-down expectation.
- **B2 (blocking, accepted):** the cross-face re-layer was a raw field write inside an undo group, which undo cannot restore — an undone move would strand the element on the wrong layer, silently invisible. Fixed: `RegisterFullObjectHierarchyUndo` before re-layering; regression test added.
- **N1 (accepted):** Tab KeyUp clears the hold before any guard; KeyDown respects the text-field guard.
- **N2 (accepted):** the x-ray camera clears to `Color.clear`, not the opaque pane background.
- **N3 (accepted):** x-ray verified in all four pane/mirror states, plus released.
- **N4 (accepted):** grab offset carried in desk space (authored-space offsets mirror across flaps).
- **N5 → Q2 escalated → Aaron:** Link mode stays face-pane-only; the fold pane's element branch is inert while it is on.
- **N6 (accepted):** the fold-gesture dead zones for element selection are stated fully (§3) and mentioned in the hint line.
- **N7 (accepted):** glossary discipline for implementation strings/comments.
- **N8 (accepted):** the Context-plumbing claim corrected (it is real work); highlight fallback for boxless elements stated.

**Round 2 (plan-reviewer, 2026-08-31): verdict APPROVE WITH CHANGES.** No blockers, no design questions; all five findings accepted and pinned:

- **N1-r2:** element-drag cancel defined — Escape/right-click/middle-press on an element drag **reverts** via `Undo.RevertAllDownToGroup` (matching Escape's meaning in the rest of the pane); MouseUp collapses the group as planned. `CancelDrag` handles both kinds.
- **N2-r2:** x-ray pinned — while held it overlays **both** face panes simultaneously, drawn over the composite texture and under the collider/link/selection outlines.
- **N3-r2:** drag-mapping rule pinned — the **cursor** picks the topmost layer (and alone decides empty-desk); cursor+offset maps through that layer's isometry, extrapolated, so motion is continuous near boundaries.
- **N4-r2:** `MoveMapped`'s cross-face reparent refuses (with a `Report` message, no Unity console error) an element that is part of the base Sheet prefab rather than an added instance — impossible with Studio-placed content today, guarded anyway.
- **N5-r2:** the press-priority chain (armed → ghost → Seam → crease → anchor → element), Link-mode inertness, and dead zones get mechanical coverage: the chain is extracted into an internal, headless-testable method the pane's `OnPress` delegates to.

## Code review

**Round 1 (code-reviewer, 2026-08-31): verdict REJECT** (1 must-fix, 7 should-fix, no design questions). Dispositions — all accepted and fixed:

- **M1 (fixed):** the element-drag grab offset was computed through whichever layer carried the element's *centre* (`TryMapAuthoredToDesk`), not the layer the press mapped through — a crease-straddling element grabbed by its visible part would teleport on the first drag pixel. Fixed: the press keeps its layer index and extrapolates that same isometry for the offset, so press and drag mappings are coherent.
- **S1 (fixed):** "paper" removed from test comments (glossary).
- **S2 (fixed):** the over-desk placement ghost draws the footprint in the sheet's authored `InvalidTint`, per plan, not a hard-coded red dot.
- **S3 (fixed):** press-chain tests extended — crease-beats-element, edge-anchor-beats-element (the dead zones), and ghost-beats-element.
- **S4 (fixed):** the twice-folded test now asserts first-principles truth (probe → Back at `(p.x − 5, p.y)` for the flipped two-pile region), not a tautology; the test documents why a twice-folded piece is never the visible surface under no-overhang rules (a fold flips the pile, so the base lands on top).
- **S5 (fixed):** highlight and placement ghost map through `DisplayLayers`, agreeing with the composite beneath them during fold drags.
- **S6 (fixed):** clicking bare desk clears the selection, consistent with empty sheet and the face panes.
- **S7 (fixed):** `xrayHeld` also clears on stage change and in the no-stage branch (a missed KeyUp can't leave the overlay stuck).

After fixes: recompile clean; **234/234** EditMode tests pass.

**Round 2 (code-reviewer, 2026-08-31): verdict APPROVE WITH FIXES.** All round-1 fixes verified real; the M1 offset algebra symbolically proven (press offset + drag mapping reproduce the authored position exactly, both faces) and the S4 test values re-derived independently. Five minors, all accepted and fixed: "paper" wording re-purged from the S4 comment and a Phase-2 model comment; **an M1 regression test added** (Back-face crease-straddler pressed off-centre — the old mixed-isometry offset fails it by 0.6 units); the redundant `elementFace` recomputation replaced by the provably-identical press `face`; `BeginPress` reports `Nothing` when `Place` fails; (S5 was a pre-existing comment, fixed alongside). Final: recompile clean, **235/235** EditMode tests pass.

## Deviations

1. **X-ray screenshots not taken.** Aaron was actively using the machine during verification; screen captures (which grab the whole desktop) were stopped after one accidentally caught his Discord (deleted unread beyond recognizing what it was, contents not used). The x-ray's mirror math is review-verified and its GUI path is a plain gated blit, but **no mechanical check saw the overlay drawn** — Aaron's hold-Tab playtest is the first real look at it, called out in the report.
2. Fold-pane live checks were driven through the ops API with synchronous scene renders instead of window repaints (the unfocused editor doesn't repaint IMGUI windows); semantics probed by RT pixels: bare desk shows the editor background, the through-fold-placed Wall shows at its mapped spot, cross-face move + single undo verified live.
