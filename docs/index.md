---
title: Home
nav_order: 1
description: OpenTransmute — decompose any codebase, accumulate its ideas, compose new systems from them.
permalink: /
---

<div style="text-align:center; padding: 2rem 0 1.5rem;">
  <img src="assets/images/logo-full.png" alt="OpenTransmute" style="max-width: 420px; width: 100%;" />
</div>

<div style="text-align:center; margin-bottom: 2.5rem;">
  <p style="font-size: 1.25rem; color: var(--body-text-color);">
    Decompose any codebase. Accumulate its ideas. Compose new systems from them.
  </p>
  <a href="Introduction" class="btn btn-primary fs-5 mb-2 mb-md-0 mr-2">Introduction</a>
  <a href="getting-started" class="btn fs-5 mb-2 mb-md-0 mr-2">Get Started</a>
  <a href="Genesis" class="btn fs-5 mb-2 mb-md-0">Origin Story</a>
</div>

---

## What is OpenTransmute?

Software is full of durable ideas — algorithms, design patterns, invariants, and abstractions — that outlive the language, framework, or organisation that first wrote them. OpenTransmute makes these ideas **explicit, searchable, and portable**.

It ships as a self-hosted **local web app** and a **CLI** (`otx`), both backed by the same SQLite database.

---

## The Workflow

<div class="feature-grid" style="display:grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 1.25rem; margin: 1.5rem 0;">

<div style="border: 1px solid var(--border-color); border-radius: 6px; padding: 1.25rem;">
<h3 style="margin-top:0;">🔬 Decompose</h3>
<p>Point OpenTransmute at a git repository or local folder. Eight AI-driven phases produce a complete language-agnostic specification and a searchable composition inventory.</p>
<a href="workflows/decompose">Learn more →</a>
</div>

<div style="border: 1px solid var(--border-color); border-radius: 6px; padding: 1.25rem;">
<h3 style="margin-top:0;">📚 Accumulate</h3>
<p>Each inventory is parsed into a local SQLite database. Over time, as you decompose more codebases, you build a cross-project library of reusable ideas.</p>
<a href="workflows/compose#the-inventory">Learn more →</a>
</div>

<div style="border: 1px solid var(--border-color); border-radius: 6px; padding: 1.25rem;">
<h3 style="margin-top:0;">🔀 Transmute</h3>
<p>Re-implement a whole decomposed project in a new language or framework directly — without going through the inventory basket.</p>
<a href="workflows/transmute">Learn more →</a>
</div>

<div style="border: 1px solid var(--border-color); border-radius: 6px; padding: 1.25rem;">
<h3 style="margin-top:0;">🎨 Compose</h3>
<p>Pick algorithms, patterns, and abstractions from across projects, add them to a basket, and synthesise new architectural designs validated for correctness and security.</p>
<a href="workflows/compose">Learn more →</a>
</div>

<div style="border: 1px solid var(--border-color); border-radius: 6px; padding: 1.25rem;">
<h3 style="margin-top:0;">⚙️ Implement</h3>
<p>Feed a compose or transmute output into the Implement pipeline. The AI writes a complete, working implementation from the spec.</p>
<a href="workflows/implement">Learn more →</a>
</div>

</div>

---

## AI Backends

OpenTransmute works with the AI backend of your choice:

| Backend | Description |
|---|---|
| **Claude Agent (CLI)** | Invokes `claude` as a subprocess. No file packing required. Auto-maps phase weights to Opus / Sonnet / Haiku. |
| **OpenAI-compatible** | Any OpenAI-compatible API — OpenAI, Azure AI Foundry, LM Studio, vLLM, and others. |
| **Ollama** | Local Ollama at `localhost:11434`. Full privacy, no API key required. |

See [AI Backends](reference/ai-backends) for setup instructions.

---

## Documentation

{: .no_toc }

| Section | Contents |
|---|---|
| [Introduction](Introduction) | What OpenTransmute is and how it thinks about software |
| [Getting Started](getting-started) | Prerequisites, installation, and first run |
| [Workflows](workflows/) | Step-by-step guides for Decompose, Compose, Transmute, and Implement |
| [CLI Reference](cli/) | Every `otx` command, option, and flag |
| [Reference](reference/) | Phases, inventory categories, output files, prompt customisation |
| [Contributing](contributing) | Adding backends, inventory categories, phases, and more |
| [Genesis](Genesis) | Why OpenTransmute was built — the experiments and the shift that made it possible |

---

## Quick Example

```bash
# Install the CLI
dotnet tool install --global --add-source ./src/OpenTransmute.Cli otx

# Decompose a repository
otx decompose https://github.com/org/repo

# Browse the inventory
otx inventory --category Algorithm

# Compose a new design from what you found
otx compose --output my-design \
  --project repo \
  --description "A resilient API gateway" \
  --technology "Go, gRPC"
```

---

<div style="text-align:center; margin-top: 2rem; font-size: 0.9rem; color: var(--body-text-color-muted);">
  OpenTransmute is open source, MIT licensed.
</div>
