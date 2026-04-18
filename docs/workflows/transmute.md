---
title: Transmute
parent: Workflows
nav_order: 3
---

# Transmute
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Overview

Transmute is a direct re-implementation workflow. It takes all the phase output files from a previously-decomposed project and feeds them as a single prebuilt prompt into the Compose engine — bypassing the inventory basket entirely.

Use Transmute when you want to re-implement a whole project in a different language or framework and don't need to cherry-pick individual ideas from the inventory first.

---

## How Transmute Differs from Compose

| | Compose | Transmute |
|---|---|---|
| **Input** | Individual inventory items from any project(s) | All phase outputs (01–06) from one decomposed project |
| **Selection** | Manual basket — pick exactly what you want | Automatic — the whole spec |
| **Cross-project mixing** | Yes | No |
| **Best for** | Synthesising new designs from reusable ideas | Re-implementing an existing project in a new stack |

Both workflows run through the same Compose pipeline and produce the same kind of output file. The difference is how the prompt is assembled.

---

## Running a Transmute Job

### Web app

Transmute is accessible in two ways:

**From the Transmute screen:**
1. Navigate to **Transmute** in the sidebar.
2. Select a source project from the dropdown (any previously-decomposed project).
3. Enter a target re-implementation description — e.g. `Re-implement in Rust using Axum and Tokio, keeping the same domain model`.
4. Choose a backend and click **Run Transmute**.

**From the Projects screen:**
1. Navigate to **Projects**.
2. Click the **Transmute** button on any project card. This pre-fills the project name in the Transmute form.

### CLI

Transmute is not yet exposed as a dedicated CLI command. To achieve a similar result from the CLI, use the Compose command with `--project` to include all items from that project's inventory:

```bash
otx compose --output rust-rewrite \
  --project my-original-project \
  --description "Re-implement in Rust using Axum and Tokio" \
  --technology "Rust, Axum, Tokio, SQLx"
```

{: .note }
This CLI approach uses the inventory items (Phase 6 output) rather than the full phase spec. For a full-spec transmute with phases 01–06, use the web app.

---

## The Target Description

The target description is the primary steering input. Be specific about:

- The **language and key frameworks** — e.g. `Rust with Axum` not just `Rust`
- Any **architectural constraints** that differ from the original — e.g. `stateless, no global registry`
- What to **keep vs. discard** from the original — e.g. `preserve the retry policy and circuit breaker; replace the custom DI container with standard constructor injection`

Good example:
```
Re-implement in Go using the standard library's net/http and database/sql.
Preserve the event sourcing model and all invariants from the original.
Replace the actor-based concurrency with goroutines and channels.
The output should be deployable as a single statically-linked binary.
```

---

## User Ethos

The user ethos setting (configured in **Settings** in the web app, or via `otx settings --user-ethos` in the CLI) is applied to Transmute runs in the same way as Compose runs — injected as a top-level authoritative instruction.

---

## Output

Results are saved to `Output/Composition/<name>/compose-output.md`.

From there, you can feed the output into the [Implement](implement) pipeline to generate working code.
