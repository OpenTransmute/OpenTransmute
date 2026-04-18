---
title: otx implement
parent: CLI Reference
nav_order: 3
---

# `otx implement`

Generate working code from a compose or transmute output spec file.

## Synopsis

```
otx implement <spec> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `spec` | Path to the spec file (e.g. `Output/Composition/my-design/compose-output.md`) *(required)* |

## Options

| Option | Default | Description |
|---|---|---|
| `--output, -o` | `Output/Implementation/<project>` | Output directory for generated code |
| `--project, -p` | inferred from spec path | Project name passed to the model for correct naming |
| `--orchestrator` | from settings | `ClaudeCode` \| `OpenAI` \| `Ollama` |
| `--api-key` | `OPENAI_API_KEY` env | OpenAI API key (never saved to disk) |
| `--endpoint` | from settings | Custom OpenAI-compatible base URL |
| `--model` | thick model from settings | Model override (defaults to the most capable/thick tier) |
| `--max-turns` | `200` | Max agent turns |
| `--timeout` | `60` | HTTP timeout in minutes |

## Examples

```bash
# Basic usage — implement a compose output
otx implement Output/Composition/my-design/compose-output.md

# Explicit project name and output directory
otx implement Output/Composition/my-design/compose-output.md \
  --project my-new-system \
  --output ./src/my-new-system

# With a specific backend and model
otx implement path/to/spec.md \
  --orchestrator OpenAI \
  --api-key sk-... \
  --model gpt-4o \
  --max-turns 300 \
  --timeout 120
```

## Notes

- The output directory is **not cleared** before a run. The agent will see any files already present and can build on or correct them.
- ClaudeCode is recommended — it uses its own file tools and handles large multi-file implementations naturally.
- For complex specs, increase `--max-turns` (try 300–500) and `--timeout` (try 90–120 minutes).
