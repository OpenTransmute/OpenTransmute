---
title: Phases
parent: Reference
nav_order: 1
---

# Decompose Phases
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

A decomposition run executes up to eight sequential phases. Each phase is defined entirely in `decompose.md` — adding new phases requires no code changes.

---

## Phase 0 — Index & Architecture Overview

| | |
|---|---|
| **Output file** | `00-index.md` |
| **Model weight** | regular |
| **Prior context** | none |

Produces a navigational index and a concise architectural summary: what the project is, what problem it solves, how the major subsystems fit together at runtime (in a single paragraph a reader can hold in their head), and 4–8 non-obvious key design decisions a re-implementer must understand — each with an explanation of what breaks if it is ignored.

---

## Phase 1 — Structural Survey

| | |
|---|---|
| **Output file** | `01-structural-survey.md` |
| **Model weight** | regular |
| **Prior context** | none |

Exhaustive directory tree (at least two levels deep with per-directory role descriptions), primary entry points, all configuration files with every key/default/type listed, external dependencies (required vs. optional, what breaks if absent), and the full build/test toolchain with every defined task.

---

## Phase 2 — Initialization & Runtime Flow

| | |
|---|---|
| **Output file** | `02-initialization-flow.md` |
| **Model weight** | regular |
| **Prior context** | phase 1 |

The complete startup sequence in numbered order — for each step: which file/function runs, what state it reads, what state it produces, and whether it can be skipped or overridden. All hook/event/callback registration points (name, when it fires, arguments, what a subscriber may do, how to register/deregister). The shutdown/cleanup sequence. All background and async tasks. Includes a Mermaid flow diagram where it aids clarity.

---

## Phase 3 — Component Specifications

| | |
|---|---|
| **Output files** | `03-01-<name>.md` … `03-N-<name>.md` |
| **Model weight** | thick |
| **Prior context** | none |
| **Phase type** | expansion |

An expansion phase that runs in two steps:

**Step 1 — Discovery.** The AI identifies all logically distinct subsystems and groups them into 3–8 thematic clusters. Output is a raw JSON array (no markdown, no prose).

**Step 2 — Spec.** For each cluster, a full specification covering:
1. Purpose (what breaks if missing)
2. Public interface (every function/method/command: signature, preconditions, postconditions, side effects, error conditions)
3. Internal state (all variables/data structures maintained across calls)
4. Algorithms (any non-trivial logic described precisely enough to reimplement)
5. Integration points (what this calls, what calls this)
6. Configuration (every user-facing option)
7. Extension interface (if applicable: discovery, lifecycle, host APIs, annotated template)
8. Edge cases and known quirks
9. Composition extracts (algorithms, patterns, invariants, key abstractions, domain vocabulary)

---

## Phase 4 — Data Formats & Protocols

| | |
|---|---|
| **Output file** | `04-data-formats.md` |
| **Model weight** | thick |
| **Prior context** | none |

Every file format the project reads or writes: full grammar or schema (BNF, JSON Schema, or equivalent), minimal and maximal valid examples, versioning scheme, backward-compatibility guarantees. Every inter-process protocol: message types, encoding, sequence diagrams, error handling and retry behavior. Every public API surface not covered in Phase 3: full signature/schema, stability guarantee, deprecation path.

---

## Phase 5 — Re-implementation Checklist

| | |
|---|---|
| **Output file** | `05-reimplementation-checklist.md` |
| **Model weight** | regular |
| **Prior context** | phases 0–4 |

An actionable ordered checklist:

1. **Dependency inventory** — for each capability needed, the stdlib module in Python, Go, Rust, TypeScript, and Java
2. **Implementation order** — components in dependency order (leaves first), with spec cross-references
3. **Acceptance criteria** — 3–5 concrete, testable behavioral assertions per component
4. **Compatibility traps** — behaviors that are easy to get subtly wrong, especially platform-specific ones
5. **What to skip** — historical accidents, deprecated code, or features not worth porting
6. **Security checklist** — every component and data flow analysed for injection, traversal, privilege escalation, unsafe defaults, missing auth, weak entropy, and more. Each finding includes severity (Low / Medium / High / Critical) and a safe re-implementation pattern.
7. **Malicious intent indicators** — flags any code patterns suggesting backdoors, data exfiltration, obfuscated logic, or unexpected outbound connections. States explicitly if none are found.

---

## Phase 6 — Composition Inventory

| | |
|---|---|
| **Output file** | `06-composition-inventory.md` → auto-imported to SQLite |
| **Model weight** | regular |
| **Prior context** | phase 3 |

Synthesises all composition extracts from Phase 3 into a single flat document. Every item in all seven categories must include:

- Full description, pseudocode, EARS behavioral requirements
- **Runnable test cases** — concrete inputs, exact expected outputs, failure mode descriptions. Anti-cheat probes (property-based cases, cross-verification cases, mutation-detection cases, structural-execution probes)
- **Boundary and adversarial cases** — empty inputs, maximum sizes, type boundaries, malformed data, concurrent calls
- **Invariant assertions** — executable post-conditions as boolean predicates
- **Security score** (1–10) and concrete safe re-implementation pattern (or explicit "No significant concerns")

After Phase 6 completes, the output file is automatically parsed and imported into the SQLite database.

---

## Phase 7 — Ethos & Style Fingerprint

| | |
|---|---|
| **Output file** | `07-ethos.md` |
| **Model weight** | regular |
| **Prior context** | phases 1, 2, 3 |

A standalone style guide derived entirely from reading the real codebase. Every claim cites a concrete example from the source — generalisations without examples are not acceptable. Covers eleven sections:

1. Naming conventions (per identifier category, with examples)
2. Error handling philosophy
3. Abstraction discipline
4. Logging and observability
5. Concurrency and async patterns
6. Configuration and magic values
7. Dependency and coupling style
8. Comment and documentation style
9. Code organisation preferences
10. Test philosophy
11. Overall character (qualitative synthesis across 2–4 paragraphs)

Where a practice diverges from widely-accepted conventions, Phase 7 appends a **Divergence note** stating the conventional approach and whether the deviation appears deliberate — so a Compose or Transmute run can choose to replicate or correct it.

---

## Phase Control

### Starting and stopping

Use `--start-phase` and `--end-phase` (CLI) or the phase controls in the web app to run a subset of phases:

```bash
# Run only phases 0–2
otx decompose https://github.com/org/repo --end-phase 2

# Resume from phase 4
otx decompose https://github.com/org/repo --project my-project --start-phase 4
```

### Adding new phases

Phases are entirely data-driven. Add a new `## Phase N — Title` section to `src/OpenTransmute.Orchestrator/Prompts/decompose.md` with:

- `**Goal:**` — what the phase produces
- `**Model Weight:**` — `thick`, `normal`, or `thin`
- `**Prior Context:**` — comma-separated phase numbers or `none`
- A fenced code block with the prompt text

No code changes are required. See [Prompts](prompts) for details.
