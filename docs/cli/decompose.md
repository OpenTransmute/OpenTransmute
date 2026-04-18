---
title: otx decompose
parent: CLI Reference
nav_order: 1
---

# `otx decompose`

Decompose a git repository or local folder into a language-agnostic specification.

## Synopsis

```
otx decompose <source> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `source` | Git URL or local folder path *(required)* |

## Options

| Option | Default | Description |
|---|---|---|
| `--project, -p` | auto-detected | Project name used in output paths and prompts |
| `--start-phase` | `0` | Phase to start from. `0` = fresh run. Higher values resume from a checkpoint. |
| `--end-phase` | run all | Last phase to run, inclusive |
| `--orchestrator` | from settings | AI engine: `ClaudeCode` \| `OpenAI` \| `Ollama` |
| `--api-key` | `OPENAI_API_KEY` env | OpenAI API key (never saved to disk) |
| `--endpoint` | from settings | Custom OpenAI-compatible base URL (Azure, LM Studio, etc.) |
| `--thick-model` | from settings | Model for heavy phases (Phase 3, 4) |
| `--regular-model` | from settings | Model for normal phases |
| `--thin-model` | from settings | Model for light phases |
| `--max-turns` | from settings | Max agent turns per phase before forcing output |
| `--max-tokens` | from settings | Max output tokens per call. `0` = auto per phase weight |
| `--keep-clone` | `false` | Keep the cloned repository directory after completion |
| `--hints` | *(none)* | Free-text domain knowledge injected into every phase prompt |

## Examples

```bash
# Decompose a remote repository with the default backend
otx decompose https://github.com/org/repo

# Decompose a local folder, explicit project name
otx decompose ./my-project --project my-project

# Run only the first three phases (index, structure, init flow)
otx decompose https://github.com/org/repo --end-phase 2

# Resume a failed run from phase 3
otx decompose https://github.com/org/repo --project my-project --start-phase 3

# Use OpenAI with specific models
otx decompose https://github.com/org/repo \
  --orchestrator OpenAI \
  --api-key sk-... \
  --thick-model gpt-4o \
  --regular-model gpt-4o-mini \
  --thin-model gpt-4o-mini

# Use Ollama
otx decompose ./my-project \
  --orchestrator Ollama \
  --thick-model llama3.1:70b \
  --regular-model llama3.1:8b \
  --thin-model llama3.2:3b

# Inject domain hints
otx decompose ./trading-engine \
  --hints "Uses event sourcing with CQRS. Idempotency keys prevent double-execution."
```

## Output

Files are written to `Output/Decomposition/<project>/` in the current working directory. See [Output Files](../reference/output-files) for the full list.

Phase 6 automatically imports the composition inventory into the SQLite database (`DB/opentransmute.db`).

## Resume on Failure

Phase state is persisted to `Output/Decomposition/<project>/job.json` after every phase completes. To resume after a failure, pass `--start-phase N` where `N` is the first phase that did not complete successfully. Outputs from phases 0 through N−1 are reloaded from disk automatically.
