---
title: CLI Reference
nav_order: 5
has_children: true
---

# CLI Reference (`otx`)

The `otx` CLI gives you full access to all OpenTransmute workflows from the terminal. It shares the same SQLite database as the web app — run both from the same working directory to share data.

## Installation

```bash
# Install as a .NET global tool from the repo
dotnet tool install --global --add-source ./src/OpenTransmute.Cli otx

# Or run directly without installing
dotnet run --project src/OpenTransmute.Cli -- <command> [options]
```

## Commands

| Command | Description |
|---|---|
| [`decompose`](decompose) | Decompose a codebase into a language-agnostic specification |
| [`compose`](compose) | Compose a new system design from inventory items |
| [`implement`](implement) | Generate working code from a compose output spec file |
| [`inventory`](inventory) | Browse inventory items extracted from decomposed projects |
| [`jobs`](jobs) | List and inspect decompose and compose jobs |
| [`settings`](settings) | Show or update persisted CLI settings |

## Global Notes

- **API keys are never saved to disk.** Pass `--api-key` on each command or set the `OPENAI_API_KEY` environment variable.
- **Settings** (`~/.opentransmute/settings.json`) provide defaults for orchestrator, models, and limits. Override any setting per-command.
- **Database** is at `DB/opentransmute.db` in the current working directory. The web app uses the same path.
