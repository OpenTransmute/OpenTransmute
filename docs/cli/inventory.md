---
title: otx inventory
parent: CLI Reference
nav_order: 4
---

# `otx inventory`

Browse and search composition inventory items extracted from decomposed projects.

## Synopsis

```
otx inventory [options]
```

## Options

| Option | Default | Description |
|---|---|---|
| `--search, -s` | | Filter by name or summary (case-insensitive contains match) |
| `--category, -c` | | Filter by category: `Algorithm`, `DesignPattern`, `Invariant`, `DataTransformation`, `DomainVocabulary`, `ArchitecturalPrimitive`, `KeyAbstraction` |
| `--project, -p` | | Filter by project name |
| `--verbose, -v` | `false` | Show the full raw markdown block for each item |

## Examples

```bash
# List all inventory items
otx inventory

# All algorithms
otx inventory --category Algorithm

# Items from a specific project
otx inventory --project my-project

# Search by name or summary
otx inventory --search "retry"

# Combined filters
otx inventory --project my-project --category DesignPattern --search "circuit"

# Show full markdown for each result
otx inventory --search "token bucket" --verbose
```

## Item Categories

| Category | What it contains |
|---|---|
| `Algorithm` | Named algorithms with pseudocode, complexity, and test cases |
| `DesignPattern` | Design patterns with where applied and problem solved |
| `Invariant` | Cross-boundary invariants and what breaks if violated |
| `DataTransformation` | Input/output shapes and transformation logic |
| `DomainVocabulary` | Domain-specific terms and their precise definitions |
| `ArchitecturalPrimitive` | Foundational building blocks everything else is built on |
| `KeyAbstraction` | Major abstractions — what they hide, what they expose |
