---
title: Output Files
parent: Reference
nav_order: 4
---

# Output Files

All output is written relative to the **working directory** — the directory you run `otx` from, or the directory the web app was started in.

---

## Decomposition Output

`Output/Decomposition/<project>/`

| File | Phase | Description |
|---|---|---|
| `00-index.md` | 0 | Architectural overview and key design decisions |
| `01-structural-survey.md` | 1 | Directory tree, entry points, config files, dependencies, toolchain |
| `02-initialization-flow.md` | 2 | Startup sequence, hooks, shutdown, async tasks |
| `03-01-<name>.md` … `03-N-<name>.md` | 3 | Component specifications (one file per cluster) |
| `04-data-formats.md` | 4 | File formats, protocols, public API surfaces |
| `05-reimplementation-checklist.md` | 5 | Ordered checklist with acceptance criteria and security analysis |
| `06-composition-inventory.md` | 6 | Flat inventory catalogue — source for database import |
| `07-ethos.md` | 7 | Style fingerprint and coding standards guide |
| `inventory.json` | post-6 | Per-project inventory export in JSON |
| `job.json` | ongoing | Job state snapshot — used for resume-on-failure |

The number of `03-NN-*.md` files varies by project. Phases 4 and 5 always use `04-` and `05-` prefixes regardless of how many component files Phase 3 produced.

---

## Composition Output

`Output/Composition/<name>/`

| File | Description |
|---|---|
| `compose-output.md` | Full compose or transmute output — architectural design document |

---

## Implementation Output

`Output/Implementation/<project>/`

All files and directories written by the Implement pipeline. The structure is determined by the AI agent based on the spec.

---

## Database

`DB/opentransmute.db`

SQLite database containing:
- Decomposed projects (name, source URL, decomposed timestamp)
- Inventory items (all seven categories, structured fields, raw markdown)
- Phase outputs (content of each completed phase file, for web app preview)
- Compose job records
- User settings singleton

The database is created automatically on first run. It is shared between the web app and the CLI when both are run from the same working directory.

---

## CLI Settings

`~/.opentransmute/settings.json`

User-level settings file for the CLI. Contains orchestrator preference, model names, endpoint URL, turn/token limits, and user ethos text. API keys are never written here.

---

## Exporting the Inventory

**Via web app** — Inventory screen → **Export current view** → downloads filtered results as JSON.

**Via API:**

```
GET /api/inventory/export?projectId=<guid>   # one project
GET /api/inventory/export                     # all projects
```

**Export format:**

```json
{
  "project": {
    "name": "my-project",
    "source": "https://github.com/org/repo",
    "decomposedAt": "2026-04-17T10:00:00Z"
  },
  "items": [
    {
      "id": "...",
      "category": "Algorithm",
      "name": "Token Bucket Rate Limiter",
      "summary": "...",
      "details": {
        "inputs": "...",
        "outputs": "...",
        "pseudocode": "..."
      },
      "rawMarkdown": "..."
    }
  ]
}
```
