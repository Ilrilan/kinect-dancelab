---
name: sdd
description: Spec-Driven Development process — spec lifecycle and working patterns. Mandatory 7 steps (dialogue → scenarios → contract → implementation), statuses (draft/scenarios/realisation/done), change-specs for revisions, drill-down decomposition, tree consistency. Spec location, domain structure, and additional conventions are defined in project instructions.
---

# SDD — Spec-Driven Development

Contract before code. Implementation starts only after the human has confirmed the contract.

The spec is the single source of truth about intent. Code is derived from the spec. This enables reviewing the contract instead of the code and catching contradictions between intent and implementation.

---

## Triggers

- New feature → **7-step cycle**
- Change to existing functionality → **change-spec cycle**
- Need to understand task context → **navigation order**
- Contract or code changed → **consistency check**
- Retroactive coverage of existing code → agent reads code, drafts spec, human reviews

---

## Project Context

**Before any spec work — read the project instructions** (`AGENTS.md` or equivalent).

This skill describes the process and patterns. Project-specific details are defined in the project's own instructions:

- Where specs live (single folder, co-located with code, distributed across packages — depends on the project)
- Where change-specs live
- Which domains and sections exist
- Additional required template sections

If project instructions don't provide this context — offer the user one of the standard layouts from [`references/layouts.md`](references/layouts.md) and record the choice.

---

## Mandatory Order for a New Feature

**Must not be violated.**

### Step 1 — Explore

Before writing anything, explore the problem space:

1. **Read existing specs** — system spec and the relevant domain spec(s) to understand current context and constraints
2. **Read relevant code** — understand what already exists, what would change, what the blast radius is
3. **Lay out options if multiple approaches exist** — name the tradeoffs, do not pick one silently
4. **Clarify with the user:**
   - Who and why: who is requesting, what is the business rationale
   - What success looks like: a concrete, measurable outcome
   - What is explicitly out of scope: what is consciously not being done

Exploration commits you to nothing. A misguided spec wastes far more time than a thorough explore phase. Do not proceed to step 2 until the approach is clear.

### Step 2 — Write the spec

1. Choose the template type:
   - New feature → [`references/templates/feature.md`](references/templates/feature.md)
   - Architectural decision → [`references/templates/architecture.md`](references/templates/architecture.md)
2. Determine the location using the "Where to place a spec" rule and project conventions
3. Create the file, status `draft`
4. Write scenarios (see "Writing scenarios"), status → `scenarios`

### Step 3 — Wait for scenarios confirmation

Stop. Explicitly ask the user to confirm that the scenarios are complete and the task is needed.

Do not proceed until explicit confirmation is received.

### Step 4 — Propose the contract

1. Study the codebase and existing top-level specs
2. Fill in the `## Contract` section (see "Writing the contract")
3. Status → `realisation`

### Step 5 — Wait for contract confirmation

Stop. Explicitly ask the user to confirm the contract.

Do not write code until explicit confirmation is received.

### Step 6 — Implement

- Strictly according to the confirmed contract
- Add a `## Tasks` section to the spec
- Implement task by task, marking completed items

If open questions arise during implementation:

| Question concerns        | Action                                                           |
| ------------------------ | ---------------------------------------------------------------- |
| A scenario               | Roll back to `scenarios`, re-confirm                             |
| The contract             | Clarify the contract; dependent tasks — blocked, others continue |
| An implementation detail | Resolved within the task, status unchanged                       |

### Step 7 — Verify and close

1. **Verify the implementation matches the contract:**
   - Every item promised in `## Contract` is present in code
   - Nothing outside `Out of scope` was implemented
   - Grep for the key symbols, endpoints, or types declared in the contract
   - If a discrepancy is found: either fix the code or update the contract (with human confirmation)
2. All Tasks done → delete the `## Tasks` section
3. If `## Variants` exists → move to `<spec-name>-variants.md` alongside the spec, add `> Variants:` to metadata, delete the section
4. Status → `done`
5. Commit: **spec and code in one commit**

---

## Changing Existing Functionality

1. Find the spec that describes the functionality being changed
2. Create a change-spec — location is determined by project conventions (usually alongside the original or in a mirrored structure); use template [`references/templates/change.md`](references/templates/change.md)
3. Run the full cycle `draft → scenarios → realisation → done` for the **delta** (only what changes)
4. When done: merge changes into the original, increment `Revision: N`, delete the change-spec

Spec and code are updated in one commit.

---

## Where to Place a Spec

The spec belongs to the **domain whose code changes the most**. Side effects on other domains go in the `### Integration` subsection inside Contract, not in separate specs.

Top level (not tied to a domain) — only when **two or more domains change significantly** and there is no single clear owner. In this case, child specs per domain are **mandatory** (see Drill-down).

Concrete file structure (folders, naming conventions) — in project instructions.

---

## Drill-down Specs

### When to create

**Optional:** single domain, feature is too large for one file — decompose. The parent spec describes external behaviour, child specs describe implementation details.

**Mandatory:** if the spec covers multiple domains — child specs per domain are **mandatory**. Implementation cannot begin without them.

