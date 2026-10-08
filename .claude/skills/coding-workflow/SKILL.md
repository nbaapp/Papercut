---
name: coding-workflow
description: The plan → adversarial plan review → code → code review pipeline for Project Papercut. Whether to use it is Aaron's call, never Claude's — for every development task, ask Aaron first (with a recommendation) and invoke this only if he says yes. Invoke after project-context and implementation-guidelines, before writing a plan or any code. Defines when to stop and ask Aaron, the plan format, and how to dispatch the isolated plan-reviewer and code-reviewer agents.
---

# Project Papercut — coding workflow

Run this **after** `project-context` and `implementation-guidelines`. **Whether a task uses this workflow is Aaron's decision, not yours.** For every development task, ask Aaron up front whether to run the full workflow — describe the task's apparent size in a sentence and give a recommendation (full workflow vs. lightweight), then wait for his answer. In the same message, if you think the task is one of the rare ones that needs Play Mode verification, say so with the reason and ask for that separately (see *Play Mode is opt-in*); otherwise don't mention it. Never invoke this skill unprompted, and never skip it on your own judgment either. If Aaron declines it for a task, all rules from `project-context` and `implementation-guidelines` (design questions go to Aaron, Inspector tunables, honest verification) still apply.

The goal of the pipeline: **Aaron makes every design decision; an unbiased second pair of eyes checks both the plan and the code.** Asking is *optional* at each gate — only ask when there is a genuine question per `implementation-guidelines` §1. The plan and both reviews are *not* optional.

```
0. Understand task, read relevant code
1. Ask Aaron any known design/implementation questions   (only if genuine)  -- STOP for answers
2. Write the detailed plan                                (always)
3. Ask Aaron any NEW questions the plan surfaced           (only if genuine)  -- STOP for answers
4. Isolated adversarial plan review (plan-reviewer agent)  (always)
5. Ask Aaron any NEW questions the review surfaced         (only if genuine)  -- STOP for answers
6. Write the code                                          (always)
7. Isolated code review (code-reviewer agent)              (always)
8. Fix confirmed findings, re-review if the fix is non-trivial
9. Report
```

"STOP for answers" means end your turn with the questions and do nothing further that depends on them. Do everything that *doesn't* depend on them first. Never advance past a stop by assuming an answer to a design question. If you must proceed under an engineering assumption, say so explicitly.

## Step 1 — Pre-plan questions

Before planning, list everything about the task that is unclear and would materially change the result: `[DECIDE]` items it touches, gaps in the docs, ambiguities in the request. Batch them into one message with a recommended answer each where you have one. If there is nothing genuine to ask, say "No pre-plan questions" and move on.

## Step 2 — The plan

Write the plan to `Documents/Plans/<yyyy-mm-dd>-<short-slug>.md` (create the folder if needed) **and** summarize it in your reply. The file is what the reviewer reads, so it must be self-contained. Required sections:

1. **Task** — what was asked, in one or two sentences, and what is explicitly out of scope.
2. **Design references** — the Design Doc / Bible sections this depends on, every `[LOCKED]`/`[DECIDE]`/`[TENTATIVE]` tag involved, and how each `[DECIDE]` is being handled (answered by Aaron in step 1, or narrowest-open — say which).
3. **Decisions already made by Aaron** — answers from step 1 (or earlier), verbatim enough that the reviewer can't mistake them for your choices.
4. **Files** — every file to be created or modified, with a one-line purpose each.
5. **Components & data** — each new/changed class or component: responsibility, public surface, serialized fields, and which are **Inspector tunables** (per `implementation-guidelines` §4a) with their defaults.
6. **Behaviour** — step-by-step description of what happens at runtime, including edge cases and failure modes (what happens on bad input, missing references, etc.). No silent failures.
7. **Interfaces & seams** — anything going behind an interface and why; how a future cut of this feature would be done (delete which component/prefab?).
8. **Testing** — how correctness will be verified: Edit Mode tests, the mechanical checks you will run through the Unity CLI (see *Verification*), and — separately — what Aaron needs to play to confirm feel and intent. Be explicit about which is which. Play Mode (Play Mode tests, `editor_play`) appears here only if Aaron opted in for this task; if he did, say exactly what you will do in it. If he didn't, everything runtime-only goes in the "Aaron plays it" list.
9. **Assumptions** — every engineering assumption you are making. Design assumptions are not allowed here — those are questions for step 3.
10. **Open questions** — anything that came up while planning. If non-empty, this is step 3.

## Step 3 — Post-plan questions

If writing the plan surfaced new genuine design questions, ask them now (batched, with recommendations) and stop. Update the plan file with the answers before step 4.

## Step 4 — Adversarial plan review

Launch the **`plan-reviewer`** agent via the Agent tool with `subagent_type: "plan-reviewer"` (a fresh, isolated agent — **never** a fork, so it carries none of your context or biases). Give it only:

- the path to the plan file,
- the original task text from Aaron, verbatim,
- the instruction to read `Documents/Design Doc.md`, `Documents/Claude Bible.md`, and `.claude/skills/implementation-guidelines/SKILL.md` before reviewing.

Do **not** tell it what you think is fine, what you're worried about, or how you'd like the review to come out.

When it returns:

