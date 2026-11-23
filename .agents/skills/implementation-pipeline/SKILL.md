---
name: implementation-pipeline
description: "Mandatory delivery pipeline for DanceLab code changes. The PM (main session) never writes or edits code: the PM records requirements in specs, decomposes into tasks, briefs flash implementers, then reviews through two circuits (PM deep review with a checklist, critic verification); the PM views UI screenshots itself (the PM model has vision), the designer is an optional extra look. Load before any implementation work in this repository."
---

# Implementation pipeline (mandatory)

The main session is the PM (owner's order, 2026-11-22: «ты менеджер… фиксируешь требования и декомпозицию, исполнение отдаёшь в конвейер и только контролируешь по хертбитам»). Requirements, decomposition, briefs, review and acceptance belong to the PM. Writing and changing code belongs to implementer agents. The PM does not write code, including "small" fixes: if a fix looks smaller than its brief, the brief is still written and the executor does the fix.

## Steps

1. Requirement goes to the spec first: scenarios recorded verbatim from the owner, contract, tasks (SDD, `.agents/skills/sdd`). No code without scenarios.
2. Decompose by artifacts: one task = one executor = non-overlapping files. Tasks touching the same file run strictly sequentially.
3. Brief every executor per `subagent-delegation`: exact paths, frozen input list, hard budget, checkpoint status file under `knowledge/status/reports/` updated at least every 10 minutes, "не подтвердил" is a valid result, stop after the report.
4. The implementer (flash) implements, builds, and tests. Executors never commit.
5. First circuit: the PM reviews the diff deeply and produces a numbered checklist (contract compliance, formulas, edge cases, regressions). For UI deliverables the PM views every screenshot itself with Read (the PM model has vision since GLM-5.4, verified 2026-11-23) and adds its visual findings to the checklist.
6. Second circuit: the critic (flash) verifies every checklist item mechanically against the code and independent runs. Read-only.
7. Visual acceptance belongs to the PM: the owner released the obligation to depend on executors for screenshots (2026-11-23). The designer (flash) is optional, at the PM's decision, for an extra independent look (for example a new UI mode before the owner's live run).
8. Acceptance: the PM commits accepted work by topic, updates spec Tasks, `knowledge/status/STATUS.md` and reports, then pushes.
9. Long tasks run in the background. The heartbeat cron (every 10 minutes) checks executor status files, stops drift (TaskStop), and commits checkpoints. Five consecutive quiet heartbeats with no active tasks stop the cron (CronPauseSelf).

## Autonomy completion report (owner's order, 2026-11-22)

When the PM reports an autonomous assignment as done, the chat report MUST attach screenshots of every UI change: PNG artifacts produced by executors (`--shot` runs under `data/preview/`), one per changed mode or screen. Attach them as Markdown images embedded in the chat message: Read each PNG (the host uploads it and returns a CDN URL), look at it yourself and confirm that it shows the change without visible defects, then reference that URL in `![alt](url)` — the dialog renders them with zoom, which the owner confirmed on phone remote control (2026-11-22). Local file links alone are not enough. A UI change is not reportable as complete without its embedded screenshot; if the executor produced none, dispatch a shot run before reporting.

## PM prohibitions

- Never write or edit code under `src/`, `tools/`, `tests/`. Accidental PM edits are reverted before briefing.
- Never accept work without both review circuits; for UI this includes your own viewing of the screenshots.
- Never create a parallel mechanism when an existing one can be extended.
- Never report an autonomous assignment complete without attaching UI screenshots (see above).
