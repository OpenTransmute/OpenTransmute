---
title: otx compose
parent: CLI Reference
nav_order: 2
---

# `otx compose`

Compose a new system design from inventory items.

## Synopsis

```
otx compose --output <name> [selection options] [options]
```

## Options

| Option | Default | Description |
|---|---|---|
| `--output, -o` | *(required)* | Output name — used in file paths and the job header |
| `--items, -i` | | Comma-separated item IDs (GUID) or exact item names |
| `--category, -c` | | Include all items in this category |
| `--project, -p` | | Include all items from this project |
| `--description` | | What the target system should be and do |
| `--environment` | | Where it runs (cloud, browser, embedded, mobile, …) |
| `--technology` | | Tech stack / language / framework |
| `--orchestrator` | from settings | `ClaudeCode` \| `OpenAI` \| `Ollama` |
| `--api-key` | `OPENAI_API_KEY` env | OpenAI API key (never saved to disk) |
| `--endpoint` | from settings | Custom OpenAI-compatible base URL |
| `--model` | from settings | Model override |
| `--max-tokens` | `8192` | Max output tokens |
| `--timeout` | from settings | HTTP timeout in minutes |
| `--ethos` | from settings | Coding style / standards injected as a top-level instruction. Overrides the saved user ethos for this run. |

At least one of `--items`, `--category`, or `--project` is required.

## Examples

```bash
# Compose from specific named items
otx compose --output my-gateway \
  --items "Token Bucket Rate Limiter,Circuit Breaker Pattern" \
  --description "A resilient API gateway" \
  --technology "Go, gRPC" \
  --environment "Kubernetes"

# All algorithms from a project
otx compose --output algo-review --project my-project --category Algorithm

# Everything from a project
otx compose --output full-design --project my-project

# Mix item selection methods
otx compose --output hybrid \
  --project trading-engine \
  --category Algorithm \
  --items "00000000-0000-0000-0000-000000000001"

# Per-run ethos override
otx compose --output strict-design --items "..." \
  --ethos "All APIs documented with OpenAPI. Errors use RFC 7807 problem details."
```

## Output

Results are saved to `Output/Composition/<name>/compose-output.md`.

## User Ethos

Set a persistent ethos applied to every compose run via `otx settings --user-ethos "..."`. Override it per-run with `--ethos`. Pass an empty string to `otx settings --user-ethos ""` to clear it.
