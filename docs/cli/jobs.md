---
title: otx jobs
parent: CLI Reference
nav_order: 5
---

# `otx jobs`

List and inspect decompose and compose jobs.

## Synopsis

```
otx jobs [options]
```

## Options

| Option | Default | Description |
|---|---|---|
| `--id` | | Show full detail for a specific job ID (GUID) |
| `--type` | all | Filter by type: `decompose` or `compose` |

## Examples

```bash
# List all recent jobs
otx jobs

# List only decompose jobs
otx jobs --type decompose

# List only compose jobs
otx jobs --type compose

# Show full detail for a specific job
otx jobs --id 3f4c1c7a-0000-0000-0000-000000000000
```

## Job List Output

The default listing shows:

- Job ID
- Type (decompose / compose)
- Status (Pending / Running / Completed / Failed)
- Project or output name
- Created timestamp
- Duration (if completed)

## Job Detail Output

`--id` shows the full job record including:

- All phase statuses and completion times (decompose jobs)
- Token usage and cost (where available)
- Error message (if failed)
- Output file paths
