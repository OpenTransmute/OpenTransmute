---
title: Workflows
nav_order: 4
has_children: true
---

# Workflows

OpenTransmute is built around four linked workflows. They can be run independently or chained together.

| Workflow | Input | Output | Guide |
|---|---|---|---|
| **Decompose** | Git URL or local folder | Spec files + inventory | [Decompose](decompose) |
| **Compose** | Inventory items (from any project) | Architectural design doc | [Compose](compose) |
| **Transmute** | A whole decomposed project | Architectural design doc | [Transmute](transmute) |
| **Implement** | A compose / transmute output file | Working code | [Implement](implement) |

A typical session looks like:

```
Decompose a repo  →  browse inventory  →  Compose a new design  →  Implement it
```

Or, for a direct re-implementation:

```
Decompose a repo  →  Transmute into a new stack  →  Implement it
```
