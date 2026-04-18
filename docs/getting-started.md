---
title: Getting Started
nav_order: 3
---

# Getting Started
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Prerequisites

| Requirement | Purpose | Minimum Version |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Runs OpenTransmute | 10.0 |
| [`claude` CLI](https://docs.anthropic.com/claude/docs/claude-code) | Default AI backend | Latest |
| Git | Decomposing remote repositories | Any |

Verify your setup:

```bash
dotnet --version   # should print 10.x.x
claude --version   # should print claude CLI version
git --version
```

The `claude` CLI is only required if you intend to use the **Claude Agent (ClaudeCode)** backend. For OpenAI or Ollama backends, Git and .NET are sufficient.

---

## Running the Web App

```bash
git clone https://github.com/JamesBreakerOfThings/OpenTransmute
cd OpenTransmute/src/OpenTransmute
dotnet run
```

Open `http://localhost:5000` in your browser. The SQLite database is created automatically at `DB/opentransmute.db` in the current directory on first run.

---

## Installing the CLI

The CLI (`otx`) is a .NET global tool that shares the same database as the web app.

```bash
# From the repo root — build and install globally
dotnet tool install --global --add-source ./src/OpenTransmute.Cli otx
```

Confirm it works:

```bash
otx --help
```

To run without installing:

```bash
dotnet run --project src/OpenTransmute.Cli -- --help
```

{: .note }
Both the web app and the CLI read and write the same `DB/opentransmute.db` in your working directory. Run them from the same directory to share data.

---

## First Decomposition

### Web app

1. Open the **Decompose** screen (home page).
2. Paste a git URL or a local folder path into the Source field.
3. Leave all other settings at their defaults.
4. Click **Run**.

The job detail page opens immediately and streams live output as each phase runs.

### CLI

```bash
otx decompose https://github.com/org/repo
```

The CLI streams phase log output to stdout in real time. When complete, output files are in `Output/Decomposition/<project>/`.

---

## Setting a Default Backend

The default backend is OpenAI. To switch to Claude (recommended) or Ollama, use `otx settings`:

```bash
# Use Claude Code as the default
otx settings --orchestrator ClaudeCode

# Use Ollama with specific models
otx settings \
  --orchestrator Ollama \
  --thick-model llama3.1:70b \
  --regular-model llama3.1:8b \
  --thin-model llama3.2:3b

# Use OpenAI
otx settings \
  --orchestrator OpenAI \
  --thick-model gpt-4o \
  --regular-model gpt-4o-mini \
  --thin-model gpt-4o-mini
```

See [AI Backends](reference/ai-backends) for full setup instructions for each backend.

---

## What Happens Next

After a decomposition completes:

- Specification files land in `Output/Decomposition/<project>/` (eight Markdown files).
- The Phase 6 inventory is automatically parsed into the SQLite database.
- You can browse the inventory in the web app's **Inventory** screen, or via `otx inventory`.
- You can run a **Compose** or **Transmute** job to synthesise new designs from the extracted knowledge.

Continue reading:

- [Decompose workflow](workflows/decompose) — detailed guide to the eight phases
- [Compose workflow](workflows/compose) — building new designs from inventory items
- [Transmute workflow](workflows/transmute) — re-implementing a whole project in a new stack
- [CLI Reference](cli/) — every `otx` command
