---
title: Introduction
nav_order: 2
---

# Introduction
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## The Problem

Every meaningful codebase contains ideas that transcend the language it was written in. A well-designed retry policy, a clever cache invalidation strategy, an elegant domain abstraction — these are durable intellectual assets. But they are almost always locked inside a specific language, framework, and organisational context, invisible to anyone who hasn't read the source.

When teams migrate platforms, start greenfield projects, or want to learn from prior art, they face the same problem: the knowledge exists, but it isn't accessible in a form you can reason about, search, or reuse.

OpenTransmute exists to solve that.

---

## What OpenTransmute Does

OpenTransmute has three core capabilities:

### 1. Decompose

Given a git repository or local folder, OpenTransmute runs a structured, eight-phase AI analysis that produces:

- A **language-agnostic specification** — architecture overviews, component specs, data format definitions, a re-implementation checklist, and a security analysis
- A **Composition Inventory** — a flat, searchable catalogue of every notable algorithm, design pattern, invariant, data transformation, domain term, architectural primitive, and key abstraction in the codebase
- An **Ethos & Style Fingerprint** — an evidence-based style guide derived directly from reading the real code, covering naming, error handling, abstraction discipline, logging, concurrency, configuration, and more

All output is Markdown, saved to `Output/Decomposition/<project>/`. Nothing is sent to external services beyond the AI backend you configure.

### 2. Accumulate

Each decomposition feeds its inventory into a local SQLite database. As you decompose more codebases, the database becomes a cross-project library of portable ideas. You can search it, filter it by category, and browse individual items with their full structured details.

### 3. Compose

Select items from the inventory — an algorithm from one project, a pattern from another, an abstraction from a third — and the Compose pipeline synthesises a new architectural design. It validates the design for correctness, security, and performance, and produces hardened output in Markdown.

**Transmute** is a shortcut variant: instead of building a basket from the inventory, you point it at an entire decomposed project and ask it to re-implement the whole thing in a new language or framework.

The optional **Implement** step takes compose/transmute output and drives an AI agent to write working code.

---

## How OpenTransmute Thinks About Software

OpenTransmute is built on a few convictions:

**Ideas are not implementations.** A binary search tree is the same idea in Python, Go, Rust, and C. OpenTransmute separates the idea from its container so you can carry it forward.

**Specifications are executable.** The decomposition output is not a summary — it is a behavioral contract, detailed enough that a competent engineer could re-implement the original without ever seeing the source code. Phase 5 writes acceptance criteria. Phase 6 writes runnable test cases including anti-cheat probes and mutation-detection cases.

**Style is part of the spec.** Phase 7 produces an evidence-based style fingerprint. Every claim cites real code. Where a codebase deviates from good practice, the fingerprint says so explicitly — so a Compose run can choose to replicate or correct it.

**Security is not an afterthought.** Phase 5 includes a full security analysis of every component and data flow. Phase 6 attaches security scores and safe re-implementation patterns to every inventory item.

---

## What OpenTransmute Is Not

- **Not a code search tool.** It does not index tokens or find usages. It synthesises meaning.
- **Not a documentation generator.** It produces specifications for re-implementation, not API docs for consumers.
- **Not a migration tool.** It produces specs and designs; it does not automatically port code (though the Implement step can write new code from a spec).
- **Not a cloud service.** Everything runs locally. Your code and your outputs stay on your machine.

---

## Next Steps

- [Getting Started](getting-started) — install OpenTransmute and run your first decomposition
- [Decompose workflow](workflows/decompose) — understand the eight phases in depth
- [CLI Reference](cli/) — full `otx` command documentation