- For each finding, decide: **accept** (fix the plan), **reject** (state why in one line), or **escalate** (it's a design question → step 5).
- Update the plan file with accepted changes and append a `## Review` section recording each finding and its disposition.
- If the review found **blocking** issues that required substantive plan changes, run a second review round on the revised plan. Stop after two rounds regardless and report what's still contested.

## Step 5 — Post-review questions

If the review surfaced genuine design questions, ask Aaron (batched, with recommendations) and stop. Record answers in the plan file.

## Step 6 — Write the code

Implement the plan as reviewed. If, while coding, you discover the plan is wrong in a way that changes design, **stop and ask** — don't improvise. If it's wrong in a purely engineering way, fix it and note the deviation in the plan's `## Deviations` section.

Run the *Verification* checks below and whatever else the plan's §8 called for.

## Step 7 — Code review

Launch the **`code-reviewer`** agent via the Agent tool with `subagent_type: "code-reviewer"` (fresh, isolated, never a fork). Give it only:

- the path to the plan file,
- the list of files created/modified (or tell it to use `git status`/`git diff`),
- the instruction to read the two docs and `implementation-guidelines` first.

Again, do not steer it.

## Step 8 — Fix

Fix every confirmed finding. Reject a finding only with a stated reason. If a finding raises a design question, ask Aaron. If fixes were more than trivial, run the code reviewer once more on the changed files. Record findings and dispositions under `## Code review` in the plan file.

## Step 9 — Report

Your final reply must include:

- what was built, file by file (brief);
- **every Inspector tunable added or changed**, with its default;
- every design question that was asked and its answer (or "none");
- every engineering assumption;
- the plan-review and code-review findings that were rejected, with reasons;
- how it was verified: compile result, tests run and their actual output, and — only if Aaron opted in — what you did in Play Mode and what you observed. Never claim a pass you didn't see;
- **what still needs Aaron to play it** — the feel/intent questions the mechanical checks cannot answer. Never present a clean CLI run as "it works"; present it as "it doesn't break, and here is what I couldn't judge".

## Verification — what the Unity CLI can and cannot tell you

The `unity` CLI plus the `com.unity.pipeline` package drive the **open** editor from the terminal (syntax and gotchas are in memory: `unity-cli-pipeline`). Use it for every step-6 verification; the old Roslyn compile approximation is the fallback only if the editor isn't running.

Aaron often runs **more than one Claude instance against the same open editor**. The editor is a shared resource: there is one Play Mode, one test runner, one compile pipeline. The rules below exist so instances don't collide.

### Play Mode is opt-in — default is never

Do **not** enter Play Mode (`editor_play`, `run_tests {"mode":"playmode"}` / `"all"`, or anything else that starts the player) unless Aaron explicitly said yes to it *for this task*. Aaron plays every change himself anyway; Play Mode from the CLI is for the rare case where a runtime-only state can't be reached any other way and he wants it probed before he sits down. The workflow is:

- Never enter Play Mode on your own judgment, however "visible or interactive" the change is. Visible/interactive behaviour is what Aaron's playtest is for.
- If you genuinely think a task needs it, ask once, batched with the workflow question at the start of the task: one sentence on *what* runtime-only thing you'd probe and *why* edit-mode checks can't reach it. Don't ask for it routinely — most tasks don't qualify, and asking every time defeats the point.
- If Aaron says yes: before `editor_play`, poll `editor_status` and confirm the editor is **not** already playing (another instance may be); if it is, wait and retry, and if it stays busy ask Aaron rather than proceeding. Then `editor_play`, poll for `playing`, `get_console_logs` (no errors/exceptions), `capture_game_view` (Read the PNG and describe what you actually see), `eval` to read the relevant state, then `editor_stop`. Never leave Play Mode running.
- If Aaron says no or you didn't ask: don't. Put the runtime-only checks in the report's "what still needs Aaron to play it" list instead. A silent Play Mode run is a rule violation even if it finds nothing.

### Standard mechanical pass, in order

1. `recompile` → poll `recompile_status` until `up_to_date`/`completed`; the `errors[]` array must be empty.
2. **Collision check** before any test run. The editor test runner closes open prefab stages, blocks on a dirty scene, and a second run started on top of a first wedges both. Probe, via `eval`/status commands: `editor_status` is not `playing`; `test_status` reports no run in progress; `recompile_status` is not compiling; `EditorSceneManager.GetActiveScene().isDirty` is false. If anything is busy, wait and re-probe (a test suite takes ~30–60 s); if it stays busy or the scene is dirty, stop and ask Aaron — never save his scene, never cancel another instance's run, never start on top of it.
3. `run_tests {"mode":"editor"}`. Report the Summary numbers and every failure verbatim, including pre-existing ones. Afterwards re-open any prefab stage the runner closed (`PrefabStageUtility.OpenPrefab`) and re-check any asset you had edited on disk (see memory `unity-cli-pipeline`).
4. For prefab/scene edits made by hand in YAML: load them through the editor (`find_assets`, `get_serialized_fields`, or `eval` with `PrefabUtility.LoadPrefabContents`) and check for missing scripts / broken references.
5. Anything else that can be read in **edit mode** through `eval` (component fields, prefab contents, a geometry query) is fair game and preferred over Play Mode.

**The limit — read this twice.** A clean pass through these steps means the change *compiles, doesn't throw, and its state looks right in the one situation you set up*. It does **not** mean the feature works as intended. You are not playing the game: you cannot feel the drag of a fold, notice that a gate opens a beat too late, see that something reads wrong at a glance, or discover the interaction that the design implies but the plan didn't spell out. Only Aaron can do that.

So:

- Never write "verified working", "works as intended", or "feature complete" on the strength of CLI checks. Say what was checked and what was observed.
- A screenshot (only ever taken in an opted-in Play Mode run) is evidence of one frame, not of behaviour over time. Describe it literally ("Scuffy is on Sheet (0,0) left of the wall; the gate is closed") rather than interpreting it as success.
- Treat a green run as the *entry ticket* to Aaron's playtest, not a substitute for it. The report's "what still needs Aaron to play it" list is mandatory even when everything passed.
- If the CLI shows something *wrong* (an exception, a failing test, a state that contradicts the plan), that is real and must be fixed or reported — negative results are reliable; positive results are partial.
