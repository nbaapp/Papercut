# **Project Papercut — Claude Bible**

**Companion to the design doc. Not a replacement for it.** The design doc defines *what the game is*. This doc defines *how it is modeled in code*, and — more importantly — marks everything that is **not yet decided**.

---

## **0\. How to use this document**

* The design doc is the authority on intent. This doc is the authority on structure and vocabulary.  
* Items marked **\[DECIDE\]** are open. **Do not resolve them by picking a reasonable default.** Ask, or implement the narrowest version that leaves both options open, and say which you did.  
* Items marked **\[TENTATIVE\]** are features that may be cut. Do not build them unless asked.  
* Items marked **\[LOCKED\]** are settled; changing them requires an explicit instruction.  
* When something isn't covered here at all, that's a gap, not permission. Flag it.

### **Standard of work**

**Scope is provisional. Quality is not.**

This project has phases, but it does not have a throwaway phase. Everything built — including work done during Checkpoint 1 to answer open questions — is shipping code until proven otherwise, and should be written to be lived with. Do not write deliberately disposable implementations, do not leave known-broken paths, and do not defer correctness on the grounds that it's "just the prototype." In practice, the prototype *becomes* the game; treat it that way from the first commit.

The uncertainty in this document is about **which features exist**, not about how well they should be built. Handle that uncertainty structurally, not sloppily:

* Systems get clear boundaries and stable interfaces, so that cutting a feature means deleting a component, not unpicking it from three other systems.  
* Features are data and components, not branches inside core systems.  
* If something might be replaced later (see §4 rendering), put it behind an interface so the swap is contained — then build the current version properly.

**Robustness is not speculative generality.** Do not build abstraction layers, plugin systems, or configuration surfaces for features that don't exist yet. Robust here means: correct, readable, tested where it matters, no silent failure, no hidden coupling. It does not mean pre-generalized. When a choice is between the simple correct implementation and a flexible one, take the simple correct one — flexibility comes from clean seams, not from extra layers.

Timeline context, for prioritisation only and never as a licence to cut corners: 3–4 months total, mechanics prototype at \~5 weeks. If quality and deadline genuinely conflict, reduce scope and say so rather than lowering the standard.

---

## **1\. Tech**

* **Engine:** Unity 6.3 LTS **\[LOCKED\]**  
* **Primary target:** Windows desktop **\[LOCKED\]**  
* **Secondary target:** WebGL build for itch.io **\[TENTATIVE\]** — likely but not committed.  
  * Consequence if it ships: no threading, tight memory budget, expensive render-texture readbacks, long load times. Fold rendering (§4) is the system most likely to break this. Avoid anything WebGL-hostile without flagging it.  
* **Rendering pipeline:** **\[DECIDE\]** — URP vs Built-in. Depends on §4.  
* **2D vs 3D:** **\[DECIDE\]** — the game is top-down and flat, but folding is a physically 3D transformation, and the desk framing (§7) implies perspective. A 3D scene rendered near-orthographically may be less fighting than 2D sprites. Not settled.  
* **Input:** **\[DECIDED — provisional, 2026-08-26\]** Unity Input System (`InputSystem_Actions` asset: `Player` map for movement, `Fold` map for the mouse). Mouse + keyboard for now; Aaron: *"We don't need to bother with gamepad for now."*

---

## **2\. Glossary (use these terms consistently in code and comments)**

| Term | Meaning |
| ----- | ----- |
| **Sheet** | One physical piece of paper. The persistent world object. |
| **Screen** | The sheet the player currently occupies. One sheet \= one screen. |
| **Front / Back** | The two faces of a sheet. |
| **Flap** | The portion of a sheet that has been folded over. |
| **Crease** | The line a fold pivots on. Persists visually after unfolding. |
| **Base** | The un-folded remainder of the sheet — the part that doesn't move. |
| **Desk** | The world space containing the grid of sheets. |
| **Flip** | Viewing a sheet's back *without* folding (see §6) — distinct from folding. **\[TENTATIVE\]** |
| **Seam** | Where the landed Flap's edge meets the Front — the mirror image of the anchoring sheet edge(s). The crease is the Flap's other boundary. Where tape would go. |

