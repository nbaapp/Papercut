---
name: coding-workflow
description: The mandatory plan → adversarial plan review → code → code review pipeline for any Project Papercut change larger than a few lines. Invoke after project-context and implementation-guidelines, before writing a plan or any code. Defines when to stop and ask Aaron, the plan format, and how to dispatch the isolated plan-reviewer and code-reviewer agents.
---

# Project Papercut — coding workflow

Run this **after** `project-context` and `implementation-guidelines`. It applies to any change that is more than a few lines of code, or that adds/changes a component, prefab, scene, or asset in a way that affects behaviour. Trivial edits (a typo, a one-line fix, renaming a private field) skip it.

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
8. **Testing** — how correctness will be verified (Edit Mode / Play Mode tests, manual steps in the Desk scene, CLI compile check).
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

Verify it compiles (use the CLI compile check from memory if the editor is open) and run whatever tests the plan's §8 called for.

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
- how it was verified (compile result, tests run, and their actual output — never claim a pass you didn't see).
