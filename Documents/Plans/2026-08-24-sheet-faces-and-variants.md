# Plan — Sheet Front/Back faces and per-sheet prefab variants

Date: 2026-08-24

## 1. Task

Aaron's request (verbatim): *"Lets work on the individual screens thing. Each screen should have two sides, containing arbitrary things on them, and I should be able to build and create them separately somewhere else, before they get placed in the map."*

Deliverable:

- `Sheet.prefab` gains two authoring roots, **Front** and **Back**, each a plain container for arbitrary children (exits, later terrain/props). The paper quad moves under each root as a `Surface` child so the roots themselves stay at identity scale.
- Each authored sheet becomes a **prefab variant** of `Sheet.prefab` in `Assets/Papercut/Sheets/`, built in prefab mode in isolation, and placed on the Desk by dropping the variant under the Desk object and setting its grid position.
- The four sheets currently in `Desk.unity` are converted into variants (their `Exit`s move under `Front`), and the scene references the variants.
- `Sheet.cs` exposes the two roots and records the Back-space coordinate convention Aaron chose (below).

**Out of scope:** folding, occlusion, any rendering of the Back at runtime, back-side exits behaviour, the level editor, art. Nothing `[TENTATIVE]` is built.

## 2. Design references

- Design Doc — *Map* ("Each piece of paper acts as a screen", "backside of papers is generally only used for the solving of puzzles on its own screen").
- Bible §2 Glossary — *Sheet, Screen, Front/Back, Desk*. The per-sheet asset is a **Sheet** (the folder is `Sheets/`), because "Screen" means *the sheet the player is on*; Aaron said "screens" in the request and that's fine in prose.
- Bible §3 — not tile-based `[LOCKED]`; sheet dimensions; world scale (#9) — `SheetGeometry` already fixes 1 unit = 1 inch; unchanged.
- Bible §6 Backside model — **`[DECIDE]` #5 authoring convention: answered by Aaron in this task** (see §3 below). The `[TENTATIVE]` flip mechanic is not touched. Back-side exits lead to the neighbour's front `[LOCKED]` — no code for that yet; Back is inactive at runtime so no exit on it can fire.
- Bible §4 Fold model — untouched. The Back root is the seam the fold/occlusion systems will consume; nothing about folds is assumed beyond "a fold will need to know where the Back content lives".
- Bible §7 — Desk grid discovery from children — unchanged; variants are still `Sheet` children of the Desk.
- Bible §9 — no puzzle element is added.
- Bible §11 — this task touches #5 (answered). It does not touch #1/#2/#6.

## 3. Decisions already made by Aaron

1. **Back-side authoring convention (Bible #5):** Back content is authored in the sheet's own Back-space, flat, as if the sheet were physically flipped over. Any mirroring across a crease is a runtime job for the fold system. Aaron: *"I think mirrored? I will want to build a level editor later, which will allow me to view both sides flat, as well as make a fold on the paper and add elements to the backside while its folded over ... whatever fits best with that vision."*
2. **Which axis the flip is about:** Option **B** — flipped about the vertical axis (like turning a page). Back-space point `(x, y)` lies directly beneath Front-space point `(−x, y)`. Aaron: *"B i guess."*

Everything else in this plan is an engineering call and is listed under §9.

## 4. Files

| File | Action | Purpose |
|---|---|---|
| `Assets/Papercut/Scripts/Desk/Sheet.cs` | modify | Serialized `front`/`back` roots, public accessors, Back deactivated at runtime, validation, gizmo unchanged. |
| `Assets/Papercut/Scripts/Desk/SheetGeometry.cs` | modify | Add the Back↔Front coordinate mapping (`BackToFront`, `FrontToBack`) as the executable statement of decision 3.2. |
| `Assets/Papercut/Tests/EditMode/SheetGeometryTests.cs` | create | Tests for the mapping (involution, mirrors x only, edge points map to opposite edge). |
| `Assets/Papercut/Prefabs/Sheet.prefab` | modify | New hierarchy: `Sheet / Front / Surface` and `Sheet / Back / Surface`. `Sheet` component references `Front` and `Back`. |
| `Assets/Papercut/Sheets/Sheet (0,0).prefab` (+ `.meta`) | create | Variant of `Sheet.prefab`; Exits East + North under `Front`. |
| `Assets/Papercut/Sheets/Sheet (1,0).prefab` (+ `.meta`) | create | Variant; Exits West + North. |
| `Assets/Papercut/Sheets/Sheet (0,1).prefab` (+ `.meta`) | create | Variant; Exits East + South. |
| `Assets/Papercut/Sheets/Sheet (1,1).prefab` (+ `.meta`) | create | Variant; Exits West + South. |
| `Assets/Papercut/Sheets.meta` | create | Folder meta. |
| `Assets/Scenes/Desk.unity` | modify | The four sheet instances now instantiate the variants; the scene-level `Exit` objects are removed (they live in the variants). `gridPosition` and name stay as scene overrides. |
| `Documents/Claude Bible.md` | modify | §6 and §11 #5: mark the authoring convention as decided, with the exact wording from §3 above. |

## 5. Components & data

### `Sheet` (modified)

- Responsibility unchanged: one physical piece of paper; grid placement; player-enter/leave events. Now also **owns the two face roots**.
- New serialized fields (references, not tunables):
  - `[SerializeField] Transform front;` — "Root for everything authored on the Front face. Local space is the sheet's space."
  - `[SerializeField] Transform back;` — "Root for everything authored on the Back face, in Back-space: point (x, y) here lies beneath Front point (−x, y) — the sheet turned over about its vertical edge. Inactive at runtime until the fold system shows it."
- New public surface:
  - `public Transform Front { get; }`
  - `public Transform Back { get; }`
- **No new Inspector tunables.** There is no feel/design value here; z-offset of the Back surface is an engineering constant baked into the prefab (see §6).
- `OnValidate`: existing grid-snap behaviour kept; additionally logs an error if `front`/`back` is null or is not a descendant of this sheet.

### `SheetGeometry` (modified)

- `public static Vector2 BackToFront(Vector2 backLocal) => new(-backLocal.x, backLocal.y);` — its own inverse, so it also maps Front→Back (documented; no second name, per review N8).
- XML doc states decision 3.2 verbatim so the convention has exactly one home.

### Prefab `Sheet.prefab` (modified)

```
Sheet            (Sheet, SheetBoundary)          local: identity
├─ Front         (Transform only)                local: identity
│  └─ Surface    (MeshFilter quad, MeshRenderer Sheet.mat)   scale 11 × 8.5, z = 0
└─ Back          (Transform only)                local: identity
   └─ Surface    (MeshFilter quad, MeshRenderer Sheet.mat)   scale 11 × 8.5, z = +0.01
```

- Camera sits at z = −10 looking +z; Desk surface is at z = 1. Back `Surface` at z = +0.01 sits behind Front and in front of the Desk, so in the editor the Back is hidden behind the Front unless Front is hidden via Scene-visibility (the hierarchy eye icon), and never z-fights.
- Both roots active in the prefab so both faces are authorable in prefab mode.
- Existing fileIDs `1000–1003` (root, transform, Sheet, SheetBoundary) and `2000–2001` (Front object/transform) are kept; Front loses its mesh components (they move to a new `Surface` child) and its scale returns to 1.

### Variant fileIDs (from review B1)

A variant asset is a `PrefabInstance` (fileID `V`) whose `m_SourcePrefab` is `Sheet.prefab`, plus *stripped* objects for every base object the variant or a scene needs to address. Unity derives a stripped object's fileID as `(baseFileID ^ V) & 0x7FFFFFFFFFFFFFFF`. So:

- Inside a variant, `m_AddedGameObjects[].targetCorrespondingSourceObject` targets `{fileID: 2001, guid: <Sheet.prefab>}` (the base), and the added Exit transforms' `m_Father` is the variant's stripped Front transform `{fileID: 2001 ^ V}`.
- In `Desk.unity`, each sheet instance's `m_Modifications` target `{fileID: 1000 ^ V, guid: <variant>}` (name), `{fileID: 1001 ^ V}` (transform) and `{fileID: 1002 ^ V}` (Sheet.gridPosition). Targeting the base IDs would orphan the overrides and collapse every sheet onto (0,0).
- The generator computes these IDs and the YAML check (§8) resolves every cross-file `{fileID, guid}` against the referenced asset, including derived IDs.

### Variants `Sheets/Sheet (x,y).prefab` (new)

- Prefab variants whose `m_SourcePrefab` is `Sheet.prefab`. Each contains only: added `Exit` GameObjects parented to the variant's `Front` transform, with the same collider offsets/sizes and `direction` values as the current scene objects.
- `gridPosition` is **not** overridden in the variant (stays `0,0`); placement is a scene-instance override. The variant is content; the scene is the map.
- Named `Sheet (x,y)` for now purely because that's what they are today; renaming to content names later is a plain rename.

### Scene `Desk.unity` (modified)

- Four `PrefabInstance` blocks now point at the variant GUIDs. Their modifications: name, `gridPosition`, local position/rotation (as today). `m_AddedGameObjects` is emptied and the scene-level Exit objects (fileIDs 5000–5313) are deleted.
- Camera, Desk, Surface, Player unchanged.

## 6. Behaviour

Runtime:

1. `Sheet.Awake`: as today (resolve Desk). Then, if `back` is null → `Debug.LogError` ("Sheet '{name}' has no Back root assigned") and continue; else `back.gameObject.SetActive(false)`. If `front` is null → error. Nothing else changes: `Desk.LayoutSheets` positions the sheet root; `SheetBoundary` walls are on the root; `Exit`s under `Front` find their `Sheet` via `GetComponentInParent` exactly as before.
2. `Exit.Awake/Start` unchanged and still valid: `GetComponentInParent<Sheet>()` walks Exit → Front → Sheet.
3. Player movement, transitions, camera: unchanged. Verified by playing the Desk scene: all four sheets, all eight exits, transitions in both directions.

Editor:

4. Authoring a new sheet: *Create → Prefab Variant* from `Sheet.prefab` into `Sheets/`, open it, put things under `Front` and `Back`. To see the Back flat, hide `Front` with the hierarchy eye icon. (The future level editor replaces this workflow; nothing here constrains it.)
5. Placing a sheet on the map: drag the variant under `Desk`, set `gridPosition`; `Sheet.OnValidate`/`Desk.OnValidate` snap it into place as today.

Failure modes:

- Missing `front`/`back` reference → error logged in `OnValidate` (edit time) and `Awake` (runtime); the sheet still functions as a bare sheet. No silent failure.
- `front`/`back` assigned but not a descendant of the sheet → `OnValidate` error.
- A variant placed outside a Desk → existing "not a child of a Desk" error.

## 7. Interfaces & seams

- **Front/Back roots are the seam** between "what is authored on a face" and "what the fold/occlusion systems do with it". Later systems ask `sheet.Front` / `sheet.Back` and iterate children; nothing about them is baked into `Sheet`.
- **`SheetGeometry.BackToFront`** is the single home of the authoring convention. The fold renderer/collision composes it with the crease reflection; if the convention were ever changed it changes in one function plus the tests.
- **Cutting/undoing:** deleting the `Back` root from the prefab and the two accessors reverts to today's model. Variants are ordinary Unity prefab variants — no custom tooling.
- No interface types introduced: there is only one implementation and one consumer of each thing today (§0 "robust ≠ pre-generalized").

## 8. Testing

- **Asset-link check (first, per review N2):** a Python script resolves every `{fileID, guid}` pair in `Desk.unity` and the four variants against the referenced asset's real objects, including derived stripped IDs, and fails on any orphan.
- **CLI compile** of `Assets/Papercut/Scripts` with Unity's bundled Roslyn (memory `compile-check-without-editor`), plus the EditMode test assembly.
- **EditMode tests** (`SheetGeometryTests`): `BackToFront(FrontToBack(p)) == p`; `BackToFront((x,y)) == (−x,y)`; the east-edge midpoint of Back maps to the west-edge midpoint of Front; run via the reflection runner from memory, or in the editor Test Runner.
- **Existing tests** (`GridDirectionTests`, `ScreenNavigatorTests`) still pass.
- **YAML sanity:** each generated prefab/scene parses (Python check: every `--- !u!` header has a unique fileID; every local `{fileID: n}` refers to an ID defined in that file or is a prefab reference with a GUID).
- **Manual, Desk scene** (Aaron, or me if the editor is free): **first** — all four sheets show distinct grid positions and names in the Inspector and there are zero console errors on load; then Back roots inactive in Play Mode; hierarchy shows `Sheet (x,y) / Front / Exit …`; walk through all eight exits; open a variant in prefab mode and confirm Front/Back authorable; hide Front → Back surface visible.

## 9. Assumptions (engineering)

1. **Back inactive at runtime**, done by `Sheet.Awake`. The fold system will own Back visibility when it exists. Unity gives no ordering guarantee between `Sheet.Awake` and the `Awake` of components already under Back, so Back components may `Awake` once before being disabled (`Start`/`Update`/`OnTrigger*` will not run); Back content must therefore be safe to `Awake`, which `Exit` is. Not a tunable — a Back that "plays" without a fold is a bug, not a setting.
2. **Back `Surface` z = +0.01** — engineering constant in the prefab, not Inspector-exposed (it's ordering, not feel).
3. **Variants do not override `gridPosition`**; the scene instance does. Rationale in §5.
4. **Existing prefab fileIDs kept** so the four scene instances' overrides remain valid and the Player prefab is untouched.
5. **Assets are hand-authored YAML** via a Python generator (editor is open, so Unity APIs aren't usable from the CLI). GUIDs for the new variants and folder are freshly generated.
6. The Back surface uses the same `Sheet.mat` as the Front. Art is out of scope; a distinct Back material is a one-field change later.
7. `Front`/`Back` are `Transform` references (not `GameObject`) because callers will parent things under them and iterate children.
8. Aaron's "I think mirrored? … whatever fits best with that vision" is read as *authored in Back-space, mirrored at runtime* because that is the reading under which a flat two-sided editor view and a folded preview show the same data. The Bible records #5 as **decided provisionally** (revisit when the level editor exists) rather than locked.
9. Editor workflow caveat: Back content is authored at identity and therefore overlaps Front content in the Scene view; hiding Front via the eye icon is per-session. Accepted for now; the level editor is the real fix. No Back tint is added (art is out of scope).
10. Variant names `Sheet (x,y)` bake a map position into a content asset; temporary, noted in the asset name only, renaming is free.

## 10. Open questions

None. (Q1, the flip axis, was asked and answered before planning.)

## Review

Plan-reviewer round 1 (verdict BLOCK).

| # | Finding | Disposition |
|---|---|---|
| B1 | Scene overrides must target the variant's derived (stripped) fileIDs, not the base prefab's; YAML check was blind to it. | **Accepted.** Generator computes `(base ^ V) & 0x7FFF…` IDs; asset-link check added (§5, §8). Editor-side authoring (option b) rejected only because the editor is open and I can't drive it; the manual check in §8 is the backstop. |
| N1 | "Mirrored" alone doesn't pick a #5 reading; don't close #5 as locked. | **Accepted.** Reading recorded as assumption §9.8; Bible marks #5 decided provisionally. Q1 not re-asked — Aaron already answered A/B explicitly; the fold-preview consequence goes in the report. |
| N2 | Nothing tests the likely breakage. | **Accepted.** Asset-link check; manual check reordered. |
| N3 | "page" in tooltip. | **Accepted.** |
| N4 | Awake ordering makes "guarantees" too strong. | **Accepted.** Claim softened (§9.1), comment in code. |
| N5 | Stale remarks in `Sheet.cs`. | **Accepted.** |
| N6 | Eye-icon workflow is per-session; suggest Back tint. | **Accepted** as a documented caveat (§9.9); tint **rejected** — it's an art value, out of scope. |
| N7 | Variant names encode map position. | **Accepted** as a note (§9.10); rename rejected — nothing better to call placeholder sheets yet. |
| N8 | `FrontToBack` duplicates `BackToFront`. | **Accepted.** One function. |
| N9 | Duplicate `m_RootOrder` in existing scene YAML. | **Accepted.** Generator emits correct sibling order. |
| Q1 | Confirm/lock the Back convention? | Answered by Aaron before planning (B); recorded provisional per N1. Consequence surfaced in the report. |
| Q2 | Runtime way to see the Back? | Not asked: the request is about *building* sheets; runtime Back display is adjacent to the `[TENTATIVE]` Flip mechanic. Prefab mode only; mentioned in the report. |

No second round: the blocking fix is a change to asset generation and verification, not to the design.

## Code review

Code-reviewer round 1 (verdict APPROVE WITH FIXES; no must-fix findings).

| # | Finding | Disposition |
|---|---|---|
| S1 | `SheetGeometryTests.cs` had no `.meta`. | **Accepted.** Added with a fixed GUID. |
| S2 | `BackToFront_KeepsPointsInsideSheet` couldn't fail for any mirror or the identity. | **Accepted.** Replaced with exact-value cases for all four corners and an asymmetric interior point. |
| S3 | The asset-link checker lived only in the session scratchpad. | **Accepted.** Checked in as `Tools/check_links.py`; memory note updated. The one-off generator stays out of the repo. |
| S4 | Missing-root error logs twice in the editor (OnValidate + Awake). | **Accepted as-is** — by plan §6; two errors for a broken prefab is not a problem worth an `#if`. |
| Q1 | Back-side Exits don't run `Start` validation until a fold activates the Back. | Engineering call: **accept (a)**. Back-side exit behaviour is `[LOCKED]` in intent but unbuilt; an edit-time walk of Back children now would be speculative. The fold system owns Back activation and validation. |

## Deviations

- The scene needs a stripped Transform block per sheet instance (the Desk transform lists it in `m_Children`); the plan implied removing them. They are regenerated, retargeted to the variant's derived root-transform ID.
- `Tools/check_links.py` added to the repo (review S3).
