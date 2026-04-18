---
title: otx settings
parent: CLI Reference
nav_order: 6
---

# `otx settings`

Show or update persisted CLI settings. Settings are stored in `~/.opentransmute/settings.json` and apply to all future CLI commands unless overridden per-command.

## Synopsis

```
otx settings [options]
```

Running `otx settings` with no options prints the current configuration.

## Options

| Option | Description |
|---|---|
| `--orchestrator` | Default engine: `ClaudeCode` \| `OpenAI` \| `Ollama` |
| `--endpoint` | Default OpenAI-compatible base URL |
| `--thick-model` | Default model for heavy phases (Phase 3, 4) |
| `--regular-model` | Default model for normal phases |
| `--thin-model` | Default model for light phases |
| `--max-turns` | Default max agent turns per phase |
| `--max-tokens` | Default max output tokens per call (`0` = auto per phase weight) |
| `--timeout` | Default HTTP timeout in minutes |
| `--user-ethos` | Personal coding standards injected into every Compose run. Pass an empty string `""` to clear. |

## Examples

```bash
# Show current settings
otx settings

# Switch to ClaudeCode (recommended for best results)
otx settings --orchestrator ClaudeCode

# Configure OpenAI
otx settings \
  --orchestrator OpenAI \
  --thick-model gpt-4o \
  --regular-model gpt-4o-mini \
  --thin-model gpt-4o-mini

# Configure Ollama
otx settings \
  --orchestrator Ollama \
  --thick-model llama3.1:70b \
  --regular-model llama3.1:8b \
  --thin-model llama3.2:3b

# Set a custom endpoint (Azure, LM Studio, vLLM, etc.)
otx settings --endpoint https://my-resource.openai.azure.com/openai/deployments/my-model

# Tune limits
otx settings --max-turns 30 --max-tokens 0 --timeout 20

# Set a persistent user ethos
otx settings --user-ethos "All code must be idiomatic Go. Errors are wrapped with %w. \
  All exported symbols have godoc comments. No global mutable state."

# Clear the user ethos
otx settings --user-ethos ""
```

## Security Note

API keys are **never saved to disk**. Pass `--api-key` on each command, or set the `OPENAI_API_KEY` environment variable. The settings file (`~/.opentransmute/settings.json`) contains only model names, endpoints, limits, and ethos text.

## Settings File Location

`~/.opentransmute/settings.json`

This is separate from the project database (`DB/opentransmute.db`). Settings apply globally to all projects; the database is per-working-directory.
