---
name: project-context
description: Read the Project Papercut design doc and Claude Bible before writing, editing, or planning any code. Use at the start of any task that touches Assets/, scenes, prefabs, scripts, or project settings, and whenever a question of intent, vocabulary, or scope comes up.
---

# Project Papercut — load project context

Before writing or planning any code for this project, read both reference docs in full:

1. `Documents/Design Doc.md` — the authority on **what the game is** (intent, pillars, mechanics, checkpoints).
2. `Documents/Claude Bible.md` — the authority on **how it is modeled in code** (vocabulary, structure, and what is and isn't decided).

Read them every time; do not rely on a summary or on memory from an earlier session. They are short and they change.

## Working rules distilled from the Bible

These are reminders of the most important rules, not a substitute for reading the docs.

- **Tag semantics:**
  - `[LOCKED]` — settled. Do not change without an explicit instruction.
  - `[DECIDE]` — open. **Never resolve by picking a reasonable default.** Either ask, or implement the narrowest version that leaves every option open — and say which you did.
  - `[TENTATIVE]` — may be cut. Do not build unless asked.
  - Something not covered at all is a **gap, not permission**. Flag it.
- **Scope is provisional; quality is not.** There is no throwaway phase. No deliberately disposable code, no known-broken paths, no "it's just the prototype."
- **Robust ≠ pre-generalized.** Clean seams and stable interfaces, yes. Abstraction layers, plugin systems, or config surfaces for features that don't exist yet, no.
- **Features are data and components, not branches inside core systems.** Cutting a feature should mean deleting a component/prefab, never unpicking it from the fold or occlusion systems.
- **Vocabulary:** use *Sheet, Screen, Front/Back, Flap, Crease, Base, Desk, Flip* consistently in code and comments. Avoid "page," "paper," "flipside," and "tile."
- **Spatial model is not tile-based** — no grids, tilemaps, or cell-based pathing.
- **The player is always on the base, never on the flap.** Never build fold-carries-player.
- **Fold data model must hold a list of folds** even though only one is exposed at a time.
- **Fold rendering** goes behind an interface separating *how a fold is rendered* from *what a fold does*; never pick the rendering approach silently.
- **Occlusion notifies covered objects** and lets them respond, rather than blanket-disabling them.
- Respect §10 non-goals (no combat, timers, procgen, multiplayer, etc.).

## Before starting work

After reading the docs, check the task against the **Open decisions register (§11)**. If the task depends on an unresolved `[DECIDE]` item, say so up front and either ask or state the narrowest-open implementation you intend to use. If quality and deadline conflict, propose reducing scope rather than lowering the standard.
