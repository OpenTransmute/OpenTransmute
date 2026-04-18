---
title: AI Backends
parent: Reference
nav_order: 3
---

# AI Backends
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

OpenTransmute supports three AI backends for decompose, compose, and implement jobs. All three implement the same internal interface — switching backends requires only a settings change.

---

## ClaudeCode (Recommended)

Invokes the `claude` CLI as a managed subprocess. The agent uses its own file-reading tools to explore the source directory during decomposition — no file packing or chunking required. Model weight tiers are automatically mapped to Anthropic's model families.

| Weight tier | Maps to |
|---|---|
| Thick | Claude Opus |
| Regular | Claude Sonnet |
| Thin | Claude Haiku |

### Setup

```bash
# Install the Claude Code CLI
npm install -g @anthropic-ai/claude-code

# Authenticate (follow the prompts)
claude login

# Verify
claude --version
```

### Configure as default

```bash
otx settings --orchestrator ClaudeCode
```

No API key is required — authentication is handled by the `claude` CLI itself. Model names cannot be overridden for this backend; Anthropic's tier mapping is used automatically.

---

## OpenAI-compatible

Calls any OpenAI-compatible HTTP API. You supply the base URL and API key; OpenTransmute handles the rest.

Compatible providers:

| Provider | Base URL |
|---|---|
| OpenAI | `https://api.openai.com/v1` |
| Azure AI Foundry | `https://<resource>.openai.azure.com/openai/deployments/<deployment>` |
| LM Studio (local) | `http://localhost:1234/v1` |
| vLLM | `http://localhost:8000/v1` |
| Any OpenAI-compatible proxy | Your endpoint URL |

### Setup

```bash
otx settings \
  --orchestrator OpenAI \
  --endpoint https://api.openai.com/v1 \
  --thick-model gpt-4o \
  --regular-model gpt-4o-mini \
  --thin-model gpt-4o-mini
```

Pass the API key at runtime (never saved to disk):

```bash
otx decompose https://github.com/org/repo --api-key sk-...
# or
export OPENAI_API_KEY=sk-...
otx decompose https://github.com/org/repo
```

### Azure AI Foundry

For Azure, include the deployment in the base URL and use the Azure API key:

```bash
otx settings \
  --orchestrator OpenAI \
  --endpoint "https://my-resource.openai.azure.com/openai/deployments/my-deployment"
otx decompose ./my-project --api-key <azure-api-key>
```

---

## Ollama

Calls a local Ollama instance via its OpenAI-compatible API at `http://localhost:11434`. No API key required — full privacy, data never leaves your machine.

### Setup

1. [Install Ollama](https://ollama.ai) for your platform.
2. Pull models for each weight tier:

```bash
# Example: using Llama 3.1 variants
ollama pull llama3.1:70b   # thick
ollama pull llama3.1:8b    # regular
ollama pull llama3.2:3b    # thin
```

3. Configure:

```bash
otx settings \
  --orchestrator Ollama \
  --thick-model llama3.1:70b \
  --regular-model llama3.1:8b \
  --thin-model llama3.2:3b
```

4. Confirm Ollama is running before starting a job:

```bash
ollama list
```

### Model selection notes

- Phase 3 (Component Specifications) is the most demanding phase — use the largest available model for the thick tier.
- For the thin tier (Phase 0, Phase 5, Phase 6, Phase 7), a 3B–8B model is usually sufficient.
- Context window size matters significantly for large codebases. Prefer models with at least 32k token context for the thick tier.

---

## Changing Backends Mid-project

You can change backends between phases without losing work. The decompose job state and all completed phase outputs are saved to disk. To resume with a different backend:

```bash
otx decompose https://github.com/org/repo \
  --project my-project \
  --start-phase 4 \
  --orchestrator ClaudeCode
```
