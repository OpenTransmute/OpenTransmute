---
title: Decompose
parent: Workflows
nav_order: 1
---

# Decompose
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Overview

Decompose takes a git repository or local folder and runs an eight-phase AI analysis, producing a complete language-agnostic specification of the codebase plus a searchable composition inventory.

The output is a set of Markdown files saved to `Output/Decomposition/<project>/`. Nothing leaves your machine beyond the API calls to your chosen AI backend.

---

## Running a Decomposition

### Web app

1. Navigate to the **Decompose** screen (home page).
2. Enter a git URL (e.g. `https://github.com/org/repo`) or an absolute local path.
3. The project name is auto-detected from the URL or folder name — edit it if needed.
4. Choose an AI backend and click **Run**.

The **Job Detail** screen opens immediately and shows:
- A horizontal phase stepper (phases 0–7) updating live as each phase completes
- A streaming log panel with real-time AI output
- Per-phase output previews, expandable to full text
- A **Re-run from phase N** button on each row

### CLI

```bash
# Decompose a remote repository (uses default backend from otx settings)
otx decompose https://github.com/org/repo

# Decompose a local folder with an explicit project name
otx decompose ./my-project --project my-project

# Run only phases 0–2
otx decompose https://github.com/org/repo --end-phase 2

# Resume from phase 3 after a failure
otx decompose https://github.com/org/repo --project my-project --start-phase 3
```

See [CLI: decompose](../cli/decompose) for the full option reference.

---

## The Eight Phases

### Phase 0 — Index & Architecture Overview

**Output:** `00-index.md` | **Model weight:** regular

Produces a navigational index and a concise architectural summary: what the project is, what problem it solves, how the major subsystems fit together at runtime, and 4–8 key design decisions a re-implementer must understand.

### Phase 1 — Structural Survey

**Output:** `01-structural-survey.md` | **Model weight:** regular

Exhaustive directory tree (at least two levels deep), primary entry points, all configuration files with every key and default value, external dependencies, and the build/test toolchain.

### Phase 2 — Initialization & Runtime Flow

**Output:** `02-initialization-flow.md` | **Model weight:** regular | **Uses:** phase 1

Every step of the startup sequence in order — which file runs, what state it reads, what state it produces. All hook and callback registration points. The shutdown sequence. Background and async tasks. Includes a Mermaid flow diagram where it aids clarity.

### Phase 3 — Component Specifications

**Output:** `03-01-*.md` … `03-N-*.md` | **Model weight:** thick

An *expansion phase* — runs in two steps:

1. **Discovery:** The AI identifies logically distinct subsystems and groups them into 3–8 thematic clusters.
2. **Spec:** For each cluster, a full specification covering purpose, public interface (every function with inputs, outputs, preconditions, postconditions, side effects, and error conditions), internal state, algorithms, integration points, configuration, extension interfaces, edge cases, and composition extracts.

The number of output files varies by project.

### Phase 4 — Data Formats & Protocols

**Output:** `04-data-formats.md` | **Model weight:** thick

Every file format the project reads or writes (with full grammar/schema and examples), every inter-process protocol (with sequence diagrams), and every public API surface not covered in Phase 3.

### Phase 5 — Re-implementation Checklist

**Output:** `05-reimplementation-checklist.md` | **Model weight:** regular | **Uses:** phases 0–4

An actionable, ordered checklist:
- Dependency inventory (stdlib equivalents in Python, Go, Rust, TypeScript, Java)
- Implementation order (leaves first, with spec cross-references)
- Acceptance criteria (3–5 testable behavioral assertions per component)
- Compatibility traps
- What to skip (historical accidents, deprecated code)
- **Security checklist** — every component and data flow analysed for injection, traversal, privilege escalation, unsafe defaults, and more. Includes a *Malicious Intent Indicators* section.

### Phase 6 — Composition Inventory

**Output:** `06-composition-inventory.md` → parsed into SQLite | **Model weight:** regular | **Uses:** phase 3

Synthesises all composition extracts from Phase 3 into a single flat document organised into seven categories. Every item includes:
- Full description and pseudocode
- EARS behavioral requirements
- Runnable test cases (with anti-cheat probes and mutation-detection cases)
- Boundary and adversarial cases
- Invariant assertions
- Security score (1–10) and safe re-implementation pattern

After Phase 6 completes, the inventory is automatically parsed and imported into the SQLite database.

### Phase 7 — Ethos & Style Fingerprint

**Output:** `07-ethos.md` | **Model weight:** regular | **Uses:** phases 1, 2, 3

A standalone style guide derived entirely from reading the real codebase. Every claim cites a concrete example from the source. Covers eleven dimensions:

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
11. Overall character (qualitative synthesis)

Where a practice diverges from widely-accepted conventions, Phase 7 appends a **Divergence note** explaining the conventional approach and whether the deviation appears deliberate — so a Compose or Transmute run can make an informed choice about whether to replicate or correct it.

---

## Domain Hints

If you have domain knowledge the AI would not discover from reading the code alone, pass it via `--hints` (CLI) or the **Hints** field in the web app. Hints are injected verbatim into every phase prompt.

```bash
otx decompose ./trading-engine \
  --hints "Uses event sourcing with CQRS. The Saga pattern coordinates multi-step trades. \
           Idempotency keys prevent double-execution on retries."
```

Keep hints factual and concise. They are most useful for:
- Explaining domain terminology the AI might misread as generic code
- Surfacing known quirks or non-obvious constraints
- Directing attention toward specific analysis goals

---

## Resuming a Failed Run

Phase state is saved to `Output/Decomposition/<project>/job.json` after every phase completes. If a job fails mid-run:

**Web app:** go to **Job History**, open the failed job, and click **Re-run from phase N** on the failed phase row.

**CLI:**
```bash
otx decompose https://github.com/org/repo --project my-project --start-phase 3
```

Outputs from completed phases are reloaded from disk automatically — you never lose work from a phase that already finished.

---

## Output Files

| File | Contents |
|---|---|
| `00-index.md` | Architecture overview, key design decisions |
| `01-structural-survey.md` | Directory tree, entry points, config, dependencies |
| `02-initialization-flow.md` | Startup sequence, hooks, shutdown, async tasks |
| `03-NN-<name>.md` | Component specification (one per cluster) |
| `04-data-formats.md` | File formats, protocols, public API surfaces |
| `05-reimplementation-checklist.md` | Ordered checklist with acceptance criteria and security analysis |
| `06-composition-inventory.md` | Flat inventory catalogue (source for DB import) |
| `07-ethos.md` | Style fingerprint and coding standards |
| `inventory.json` | Per-project inventory export |
| `job.json` | Job state for resume-on-failure |

All files are relative to `Output/Decomposition/<project>/` in your working directory.