Avoid "page," "paper," "flipside," and "tile" in code. They're fine in the design doc; here they cause drift.

---

## **3\. Spatial model**

**Not tile-based. \[LOCKED\]** Objects are placed at free positions and move continuously. Do not build a grid, tilemap, or cell-based pathing system.

* Sheet dimensions: 8.5 × 11, landscape (11 wide × 8.5 tall).  
* **\[DECIDE\]** World unit scale — is 1 unit \= 1 inch of paper, or is the sheet normalized (e.g. 11 × 8.5 units)? Pick one early and never mix.  
* Terrain (traversable / untraversable) — **\[DECIDED — provisional, 2026-08-25\]**: placed objects with box colliders, over art drawn outside Unity. Aaron: *"drawing the maps in photoshop, then doing the collision/terrain in Unity for the fine tuning ... I don't know that I need the collision to be more than boxes."* Ground is the sheet surface itself (no component). A region is a `TerrainRegion` with a `BoxCollider2D`; it is a wall, or it is crossable only with a player `Ability` (water needs Swim) — kinds are prefabs differing in data, not code. **Cost of this choice, deliberately deferred to the occlusion task:** a box partially under a flap must either be clipped at runtime (box → polygon) or handled by a per-region coverage rule; that is the "how folding clips terrain" fork and it feeds §5. There are no authored exit boxes any more (see §7); a region at a sheet edge simply means there is no room to arrive there. **Revised 2026-09-10** (Aaron, offered convex-only / any-polygon / art-derived collision: *"Yeah, can you implement 3?"* — any polygon; and *"Both shapes, separate prefabs"*): a region's authored collider is a `BoxCollider2D` **or** a `PolygonCollider2D` holding **one simple outline** of any shape — concave allowed, no holes (Aaron: single outline only; an island is a separate region). A polygon is split into convex pieces at load (`PolygonDecomposition`: ear clipping + convex merge) and every consumer of a footprint sees pieces (`FaceFootprint`), so folding clips a polygon exactly as it clips a box. `Wall (Polygon)` / `Water (Polygon)` prefabs sit beside the box ones; existing box regions are untouched. The Sheet Studio draws a polygon point by point and edits vertices (drag; drag an edge midpoint to insert; right-click a vertex to remove); an edit that leaves the outline self-crossing reverts on release (Aaron), and vertices magnet to other regions' vertices and the sheet edge (Aaron).  
* **Props \[DECIDED — 2026-09-10\]:** static scenery (the Tree) is art only — no collider of its own. Aaron: *"I would rather separate out the collision from those objects, and manually put it over them. I think for objects like trees its just kinda clunky with them individually having collision."* Its collision is authored by laying Wall regions over the drawing in the Studio. A prop lives in `Prefabs/Props`; the Studio picks, outlines and fold-previews it by its sprite's rect (`StudioPlacement.IsProp`).  
* **Collision view \[2026-09-14\]:** static terrain is invisible as shipped (the map art is the drawing), which is no good for testing a sheet before its art exists. Aaron: *"give an option per page in the editor to make the collision visible or not … in solid colors, so its very clear."* Each sheet has `Sheet.showCollision` (the Studio toolbar's **Collision** toggle, saved with the variant); while it is on, `TerrainFill` on the Wall/Water prefabs draws the region's collider as a solid fill (colour per prefab) as face content on the region's own layer, so it folds, clips and mirrors like everything else and the fold and occlusion systems never know. Shown in the game and in the Studio panes alike. Cutting it is removing the component from the prefabs; the Gate keeps its own sprite.  
* Movement is continuous, but **\[DECIDE\]** whether it's 8-directional, fully analog, or grid-snapped-feeling-but-free.

**Consequence worth knowing:** with free placement, "is this thing covered by the fold?" becomes a geometry test against a polygon rather than a cell lookup. Everything downstream (blocks, terrain, exits, the player) inherits that.

---

## **4\. Fold model**

### **Geometry \[LOCKED unless noted\]**

* A fold is defined by a **crease line** plus which side of it becomes the flap.  
* Two legal crease families:  
  * **Edge fold** — crease parallel to a sheet edge, at any distance in from it.  
  * **Corner fold** — crease at exactly 45° to the edges, starting from a corner.  
* **\[DECIDE\]** Is fold depth continuous, or snapped to increments? Continuous is truer to paper; snapping serves pillar 3 ("the challenge is the puzzle, not reading the world"). Untested. *Kept open in code:* the model is continuous and `FoldDragInput.depthSnap` (Inspector, 0 = continuous) snaps the drag — play both.  
* ~~Flaps may extend past the sheet's boundary. A corner flap can overhang the desk. That's legal and should render.~~ **Reversed by Aaron, 2026-08-26:** *"I don't actually want to allow extension past the edge on folds. With the shape of the paper and size of the screen, I think there's just not enough room to make that interesting. Also opens some weird edge cases that I don't want to answer."* The landed Flap must lie on the sheet: `FoldGeometry.MaxDepth` (edge folds: half the extent; corner folds: the sheet height); input clamps, `SheetFolds.TryCommit` rejects. The Design Doc's "Folds extending beyond the sides of the screen" under *Valid Folds* is superseded. The geometry code stays general.

### **Validity**

* **Invalid:** any fold whose flap would cover the player, wholly or partially. **\[LOCKED\]**  
  * Structural consequence: **the player is always on the base, never on the flap.** The player can never be transported or flipped by a fold. Do not build fold-carries-player.  
* **Valid:** folds that partially cover objects. A partially covered pushable can be pushed fully under, or pulled out and pushed back on top.  
* ~~**\[DECIDE\]** Multiple simultaneous folds per sheet — flagged in the design doc as needing playtest. **Build the data model to hold a list of folds, but implement and expose only one at a time until told otherwise.**~~ **\[DECIDED — provisional, playtest, 2026-08-26\]** Aaron: *"Can we try allowing multiple folds on one sheet?"* Exposed behind an Inspector toggle, `SheetFolds.allowMultipleFolds` (base Sheet prefab). The sheet is modelled as a stack of layers (`SheetLayers`): each fold lifts everything on the Flap side of its crease and lays it over, so rendering, collision and occlusion are N-fold from the start. Folds stay an ordered list.  
* ~~**\[DECIDE\]** Folding an already-folded sheet (fold-on-fold, flap folded again) — same as above, assume no.~~ **\[DECIDED — provisional, playtest, 2026-08-26\]** Aaron: *"I do in fact mean stacking is allowed - but also make it a toggle."* `SheetFolds.allowStacking`: on, a fold may land on another Flap and may cut through one (the Flap side of the crease is folded over however many layers deep — one toggle for both, Aaron: *"one toggle"*); off, every fold must be independent of every other (their lifted and landed regions never touch; a crossing drag tints red, `FoldRejection.OverlapsFold`). A fold with a later fold on or through it cannot be unfolded first (`CoveredByLaterFold`). Landing into a region an earlier fold emptied is legal; the landed edge over empty desk is a wall, not an exit.

### **Unfolding**

* **\[DECIDED — provisional, 2026-08-26\]** Unfold: click on/near **the Seam only** (Aaron, 2026-08-26: *"I was more imagining that I would click on the seam where the front of the paper when it is unfolded meets the back … where you would put the tape"*; and later that day, when the crease was needed as a grab: *"just remove the unfold trigger there and replace it, since we can already unfold at the inner edge of the fold where the tape goes which is the intent"*; a tape visual may come later); free; animated swing-back with an Inspector duration (`SheetFolds.unfoldDuration`, 0 = instant). A press on a **crease** instead grabs that fold line to fold it further (`FoldDragInput.creaseGrabDistance`). **Refused while the player stands on the landed flap** (Aaron: *"You cant unfold while the player is on the flap, it fails."*). Creases left by undone folds are drawn faintly and clear with the fold reset when the player leaves (Aaron: *"creases clear on reset."*).  
* ~~**\[DECIDE\]** What happens to an object resting on the flap when it unfolds — does it ride the flap back to its original side, or stay in place? The "push a block across a fold, unfold, now it's on the other side" toolkit entry implies **objects ride the flap**. Confirm, because the alternative is also coherent and the puzzles differ enormously.~~ **\[DECIDED — provisional, 2026-08-27\]** Aaron: *"Yes"* (objects ride the paper). A movable object (`PushableBlock`) is content of the sheet like terrain — a rect on the flat sheet plus a side — so the layers carry it; its rect and face never change because of a fold or unfold, so on unfold it rides its piece of the sheet all the way around: a block pushed onto a Flap ends up on the **Back** at the mirrored place, hidden until that region is folded again (Aaron, later that day: *"It needs to ride the flap all the way around to the back when that unfold happens."*).  
* ~~**\[DECIDE\]** An object straddling the crease at unfold time. Real edge case with free placement; needs a rule.~~ **\[DECIDED — provisional, 2026-08-27\]** Aaron: an object across a **crease** is on one piece of paper and is fine (*"if its just on the crease, that should be fine"* — the part that comes around is shown and pushable; pushed across the crease it *"rolls around back to the back of the page"*); an object across a Flap's **Seam** is *"on 2 sides of the paper at once"* and the unfold is refused (`FoldRejection.ObjectOnEdge`; Aaron: *"you can't unfold if its on the edge"*). A fold is never refused because of an object.

### **Rendering \[DECIDED — provisional, 2026-08-26\]**

**Render-texture compositing**, chosen by Aaron from the three candidates (mesh deformation, stencil/shader masking, render-texture compositing): *"I think we can go with render-texture compositing. Its okay if itch hates me, I'm fine to make it a downloadable if necessary."* Lives in `RenderTextureFoldRenderer` behind `IFoldRenderer` (the seam between *what a fold does* and *how it is drawn*): a camera per face renders that face's content (on the `SheetFront`/`SheetBack` layers, which the main camera does not draw) into a texture; the visible sheet is a Base mesh from the Front texture plus a landed-Flap mesh from the Back texture through the fold's reflection, plus crease lines. No readbacks; the WebGL cost is one small camera pass per visible face. Crease lines are face content (drawn under Front and, mirrored, under Back — Aaron, 2026-08-26: *"the crease lines should instead be a part of the paper"*), so they fold with the sheet. Authored face content lives in local z (−0.1, 0]; creases sit at −0.1, in front of it. Wrinkle/crease art later is a shader on these textures.

### **Input \[DECIDED — provisional, 2026-08-26; the first scheme to prototype\]**

Press near an edge or corner of the Screen and drag inward; the grabbed edge/corner follows the cursor (edge depth = half the drag distance; the corner lands under the cursor); the fold previews live; release commits; right-click/Escape cancels. An invalid fold (would cover the player) tints red and release does nothing (Aaron: *"A for sure."*). With multiple folds on, a press tries the Seam (unfold) first, then a crease (grab that fold line, fold further), then the sheet's own edges and corners (new fold); with them off, a folded sheet only answers the Seam click. Lives in `FoldDragInput` on the Desk; every distance is an Inspector value.

---

## **5\. Occlusion & collision**

* A flap **covers** part of the base. Covered front content is hidden and non-interactive. **\[DECIDED — 2026-08-26\]** Covered terrain is gone for collision (Aaron: *"Yes, duh."*); a partially covered box region is **clipped at runtime** to its visible part (Aaron: *"a for certain."*). Mechanism: `SheetOcclusion` notifies every `IFoldOccludee` with what is left of it and where; `TerrainRegion` swaps its authored collider (box or polygon, see §3) for a clipped `PolygonCollider2D`. The clip is exact: each visible part is the footprint intersected with a layer of the folded sheet, placed where that layer lies, minus the layers above (`SheetLayers.Coverage`). Front content on a piece folded twice is Front-up again wherever it landed.  
* The flap's own surface is walkable — it presents the **back** of the sheet (see §6).  
* **Movable objects \[DECIDED — provisional, 2026-08-27\]:** a `PushableBlock` is clipped by folding exactly like terrain (`SheetPlacement`); a block partly under a landed Flap belongs under it (Aaron: *"if its covered at all, it belongs to the side that is covered by the flap"*) and can be pushed fully under; a block wholly clear of a Flap that is pushed into its edge climbs on top (*"if its pulled out, then it becomes part of the top, and if you push it over, it now goes on the top"*). Blocks stop at walls, gated terrain, sheet edges and other blocks (*"stop"*); they roll across creases (boundary walls on a crease carry `CreaseWall`). **Pressure is a fact about the sheet, not about visibility** (Aaron: *"the buttons and the stuff on them don't disappear, they are just innaccessable because they are covered … its still holding the button down under the page"*): a `PressurePlate` is pressed while a presser's centre is on it on the same face of the flat sheet (`PressureRules`).  
* **\[DECIDE\]** Layering rule: does the player walk *on* the flap, or is stepping onto the flap surface the same as any other traversal? Are there height/edge effects at the crease, or is the world perfectly flat for movement? *Narrowest-open in code:* the world is flat; stepping onto the landed flap is ordinary traversal; the crease is a wall on the lifted side only. (Overhangs were briefly walkable — Aaron, 2026-08-26: *"it should be walkable for now"* — and then removed altogether the same day; see §4. Only the Screen has live physics, which still holds.) With several folds a landed edge lying inside the sheet rect over empty desk is also a wall (`OutlineKind.Wall`).

---

## **6\. Backside model**

**The single most confusable part of the system. Get the convention written down before writing code.**

When a flap folds over, the surface now facing up is the **back of the flap region, reflected across the crease line**. It is not "the back of the sheet" in general, and it is not un-mirrored.

* **\[DECIDED — provisional, 2026-08-24\]** Authoring convention: back-side content is authored **in the sheet's own Back-space, flat**, as the sheet looks when physically turned over about its vertical edge — Back point (x, y) lies directly beneath Front point (−x, y). Any mirroring across a crease is done at runtime by the fold system. Chosen by Aaron so a future level editor can show both faces flat and preview a fold from the same authored data. Revisit when the level editor exists. Lives in code as `SheetGeometry.BackToFront`; each sheet's Back content is authored under its `Back` root (see `Sheet`). The Back root is never moved at runtime (since 2026-08-26, multiple folds: one root cannot be posed for two Flaps); exposed Back content is placed through its occludee's clipped collider.  
* **\[TENTATIVE\]** A separate **flip** mechanic — a button/spot that shows the whole sheet's back, and/or an unlockable that lets the player move to the back freely. These are *different mechanics from folding* and may or may not ship. Do not implement without an explicit go-ahead, and do not conflate them with fold-reveals-back.  
* Back-side exits lead to the **front** of the neighbouring sheet, never to another sheet's back. **\[LOCKED\]**

---

## **7\. Sheets, desk, and transitions**

* The world is a grid of sheets on a desk. Zoomed out, the whole grid is visible; zoomed in, the current sheet fills the view with neighbouring sheet edges peeking in.  
* Moving between screens **slides the sheets** so the new sheet is centred. The camera framing is the sheets moving, not the camera moving. **\[DECIDED — provisional, 2026-08-25\]** Literally true: the camera and the Desk (surface, later UI) never move; the Desk's `SheetGrid` child, holding every Sheet and the player, slides under the fixed camera (`DeskSlider`). Chosen by Aaron so a static background can sit outside the moving hierarchy.  
* Grid dimensions and layout: **\[DECIDE\]** — target is \~16 screens, 25 max, locked at Checkpoint 1\.  
* Desk margins hold UI.
* **Two Desk scenes \[2026-09-09\]:** `Assets/Scenes/Desk.unity` is the official world (the maps that may ship; starts empty) and `Assets/Scenes/Test Desk.unity` is the mechanics test bed (the hand-authored (0,0)–(1,1) sheets). Each Desk scene owns a **sheet set**: the sheet variants in `Assets/Papercut/Sheets/<scene name>/`. The Sheet Studio derives the set from the open Desk's scene and creates the folder with the first sheet. Both scenes share every prefab and tunable default, but Inspector values on the Desk objects (drag feel, slide, camera) are per scene from here on.
* **Exits are not authored \[DECIDED — 2026-08-26\].** Aaron: *"if the player leaves the paper, the move to the adjacent page if there is one (and there is room on the edge of the adjacent paper to make the jump), and it doesn't matter if the 'exit' was on the front or back. So an 'exit' in practicality is just any part of the traversable ground that reaches the edge."* In code: `SheetBoundary` rebuilds walls and `SheetEdge` triggers from `WalkableOutline` (the boundary of the union of the sheet's layers; a crease, or a landed edge over empty desk, is always a wall); `ScreenNavigator` travels if a neighbour exists and `TravelRules.HasRoom` finds no impassable terrain at the entry position. The `Exit` component is gone. Arrival is always the entry strip of the near edge.  
* **\[DECIDE\]** Is the zoomed-out map view a player-facing feature (a map screen), a transition state, or just how the scene happens to be built?

---

## **8\. State & persistence**

* **Folds do not persist.** A sheet unfolds when the player leaves and returns. **\[LOCKED\]**  
* **\[DECIDE\]** Do *object* positions persist? Pushed blocks, thrown switches, opened doors. The design doc doesn't say, and the answer determines whether a screen can be left in an unsolvable state. If folds reset but blocks don't, a block pushed onto a flap ends up somewhere on unfold — where? **Partly decided (provisional, 2026-08-27)** — Aaron: *"for now, leaving fully resets the page, moving the block back to where it was at the start of the screen. Gates shouldn't reset however (other effects of button presses might or might not reset, and that is determined by me.)"* Blocks reset on `Sheet.PlayerLeft`; plate effects are untouched by the reset (a Hold plate held by a block releases because the block left it — flagged). Other object kinds: still open.  
* **\[DECIDE\]** Is there still a reset button? If folds auto-reset, its purpose changes to resetting object state — which only matters if the answer above is "yes, they persist."  
* **\[DECIDE\]** Save system — none, checkpoint, or continuous. Not needed for the prototype.

---

## **9\. Puzzle elements**

**The entire contents of this section are \[TENTATIVE\]. Nothing here is committed.** *Built on Aaron's request (2026-08-27), as isolated playtest elements: pushable blocks (`PushableBlock`, `BlockPusher`), pressure plates — Hold and Latch as data (`PressurePlate`, pressed by any `IPlatePresser`), effects behind `PlateEffect` (one so far, `RemoveObjectEffect`), and a `Gate` wall prefab. Whether they ship is still the Checkpoint 1 call; cutting them is deleting those components and prefabs.* `Ability.Push` exists (Aaron: *"you can add the ability, ig, but I won't use it for now"*); blocks may require it (`requiresAbility`).

These are a brainstorm list, not a feature list. Which of them ship gets decided at Checkpoint 1, after prototyping. **Do not implement any of them as a shipping feature without being told to.** If one is needed to exercise another system, build it to the same standard as everything else but keep it isolated, and say why it exists. Cutting it later should mean deleting a component and its prefab — never unpicking it from the fold or occlusion systems.

Element ideas currently floated:

* Traversable / untraversable terrain  
* Pushable blocks and other pushable elements  
* Buttons (pressure)  
* Switches, instant effect (e.g. opens a door)  
* Switches, on/off toggle  
* Toggle-visible blocks (Mario on/off blocks)  
* A flip button/spot that shows the sheet's back  
* Front-only / back-only blocks  
* Elements that float up on top of the flap when covered by a fold  
* Water, traversable only with a swimming ability

Unlockable ideas, same status, same Checkpoint 1 deadline: swimming, push/pull (possibly granted early as a tutorial), free movement to the back of the sheet, rock breaking.

Collectables: optional, no mechanical effect, narrative flavour only. Same status.

**Structural implication, which does hold regardless of what gets picked:** build so that elements are *data-driven and additive* — an ability is a flag consulted by an element, not a code branch; an element is a component, not a subclass of a bespoke hierarchy. The list above will shrink and change, and the cost of that should be deleting prefabs, not refactoring systems.

One item worth calling out early because it constrains §5: **"floats up if covered"** means a covered object is not necessarily just hidden — some object types may need to relocate onto the flap. So the occlusion system should *notify* objects that they've been covered and let them respond, rather than blanket-disabling everything under the flap. That's cheap to build now and expensive to retrofit, even if the float-up element itself never ships.

---

## **10\. Non-goals**

Unless explicitly reversed:

* No combat, no enemies, no damage, no death, no fail state.  
* No real-time pressure, no timers.  
* No inventory beyond ability flags and collectable counts.  
* No procedural generation. Every screen is hand-authored.  
* No multiplayer, no online anything.  
* No dialogue system beyond simple text boxes (narrative is a December task).

---

## **11\. Open decisions register**

Consolidated. Roughly ordered by how much rework the wrong guess costs.

1. ~~Multiple folds per sheet — data model must allow it even if gameplay doesn't yet. (§4)~~ **Decided (provisional, playtest, 2026-08-26):** built, behind `allowMultipleFolds` / `allowStacking`; see §4. Whether either ships is Aaron's call after playing.  
2. ~~Fold rendering approach. (§4)~~ **Decided (provisional, 2026-08-26):** render-texture compositing behind `IFoldRenderer`; see §4.  
3. ~~Fold input scheme and preview/commit feel. (§4)~~ **Decided (provisional, 2026-08-26):** drag from edge/corner (or from an existing crease, to fold further) with live preview, red-refuse, click-Seam unfold; see §4. Feel values are Inspector tunables; playtest.  
4. 2D vs 3D scene setup \+ render pipeline. (§1)  
5. ~~Back-side authoring convention (mirrored at runtime vs pre-mirrored). (§6)~~ **Decided (provisional, 2026-08-24):** authored in Back-space, mirrored at runtime; see §6.  
6. ~~Terrain representation. (§3)~~ **Decided (provisional, 2026-08-25):** placed box-collider regions over hand-drawn art; partial-coverage-by-flap deferred to occlusion; see §3. **Revised 2026-09-10:** box or single-outline polygon regions; see §3.  
7. ~~Do objects ride the flap on unfold? (§4)~~ **Decided (provisional, 2026-08-27):** yes; see §4.  
8. Object-state persistence across screen exits. (§8) — blocks reset on leaving, effects do not (2026-08-27); other kinds open.  
9. World unit scale. (§3)  
10. Continuous vs snapped fold depth. (§4) — open; `FoldDragInput.depthSnap` lets both be played.  
11. ~~Straddling-the-crease behaviour on unfold. (§4)~~ **Decided (provisional, 2026-08-27):** crease fine, Seam refuses; see §4.  
12. Movement scheme (analog / 8-way). (§3)  
13. Which puzzle elements and unlockables actually ship — Checkpoint 1\. (§9)  
14. Grid dimensions and screen count. (§7)  
15. Whether the zoomed-out view is a feature. (§7)  
16. Reset button — purpose and scope. (§8)  
17. Save system. (§8)  
18. ~~Arrival after leaving from an overhang's far edge (§7).~~ Moot since 2026-08-26: folds no longer overhang (§4).

---

## **12\. Checkpoint 1 scope (\~late September)**

This is a scope boundary, not a quality boundary — see §0. Everything below is real code. The purpose of the milestone is to answer the open questions above by playing rather than by deciding on paper. Proposed, not prescribed:

* One sheet, front and back, hand-authored.  
* Free player movement with collision.  
* One fold: edge and corner, with whatever input scheme is cheapest to try.  
* Unfold.  
* Terrain occlusion by flap; back-of-flap surface walkable.  
* One movable object that survives fold and unfold, exercising the fold/object interaction. Whether pushable blocks ship as a feature is still open (§9); the implementation is not provisional.  
* A second sheet, to prove screen transition and the sheet-slide.

Explicitly out of prototype scope: art, narrative, unlockables, collectables, UI, audio, save.

