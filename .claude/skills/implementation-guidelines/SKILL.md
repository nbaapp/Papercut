---
name: implementation-guidelines
description: Aaron's guidelines for adding anything to Project Papercut. Invoke before creating, editing, or planning any code, prefab, scene, or asset — after project-context has loaded the docs. Covers when to ask vs. decide, standard of work, structure rules, and vocabulary.
---

# Project Papercut — implementation guidelines

Run this **after** `project-context` (which reads `Documents/Design Doc.md` and `Documents/Claude Bible.md`) and **before** writing any code. These are the rules for how work gets added to the project. The Bible is the source of truth; if this file and the Bible disagree, the Bible wins — and say so.

## 1. Ask before you build — but only when it matters

**Before implementing anything, if there is anything unclear about the design or the implementation, ask.** Do not make arbitrary choices that affect design.

The test for whether to ask:

- **Ask** when different reasonable answers would produce materially different gameplay, structure, data models, or player-facing behavior — and nothing in the docs settles it. Anything touching a `[DECIDE]` item is in this category by default.
- **Don't ask** when there is an obvious right answer, when the docs already answer it, or when it is a routine engineering call (naming a private field, file layout inside an established folder, which of two equivalent Unity APIs to call). Make those calls yourself and move on.
- **Batch questions.** Do all the work that doesn't depend on the answer, then ask everything that does in one message, with a recommended answer for each where you have one.
- If you must proceed under an assumption, implement the **narrowest version that leaves every option open**, and state the assumption explicitly in your reply.

Never present a question with one obviously correct option — that's noise, not diligence.

## 2. Tag semantics (from the Bible §0)

- `[LOCKED]` — settled. Do not change without an explicit instruction.
- `[DECIDE]` — open. **Never resolve by picking a reasonable default.** Ask (per §1 above), or build the narrowest-open version and say which you did.
- `[TENTATIVE]` — may be cut. Do not build unless asked. If one is needed to exercise another system, build it to full standard, keep it isolated, and say why it exists.
- Not covered anywhere → a **gap, not permission**. Flag it.

Check the task against the **Open decisions register (Bible §11)** before starting.

## 3. Standard of work

- **Scope is provisional. Quality is not.** There is no throwaway phase; the prototype becomes the game. No disposable implementations, no known-broken paths, no deferring correctness because "it's just the prototype."
- **Robust ≠ pre-generalized.** Correct, readable, tested where it matters, no silent failure, no hidden coupling. Do not build abstraction layers, plugin systems, or config surfaces for features that don't exist yet. Prefer the simple correct implementation; flexibility comes from clean seams, not extra layers.
- If quality and deadline conflict, **propose reducing scope** rather than lowering the standard.

## 4. Structure rules

- **Features are data and components, not branches inside core systems.** Cutting a feature must mean deleting a component/prefab, never unpicking it from fold, occlusion, or collision code.
- An ability is a **flag consulted by an element**, not a code branch. An element is a **component**, not a subclass of a bespoke hierarchy.
- Anything that might be replaced later (e.g. fold rendering) goes **behind an interface** separating *what it does* from *how it's done* — then build the current version properly.
- **Fold data model holds a list of folds** even though only one is exposed at a time.
- **Fold rendering** approach is never picked silently — present the tradeoff first.
- **Occlusion notifies covered objects** and lets them respond; never blanket-disable everything under a flap.
- **The player is always on the base, never on the flap.** Never build fold-carries-player.
- **Not tile-based.** No grids, tilemaps, or cell-based pathing. Coverage is a geometry test against a polygon.
- Avoid anything WebGL-hostile (threading, heavy render-texture readbacks) without flagging it.
- Respect the **non-goals (Bible §10)**: no combat, timers, procgen, multiplayer, inventory beyond flags/counts, or dialogue system beyond text boxes.

## 4a. Tunables live in the Inspector

**Any value that changes how the game feels or plays must be editable in the Unity Inspector — never a hard-coded literal.** This includes, but is not limited to: movement speed and acceleration, camera follow/zoom/offset/smoothing, fold/flip animation durations and curves, spawn rates and counts, input thresholds and dead zones, interaction radii, timings, colors and sizes that are design choices rather than implementation details.

- Expose them as `[SerializeField] private` fields on the owning `MonoBehaviour`, with `[Tooltip]` explaining what the value does and `[Min]`/`[Range]` where a bound is obvious. Group them under a `[Header]`.
- Give every tunable a sensible default in the field initializer so a freshly added component works out of the box.
- If a set of tunables is shared by many objects or is meaningfully "a preset" (e.g. a camera profile), put it on a `ScriptableObject` instead of duplicating fields — but only when there is more than one consumer today. Do not pre-build config surfaces for features that don't exist (see §3).
- Reading a tunable at runtime must be live: no caching into a local at `Awake` in a way that makes Inspector edits during Play Mode stop working, unless there is a real reason (state it).
- **Numbers that are not tunables** (array indices, unit conversions, epsilon comparisons, layer masks derived from names) stay as named `const`s in code. The test: *would Aaron plausibly want to tweak this to change the feel or design?* If yes → Inspector. If it's an engineering constant → `const`.
- When you add or rename a tunable, list it in your reply so Aaron knows what is now adjustable.

## 5. Vocabulary

Use *Sheet, Screen, Front/Back, Flap, Crease, Base, Desk, Flip* consistently in code, comments, and file names. Avoid "page," "paper," "flipside," and "tile."

## 6. The coding workflow is Aaron's call

For every development task, ask Aaron whether to use the full **`coding-workflow`** skill before writing any code — do not decide this yourself in either direction. State briefly what the task involves and recommend full workflow or lightweight; wait for his answer. If yes, invoke the skill and follow it: pre-plan questions → detailed plan → (new questions) → isolated adversarial plan review → (new questions) → code → isolated code review → fix → report. If no, work lightweight — every other rule in this document still applies.

## 7. Before you report done

- Every `[DECIDE]` item the work touched is either asked about or explicitly called out with the narrowest-open choice made.
- Every assumption you made is listed in the reply.
- Nothing `[TENTATIVE]` was built without a go-ahead.
- Names use the glossary.
