# Project Papercut

Unity 6.3 LTS top-down paper-folding puzzle game.

**Before writing, editing, or planning any code, invoke the `project-context` skill.** It has you read `Documents/Design Doc.md` and `Documents/Claude Bible.md`, which define the game's intent, code vocabulary, and which decisions are open (`[DECIDE]`), settled (`[LOCKED]`), or may be cut (`[TENTATIVE]`). Do not resolve `[DECIDE]` items by picking a default.

**Then invoke the `implementation-guidelines` skill** before writing any code. It holds the working rules for adding anything to the project — including: if anything about the design or implementation is unclear, ask before building; never make arbitrary design-affecting choices; but don't ask questions that have an obvious right answer.

**Then, for any change larger than a few lines, invoke the `coding-workflow` skill** and follow it end to end: pre-plan questions (only if genuine) → detailed plan file → post-plan questions (only if genuine) → isolated adversarial review by the `plan-reviewer` agent → post-review questions (only if genuine) → code → isolated review by the `code-reviewer` agent → fixes → report. Never make a design decision on Aaron's behalf; every feel/design value goes in the Inspector (see `implementation-guidelines` §4a).