**Threshold:** a subsystem warrants its own drill-down spec when it has a distinct, nameable contract AND describing it inside the parent's `## Contract` would exceed one screenful. If it fits in a few lines — keep it in the parent.

### Folder structure

A flat spec file (`domain.md`) becomes a folder only when a child spec is needed — not preemptively:

```
specs/
  <domain>/
    index.md      ← parent spec (the former domain.md, renamed)
    <detail-A>.md ← child spec: implementation detail or subsystem
    <detail-B>.md
```

The parent is always `index.md` inside the domain folder. The pattern applies recursively: if a child spec itself needs drill-downs, it becomes `child/index.md` with its own children — no depth limit.

### Format in the parent spec

```markdown
## Drill-down

| Spec                     | What it describes |
| ------------------------ | ----------------- |
| path/to/domain-A-spec.md | ...               |
| path/to/domain-B-spec.md | ...               |
```

A link is a pointer — "details live there" — not a summary.

**Links to not-yet-created specs** — allowed. If while writing the parent spec it is clear that child specs are needed but not yet written — record their paths in the Drill-down table immediately. This documents the planned structure and serves as a reminder that child specs must be created before implementation begins.

### Cascade rule

- Parent contract changes → check child specs
- Child spec changes → check parent

---

## Open Questions

Open questions are a mechanism for blocking an immature spec. If there are unresolved questions before scenarios can be confirmed — record them explicitly, do not move to the contract.

Placed **before** `## Scenarios`, separated by `---`.

**Format:**

```markdown
## Open Questions

- [ ] **Short label** Detailed description of the question and why it is blocking.
- [ ] **Another one** ...
```

The bold label (2–5 words) summarises the essence. The section is deleted when all questions are resolved — before transitioning to `scenarios`.

---

## Blocked Tasks

When an open question arises during the `realisation` phase — tasks that depend on it are marked as blocked, others continue.

**Format:**

```markdown
_Blocked by open question (brief question name):_

- [ ] ~~Implement X~~
- [ ] ~~Implement Y~~
```

Strikethrough text signals the task exists but cannot be done. The italic comment before the group explains the reason.

---

## Statuses and Transitions

```
draft → scenarios → realisation → done
```

| Status        | Meaning                                      |
| ------------- | -------------------------------------------- |
| `draft`       | Idea recorded, scenarios incomplete          |
| `scenarios`   | Scenarios confirmed by the human             |
| `realisation` | Contract confirmed, implementation may begin |
| `done`        | Implemented, spec matches the code           |

**No transition happens without explicit human confirmation.**

---

## Writing Scenarios

- Describe behaviour from the perspective of the user or data consumer — **not the implementation**
- Do not reference specific components, UI elements, or tabs — those are implementation details
- `Out of scope` is mandatory: explicit exclusions matter more than their absence

**Numbering format:** `**S1.**` — bold, with a period. Each scenario is a single declarative fact about system behaviour, not a description of a user action.

Correct:

```
**S1.** The system returns a paginated list of payments with amounts and dates.
```

Incorrect (references implementation, describes action rather than behaviour):

```
S1. On the "Payments" tab the user sees a table rendered via the PaymentTable component.
```

**Explicitly empty Out of scope:** if there are no exclusions — write `*(empty)*`, do not omit the section. A missing section reads as "forgotten"; `*(empty)*` reads as "intentional".

---

## Writing the Contract

- Describes the **external interface**: what the module accepts and what it returns
- Does not describe how it is implemented internally — that is not a contract, that is code
- Each level of the hierarchy knows only its own contract; details belong in drill-down

---

## Navigating Specs

### Reading order when given a task

1. System-level top spec — understand the context and which domain the task belongs to
2. Domain spec (index or root domain spec) — understand what the domain does and does not do
3. Specific feature spec — only if the task concerns one feature within the domain

Do not read all specs in sequence. Read only what is needed for the current task.

### When to go deeper (drill-down)

- Details of a specific component's contract are needed
- The task concerns one feature within a domain

### When to go up (to the parent)

- It is unclear why the current component exists — motivation is always higher up
- The change affects a contract that is visible at the parent level

---

## Consistency

### Mandatory check on every change

1. Find all specs that reference the changed one (grep by filename)
2. Verify that links and descriptions remain correct
3. Update the spec status if implementation is complete or has changed

### Signs of inconsistency — fix immediately

- A link to a spec that was deleted or renamed
- Contract description in the parent diverges from details in the child
- Status `done` but code does not match the contract
- `Out of scope` contradicts implemented functionality

### Spec is updated together with code — never separately

A commit that changes code **always** includes an update to the corresponding spec.

---

## Retroactive Application

When applying SDD to existing code:

1. Read the code for the relevant part of the system
2. Draft a spec (status `draft`)
3. Show the user for review
4. Contradictions between intent and code — bugs or dead code

---

## Templates

- New feature: [`references/templates/feature.md`](references/templates/feature.md)
- Architectural decision: [`references/templates/architecture.md`](references/templates/architecture.md)
- Change to an existing spec: [`references/templates/change.md`](references/templates/change.md)

When creating a new spec — open the appropriate template via `Read` and copy the structure.
